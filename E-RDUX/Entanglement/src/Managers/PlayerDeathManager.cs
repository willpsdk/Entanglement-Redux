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

        // Kills the local player outright. TAKEDAMAGE won't do it - Boneworks has a death-save
        // that survives the first lethal hit, so a single big hit just leaves you bloodied.
        // Death() is the game's own instant-kill and it raises OnPlayerDeath, so the rest of the
        // flow (broadcasting to everyone, ragdolls on their end) runs exactly like a normal death.
        public static void Suicide()
        {
            if (hasDied)
                return;
            if (PlayerScripts.playerHealth == null || !PlayerScripts.playerHealth.alive)
                return;

            PlayerScripts.playerHealth.Death();
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

            // Wait for us to teleport
            while (!PlayerScripts.playerHealth.alive)
                yield return null;

            hasDied = false;
        }
    }
}
