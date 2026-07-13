using UnityEngine;

namespace Entanglement.Gamemodes
{
    // Client -> host entries are reports; Host -> all are what the host broadcasts after.
    // Custom carries a free-form string for mode-specific events, no new message id needed.
    public enum GamemodeEventType : byte {
        // Client -> host
        ReportPlayerKilled = 0,   // a = killer id, b = 0 (victim is the sender)

        // Host -> all
        RoundStart = 10,
        RoundEnd = 11,
        PlayerKilled = 12,        // a = killer id, b = victim id
        PlayerScored = 13,        // a = scorer id, value = new score
        PlayerEliminated = 14,    // a = eliminated user id
        Custom = 15,              // message = modder-defined payload
    }

    // Read-only snapshot handed to OnStateApplied - the live GamemodeHandler dictionaries are
    // mutable and host-only-writable, this is a safe copy for a mode's own reaction logic
    public struct GamemodeState {
        public string activeModeId;
        public bool roundActive;
        public float roundTimeRemaining;
    }

    /// <summary>
    /// Base class for an Entanglement Redux gamemode. See docs/Gamemodes.md for the full guide -
    /// this summary covers just the contract:
    ///
    /// - Exactly one mode is ever "active" at a time, chosen by the host via the BoneMenu Gamemodes
    ///   category or GamemodeHandler.StartMode(Id). Only the host's copy of the mode ever runs
    ///   HostTick/OnPlayerKilled/etc - clients only see OnStateApplied/OnEventReceived, which fire
    ///   identically on every machine (including the host) once the host's authoritative state or
    ///   event broadcast round-trips back down.
    /// - Register your mode with GamemodeHandler.RegisterGamemode(new YourMode()) from your own
    ///   mod's startup (an EntanglementModule.OnModuleLoaded is a good place). It then appears in
    ///   the Gamemodes menu for every host automatically, no core code changes needed.
    /// - Never spawn/despawn things or apply damage from OnStateApplied/OnEventReceived - those
    ///   run on EVERY machine including clients, and doing so would run the action once per
    ///   player in the lobby instead of once. Side effects that should happen exactly once belong
    ///   in the host-only members (HostTick, OnPlayerKilled, OnRoundStart/End).
    /// </summary>
    public abstract class EntanglementGamemode
    {
        // Unique key, also the wire identifier - keep this stable across versions of your mode
        public abstract string Id { get; }

        // Shown in the Gamemodes BoneMenu category
        public abstract string DisplayName { get; }
        public virtual Color MenuColor => Color.white;

        // Whether players are grouped into teams (0 = no team / free-for-all is always valid)
        public virtual bool UsesTeams => false;
        public virtual int TeamCount => 2;

        // Called for each team id (0-based) once UsesTeams is true and a player's on that team,
        // used to tint their nametag for everyone else. Default is no coloring.
        public virtual Color GetTeamColor(byte team) => Color.white;

        // If true, dying removes you from the round - your rep is hidden from every other
        // player until the next round starts. Off by default; nothing changes unless a mode
        // opts in. See docs/Gamemodes.md for exactly what this does and doesn't do.
        public virtual bool EliminationMode => false;

        // Used to set GamemodeHandler.RoundTimeRemaining when a round starts, unless the host
        // has set a round duration override in BoneMenu
        public virtual float DefaultRoundSeconds => 300f;

        // --- Host-only lifecycle. Never called on a client's machine. ---
        public virtual void OnModeStart() { }
        public virtual void OnModeStop() { }
        public virtual void OnRoundStart() { }
        public virtual void OnRoundEnd() { }
        public virtual void HostTick(float deltaTime) { }
        public virtual void OnPlayerKilled(long killerId, long victimId) { }
        public virtual void OnPlayerJoined(long userId) { }
        public virtual void OnPlayerLeft(long userId) { }

        // --- Runs on every machine (host included) once state/events round-trip from the host ---
        public virtual void OnStateApplied(GamemodeState state) { }
        public virtual void OnEventReceived(GamemodeEventType type, long a, long b, int value, string message) { }

        // --- Host-facing helpers, safe to call from anywhere in your mode's own code ---
        protected void SetScore(long userId, int score) => GamemodeHandler.SetScore(userId, score);
        protected void AddScore(long userId, int delta) => GamemodeHandler.AddScore(userId, delta);
        protected void SetTeam(long userId, byte team) => GamemodeHandler.SetTeam(userId, team);
        protected void BroadcastEvent(GamemodeEventType type, long a = 0, long b = 0, int value = 0, string message = null)
            => GamemodeHandler.BroadcastEvent(type, a, b, value, message);
        protected void StartRound() => GamemodeHandler.StartRound();
        protected void EndRound() => GamemodeHandler.EndRoundInternal();
    }
}
