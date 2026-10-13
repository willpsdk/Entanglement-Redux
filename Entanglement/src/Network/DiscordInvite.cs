using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;

using MelonLoader;

using Steamworks;

using Entanglement.UI;

namespace Entanglement.Network {
    public static class DiscordInvite {
        const int RefreshMs = 20000;
        const int ContestedRefreshMs = 5000;

        // Apps known to put their own Discord status over ours, by process name.
        static readonly string[] rivals = { "Medal" };
        const int RivalCheckMs = 30000;
        static int nextRivalCheck;
        static volatile string rival;
        static bool toldRival;
        // Uploaded in the Discord app under Rich Presence → Art Assets, with this exact name.
        const string LogoAsset = "logo";
        // Fallback if the portal asset is not uploaded yet.
        const string LogoUrl = "https://files.catbox.moe/k3o085.png";

        const int Handshake = 0;
        const int Frame = 1;
        const int Close = 2;
        const int Ping = 3;
        const int Pong = 4;

        static readonly ConcurrentQueue<string> notes = new ConcurrentQueue<string>();
        static readonly ConcurrentQueue<string> warnings = new ConcurrentQueue<string>();
        static readonly ConcurrentQueue<string> longToasts = new ConcurrentQueue<string>();
        static readonly ConcurrentQueue<string> toasts = new ConcurrentQueue<string>();
        static readonly ConcurrentQueue<ulong> joins = new ConcurrentQueue<ulong>();
        static readonly MelonPreferences_Category discordPrefs = MelonPreferences.CreateCategory("EntanglementDiscord");
        static readonly MelonPreferences_Entry<string> applicationId = discordPrefs.CreateEntry(
            "applicationId",
            "1548740160978559067",
            description: "Discord Application ID for Entanglement Redux presence. Not a bot token. Leave empty to disable.");
        // 0 = portal asset "logo", 1 = hosted URL, 2 = no image
        static int logoMode;
        static bool allowJoin = true;
        static readonly object gate = new object();
        static readonly object pipeLock = new object();

        static Thread thread;
        static NamedPipeClientStream pipe;
        static volatile bool running;
        static volatile bool sending;
        static volatile bool connected;

        static bool inLobby;
        static ulong lobbyId;
        static int current = 1;
        static int limit = 1;
        static bool locked;
        static ServerVisibility visibility = ServerVisibility.Private;
        static string scene = "";
        static string statusLine = "Playing Entanglement Redux";
        static long startedAt;
        static int revision;
        static int nonce;
        static bool dirty = true;
        static int lastSent;
        static int sendDelay = 4000;
        static string lastError;
        static string failedClient;
        static bool loggedMissing;
        static bool loggedReady;

        static volatile bool inviteRequested;
        // 0 = party + join secret, 1 = same without privacy, 2 = status text only
        static int activityMode;
        // Discord can fire ACTIVITY_JOIN before Client.StartClient - keep it until JoinLobby can run
        static ulong pendingJoinLobby;
        static float pendingJoinAt;
        const float LeaveSettleSeconds = 1.5f;

        public static bool Connected => connected;

        /// <summary>Send the card again now, as it stands: an invite is about to go out.</summary>
        public static void Refresh() {
            lock (gate) {
                dirty = true;
                lastSent = 0;
            }
        }

        public static void RequestInviteDialog() {
            if (!connected)
                return;
            inviteRequested = true;
        }

        public static string StatusLine {
            get {
                lock (gate)
                    return statusLine;
            }
        }

        public static void Start() {
            if (thread != null)
                return;

            clientId = (applicationId.Value ?? "").Trim();
            MelonPreferences.Save();
            if (clientId.Length < 8) {
                Note("Discord presence is off. Set EntanglementDiscord.applicationId in UserData/MelonPreferences.cfg.");
                return;
            }

            RegisterSteamJoin(clientId);
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Entanglement Discord Invite" };
            thread.Start();
        }

        public static void Stop() {
            running = false;
            sending = false;
            connected = false;
            lock (pipeLock) {
                try { pipe?.Dispose(); } catch { }
                pipe = null;
            }
            try { thread?.Join(1000); } catch { }
        }

