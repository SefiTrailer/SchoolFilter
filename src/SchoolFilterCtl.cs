using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolFilter.Controller
{
    internal static class Program
    {
        private const string DefaultFirebaseProjectId = "school-filter-2026";
        private const string DefaultRoomId = "yeshiva-lab";
        private const int SinkholePort = 9999;

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SchoolFilter"
        );

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SchoolFilter"
        );

        private static readonly string StateFilePath = Path.Combine(DataDir, "state.txt");
        private static readonly string CachedPacFilePath = Path.Combine(DataDir, "cached_room_filter.pac");

        // Dynamic Room State served by local Sinkhole + PAC Server on 127.0.0.1:9999
        private static volatile string CurrentPacScript = null;
        private static volatile string CurrentRoomDisplayLabel = "ישיבת נשמת התורה — חדר מחשבים";
        private static List<string> CurrentRoomWhitelist = new List<string>
        {
            "one-class.co.il",
            "gemini.google.com",
            "copilot.microsoft.com",
            "classroom.google.com",
            "edu.gov.il",
            "education.gov.il",
            "docs.google.com",
            "drive.google.com"
        };

        // Background telemetry/CDN domains that browsers request automatically without user navigation.
        private static readonly string[] SilentTelemetryDomains = new string[]
        {
            "googleapis.com", "gstatic.com", "google.com", "gvt1.com", "gvt2.com", "1e100.net",
            "microsoft.com", "windows.com", "windowsupdate.com", "live.com", "msn.com", "bing.com",
            "msedge.net", "office.com", "office365.com", "office.net", "skype.com", "sfx.ms",
            "azureedge.net", "trafficmanager.net", "visualstudio.com", "aka.ms",
            "cloudflare.com", "cloudflare-dns.com", "amazonaws.com", "akamai.net", "akamaihd.net",
            "edgekey.net", "edgesuite.net", "fastly.net", "digicert.com", "lencr.org", "sectigo.com",
            "verisign.com", "globalsign.com", "identrust.com", "pki.goog", "ocsp", "crl",
            "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com",
            "googletagmanager.com", "adnxs.com", "rubiconproject.com", "criteo.com", "taboola.com",
            "outbrain.com", "mozilla.org", "mozilla.com", "mozilla.net", "firefox.com",
            "opera.com", "brave.com", "localhost", "127.0.0.1"
        };

        // Installed desktop games & game launchers closed automatically during class
        private static readonly Dictionary<string, string> BlockedGameProcesses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RobloxPlayerBeta", "Roblox" },
            { "RobloxPlayerLauncher", "Roblox" },
            { "RobloxStudioBeta", "Roblox Studio" },
            { "Windows10Universal", "Roblox (Windows App)" },
            { "Minecraft", "Minecraft" },
            { "MinecraftLauncher", "Minecraft Launcher" },
            { "Minecraft.Windows", "Minecraft" },
            { "TLauncher", "Minecraft (TLauncher)" },
            { "Lunar Client", "Minecraft (Lunar Client)" },
            { "Badlion Client", "Minecraft (Badlion)" },
            { "FortniteClient-Win64-Shipping", "Fortnite" },
            { "FortniteLauncher", "Fortnite" },
            { "EpicGamesLauncher", "Epic Games" },
            { "RocketLeague", "Rocket League" },
            { "FallGuys_client_game", "Fall Guys" },
            { "steam", "Steam" },
            { "cs2", "Counter-Strike 2" },
            { "csgo", "Counter-Strike" },
            { "dota2", "Dota 2" },
            { "hl2", "Half-Life / Source Game" },
            { "gta5", "GTA V" },
            { "PlayGTAV", "GTA V" },
            { "FiveM", "FiveM (GTA RP)" },
            { "RiotClientServices", "Riot Games" },
            { "RiotClientUx", "Riot Games" },
            { "VALORANT", "Valorant" },
            { "VALORANT-Win64-Shipping", "Valorant" },
            { "LeagueClient", "League of Legends" },
            { "LeagueClientUx", "League of Legends" },
            { "League of Legends", "League of Legends" },
            { "Battle.net", "Battle.net" },
            { "Overwatch", "Overwatch" },
            { "Hearthstone", "Hearthstone" },
            { "EADesktop", "EA Play" },
            { "Origin", "Origin Games" },
            { "Among Us", "Among Us" },
            { "GenshinImpact", "Genshin Impact" },
            { "StarRail", "Honkai: Star Rail" },
            { "HD-Player", "BlueStacks Android Emulator" },
            { "Bluestacks", "BlueStacks" },
            { "LDPlayer", "LDPlayer Emulator" },
            { "Nox", "Nox Emulator" }
        };

        public const int INTERNET_OPTION_PER_CONNECTION_OPTION = 75;
        public const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        public const int INTERNET_OPTION_REFRESH = 37;

        public const int INTERNET_PER_CONN_FLAGS = 1;
        public const int INTERNET_PER_CONN_PROXY_SERVER = 2;
        public const int INTERNET_PER_CONN_PROXY_BYPASS = 3;
        public const int INTERNET_PER_CONN_AUTOCONFIG_URL = 4;

        public const int PROXY_TYPE_DIRECT = 0x00000001;
        public const int PROXY_TYPE_PROXY = 0x00000002;
        public const int PROXY_TYPE_AUTO_PROXY_URL = 0x00000004;
        public const int PROXY_TYPE_AUTO_DETECT = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct INTERNET_PER_CONN_OPTION
        {
            public int dwOption;
            public INTERNET_PER_CONN_OPTION_VALUE Value;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INTERNET_PER_CONN_OPTION_VALUE
        {
            [FieldOffset(0)]
            public int dwValue;
            [FieldOffset(0)]
            public IntPtr pszValue;
            [FieldOffset(0)]
            public System.Runtime.InteropServices.ComTypes.FILETIME ftValue;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct INTERNET_PER_CONN_OPTION_LIST
        {
            public int dwSize;
            public IntPtr pszConnection;
            public int dwOptionCount;
            public int dwOptionError;
            public IntPtr pOptions;
        }

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static volatile bool IsFilterCurrentlyEnforced = true;
        private static DateTime LastNotificationTime = DateTime.MinValue;
        private static string LastNotifiedTarget = "";
        private static readonly object NotificationLock = new object();

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string action = (args.Length > 0) ? args[0].Trim().ToLowerInvariant() : "block";

            try
            {
                if (action == "sinkhole")
                {
                    RunSinkholeAndCloudSyncDaemon();
                    return 0;
                }
                else if (action == "notify")
                {
                    string targetName = (args.Length > 1) ? args[1] : "אתר לא מורשה";
                    string isGame = (args.Length > 2) ? args[2] : "web";
                    string roomLabel = (args.Length > 3) ? args[3] : GetConfiguredRoomDisplayLabel();
                    string csvWhitelist = (args.Length > 4) ? args[4] : "one-class.co.il,gemini.google.com,copilot.microsoft.com,classroom.google.com,edu.gov.il";
                    Application.Run(new ClassroomBlockNotificationForm(targetName, isGame == "game", roomLabel, csvWhitelist));
                    return 0;
                }
                else if (action == "watchdog")
                {
                    EnsureSinkholeRunning();
                    return 0;
                }
                else if (action == "stop-sinkhole")
                {
                    StopSinkholeServer();
                    return 0;
                }
                else if (action == "allow" || action == "unblock" || action == "off" || action == "restore")
                {
                    SaveLocalOverride("allow");
                    DisableFilter(false);
                }
                else if (action == "uninstall-cleanup")
                {
                    DisableFilter(true);
                }
                else
                {
                    SaveLocalOverride("auto");
                    EnableFilter();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static void SaveLocalOverride(string mode)
        {
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                File.WriteAllText(StateFilePath, mode);
            }
            catch {}
        }

        private static string ReadLocalOverride()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    return File.ReadAllText(StateFilePath).Trim().ToLowerInvariant();
                }
            }
            catch {}
            return "auto";
        }

        private static string GetConfigValue(string keyName, string defaultValue)
        {
            try
            {
                string configPath = Path.Combine(InstallDir, "config.ini");
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith(";") || line.StartsWith("#") || !line.Contains("="))
                            continue;

                        int idx = line.IndexOf('=');
                        string key = line.Substring(0, idx).Trim();
                        string val = line.Substring(idx + 1).Trim();
                        if (string.Equals(key, keyName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                        {
                            return val;
                        }
                    }
                }
            }
            catch {}
            return defaultValue;
        }

        private static string GetConfiguredRoomDisplayLabel()
        {
            string room = GetConfigValue("RoomName", "ישיבת נשמת התורה - חדר מחשבים");
            return room;
        }

        private static string GetLocalPacEndpointUrl(long cacheBuster)
        {
            return "http://127.0.0.1:" + SinkholePort + "/filter.pac?v=" + cacheBuster.ToString();
        }

        private static void EnableFilter()
        {
            EnsureSinkholeRunning();
            long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            RefreshPacSettings(ts);
            ConfigureGameFirewallRules(true);
        }

        private static void RefreshPacSettings(long cacheBuster)
        {
            string pacUrl = GetLocalPacEndpointUrl(cacheBuster);
            SetWinInetPac(true, pacUrl);
            ApplyPacToRegistryRoot(Registry.CurrentUser, true, pacUrl);
            ApplyPacToAllLoadedUsers(true, pacUrl);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        private static void DisableFilter(bool stopDaemon)
        {
            SetWinInetPac(false, "");
            ApplyPacToRegistryRoot(Registry.CurrentUser, false, "");
            ApplyPacToAllLoadedUsers(false, "");
            ConfigureGameFirewallRules(false);

            if (stopDaemon)
            {
                StopSinkholeServer();
            }

            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        private static void ConfigureGameFirewallRules(bool blockGames)
        {
            try
            {
                RunNetsh("advfirewall firewall delete rule name=\"SchoolFilter_BlockGameUDP\"");
                RunNetsh("advfirewall firewall delete rule name=\"SchoolFilter_BlockGameTCP\"");

                if (blockGames)
                {
                    RunNetsh("advfirewall firewall add rule name=\"SchoolFilter_BlockGameUDP\" dir=out action=block protocol=UDP remoteport=1024-11099,11401-65535");
                    RunNetsh("advfirewall firewall add rule name=\"SchoolFilter_BlockGameTCP\" dir=out action=block protocol=TCP remoteport=1119,3074,3724,5222,6667,7000-9100,19132-19133,25565,27000-27100,30120");
                }
            }
            catch {}
        }

        private static void RunNetsh(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh.exe", arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit(1500);
                }
            }
            catch {}
        }

        private static void EnsureSinkholeRunning()
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, SinkholePort, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(80))
                    {
                        client.EndConnect(ar);
                        return;
                    }
                }
            }
            catch {}

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(exePath, "sinkhole")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(50);
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, SinkholePort, null, null);
                            if (ar.AsyncWaitHandle.WaitOne(50))
                            {
                                client.EndConnect(ar);
                                break;
                            }
                        }
                    }
                    catch {}
                }
            }
            catch {}
        }

        private static void StopSinkholeServer()
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, SinkholePort, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(150))
                    {
                        client.EndConnect(ar);
                        NetworkStream stream = client.GetStream();
                        byte[] stopCmd = Encoding.ASCII.GetBytes("STOP_SINKHOLE\r\n\r\n");
                        stream.Write(stopCmd, 0, stopCmd.Length);
                        stream.Flush();
                    }
                }
            }
            catch {}
        }

        private static string LoadInitialPacScript()
        {
            try
            {
                if (File.Exists(CachedPacFilePath))
                {
                    string cached = File.ReadAllText(CachedPacFilePath, Encoding.ASCII);
                    if (!string.IsNullOrEmpty(cached)) return cached;
                }
                string installedPac = Path.Combine(InstallDir, "filter.pac");
                if (File.Exists(installedPac))
                {
                    return File.ReadAllText(installedPac, Encoding.ASCII);
                }
            }
            catch {}

            return GenerateDynamicPacScript(true, new List<string>
            {
                "one-class.co.il", "*.one-class.co.il",
                "gemini.google.com", "*.gemini.google.com",
                "copilot.microsoft.com", "*.copilot.microsoft.com",
                "edu.gov.il", "*.edu.gov.il",
                "education.gov.il", "*.education.gov.il",
                "classroom.google.com", "docs.google.com", "drive.google.com", "accounts.google.com"
            });
        }

        private static string GenerateDynamicPacScript(bool filterEnabled, List<string> userWhitelist)
        {
            List<string> effectiveList = new List<string>(userWhitelist);

            // If Google Gemini or Google Classroom is in the whitelist, include required Google static/auth sub-resources
            bool hasGoogle = effectiveList.Contains("gemini.google.com") || effectiveList.Contains("classroom.google.com");
            if (hasGoogle)
            {
                string[] googleHelpers = new string[]
                {
                    "accounts.google.com", "*.gstatic.com", "*.googleapis.com",
                    "*.googleusercontent.com", "*.clients6.google.com", "apis.google.com", "ogs.google.com"
                };
                foreach (string h in googleHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // If Microsoft Copilot is in the whitelist, include required Copilot/Bing/Microsoft auth sub-resources
            if (effectiveList.Contains("copilot.microsoft.com"))
            {
                string[] copilotHelpers = new string[]
                {
                    "sydney.bing.com", "edgeservices.bing.com", "*.bing.net",
                    "login.live.com", "login.microsoftonline.com", "*.msauth.net", "*.msftauth.net"
                };
                foreach (string h in copilotHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("function FindProxyForURL(url, host) {");
            sb.AppendLine("    host = host.toLowerCase();");
            sb.AppendLine("    var FILTER_ENABLED = " + (filterEnabled ? "true" : "false") + ";");
            sb.AppendLine("    if (!FILTER_ENABLED) { return \"DIRECT\"; }");
            sb.AppendLine("    if (isPlainHostName(host) || shExpMatch(host, \"*.local\") || shExpMatch(host, \"localhost\") || shExpMatch(host, \"127.*\") || shExpMatch(host, \"10.*\") || shExpMatch(host, \"192.168.*\") || shExpMatch(host, \"172.16.*\") || shExpMatch(host, \"172.17.*\") || shExpMatch(host, \"172.18.*\") || shExpMatch(host, \"172.19.*\") || shExpMatch(host, \"172.2*.*\") || shExpMatch(host, \"172.30.*\") || shExpMatch(host, \"172.31.*\")) {");
            sb.AppendLine("        return \"DIRECT\";");
            sb.AppendLine("    }");
            sb.AppendLine("    var whitelist = [");
            for (int i = 0; i < effectiveList.Count; i++)
            {
                string item = effectiveList[i].Replace("\"", "").Trim().ToLowerInvariant();
                string comma = (i < effectiveList.Count - 1) ? "," : "";
                sb.AppendLine("        \"" + item + "\"" + comma);
            }
            sb.AppendLine("    ];");
            sb.AppendLine("    for (var i = 0; i < whitelist.length; i++) {");
            sb.AppendLine("        var pattern = whitelist[i].toLowerCase();");
            sb.AppendLine("        if (shExpMatch(host, pattern)) { return \"DIRECT\"; }");
            sb.AppendLine("        if (pattern.indexOf(\"*.\") === 0) {");
            sb.AppendLine("            var baseDomain = pattern.substring(2);");
            sb.AppendLine("            if (host === baseDomain) { return \"DIRECT\"; }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("    return \"PROXY 127.0.0.1:9999\";");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static List<string> ExtractCleanBaseDomains(List<string> rawWhitelist)
        {
            List<string> baseDomains = new List<string>();
            if (rawWhitelist == null) return baseDomains;
            foreach (string item in rawWhitelist)
            {
                string clean = item.Trim().ToLowerInvariant();
                if (clean.StartsWith("*.")) clean = clean.Substring(2);
                if (string.IsNullOrEmpty(clean) || clean == "accounts.google.com") continue;
                if (!baseDomains.Contains(clean))
                {
                    baseDomains.Add(clean);
                }
            }
            return baseDomains;
        }

        private static bool ParseFirestoreRoomJson(string json, out bool filterEnabled, out List<string> whitelist, out string roomLabel)
        {
            filterEnabled = true;
            whitelist = new List<string>();
            roomLabel = GetConfiguredRoomDisplayLabel();

            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                Match boolMatch = Regex.Match(json, "\"filterEnabled\"\\s*:\\s*\\{\\s*\"booleanValue\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                if (boolMatch.Success)
                {
                    filterEnabled = string.Equals(boolMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                }

                string rName = "";
                Match rMatch = Regex.Match(json, "\"name\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                if (rMatch.Success) rName = rMatch.Groups[1].Value;
                if (!string.IsNullOrEmpty(rName))
                {
                    roomLabel = rName;
                }

                int wlIdx = json.IndexOf("\"whitelist\"", StringComparison.OrdinalIgnoreCase);
                if (wlIdx >= 0)
                {
                    int arrEnd = json.IndexOf("]", wlIdx);
                    if (arrEnd > wlIdx)
                    {
                        string wlSection = json.Substring(wlIdx, arrEnd - wlIdx);
                        MatchCollection matches = Regex.Matches(wlSection, "\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                        foreach (Match m in matches)
                        {
                            string domain = m.Groups[1].Value.Trim().ToLowerInvariant();
                            if (!string.IsNullOrEmpty(domain))
                            {
                                string baseDom = domain.StartsWith("*.") ? domain.Substring(2) : domain;
                                if (!whitelist.Contains(baseDom)) whitelist.Add(baseDom);
                                if (!whitelist.Contains("*." + baseDom)) whitelist.Add("*." + baseDom);
                            }
                        }
                    }
                }

                if (whitelist.Count == 0)
                {
                    whitelist.AddRange(new string[]
                    {
                        "one-class.co.il", "*.one-class.co.il",
                        "gemini.google.com", "*.gemini.google.com",
                        "copilot.microsoft.com", "*.copilot.microsoft.com",
                        "edu.gov.il", "*.edu.gov.il",
                        "classroom.google.com"
                    });
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RunSinkholeAndCloudSyncDaemon()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\SchoolFilterSinkholeMutex", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                CurrentPacScript = LoadInitialPacScript();
                CurrentRoomDisplayLabel = GetConfiguredRoomDisplayLabel();

                TcpListener listener = null;
                bool running = true;

                Thread cloudSyncThread = new Thread(() =>
                {
                    string lastGeneratedPac = null;
                    bool lastCloudFilterEnabled = true;
                    bool lastAppliedFirewallState = false;
                    long currentCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

                    int tickCounter = 0;
                    while (running)
                    {
                        try
                        {
                            if (tickCounter % 2 == 0)
                            {
                                string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                                string roomId = GetConfigValue("RoomId", DefaultRoomId);
                                long ticks = DateTime.UtcNow.Ticks;
                                string firestoreUrl = "https://firestore.googleapis.com/v1/projects/" + projectId +
                                                      "/databases/(default)/documents/rooms/" + Uri.EscapeDataString(roomId) +
                                                      "?nocache=" + ticks;

                                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(firestoreUrl);
                                req.Proxy = null;
                                req.Timeout = 4000;
                                req.UserAgent = "SchoolFilter-RoomDaemon/3.1";

                                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                                {
                                    string json = sr.ReadToEnd();
                                    bool cloudFilterEnabled;
                                    List<string> whitelist;
                                    string roomLabel;

                                    if (ParseFirestoreRoomJson(json, out cloudFilterEnabled, out whitelist, out roomLabel))
                                    {
                                        CurrentRoomDisplayLabel = roomLabel;
                                        CurrentRoomWhitelist = ExtractCleanBaseDomains(whitelist);
                                        string newPac = GenerateDynamicPacScript(cloudFilterEnabled, whitelist);
                                        CurrentPacScript = newPac;

                                        bool pacChanged = (lastGeneratedPac != null && lastGeneratedPac != newPac);
                                        if (pacChanged || lastGeneratedPac == null)
                                        {
                                            try
                                            {
                                                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                                                File.WriteAllText(CachedPacFilePath, newPac, Encoding.ASCII);
                                            }
                                            catch {}
                                        }

                                        if (pacChanged)
                                        {
                                            currentCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                                            if (cloudFilterEnabled != lastCloudFilterEnabled)
                                            {
                                                SaveLocalOverride("auto");
                                            }
                                        }

                                        lastGeneratedPac = newPac;
                                        lastCloudFilterEnabled = cloudFilterEnabled;

                                        if (pacChanged && cloudFilterEnabled && ReadLocalOverride() != "allow")
                                        {
                                            RefreshPacSettings(currentCacheBuster);
                                        }
                                    }
                                }
                            }

                            string localOverride = ReadLocalOverride();
                            bool shouldEnforceBlock = (localOverride == "allow") ? false : lastCloudFilterEnabled;
                            IsFilterCurrentlyEnforced = shouldEnforceBlock;

                            if (shouldEnforceBlock)
                            {
                                if (!IsProxyCurrentlyEnforced())
                                {
                                    RefreshPacSettings(currentCacheBuster);
                                }
                                if (!lastAppliedFirewallState)
                                {
                                    ConfigureGameFirewallRules(true);
                                    lastAppliedFirewallState = true;
                                }

                                EnforceInstalledGameBlock();
                            }
                            else
                            {
                                if (IsProxyCurrentlyEnforced())
                                {
                                    DisableFilter(false);
                                }
                                if (lastAppliedFirewallState)
                                {
                                    ConfigureGameFirewallRules(false);
                                    lastAppliedFirewallState = false;
                                }
                            }
                        }
                        catch {}

                        tickCounter++;
                        for (int i = 0; i < 5 && running; i++)
                        {
                            Thread.Sleep(500);
                        }
                    }
                });
                cloudSyncThread.IsBackground = true;
                cloudSyncThread.Start();

                try
                {
                    listener = new TcpListener(IPAddress.Loopback, SinkholePort);
                    listener.Start();

                    while (running)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        ThreadPool.QueueUserWorkItem(state =>
                        {
                            TcpClient c = (TcpClient)state;
                            try
                            {
                                c.ReceiveTimeout = 1000;
                                c.SendTimeout = 1000;
                                NetworkStream stream = c.GetStream();
                                byte[] buf = new byte[1024];
                                int read = 0;
                                try
                                {
                                    read = stream.Read(buf, 0, buf.Length);
                                }
                                catch {}

                                string req = (read > 0) ? Encoding.ASCII.GetString(buf, 0, read) : "";
                                if (req.StartsWith("STOP_SINKHOLE", StringComparison.Ordinal))
                                {
                                    running = false;
                                    try { listener.Stop(); } catch {}
                                    return;
                                }

                                if (req.StartsWith("GET /filter.pac", StringComparison.OrdinalIgnoreCase) ||
                                    req.StartsWith("HEAD /filter.pac", StringComparison.OrdinalIgnoreCase))
                                {
                                    string pacContent = CurrentPacScript ?? LoadInitialPacScript();
                                    byte[] pacBytes = Encoding.ASCII.GetBytes(pacContent);
                                    string pacHeader = "HTTP/1.1 200 OK\r\n" +
                                                       "Content-Type: application/x-ns-proxy-autoconfig\r\n" +
                                                       "Content-Length: " + pacBytes.Length + "\r\n" +
                                                       "Cache-Control: no-store, no-cache, must-revalidate, max-age=0\r\n" +
                                                       "Connection: close\r\n\r\n";
                                    byte[] pacHeaderBytes = Encoding.ASCII.GetBytes(pacHeader);
                                    stream.Write(pacHeaderBytes, 0, pacHeaderBytes.Length);
                                    stream.Write(pacBytes, 0, pacBytes.Length);
                                    stream.Flush();
                                    return;
                                }

                                bool isDirectInfoPage = false;
                                string blockedDomain = ExtractDomainFromHttpRequest(req, out isDirectInfoPage);

                                if (!isDirectInfoPage && IsFilterCurrentlyEnforced && !string.IsNullOrEmpty(blockedDomain))
                                {
                                    MaybeShowBrowserBlockNotification(blockedDomain);
                                }

                                string htmlBody = BuildHebrewBlockPageHtml(blockedDomain, CurrentRoomDisplayLabel, CurrentRoomWhitelist);
                                byte[] bodyBytes = Encoding.UTF8.GetBytes(htmlBody);
                                string statusCode = isDirectInfoPage ? "200 OK" : "403 Forbidden";
                                string header = "HTTP/1.1 " + statusCode + "\r\n" +
                                                "Content-Type: text/html; charset=utf-8\r\n" +
                                                "Content-Length: " + bodyBytes.Length + "\r\n" +
                                                "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                                                "Connection: close\r\n\r\n";
                                byte[] headerBytes = Encoding.ASCII.GetBytes(header);

                                stream.Write(headerBytes, 0, headerBytes.Length);
                                stream.Write(bodyBytes, 0, bodyBytes.Length);
                                stream.Flush();
                            }
                            catch {}
                            finally
                            {
                                try { c.Close(); } catch {}
                            }
                        }, client);
                    }
                }
                catch {}
                finally
                {
                    running = false;
                    if (listener != null)
                    {
                        try { listener.Stop(); } catch {}
                    }
                }
            }
        }

        private static void EnforceInstalledGameBlock()
        {
            try
            {
                Process[] allProcs = Process.GetProcesses();
                foreach (Process p in allProcs)
                {
                    try
                    {
                        string procName = p.ProcessName;
                        string gameDisplayName = null;

                        if (BlockedGameProcesses.TryGetValue(procName, out gameDisplayName))
                        {
                        }
                        else if (string.Equals(procName, "javaw", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(procName, "java", StringComparison.OrdinalIgnoreCase))
                        {
                            string title = p.MainWindowTitle ?? "";
                            if (title.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("Lunar Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("Badlion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                title.IndexOf("TLauncher", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                gameDisplayName = "Minecraft";
                            }
                        }

                        if (!string.IsNullOrEmpty(gameDisplayName))
                        {
                            p.Kill();
                            TriggerPopupNotification(gameDisplayName, true);
                        }
                    }
                    catch {}
                    finally
                    {
                        try { p.Dispose(); } catch {}
                    }
                }
            }
            catch {}
        }

        private static string ExtractDomainFromHttpRequest(string req, out bool isDirectInfoPage)
        {
            isDirectInfoPage = false;
            if (string.IsNullOrEmpty(req)) return "";

            try
            {
                string[] lines = req.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return "";

                string firstLine = lines[0];
                if (firstLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = firstLine.Split(' ');
                    if (parts.Length >= 2)
                    {
                        string hostPort = parts[1].Trim();
                        int colonIdx = hostPort.IndexOf(':');
                        return (colonIdx > 0) ? hostPort.Substring(0, colonIdx) : hostPort;
                    }
                }

                if (firstLine.StartsWith("GET /", StringComparison.OrdinalIgnoreCase))
                {
                    isDirectInfoPage = true;
                    int siteIdx = firstLine.IndexOf("site=", StringComparison.OrdinalIgnoreCase);
                    if (siteIdx > 0)
                    {
                        string rest = firstLine.Substring(siteIdx + 5);
                        int endIdx = rest.IndexOfAny(new char[] { ' ', '&', '#' });
                        string rawSite = (endIdx > 0) ? rest.Substring(0, endIdx) : rest;
                        return Uri.UnescapeDataString(rawSite);
                    }
                    return "";
                }

                foreach (string line in lines)
                {
                    if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    {
                        string host = line.Substring(5).Trim();
                        int colonIdx = host.IndexOf(':');
                        string cleanHost = (colonIdx > 0) ? host.Substring(0, colonIdx) : host;
                        if (cleanHost != "127.0.0.1" && cleanHost != "localhost")
                        {
                            return cleanHost;
                        }
                    }
                }
            }
            catch {}

            return "";
        }

        private static void MaybeShowBrowserBlockNotification(string domain)
        {
            try
            {
                string cleanDomain = domain.Trim().ToLowerInvariant();
                if (cleanDomain.StartsWith("www."))
                {
                    cleanDomain = cleanDomain.Substring(4);
                }

                if (string.IsNullOrEmpty(cleanDomain)) return;

                foreach (string ignored in SilentTelemetryDomains)
                {
                    if (cleanDomain == ignored || cleanDomain.EndsWith("." + ignored, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                if (!IsForegroundWindowBrowser())
                {
                    return;
                }

                TriggerPopupNotification(cleanDomain, false);
            }
            catch {}
        }

        private static bool IsForegroundWindowBrowser()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return false;

                using (Process p = Process.GetProcessById((int)pid))
                {
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    return (name == "chrome" || name == "msedge" || name == "firefox" ||
                            name == "brave" || name == "opera" || name == "iexplore");
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TriggerPopupNotification(string targetName, bool isGame)
        {
            lock (NotificationLock)
            {
                DateTime now = DateTime.UtcNow;
                double secondsSinceLast = (now - LastNotificationTime).TotalSeconds;

                if (secondsSinceLast < 4.0) return;
                if (string.Equals(LastNotifiedTarget, targetName, StringComparison.OrdinalIgnoreCase) && secondsSinceLast < 8.0) return;

                LastNotificationTime = now;
                LastNotifiedTarget = targetName;
            }

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string safeArg = targetName.Replace("\"", "");
                string safeRoom = (CurrentRoomDisplayLabel ?? "").Replace("\"", "");
                string csvWhitelist = string.Join(",", CurrentRoomWhitelist.ToArray()).Replace("\"", "");
                ProcessStartInfo psi = new ProcessStartInfo(exePath, "notify \"" + safeArg + "\" " + (isGame ? "game" : "web") + " \"" + safeRoom + "\" \"" + csvWhitelist + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch {}
        }

        private static string GetFriendlySiteLabel(string domain)
        {
            if (domain == "one-class.co.il") return "🎓 One-Class (one-class.co.il)";
            if (domain == "gemini.google.com") return "✨ Google Gemini AI";
            if (domain == "copilot.microsoft.com") return "🤖 Microsoft Copilot AI";
            if (domain == "classroom.google.com") return "📖 Google Classroom";
            if (domain == "docs.google.com") return "📝 Google Docs";
            if (domain == "drive.google.com") return "📁 Google Drive";
            if (domain == "edu.gov.il" || domain == "education.gov.il") return "🏫 משרד החינוך (" + domain + ")";
            return "🌐 " + domain;
        }

        private static string BuildHebrewBlockPageHtml(string blockedDomain, string roomLabel, List<string> allowedDomains)
        {
            string displayDomain = string.IsNullOrEmpty(blockedDomain) ? "אתר לא מורשה" : WebUtility.HtmlEncode(blockedDomain);
            string displayRoom = string.IsNullOrEmpty(roomLabel) ? "כעת מתקיים שיעור בכיתה" : WebUtility.HtmlEncode(roomLabel);

            StringBuilder linksHtml = new StringBuilder();
            if (allowedDomains != null && allowedDomains.Count > 0)
            {
                foreach (string dom in allowedDomains)
                {
                    string safeDom = WebUtility.HtmlEncode(dom);
                    string label = WebUtility.HtmlEncode(GetFriendlySiteLabel(dom));
                    linksHtml.Append("<a class='btn' href='https://" + safeDom + "'>" + label + "</a>");
                }
            }

            return "<!DOCTYPE html>" +
                   "<html dir='rtl' lang='he'>" +
                   "<head>" +
                   "<meta charset='utf-8'>" +
                   "<meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                   "<title>האתר נחסם - כעת מתקיים שיעור | SchoolFilter</title>" +
                   "<style>" +
                   "*{box-sizing:border-box;font-family:'Segoe UI',system-ui,-apple-system,sans-serif}" +
                   "body{margin:0;min-height:100vh;background:linear-gradient(135deg,#0f172a 0%,#1e293b 100%);color:#0f172a;display:flex;align-items:center;justify-content:center;padding:20px;text-align:center}" +
                   ".card{background:#ffffff;max-width:620px;width:100%;border-radius:20px;padding:34px 30px;box-shadow:0 20px 50px rgba(0,0,0,0.35);border-top:8px solid #dc2626}" +
                   ".icon{width:68px;height:68px;background:#fef2f2;color:#dc2626;border-radius:50%;display:inline-flex;align-items:center;justify-content:center;font-size:34px;margin-bottom:14px;border:2px solid #fecaca}" +
                   "h1{color:#dc2626;margin:0 0 10px 0;font-size:25px;font-weight:800}" +
                   ".lesson-badge{display:inline-block;background:#fef3c7;color:#92400e;font-weight:700;padding:6px 16px;border-radius:999px;font-size:14px;margin-bottom:14px;border:1px solid #fde68a}" +
                   ".domain-box{background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:9px 16px;margin:10px 0 16px 0;font-family:Consolas,monospace;font-size:15px;color:#334155;direction:ltr;font-weight:600}" +
                   "p{color:#475569;font-size:15px;line-height:1.6;margin:0 0 18px 0}" +
                   ".wl-box{background:#f0fdf4;border:1px solid #bbf7d0;border-radius:14px;padding:16px;text-align:center}" +
                   ".links-title{font-size:14px;font-weight:800;color:#166534;margin-bottom:12px}" +
                   ".links{display:flex;flex-wrap:wrap;gap:8px;justify-content:center}" +
                   ".btn{text-decoration:none;background:#ffffff;color:#1d4ed8;border:1px solid #bfdbfe;padding:8px 14px;border-radius:10px;font-weight:700;font-size:13px;transition:all 0.15s;box-shadow:0 1px 2px rgba(0,0,0,0.04)}" +
                   ".btn:hover{background:#2563eb;color:#ffffff}" +
                   "</style>" +
                   "</head>" +
                   "<body>" +
                   "<div class='card'>" +
                   "<div class='icon'>🛑</div>" +
                   "<h1>הגלישה לאתר זה נחסמה</h1>" +
                   "<div class='lesson-badge'>📚 מצב שיעור פעיל: " + displayRoom + "</div>" +
                   "<div class='domain-box'>" + displayDomain + "</div>" +
                   "<p>הכניסה לאתר זה חסומה כעת מאחר שהמחשב נמצא <strong>במצב שיעור</strong>.<br>להלן <strong>הרשימה הלבנה</strong> של האתרים המותרים כעת בחדר שלך:</p>" +
                   "<div class='wl-box'>" +
                   "<div class='links-title'>✅ הרשימה הלבנה שלך לשיעור (לחץ על אתר לכניסה):</div>" +
                   "<div class='links'>" + linksHtml.ToString() + "</div>" +
                   "</div>" +
                   "</div>" +
                   "</body>" +
                   "</html>";
        }

        private static bool IsProxyCurrentlyEnforced()
        {
            try
            {
                string[] subKeyNames = Registry.Users.GetSubKeyNames();
                foreach (string sid in subKeyNames)
                {
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        using (RegistryKey connKey = Registry.Users.OpenSubKey(sid + @"\Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections", false))
                        {
                            if (connKey != null)
                            {
                                byte[] data = connKey.GetValue("DefaultConnectionSettings") as byte[];
                                if (data == null || data.Length < 12 || (data[8] & PROXY_TYPE_AUTO_PROXY_URL) == 0)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetWinInetPac(bool enable, string pacUrl)
        {
            try
            {
                INTERNET_PER_CONN_OPTION[] options = new INTERNET_PER_CONN_OPTION[2];
                options[0].dwOption = INTERNET_PER_CONN_FLAGS;
                options[0].Value.dwValue = enable
                    ? (PROXY_TYPE_AUTO_PROXY_URL | PROXY_TYPE_DIRECT)
                    : PROXY_TYPE_DIRECT;

                options[1].dwOption = INTERNET_PER_CONN_AUTOCONFIG_URL;
                options[1].Value.pszValue = Marshal.StringToHGlobalAuto(enable ? pacUrl : "");

                int optSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION));
                IntPtr optionsPtr = Marshal.AllocCoTaskMem(optSize * options.Length);

                for (int i = 0; i < options.Length; i++)
                {
                    IntPtr optPtr = new IntPtr(optionsPtr.ToInt64() + (i * optSize));
                    Marshal.StructureToPtr(options[i], optPtr, false);
                }

                INTERNET_PER_CONN_OPTION_LIST list = new INTERNET_PER_CONN_OPTION_LIST();
                list.dwSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION_LIST));
                list.pszConnection = IntPtr.Zero;
                list.dwOptionCount = options.Length;
                list.dwOptionError = 0;
                list.pOptions = optionsPtr;

                int listSize = Marshal.SizeOf(typeof(INTERNET_PER_CONN_OPTION_LIST));
                IntPtr listPtr = Marshal.AllocCoTaskMem(listSize);
                Marshal.StructureToPtr(list, listPtr, false);

                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, listPtr, listSize);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

                Marshal.FreeHGlobal(options[1].Value.pszValue);
                Marshal.FreeCoTaskMem(optionsPtr);
                Marshal.FreeCoTaskMem(listPtr);
            }
            catch {}
        }

        private static void ApplyPacToAllLoadedUsers(bool enable, string pacUrl)
        {
            try
            {
                string[] subKeyNames = Registry.Users.GetSubKeyNames();
                foreach (string sid in subKeyNames)
                {
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (RegistryKey userRoot = Registry.Users.OpenSubKey(sid, true))
                            {
                                if (userRoot != null)
                                {
                                    ApplyPacToRegistryRoot(userRoot, enable, pacUrl);
                                }
                            }
                        }
                        catch {}
                    }
                }
            }
            catch {}
        }

        private static void ApplyPacToRegistryRoot(RegistryKey rootKey, bool enable, string pacUrl)
        {
            try
            {
                using (RegistryKey inetKey = rootKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (inetKey != null)
                    {
                        if (enable)
                        {
                            inetKey.SetValue("AutoConfigURL", pacUrl, RegistryValueKind.String);
                            inetKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                            inetKey.SetValue("AutoDetect", 0, RegistryValueKind.DWord);
                        }
                        else
                        {
                            inetKey.DeleteValue("AutoConfigURL", false);
                            inetKey.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                        }
                    }
                }

                using (RegistryKey connKey = rootKey.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections"))
                {
                    if (connKey != null)
                    {
                        UpdateConnectionBinaryValue(connKey, "DefaultConnectionSettings", enable, pacUrl);
                        UpdateConnectionBinaryValue(connKey, "SavedLegacySettings", enable, pacUrl);
                    }
                }
            }
            catch {}
        }

        private static void UpdateConnectionBinaryValue(RegistryKey connKey, string valueName, bool enable, string pacUrl)
        {
            try
            {
                int version = 1;
                byte[] existing = connKey.GetValue(valueName) as byte[];
                if (existing != null && existing.Length >= 8)
                {
                    version = BitConverter.ToInt32(existing, 4) + 1;
                }

                byte[] urlBytes = enable ? Encoding.ASCII.GetBytes(pacUrl) : new byte[0];
                int flags = enable ? (PROXY_TYPE_DIRECT | PROXY_TYPE_AUTO_PROXY_URL) : PROXY_TYPE_DIRECT;

                byte[] blob = new byte[24 + urlBytes.Length + 32];
                Buffer.BlockCopy(BitConverter.GetBytes(0x46), 0, blob, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(version), 0, blob, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(flags), 0, blob, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, blob, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, blob, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(urlBytes.Length), 0, blob, 20, 4);
                if (urlBytes.Length > 0)
                {
                    Buffer.BlockCopy(urlBytes, 0, blob, 24, urlBytes.Length);
                }

                connKey.SetValue(valueName, blob, RegistryValueKind.Binary);
            }
            catch {}
        }
    }

    internal sealed class ClassroomBlockNotificationForm : Form
    {
        private System.Windows.Forms.Timer closeTimer;

        public ClassroomBlockNotificationForm(string targetName, bool isGame, string roomLabel, string csvWhitelist)
        {
            this.Text = "SchoolFilter - חסימת גלישה בזמן שיעור";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(540, 320);
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(220, 38, 38);
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;

            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(
                workingArea.Left + (workingArea.Width - this.Width) / 2,
                workingArea.Top + 65
            );

            Panel contentPanel = new Panel
            {
                Location = new Point(3, 6),
                Size = new Size(this.Width - 6, this.Height - 9),
                BackColor = Color.White
            };

            Label badgeLabel = new Label
            {
                Text = "📚 מצב שיעור: " + (string.IsNullOrEmpty(roomLabel) ? "ישיבת נשמת התורה" : roomLabel),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(146, 64, 14),
                BackColor = Color.FromArgb(254, 243, 199),
                AutoSize = false,
                Size = new Size(contentPanel.Width - 36, 26),
                Location = new Point(18, 12),
                TextAlign = ContentAlignment.MiddleCenter
            };

            Label titleLabel = new Label
            {
                Text = isGame
                    ? "🛑 המשחק נחסם ונסגר — כעת מתקיים שיעור!"
                    : "🛑 האתר נחסם לצפייה — כעת מתקיים שיעור!",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 38, 38),
                AutoSize = false,
                Size = new Size(contentPanel.Width - 24, 30),
                Location = new Point(12, 42),
                TextAlign = ContentAlignment.MiddleCenter
            };

            Label targetLabel = new Label
            {
                Text = isGame
                    ? "המשחק שנחסם: " + targetName
                    : "האתר שנחסם: " + targetName,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = false,
                Size = new Size(contentPanel.Width - 24, 22),
                Location = new Point(12, 74),
                TextAlign = ContentAlignment.MiddleCenter
            };

            Label wlTitleLabel = new Label
            {
                Text = "✅ הרשימה הלבנה שלך בחדר זה (לחץ על אתר לכניסה):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(22, 101, 52),
                AutoSize = false,
                Size = new Size(contentPanel.Width - 24, 22),
                Location = new Point(12, 100),
                TextAlign = ContentAlignment.MiddleCenter
            };

            FlowLayoutPanel sitesFlow = new FlowLayoutPanel
            {
                Location = new Point(18, 125),
                Size = new Size(contentPanel.Width - 36, 125),
                BackColor = Color.FromArgb(240, 253, 244),
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true,
                Padding = new Padding(6)
            };

            string[] domains = (csvWhitelist ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawDom in domains)
            {
                string dom = rawDom.Trim();
                if (string.IsNullOrEmpty(dom)) continue;
                Button siteBtn = new Button
                {
                    Text = dom,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    AutoSize = true,
                    Height = 28,
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(29, 78, 216),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(3)
                };
                siteBtn.FlatAppearance.BorderColor = Color.FromArgb(191, 219, 254);
                string targetUrl = "https://" + dom;
                siteBtn.Click += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true }); } catch {}
                    this.Close();
                };
                sitesFlow.Controls.Add(siteBtn);
            }

            Button openPortalBtn = new Button
            {
                Text = "📋 פתח רשימה מלאה בדפדפן",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Size = new Size(260, 36),
                Location = new Point(30, 260),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            openPortalBtn.FlatAppearance.BorderSize = 0;
            openPortalBtn.Click += (s, e) =>
            {
                try
                {
                    string url = "http://127.0.0.1:9999/?site=" + Uri.EscapeDataString(targetName);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch {}
                this.Close();
            };

            Button closeBtn = new Button
            {
                Text = "הבנתי, סגור",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Size = new Size(195, 36),
                Location = new Point(305, 260),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            closeBtn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            closeBtn.Click += (s, e) => this.Close();

            contentPanel.Controls.Add(badgeLabel);
            contentPanel.Controls.Add(titleLabel);
            contentPanel.Controls.Add(targetLabel);
            contentPanel.Controls.Add(wlTitleLabel);
            contentPanel.Controls.Add(sitesFlow);
            contentPanel.Controls.Add(openPortalBtn);
            contentPanel.Controls.Add(closeBtn);
            this.Controls.Add(contentPanel);

            closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = 9000;
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                this.Close();
            };
            closeTimer.Start();
        }
    }
}
