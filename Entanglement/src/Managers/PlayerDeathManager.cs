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

        public static void Initialize()
        {
            Player_Health.add_OnPlayerDeath(new Action(DeathHook));
        }

        public static void DeathHook()
        {
            if (hasDied)
                return;

            hasDied = true;

            MelonCoroutines.Start(OnDeathFinished());
        }

        public static IEnumerator OnDeathFinished()
        {
            // FIX: Broadcast the death event IMMEDIATELY so other players see the ragdoll instantly
            PlayerEventMessageData data = new PlayerEventMessageData()
            {
                type = PlayerEventType.Death,
            };

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.PlayerEvent, data);
            Node.activeNode.BroadcastMessageP2P(NetworkChannel.Reliable, message.GetBytes());

#if DEBUG
            EntangleLogger.Log("Died! Sending Death event to all players!");
            if (PlayerRepresentation.debugRepresentation != null)
                PlayerRepresentation.debugRepresentation.CreateRagdoll();
#endif

            // NOW we wait for the 1-second local reset delay
            yield return new WaitForSeconds(1f);

            // Wait for us to teleport
            while (!PlayerScripts.playerHealth.alive)
                yield return null;

            hasDied = false;
        }
    }
}