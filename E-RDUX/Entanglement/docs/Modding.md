# Building a Mod That Works With Entanglement Redux

So you've got a MelonLoader mod and you want it to play nice with Entanglement Redux. This doc covers everything from "how do I even start?" to "how do I sync files across the network?"

**Don't need multiplayer stuff?** Then you don't need any of this. Entanglement Redux won't get in your way if you're just making a regular single-player mod.

## Getting Started: The Module System

Entanglement Redux has its own lightweight module system because it needs to call your code at specific times—like right before it does networking stuff, or when a scene loads. It's optional, but it makes things easier.

Here's the bare minimum:

```csharp
[assembly: Entanglement.Modularity.EntanglementModuleInfo(
    typeof(MyModule), 
    "My Cool Mod",      // display name
    "1.0.0",            // your version
    "YourName"          // author
)]

public class MyModule : Entanglement.Modularity.EntanglementModule
{
    public override void OnModuleLoaded() {
        // This runs once when the mod loads.
        // Register message handlers, subscribe to events, etc here.
        MelonLogger.Msg("My module loaded!");
    }

    public override void Update() { }
    public override void OnSceneWasInitialized(int buildIndex, string sceneName) { }
}
```

Then from your main MelonMod class, tell Entanglement to load it:

```csharp
public override void OnApplicationStart() {
    Entanglement.Modularity.ModuleHandler.SetupModule(System.Reflection.Assembly.GetExecutingAssembly());
}
```

That's it. The module fires `OnModuleLoaded()` right away, and then gets called at the same time Entanglement does its own updates. The full list of available hooks is: `Update`, `FixedUpdate`, `LateUpdate`, `OnSceneWasInitialized`, `OnLoadingScreen`, `OnApplicationQuit`.

**Quick tip:** You don't *need* a module just to receive network messages. If you only want to react to incoming data, just register the message handler (next section) and you're good.

## Sending Network Messages

Entanglement's entire networking layer works the same way: a `NetworkMessageHandler<T>` knows how to pack your data into bytes and unpack it on the other end. Everything—object sync, chat, voice, gamemodes—is built on this same pattern.

Here's how to add your own message type:

```csharp
public class MyMessageData : NetworkMessageData {
    public string someText;
    public int someNumber;
}

public class MyMessageHandler : NetworkMessageHandler<MyMessageData>
{
    public override byte? MessageIndex => 200;  // pick an unused ID (see below)

    public override NetworkMessage CreateMessage(MyMessageData data) {
        NetworkMessage message = new NetworkMessage();
        // Pack your data into message.messageData
        // Use BinaryWriter/BinaryReader if you want, or hand-roll it
        return message;
    }

    public override void HandleMessage(NetworkMessage message, long sender) {
        // Unpack message.messageData
        // sender is the Steam ID of whoever sent it
    }
}
```

Register it once (from `OnModuleLoaded` or `OnApplicationStart`):

```csharp
Entanglement.Network.NetworkMessage.RegisterHandler<MyMessageHandler>();
```

To send a message:

```csharp
var data = new MyMessageData { someText = "hi", someNumber = 5 };
var msg = Entanglement.Network.NetworkMessage.CreateMessage((byte)200, data);
Entanglement.Network.Node.activeNode.BroadcastMessage(
    Entanglement.Network.NetworkChannel.Reliable, 
    msg.GetBytes()
);
// Use SendMessage(userId, channel, bytes) to send to one player instead
```

### Picking a Message ID

Here's the honest truth: **there's no central registry of message IDs.** You just pick a byte that nobody else in the running game has claimed. Entanglement itself uses 0–49, and custom compat messages (CustomMaps, PlayerModels) use 80–81.

**What to do:** Pick something high (150+) and assume you won't collide. Wrap your `RegisterHandler` call in a try/catch just in case:

```csharp
try {
    Entanglement.Network.NetworkMessage.RegisterHandler<MyMessageHandler>();
} catch (Exception e) {
    MelonLogger.Error($"Failed to register my message: {e.Message}");
    // your mod still works, just without networking
}
```

It's not a perfect system, but it's honest.

## Getting Notified About Players

You don't need to build your own player tracking. Entanglement already does it:

- **`Entanglement.Representation.PlayerRepresentation.representations`** — a live dictionary of every connected player: Steam ID → their in-world player rep
- **`Entanglement.Network.Node.activeNode.connectedUsers`** — just the list of connected Steam IDs

Read from these instead of keeping your own copies. They stay in sync automatically.

## File Sync: The Easy Part

### Items and Player Models (Automatic)

You don't need to do anything. If someone spawns your item or player model in multiplayer, it syncs automatically.

Here's what actually happens behind the scenes:
1. Someone spawns your item
2. Entanglement tells all players "item X spawned at location Y"
3. A player who doesn't recognize the item asks the spawner for it
4. The spawner finds the `.melon` file that contains it and sends it over Steam
5. The receiver loads the file and the item appears

Player models work the same way. It's all peer-to-peer, no upload step, no third-party hosting needed.

**One important thing:** If your `.melon` file contains `.bytes` assets (compiled C# assemblies), we won't send the whole file. There's no safe way to split "the mesh you wanted" from "the code you didn't agree to run," so we don't try. If you want to let people download your item's assets, put the assets in a separate bundle with no code in it.

Players can disable file sync or set a max file size in BoneMenu (`Entanglement Redux > File Sync`). If you want to read those settings:

```csharp
Entanglement.Sync.SyncPrefs.itemSyncEnabled
Entanglement.Sync.SyncPrefs.playermodelSyncEnabled
Entanglement.Sync.SyncPrefs.maxSyncSizeKB
```

### Sending Your Own Files

Maybe you want to send a config file, a level fragment, or something custom. You can use the same file transfer system:

```csharp
using Entanglement.Sync;

// Set this up once at startup
FileTransferManager.RegisterCategoryHandler(
    FileTransferCategory.Custom1,
    OnFileReceived,
    OnFileFailed
);

void OnFileReceived(FileTransfer transfer) {
    string savePath = Path.Combine(myModFolder, transfer.fileName);
    FileTransferManager.WriteReceivedFile(transfer, savePath);
}

void OnFileFailed(FileTransfer transfer) {
    // Something went wrong; transfer might be null
    MelonLogger.Warning($"File transfer failed: {transfer?.fileName}");
}

// Send a file whenever you need to
FileTransferManager.SendFile(targetUserId, fullFilePath, FileTransferCategory.Custom1);
```

There are four shared slots: `Custom1` through `Custom4`. Only one handler can be registered per category at a time. If you're worried about collisions with other mods, filter by `transfer.fileName` inside your handler instead of assuming everything there is yours.

Files automatically chunk and pace themselves—don't worry about packet size, just call `SendFile` and it handles the rest.

## Important: The Join Gate

When a player first joins, they stay hidden from other players until their file downloads finish. This keeps people from seeing someone half-loaded.

**Here's what this means for you:** If your mod sends a file to someone the moment they join, you're holding up their visibility along with items and player models. It's usually fine (or even desirable), but if you're sending something large and optional, consider waiting a few seconds instead of firing it immediately.

This is capped at 90 seconds—if a download gets stuck, the player appears anyway rather than waiting forever.

## Next Steps

- **Making a gamemode?** Check out [Gamemodes.md](Gamemodes.md)
- **Need more network message examples?** Look at `CustomItemSync.cs` or `FileTransfer.cs` for real implementations
