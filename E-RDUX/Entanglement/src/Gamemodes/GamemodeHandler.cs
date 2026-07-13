using System;
using System.Collections.Generic;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Managers;
using Entanglement.Representation;

namespace Entanglement.Gamemodes
{
    public static class GamemodeHandler
    {
        public static readonly Dictionary<string, EntanglementGamemode> registeredModes = new Dictionary<string, EntanglementGamemode>();
        public static EntanglementGamemode ActiveMode { get; private set; }
        public static bool RoundActive { get; private set; }
        public static float RoundTimeRemaining { get; private set; }

        // 0 means "use the active mode's own DefaultRoundSeconds", host-settable from BoneMenu
        public static float roundDurationOverrideSeconds = 0f;

        // You can't start a match on your own - there's nobody to play against. This counts the
        // host plus everyone connected. Bump it if you want to force a bigger minimum.
        public const int minPlayersToStart = 2;
        public static int PlayerCount => (Node.activeNode?.connectedUsers.Count ?? 0) + 1;

        public static readonly Dictionary<long, int> scores = new Dictionary<long, int>();
        public static readonly Dictionary<long, byte> teams = new Dictionary<long, byte>();
        public static readonly HashSet<long> eliminated = new HashSet<long>();

        static long lastAttacker;
        static float lastAttackTime = -10f;
        const float attackMemorySeconds = 8f;

        static float stateBroadcastTimer;
        const float stateBroadcastInterval = 2f;

        public static void RegisterGamemode(EntanglementGamemode mode) {
            if (mode == null || string.IsNullOrEmpty(mode.Id)) return;
            registeredModes[mode.Id] = mode;
        }

        public static void Initialize() {
            RegisterGamemode(new BuiltIn.DeathmatchGamemode());
            RegisterGamemode(new BuiltIn.TeamBattleGamemode());
            RegisterGamemode(new BuiltIn.LastManStandingGamemode());

            PlayerAttackMessageHandler.OnDamageReceived += OnLocalDamageReceived;
            PlayerDeathManager.OnLocalPlayerDied += OnLocalPlayerDied;
        }

        // The one thing the menu calls to kick off a game. Picks the mode and starts its first
        // round in one go, but only if there are enough players and nothing's already running -
        // so you can't start a match solo or stack two at once. Hands back a plain-English reason
        // when it won't, so the UI has something to show.
        public static bool TryStartMatch(string id, out string reason) {
            reason = "";
            if (!Node.isServer) { reason = "Only the host can start a gamemode."; return false; }
            if (RoundActive) { reason = "A round is already running. Force stop it first."; return false; }
            if (PlayerCount < minPlayersToStart) { reason = $"Need at least {minPlayersToStart} players to start."; return false; }
            if (!registeredModes.ContainsKey(id)) { reason = "That gamemode isn't registered."; return false; }

            StartMode(id);
            StartRound();
            return true;
        }

        public static bool StartMode(string id) {
            if (!Node.isServer) return false;
            if (!registeredModes.TryGetValue(id, out EntanglementGamemode mode)) return false;

            ActiveMode?.OnModeStop();
            scores.Clear();
            teams.Clear();
            eliminated.Clear();
            RoundActive = false;

            ActiveMode = mode;
            ActiveMode.OnModeStart();

            BroadcastState();
            EntangleLogger.Log($"[Gamemode] Started '{mode.DisplayName}'");
            return true;
        }

        // Stops whatever's active. If a round is running, the mode gets OnRoundEnd() first so it
        // can clean up (stop a capture timer, whatever) rather than being cut off mid-round.
        public static void StopMode() {
            if (!Node.isServer || ActiveMode == null) return;

            if (RoundActive) {
                RoundActive = false;
                ActiveMode.OnRoundEnd();
                BroadcastEvent(GamemodeEventType.RoundEnd);
            }

            ActiveMode.OnModeStop();
            EntangleLogger.Log($"[Gamemode] Stopped '{ActiveMode.DisplayName}'");
            ActiveMode = null;
            scores.Clear();
            teams.Clear();
            eliminated.Clear();

            BroadcastState();
        }

        public static void StartRound() {
            if (!Node.isServer || ActiveMode == null) return;

            RoundActive = true;
            RoundTimeRemaining = roundDurationOverrideSeconds > 0f ? roundDurationOverrideSeconds : ActiveMode.DefaultRoundSeconds;
            eliminated.Clear();

            ActiveMode.OnRoundStart();
            BroadcastEvent(GamemodeEventType.RoundStart);
            BroadcastState();
        }

