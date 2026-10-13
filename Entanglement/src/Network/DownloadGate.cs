using Steamworks;

using System;

using UnityEngine;

using Entanglement.Sync;
using Entanglement.UI;

namespace Entanglement.Network
{
    public static class DownloadGate
    {
        static bool joining;
        static float joinTime;

        static CSteamID pendingLobby;
        static string pendingFile;
        static bool waitingPackages;
        static bool sawDownload;
        static float deferTime;
        static float nextForceOpen;

        static int heldScene = -1;
        static bool heldReload;
        static string expectedFile;
        static float expectTime;

        const float minSettleSeconds = 3f;
        const float offerWaitSeconds = 180f;
        const float forceOpenEvery = 4f;

        public static void OnJoinedServer()
        {
            if (Node.isServer)
                return;

            joining = true;
            joinTime = Time.time;
        }

        public static void Reset()
        {
            joining = false;
            heldScene = -1;
            expectedFile = null;
            pendingLobby = CSteamID.Nil;
            pendingFile = null;
            waitingPackages = false;
        }

        public static void BeginDeferredJoin(CSteamID lobbyId, bool packages) {
            if (lobbyId == CSteamID.Nil)
                return;
            pendingLobby = lobbyId;
            waitingPackages = packages;
            pendingFile = null;
            sawDownload = false;
            deferTime = Time.unscaledTime;
            nextForceOpen = 0f;
        }

        public static void CancelDeferredJoin(string reason)
        {
            if (pendingLobby == CSteamID.Nil && !waitingPackages && string.IsNullOrEmpty(pendingFile))
                return;
            pendingLobby = CSteamID.Nil;
            pendingFile = null;
            waitingPackages = false;
            if (!string.IsNullOrEmpty(reason))
                GameNotify.Show(reason, 6f);
        }

        // Decline only aborts join when the offer was actually gating the deferred join.
        public static void CancelDeferredJoinIfRequired(string fileName, bool isMapOffer)
        {
            if (pendingLobby == CSteamID.Nil)
                return;

            if (waitingPackages) {
                CancelDeferredJoin("You declined the download, so you can't join.");
                return;
            }

            if (string.IsNullOrEmpty(pendingFile))
                return;

            if (isMapOffer && string.Equals(pendingFile, fileName, StringComparison.OrdinalIgnoreCase))
                CancelDeferredJoin("You declined the download, so you can't join.");
        }

        public static bool TryDeferJoin(CSteamID lobbyId)
        {
            if (lobbyId == CSteamID.Nil)
                return false;
            if (pendingLobby == lobbyId && (waitingPackages || !string.IsNullOrEmpty(pendingFile)))
                return true;

            if (TryDeferPackages(lobbyId))
                return true;

            string fileName = CustomMapSync.LobbyMapFile(lobbyId);
            if (string.IsNullOrEmpty(fileName) || CustomMapSync.HasLocalMap(fileName))
                return false;

            string packed = SteamMatchmaking.GetLobbyData(lobbyId, CustomMapSync.LobbyMapPackageKey);
            ThunderstorePackage package;
            if (!TryParsePackage(packed, out package)) {
                GameNotify.Show("Host map " + fileName + " is not on Thunderstore. Join if you have it, or take the file once you are in.", 8f);
                return false;
            }

            pendingLobby = lobbyId;
            pendingFile = fileName;
            waitingPackages = false;
            sawDownload = false;
            deferTime = Time.unscaledTime;
            nextForceOpen = 0f;
            Thunderstore.OfferPackage(0, package, FileTransferCategory.CustomMap, fileName, "", false);
            GameNotify.Show("You need " + fileName + " before you can join.", 8f);
            GameRadial.ForceOpenDownloads();
            return true;
        }

