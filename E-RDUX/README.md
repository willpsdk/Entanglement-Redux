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
- Automatic file sync — if someone's running a custom item or playermodel you don't have, you just get it from them, no manual downloading
- Built-in gamemodes (Deathmatch, Team Battle, Last Man Standing) with scoring, teams, elimination, and a BoneMenu scoreboard, plus an API so other mods can add their own


## Installing

You'll need:

- [MelonLoader](https://melonwiki.xyz/) installed on your BONEWORKS install
- [ModThatIsNotMod](https://boneworks.thunderstore.io/) — required dependency, the mod won't load without it
- Steam running before you launch the game

Drop `EntanglementRedux.dll` and `ModThatIsNotMod.dll` into your `Mods` folder. Both Steamworks.NET and `steam_api64.dll` are embedded in the mod and it tries to sort itself out on startup — `steam_api64.dll` gets extracted to `%AppData%/EntanglementMod/` and preloaded automatically. If Steam still fails to initialize (check the MelonLoader console for a `steam_api64.dll` error), drop `steam_api64.dll` into your BONEWORKS root folder yourself as a fallback.

You can download Steamworks.NET from this link: https://github.com/rlabrecque/Steamworks.NET/releases/tag/20.1.0

If your getting errors in start up put these files here:

Go into the Downloads -> (Steamworks.NET You just downloaded) -> x64

Put `Steamworks.NET.dll` into BONEWORKS\BONEWORKS\MelonLoader\Managed
Put `steam_api64.dll` into BONEWORKS\BONEWORKS

should have steamworks embedded in the `EntanglementRedux~` but its still WIP.

## Building from source

Open `Entanglement.sln` in Visual Studio. You'll need MelonLoader's `Managed` folder (the unhollowed game assemblies) referenced by the project — point the project at your own BONEWORKS install, or drop the DLLs into a `managed` folder next to the project.

The build outputs `EntanglementRedux.dll`.

## Modding

Want your mod to sync over the network, or to add your own gamemode without touching this
codebase? Both are supported:

- [Modding.md](Entanglement/docs/Modding.md) — how to hook a third-party mod into Entanglement's networking, send your own messages, and get your custom items/playermodels synced for free
- [Gamemodes.md](Entanglement/docs/Gamemodes.md) — how the gamemode framework works, and how to write your own using the built-in ones as reference

## Contributing

Bug reports and pull requests are welcome. If you're fixing something, a short description of what was broken and why your fix addresses it goes a long way — this codebase has a lot of history and it's easy to accidentally reintroduce something that was already fixed once.

## License

MIT. See [LICENSE](LICENSE).

## Credits

Built on the original Entanglement by zCubed and Lakatrazz. Maintained by willpsdk & datgingeguy.
