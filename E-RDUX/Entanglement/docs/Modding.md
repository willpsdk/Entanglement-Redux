# Building a mod that talks to Entanglement Redux

This is for people writing a separate MelonLoader mod that wants to hook into Entanglement
Redux - get told when a player joins, send your own data across the lobby, sync your own
files, that kind of thing. If you want to build a gamemode specifically, skip ahead to
`Gamemodes.md` - this doc is the more general "how do I even talk to this mod" one.

Everything here is optional. If you're just making a normal Boneworks mod that doesn't care
about multiplayer, none of this applies to you and Entanglement won't get in your way.

## Getting your mod loaded and noticed

Entanglement has its own lightweight module system, separate from MelonLoader's, because it
needs update hooks that fire in a specific order relative to its own networking tick. To hook
into it:

```csharp
[assembly: Entanglement.Modularity.EntanglementModuleInfo(typeof(MyModule), "My Mod", "1.0.0", "YourName")]

public class MyModule : Entanglement.Modularity.EntanglementModule
{
    public override void OnModuleLoaded() {
        // register message handlers, subscribe to events, whatever you need
    }

    public override void Update() { }
    public override void OnSceneWasInitialized(int buildIndex, string sceneName) { }
}
```

Then, from your own MelonMod's `OnApplicationStart`, tell Entanglement about your assembly:

```csharp
Entanglement.Modularity.ModuleHandler.SetupModule(System.Reflection.Assembly.GetExecutingAssembly());
```

That's it. `OnModuleLoaded()` fires once, right away, and the rest of the lifecycle methods
get called from the same place Entanglement calls its own (see `Module.cs` for the full list
- it's short, just `Update`/`FixedUpdate`/`LateUpdate`/`OnSceneWasInitialized`/
`OnLoadingScreen`/`OnApplicationQuit`).

You don't strictly need a module just to send network messages - it's mainly useful if you
want the lifecycle hooks. If all you want is to react to a message arriving, registering the
handler (next section) is enough on its own.

## Sending your own network messages

Entanglement's whole networking layer is built around one pattern: a byte identifies the
message type, and a `NetworkMessageHandler<T>` knows how to turn your data into bytes and
back. Every feature in this mod - object sync, chat, voice, gamemodes - is just another one
of these. Yours can be too.

```csharp
public class MyMessageData : NetworkMessageData {
    public string someText;
    public int someNumber;
}

public class MyMessageHandler : NetworkMessageHandler<MyMessageData>
{
    public override byte? MessageIndex => 200; // pick something unclaimed, see below

    public override NetworkMessage CreateMessage(MyMessageData data) {
        NetworkMessage message = new NetworkMessage();
        // pack data.someText / data.someNumber into message.messageData however you like
        return message;
    }

    public override void HandleMessage(NetworkMessage message, long sender) {
        // unpack message.messageData, sender is the Steam id of whoever sent it
    }
}
```

Register it once, from `OnModuleLoaded` or `OnApplicationStart`:

```csharp
Entanglement.Network.NetworkMessage.RegisterHandler<MyMessageHandler>();
```

To actually send one:

```csharp
var msg = Entanglement.Network.NetworkMessage.CreateMessage((byte)200, new MyMessageData { someText = "hi", someNumber = 5 });
Entanglement.Network.Node.activeNode.BroadcastMessage(Entanglement.Network.NetworkChannel.Reliable, msg.GetBytes());
// or SendMessage(userId, channel, bytes) to talk to one specific player
```

### About that message id

Here's the part I want to be straight with you about: **there's no central registry**. The
byte you pick for `MessageIndex` just needs to not collide with anything else registered in
the same running game. If two different mods both pick 200, whichever one registers second
throws an exception the moment it tries to register - Entanglement itself currently uses ids
0 through 49, and its own optional compat messages (CustomMaps, PlayerModels support) use 80
and 81. Past that, it's genuinely first-come-first-served between whatever mods happen to be
installed together.

