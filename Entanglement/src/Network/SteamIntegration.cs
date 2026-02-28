using System;
using System.Linq;
using System.Collections.Generic;
using Steamworks;
using MelonLoader;
using UnityEngine;
using Entanglement.UI;
using Entanglement.Extensions;

namespace Entanglement.Network
{
    public enum VoiceStatus { Disabled, Enabled, }

    public static class SteamIntegration
    {
        public const string multiplayerIcon = "entanglement";
        public const string singleplayerIcon = "boneworks";
        public const string notHosting = "This user isn't hosting a game!";

        public static string targetScene = "undefined";

        public static CSteamID lobbyId = CSteamID.Nil;
        public static CSteamID currentUser = CSteamID.Nil;
        public static CSteamID hostUser = CSteamID.Nil;

        public static bool isInvalid;
        public static bool hasLobby => lobbyId != CSteamID.Nil;
        public static bool isHost => hasLobby && hostUser == currentUser;
        public static bool isConnected => hasLobby && hostUser != currentUser;

        public static VoiceStatus voiceStatus = VoiceStatus.Disabled;
        public static Dictionary<byte, ulong> byteIds = new Dictionary<byte, ulong>();

        public static byte lastByteId = 1;

        public static ulong GetLongId(byte shortId)
        {
            if (shortId == 0) return hostUser.m_SteamID;
            return byteIds.TryIdx(shortId);
        }

        public static byte GetByteId(ulong longId)
        {
            // FIX: Only the host uses 0. Everyone else must be looked up so they don't steal the Host's ID!
            if (longId == hostUser.m_SteamID) return 0;
            return byteIds.FirstOrDefault(o => o.Value == longId).Key;
        }

        public static byte CreateByteId() => lastByteId++;

        // FIX: Prevent "An item with the same key has already been added" error
        public static void RegisterUser(ulong userId, byte byteId)
        {
            if (!byteIds.ContainsKey(byteId))
            {
                byteIds.Add(byteId, userId);
                EntangleLogger.Verbose($"[SteamIntegration] Registered user {userId} with byte ID {byteId}");
            }
            else if (byteIds[byteId] != userId)
            {
                // If the key exists but points to a different user, update it
                EntangleLogger.Verbose($"[SteamIntegration] Updated user mapping for byte ID {byteId}: {byteIds[byteId]} -> {userId}");
                byteIds[byteId] = userId;
            }
            else
            {
                // Same user already registered, no action needed
                EntangleLogger.Verbose($"[SteamIntegration] User {userId} already registered with byte ID {byteId}");
            }
        }

        public static byte RegisterUser(ulong userId)
        {
            byte byteId = CreateByteId();
            RegisterUser(userId, byteId);
            return byteId;
        }

        // FIX: Added null check for RemoveUser
        public static void RemoveUser(ulong userId)
        {
            byte byteId = GetByteId(userId);
            if (byteId > 0)
            {
                byteIds.Remove(byteId);
                EntangleLogger.Verbose($"[SteamIntegration] Removed user {userId} (byte ID {byteId})");
            }
        }

        public static void Initialize()
        {
            try
            {
                EntangleLogger.Log("Attempting to initialize Steam API...");

                if (!SteamAPI.Init())
                {
                    throw new Exception(
                        "SteamAPI.Init() returned false. This usually means:\n" +
                        "1. Steam is not running - Please launch Steam first\n" +
                        "2. steam_appid.txt is missing in game folder\n" +
                        "3. Steam AppID is incorrect\n" +
                        "4. Running BONEWORKS outside of Steam\n" +
                        "5. Steam client needs to be updated\n" +
                        "6. Your Steam account doesn't own BONEWORKS\n" +
                        "\nFIX: Make sure to launch BONEWORKS through Steam, not directly!"
                    );
                }

                currentUser = SteamUser.GetSteamID();
                EntangleLogger.Log($"✓ Steam API initialized successfully!");
                EntangleLogger.Log($"Current Steam User: {SteamFriends.GetPersonaName()}");
                EntangleLogger.Log("Initializing Rich Presence...");
                DefaultRichPresence();
                EntangleLogger.Log("✓ Rich Presence initialized!");
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"\n" +
                    $"═══════════════════════════════════════════════════════\n" +
                    $"STEAM INITIALIZATION FAILED!\n" +
                    $"═══════════════════════════════════════════════════════\n" +
                    $"Error: {e.Message}\n" +
                    $"═══════════════════════════════════════════════════════\n" +
                    $"\n" +
                    $"TROUBLESHOOTING STEPS:\n" +
                    $"1. Make sure Steam is running\n" +
                    $"2. Close and reopen Steam completely\n" +
                    $"3. Launch BONEWORKS from Steam (don't run .exe directly)\n" +
                    $"4. Verify BONEWORKS files in Steam properties\n" +
                    $"5. Update Steam client to latest version\n" +
                    $"6. Restart your PC\n" +
                    $"\n" +
                    $"Full error: {e.StackTrace}\n" +
                    $"═══════════════════════════════════════════════════════");
                isInvalid = true;
            }
        }

