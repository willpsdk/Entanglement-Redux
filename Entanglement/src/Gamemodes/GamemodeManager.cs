using System;
using System.Collections.Generic;
using System.Reflection;

using Entanglement.Network;
using Entanglement.UI;

using UnityEngine;

namespace Entanglement.Gamemodes
{
    /// <summary>
    /// Host-driven gamemode lifecycle (Fusion-shaped). Metadata sync messages can land later;
    /// selection/start already gates combat via <see cref="CanAttack"/>.
    /// </summary>
    public static class GamemodeManager
    {
        public static readonly List<Gamemode> Gamemodes = new List<Gamemode>();
        public static Gamemode Active { get; private set; }

        public static bool HasActive => Active != null && Active.IsStarted;

        public static event Action<Gamemode> OnGamemodeChanged;
        public static event Action<Gamemode> OnGamemodeStarted;
        public static event Action<Gamemode> OnGamemodeStopped;

        // Fusion-style ready countdown (host).
        public static float StartTimer = -1f;
        public const float DefaultCountdown = 30f;

        public static void LoadFromAssembly(Assembly assembly) {
            if (assembly == null)
                return;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { return; }

            for (int i = 0; i < types.Length; i++) {
                Type t = types[i];
                if (t == null || t.IsAbstract || !typeof(Gamemode).IsAssignableFrom(t))
                    continue;
                try {
                    Register((Gamemode)Activator.CreateInstance(t));
                }
                catch (Exception e) {
                    EntangleLogger.Warn("[Gamemode] Failed to create " + t.Name + ": " + e.Message);
                }
            }
        }

        public static void Register(Gamemode mode) {
            if (mode == null)
                return;
            for (int i = 0; i < Gamemodes.Count; i++) {
                if (Gamemodes[i].Id == mode.Id)
                    return;
            }
            Gamemodes.Add(mode);
            try { mode.OnRegistered(); }
            catch (Exception e) { EntangleLogger.Warn("[Gamemode] OnRegistered " + mode.Id + ": " + e.Message); }
            EntangleLogger.Log("[Gamemode] Registered " + mode.Id);
        }

        public static Gamemode Find(string id) {
            for (int i = 0; i < Gamemodes.Count; i++) {
                if (string.Equals(Gamemodes[i].Id, id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Gamemodes[i].Title, id, StringComparison.OrdinalIgnoreCase))
                    return Gamemodes[i];
            }
            return null;
        }

        public static void Select(Gamemode mode) {
            if (!SteamIntegration.isHost && SteamIntegration.hasLobby) {
                GameNotify.Show("Only the host can select a gamemode.", 3f);
                return;
            }

            if (Active != null && !ReferenceEquals(Active, mode))
                Deselect();

            Active = mode;
            if (Active == null)
                return;

            Active.IsSelected = true;
            try { Active.OnSelected(); }
            catch { }
            GameNotify.Show("Gamemode: " + Active.Title, 3f, GameNotify.Kind.Success, "Gamemode");
            OnGamemodeChanged?.Invoke(Active);
            BroadcastState();
        }

        public static void Deselect() {
            if (Active == null)
                return;
            if (Active.IsStarted)
                Stop();
            if (Active.IsReady)
                Unready();
            try { Active.OnDeselected(); }
            catch { }
            Active.IsSelected = false;
            GameNotify.Show("Sandbox", 2f, GameNotify.Kind.Info, "Gamemode");
            Active = null;
            StartTimer = -1f;
            OnGamemodeChanged?.Invoke(null);
            BroadcastState();
        }

        public static void Ready() {
            if (Active == null || Active.IsReady)
                return;
            Active.IsReady = true;
            try { Active.OnReady(); }
            catch { }
            if (SteamIntegration.isHost || !SteamIntegration.hasLobby)
                StartTimer = DefaultCountdown;
            GameNotify.Show(Active.Title + " ready — starting soon", 2f, GameNotify.Kind.Info, "Gamemode");
            BroadcastState();
        }

        public static void Unready() {
            if (Active == null || !Active.IsReady)
                return;
            Active.IsReady = false;
            StartTimer = -1f;
            try { Active.OnUnready(); }
            catch { }
            BroadcastState();
        }

        public static void StartActive() {
            if (Active == null)
                return;
            if (!SteamIntegration.isHost && SteamIntegration.hasLobby) {
                GameNotify.Show("Only the host can start a gamemode.", 3f);
                return;
            }
            if (!Active.IsSelected)
                Active.IsSelected = true;
            Active.IsReady = true;
            Active.IsStarted = true;
            StartTimer = -1f;
            try { Active.OnStarted(); }
            catch { }
            try { Active.OnLevelReady(); }
            catch { }
            GameNotify.Show(Active.Title + " started!", 4f, GameNotify.Kind.Success, "Gamemode");
            OnGamemodeStarted?.Invoke(Active);
            BroadcastState();
        }