        static bool TryDeferPackages(CSteamID lobbyId)
        {
            string missing;
            if (ModDependency.TryValidateLobbyMods(lobbyId, out missing) && ModDependency.MissingContentCount == 0)
                return false;

            int code = ModDependency.DownloadMissingFromThunderstore();
            int content = ModDependency.OfferMissingContent();
            if (code <= 0 && content <= 0 && string.IsNullOrEmpty(missing))
                return false;

            GameRadial.ForceOpenDownloads();

            // Code mods need a restart - do not auto-join after accept.
            if (code > 0) {
                GameNotify.Show("You are missing code mods. Accept them, restart Boneworks, then try again.", 8f);
                return true;
            }

            pendingLobby = lobbyId;
            pendingFile = null;
            waitingPackages = true;
            sawDownload = false;
            deferTime = Time.unscaledTime;
            nextForceOpen = 0f;
            GameNotify.Show("Please accept the host's packages. You will be able to join then :)", 8f);
            return true;
        }

        // Switches an in-flight packages defer into a map defer (keeps pendingLobby).
        static bool TryStartMapDefer(CSteamID lobbyId)
        {
            string fileName = CustomMapSync.LobbyMapFile(lobbyId);
            if (string.IsNullOrEmpty(fileName) || CustomMapSync.HasLocalMap(fileName))
                return false;

            string packed = SteamMatchmaking.GetLobbyData(lobbyId, CustomMapSync.LobbyMapPackageKey);
            ThunderstorePackage package;
            if (!TryParsePackage(packed, out package)) {
                CancelDeferredJoin("Host is on " + fileName + ", which is not published. Ask them to switch maps.");
                return true;
            }

            waitingPackages = false;
            pendingFile = fileName;
            sawDownload = false;
            deferTime = Time.unscaledTime;
            nextForceOpen = 0f;
            Thunderstore.OfferPackage(0, package, FileTransferCategory.CustomMap, fileName, "", false);
            GameNotify.Show("You need " + fileName + " before you can join.", 8f);
            GameRadial.ForceOpenDownloads();
            return true;
        }

        public static bool TryHoldScene(int sceneIndex, bool reload)
        {
            if (!NeedsHold())
                return false;

            heldScene = sceneIndex;
            heldReload = reload;
            if (Client.instance != null)
                Client.instance.pendingScene = sceneIndex;
            GameNotify.Show("Still downloading. The level will wait.", 6f);
            GameRadial.ForceOpenDownloads();
            return true;
        }

        public static void ExpectMap(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || CustomMapSync.HasLocalMap(fileName))
                return;
            expectedFile = fileName;
            expectTime = Time.unscaledTime;
            GameRadial.ForceOpenDownloads();
        }

        public static void Tick()
        {
            KeepDownloadsOpen();
            ReleaseExpectedIfReady();
            ContinueDeferredJoin();
            ReleaseHeldScene();
        }

        public static bool IsGated
        {
            get
            {
                if (Node.isServer || !joining)
                    return false;

                if (NeedsHold())
                    return true;

                if (Time.time - joinTime < minSettleSeconds)
                    return true;

                joining = false;
                return false;
            }
        }

        static void KeepDownloadsOpen()
        {
            if (!Thunderstore.HasPending && !FileTransferManager.HasPendingConsent)
                return;
            if (Time.unscaledTime < nextForceOpen)
                return;
            nextForceOpen = Time.unscaledTime + forceOpenEvery;
            GameRadial.ForceOpenDownloads();
        }