        public static void SetSolo(string level) {
            level = CleanScene(level);
            lock (gate) {
                if (!inLobby && scene == level && revision > 0)
                    return;
                inLobby = false;
                lobbyId = 0;
                scene = level;
                statusLine = PlayingLine(level, false, 0, 0);
                revision++;
                dirty = true;
            }
        }

        public static void SetLobby(ulong id, int members, int capacity, bool isLocked, string level, ServerVisibility vis) {
            if (capacity < 1)
                capacity = 1;
            if (members < 1)
                members = 1;
            if (capacity < members)
                capacity = members;
            level = CleanScene(level);
            bool listed = !isLocked && vis != ServerVisibility.Private;

            lock (gate) {
                if (inLobby && lobbyId == id && current == members && limit == capacity && locked == isLocked && visibility == vis && scene == level)
                    return;

                if (lobbyId != id)
                    startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                inLobby = true;
                lobbyId = id;
                current = members;
                limit = capacity;
                locked = isLocked;
                visibility = vis;
                scene = level;
                statusLine = PlayingLine(level, listed, members, capacity);
                activityMode = 0;
                revision++;
                dirty = true;
                lastSent = 0;
            }
        }

        public static void DrainJoins() {
            while (notes.TryDequeue(out string note)) {
                if (!string.IsNullOrEmpty(note))
                    EntangleLogger.Debug(note);
            }

            while (warnings.TryDequeue(out string warning)) {
                if (!string.IsNullOrEmpty(warning))
                    EntangleLogger.Warn(warning);
            }

            while (toasts.TryDequeue(out string toast)) {
                if (!string.IsNullOrEmpty(toast))
                    GameNotify.Show(toast, 4f);
            }

            // Long enough to read.
            while (longToasts.TryDequeue(out string longToast)) {
                if (!string.IsNullOrEmpty(longToast))
                    GameNotify.Show(longToast, 10f);
            }

            while (joins.TryDequeue(out ulong id)) {
                if (id == 0)
                    continue;
                TryJoinLobby(id);
            }

            if (pendingJoinLobby != 0)
                TryJoinLobby(pendingJoinLobby);
        }

        static void TryJoinLobby(ulong id) {
            if (id == 0)
                return;

            if (Client.instance == null) {
                pendingJoinLobby = id;
                return;
            }

            if (pendingJoinAt > 0f && UnityEngine.Time.unscaledTime < pendingJoinAt) {
                pendingJoinLobby = id;
                return;
            }

            if (SteamIntegration.hasLobby) {
                if (SteamIntegration.lobby.m_SteamID == id) {
                    pendingJoinLobby = 0;
                    GameNotify.Show("You're already in this server.", 3f);
                    return;
                }

                // Leave the current lobby so Discord Ask-to-Join can finish connecting.
                pendingJoinLobby = id;
                pendingJoinAt = UnityEngine.Time.unscaledTime + LeaveSettleSeconds;
                GameNotify.Show("Leaving this game to join from Discord.", 4f);
                EntangleLogger.Log("[Discord] Leaving this game to join another one from Discord.");
                if (Server.instance != null)
                    Server.instance.Shutdown();
                else
                    Client.instance.DisconnectFromServer(false);
                return;
            }

            pendingJoinLobby = 0;
            pendingJoinAt = 0f;
            GameNotify.Show("Joining from Discord.", 3f);
            Client.instance.JoinLobby(new CSteamID(id));
        }

        static void Run() {
            while (running) {
                try {
                    NamedPipeClientStream next = Connect();
                    if (next == null) {
                        if (!loggedMissing) {
                            loggedMissing = true;
                            Note("Discord isn't running. Status stays on Steam until Discord is open.");
                        }
                        Sleep(5000);
                        continue;
                    }

                    lock (pipeLock)
                        pipe = next;

                    if (!SendHandshake()) {
                        if (failedClient != clientId) {
                            failedClient = clientId;
                            Note("Discord did not accept the Entanglement Redux application. Check the Application ID.");
                        }
                        Drop();
                        Sleep(5000);
                        continue;
                    }

                    WriteFrame(Frame, "{\"cmd\":\"SUBSCRIBE\",\"evt\":\"ACTIVITY_JOIN\",\"nonce\":\"join\"}");
                    WriteFrame(Frame, "{\"cmd\":\"SUBSCRIBE\",\"evt\":\"ACTIVITY_JOIN_REQUEST\",\"nonce\":\"ask\"}");

                    connected = true;
                    if (!loggedReady) {
                        loggedReady = true;
                        Note("Discord status connected as Entanglement Redux.");
                    }

                    lastSent = 0;
                    sendDelay = 0;
                    dirty = true;
                    sending = true;
                    Pump();
                }
                catch (Exception e) {
                    if (running)
                        EntangleLogger.Warn("Discord invite pipe closed: " + e.Message);
                }
                finally {
                    sending = false;
                    connected = false;
                    Drop();
                }

                if (running)
                    Sleep(2000);
            }
        }

