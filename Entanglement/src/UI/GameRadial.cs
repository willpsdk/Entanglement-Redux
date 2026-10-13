using System;
using System.Collections.Generic;

using Entanglement.Data;
using Entanglement.Managers;
using Entanglement.Network;
using Entanglement.Objects;
using Entanglement.Representation;
using Entanglement.Sync;
using Entanglement.Voice;
using Entanglement.Gamemodes;
using Steamworks;

using StressLevelZero.UI;
using StressLevelZero.UI.Radial;

using UnityEngine;

namespace Entanglement.UI
{
    public static class GameRadial
    {
        static PageItem.Directions[] slots;

        static PageItem.Directions[] Slots() {
            if (slots != null)
                return slots;
            slots = new[] {
                PageItem.Directions.NORTH,
                PageItem.Directions.NORTHEAST,
                PageItem.Directions.EAST,
                PageItem.Directions.SOUTHEAST,
                PageItem.Directions.SOUTHWEST,
                PageItem.Directions.WEST,
                PageItem.Directions.NORTHWEST,
            };
            return slots;
        }

        static PageView pageView;
        static Page pending;
        static bool animateNext = true;
        static float nextClick;

        public static bool AllowClick() {
            if (Time.unscaledTime < nextClick)
                return false;
            nextClick = Time.unscaledTime + 0.35f;
            return true;
        }

        static Action Once(Action action) {
            return () => {
                if (!AllowClick())
                    return;
                action?.Invoke();
            };
        }

        static CallResult<LobbyMatchList_t> lobbySearch;
        static int searchGen;
        static int searchWaiting;
        static bool viewingServers;
        static readonly List<CSteamID> servers = new List<CSteamID>();
        static int playerPage;
        static int downloadPage;

        public static void OnSceneWasInitialized() {
            pageView = null;
            pending = null;
        }

        public static void Tick() {
            if (pending == null)
                return;
            if (!Bind())
                return;

            if (animateNext)
                pageView.ChangePage(pending);
            else
                pageView.Render(pending);

            pending = null;
            animateNext = true;
        }

        public static bool Open() {
            if (!AllowClick())
                return true;
            if (!Bind())
                return false;

            ShowHub();
            return true;
        }

        static bool Bind() {
            if (pageView)
                return true;

            GameObject rig = null;
            if (PlayerScripts.playerRig)
                rig = PlayerScripts.playerRig.gameObject;
            if (!rig)
                rig = LocalPlayer.GetRigManager();
            if (!rig)
                return false;

            Transform panel = rig.transform.Find("[UIRig]/PLAYERUI/panel_Default");
            if (!panel)
                return false;

            pageView = panel.GetComponent<PageView>();
            return pageView != null && pageView.m_HomePage != null;
        }

        static void Show(Page page, bool animate = true) {
            pending = page;
            animateNext = animate;
        }

        static void ShowHome() {
            viewingServers = false;
            if (pageView != null && pageView.m_HomePage != null)
                Show(pageView.m_HomePage);
        }

        static Page PageOf(params Entry[] entries) {
            Page page = new Page();
            page.items = new Il2CppSystem.Collections.Generic.List<PageItem>();

            PageItem.Directions[] pageSlots = Slots();
            int count = Math.Min(entries.Length, pageSlots.Length);
            for (int i = 0; i < count; i++)
                page.items.Add(new PageItem(Fit(entries[i].label, entries[i].wide), pageSlots[i], Once(entries[i].action)));

            page.items.Add(new PageItem("Back", PageItem.Directions.SOUTH, Once(ShowHome)));
            return page;
        }

        static Page PageBack(Action back, params Entry[] entries) {
            Page page = new Page();
            page.items = new Il2CppSystem.Collections.Generic.List<PageItem>();

            PageItem.Directions[] pageSlots = Slots();
            int count = Math.Min(entries.Length, pageSlots.Length);
            for (int i = 0; i < count; i++)
                page.items.Add(new PageItem(Fit(entries[i].label, entries[i].wide), pageSlots[i], Once(entries[i].action)));

            page.items.Add(new PageItem("Back", PageItem.Directions.SOUTH, Once(back)));
            return page;
        }

