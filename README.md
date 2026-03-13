 

Entanglement: Redux is an open-source revival and complete overhaul of the classic BONEWORKS multiplayer mod Entanglement.With the deprecation of the Discord Game SDK, the original Entanglement mod was left without a functioning networking backend. Entanglement: Redux brings the mod back to life by migrating the entire networking architecture to Steamworks.NET, while completely rewriting the physics sync to match modern VR multiplayer standards.

While this has been done before (ReTangled) it has not been a complete overhaul and just a bandaid for it to use Steam Networking! ﻿Entanglement: Redux aims to bring an improved and smoother gameplay!

🔧 Features: 

Steam Networking Intergration

We have completely ripped out the deprecated Discord Game SDK and replaced it with native Steam Matchmaking and P2P Networking.

Public Lobbies: Host and Find public servers directly inside BoneMenu via Steam's Worldwide Lobby Search!
Steam Overlay Support: Join & Invite players through Steam's Built-In overlay!
Native Voice Chat:  Steam VoIP intergration which replaces old Discord Audio Buffer System!

Overhauled Physics Syncing

The original Entanglement Multiplayer suffered from jittery objects because it forced physical items to teleport to exact X/Y/Z coordinates. Entanglement: Redux completely changes how objects sync:

Velocity Extrapolation: Instead of just sending postions, objects now sync there Velocity & ﻿Angular Velocity!
PD Controllers: Objects smoothly fly to their destinations using a tuned Proportional-Derivative joint controller, respecting the Boneworks physics engine instead of fighting it.
﻿Sleep States: To save immense amounts of network bandwidth, the mod now tracks Rigidbody sleep states and stops sending packets when objects are resting.

 Performance & Optimization Upgrades

﻿High-Density NPC and Entity Support: The synchronization architecture has been heavily optimised to handle scenes with massive NPC counts. By streamlining physics calculations and distributing network updates efficiently, Entanglement: Redux maintains stable framerates and prevents performance degradation, even during large-scale combat encounters that may occur while in Sandbox and Story Mode
﻿Optimized Component Caching: We have eliminated thousands of expensive `GetComponent()' calls per frame. Core systems like `TransformSyncable' and `PlayerRep' now utilize custom caching dictionaries for instant lookups of rigidbodies, destructible objects, and grip points. This eliminates the micro-stutters previously associated with grabbing or interacting with items.
﻿Network Message Processing: The legacy networking backend relied on inefficient, reflection-based message parsing (such as executing LINQ queries on every frame). Entanglement: Redux leverages a pre-registered `NetworkMessageHandler’ index to process incoming packets in constant time, significantly lowering the CPU overhead required for network communication.
﻿﻿Data Compression and Struct Simplification: Network payloads have been strictly compressed using custom `SimplifiedTransform’ and `SimplifiedQuaternion’ structs. This ensures that even when transmitting high-fidelity velocity and angular velocity data, the overall packet footprint remains remarkably small.

Status: Work In Progress (WIP)This project is currently in active development. While the core Steamworks migration and physics overhauls are implemented, you will encounter bugs, desyncs, and missing features. It is not yet ready for a full release! If you would like to contribute to this project please do so on the github page! ﻿https://github.com/willpsdk/Entanglement-Redux
