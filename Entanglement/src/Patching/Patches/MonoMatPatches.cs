using System;
using System.Collections;
using System.Collections.Generic;

using HarmonyLib;

using MelonLoader;

using UnityEngine;

using Entanglement.Network;
using Entanglement.Extensions;

namespace Entanglement.Patching
{
    public static class MonoMatSync
    {
        public static bool isRemote;

        public static bool consumingCopy;

        /// <summary>Only our copy of a magazine goes into the machine; the balance comes from the host.</summary>
        public static bool ConsumeCopy(Control_MonoMat machine, StressLevelZero.Props.Weapons.Magazine magazine) {
            if (!machine || !magazine)
                return false;
            MonoMatInsertPatch.isRemoteInsert = true;
            consumingCopy = true;
            try { machine.InsertMagazine(magazine); return true; }
            catch (Exception e) { EntangleLogger.Debug("[MonoMat] Could not take a magazine copy: " + e.Message); return false; }
            finally {
                MonoMatInsertPatch.isRemoteInsert = false;
                consumingCopy = false;
            }
        }

        // A loose magazine nobody is holding, right at one of the machine's slots.
        static StressLevelZero.Props.Weapons.Magazine LooseMagazineAt(Control_MonoMat machine) {
            StressLevelZero.Interaction.AmmoReciever[] slots = machine.GetComponentsInChildren<StressLevelZero.Interaction.AmmoReciever>(true);
            for (int s = 0; slots != null && s < slots.Length; s++) {
                if (!slots[s])
                    continue;
                Collider[] near = Physics.OverlapSphere(slots[s].transform.position, 0.6f);
                for (int i = 0; near != null && i < near.Length; i++) {
                    StressLevelZero.Props.Weapons.Magazine mag = near[i] ? near[i].GetComponentInParent<StressLevelZero.Props.Weapons.Magazine>() : null;
                    if (!mag || !mag.gameObject.activeInHierarchy || mag.transform.parent != null)
                        continue;
                    if (Magazine_Settings.HeldHere(mag.transform))
                        continue;
                    Entanglement.Objects.TransformSyncable sync = Entanglement.Objects.TransformSyncable.cache.Get(mag.gameObject);
                    if (sync && sync.ownerQueue != null && sync.ownerQueue.Count > 0)
                        continue;
                    return mag;
                }
            }
            return null;
        }

        /// <summary>Another player's deposit came and our copy of their magazine isn't known by id here: take whichever loose magazine sits at the slot.</summary>
        public static void ConsumeNear(Control_MonoMat machine) {
            try {
                StressLevelZero.Props.Weapons.Magazine mag = LooseMagazineAt(machine);
                if (mag && ConsumeCopy(machine, mag))
                    EntangleLogger.Debug("[MonoMat] Took the magazine sitting at " + machine.name + " instead");
            }
            catch { }
        }

        static readonly HashSet<int> watching = new HashSet<int>();

        /// <summary>
        /// A magazine we ignored at a slot because another player owns it. Their insert should arrive and
        /// take it; if it never does (it wasn't synced, was lost) the magazine floated outside the slot
        /// for good. Looked at again a few times, and taken in if it is still sitting there loose.
        /// </summary>
        public static void WatchIgnored(StressLevelZero.Interaction.AmmoReciever receiver, StressLevelZero.Interaction.InteractableHost host) {
            if (!receiver || !host)
                return;
            int key = host.GetInstanceID();
            if (!watching.Add(key))
                return;
            MelonCoroutines.Start(CoWatch(receiver, host, key));
        }