        static NamedPipeClientStream Connect() {
            for (int i = 0; i < 10 && running; i++) {
                var next = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try {
                    next.Connect(150);
                    return next;
                }
                catch {
                    try { next.Dispose(); } catch { }
                }
            }

            return null;
        }

        static string clientId = "";

        static bool SendHandshake() {
            WriteFrame(Handshake, "{\"v\":1,\"client_id\":\"" + clientId + "\"}");
            PipeRead read = ReadFrame(out int opcode, out string reply, 2500);
            return read == PipeRead.Data && reply != null && opcode != Close;
        }

        static void Pump() {
            while (running && pipe != null && pipe.IsConnected) {
                CheckRivals();
                TrySend();
                TryInviteDialog();

                PipeRead read = ReadFrame(out int opcode, out string json, 250);
                if (read == PipeRead.Timeout)
                    continue;
                if (read != PipeRead.Data || json == null)
                    return;
                if (opcode == Close)
                    return;
                if (opcode == Ping) {
                    WriteFrame(Pong, json);
                    continue;
                }
                Handle(json);
            }
        }

        static void Handle(string json) {
            if (string.IsNullOrEmpty(json))
                return;

            string evt = JsonString(json, "evt");
            if (evt == "ACTIVITY_JOIN") {
                ulong id = ParseLobbySecret(JsonString(json, "secret"));
                if (id != 0)
                    joins.Enqueue(id);
                return;
            }

            if (evt == "ACTIVITY_JOIN_REQUEST") {
                string userId = UserId(json);
                string name = JsonString(json, "username");
                if (string.IsNullOrEmpty(userId))
                    return;
                string who = string.IsNullOrEmpty(name) ? "Someone" : name;
                AskAnswer answer = AnswerAsk();
                if (answer == AskAnswer.Accept) {
                    SendJoinInvite(userId);
                    toasts.Enqueue(who + " wants to join. Letting them in!");
                }
                else if (answer == AskAnswer.Host) {
                    // Private: invite only. Discord shows the host the request to accept or not.
                    toasts.Enqueue(who + " asked to join on Discord. Accept or decline it in Discord.");
                }
                else {
                    CloseJoinRequest(userId);
                    toasts.Enqueue("Turned down a Discord join request: the server is full or locked.");
                }
                return;
            }

            if (evt == "ERROR") {
                string message = JsonString(json, "message") ?? "unknown";
                string lower = message.ToLower();
                if (logoMode < 2 && (lower.Contains("image") || lower.Contains("asset") || lower.Contains("url"))) {
                    logoMode++;
                    lastSent = 0;
                    sendDelay = 0;
                    dirty = true;
                    Note(logoMode == 1
                        ? "Discord rejected the portal logo. Trying the hosted image."
                        : "Discord rejected the logo. Status text is still on.");
                    return;
                }
                if (activityMode < 1 && (lower.Contains("privacy") || lower.Contains("party") || lower.Contains("secret") || lower.Contains("join") || lower.Contains("instance"))) {
                    activityMode = 1;
                    lastSent = 0;
                    sendDelay = 0;
                    dirty = true;
                    Note("Discord rejected the party privacy field (" + message + "). Retrying the join secret.");
                    return;
                }
                if (message != lastError) {
                    lastError = message;
                    Note("Discord status update failed: " + lastError);
                }
                sendDelay = 15000;
                dirty = true;
            }
        }

