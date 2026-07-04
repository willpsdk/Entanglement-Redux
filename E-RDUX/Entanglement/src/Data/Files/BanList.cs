using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;

using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Data {
    public static class BanList {
        public static List<Tuple<long, string>> bannedUsers = new List<Tuple<long, string>>();

        public static string banlistPath;

        public static void PullFromFile() {
            XDocument InstantiateDefault(string verb = "missing") {
                EntangleLogger.Log($"Banlist was {verb}, created it!", ConsoleColor.DarkCyan);
                var defaultDocument = CreateDefault();
                File.WriteAllText(banlistPath, defaultDocument.ToString());

                return defaultDocument;
            }

            XDocument document = null;
            banlistPath = PersistentData.GetPath("banlist.xml");

            try {
                if (File.Exists(banlistPath))
                {
                    EntangleLogger.Log("Banlist was found, attempting to read it!", ConsoleColor.DarkCyan);
                    string raw = File.ReadAllText(banlistPath);
                    document = XDocument.Parse(raw);

                    if (document.Root.Name != "BanList")
                        throw new ArgumentException("Xml root wasn't BanList, recreating the xml...");
                }
            }
            catch (System.Exception exception) {
                EntangleLogger.Error($"Encountered error while parsing banlist: {exception.Message}, it must be recreated to ensure validity, sorry about that!");
                document = InstantiateDefault("malformed");
            }

            if (document == null)
                document = InstantiateDefault();

            if (document != null) {
                document.Descendants("Ban").ForEach((element) => {
                    if (element.TryGetAttribute("id", out string rawId) && element.TryGetAttribute("name", out string userName)) {
                        if (long.TryParse(rawId, out long id)) {
                            bannedUsers.Add(new Tuple<long, string>(id, userName));
                            EntangleLogger.Log($"Found banned id {id}", ConsoleColor.DarkRed);
                        }
                    }
                });
            }
        }

        public static XDocument CreateDefault() {
            XDocument banDocument = new XDocument();

            banDocument.Add(new XElement("BanList"));

            banDocument.Root.Add(new XComment("Example ban: <Ban id=71238129037854/>"));

            return banDocument;
        }

        public static void UpdateBanFile() {
            var baseDoc = CreateDefault();

            foreach (var tuple in bannedUsers) {
                XElement banEntry = new XElement("Ban");
                banEntry.SetAttributeValue("id", tuple.Item1);

                var userName = new XComment(tuple.Item2);

                baseDoc.Root.Add(userName);
                baseDoc.Root.Add(banEntry);
            }

            EntangleLogger.Log($"Banlist changed, updating the xml!", ConsoleColor.DarkCyan);
            File.WriteAllText(banlistPath, baseDoc.ToString());
        }

        public static void BanUser(long userId, string userName) {
            var tuple = new Tuple<long, string>(userId, userName);
            if (!bannedUsers.Contains(tuple))
                bannedUsers.Add(tuple);

            EntangleLogger.Log($"Banned {userName}, id is {userId}!", ConsoleColor.DarkRed);
            UpdateBanFile();
        }

        public static void UnbanUser(long userId, string userName) {
            var tuple = new Tuple<long, string>(userId, userName);
            if (bannedUsers.Contains(tuple))
                bannedUsers.Remove(tuple);

            EntangleLogger.Log($"Unbanned {userName}, id is {userId}!", ConsoleColor.DarkCyan);
            UpdateBanFile();
        }
    }
}
