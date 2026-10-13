    namespace Entanglement.Network
{
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
            ItemSyncFileIncoming = 46,
            PlayermodelSyncRequest = 47, // "I don't have your playermodel file, please send it"
            GamemodeState = 48,          // RETIRED with the gamemode system - no sender, no handler
            GamemodeEvent = 49,          // RETIRED with the gamemode system - no sender, no handler
            MapSyncRequest = 50,
            PlayerRepGrab = 51,          // RETIRED with the object sync revert - no sender, no handler
            PlayerRepRelease = 52,       // RETIRED with the object sync revert - no sender, no handler
            ThunderstoreOffer = 53,      // "Get this published package from Thunderstore, not from me"
            SlowMo = 54,                 // Control_GlobalTime intensity, when lobby slow-mo sync is on
            ClawTarget = 55,
            FileTransferEnd = 56,
            NpcState = 57,
            MagazineAmmo = 58,           // RETIRED with the object sync revert - no sender, no handler
            FlashlightState = 59,
            SyncableRelease = 60,        // RETIRED with the object sync revert - no sender, no handler
            NpcDamage = 61,              // RETIRED - the TakeDamage patch it needed broke NPC damage
            MonoMatState = 62,           // Whole vending state, host-authoritative
            GunRack = 63,                // Someone racked a gun you are holding
            NpcOwner = 64,               // Who runs an NPC's AI now; follows whoever it is fighting
            SyncOwner = 65,              // Bumped a prop: client asks, host grants and tells everyone
            NpcHealth = 66,
            NpcHit = 67,
            GunSlide = 68;               // Owner racking their gun by hand, shown on everyone's copy
    }
}