        internal static void EndRoundInternal() {
            if (!Node.isServer || ActiveMode == null || !RoundActive) return;
            RoundActive = false;
            ActiveMode.OnRoundEnd();
            eliminated.Clear();
            BroadcastEvent(GamemodeEventType.RoundEnd);
            BroadcastState();
        }

        // Live "on the fly" adjustment of the current round's clock. Setting this while no
        // round is active just primes the value for when one starts.
        public static void SetRoundTimeRemaining(float seconds) {
            if (!Node.isServer) return;
            RoundTimeRemaining = Mathf.Max(0f, seconds);
            BroadcastState();
        }

        public static void AddRoundTime(float deltaSeconds) => SetRoundTimeRemaining(RoundTimeRemaining + deltaSeconds);

        // 0 = fall back to the active mode's own DefaultRoundSeconds next time a round starts
        public static void SetRoundDuration(float seconds) {
            if (!Node.isServer) return;
            roundDurationOverrideSeconds = Mathf.Max(0f, seconds);
        }

        public static void SetScore(long userId, int score) {
            if (!Node.isServer) return;
            scores[userId] = score;
            BroadcastEvent(GamemodeEventType.PlayerScored, userId, 0, score);
            BroadcastState();
        }

        public static void AddScore(long userId, int delta) {
            scores.TryGetValue(userId, out int current);
            SetScore(userId, current + delta);
        }

        public static bool ShouldBlockDamage(long attackerId) {
            if (ActiveMode == null || !ActiveMode.UsesTeams) return false;

            long localId = SteamIntegration.currentUserId;
            if (!teams.TryGetValue(attackerId, out byte attackerTeam)) return false;
            if (!teams.TryGetValue(localId, out byte localTeam)) return false;

            return attackerTeam == localTeam;
        }

        public static void SetTeam(long userId, byte team) {
            if (!Node.isServer) return;
            teams[userId] = team;
            BroadcastState();
        }

        public static void BroadcastEvent(GamemodeEventType type, long a = 0, long b = 0, int value = 0, string message = null) {
            if (!Node.isServer) return;

            GamemodeEventData data = new GamemodeEventData { type = type, a = a, b = b, value = value, message = message ?? "" };
            NetworkMessage netMessage = NetworkMessage.CreateMessage(BuiltInMessageType.GamemodeEvent, data);
            if (netMessage != null)
                Node.activeNode?.BroadcastMessage(NetworkChannel.Reliable, netMessage.GetBytes());

            HandleEventLocally(type, a, b, value, message ?? "");
        }

        // Runs on whatever machine an event landed on - the host handles its own here (it never
        // receives its own broadcast), clients handle it when the message arrives. Round start/end
        // pop the same on-screen notification for everyone so a match visibly starts and ends,
        // then the mode gets its callback.
        static void HandleEventLocally(GamemodeEventType type, long a, long b, int value, string message) {
            switch (type) {
                case GamemodeEventType.RoundStart:
                    EntangleNotif.GamemodeStarted(ActiveMode?.DisplayName ?? "Gamemode");
                    break;
                case GamemodeEventType.RoundEnd:
                    EntangleNotif.GamemodeEnded(ActiveMode?.DisplayName ?? "Gamemode");
                    break;
            }

            ActiveMode?.OnEventReceived(type, a, b, value, message);
        }

        public static void Tick() {
            if (!SteamIntegration.hasLobby) return;

            if (Node.isServer && ActiveMode != null) {
                ActiveMode.HostTick(Time.deltaTime);

                if (RoundActive) {
                    RoundTimeRemaining -= Time.deltaTime;
                    if (RoundTimeRemaining <= 0f)
                        EndRoundInternal();
                }

                stateBroadcastTimer += Time.deltaTime;
                if (stateBroadcastTimer >= stateBroadcastInterval) {
                    stateBroadcastTimer = 0f;
                    BroadcastState();
                }
            }
        }

        static void BroadcastState() {
            if (!Node.isServer) return;

            GamemodeStateData data = new GamemodeStateData {
                activeModeId = ActiveMode?.Id ?? "",
                roundActive = RoundActive,
                roundTimeRemaining = RoundTimeRemaining,
                scores = new Dictionary<long, int>(scores),
                teams = new Dictionary<long, byte>(teams),
                eliminated = new List<long>(eliminated),
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.GamemodeState, data);
            if (message != null)
                Node.activeNode?.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());

