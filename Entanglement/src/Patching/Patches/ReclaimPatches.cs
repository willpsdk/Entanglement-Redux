using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Interaction;
using StressLevelZero.Pool;
using StressLevelZero.Props;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class ReclaimSync
    {
        static readonly Dictionary<int, float> notedAt = new Dictionary<int, float>();
        const float RepeatWindow = 5f;

        static bool Note(int key) {
            float now = Time.realtimeSinceStartup;
            float last;
            if (notedAt.TryGetValue(key, out last) && now - last < RepeatWindow)
                return false;
            if (notedAt.Count > 256) {
                List<int> old = new List<int>();
                foreach (KeyValuePair<int, float> pair in notedAt)
                    if (now - pair.Value >= RepeatWindow)
                        old.Add(pair.Key);
                for (int i = 0; i < old.Count; i++)
                    notedAt.Remove(old[i]);
            }
            notedAt[key] = now;
            return true;
        }

        public static void BroadcastDespawn(Poolee poolee, Rigidbody rb) {
            if (!SteamIntegration.hasLobby || Node.activeNode == null)
                return;
            if (!poolee && !rb)
                return;

            GameObject go = rb ? rb.gameObject : poolee.gameObject;
            if (!go)
                return;

            int key = go.GetInstanceID();
            if (!Note(key))
                return;

            TransformSyncable sync = TransformSyncable.cache.Get(go);
            if (!sync && poolee)
                sync = TransformSyncable.cache.Get(poolee.gameObject);
            if (!sync && go.transform.root)
                sync = TransformSyncable.Real(go.transform.root.GetComponentInChildren<TransformSyncable>(true));

            if (!sync || !sync.isValid || sync.objectId == 0)
                return;

            if (!sync.IsOwner() && sync.staleOwner != 0 && sync.staleOwner != SteamIntegration.currentUserId)
                return;

            EntangleLogger.Debug("[Reclaim] " + go.name + " #" + sync.objectId + " reclaimed here, despawning it for everyone");

            NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.ObjectDestroy, new ObjectDestroyMessageData() {
                objectId = sync.objectId
            });
            if (message != null)
                Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
        }
    }

    [HarmonyPatch(typeof(ReclamationBin), "CoDelayDespawn")]
    public static class ReclamationBinDespawnPatch
    {
        public static void Postfix(Poolee poolee, Rigidbody rb, bool hasHost, InteractableHost host) {
            // A throw here went up into the bin's own StartCoroutine, and the item never despawned.
            try { ReclaimSync.BroadcastDespawn(poolee, rb); }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("ReclamationBinDespawnPatch", e); }
        }
    }

    [HarmonyPatch(typeof(Fizzler), "CoDelayDespawn")]
    public static class FizzlerDespawnPatch
    {
        public static void Postfix(Poolee poolee, Rigidbody rb) {
            try { ReclaimSync.BroadcastDespawn(poolee, rb); }
            catch (System.Exception e) { Entanglement.Patching.PatchGuard.Report("FizzlerDespawnPatch", e); }
        }
    }
}
