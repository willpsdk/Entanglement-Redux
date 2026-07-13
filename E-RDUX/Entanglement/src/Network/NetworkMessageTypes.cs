    namespace Entanglement.Network
{
    // This used to be an enum but C# doesn't like casting to explicit enum types so this is a class for now
    public class BuiltInMessageType
    {
        public static byte
            Unknown = 0,
            PlayerRepSync = 1,
            GunShot = 2,
            SpawnObject = 3,
            LevelChange = 4,
            PlayerAttack = 5,
            Connection = 6,
            Disconnect = 7,
            ModAsset = 8,
            HandPose = 9,
            GripRadius = 10,
            BalloonShot = 11,
            PowerPunch = 12,
            TransformSync = 13,
            PuppetSync = 14,
            TransformQueue = 15,
            PuppetQueue = 16,
            TransformCreate = 17,
            PuppetCreate = 18,
            IDCallback = 19,
            ZombieMode = 20,
            ZombieLoadout = 21,
            ZombieDiff = 22,
            ZombieStart = 23,
            ZombieWave = 24,
            FantasyCount = 25,
            FantasyDiff = 26,
            FantasyChal = 27,
            ShortId = 28,
            MagazinePlug = 29,
            FileTransferBegin = 30,
            FileTransferChunk = 31,
            ObjectDestroy = 32,
            Heartbeat = 33,
            TransformCollision = 34,
            SpawnRequest = 35,
            SpawnClient = 36,
            SpawnTransfer = 37,
            GripEvent = 38,
            PlayerEvent = 39,
            SceneEvent = 40,
            ClientReady = 41,
            TransformSyncBatch = 42,
            VoiceData = 43,
            ItemSyncRequest = 44,        // "I don't have item X you just spawned, please send it"
            ItemSyncUnavailable = 45,    // "I don't have that item's file either, give up"
            ItemSyncFileIncoming = 46,   // "Sending item X as file Y" - sent right before the P2P transfer starts
            PlayermodelSyncRequest = 47, // "I don't have your playermodel file, please send it"
            GamemodeState = 48,          // Host-authoritative round/score state, sent on change
            GamemodeEvent = 49;          // One-off gamemode events (kill, capture, round start/end)
    }
}