            ApplyState(data);
        }

        internal static void ApplyState(GamemodeStateData data) {
            if (!Node.isServer) {
                scores.Clear();
                foreach (var pair in data.scores) scores[pair.Key] = pair.Value;

                teams.Clear();
                foreach (var pair in data.teams) teams[pair.Key] = pair.Value;

                eliminated.Clear();
                foreach (long id in data.eliminated) eliminated.Add(id);

                RoundActive = data.roundActive;
                RoundTimeRemaining = data.roundTimeRemaining;

                if (string.IsNullOrEmpty(data.activeModeId))
                    ActiveMode = null;
                else if (ActiveMode == null || ActiveMode.Id != data.activeModeId) {
                    registeredModes.TryGetValue(data.activeModeId, out EntanglementGamemode mode);
                    ActiveMode = mode;
                }
            }

            ApplyVisuals();

            ActiveMode?.OnStateApplied(new GamemodeState {
                activeModeId = data.activeModeId,
                roundActive = data.roundActive,
                roundTimeRemaining = data.roundTimeRemaining,
            });
        }

        // Reflects eliminated/teams onto every rep's visibility and nametag color. Runs on
        // every machine (including the host, via BroadcastState -> ApplyState) so this is the
        // one place visuals and data are kept in sync, rather than every call site remembering to.
        static void ApplyVisuals() {
            foreach (var pair in PlayerRepresentation.representations) {
                PlayerRepresentation rep = pair.Value;
                if (rep == null) continue;

                rep.SetEliminated(eliminated.Contains(pair.Key));

                if (ActiveMode != null && ActiveMode.UsesTeams && teams.TryGetValue(pair.Key, out byte team))
                    rep.SetNameColor(ActiveMode.GetTeamColor(team));
                else
                    rep.SetNameColor(Color.white);
            }
        }

        internal static void ApplyEvent(long sender, GamemodeEventData data) {
            if (Node.isServer && data.type == GamemodeEventType.ReportPlayerKilled) {
                ProcessDeath(data.a, sender);
                return;
            }

            if (!Node.isServer)
                HandleEventLocally(data.type, data.a, data.b, data.value, data.message);
        }

        // Host-only: the single place a death actually gets processed, whether it happened on
        // the host itself or was reported in by a client. Handles scoring/attribution AND
        // elimination, so the two can never fall out of sync with each other.
        static void ProcessDeath(long killerId, long victimId) {
            if (ActiveMode == null) return;

            ActiveMode.OnPlayerKilled(killerId, victimId);
            BroadcastEvent(GamemodeEventType.PlayerKilled, killerId, victimId);

            // RoundActive is checked here too, not just at the top of the method - a mode's
            // OnPlayerKilled can end the round itself (last man standing does exactly this),
            // and if it did we don't want to eliminate the victim into a round that no longer
            // exists, only for them to stay invisible until the next one starts.
            if (RoundActive && ActiveMode.EliminationMode && eliminated.Add(victimId)) {
                BroadcastEvent(GamemodeEventType.PlayerEliminated, victimId);
                BroadcastState();
            }
        }

        static void OnLocalDamageReceived(long attacker, float damage) {
            lastAttacker = attacker;
            lastAttackTime = Time.time;
        }

        // Fires for ANY death, not just PvP - attribution (who gets credit) still only applies
        // within the attack memory window, but elimination needs to catch every death, so this
        // always reports rather than bailing out early when there's no recent attacker.
        static void OnLocalPlayerDied() {
            if (ActiveMode == null) return;

            bool attributed = Time.time - lastAttackTime <= attackMemorySeconds;
            long killerId = attributed ? lastAttacker : 0;
            long localId = SteamIntegration.currentUserId;

            if (Node.isServer) {
                ProcessDeath(killerId, localId);
                return;
            }

            GamemodeEventData report = new GamemodeEventData { type = GamemodeEventType.ReportPlayerKilled, a = killerId, b = 0, value = 0, message = "" };
            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.GamemodeEvent, report);
            if (message != null)
                Node.activeNode?.SendMessage(SteamIntegration.lobbyOwnerId, NetworkChannel.Reliable, message.GetBytes());
        }

        public static void Clear() {
            ActiveMode = null;
            RoundActive = false;
            scores.Clear();
            teams.Clear();
            eliminated.Clear();
        }
    }
}