        static IEnumerator CoWatch(StressLevelZero.Interaction.AmmoReciever receiver, StressLevelZero.Interaction.InteractableHost host, int key) {
            try {
                yield return new WaitForSeconds(2.5f);
                for (int attempt = 0; attempt < 4; attempt++) {
                    if (!receiver || !host)
                        yield break;
                    StressLevelZero.Props.Weapons.Magazine mag = host.GetComponentInParent<StressLevelZero.Props.Weapons.Magazine>();
                    if (!mag || !mag.gameObject.activeInHierarchy)
                        yield break;
                    Control_MonoMat machine = receiver.GetComponentInParent<Control_MonoMat>();
                    if (!machine)
                        yield break;
                    if ((mag.transform.position - receiver.transform.position).sqrMagnitude > 0.6f * 0.6f)
                        yield break;

                    Entanglement.Objects.TransformSyncable sync = Entanglement.Objects.TransformSyncable.cache.Get(mag.gameObject);
                    bool held = Magazine_Settings.HeldHere(mag.transform) || (sync && sync.ownerQueue != null && sync.ownerQueue.Count > 0);
                    if (!held && mag.transform.parent == null) {
                        if (ConsumeCopy(machine, mag))
                            EntangleLogger.Debug("[MonoMat] " + mag.name + " was still outside " + machine.name + " with no insert coming: taking it in");
                        yield break;
                    }
                    yield return new WaitForSeconds(1.5f);
                }
            }
            finally { watching.Remove(key); }
        }

        /// <summary>Client: tell the host how many rounds just went in here.</summary>
        public static void SendDeposit(Control_MonoMat machine, int count, bool small) {
            if (count <= 0 || machine == null || Node.isServer)
                return;
            count = Math.Min(count, 0x7FFF);
            EntangleLogger.Debug("[MonoMat] Deposited " + count + (small ? " small" : " medium") + " into " + machine.name + ", telling the host");
            Entanglement.Objects.SceneEventSync.SendEvent(SceneEventType.MonoMatBullets, machine.transform,
                (ushort)((count << 1) | (small ? 1 : 0)), false);
        }

        public static void SendState(Control_MonoMat machine, bool afterChange = false) {
            if (isRemote || !SteamIntegration.hasLobby || (!Node.isServer && !afterChange) || machine == null || Node.activeNode == null)
                return;

            try {
                MonoMatStateMessageData data = new MonoMatStateMessageData {
                    objectPath = machine.transform.GetFullPath(),
                    smallBullets = machine._smallBullets,
                    mediumBullets = machine._mediumBullets,
                    opened = machine._opened,
                    unlocked = machine._unlocked,
                };

                NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MonoMatState, data);
                if (message != null)
                    Node.activeNode.BroadcastMessage(NetworkChannel.Reliable, message.GetBytes());
            }
            catch (Exception e) {
                EntangleLogger.Warn("[MonoMat] Could not publish state: " + e.Message);
            }
        }

        /// <summary>Host side: one state message per machine, for the late-join replay.</summary>
        public static int ReplayTo(long userId) {
            if (!SteamIntegration.isHost || Node.activeNode == null)
                return 0;

            int sent = 0;
            try {
                Control_MonoMat[] machines = UnityEngine.Object.FindObjectsOfType<Control_MonoMat>();
                if (machines == null)
                    return 0;

                for (int i = 0; i < machines.Length; i++) {
                    Control_MonoMat machine = machines[i];
                    if (machine == null)
                        continue;

                    try {
                        MonoMatStateMessageData data = new MonoMatStateMessageData {
                            objectPath = machine.transform.GetFullPath(),
                            smallBullets = machine._smallBullets,
                            mediumBullets = machine._mediumBullets,
                            opened = machine._opened,
                            unlocked = machine._unlocked,
                        };

                        NetworkMessage message = NetworkMessage.CreateMessage(BuiltInMessageType.MonoMatState, data);
                        if (message == null)
                            continue;

                        Node.activeNode.SendMessage(userId, NetworkChannel.Reliable, message.GetBytes());
                        sent++;
                    }
                    catch { }
                }
            }
            catch { }

            return sent;
        }

