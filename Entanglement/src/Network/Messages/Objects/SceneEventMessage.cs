using System;
using System.Text;

using Entanglement.Extensions;
using Entanglement.Objects;

using UnityEngine;

namespace Entanglement.Network
{
    public enum SceneEventType : byte {
        ButtonPress    = 0,
        ButtonDepress  = 1,
        KeyLock        = 2,
        KeyUnlock      = 3,
        MonoMatInsert  = 4,
        PullDevicePull = 5,
        NpcDeath       = 6,
        NpcDespawn     = 7,
        MonoMatChange  = 8,
        GarageDoorUp   = 9,
        GarageDoorDown = 10,
        GarageDoorStop = 11,
        GarageDoorUse  = 12,
        GarageDoorToggle = 13,
        // Story interact extensions (campaign elevators, doors, power, trains, …)
        PowerOnOff     = 14, // arg: 1=on 0=off — Elevator / Motor / Joint
        PowerToggle    = 15, // Powerable_Joint.ToggleVoltage
        LeverOnOff     = 16, // arg: 1=on 0=off — OnOffSwitch / Control_PowerLever CIRCUIT
        SimpleDoorOpen = 17, // arg: 1=positive dir
        SimpleDoorClose = 18,
        SimpleDoorLock = 19,
        SimpleDoorUnlock = 20,
        DoorShatter    = 21,
        DoorBreakLock  = 22,
        DoorKeyLock    = 23, // arg: 1=locking
        QuarantineOpen = 24,
        QuarantineClose = 25,
        HingeOpen      = 26,
        HingeClose     = 27,
        HingeRelease   = 28,
        GrabbyElevator = 29,
        TrainDepart    = 30,
        TrainDoorsOpen = 31,
        TrainDoorsClose = 32,
        FuseIn         = 33,
        FuseOut        = 34,
        SocketCharge   = 35, // arg: 1=enter 0=exit
        ClaimedSuccess = 36,
        GateOpen       = 37,
        TriggerSwitch  = 38, // arg: 1=enter/on 0=exit/off
        MuseumArchive  = 39, // arg: 1=unlocked
        NavMeshDoor    = 40, // arg: 1=open 0=close
        JimmySwallow   = 41,
        TowerBossNext  = 42, // GameControl_TowerBoss.NextStage
        TowerBossSteam = 43, // ElevatorSteamTurnOff
        TowerBossTeleport = 44,
        TowerBossStage = 45, // late-join: arg = stage to catch up to
        TowerDance     = 46, // GameControl_Tower.ToggleDance arg 1=on
        TurretState    = 47, // arg = EnemyTurret.TurretStates
        TurretFire     = 48, // transient, never recorded for late join
        TurretKill     = 49,
        MonoMatBullets = 50, // arg = (count << 1) | isSmall
        ObjectBreak    = 51, // ObjectDestructable / Prop_Health died; see BreakSync
        // A client walked into or out of a trigger volume; the host runs it. See TriggerRelay.
        PlayerTriggerEnter = 52,
        PlayerTriggerExit  = 53,
        LaserTriggerEnter  = 54,
        LaserTriggerExit   = 55,
        NpcBind            = 56, // arg = first id, path = "scene path|bodies"; see SceneNpcSync
        NpcFizzle          = 57,
        NpcConfig          = 58, // arg = spawn id, path = "config|x,y,z"; see NpcConfigSync
        ClawPounce         = 59, // arg = BossClawAi.PounceState, from the machine running the claw
        ClawScoop          = 60, // arg: 1=closed 0=open
        ClawBind           = 61, // arg = first body id, path = "claw path|bodies"
        ClawMental         = 62, // arg = BossClawAi.MentalState (rest / patrol / pounce / unload)
        ClawExtend         = 63, // arg = packed current and target extension
    }

        // Story beats can fire while a late joiner is still loading; queue until the scene exists.
    [Net.HandleOnLoaded]
    public class SceneEventMessageHandler : NetworkMessageHandler<SceneEventMessageData>
    {
        public override byte? MessageIndex => BuiltInMessageType.SceneEvent;

        public override NetworkMessage CreateMessage(SceneEventMessageData data)
        {
            NetworkMessage message = new NetworkMessage();

            byte[] utf8 = Encoding.UTF8.GetBytes(data.objectPath);

            message.messageData = new byte[sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + utf8.Length];

            ushort sceneTag = data.sceneTag != 0 ? data.sceneTag : SceneEventSync.CurrentSceneTag();

            int index = 0;
            message.messageData[index++] = (byte)data.eventType;
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(data.arg), ref index);
            message.messageData = message.messageData.AddBytes(BitConverter.GetBytes(sceneTag), ref index);
            message.messageData[index++] = (byte)(data.replay ? 1 : 0);
            message.messageData = message.messageData.AddBytes(utf8, ref index);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length <= 0)
                throw new IndexOutOfRangeException();

            int index = 0;
            SceneEventType eventType = (SceneEventType)message.messageData[index++];

            ushort arg = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            ushort sceneTag = BitConverter.ToUInt16(message.messageData, index);
            index += sizeof(ushort);

            bool replay = (message.messageData[index++] & 1) != 0;

            byte[] pathBytes = new byte[message.messageData.Length - index];
            for (int i = 0; i < pathBytes.Length; i++)
                pathBytes[i] = message.messageData[index++];

            string objectPath = Encoding.UTF8.GetString(pathBytes);

            if (sceneTag != 0 && sceneTag != SceneEventSync.CurrentSceneTag())
                return;

            SceneEventSync.ApplyRemoteEvent(eventType, arg, objectPath, replay, Server.instance != null);

            if (Server.instance != null) {
                byte[] msgBytes = message.GetBytes();
                Server.instance.BroadcastMessageExcept(NetworkChannel.Reliable, msgBytes, sender);
            }
        }
    }

    public class SceneEventMessageData : NetworkMessageData {
        public SceneEventType eventType;
        public ushort arg;
        public string objectPath;
        public ushort sceneTag; // 0 = the level we are in now
        public bool replay; // catching a late joiner up, not happening now
    }
}
