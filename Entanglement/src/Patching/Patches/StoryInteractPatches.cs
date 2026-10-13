using System;

using HarmonyLib;

using UnityEngine;

using StressLevelZero.Environment;
using StressLevelZero.Interaction;
using StressLevelZero.Props;
using StressLevelZero.Zones;
using StressLevelZero.Pool;

using Entanglement.Network;
using Entanglement.Objects;

namespace Entanglement.Patching
{
    public static class StoryInteractSync
    {
        public static bool isRemote;

        static readonly System.Collections.Generic.Dictionary<long, ushort> lastState = new System.Collections.Generic.Dictionary<long, ushort>();

        static bool IsState(SceneEventType type) => type == SceneEventType.LeverOnOff || type == SceneEventType.PowerOnOff;

        public static void OnSceneCleanup() {
            lastState.Clear();
            RemoteToggleGuard.OnSceneCleanup();
        }

        public static void Send(SceneEventType type, Transform transform, ushort arg = 0) {
            if (isRemote || SceneEventSync.applyingRemote || !SteamIntegration.hasLobby || !transform)
                return;

            long key = SceneEventSync.EventKey(transform.GetInstanceID(), type);
            if (IsState(type)) {
                ushort last;
                if (lastState.TryGetValue(key, out last) && last == arg)
                    return;
                lastState[key] = arg;
            }
            else if (!SceneEventSync.MarkEvent(key))
                return;

            // Remembered above, so a later real change is compared against the starting state.
            if (SceneEventSync.Settling)
                return;

            SceneEventSync.SendEvent(type, transform, arg);
        }

        static void RememberState(SceneEventType type, Transform target, ushort arg) {
            if (IsState(type) && target)
                lastState[SceneEventSync.EventKey(target.GetInstanceID(), type)] = arg;
        }