        public static void ApplyState(string objectPath, int smallBullets, int mediumBullets, bool opened, bool unlocked) {
            try {
                Transform target = objectPath.GetFromFullPath();
                if (!target)
                    return;

                Control_MonoMat machine = target.GetComponent<Control_MonoMat>();
                if (machine == null)
                    return;

                if (machine._smallBullets != smallBullets || machine._mediumBullets != mediumBullets
                    || machine._opened != opened || machine._unlocked != unlocked)
                    EntangleLogger.Debug("[MonoMat] " + machine.name + " set to the host's balance: " + smallBullets + " small, "
                        + mediumBullets + " medium" + (unlocked ? ", unlocked" : "") + (opened ? ", open" : "")
                        + " (had " + machine._smallBullets + " small, " + machine._mediumBullets + " medium)");

                isRemote = true;
                try {
                    machine._smallBullets = smallBullets;
                    machine._mediumBullets = mediumBullets;
                    machine._opened = opened;
                    machine._unlocked = unlocked;

                    machine.AddBullets(0, true);
                }
                finally { isRemote = false; }
            }
            catch (Exception e) {
                EntangleLogger.Warn("[MonoMat] Could not apply state: " + e.Message);
            }
        }
    }

    // Signature verified against AmmoReciever.cs: void OnInteractableHostEnter(InteractableHost host)
    // A magazine another player is moving is only a copy here. When it drifts into the slot the real
    // one went into, the owner's insert arrives as a MonoMatInsert event; taking the copy as well
    // counted every magazine twice.
    [HarmonyPatch(typeof(StressLevelZero.Interaction.AmmoReciever), "OnInteractableHostEnter")]
    public static class AmmoRecieverEnterPatch
    {
        public static bool Prefix(StressLevelZero.Interaction.AmmoReciever __instance, StressLevelZero.Interaction.InteractableHost host) {
            try {
                if (!SteamIntegration.hasLobby || host == null)
                    return true;

                Entanglement.Objects.TransformSyncable sync = Entanglement.Objects.TransformSyncable.cache.Get(host.gameObject);
                if (!sync && host.rb)
                    sync = Entanglement.Objects.TransformSyncable.cache.Get(host.rb.gameObject);
                if (!sync || sync.staleOwner == 0 || sync.IsOwner()
                    || sync.staleOwner == Entanglement.Representation.PlayerRepresentation.TesterRepId)
                    return true;

                EntangleLogger.Debug("[MonoMat] Ignored " + host.name + " entering an ammo slot: it's "
                    + sync.staleOwner + "'s, their insert comes over the network");
                MonoMatSync.WatchIgnored(__instance, host);
                return false;
            }
            catch (Exception e) {
                PatchGuard.Report("MonoMatPatches.AmmoRecieverEnter", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Control_MonoMat), "AddBullets")]
    public static class MonoMatAddBulletsPatch
    {
        // Another player's magazine going into our copy: nothing is added here (MonoMatSync).
        public static bool Prefix() => !MonoMatSync.consumingCopy;

        public static void Postfix(Control_MonoMat __instance) {
            if (MonoMatSync.consumingCopy)
                return;
            try { MonoMatSync.SendState(__instance); }
            catch (Exception e) { PatchGuard.Report("MonoMatPatches.AddBullets", e); }
        }
    }

    // Signature verified against Control_MonoMat.cs: void GiveChange()
    [HarmonyPatch(typeof(Control_MonoMat), "GiveChange")]
    public static class MonoMatGiveChangePatch
    {
        public static bool Prefix() {
            try {
                if (!SteamIntegration.hasLobby)
                    return true;
                return !Entanglement.Objects.SceneEventSync.applyingRemote && !MonoMatSync.isRemote;
            }
            catch (Exception e) {
                PatchGuard.Report("MonoMatPatches.GiveChange", e);
                return true;
            }
        }

        public static void Postfix(Control_MonoMat __instance) {
            try {
                if (!Entanglement.Objects.SceneEventSync.applyingRemote)
                    MonoMatSync.SendState(__instance, true);
            }
            catch (Exception e) { PatchGuard.Report("MonoMatPatches.GiveChange.Post", e); }
        }
    }
}