        static void TrySend() {
            if (!sending || !running)
                return;

            int now = Environment.TickCount;
            int rev;
            string json;
            bool lobby;
            lock (gate) {
                // Nothing changed for a while: send it again anyway (see the class notes).
                int refresh = inLobby || rival != null ? ContestedRefreshMs : RefreshMs;
                if (!dirty && lastSent != 0 && unchecked(now - lastSent) >= refresh)
                    dirty = true;
                if (!dirty)
                    return;
                if (lastSent != 0 && unchecked(now - lastSent) < sendDelay)
                    return;
                rev = revision;
                lobby = inLobby;
                json = BuildActivity();
            }

            try {
                WriteFrame(Frame, json);
            }
            catch (Exception e) {
                dirty = true;
                Note("Discord status send failed: " + e.Message);
                return;
            }

            lock (gate) {
                if (revision == rev)
                    dirty = false;
            }

            bool first = lastSent == 0;
            lastSent = now;
            sendDelay = 4000;
            if (lobby && first)
                Note("Discord lobby status sent.");
        }

        /// <summary>The name of a running app known to cover our Discord status, or null.</summary>
        public static string RivalApp => rival;

        // Every 30 s, on this thread: is anything running that sets its own Discord status?
        static void CheckRivals() {
            int now = Environment.TickCount;
            if (nextRivalCheck != 0 && unchecked(now - nextRivalCheck) < 0)
                return;
            nextRivalCheck = now + RivalCheckMs;

            string found = null;
            for (int i = 0; i < rivals.Length && found == null; i++) {
                System.Diagnostics.Process[] running = null;
                try {
                    running = System.Diagnostics.Process.GetProcessesByName(rivals[i]);
                    if (running != null && running.Length > 0)
                        found = rivals[i];
                }
                catch { }
                finally {
                    if (running != null)
                        for (int j = 0; j < running.Length; j++)
                            try { running[j].Dispose(); } catch { }
                }
            }

            if (found != rival) {
                Note(found != null
                    ? "[Discord] " + found + " is running: sending our status every " + (ContestedRefreshMs / 1000) + " s to stay on top of its own"
                    : "[Discord] " + rival + " closed");
                rival = found;
            }

            // Once, and in a lobby, where invites matter.
            bool lobby;
            lock (gate)
                lobby = inLobby;
            if (found != null && lobby && !toldRival) {
                toldRival = true;
                string message = "You currently have " + found + " running. " + found + " can take over Entanglement Redux's "
                    + "Discord status (rich presence), so Discord invites may not work. Please close " + found
                    + " to invite friends through Discord.";
                warnings.Enqueue("[Discord] " + message);
                longToasts.Enqueue(message);
            }
        }

        static void TryInviteDialog() {
            if (!inviteRequested || !sending || !running)
                return;
            inviteRequested = false;
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            try {
                WriteFrame(Frame, "{\"cmd\":\"OPEN_INVITE_DIALOG\",\"nonce\":\"invite\",\"args\":{\"pid\":" + pid + "}}");
                Note("Discord invite window opened.");
            }
            catch {
                Note("Couldn't open the Discord invite window.");
            }
        }

