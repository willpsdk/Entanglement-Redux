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

            yield return new WaitForSeconds(1f);

#if DEBUG
            EntangleLogger.Log("Died! Sending Death event to all players!");
#endif

            PlayerEventMessageData data = new PlayerEventMessageData()
            {
                type = PlayerEventType.Death,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerEvent, data);
            Node.activeNode.BroadcastMessageP2P(NetworkChannel.Reliable, message.GetBytes());

#if DEBUG
            if (PlayerRepresentation.debugRepresentation != null)
                PlayerRepresentation.debugRepresentation.CreateRagdoll();
#endif

            // Wait for us to respawn, but give up after a while - if we somehow never come back
            // alive we don't want hasDied stuck true forever, or no future death would register.
            float waited = 0f;
            while (!PlayerScripts.playerHealth.alive && waited < 15f) {
                waited += Time.deltaTime;
                yield return null;
            }

            hasDied = false;
        }
    }
}
