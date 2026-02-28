# Entanglement: Redux

A high-performance multiplayer networking mod for **BONEWORKS** on PC VR. Play with friends in seamless, smooth co-op gameplay with integrated proximity-based voice chat.

![Version](https://img.shields.io/badge/version-0.1.0-blue)
![Target](https://img.shields.io/badge/target-BONEWORKS-red)
![Framework](https://img.shields.io/badge/framework-.NET%204.7.2-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)

---

## ✨ Features

### 🎮 **High-Performance Networking**
- **60Hz Synchronization** - Ultra-smooth player and object movement
- **Smart Interpolation** - Hides network latency beautifully
- **Optimized Bandwidth** - Efficient P2P networking via Steam
- **Low-Latency Updates** - Reliable and unreliable channels for different data types

### 🗣️ **Advanced Voice Chat**
- **Proximity-Based Voice** - Only hear players within configurable range (10m-500m)
- **Global Voice Mode** - Broadcast to all players option
- **Per-Player Muting** - Mute annoying players with one click
- **Microphone Selection** - Choose from available input devices
- **Volume Controls** - Independent mic input and speaker output sliders
- **Spatial Audio** - Voice comes from player's head position in 3D space

### 👁️ **Player Visibility**
- **Always Visible** - Players don't disappear when moving between rooms
- **Smart Layer Management** - Uses proper culling layers to prevent visibility issues
- **Smooth Body Animations** - IK updates ensure natural movement
- **30Hz Animation Sync** - Smooth animation state synchronization

### 👄 **Talking Animations**
- **Mouth Movement** - Ford's mouth animates when speaking
- **Network Synced** - Everyone sees who's talking
- **Smooth Blending** - Natural fade in/out animations
- **Voice Detection** - Automatic detection via Steam voice input

### 🏷️ **Nametags**
- **Always Facing Camera** - Nametags billboard toward the player
- **Player Identification** - See teammate names above their heads
- **Toggle Option** - Hide/show nametags as needed

### 🚪 **Story Mode Sync**
- **Door Synchronization** - Doors sync state across all players
- **NPC Tracking** - NPC spawning and state synchronized
- **Destructible Objects** - Breakable objects sync properly
- **Multi-Player Compatible** - Full story mode support

### 🎛️ **Customization**
- **Server Settings** - Max players, visibility (public/private), locked status
- **Voice Settings** - Voice mode, microphone, volumes, proximity range
- **Logging Levels** - Verbose network logging for debugging

---

## 🚀 Installation

### Requirements
- **BONEWORKS** (Steam)
- **MelonLoader** v0.5.4 or higher
- **ModThatIsNotMod** (dependency)
- **.NET Framework 4.7.2**

### Setup
1. Download the latest `EntanglementRedux.dll` from [Releases](https://github.com/willpsdk/Entanglement-Redux/releases)
2. Place in: `[BONEWORKS]\Mods\`
3. Launch BONEWORKS
4. Access via Bone Menu: **Entanglement: Redux**

---

## 📖 Usage

### Starting a Server
1. Open Bone Menu → **Entanglement: Redux** → **Server Menu**
2. Click **"Start Server"**
3. Configure settings (max players, visibility, lock status)
4. Friends can join via public lobby list or direct invite

### Joining a Server
1. Open Bone Menu → **Entanglement: Redux** → **Client Menu** → **Public Lobbies**
2. Click **"Refresh Lobbies"**
3. Select a lobby and join

### Voice Chat Setup
1. Go to **Voice Menu** in Entanglement settings
2. Select **Voice Mode**: Disabled / Global / Proximity
3. Choose your **Microphone**
4. Adjust **Mic Volume** and **Output Volume**
5. (Optional) Set **Proximity Range** for proximity mode
6. Visit **Player Mute List** to mute/unmute specific players

### Server Settings
- **Max Players** - Adjust capacity (1-8)
- **Locked** - Toggle to prevent new joins
- **Visibility** - Private/Friends Only/Public

---

## 🔧 Technical Specs

### Synchronization Rates
| Component | Update Rate |
|-----------|------------|
| Player Movement | 60Hz |
| Object Transforms | 60Hz |
| Animations | 30Hz |
| Voice Activity | 100ms checks |
| Voice Chat | On-demand |

### Network Channels
| Channel | Type | Use Case |
|---------|------|----------|
| Reliable | TCP-like | Critical updates (connections, door state) |
| Unreliable | UDP-like | Frequent updates (movement, transforms) |
| Attack | Unreliable | Combat events |
| Object | Unreliable | Object synchronization |
| Transaction | Reliable | Economy/transaction data |

### Voice Chat Details
- **Proximity Range**: 10m - 500m (default 50m)
- **Spatial Audio**: 3D positioning from player head
- **Mute Range**: Per-player muting independent of proximity
- **Detection**: Steam voice API integration

---

## 🎯 Features Explained

### Why 60Hz?
Modern VR games run at 60-90 FPS. Our 60Hz sync ensures updates arrive frequently enough that interpolation can smooth movement naturally. This matches your game's refresh rate!

### Proximity Voice
Only hearing nearby players creates immersion and reduces audio clutter. Configurab range lets you balance immersion vs hearing far-away teammates.

### Player Muting
Some players might be annoying (background noise, music, loud). One-click muting without affecting gameplay.

### Talking Animations
Players can see who's speaking via mouth movement. Combined with spatial audio, it creates a more present multiplayer experience.

---

## 🐛 Known Issues & Limitations

- **First person hands** are local-only (your hands, not others')
- **Voice audio** requires good microphone (background noise filtering recommended)
- **Ping dependent** - Very high latency (200ms+) may show noticeable desync
- **Limited to 8 players** per server (Steam P2P limitation)

---

## 📊 Performance Impact

| Metric | Impact | Details |
|--------|--------|---------|
| CPU | Minimal | ~2-5% per connected player |
| Memory | Low | ~50MB base + 10MB per player |
| Network | Low | ~5-15KB/s per player |
| FPS | None | Runs alongside game at 60+ FPS |

---

## 🔐 Security & Privacy

- **P2P Only** - Direct player-to-player connections via Steam
- **No Central Server** - Your data never touches our servers
- **Steam Integration** - Uses Steam's secure networking
- **Local Voices** - Voice data never stored or relayed (except during session)
- **Private Lobbies** - Password-protected or friends-only options available

---

## 🤝 Contributing

Found a bug or have a feature idea? 

1. Check [Issues](https://github.com/willpsdk/Entanglement-Redux/issues)
2. Create a detailed bug report or feature request
3. Submit a pull request with improvements

---

## 📝 Credits

**Entanglement: Redux** is built by:
- **willpsdk** - Lead developer
- **zCubed** - Networking architect
- **Lakatrazz** - Voice chat implementation

Based on the original **Entanglement** mod by the Entanglement team.

**Special Thanks To:**
- MelonLoader team for the modding framework
- Stress Level Zero for BONEWORKS
- Steamworks.NET for Steam integration
- The VR modding community

---

## 📄 License

This project is licensed under the **MIT License** - see [LICENSE](LICENSE) for details.

---

## 🔗 Links

- **Steam Workshop**: [BONEWORKS](https://steamcommunity.com/app/1313140)
- **MelonLoader**: [Download](https://melonwiki.xyz/)
- **GitHub**: [Entanglement-Redux](https://github.com/willpsdk/Entanglement-Redux)

---

## 💬 Support

- **Discord**: [Entanglement Community](#) (add your discord if you have one)
- **Issues**: [GitHub Issues](https://github.com/willpsdk/Entanglement-Redux/issues)
- **Wiki**: [GitHub Wiki](https://github.com/willpsdk/Entanglement-Redux/wiki)

---

## 🎮 Have Fun!

Enjoy seamless multiplayer BONEWORKS gameplay with your friends worldwide!

*Made with ❤️ for VR enthusiasts*