        static string BuildActivity() {
            string token = (++nonce).ToString();
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var fields = new System.Collections.Generic.List<string>();
            fields.Add("\"type\":0");
            if (logoMode == 0)
                fields.Add("\"assets\":{\"large_image\":\"" + LogoAsset + "\",\"large_text\":\"Entanglement Redux\"}");
            else if (logoMode == 1)
                fields.Add("\"assets\":{\"large_image\":\"" + LogoUrl + "\",\"large_text\":\"Entanglement Redux\"}");

            bool canJoin = allowJoin && activityMode < 2 && inLobby && !locked && current < limit;
            string line = string.IsNullOrEmpty(statusLine) ? "Playing Entanglement Redux" : statusLine;
            if (line.Length < 2)
                line = "Playing Entanglement Redux";
            fields.Add("\"details\":\"" + Esc(line) + "\"");

            if (inLobby)
                fields.Add("\"state\":\"" + current + "/" + limit + "\"");
            else if (!string.IsNullOrEmpty(scene) && scene.Length >= 2 && (statusLine == null || statusLine.IndexOf(scene, StringComparison.Ordinal) < 0))
                fields.Add("\"state\":\"" + Esc(scene) + "\"");

            if (inLobby) {
                string partyId = lobbyId.ToString();
                string size = "\"size\":[" + current + "," + limit + "]";

                if (canJoin && activityMode == 0)
                    fields.Add("\"party\":{\"id\":\"" + partyId + "\"," + size + ",\"privacy\":"
                        + (visibility == ServerVisibility.Public ? "1" : "0") + "}");
                else
                    fields.Add("\"party\":{\"id\":\"" + partyId + "\"," + size + "}");

                if (canJoin) {
                    fields.Add("\"secrets\":{\"join\":\"L" + partyId + "\"}");
                    fields.Add("\"instance\":true");
                }
            }

            if (inLobby && startedAt > 0)
                fields.Add("\"timestamps\":{\"start\":" + (startedAt / 1000) + "}");

            return "{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"" + token + "\",\"args\":{\"pid\":" + pid + ",\"activity\":{" + string.Join(",", fields) + "}}}";
        }

        public static string PlayingLine(string level, bool showPlayers, int members, int capacity) {
            level = CleanScene(level);
            if (showPlayers && capacity > 0) {
                string count = members + "/" + capacity;
                if (!string.IsNullOrEmpty(level))
                    return "Playing Entanglement Redux - (" + level + " - " + count + ")";
                return "Playing Entanglement Redux - (" + count + ")";
            }
            if (!string.IsNullOrEmpty(level))
                return "Playing Entanglement Redux - " + level;
            return "Playing Entanglement Redux";
        }

        static string CleanScene(string level) {
            if (string.IsNullOrEmpty(level) || level == "undefined")
                return "";
            if (level.Length > 64)
                return level.Substring(0, 64);
            return level;
        }

        static void RegisterSteamJoin(string applicationId) {
            try {
                string steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
                if (string.IsNullOrEmpty(steam))
                    return;

                steam = steam.Replace('/', '\\');
                string command = "\"" + steam + "\" steam://rungameid/823500";
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\discord-" + applicationId)) {
                    if (key == null)
                        return;
                    key.SetValue("", "URL:Run game " + applicationId + " protocol");
                    key.SetValue("URL Protocol", "");
                    using (var icon = key.CreateSubKey("DefaultIcon"))
                        icon?.SetValue("", steam);
                    using (var open = key.CreateSubKey(@"shell\open\command"))
                        open?.SetValue("", command);
                }
            }
            catch (Exception e) {
                EntangleLogger.Warn("Couldn't register Discord join launch: " + e.Message);
            }
        }

        enum PipeRead { Data, Timeout, Closed }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PeekNamedPipe(Microsoft.Win32.SafeHandles.SafePipeHandle handle, IntPtr buffer, uint nBufferSize, IntPtr bytesRead, out uint totalBytesAvail, IntPtr bytesLeftThisMessage);

        static PipeRead ReadFrame(out int opcode, out string body, int timeoutMs) {
            opcode = Close;
            body = null;
            var header = new byte[8];
            PipeRead read = Fill(header, 8, timeoutMs);
            if (read != PipeRead.Data)
                return read;

            opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > 65536)
                return PipeRead.Closed;
            if (length == 0) {
                body = "";
                return PipeRead.Data;
            }

