# Entanglement Redux

Multiplayer for BONEWORKS. Steam lobbies, other players in the room, and stuff you grab actually moving for everyone else.

This is a continuation of the original Entanglement mod. It is not the old `boneworks-mp` project and does not share code with it.

Still a work in progress. Things will break.

## What you get

- Host or join over Steam (public, friends-only, or private)
- Other players, with hands and fingers
- Guns, magazines, and other held stuff that stays in their hands
- Story sync: buttons, keys, levers, valves, boxes, NPC deaths
- Death and ragdolls
- Custom items, maps, and playermodels sent between players. You approve downloads before they install

Open the wrist circle menu for Profile, Lobby, Matchmaking, Players, Downloads, and Settings.

## Install

You need [MelonLoader](https://melonwiki.xyz/), [ModThatIsNotMod](https://boneworks.thunderstore.io/), and Steam running before you launch.

Put `EntanglementRedux.dll` and `ModThatIsNotMod.dll` in your `Mods` folder. Steamworks is bundled. If Steam still fails to start the mod, grab [Steamworks.NET 20.1.0](https://github.com/rlabrecque/Steamworks.NET/releases/tag/20.1.0), drop `Steamworks.NET.dll` into `BONEWORKS\MelonLoader\Managed`, and `steam_api64.dll` next to `BONEWORKS.exe`.

## Building

Open `E-RDUX/Entanglement.sln` in Visual Studio and point the references at your BONEWORKS MelonLoader `Managed` folder. The output is `EntanglementRedux.dll`.

## Other mods

If you want your mod to send messages or files through Entanglement, see [Modding.md](E-RDUX/Entanglement/docs/Modding.md).

## License

MIT. See [LICENSE](LICENSE).

Built on the original Entanglement by zCubed and Lakatrazz. Maintained by willpsdk and datgingeguy.