        static void ShowHub() {
            viewingServers = false;
            Show(PageOf(
                new Entry("Lobby", ShowLobby),
                new Entry("Server Browser", ShowServers),
                new Entry("Players", ShowPlayers),
                new Entry("Downloads", ShowDownloads),
                new Entry("Gamemodes", ShowGamemodes),
                new Entry("Notifications", ShowNotifications),
                new Entry("Settings", ShowSettings)
            ));
        }

        /// <summary>Fusion MenuNotifications (radial).</summary>
        static void ShowNotifications() => MenuNotifications.Show(ShowHub);

        /// <summary>Fusion MenuGamemode overview (radial).</summary>
        static void ShowGamemodes() => MenuGamemode.Show(ShowHub);

        /// <summary>Shared page builder for Fusion-shaped menu modules (MenuGamemode, etc.).</summary>
        public struct MenuEntry {
            public string label;
            public Action action;
            public bool wide;
            public MenuEntry(string label, Action action, bool wide = false) {
                this.label = label;
                this.action = action;
                this.wide = wide;
            }
        }

        public static void OpenBackPage(Action back, List<MenuEntry> entries, bool animate = false) {
            if (entries == null)
                entries = new List<MenuEntry>();
            var mapped = new Entry[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                mapped[i] = new Entry(entries[i].label, entries[i].action, entries[i].wide);
            Show(PageBack(back ?? ShowHub, mapped), animate);
        }

        static void ShowLobby() {
            Show(PageBack(ShowHub,
                new Entry("Start Server", () => { Server.StartServer(); ShowLobby(); }),
                new Entry("Suicide", () => {
                    if (!SteamIntegration.hasLobby) {
                        GameNotify.Show("You need to be in a server to do that.", 3f);
                        return;
                    }
                    PlayerDeathManager.Suicide();
                }),
                new Entry("Stop Server", () => {
                    if (Server.instance == null && !(SteamIntegration.hasLobby && SteamIntegration.isHost)) {
                        GameNotify.Show(SteamIntegration.hasLobby
                            ? "You're not the host. Use Disconnect."
                            : "No server running.", 3f);
                        ShowLobby();
                        return;
                    }

                    if (Server.StopHosting())
                        GameNotify.Show("Server stopped.", 3f);
                    else
                        GameNotify.Show("Couldn't stop the server.", 3f);
                    ShowLobby();
                }),
                new Entry("Disconnect", () => {
                    if (Node.isServer) {
                        GameNotify.Show("You're the host. Use Stop Server.", 3f);
                        ShowLobby();
                        return;
                    }
                    if (Node.activeNode is Client client)
                        client.DisconnectFromServer();
                    else if (!SteamIntegration.hasLobby)
                        GameNotify.Show("You're not in a server.", 3f);
                    ShowLobby();
                }),
                new Entry("Invite", InviteSteam),
                new Entry("Lobby Options", ShowLobbyOptions)
            ), false);
        }

        static void InviteSteam() {
            if (!SteamIntegration.hasLobby) {
                GameNotify.Show("Start or join a server first.", 3f);
                return;
            }

            SteamIntegration.UpdateActivity();
            DiscordInvite.Refresh();
            ShowHome();
            SteamFriends.ActivateGameOverlayInviteDialog(SteamIntegration.lobby);
        }

        static void ShowLobbyOptions() {
            Show(PageBack(ShowLobby,
                new Entry(PlayerSlotLabel(), () => {
                    int next = Server.maxPlayers + 1;
                    if (next > 16)
                        next = Server.serverMinimum;
                    Server.maxPlayers = (byte)next;
                    Server.instance?.UpdateLobbyConfig();
                    ShowLobbyOptions();
                }),
                new Entry(Server.isLocked ? "Locked" : "Open", () => {
                    Server.isLocked = !Server.isLocked;
                    Server.instance?.UpdateLobbyConfig();
                    ShowLobbyOptions();
                }),
                new Entry(Server.visibility.ToString(), () => {
                    Server.visibility = (ServerVisibility)(((int)Server.visibility + 1) % 3);
                    Server.instance?.UpdateLobbyConfig();
                    ShowLobbyOptions();
                }),
                new Entry(SlowMoSync.Label(), () => {
                    if (SteamIntegration.hasLobby && !SteamIntegration.isHost) {
                        GameNotify.Show("Only the host can change slow-mo sync.", 3f);
                        ShowLobbyOptions();
                        return;
                    }

                    SlowMoSync.Cycle();
                    ShowLobbyOptions();
                }),
                new Entry(Server.ClientTeleportAllowed ? "Teleport: Everyone" : "Teleport: Host Only", () => {
                    if (SteamIntegration.hasLobby && !SteamIntegration.isHost) {
                        GameNotify.Show("Only the host can change who can teleport.", 3f);
                        ShowLobbyOptions();
                        return;
                    }

                    Server.clientTeleport = !Server.clientTeleport;
                    Server.instance?.UpdateLobbyConfig();
                    ShowLobbyOptions();
                })
            ), false);
        }

        static void ShowServers() {
            viewingServers = true;
            serverPage = 0;
            servers.Clear();
            searchGen++;
            searchWaiting = searchGen;

            if (lobbySearch == null)
                lobbySearch = CallResult<LobbyMatchList_t>.Create(OnLobbySearch);

            SteamMatchmaking.AddRequestLobbyListStringFilter("entanglement", "true", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            lobbySearch.Set(SteamMatchmaking.RequestLobbyList());
            ShowServerPage(true);
        }

        static void OnLobbySearch(LobbyMatchList_t result, bool failed) {
            if (searchWaiting != searchGen)
                return;

            servers.Clear();
            if (!failed) {
                int count = (int)result.m_nLobbiesMatching;
                for (int i = 0; i < count; i++)
                    servers.Add(SteamMatchmaking.GetLobbyByIndex(i));
            }
            IncludeLocalLobby();

            if (viewingServers)
                ShowServerPage(false);
        }

        static void IncludeLocalLobby() {
            if (!SteamIntegration.hasLobby)
                return;

            CSteamID mine = SteamIntegration.lobby;
            for (int i = 0; i < servers.Count; i++) {
                if (servers[i] == mine)
                    return;
            }
            servers.Insert(0, mine);
        }

        static int serverPage;

        static void ShowServerPage(bool searching) {
            if (!viewingServers)
                return;

            // Arrows sit on the two small wedges above Back, so the big slots stay servers.
            const int pageSize = 5;
            int pages = servers.Count == 0 ? 1 : (servers.Count + pageSize - 1) / pageSize;
            if (serverPage >= pages)
                serverPage = 0;
            if (serverPage < 0)
                serverPage = 0;

            Page page = new Page();
            page.items = new Il2CppSystem.Collections.Generic.List<PageItem>();
            page.items.Add(new PageItem(PageArrow(-1, pages), PageItem.Directions.SOUTHWEST, Once(() => StepServerPage(-1))));
            page.items.Add(new PageItem(PageArrow(1, pages), PageItem.Directions.SOUTHEAST, Once(() => StepServerPage(1))));

            PageItem.Directions[] serverSlots = {
                PageItem.Directions.NORTH,
                PageItem.Directions.NORTHEAST,
                PageItem.Directions.EAST,
                PageItem.Directions.WEST,
                PageItem.Directions.NORTHWEST,
            };

            if (searching && servers.Count == 0)
                page.items.Add(new PageItem("Searching...", serverSlots[0], Once(() => { })));
            else if (servers.Count == 0)
                page.items.Add(new PageItem("No servers", serverSlots[0], Once(() => { })));
            else {
                int start = serverPage * pageSize;
                int slot = 0;
                for (int i = start; i < servers.Count && slot < serverSlots.Length; i++) {
                    CSteamID id = servers[i];
                    page.items.Add(new PageItem(Fit(ServerLabel(id)), serverSlots[slot], Once(() => Join(id))));
                    slot++;
                }
            }

            page.items.Add(new PageItem("Back", PageItem.Directions.SOUTH, Once(ShowHub)));
            Show(page, false);
        }

        static string PageArrow(int dir, int pages) {
            if (pages <= 1)
                return dir < 0 ? "<" : ">";
            if (dir < 0)
                return "< " + (serverPage + 1).ToString();
            return (serverPage + 1).ToString() + "/" + pages.ToString() + " >";
        }

        static int ServerPageCount() {
            const int pageSize = 5;
            if (servers.Count == 0)
                return 1;
            return (servers.Count + pageSize - 1) / pageSize;
        }

        static void StepServerPage(int dir) {
            int pages = ServerPageCount();
            if (pages <= 1) {
                GameNotify.Show("No more pages.", 2f);
                return;
            }

            serverPage = (serverPage + dir + pages) % pages;
            ShowServerPage(false);
        }

        static void Join(CSteamID lobbyId) {
            viewingServers = false;
            ShowHome();

            if (lobbyId == CSteamID.Nil) {
                GameNotify.Show("That Server no longer exists.", 3f);
                return;
            }

            if (SteamIntegration.hasLobby) {
                if (SteamIntegration.lobby == lobbyId)
                    GameNotify.Show("You're already in this server.", 3f);
                else
                    GameNotify.Show("Leave your server before joining another.", 3f);
                return;
            }

            string host = SteamMatchmaking.GetLobbyData(lobbyId, "host_name");
            GameNotify.Show(string.IsNullOrEmpty(host) ? "Joining..." : $"Joining {host}...", 3f);
            Client.instance?.JoinLobby(lobbyId);
        }

        static string ServerLabel(CSteamID lobbyId) {
            string host = SteamMatchmaking.GetLobbyData(lobbyId, "host_name");
            if (string.IsNullOrEmpty(host))
                host = "Unknown";

            SteamIntegration.LobbyOccupancy(lobbyId, out int current, out int limit, out _);
            if (SteamMatchmaking.GetLobbyData(lobbyId, "locked") == "1")
                return host + " locked";
            if (limit <= 0)
                return host;
            return host + " " + current + "/" + limit;
        }

        static string PlayerSlotLabel() {
            if (!SteamIntegration.hasLobby)
                return $"Players {Server.maxPlayers}";

            SteamIntegration.LobbyOccupancy(SteamIntegration.lobby, out _, out int limit, out int open);
            if (SteamIntegration.isHost && Server.isLocked)
                return $"Players {limit} (Locked)";
            if (open <= 0)
                return $"Players {limit} (Full)";
            return $"Players {limit} ({open} open)";
        }

        static void ShowPlayers() {
            playerPage = 0;
            ShowPlayerPage();
        }

        static void ShowPlayerPage() {
            var members = Members();
            ShowWheelPage(members.ConvertAll(id => {
                string name = SteamIntegration.GetUserName(id);
                return new Entry(name, () => ShowPlayer(id, name));
            }), playerPage, page => { playerPage = page; ShowPlayerPage(); }, ShowHub, "Just you");
        }

        static void ShowPlayer(long userId, string name) {
            var entries = new List<Entry> {
                new Entry("Steam Profile", () => {
                    SteamFriends.ActivateGameOverlayToUser("steamid", new CSteamID((ulong)userId));
                })
            };

            if (SteamIntegration.isHost) {
                entries.Add(new Entry("Kick", () => {
                    Server.instance?.KickUser(userId, name);
                    ShowPlayers();
                }));
                entries.Add(new Entry("Ban", () => {
                    BanList.BanUser(userId, name);
                    Server.instance?.KickUser(userId, name, DisconnectReason.Banned);
                    ShowPlayers();
                }));
            }

            if (SteamIntegration.isHost || Server.ClientTeleportAllowed) {
                entries.Add(new Entry("Teleport To", () => {
                    string problem = Server.TeleportTo(userId);
                    if (problem != null)
                        GameNotify.Show(problem, 3f);
                }));
            }

            bool muted = VoiceManager.IsMuted(userId);
            entries.Add(new Entry(muted ? "Unmute" : "Mute", () => {
                VoiceManager.SetMuted(userId, !muted);
                ShowPlayer(userId, name);
            }));

            Show(PageBack(ShowPlayers, entries.ToArray()), false);
        }

        static List<long> Members() {
            var members = new List<long>();
            if (!SteamIntegration.hasLobby)
                return members;

            int count = SteamMatchmaking.GetNumLobbyMembers(SteamIntegration.lobby);
            for (int i = 0; i < count; i++) {
                long id = (long)SteamMatchmaking.GetLobbyMemberByIndex(SteamIntegration.lobby, i).m_SteamID;
                if (id != SteamIntegration.currentUserId)
                    members.Add(id);
            }
            return members;
        }

        static bool downloadsWanted;

        public static void ForceOpenDownloads() {
            downloadPage = 0;
            downloadsWanted = true;
            if (Bind())
                ShowDownloadPage();
        }

        /// <summary>The circle menu has just opened.</summary>
        public static void OnMenuOpened() {
            if (!downloadsWanted)
                return;
            if (!FileTransferManager.HasPendingConsent && !Thunderstore.HasPending) {
                downloadsWanted = false;
                return;
            }
            if (!Bind())
                return;
            downloadsWanted = false;
            downloadPage = 0;
            // Drawn by Tick next frame, after the game has finished opening the menu.
            ShowDownloadPage();
        }

        static void ShowDownloads() {
            downloadPage = 0;
            ShowDownloadPage();
        }

        static void ShowDownloadPage() {
            var pending = FileTransferManager.GetPendingConsentTransfers();
            var store = Thunderstore.GetPending();
            var entries = new List<Entry>();
            if (pending.Count > 0 || store.Count > 0) {
                entries.Add(new Entry("Accept All", () => {
                    FileTransferManager.AcceptAllPending();
                    Thunderstore.AcceptAllPending();
                    GameNotify.Show("Accepted pending downloads.", 3f);
                    ShowDownloads();
                }));
            }

            for (int s = 0; s < store.Count; s++) {
                Thunderstore.PendingOffer offer = store[s];
                string key = offer.key;
                string label = offer.label;
                entries.Add(new Entry(label, () => ShowStoreDownload(key, label)));
            }

            for (int i = 0; i < pending.Count; i++) {
                FileTransfer transfer = pending[i];
                string name = System.IO.Path.GetFileName(transfer.fileName);
                entries.Add(new Entry(name, () => ShowDownload(transfer)));
            }

            ShowWheelPage(entries, downloadPage, page => { downloadPage = page; ShowDownloadPage(); }, ShowHub, "No downloads");
        }

        static void ShowStoreDownload(string key, string label) {
            Show(PageBack(ShowDownloads,
                new Entry("Accept Thunderstore", () => {
                    Thunderstore.AcceptPending(key);
                    GameNotify.Show("Accepted " + label, 3f);
                    ShowDownloads();
                }),
                new Entry("Decline", () => {
                    Thunderstore.DeclinePending(key);
                    GameNotify.Show("Declined " + label, 3f);
                    ShowDownloads();
                })
            ), false);
        }

        static void ShowDownload(FileTransfer transfer) {
            string name = System.IO.Path.GetFileName(transfer.fileName);
            float mb = transfer.totalBytes / 1024f / 1024f;
            ushort id = transfer.id;
            long peer = transfer.peer;
            Show(PageBack(ShowDownloads,
                new Entry($"Accept {mb:F1} MB", () => {
                    FileTransferManager.AcceptPending(peer, id);
                    GameNotify.Show($"Accepted {name}", 3f);
                    ShowDownloads();
                }),
                new Entry("Decline", () => {
                    FileTransferManager.DenyPending(peer, id);
                    GameNotify.Show($"Declined {name}", 3f);
                    ShowDownloads();
                })
            ), false);
        }

        // Five names, then Prev and Next. South stays Back.
        static void ShowWheelPage(List<Entry> items, int page, Action<int> setPage, Action back, string emptyLabel) {
            const int pageSize = 5;
            int pages = Math.Max(1, (Math.Max(items.Count, 1) + pageSize - 1) / pageSize);
            if (page >= pages)
                page = 0;

            var shown = new List<Entry>();
            if (items.Count == 0)
                shown.Add(new Entry(emptyLabel, () => { }));
            else {
                int start = page * pageSize;
                for (int i = start; i < items.Count && shown.Count < pageSize; i++)
                    shown.Add(items[i]);
            }

            if (pages > 1) {
                int prev = page;
                int next = page;
                shown.Add(new Entry("Prev", () => { if (prev > 0) setPage(prev - 1); }));
                shown.Add(new Entry("Next", () => { if (next < pages - 1) setPage(next + 1); }));
            }

            Show(PageBack(back, shown.ToArray()), false);
        }

        static void ShowSettings() {
            Show(PageBack(ShowHub,
                new Entry(Client.nameTagsVisible ? "Names On" : "Names Off", () => {
                    Client.nameTagsVisible = !Client.nameTagsVisible;
                    ShowSettings();
                }),
                new Entry("Voice", ShowVoice),
                new Entry("File Sync", ShowSync),
                new Entry("Banned", () => { bannedPage = 0; ShowBanned(); }),
                new Entry(TransportLabel(), () => {
                    string next = Entanglement.Network.Layers.NetworkLayerManager.CyclePreferredTitle();
                    GameNotify.Show("Transport → " + next + ". Restart the game to apply AppID changes.", 5f, GameNotify.Kind.Info);
                    ShowSettings();
                }),
                new Entry("Debug", ShowDebug)
            ), false);
        }

        static string TransportLabel() {
            string active = Entanglement.Network.Layers.NetworkLayerManager.ActiveTitle;
            string preferred = Entanglement.Network.Layers.NetworkPrefs.ResolvedLayerTitle();
            if (!string.Equals(active, preferred, StringComparison.OrdinalIgnoreCase))
                return "Net: " + active + " → " + preferred;
            return "Net: " + active;
        }

        static int bannedPage;

        // Everyone you've banned, one tap to unban.
        static void ShowBanned() {
            var entries = new List<Entry>();
            foreach (var user in BanList.bannedUsers) {
                long id = user.Item1;
                string name = user.Item2;
                entries.Add(new Entry("Unban " + name, () => {
                    BanList.UnbanUser(id, name);
                    GameNotify.Show("Unbanned " + name + ".", 3f);
                    ShowBanned();
                }));
            }
            ShowWheelPage(entries, bannedPage, page => { bannedPage = page; ShowBanned(); }, ShowSettings, "Nobody banned");
        }

        static void ShowDebug() {
            Show(PageBack(ShowSettings,
                new Entry(DebugLog.On ? "Debug Log On" : "Debug Log", () => {
                    DebugLog.Toggle();
                    GameNotify.Show(DebugLog.On
                        ? "Debug log on. Details go to MelonLoader/Latest.log."
                        : "Debug log off.", 4f);
                    ShowDebug();
                }),
                new Entry("Dump Status", () => DebugLog.DumpAll()),
                new Entry("Debug Channels", ShowDebugChannels),
                new Entry(PlayerRepresentation.RepresentationTestActive ? "Rep Test On" : "Representation Test", () => {
                    if (PlayerRepresentation.RepresentationTestActive)
                        PlayerRepresentation.StopRepresentationTest();
                    else
                        PlayerRepresentation.StartRepresentationTest();
                    ShowDebug();
                }),
                new Entry(PlayerRepresentation.debugFrozen ? "Dummy Frozen" : "Freeze Dummy", () => {
                    if (!PlayerRepresentation.RepresentationTestActive) {
                        GameNotify.Show("Turn on the Representation Test first.", 3f);
                        return;
                    }
                    PlayerRepresentation.debugFrozen = !PlayerRepresentation.debugFrozen;
                    ShowDebug();
                }),
                new Entry("Solo New Stack", ShowNewStackTests),
                new Entry("Sync Tests", ShowSyncTests)
            ), false);
        }

        static void ShowDebugChannels() {
            Show(PageBack(ShowDebug,
                new Entry(DebugLog.PrefEnabled("entities") ? "Entities On" : "Entities Off", () => {
                    DebugLog.ToggleChannel("entities"); ShowDebugChannels();
                }),
                new Entry(DebugLog.PrefEnabled("puppet") ? "Puppet On" : "Puppet Off", () => {
                    DebugLog.ToggleChannel("puppet"); ShowDebugChannels();
                }),
                new Entry(DebugLog.PrefEnabled("combat") ? "Combat On" : "Combat Off", () => {
                    DebugLog.ToggleChannel("combat"); ShowDebugChannels();
                }),
                new Entry(DebugLog.PrefEnabled("gamemode") ? "Gamemode On" : "Gamemode Off", () => {
                    DebugLog.ToggleChannel("gamemode"); ShowDebugChannels();
                }),
                new Entry(DebugLog.PrefEnabled("transport") ? "Transport On" : "Transport Off", () => {
                    DebugLog.ToggleChannel("transport"); ShowDebugChannels();
                })
            ), false);
        }

        /// <summary>Solo checks for RigPuppet / NetworkEntity / gamemode / combat / transport.</summary>
        static void ShowNewStackTests() {
            Show(PageBack(ShowDebug,
                new Entry("Puppet Check", () => GameNotify.Show(SoloDebug.PuppetCheck(), 6f)),
                new Entry("Entity Stack", () => GameNotify.Show(SoloDebug.EntityStackCheck(), 6f)),
                new Entry("Gamemode Solo", () => GameNotify.Show(SoloDebug.GamemodeSolo(), 6f)),
                new Entry("Combat Dummy", () => GameNotify.Show(SoloDebug.CombatDummyCheck(), 6f)),
                new Entry("Transport", () => GameNotify.Show(SoloDebug.TransportCheck(), 5f)),
                new Entry("NPCs At Dummy", () => GameNotify.Show(SoloDebug.SendNpcs(), 3f)),
                new Entry("Install Custom Content Dependencies", () => {
                    Thunderstore.InstallCustomContentDependencies();
                    PopUpMenuView menu = PopUpMenuView.instance;
                    if (menu != null)
                        menu.Deactivate();
                }, true)
            ), false);
        }

        // Checks that need no second player (SoloDebug). Each says what it measured.
        static void ShowSyncTests() {
            Show(PageBack(ShowDebug,
                new Entry("Follow Test", () => GameNotify.Show(SoloDebug.StartFollowTest(), 4f)),
                new Entry("NPC Follow Test", () => GameNotify.Show(SoloDebug.StartNpcFollowTest(), 4f)),
                new Entry("Freeze Test", () => GameNotify.Show(SoloDebug.StartFreezeTest(), 4f)),
                new Entry("Hit Check", () => GameNotify.Show(SoloDebug.HitCheck(), 5f)),
                new Entry("Turrets And Claw", () => GameNotify.Show(SoloDebug.TurretAndClaw(), 6f)),
                new Entry("Thunderstore Check", () => GameNotify.Show(SoloDebug.ThunderstoreCheck(), 6f)),
                new Entry("More Tests", ShowMoreTests)
            ), false);
        }

        static void ShowMoreTests() {
            Show(PageBack(ShowSyncTests,
                new Entry("Fizzle Nearest Corpse", () => GameNotify.Show(SoloDebug.FizzleNearest(), 3f)),
                new Entry("Restart Nearest NPC", () => GameNotify.Show(SoloDebug.RestartNearestNpc(), 3f))
            ), false);
        }

        static void ShowVoice() {
            Show(PageBack(ShowHub,
                new Entry(VoiceManager.micEnabled ? "Mic On" : "Mic Off", () => {
                    VoiceManager.micEnabled = !VoiceManager.micEnabled;
                    ShowVoice();
                }),
                new Entry(VoiceManager.mode == VoiceMode.Global ? "Global" : "Proximity", () => {
                    VoiceManager.mode = VoiceManager.mode == VoiceMode.Global ? VoiceMode.Proximity : VoiceMode.Global;
                    VoiceManager.ApplySettings();
                    ShowVoice();
                }),
                new Entry($"Range {VoiceManager.proximityRange}m", () => {
                    VoiceManager.proximityRange += 5;
                    if (VoiceManager.proximityRange > 60)
                        VoiceManager.proximityRange = 10;
                    VoiceManager.ApplySettings();
                    ShowVoice();
                }),
                new Entry($"Vol {VoiceManager.outputVolume}%", () => {
                    VoiceManager.outputVolume += 10;
                    if (VoiceManager.outputVolume > 200)
                        VoiceManager.outputVolume = 0;
                    VoiceManager.ApplySettings();
                    ShowVoice();
                }),
                new Entry("Mic Help", () => {
                    GameNotify.Show("Voice Chat uses Steam Mic. Change in Steam Settings under Voice > Voice Input Device", 8f);
                })
            ), false);
        }

        static void ShowSync() {
            Show(PageBack(ShowSettings,
                new Entry(SyncPrefs.itemSyncEnabled.Value ? "Items On" : "Items Off", () => {
                    SyncPrefs.itemSyncEnabled.Value = !SyncPrefs.itemSyncEnabled.Value;
                    ShowSync();
                }),
                new Entry(SyncPrefs.playermodelSyncEnabled.Value ? "Models On" : "Models Off", () => {
                    SyncPrefs.playermodelSyncEnabled.Value = !SyncPrefs.playermodelSyncEnabled.Value;
                    ShowSync();
                }),
                new Entry(SyncPrefs.mapSyncEnabled.Value ? "Maps On" : "Maps Off", () => {
                    SyncPrefs.mapSyncEnabled.Value = !SyncPrefs.mapSyncEnabled.Value;
                    ShowSync();
                }),
                new Entry(SyncPrefs.requireDownloadConsent.Value ? "Ask First" : "Auto Download", () => {
                    SyncPrefs.requireDownloadConsent.Value = !SyncPrefs.requireDownloadConsent.Value;
                    ShowSync();
                }),
                new Entry($"Max {SyncPrefs.maxSyncSizeKB.Value / 1024}MB", () => {
                    int mb = SyncPrefs.maxSyncSizeKB.Value / 1024 + 10;
                    if (mb > 200)
                        mb = 10;
                    SyncPrefs.maxSyncSizeKB.Value = mb * 1024;
                    ShowSync();
                })
            ), false);
        }

        static string Fit(string text, bool wide = false) {
            if (string.IsNullOrEmpty(text))
                return " ";
            text = text.Replace("\n", " ").Trim();
            int limit = wide ? 40 : 18;
            return text.Length <= limit ? text : text.Substring(0, limit - 1) + "...";
        }

        struct Entry {
            public string label;
            public Action action;
            public bool wide;
            public Entry(string label, Action action, bool wide = false) {
                this.label = label;
                this.action = action;
                this.wide = wide;
            }
        }
    }
}