            var data = new byte[length];
            read = Fill(data, length, timeoutMs);
            if (read != PipeRead.Data)
                return read;
            body = Encoding.UTF8.GetString(data);
            return PipeRead.Data;
        }

        static PipeRead Fill(byte[] buffer, int count, int timeoutMs) {
            int got = 0;
            int start = Environment.TickCount;
            while (got < count && running) {
                int n;
                try {
                    lock (pipeLock) {
                        if (pipe == null || !pipe.IsConnected)
                            return PipeRead.Closed;
                        uint avail;
                        if (!PeekNamedPipe(pipe.SafePipeHandle, IntPtr.Zero, 0, IntPtr.Zero, out avail, IntPtr.Zero))
                            return PipeRead.Closed;
                        if (avail == 0)
                            n = 0;
                        else {
                            int take = count - got;
                            if (avail < (uint)take)
                                take = (int)avail;
                            n = pipe.Read(buffer, got, take);
                        }
                    }
                }
                catch {
                    return PipeRead.Closed;
                }

                if (n > 0) {
                    got += n;
                    continue;
                }

                if (timeoutMs <= 0 || unchecked(Environment.TickCount - start) > timeoutMs)
                    return PipeRead.Timeout;
                Thread.Sleep(20);
            }

            return got == count ? PipeRead.Data : PipeRead.Closed;
        }

        static void Note(string message) {
            if (!string.IsNullOrEmpty(message))
                notes.Enqueue(message);
        }

        static void WriteFrame(int opcode, string json) {
            byte[] body = Encoding.UTF8.GetBytes(json ?? "");
            var header = new byte[8];
            BitConverter.GetBytes(opcode).CopyTo(header, 0);
            BitConverter.GetBytes(body.Length).CopyTo(header, 4);

            lock (pipeLock) {
                if (pipe == null || !pipe.IsConnected)
                    throw new System.IO.IOException("Discord pipe is closed.");
                pipe.Write(header, 0, 8);
                pipe.Write(body, 0, body.Length);
                pipe.Flush();
            }
        }

        static void Drop() {
            lock (pipeLock) {
                try { pipe?.Dispose(); } catch { }
                pipe = null;
            }
        }

        static void Sleep(int ms) {
            int left = ms;
            while (running && left > 0) {
                int slice = left > 50 ? 50 : left;
                Thread.Sleep(slice);
                left -= slice;
            }
        }

        enum AskAnswer { Accept, Host, Decline }

        static AskAnswer AnswerAsk() {
            lock (gate) {
                if (!inLobby || locked || current >= limit)
                    return AskAnswer.Decline;
                if (visibility == ServerVisibility.Private)
                    return AskAnswer.Host;
                return AskAnswer.Accept;
            }
        }

        static void SendJoinInvite(string userId) {
            int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            WriteFrame(Frame, "{\"cmd\":\"SEND_ACTIVITY_JOIN_INVITE\",\"nonce\":\"yes\",\"args\":{\"user_id\":\"" + userId + "\",\"pid\":" + pid + "}}");
        }

        static void CloseJoinRequest(string userId) {
            WriteFrame(Frame, "{\"cmd\":\"CLOSE_ACTIVITY_JOIN_REQUEST\",\"nonce\":\"no\",\"args\":{\"user_id\":\"" + userId + "\"}}");
        }

        static ulong ParseLobbySecret(string secret) {
            if (string.IsNullOrEmpty(secret))
                return 0;
            int start = 0;
            while (start < secret.Length && !char.IsDigit(secret[start]))
                start++;
            ulong id = 0;
            for (int i = start; i < secret.Length; i++) {
                if (!char.IsDigit(secret[i]))
                    break;
                id = id * 10 + (ulong)(secret[i] - '0');
            }
            return id;
        }

        static string UserId(string json) {
            int user = json.IndexOf("\"user\"", StringComparison.Ordinal);
            string slice = user >= 0 ? json.Substring(user) : json;
            return JsonString(slice, "id") ?? JsonString(json, "user_id");
        }

        static string JsonString(string json, string key) {
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0)
                return null;

            int colon = json.IndexOf(':', i + needle.Length);
            if (colon < 0)
                return null;

            int quote = json.IndexOf('"', colon + 1);
            if (quote < 0)
                return null;

            var value = new StringBuilder();
            for (int p = quote + 1; p < json.Length; p++) {
                char c = json[p];
                if (c == '\\' && p + 1 < json.Length) {
                    value.Append(json[++p]);
                    continue;
                }
                if (c == '"')
                    return value.ToString();
                value.Append(c);
            }

            return null;
        }

        static string Esc(string value) {
            if (string.IsNullOrEmpty(value))
                return "";

            var text = new StringBuilder(value.Length);
            foreach (char c in value) {
                if (c == '\\' || c == '"')
                    text.Append('\\').Append(c);
                else if (c == '\n')
                    text.Append("\\n");
                else if (c >= 32)
                    text.Append(c);
            }
            return text.ToString();
        }
    }
}
