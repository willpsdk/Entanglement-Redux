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

![Thudnerstore API Downloads](https://github.com/willpsdk/Entanglement-Redux/blob/main/ReduxShowcase/CustomPlayerPrev.gif)

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
  
> Your Lobby is automatically set to public! Change it via Entanglement → Lobby → Lobby Settings → Click on the middle right button that should say either Public, Friendsonly or Private. If your lobby is Public or Friendsonly people can join via your Discord Rich Presence if you have it enabled. If set to Friends Only only people on your Discord Friends list can join through Discord!

  **Why am I getting Steamworks / Steam_api64.dll Error on boot?**
> Entanglement Redux is supposed to extract Steamworks.NET.DLL & SteamAPI.DLL but sometimes it fails to. You will have to do it manually. Here is how you do it!
> - Download [Steamworks](https://github.com/rlabrecque/Steamworks.NET/releases/download/20.1.0/Steamworks.NET-Standalone_20.1.0.zip)
> - Extract the Folder
> - Place 'Steamworks.NET.DLL' into BONEWORKS\MelonLoader\Managed
> - Place 'steam_api64.dll into the Root BONEWORKS folder where BONEWORKS.exe is!'

  **My Friend has Custom Content! How do I get it?**
> Entanglement Redux will detect the Custom Playermodels, Custom Items, Custom Maps & Code Mods (these require a game restart). It will try to detect what mods are being used then Instruct the user to download them! Here is how to accept the Downloads:
> - Open the Radial Menu and select Entanglement
> - Click on downloads
> - Click accept all or click on individual ones! Whatever you prefer :)

###
---

## 🤝 Contributing

Contributions are welcome!

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request


---

## 📄 License

Distributed under the MIT License. See [LICENSE](LICENSE) for more information.

---

## 📬 Contact

[Join](https://discord.gg/tRvsvjCX6M) the Discord :)
(If you have any inquireys feel free to DM Me on Discord 'willpcctv')


---
