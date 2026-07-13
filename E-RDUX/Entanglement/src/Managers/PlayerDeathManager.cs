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

        // Just hurts the local player enough to kill them. Everything after - the death event
        // going out to everyone else and the ragdoll appearing on their end - is the same path a
        // normal death already takes, we're only pulling the trigger on it early.
        public static void Suicide()
        {
            if (hasDied)
                return;
            if (PlayerScripts.playerHealth == null || !PlayerScripts.playerHealth.alive)
                return;

            PlayerScripts.playerHealth.TAKEDAMAGE(1000f);
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