        public static bool Apply(SceneEventType type, ushort arg, Transform target) {
            if (!target)
                return false;

            // A change from someone else is the state now, so our own next report of it isn't news.
            RememberState(type, target, arg);

            isRemote = true;
            try {
                switch (type) {
                    case SceneEventType.PowerOnOff: {
                        bool on = arg != 0;
                        ElevatorPowerable elev = target.GetComponent<ElevatorPowerable>();
                        if (elev) { elev.SwitchPowerOnOff(on); return true; }
                        Powerable_Motor motor = target.GetComponent<Powerable_Motor>();
                        if (motor) { motor.SwitchPowerOnOff(on); return true; }
                        Powerable_Joint joint = target.GetComponent<Powerable_Joint>();
                        if (joint) { joint.SwitchPowerOnOff(on); return true; }
                        return false;
                    }
                    case SceneEventType.PowerToggle: {
                        Powerable_Joint joint = target.GetComponent<Powerable_Joint>();
                        if (!joint) return false;
                        joint.ToggleVoltage();
                        return true;
                    }
                    case SceneEventType.LeverOnOff: {
                        bool on = arg != 0;

                        Control_PowerLever lever = target.GetComponent<Control_PowerLever>();
                        if (lever) {
                            try {
                                if (on) lever.CIRCUIT();
                                else lever.NONCIRCUIT();
                                return true;
                            }
                            catch (Exception e) {
                                EntangleLogger.Warn("Lever CIRCUIT failed: " + e.Message);
                            }
                        }

                        OnOffSwitch sw = target.GetComponent<OnOffSwitch>();
                        if (sw) {
                            try {
                                sw.OnOffEvent(on);
                                return true;
                            }
                            catch (Exception e) {
                                EntangleLogger.Warn("OnOffSwitch.OnOffEvent failed: " + e.Message);
                            }
                        }

                        return false;
                    }
                    case SceneEventType.SimpleDoorOpen: {
                        SimpleDoorControl door = target.GetComponent<SimpleDoorControl>();
                        if (!door) return false;
                        door.Open(arg != 0);
                        return true;
                    }
                    case SceneEventType.SimpleDoorClose: {
                        SimpleDoorControl door = target.GetComponent<SimpleDoorControl>();
                        if (!door) return false;
                        door.Close();
                        return true;
                    }
                    case SceneEventType.SimpleDoorLock: {
                        SimpleDoorControl door = target.GetComponent<SimpleDoorControl>();
                        if (!door) return false;
                        door.Lock();
                        return true;
                    }
                    case SceneEventType.SimpleDoorUnlock: {
                        SimpleDoorControl door = target.GetComponent<SimpleDoorControl>();
                        if (!door) return false;
                        door.Unlock();
                        return true;
                    }
                    case SceneEventType.DoorShatter: {
                        DoorControl door = target.GetComponent<DoorControl>();
                        if (!door) return false;
                        door.SHATTERDOOR();
                        return true;
                    }
                    case SceneEventType.DoorBreakLock: {
                        DoorControl door = target.GetComponent<DoorControl>();
                        if (!door) return false;
                        door.BREAKLOCK();
                        return true;
                    }
                    case SceneEventType.DoorKeyLock: {
                        DoorControl door = target.GetComponent<DoorControl>();
                        if (!door) return false;
                        door.KEYLOCKUNLOCK(arg != 0);
                        return true;
                    }
                    case SceneEventType.QuarantineOpen: {
                        QuarantineDoor door = target.GetComponent<QuarantineDoor>();
                        if (!door) return false;
                        door.OpenDoor();
                        return true;
                    }
                    case SceneEventType.QuarantineClose: {
                        QuarantineDoor door = target.GetComponent<QuarantineDoor>();
                        if (!door) return false;
                        door.CloseDoor();
                        return true;
                    }
                    case SceneEventType.HingeOpen: {
                        TriggeredHinge hinge = target.GetComponent<TriggeredHinge>();
                        if (!hinge) return false;
                        hinge.Open();
                        return true;
                    }
                    case SceneEventType.HingeClose: {
                        TriggeredHinge hinge = target.GetComponent<TriggeredHinge>();
                        if (!hinge) return false;
                        hinge.Close();
                        return true;
                    }
                    case SceneEventType.HingeRelease: {
                        TriggeredHinge hinge = target.GetComponent<TriggeredHinge>();
                        if (!hinge) return false;
                        hinge.Release();
                        return true;
                    }
                    case SceneEventType.GrabbyElevator: {
                        GrabbyElevator elev = target.GetComponent<GrabbyElevator>();
                        if (!elev) return false;
                        elev.ToggleMove();
                        return true;
                    }
                    case SceneEventType.TrainDepart: {
                        Train1 train = target.GetComponent<Train1>();
                        if (!train) return false;
                        train.TrainDepart();
                        return true;
                    }
                    case SceneEventType.TrainDoorsOpen: {
                        TrainDoors doors = target.GetComponent<TrainDoors>();
                        if (!doors) return false;
                        doors.OpenDoors();
                        return true;
                    }
                    case SceneEventType.TrainDoorsClose: {
                        TrainDoors doors = target.GetComponent<TrainDoors>();
                        if (!doors) return false;
                        doors.CloseDoors();
                        return true;
                    }
                    case SceneEventType.FuseIn: {
                        Control_SurgeBox box = target.GetComponent<Control_SurgeBox>();
                        if (box) { box.PutFuseIn(); return true; }
                        return false;
                    }
                    case SceneEventType.FuseOut: {
                        Control_SurgeBox box = target.GetComponent<Control_SurgeBox>();
                        if (box) { box.RemovedFuse(); return true; }
                        return false;
                    }
                    case SceneEventType.SocketCharge: {
                        PowerSocket socket = target.GetComponent<PowerSocket>();
                        if (!socket) return false;
                        if (arg != 0) socket.M_Charge();
                        else socket.M_StopCharge();
                        return true;
                    }
                    case SceneEventType.ClaimedSuccess: {
                        ClaimedTrigger claim = target.GetComponent<ClaimedTrigger>();
                        if (!claim) return false;
                        claim.Trigger();
                        return true;
                    }
                    case SceneEventType.GateOpen: {
                        Gate gate = target.GetComponent<Gate>();
                        if (!gate) return false;
                        gate.StartCoroutine(gate.openGate());
                        return true;
                    }
                    case SceneEventType.TriggerSwitch: {
                        TriggerSwitch plate = target.GetComponent<TriggerSwitch>();
                        if (!plate) return false;
                        if (arg != 0) plate.CIRCUIT();
                        else plate.NONCIRCUIT();
                        return true;
                    }
                    case SceneEventType.MuseumArchive: {
                        GameControl_Museum museum = target.GetComponent<GameControl_Museum>();
                        if (!museum) return false;
                        museum.ARCHIVEDOOR(arg != 0);
                        return true;
                    }
                    case SceneEventType.NavMeshDoor: {
                        NavMesh_Door door = target.GetComponent<NavMesh_Door>();
                        if (!door) return false;
                        door.ToggleArea(arg != 0);
                        return true;
                    }
                    case SceneEventType.JimmySwallow: {
                        jimmySwallow jimmy = target.GetComponent<jimmySwallow>();
                        if (!jimmy) return false;
                        jimmy.StartCoroutine(jimmy.goSwallow());
                        return true;
                    }
                    case SceneEventType.TowerBossNext: {
                        GameControl_TowerBoss boss = target.GetComponent<GameControl_TowerBoss>();
                        if (!boss) return false;
                        boss.NextStage();
                        return true;
                    }
                    case SceneEventType.TowerBossSteam: {
                        GameControl_TowerBoss boss = target.GetComponent<GameControl_TowerBoss>();
                        if (!boss) return false;
                        boss.ElevatorSteamTurnOff();
                        return true;
                    }
                    case SceneEventType.TowerBossTeleport: {
                        GameControl_TowerBoss boss = target.GetComponent<GameControl_TowerBoss>();
                        if (!boss) return false;
                        boss.teleport();
                        return true;
                    }
                    case SceneEventType.TowerBossStage: {
                        GameControl_TowerBoss boss = target.GetComponent<GameControl_TowerBoss>();
                        if (!boss) return false;
                        int targetStage = arg;
                        int safety = 0;
                        while (boss.stage < targetStage && safety < 12) {
                            boss.NextStage();
                            safety++;
                        }
                        return true;
                    }
                    case SceneEventType.TowerDance: {
                        GameControl_Tower tower = target.GetComponent<GameControl_Tower>();
                        if (!tower) return false;
                        tower.ToggleDance(arg != 0);
                        return true;
                    }
                    default:
                        return false;
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("Story interact apply " + type + " failed: " + e.Message);
                return false;
            }
            finally {
                isRemote = false;
            }
        }
    }