        public static void DefaultRichPresence()
        {
            if (isInvalid) return;
            try
            {
                SteamFriends.SetRichPresence("status", $"Playing Entanglement: Redux (v{EntanglementMod.VersionString})");
                SteamFriends.SetRichPresence("details", "Playing Solo");
                SteamFriends.SetRichPresence("connect", "");
            }
            catch (Exception e) { EntangleLogger.Error($"Failed to set Rich Presence: {e.Message}\nTrace: {e.StackTrace}"); }
        }

        public static void UpdateActivity()
        {
            if (isInvalid) return;

            if (hasLobby)
            {
                EntangleLogger.Log($"Updating Rich Presence: {(isHost ? "Hosting" : "Playing")} in a server!");
                SteamFriends.SetRichPresence("status", $"Playing Entanglement: Redux (v{EntanglementMod.VersionString})");
                SteamFriends.SetRichPresence("details", isHost ? "Hosting a Server" : "Playing in a Server");
                SteamFriends.SetRichPresence("steam_display", "#StatusFull");
                SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobbyId.m_SteamID}");
            }
            else DefaultRichPresence();
        }

        public static string ParseScene(string scene)
        {
            switch (scene.ToLower())
            {
                default: return scene;
                case "sandbox_blankbox": return "Blankbox";
                case "scene_redactedchamber": return "[REDACTED] Chamber";
                case "scene_mainmenu": return "Main Menu";
                case "scene_breakroom": return "Breakroom";
                case "scene_streets": return "Streets";
                case "scene_tuscany": return "Tuscany";
                case "zombie_warehouse": return "Zombie Warehouse";
                case "scene_throneroom": return "Throne Room";
                case "scene_runoff": return "Runoff";
                case "scene_arena": return "Arena Campaign";
                case "arena_fantasy": return "Arena Gamemode";
                case "sandbox_museumbasement": return "Museum Basement";
                case "sandbox_handgunbox": return "Handgun Range";
                case "scene_hoverjunkers": return "Hover Junkers";
                case "scene_tower": return "Tower";
                case "scene_warehouse": return "Warehouse";
                case "scene_towerboss": return "Time Tower";
                case "scene_sewerstation": return "Sewers";
                case "scene_museum": return "Museum";
                case "scene_dungeon": return "Dungeon";
                case "scene_subwaystation": return "Central Station";
            }
        }

        public static void Update() { if (!isInvalid) SteamAPI.RunCallbacks(); }
        public static void Flush() { }
        public static void Tick() { Update(); Flush(); }

        public static void Shutdown()
        {
            if (!isInvalid) { SteamFriends.ClearRichPresence(); SteamAPI.Shutdown(); }
        }

        public static void UpdateVoice(VoiceStatus status)
        {
            voiceStatus = status;
            if (!hasLobby) return;

            // FIX: Use new proximity-based voice chat system
            if (status == VoiceStatus.Enabled)
            {
                Managers.VoiceChatManager.SetVoiceChatMode(Managers.VoiceChatManager.VoiceChatMode.Proximity);
            }
            else
            {
                Managers.VoiceChatManager.SetVoiceChatMode(Managers.VoiceChatManager.VoiceChatMode.Disabled);
            }
        }
    }
}