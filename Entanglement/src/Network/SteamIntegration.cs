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
    public enum VoiceStatus
    {
        Disabled,
        Enabled,
    }

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

        // An ID of 0 is reserved for the host
        public static byte localByteId = 0;
        public static byte lastByteId = 1;

        public static ulong GetLongId(byte shortId)
        {
            if (shortId == 0) return hostUser.m_SteamID;

            return byteIds.TryIdx(shortId);
        }

        public static byte GetByteId(ulong longId)
        {
            if (longId == currentUser.m_SteamID) return localByteId;

            return byteIds.FirstOrDefault(o => o.Value == longId).Key;
        }

        public static byte CreateByteId() => lastByteId++;

        public static void RegisterUser(ulong userId, byte byteId) => byteIds.Add(byteId, userId);

        public static byte RegisterUser(ulong userId)
        {
            byte byteId = CreateByteId();
            RegisterUser(userId, byteId);
            return byteId;
        }

        public static void RemoveUser(ulong userId) => byteIds.Remove(GetByteId(userId));

        public static void Initialize()
        {
            try
            {
                if (!SteamAPI.Init())
                {
                    throw new Exception("SteamAPI.Init() returned false. Make sure Steam is running.");
                }

                currentUser = SteamUser.GetSteamID();
                EntangleLogger.Log($"Current Steam User: {SteamFriends.GetPersonaName()}");

                EntangleLogger.Log("Initializing Rich Presence...");
                DefaultRichPresence();
                EntangleLogger.Log("Rich Presence initialized!");
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Failed to initialize the Steam Client! Continuing without Entanglement!\nDid you make sure to start the game with Steam open?\nFailed with reason: {e.Message}\nTrace: {e.StackTrace}");
                isInvalid = true;
            }
        }

        public static void DefaultRichPresence()
        {
            if (isInvalid) return;

            try
            {
                // The main text shown right under "BONEWORKS"
                SteamFriends.SetRichPresence("status", $"Playing Entanglement: Redux (v{EntanglementMod.VersionString})");

                // The sub-message text
                SteamFriends.SetRichPresence("details", "Playing Solo");

                SteamFriends.SetRichPresence("connect", "");
            }
            catch (Exception e)
            {
                EntangleLogger.Error($"Failed to set Rich Presence: {e.Message}\nTrace: {e.StackTrace}");
            }
        }

        public static void UpdateActivity()
        {
            if (isInvalid) return;

            if (hasLobby)
            {
                EntangleLogger.Log($"Updating Rich Presence: {(isHost ? "Hosting" : "Playing")} in a server!");

                // The main text shown right under "BONEWORKS"
                SteamFriends.SetRichPresence("status", $"Playing Entanglement: Redux (v{EntanglementMod.VersionString})");

                // The sub-message text changes whether you are the host or a client
                SteamFriends.SetRichPresence("details", isHost ? "Hosting a Server" : "Playing in a Server");

                SteamFriends.SetRichPresence("steam_display", "#StatusFull");
                SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobbyId.m_SteamID}");
            }
            else
            {
                DefaultRichPresence();
            }
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

        public static void Update()
        {
            if (!isInvalid)
            {
                SteamAPI.RunCallbacks();
            }
        }

        public static void Flush() { }

        public static void Tick()
        {
            Update();
            Flush();
        }

        public static void Shutdown()
        {
            if (!isInvalid)
            {
                SteamFriends.ClearRichPresence();
                SteamAPI.Shutdown();
            }
        }

        public static void UpdateVoice(VoiceStatus status)
        {
            voiceStatus = status;

            if (!hasLobby) return;

            switch (status)
            {
                default:
                case VoiceStatus.Disabled:
                    SteamUser.StopVoiceRecording();
                    break;
                case VoiceStatus.Enabled:
                    SteamUser.StartVoiceRecording();
                    break;
            }
        }
    }
}