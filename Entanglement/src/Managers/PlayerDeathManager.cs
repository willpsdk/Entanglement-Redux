using System;
using System.Collections;

using Entanglement.Network;
using Entanglement.Data;
using Entanglement.Representation;

using UnityEngine;

using MelonLoader;

namespace Entanglement.Managers
{
    public static class PlayerDeathManager
    {
        public static bool hasDied = false;
        public static event Action OnLocalPlayerDied;

        static bool killing;

        const float PlayerHitWindow = 1.5f;
        static float playerHitAt = -10f;

        /// <summary>Another player just hurt you: a melee hit, or their bullet.</summary>
        public static void NoteHitByPlayer() {
            playerHitAt = Time.realtimeSinceStartup;
        }

        // How long the death FX (slow-mo + eye close) is allowed to play before we force a respawn
        const float deathFxSeconds = 2.5f;
        const float respawnTimeoutSeconds = 12f;

        public static void Initialize()
        {
            Player_Health.add_OnPlayerDeath(new Action(DeathHook));
            // A kill cut short by the level changing never got to clear this.
            killing = false;
        }

        public static void Suicide()
        {
            if (hasDied || killing)
                return;
            if (PlayerScripts.playerHealth == null || !PlayerScripts.playerHealth.alive)
                return;

            killing = true;
            MelonCoroutines.Start(DoSuicide());
        }

        static IEnumerator DoSuicide()
        {
            Player_Health health = PlayerScripts.playerHealth;

            Player_Health.HealthMode savedMode = health.healthMode;
            bool savedInvulnerable = health.invulnerable;

            try {
                health.invulnerable = false;
                health.ToggleInstantDeathMode(true);
                health.healthMode = Player_Health.HealthMode.InsantDeath;
                health.TAKEDAMAGE(1000f);
            }
            catch (Exception e) { EntangleLogger.Warn("[Suicide] Damage failed: " + e.Message); }

            float waited = 0f;
            while (health.alive && waited < 1f) {
                waited += Time.deltaTime;
                yield return null;
            }

            if (health.alive) {
                EntangleLogger.Warn("[Suicide] Damage did not kill, calling Death directly.");
                try { health.Death(); }
                catch (Exception e) { EntangleLogger.Warn("[Suicide] Death failed: " + e.Message); }
            }

            try {
                health.ToggleInstantDeathMode(false);
                health.healthMode = savedMode;
                health.invulnerable = savedInvulnerable;
            }
            catch { }
            killing = false;
        }

        public static void CheckLethality()
        {
            if (!SteamIntegration.hasLobby || hasDied || killing)
                return;

            Player_Health health = PlayerScripts.playerHealth;
            if (health == null || !health.alive || !health.deathIsImminent)
                return;

            // An NPC's hit keeps the game's death-save.
            if (Time.realtimeSinceStartup - playerHitAt > PlayerHitWindow)
                return;

            if (health.curr_Health <= health.max_Health * 0.5f) {
                EntangleLogger.Log("[Death] Downed by another player; no death-save in PvP.");
                Suicide();
            }
        }

        public static void DeathHook()
        {
            if (hasDied)
                return;

            hasDied = true;

            MelonCoroutines.Start(OnDeathFinished());
        }

        public static IEnumerator OnDeathFinished() {
            OnLocalPlayerDied?.Invoke();

            // Your own body drops where you died, the same one everyone else sees.
            if (SteamIntegration.hasLobby)
                PlayerRepresentation.CreateLocalRagdoll();

            SendEvent(PlayerEventType.Death);

            float fxWait = 0f;
            while (fxWait < deathFxSeconds) {
                fxWait += Time.unscaledDeltaTime;
                yield return null;
            }

#if DEBUG
            if (PlayerRepresentation.debugRepresentation != null)
                PlayerRepresentation.debugRepresentation.CreateRagdoll();
#endif

            // Force a playable recovery when the level will not reload for us
            if (SteamIntegration.hasLobby) {
                ForceRespawn();
                SendEvent(PlayerEventType.Respawn);
            }

            float waited = 0f;
            while (PlayerScripts.playerHealth != null && !PlayerScripts.playerHealth.alive && waited < respawnTimeoutSeconds) {
                // Keep clearing slow-mo in case the death FX re-applies it
                if (Time.timeScale < 0.99f)
                    ClearDeathTimeScale();

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            ClearDeathTimeScale();
            hasDied = false;
            // The clock too, or it still thinks it is slowed and the next press goes too far.
            try { SlowMoSync.AfterDeath(); }
            catch (Exception e) { EntangleLogger.Warn("[Death] Could not reset the slow-mo clock: " + e.Message); }
        }

        static void SendEvent(PlayerEventType type) => PlayerEventMessageHandler.Send(type);

        static void ClearDeathTimeScale() {
            float normal = 0f;
            try { normal = SlowMoSync.NormalFixedDeltaTime(); }
            catch { }

            Time.timeScale = 1f;
            if (normal > 0.001f)
                Time.fixedDeltaTime = normal;
        }

        // Completes the death pipeline without a scene reload: restore health, clear slow-mo, live again.
        public static void ForceRespawn() {
            Player_Health health = PlayerScripts.playerHealth;
            if (health == null)
                return;

            ClearDeathTimeScale();

            try {
                // InstantDeath and other BW mods use SetFullHealth to revive without reloading
                health.SetFullHealth();
            }
            catch (Exception e) {
                EntangleLogger.Warn($"SetFullHealth failed during MP respawn: {e.Message}");
            }

            try {
                health.curr_Health = health.max_Health;
            }
            catch { }

            try {
                if (!health.alive)
                    health.alive = true;
            }
            catch (Exception e) {
                EntangleLogger.Warn($"Failed to set Player_Health.alive during MP respawn: {e.Message}");
            }

            // Death-save / imminent flags can stick and immediately re-kill after a forced revive
            try { health.deathIsImminent = false; } catch { }
            try { health.ToggleInstantDeathMode(false); } catch { }

            EntangleLogger.Log("Forced multiplayer respawn after death (level reload disabled in lobby).");
        }
    }
}
