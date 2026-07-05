# Entanglement Redux

A multiplayer mod for BONEWORKS. This is a continuation of the original Entanglement mod, rebuilt on Steamworks instead of the (now dead) Discord Game SDK, with a mostly rewritten networking and object sync layer.

It is not affiliated with the original `boneworks-mp` project. It shares no code with it.

## Status

This is a work in progress. Expect bugs. Current version is 0.4.0.

## What it does

- Steam-based lobbies and P2P networking (public, friends-only, or private)
- Synced object physics with velocity-based dead reckoning, so held and thrown objects don't stutter or snap
- Player representations (the models you see for other players) with interpolated movement and full hand/finger tracking
- Story mode sync: buttons, keys, levers, valves, pull boxes, and NPC deaths/despawns all replicate across clients
- Ragdoll death sync
- Zone-aware culling that won't hide a player who's still standing in a zone with someone else


## Installing

You'll need:

- [MelonLoader](https://melonwiki.xyz/) installed on your BONEWORKS install
- [ModThatIsNotMod](https://boneworks.thunderstore.io/) — required dependency, the mod won't load without it
- Steam running before you launch the game

Drop `EntanglementRedux.dll` and `ModThatIsNotMod.dll` into your `Mods` folder. Steamworks.NET and the native Steam API DLL are embedded in the mod and extract themselves automatically, you don't need to install them separately.

## Building from source

Open `Entanglement.sln` in Visual Studio. You'll need MelonLoader's `Managed` folder (the unhollowed game assemblies) referenced by the project — point the project at your own BONEWORKS install, or drop the DLLs into a `managed` folder next to the project.

The build outputs `EntanglementRedux.dll`.

## Contributing

Bug reports and pull requests are welcome. If you're fixing something, a short description of what was broken and why your fix addresses it goes a long way — this codebase has a lot of history and it's easy to accidentally reintroduce something that was already fixed once.

## License

MIT. See [LICENSE](LICENSE).

## Credits

Built on the original Entanglement by zCubed and Lakatrazz. Maintained by willpsdk.
