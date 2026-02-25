# Entanglement: Redux 🔗
![Status](https://img.shields.io/badge/Status-Work_In_Progress_(WIP)-orange.svg)
![Game](https://img.shields.io/badge/Game-BONEWORKS-blue)

**Entanglement: Redux** is an open-source revival and complete overhaul of the classic *BONEWORKS* multiplayer mod. 

With the deprecation of the Discord Game SDK, the original Entanglement mod was left without a functioning networking backend. Entanglement *Redux* brings the mod back to life by migrating the entire networking architecture to **Steamworks.NET**, while completely rewriting the physics sync to match modern VR multiplayer standards.

### ⚠️ Status: Work In Progress (WIP)
This project is currently in **active development**. While the core Steamworks migration and physics overhauls are implemented, you will encounter bugs, desyncs, and missing features. It is not yet ready for a full public release, but developers and testers are welcome to test and suggest improvements! :)

---

## ✨ Entanglement: Redux - Features

### 🌐 1. Native Steamworks Integration
We have completely excised the deprecated Discord Game SDK, rebuilding the mod's entire networking foundation to utilize native Steam Matchmaking and P2P Networking. 
* **Public Lobbies:** Host and discover public servers directly through the in-game BoneMenu, leveraging Steam's robust global lobby search.
* **Steam Overlay Support:** Seamlessly join friends, send invites, and view detailed Rich Presence directly through the Steam Overlay (`Shift + Tab`).
* **Native Voice Chat:** Integrated Steam VoIP entirely replaces the legacy Discord audio buffer system, delivering low-latency, high-fidelity proximity chat.

### ↕️ 2. Updated & Improved Physics Syncing
The original Entanglement suffered from severe object jitter caused by forced physical teleportation. *Entanglement: Redux* fundamentally changes how physical objects synchronize across the network to provide a buttery-smooth multiplayer experience:
* **Velocity Extrapolation:** Instead of simply broadcasting raw positions, networked objects now synchronize their `velocity` and `angularVelocity`, allowing clients to accurately predict movement between network ticks.
* **PD Controllers:** Physical items now smoothly travel to their target destinations using a finely tuned Proportional-Derivative (PD) joint controller. This respects the Boneworks physics engine rather than fighting it.
* **Sleep States:** To dramatically reduce network bandwidth overhead, the engine now actively monitors Rigidbody sleep states, intelligently halting packet transmission the moment an object comes to a rest.

### 💀 3. Dynamic Player Representation & Death Sync
Combat and player interactions have been significantly upgraded to feel incredibly responsive and grounded in the game world.
* **Instant Ragdoll Sync:** When a player is killed (whether by self-inflicted damage, NPCs, or PvP combat), their avatar instantly collapses into a fully synchronized physics ragdoll visible to all connected clients.
* **Performance Cleanup:** To maintain peak server performance and prevent physics calculation lag, player ragdolls persist for 30 seconds before seamlessly sinking into the environment and despawning.

### 🛡️ 4. Enhanced Server Stability & Level Transitions
Behind the scenes, the mod's architecture has been completely fortified to prevent crashes and ensure a seamless cooperative experience.
* **Seamless Level Loading:** Scene transitions have been completely rewritten to safely handle the destruction and recreation of local player rigs, eliminating loading screen crashes and broken holograms.
* **Host Protection:** Added intelligent server-side safeguards, including an anti-spam host-creation cooldown, to prevent console flooding and accidental concurrent lobby generation.
* **Optimized Injection:** Harmony patches have been streamlined to improve general mod compatibility and overall client stability during heavy combat and object destruction.

---

## 🛠️ Dependencies

Melon Loader 5.4,
BONEWORKS (duh),
ModThatIsntAMod
(if asking for steamworks download this and put the x64 Dll in Mods! https://github.com/rlabrecque/Steamworks.NET/releases/tag/20.1.0)

Download the Latest build from Nexus!
https://www.nexusmods.com/boneworks/mods/108
Or use Thunderstore! (_I update Nexus first_)

## 🤝 Contributing

**We want your help!** Entanglement: Redux is a massive undertaking, and we are looking for developers, modders, and VR enthusiasts to help us get this to a polished 1.0 release state.

How you can contribute:
* **Find Bugs!:** Have fun with friends and then report any issues in the Issues tab!
* **Physics Tuning:** Help us refine the Syncing for a better expericence! Object and Physics Syncing in: ObjectSync.cs, Syncable.cs, TransformSyncable.cs, PooleeSyncable.cs and SyncUtilities.cs | Player Syncing in: PlayerRep.cs | Network Message Handlers (_The Packets_) in: TransformSyncMessage.cs, PlayerRepSyncMessage.cs and TransformCreateMessage.cs
* **Add Your Own Features:** Add your own Quality of Life features or entire new features! Up to you :)

**To Contribute:**
1. Fork the repository.
2. Create a new branch for your mod.
3. Commit your change.
4. I'll test your change!.
5. If its good ill merge it.

Thank you for Contributing :)

---

## 📜 Credits
* **The Original Entanglement Team:** For creating the legendary foundation that proved BONEWORKS multiplayer was possible.
* **Lakatrazz & The ModThatIsNotMod Team:** For the essential BoneMenu and modding frameworks.
* **BoneLab Fusion (Lakatrazz/Maranara):** For the inspiration behind the velocity-based physics extrapolation.
