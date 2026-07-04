# Changelog
### **UNRELEASED - v0.4.0 - Smoother sync & story mode**
- Object sync now sends velocities and dead reckons between packets, remote objects glide instead of stuttering
- Remote-owned rigidbodies use rigidbody interpolation so their motion renders smoothly in VR
- Doors, levers, valves and other scene-jointed objects are driven through velocities instead of a rigid follow joint, so the sync no longer fights their hinge (this was the door stutter)
- Player representations interpolate and extrapolate between packets instead of snapping on arrival
- Story mode progress now syncs: button presses (ButtonToggle) and key insertions (KeyReciever) replicate to everyone, and physically-synced levers/valves drive their circuits on every client
- Magazines no longer vibrate in other players' guns (the sync stopped fighting the plug joint, the gun's sync carries the magazine)
- MonoMat ammo machines now work in multiplayer: magazine deposits replicate, so balance, unlock state, the door and change match on every client
- Level transitions are faster: Unity's async loading throttle is lifted during loading screens, and the host announces level changes when loading STARTS so clients load in parallel instead of waiting for the host to finish
- NPCs are much smoother: remote NPC muscles no longer fight the networked pose, and every jointed body (NPC bones, pull box handles, flails) is driven through velocities so nothing vibrates against its joints
- NPC deaths and despawns now sync: whoever's simulation kills an NPC first kills it for everyone (matching death effects), and corpses despawn everywhere when the host cleans them up
- Pull boxes (handle crates) now work in multiplayer: the pull replicates to everyone, the dispensed item spawns once through the host and syncs to all clients
- Protocol version bumped to 0.4.0 (wire format changed, older clients are rejected cleanly)

### **UNRELEASED - Steamworks networking**
- Replaced the Discord Game SDK networking backend with Steamworks.NET 20.1.0
  - Lobbies, invites, rich presence and P2P transport now run over Steam
  - Join games via the Steam overlay ("Join Game" / lobby invites) or the in-game Public Lobbies browser
  - Server visibility is now Private / Friends Only / Public (Steam lobby types)
  - steam_api64.dll and Steamworks.NET.dll are embedded and auto-extracted, no Discord install needed
  - Voice chat was removed (it was a Discord service; Steam has no drop-in equivalent)
  - NOTE: Steam must be running and the game must be launched through Steam; Oculus cross-play is no longer possible

### **v0.1.0 - IN DEVELOPMENT**

#### *Done features*
- TODO

#### *WIP features*
- Rewrote networking backend to make it more user friendly to other modders
  - Warning: These changes might incur a performance penalty for people with weaker /
  older CPUs, sorry about that, we see it as a fair trade for friendliness and speed.

#### *Planned features*
- Changed how we handle connections to use an "acknowledgement" system. This won't change any part the joining process for the end user except it helps us handle being disconnected a lot better because we wait for a `ConnectionAck` message to determine whether or not we are allowed to join the game we're trying to connect to.
- Fixed "Lobby purgatory" when being disconnected from full lobbies
- Made PVP a lot better
  - Teams
  - Friendly fire toggle

### **v0.0.5 - Current**
- Fixed a problem with lobby capacities

### **v0.0.4**
- Fixed a critical divide by zero error that happened when pausing to the SteamVR menu

### **v0.0.3**
- Optimized the mod for better performance
- Increased stability of physics
