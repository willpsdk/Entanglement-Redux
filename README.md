# Entanglement: Redux 🔗
![Status](https://img.shields.io/badge/Status-Work_In_Progress_(WIP)-orange.svg)
![Game](https://img.shields.io/badge/Game-BONEWORKS-blue)

**Entanglement: Redux** is an open-source revival and complete overhaul of the classic *BONEWORKS* multiplayer mod. 

With the deprecation of the Discord Game SDK, the original Entanglement mod was left without a functioning networking backend. Entanglement *Redux* brings the mod back to life by migrating the entire networking architecture to **Steamworks.NET**, while completely rewriting the physics sync to match modern VR multiplayer standards.

### ⚠️ Status: Work In Progress (WIP)
This project is currently in **active development**. While the core Steamworks migration and physics overhauls are implemented, you will encounter bugs, desyncs, and missing features. It is not yet ready for a full public release, but developers and testers are welcome to test and suggest improvements! :)

---

## ✨ What's New in Entanglement: Redux?

### 1. Steamworks Integration
We have completely ripped out the dead Discord Game SDK and replaced it with native **Steam Matchmaking and P2P Networking**.
* **Public Lobbies:** Host and find public servers directly inside the BoneMenu via Steam's worldwide lobby search.
* **Steam Overlay Support:** Join friends, invite players, and view rich presence directly through the Steam overlay (`Shift + Tab`).
* **Native Voice Chat:** Integrated Steam VoIP replaces the old Discord audio buffer system.

### 2. "Fusion-Style" Physics Syncing
The original Entanglement suffered from jittery objects because it forced physical items to teleport to exact X/Y/Z coordinates. Entanglement *Redux* completely changes how objects sync:
* **Velocity Extrapolation:** Instead of just sending positions, objects now sync their `velocity` and `angularVelocity`. 
* **PD Controllers:** Objects smoothly fly to their destinations using a tuned Proportional-Derivative joint controller, respecting Boneworks' physics engine instead of fighting it.
* **Sleep States:** To save immense amounts of network bandwidth, the mod now tracks Rigidbody sleep states and stops sending packets when objects are resting.

---

## 🛠️ Dependencies

Melon Loader 5.4
BONEWORKS (duh)
ModThatIsntAMod

I will post a 0.1 build when I get a chance! Please be paitent, im the only person working on this project. Thank You :)

## 🤝 Contributing

**We want your help!** Entanglement: Redux is a massive undertaking, and we are looking for developers, modders, and VR enthusiasts to help us get this to a polished 1.0 release state.

How you can contribute:
* **Bug Squashing:** Have fun with friends and then report any issues in the Issues tab!
* **Physics Tuning:** Help us refine the `ConfigurableJoint` and PD controller math in `TransformSyncable.cs` to make throwing and catching items even smoother.
* **Compatibility:** Help us patch standard Boneworks items (guns, magazines, custom maps) to work flawlessly over Steam P2P.

**To Contribute:**
1. Fork the repository.
2. Create a new branch for your feature (`git checkout -b feature/AmazingFeature`).
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`).
4. Push to the branch (`git push origin feature/AmazingFeature`).
5. Open a Pull Request!

---

## 📜 Credits
* **The Original Entanglement Team:** For creating the legendary foundation that proved BONEWORKS multiplayer was possible.
* **Lakatrazz & The ModThatIsNotMod Team:** For the essential BoneMenu and modding frameworks.
* **BoneLab Fusion (Lakatrazz/Maranara):** For the inspiration behind the velocity-based physics extrapolation.