        public static void Stop() {
            if (Active == null || !Active.IsStarted)
                return;
            Active.IsStarted = false;
            try { Active.OnStopped(); }
            catch { }
            GameNotify.Show(Active.Title + " ended", 3f, GameNotify.Kind.Info, "Gamemode");
            OnGamemodeStopped?.Invoke(Active);
            BroadcastState();
        }

        public static void Tick() {
            if (Active == null)
                return;
            try { Active.OnUpdate(); }
            catch { }

            if (Active.IsSelected && !Active.IsStarted && !Active.ManualReady && Active.CheckReadyConditions() && !Active.IsReady)
                Ready();

            if ((SteamIntegration.isHost || !SteamIntegration.hasLobby) && Active.IsReady && !Active.IsStarted && StartTimer >= 0f) {
                StartTimer -= Time.unscaledDeltaTime;
                if (StartTimer <= 0f)
                    StartActive();
                else if (StartTimer <= 5f && Mathf.FloorToInt(StartTimer + Time.unscaledDeltaTime) != Mathf.FloorToInt(StartTimer))
                    GameNotify.Show(Mathf.CeilToInt(StartTimer).ToString(), 1f, GameNotify.Kind.Warning, "Gamemode");
            }
        }

        public static void BroadcastState() {
            if (!SteamIntegration.isHost || !SteamIntegration.hasLobby || Node.activeNode == null)
                return;
            var data = new GamemodeStateMessageData {
                gamemodeId = Active != null ? Active.Id : "",
                selected = Active != null && Active.IsSelected,
                ready = Active != null && Active.IsReady,
                started = Active != null && Active.IsStarted,
            };
            NetworkMessage msg = NetworkMessage.CreateMessage(BuiltInMessageType.GamemodeState, data);
            if (msg != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, msg.GetBytes());
        }

        /// <summary>Client apply from host GamemodeState message.</summary>
        public static void ApplyRemoteState(string id, bool selected, bool ready, bool started) {
            if (string.IsNullOrEmpty(id) || !selected) {
                if (Active != null) {
                    if (Active.IsStarted) {
                        Active.IsStarted = false;
                        try { Active.OnStopped(); } catch { }
                    }
                    Active.IsReady = false;
                    Active.IsSelected = false;
                    try { Active.OnDeselected(); } catch { }
                    Active = null;
                    OnGamemodeChanged?.Invoke(null);
                }
                return;
            }

            Gamemode mode = Find(id);
            if (mode == null)
                return;

            if (!ReferenceEquals(Active, mode)) {
                if (Active != null)
                    DeselectLocalOnly();
                Active = mode;
                Active.IsSelected = true;
                try { Active.OnSelected(); } catch { }
                OnGamemodeChanged?.Invoke(Active);
                GameNotify.Show("Gamemode: " + Active.Title, 3f, GameNotify.Kind.Success, "Gamemode");
            }

            if (ready && !Active.IsReady) {
                Active.IsReady = true;
                try { Active.OnReady(); } catch { }
            }
            else if (!ready && Active.IsReady) {
                Active.IsReady = false;
                try { Active.OnUnready(); } catch { }
            }

            if (started && !Active.IsStarted) {
                Active.IsStarted = true;
                try { Active.OnStarted(); } catch { }
                try { Active.OnLevelReady(); } catch { }
                GameNotify.Show(Active.Title + " started!", 4f, GameNotify.Kind.Success, "Gamemode");
                OnGamemodeStarted?.Invoke(Active);
            }
            else if (!started && Active.IsStarted) {
                Active.IsStarted = false;
                try { Active.OnStopped(); } catch { }
                GameNotify.Show(Active.Title + " ended", 3f, GameNotify.Kind.Info, "Gamemode");
                OnGamemodeStopped?.Invoke(Active);
            }
        }

        static void DeselectLocalOnly() {
            if (Active == null)
                return;
            if (Active.IsStarted) {
                Active.IsStarted = false;
                try { Active.OnStopped(); } catch { }
            }
            Active.IsReady = false;
            Active.IsSelected = false;
            try { Active.OnDeselected(); } catch { }
            Active = null;
        }

        public static void OnSceneLoad() {
            if (Active != null && Active.IsStarted && Active.AutoStopOnSceneLoad)
                Stop();
        }

        /// <summary>Combat gate used by melee/PvP senders.</summary>
        public static bool CanAttack(long attackerId, long victimId) {
            if (Active == null || !Active.IsStarted)
                return true;
            try { return Active.CanAttack(attackerId, victimId); }
            catch { return true; }
        }
    }
}