        static void ContinueDeferredJoin()
        {
            if (pendingLobby == CSteamID.Nil)
                return;
            if (SteamIntegration.hasLobby)
                return;

            if (Thunderstore.IsBusy || FileTransferManager.HasActiveDownloads)
                sawDownload = true;

            if (waitingPackages) {
                if (Thunderstore.HasPending || FileTransferManager.HasPendingConsent)
                    return;
                if (Thunderstore.IsBusy || FileTransferManager.HasActiveDownloads)
                    return;

                string missing;
                ModDependency.TryValidateLobbyMods(pendingLobby, out missing);
                if (ModDependency.MissingContentCount > 0 || !string.IsNullOrEmpty(missing)) {
                    if (Time.unscaledTime - deferTime > offerWaitSeconds) {
                        CancelDeferredJoin("Downloads didn't finish, you didn't join the lobby");
                    }
                    return;
                }

                // Packages done - still must have the lobby map before JoinLobby.
                if (TryStartMapDefer(pendingLobby))
                    return;

                CSteamID lobbyId = pendingLobby;
                pendingLobby = CSteamID.Nil;
                waitingPackages = false;
                pendingFile = null;
                Client.instance?.BeginSteamJoin(lobbyId);
                return;
            }

            if (string.IsNullOrEmpty(pendingFile))
                return;

            if (CustomMapSync.HasLocalMap(pendingFile) && !Thunderstore.IsBusy && !FileTransferManager.HasActiveDownloads
                && !Thunderstore.HasPending && !FileTransferManager.HasPendingConsent) {
                CSteamID lobbyId = pendingLobby;
                pendingLobby = CSteamID.Nil;
                pendingFile = null;
                Client.instance?.BeginSteamJoin(lobbyId);
                return;
            }

            bool gaveUp = sawDownload && !Thunderstore.IsBusy && !FileTransferManager.HasActiveDownloads
                && !Thunderstore.HasPending && !FileTransferManager.HasPendingConsent;
            bool neverStarted = !sawDownload && Time.unscaledTime - deferTime > offerWaitSeconds;
            if (!gaveUp && !neverStarted)
                return;

            CancelDeferredJoin("Map download has failed. So you have not joined");
        }

        static void ReleaseHeldScene()
        {
            if (heldScene < 0 || NeedsHold())
                return;

            int index = heldScene;
            bool reload = heldReload;
            heldScene = -1;
            heldReload = false;

            Entanglement.Patching.ClientSceneGate.AllowNetworkLoad();
            if (Client.instance != null)
                Client.instance.pendingScene = index;
            StressLevelZero.Utilities.BoneworksSceneManager.LoadScene(index);
            if (reload && Client.instance != null && Client.instance.currentScene == index) {
                // Same-scene reload after downloads: still send ClientReady so host can replay.
                NetworkMessage readyMessage = NetworkMessage.CreateMessage(BuiltInMessageType.ClientReady, new EmptyMessageData());
                if (readyMessage != null)
                    Node.activeNode?.BroadcastMessage(NetworkChannel.Reliable, readyMessage.GetBytes());
            }
        }

        static void ReleaseExpectedIfReady()
        {
            if (string.IsNullOrEmpty(expectedFile))
                return;
            if (CustomMapSync.HasLocalMap(expectedFile)) {
                expectedFile = null;
                return;
            }
            if (Thunderstore.IsBusy || Thunderstore.HasPending || FileTransferManager.HasActiveDownloads || FileTransferManager.HasPendingConsent)
                return;
            if (Time.unscaledTime - expectTime < offerWaitSeconds)
                return;

            GameNotify.Show("You declined the map download, so you are unable to join the host.", 6f);
            expectedFile = null;

            if (!Node.isServer && Client.instance != null)
                Client.instance.DisconnectFromServer(false);
        }

        static bool NeedsHold()
        {
            if (Thunderstore.IsBusy || Thunderstore.HasPending || FileTransferManager.HasActiveDownloads || FileTransferManager.HasPendingConsent)
                return true;
            return !string.IsNullOrEmpty(expectedFile) && !CustomMapSync.HasLocalMap(expectedFile);
        }

        static bool TryParsePackage(string packed, out ThunderstorePackage package)
        {
            package = new ThunderstorePackage();
            if (string.IsNullOrEmpty(packed))
                return false;
            string[] parts = packed.Split('/');
            if (parts.Length != 3)
                return false;
            if (!Thunderstore.IsToken(parts[0]) || !Thunderstore.IsToken(parts[1]) || !Thunderstore.IsToken(parts[2]))
                return false;
            package.author = parts[0];
            package.name = parts[1];
            package.version = parts[2];
            package.displayName = parts[1];
            return true;
        }
    }
}