Practically: pick something well clear of the low end (150+ is a reasonable bet right now),
and wrap your `RegisterHandler` call in a try/catch so that if you do collide with something,
your mod logs an error and keeps running instead of taking the whole game down with it. It's
not a great system, but it's an honest one, and it's the same one every message in this mod
uses internally.

## Getting notified about players

You don't need your own messages for basic stuff like "a player joined." Entanglement already
tracks this - `Entanglement.Representation.PlayerRepresentation.representations` is a live
dictionary of every connected player's Steam id to their in-world representation, and
`Entanglement.Network.Node.activeNode.connectedUsers` is the raw id list. Read from these
rather than keeping your own copy.

## Syncing files (items, playermodels, or your own)

If you're making a spawnable item or a playermodel, **you don't need to do anything** - it
already syncs. This is worth explaining because it's easy to assume you need to hook
something, and you really don't.

Here's what's actually happening: when someone spawns your item, Entanglement's existing
spawn message already tells every other client what got spawned and where. If one of those
clients doesn't recognize the item (because they don't have your mod), their game asks the
spawner for the file behind it, over Steam directly - no third-party file host, no upload
step, nothing you have to set up. The spawner finds which of their installed `.melon` files
contains that item and sends it. The receiver loads it, registers it, and the item that was
waiting to spawn finally shows up. Playermodels work the same way, just simpler, since there's
only one file to ask for instead of a whole item registry to search.

The one thing that won't sync, on purpose: if your `.melon` contains a `.bytes` asset (a
compiled C# assembly), the entire file is refused, not just the code part. There's no safe
way to split "the mesh you wanted" from "the code you didn't agree to run," so we don't try -
we just don't send it. If you want your item's assets to reach people who don't have your mod
installed, keep them in a bundle that's assets only, separate from anything with code in it.

Players can turn either kind of sync off, or cap the file size, from
`Entanglement Redux > File Sync` in BoneMenu. If you want to read those settings yourself:
`Entanglement.Sync.SyncPrefs.itemSyncEnabled`, `.playermodelSyncEnabled`, `.maxSyncSizeKB`.

### Syncing something that isn't an item or a playermodel

The file transfer underneath both of those is a normal public API, not something private to
them. If your mod needs to send a file to another player - a config, a level fragment,
anything - you can use the same thing:

```csharp
using Entanglement.Sync;

// once, at startup
FileTransferManager.RegisterCategoryHandler(FileTransferCategory.Custom1, OnFileReceived, OnFileFailed);

void OnFileReceived(FileTransfer transfer) {
    string path = Path.Combine(myModFolder, transfer.fileName);
    FileTransferManager.WriteReceivedFile(transfer, path);
}

void OnFileFailed(FileTransfer transfer) {
    // transfer can be null here - e.g. the file couldn't even be read on the sending side
}

// whenever you want to actually send one
FileTransferManager.SendFile(targetUserId, fullFilePath, FileTransferCategory.Custom1);
```

There are four shared slots, `Custom1` through `Custom4`, because the category list can't be
extended from outside this mod's own source. Only one handler can be registered per category
at a time, so if you're worried about another mod also using `Custom1`, filter on
`transfer.fileName` inside your own handler rather than assuming everything that lands there
is yours.

This doesn't include any kind of "ask first" negotiation - `SendFile` just starts sending.
Item sync and playermodel sync both build their own request/reply step on top of it using a
small message like the one described above (see `CustomItemSync.cs` if you want the exact
pattern). Files move over the reliable channel, chunked and paced automatically, so you don't
need to think about packet size.

### One thing to know about sending a file right when someone joins

A freshly-joined client holds off broadcasting its own body/nametag until it has no file
downloads in flight - so nobody sees someone half-loaded in. This check isn't per-category, it
watches every incoming transfer regardless of who registered it. If your mod pushes a file at
someone the moment they join, you're holding up their entrance right along with item/
playermodel sync, for as long as your transfer takes (capped at 90 seconds either way, so it
can't hang someone forever). Usually that's fine or even desirable, but if you're sending
something large and non-essential, consider delaying it a few seconds rather than firing it
immediately on connect.

## See also

- `Gamemodes.md` - writing an actual competitive mode (deathmatch, capture the flag, etc.)