    // --- Power / elevators ---

    [HarmonyPatch(typeof(ElevatorPowerable), "SwitchPowerOnOff")]
    public static class ElevatorPowerPatch {
        public static void Postfix(ElevatorPowerable __instance, bool isOn) {
            try {
                StoryInteractSync.Send(SceneEventType.PowerOnOff, __instance.transform, (ushort)(isOn ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Powerable_Motor), "SwitchPowerOnOff")]
    public static class PowerMotorPatch {
        public static void Postfix(Powerable_Motor __instance, bool isOn) {
            try {
                StoryInteractSync.Send(SceneEventType.PowerOnOff, __instance.transform, (ushort)(isOn ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Powerable_Joint), "SwitchPowerOnOff")]
    public static class PowerJointOnOffPatch {
        public static void Postfix(Powerable_Joint __instance, bool isOn) {
            try {
                StoryInteractSync.Send(SceneEventType.PowerOnOff, __instance.transform, (ushort)(isOn ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    public static class RemoteToggleGuard
    {
        const float Window = 1.5f;

        class Pending {
            public float eventAt = -100f;
            public float replayAt = -100f;
        }

        static readonly System.Collections.Generic.Dictionary<int, Pending> pending = new System.Collections.Generic.Dictionary<int, Pending>();

        public static void OnSceneCleanup() => pending.Clear();

        /// <summary>Harmony prefix for a toggle: false to skip this run of it.</summary>
        public static bool Allow(Component toggled) {
            try {
                if (!SteamIntegration.hasLobby || !toggled)
                    return true;

                bool fromEvent = StoryInteractSync.isRemote || GarageDoorSync.isRemote;
                bool fromReplay = !fromEvent && SceneEventSync.applyingRemote;
                if (!fromEvent && !fromReplay)
                    return true;

                int id = toggled.GetInstanceID();
                Pending p;
                if (!pending.TryGetValue(id, out p)) {
                    p = new Pending();
                    pending[id] = p;
                }

                float now = Time.unscaledTime;
                if (fromEvent) {
                    if (now - p.replayAt < Window) {
                        p.replayAt = -100f;
                        return false;
                    }
                    p.eventAt = now;
                }
                else {
                    if (now - p.eventAt < Window) {
                        p.eventAt = -100f;
                        return false;
                    }
                    p.replayAt = now;
                }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(Powerable_Joint), "ToggleVoltage")]
    public static class PowerJointTogglePatch {
        public static bool Prefix(Powerable_Joint __instance) => RemoteToggleGuard.Allow(__instance);

        public static void Postfix(Powerable_Joint __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.PowerToggle, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Control_PowerLever), "CIRCUIT")]
    public static class PowerLeverOnPatch {
        public static void Postfix(Control_PowerLever __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.LeverOnOff, __instance.transform, 1);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Control_PowerLever), "NONCIRCUIT")]
    public static class PowerLeverOffPatch {
        public static void Postfix(Control_PowerLever __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.LeverOnOff, __instance.transform, 0);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // --- Doors ---

    [HarmonyPatch(typeof(SimpleDoorControl), "Open")]
    public static class SimpleDoorOpenPatch {
        public static void Postfix(SimpleDoorControl __instance, bool isPositiveDirection) {
            try {
                StoryInteractSync.Send(SceneEventType.SimpleDoorOpen, __instance.transform, (ushort)(isPositiveDirection ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(SimpleDoorControl), "Close")]
    public static class SimpleDoorClosePatch {
        public static void Postfix(SimpleDoorControl __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.SimpleDoorClose, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(SimpleDoorControl), "Lock")]
    public static class SimpleDoorLockPatch {
        public static void Postfix(SimpleDoorControl __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.SimpleDoorLock, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(SimpleDoorControl), "Unlock")]
    public static class SimpleDoorUnlockPatch {
        public static void Postfix(SimpleDoorControl __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.SimpleDoorUnlock, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(DoorControl), "SHATTERDOOR")]
    public static class DoorShatterPatch {
        public static void Postfix(DoorControl __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.DoorShatter, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(DoorControl), "BREAKLOCK")]
    public static class DoorBreakLockPatch {
        public static void Postfix(DoorControl __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.DoorBreakLock, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(DoorControl), "KEYLOCKUNLOCK")]
    public static class DoorKeyLockPatch {
        public static void Postfix(DoorControl __instance, bool isLocking) {
            try {
                StoryInteractSync.Send(SceneEventType.DoorKeyLock, __instance.transform, (ushort)(isLocking ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(QuarantineDoor), "OpenDoor")]
    public static class QuarantineOpenPatch {
        public static void Postfix(QuarantineDoor __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.QuarantineOpen, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(QuarantineDoor), "CloseDoor")]
    public static class QuarantineClosePatch {
        public static void Postfix(QuarantineDoor __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.QuarantineClose, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TriggeredHinge), "Open")]
    public static class HingeOpenPatch {
        public static void Postfix(TriggeredHinge __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.HingeOpen, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TriggeredHinge), "Close")]
    public static class HingeClosePatch {
        public static void Postfix(TriggeredHinge __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.HingeClose, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TriggeredHinge), "Release")]
    public static class HingeReleasePatch {
        public static void Postfix(TriggeredHinge __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.HingeRelease, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(NavMesh_Door), "ToggleArea")]
    public static class NavMeshDoorPatch {
        public static void Postfix(NavMesh_Door __instance, bool open) {
            try {
                StoryInteractSync.Send(SceneEventType.NavMeshDoor, __instance.transform, (ushort)(open ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // --- Elevator / train ---

    [HarmonyPatch(typeof(GrabbyElevator), "ToggleMove")]
    public static class GrabbyElevatorPatch {
        public static bool Prefix(GrabbyElevator __instance) => RemoteToggleGuard.Allow(__instance);

        public static void Postfix(GrabbyElevator __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.GrabbyElevator, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Train1), "TrainDepart")]
    public static class TrainDepartPatch {
        public static void Postfix(Train1 __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TrainDepart, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TrainDoors), "OpenDoors")]
    public static class TrainDoorsOpenPatch {
        public static void Postfix(TrainDoors __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TrainDoorsOpen, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TrainDoors), "CloseDoors")]
    public static class TrainDoorsClosePatch {
        public static void Postfix(TrainDoors __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TrainDoorsClose, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // --- Power / puzzles ---

    [HarmonyPatch(typeof(Control_SurgeBox), "PutFuseIn")]
    public static class FuseInPatch {
        public static void Postfix(Control_SurgeBox __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.FuseIn, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(Control_SurgeBox), "RemovedFuse")]
    public static class FuseOutPatch {
        public static void Postfix(Control_SurgeBox __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.FuseOut, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(PowerSocket), "M_Charge")]
    public static class SocketChargePatch {
        public static void Postfix(PowerSocket __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.SocketCharge, __instance.transform, 1);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(PowerSocket), "M_StopCharge")]
    public static class SocketStopChargePatch {
        public static void Postfix(PowerSocket __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.SocketCharge, __instance.transform, 0);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(ClaimedTrigger), "Trigger")]
    public static class ClaimedTriggerPatch {
        public static void Postfix(ClaimedTrigger __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.ClaimedSuccess, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // Gate.OnTriggerEnter takes NO Collider arg in BONEWORKS - wrong postfix sig native-crashes Il2Cpp.
    [HarmonyPatch(typeof(Gate), "OnTriggerEnter")]
    public static class GateOpenPatch {
        public static void Postfix(Gate __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.GateOpen, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TriggerSwitch), "CIRCUIT")]
    public static class TriggerSwitchOnPatch {
        public static void Postfix(TriggerSwitch __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TriggerSwitch, __instance.transform, 1);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(TriggerSwitch), "NONCIRCUIT")]
    public static class TriggerSwitchOffPatch {
        public static void Postfix(TriggerSwitch __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TriggerSwitch, __instance.transform, 0);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GameControl_Museum), "ARCHIVEDOOR")]
    public static class MuseumArchivePatch {
        public static void Postfix(GameControl_Museum __instance, bool isUnlocked) {
            try {
                StoryInteractSync.Send(SceneEventType.MuseumArchive, __instance.transform, (ushort)(isUnlocked ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(jimmySwallow), "goSwallow")]
    public static class JimmySwallowPatch {
        public static void Postfix(jimmySwallow __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.JimmySwallow, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // --- Time Tower (scene_towerboss) + Tower climb dance ---

    [HarmonyPatch(typeof(GameControl_TowerBoss), "NextStage")]
    public static class TowerBossNextPatch {
        public static void Postfix(GameControl_TowerBoss __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TowerBossNext, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GameControl_TowerBoss), "ElevatorSteamTurnOff")]
    public static class TowerBossSteamPatch {
        public static void Postfix(GameControl_TowerBoss __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TowerBossSteam, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GameControl_TowerBoss), "teleport")]
    public static class TowerBossTeleportPatch {
        public static void Postfix(GameControl_TowerBoss __instance) {
            try {
                StoryInteractSync.Send(SceneEventType.TowerBossTeleport, __instance.transform);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    [HarmonyPatch(typeof(GameControl_Tower), "ToggleDance")]
    public static class TowerDancePatch {
        public static void Postfix(GameControl_Tower __instance, bool on) {
            try {
                StoryInteractSync.Send(SceneEventType.TowerDance, __instance.transform, (ushort)(on ? 1 : 0));
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }
    }

    // Kill volumes beyond reclaim bins - same ObjectDestroy path.
    [HarmonyPatch(typeof(DespawnVolume), "OnTriggerEnter")]
    public static class DespawnVolumePatch {
        public static bool Prefix(Collider other) {
            try {
                if (!other)
                    return true;
                Rigidbody rb = other.attachedRigidbody;
                Transform root = rb ? rb.transform.root : other.transform.root;
                if (!root)
                    return true;
                string name = root.name;
                if (name.StartsWith("PlayerRep.") || name.StartsWith("Ragdoll.") || name.StartsWith("ZoneProbe."))
                    return false;
                // All the game does here is put an NPC away through its zone tracker.
                return ZoneDespawnGuard.Allows(TrackerOf(other));
            }
            catch { return true; }
        }

        // The same lookup the game does.
        static StressLevelZero.Zones.ZoneTracker TrackerOf(Collider other) {
            StressLevelZero.AI.TriggerRefProxy proxy = StressLevelZero.AI.TriggerRefProxy.Cache.Get(other.gameObject);
            if (proxy == null || !proxy.root)
                return null;
            return StressLevelZero.Zones.ZoneTracker.Cache.Get(proxy.root);
        }

        public static void Postfix(DespawnVolume __instance, Collider other) {
            try {
                if (!other)
                    return;
                Rigidbody rb = other.attachedRigidbody;
                if (!rb)
                    return;
                Poolee poolee = rb.GetComponentInParent<Poolee>();
                ReclaimSync.BroadcastDespawn(poolee, rb);
            }
            catch (Exception e) { Entanglement.Patching.PatchGuard.Report("StoryInteractPatches.Postfix", e); }
        }

        public static Exception Finalizer(Exception __exception, Collider other) {
            if (__exception == null)
                return null;
            string what = "?";
            try {
                Rigidbody rb = other ? other.attachedRigidbody : null;
                Transform root = rb ? rb.transform.root : other ? other.transform.root : null;
                what = root ? root.name : "?";
                Poolee poolee = rb ? rb.GetComponentInParent<Poolee>() : null;
                if (poolee)
                    what += poolee.pool ? " (pool " + poolee.pool.name + ")" : " (a poolee with no pool)";
                else
                    what += " (not pooled)";
            }
            catch { }
            Entanglement.Patching.PatchGuard.Report("DespawnVolume.OnTriggerEnter (the game's own) on " + what, __exception);
            return null;
        }
    }
}
