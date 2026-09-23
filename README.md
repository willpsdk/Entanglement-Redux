# Entanglement Redux

An alternative multiplayer mod for BONEWORKS - With many Improvements & Quality of life features bringing it to modern multiplayer standards.

![Entanglement Redux Banner](https://github.com/willpsdk/Entanglement-Redux/blob/main/ReduxShowcase/ReduxBanner.webp)

---

## 📖 Table of Contents

- [About](#about)
- [Features](#features)
- [Features](#features)
- [Demo](#Demo)
- [Installation](#installation)
- [FAQ](#FAQ)
- [Usage](#usage)
- [Screenshots](#screenshots)
- [Contributing](#contributing)
- [License](#license)
- [Contact](#contact)

---

## About

Entanglement Redux aims to give players a reliable Multiplayer Mod for BONEWORKS that synchronises pretty much everything.

We utilize Steamworks.NET P2P Networking for the NetCode which means only Players on Steam can play, but we plan to change this in the future so players with the Oculus Store / Meta Horizon Link Store can also play!

---

##  Features

- **Heavily Improved Synchronisation & Item Sync** — Smoother Objects, NPC & Weapon Interactions between players.
- **Thunderstore Mod Sharing** — Utilizing the amazing Thunderstore API, we are able to detect
- **Revamped Player Rep Collisions / Interactions** — We have fixed the Player Rep's weight issue, which previously stopped dynamicx§ props, elevators or platforms from moving like intended. Which was a issue in other Multiplayer Mods.
- PCVR Only (This is for BONEWORKS Legacy 1.6) I assume Fusion will be updated to support 1.7 (when it eventually releases)

---

##  Demo

New UI Built into BONEWORKS's Radial Menu
![Radial UI Demo](https://github.com/willpsdk/Entanglement-Redux/blob/main/ReduxShowcase/newreduxui.gif)

We Support Discord Invites Too! - Entanglement Redux has full Discord Intergration. Meaning your friends can join, invite through Discord!

![Radial UI Demo](https://github.com/willpsdk/Entanglement-Redux/blob/main/ReduxShowcase/discordinvite.gif)

You no longer have to have the same mods for it to sync! It now downloads all mods each player has to your game so your able to join without manually installing mods.

[Thudnerstore API Downloads](https://github.com/willpsdk/Entanglement-Redux/blob/main/ReduxShowcase/CustomPlayerPrev.gif)

---

### Prerequisites

- [MelonLoader](https://github.com/LavaGang/MelonLoader/releases)
- [ModThatIsNotMod](https://old.thunderstore.io/c/boneworks/p/gnonme/ModThatIsNotMod/)

##  Installation

- Download the Latest MelonLoader Release for your OS
- Open the MelonLoader installer and choose BONEWORKS & make sure the install version is 5.4
- Download ModThatIsNotMod and place the DLL into the Mods folder
- Download Entanglement Redux and place the DLL into the mods folder.
- Make sure Steam is open then open BONEWORKS.
- In BONEWORKS open the radial menu (circle menu with Scenes, Inventory ect) and click Entanglement
- Go to Lobby and click Start Lobby
- Invite Via Steam or on Discord press the + to the left of the text box and click "Invite to play Entanglement Redux"
  
##  FAQ
  **Why are Random people joining my lobby?** 
> Your Lobby is automatically set to public! Change it via Entanglement → Lobby → Lobby Settings → Click on the middle right button that should say either Public, friendsonly or Private. If your lobby is Public or Friends Only people can join via your Discord Rich Presence if you have it enabled. If set to Friends Only only people on your Discord Friends list can join!



###
---

## 💻 Usage

Basic example:

```bash
npm start
```

Code example:

```javascript
import { doThing } from "your-package";

doThing({
  option: true,
});
```

> **Tip:** Use blockquotes like this to highlight tips, notes, or warnings.

> **⚠️ Warning:** Use this style to call out important caveats.

---

## ⚙️ Configuration

| Option    | Type      | Default   | Description                  |
|-----------|-----------|-----------|-------------------------------|
| `apiKey`  | `string`  | `null`    | Your API key                  |
| `debug`   | `boolean` | `false`   | Enables verbose logging       |
| `timeout` | `number`  | `5000`    | Request timeout in ms         |

---

## 📸 Screenshots

<!-- Use a table or side-by-side layout for multiple screenshots -->

| Home Screen | Settings Screen |
|:---:|:---:|
| ![Screenshot 1](https://via.placeholder.com/400x250?text=Screenshot+1) | ![Screenshot 2](https://via.placeholder.com/400x250?text=Screenshot+2) |

<!-- Or full-width single images -->
<!-- ![Full Screenshot](path/to/screenshot.png) -->

---

## 🗺️ Roadmap

- [x] Core functionality
- [x] Basic documentation
- [ ] Add tests
- [ ] Add CI/CD pipeline
- [ ] Publish v2.0

See the [open issues](https://github.com/username/repo-name/issues) for a full list of proposed features.

---

## 🤝 Contributing

Contributions are welcome!

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct.

---

## 📄 License

Distributed under the MIT License. See [LICENSE](LICENSE) for more information.

---

## 📬 Contact

**Your Name** — [@your_twitter](https://twitter.com/your_twitter) — your.email@example.com

Project Link: [https://github.com/username/repo-name](https://github.com/username/repo-name)

---

## 🙏 Acknowledgments

- [Resource or library you used](https://example.com)
- [Inspiration](https://example.com)
- [Icons / assets credit](https://example.com)

<!--
FORMATTING CHEAT SHEET (delete this section before publishing)

# H1 Heading
## H2 Heading
### H3 Heading

**Bold text**
*Italic text*
~~Strikethrough~~
`inline code`

- Bullet point
  - Nested bullet
1. Numbered list

> Blockquote

[Link text](https://example.com)
![Image alt text](path/to/image.png)

```
code block
```

| Col 1 | Col 2 |
|-------|-------|
| A     | B     |

Horizontal rule: ---

Task list:
- [x] Done
- [ ] Not done
-->
