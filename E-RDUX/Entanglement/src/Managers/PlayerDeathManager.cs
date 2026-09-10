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

        // How long the death FX (slow-mo + eye close) is allowed to play before we force a respawn
        const float deathFxSeconds = 2.5f;
        const float respawnTimeoutSeconds = 12f;

        public static void Initialize()
        {
            Player_Health.add_OnPlayerDeath(new Action(DeathHook));
        }

        // Kills the local player the same way a death pit does: flip on instant-death mode so the
        // death-save doesn't soak the hit, then deal lethal damage. This goes through the normal
        // damage-to-respawn path (unlike Death(), which left you stuck dead with no respawn, which
        // then blocked all further damage and made you unkillable). Instant-death is switched back
        // off once the kill registers so single-player death-saves keep working.
        public static void Suicide()
        {
            if (hasDied)
                return;
            if (PlayerScripts.playerHealth == null || !PlayerScripts.playerHealth.alive)
                return;

            MelonCoroutines.Start(DoSuicide());
        }

        static IEnumerator DoSuicide()
        {
            Player_Health health = PlayerScripts.playerHealth;

            health.ToggleInstantDeathMode(true);
            health.TAKEDAMAGE(1000f);

            float waited = 0f;
            while (health.alive && waited < 1f) {
                waited += Time.deltaTime;
                yield return null;
            }

            health.ToggleInstantDeathMode(false);
        }

        // Boneworks has a death-save that keeps you alive on a hit that should've killed you, plus
        // health regen - great single-player, but in PvP it means you get shot down to a sliver and
        // never actually die ("didn't go past a certain health point"). While in a lobby, once the
        // game flags death as imminent (the death-save firing) and health is genuinely low, finish
        // the kill so hits are lethal. Runs every frame; guarded so it can't fire at full health or
        // loop after a respawn. Single-player (no lobby) is left completely alone.
        public static void CheckLethality()
        {
            if (!SteamIntegration.hasLobby || hasDied)
                return;

            Player_Health health = PlayerScripts.playerHealth;
            if (health == null || !health.alive)
                return;

            if (health.deathIsImminent && health.curr_Health <= health.max_Health * 0.5f)
                Suicide();
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

            // Let the slow-mo / eye-close vignette play. In lobbies, RELOADLEVEL is blocked and
            // reloadLevelOnDeath is forced off, so the game never finishes this sequence on its own.
            float fxWait = 0f;
            while (fxWait < deathFxSeconds) {
                fxWait += Time.unscaledDeltaTime;
                yield return null;
            }

#if DEBUG
            EntangleLogger.Log("Died! Sending Death event to all players!");
#endif

            if (Node.activeNode != null && SteamIntegration.hasLobby) {
                PlayerEventMessageData data = new PlayerEventMessageData()
                {
                    type = PlayerEventType.Death,
                };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerEvent, data);
                Node.activeNode.BroadcastMessageP2P(NetworkChannel.Reliable, message.GetBytes());
            }

#if DEBUG
            if (PlayerRepresentation.debugRepresentation != null)
                PlayerRepresentation.debugRepresentation.CreateRagdoll();
#endif

            // Force a playable recovery when the level will not reload for us
            if (SteamIntegration.hasLobby)
                ForceRespawn();

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
        }

        static void ClearDeathTimeScale() {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
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
