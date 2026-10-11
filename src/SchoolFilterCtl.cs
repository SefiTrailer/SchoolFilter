using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
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

        private static readonly string InstallDir = ResolveInstallDir();

        private static string ResolveInstallDir()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(exeDir) && File.Exists(Path.Combine(exeDir, "config.ini")))
                {
                    return exeDir;
                }
            }
            catch {}
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "SchoolFilter"
            );
        }

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SchoolFilter"
        );

        private static readonly string StateFilePath = Path.Combine(DataDir, "state.txt");
        private static readonly string CachedPacFilePath = Path.Combine(DataDir, "cached_room_filter.pac");
        private static readonly string LastCommandFilePath = Path.Combine(DataDir, "last_cmd.txt");
        private static readonly string LastSysCommandFilePath = Path.Combine(DataDir, "last_sys_cmd.txt");

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

        private static readonly string LastKilledGameFilePath = Path.Combine(DataDir, "last_killed_game.txt");

        // Background telemetry/CDN domains that browsers request automatically without user navigation.
        private static readonly string[] SilentTelemetryDomains = new string[]
        {
            "googleapis.com", "gstatic.com", "google.com", "gvt1.com", "gvt2.com", "1e100.net",
            "microsoft.com", "windows.com", "windowsupdate.com", "live.com", "msn.com", "bing.com",
            "msedge.net", "office.com", "office365.com", "office.net", "skype.com", "sfx.ms",
            "azureedge.net", "trafficmanager.net", "visualstudio.com", "aka.ms", "windows.net",
            "msftconnecttest.com", "msftncsi.com", "msauth.net", "msftauth.net", "bing.net",
            "cloudflare.com", "cloudflare-dns.com", "amazonaws.com", "akamai.net", "akamaihd.net",
            "akamaized.net", "cloudfront.net", "b-cdn.net", "fastlylb.net",
            "edgekey.net", "edgesuite.net", "fastly.net", "digicert.com", "lencr.org", "sectigo.com",
            "verisign.com", "globalsign.com", "identrust.com", "pki.goog", "ocsp", "crl",
            "doubleclick.net", "googlesyndication.com", "googleadservices.com", "google-analytics.com",
            "googletagmanager.com", "adnxs.com", "rubiconproject.com", "criteo.com", "taboola.com",
            "outbrain.com", "scorecardresearch.com", "amazon-adsystem.com", "casalemedia.com",
            "pubmatic.com", "openx.net", "demdex.net", "krxd.net", "rlcdn.com", "bidswitch.net",
            "sharethrough.com", "smartadserver.com", "clarity.ms", "hotjar.com", "sentry.io",
            "datadoghq.com", "newrelic.com", "jsdelivr.net", "unpkg.com", "fontawesome.com",
            "typekit.net", "wp.com", "gravatar.com", "ytimg.com", "ggpht.com", "fbcdn.net",
            "facebook.net", "licdn.com", "twimg.com", "pinimg.com", "yahoo.com", "yimg.com",
            "mozilla.org", "mozilla.com", "mozilla.net", "firefox.com",
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
            { "cstrike", "Counter-Strike 1.6" },
            { "dota2", "Dota 2" },
            { "hl2", "Half-Life / Source Game" },
            { "hl", "Half-Life" },
            { "gta5", "GTA V" },
            { "PlayGTAV", "GTA V" },
            { "gta_sa", "GTA San Andreas" },
            { "gta-vc", "GTA Vice City" },
            { " gta3", "GTA III" },
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
            { "dnplayer", "LDPlayer Emulator" },
            { "Nox", "Nox Emulator" },
            { "MEmu", "MEmu Emulator" },
            { "icytower", "Icy Tower" },
            { "icytower13", "Icy Tower" },
            { "icytower14", "Icy Tower" },
            { "icytower15", "Icy Tower" },
            { "lf2", "Little Fighter 2" },
            { "ChickenInvaders", "Chicken Invaders" },
            { "CI2", "Chicken Invaders 2" },
            { "CI3", "Chicken Invaders 3" },
            { "CI4", "Chicken Invaders 4" },
            { "CI5", "Chicken Invaders 5" },
            { "ra2", "Command & Conquer: Red Alert 2" },
            { "yuri", "Red Alert 2: Yuri's Revenge" },
            { "gamemd", "Red Alert 2" },
            { "generals", "Command & Conquer: Generals" },
            { "empires2", "Age of Empires II" },
            { "age2_x1", "Age of Empires II" },
            { "AoE2DE", "Age of Empires II" },
            { "AoE2DE_s", "Age of Empires II" },
            { "Stronghold", "Stronghold" },
            { "StrongholdCrusader", "Stronghold Crusader" },
            { "Crusader", "Stronghold Crusader" },
            { "Worms", "Worms" },
            { "WA", "Worms Armageddon" },
            { "WWP", "Worms World Party" },
            { "Claw", "Captain Claw" },
            { "jazz2", "Jazz Jackrabbit 2" },
            { "dave", "Dangerous Dave" },
            { "dosbox", "DOSBox Game Emulator" },
            { "retroarch", "RetroArch Emulator" },
            { "mame", "MAME Arcade Emulator" },
            { "project64", "Project64 Emulator" },
            { "dolphin", "Dolphin Emulator" },
            { "pcsx2", "PCSX2 Emulator" },
            { "ppsspp", "PPSSPP Emulator" },
            { "PPSSPPWindows", "PPSSPP Emulator" },
            { "PPSSPPWindows64", "PPSSPP Emulator" },
            { "GeometryDash", "Geometry Dash" },
            { "Undertale", "Undertale" },
            { "Cuphead", "Cuphead" },
            { "Terraria", "Terraria" },
            { "Brawlhalla", "Brawlhalla" },
            { "Hollow Knight", "Hollow Knight" },
            { "Celeste", "Celeste" },
            { "Subnautica", "Subnautica" },
            { "PlantsVsZombies", "Plants vs. Zombies" },
            { "popcapgame1", "Plants vs. Zombies" },
            { "Zuma", "Zuma" },
            { "Bejeweled", "Bejeweled" },
            { "dxball", "DX-Ball" },
            { "blobby", "Blobby Volley" },
            { "TrackMania", "TrackMania" },
            { "TmForever", "TrackMania" },
            { "eurotrucks2", "Euro Truck Simulator 2" },
            { "speed", "Need for Speed" },
            { "nfsw", "Need for Speed" },
            { "nfs", "Need for Speed" },
            { "fifa", "FIFA" },
            { "FC24", "EA Sports FC 24" },
            { "FC25", "EA Sports FC 25" },
            { "pes", "Pro Evolution Soccer" },
            { "NBA2K", "NBA 2K" },
            { "iw3sp", "Call of Duty 4" },
            { "iw3mp", "Call of Duty 4" },
            { "iw4sp", "Call of Duty: Modern Warfare 2" },
            { "iw4mp", "Call of Duty: Modern Warfare 2" },
            { "cod", "Call of Duty" },
            { "warcraft", "Warcraft" },
            { "war3", "Warcraft III" },
            { "starcraft", "StarCraft" },
            { "Solitaire", "Solitaire" },
            { "WinMine", "Minesweeper" },
            { "MineSweeper", "Minesweeper" },
            { "Pinball", "3D Pinball" },
            { "ChessTitans", "Chess Titans" },
            { "PurblePlace", "Purble Place" },
            { "FreeCell", "FreeCell" },
            { "SpiderSolitaire", "Spider Solitaire" },
            { "Hearts", "Hearts" },
            { "Mahjong", "Mahjong" }
        };

        // Known safe system, office, programming, and school processes that must never be killed by heuristic game checks
        private static readonly HashSet<string> SafeNonGameProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
            "fontdrvhost", "winlogon", "dwm", "spoolsv", "explorer", "sihost", "taskhostw", "taskmgr",
            "RuntimeBroker", "ApplicationFrameHost", "SystemSettings", "SearchHost", "SearchApp",
            "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "ctfmon", "dllhost",
            "WmiPrvSE", "audiodg", "conhost", "cmd", "powershell", "pwsh", "wt", "msiexec", "regedit",
            "mmc", "control", "schtasks", "netsh", "icacls", "taskkill", "ping", "wmic", "gpupdate",
            "SchoolFilterCtl", "SchoolFilter_Setup", "Uninstall",
            "chrome", "msedge", "firefox", "brave", "opera", "iexplore", "msedgewebview2",
            "GoogleUpdate", "MicrosoftEdgeUpdate",
            "winword", "excel", "powerpnt", "onenote", "outlook", "mspub", "msaccess", "lync", "Teams",
            "Acrobat", "AcroRd32", "FoxitReader", "SumatraPDF",
            "notepad", "notepad++", "Code", "devenv", "pycharm64", "idea64", "eclipse", "netbeans",
            "python", "pythonw", "node", "git", "dotnet", "csc",
            "veyon-service", "veyon-server", "veyon-worker", "veyon-master", "veyon-configurator", "veyon-cli",
            "AnyDesk", "TeamViewer", "TeamViewer_Service", "Zoom", "vlc", "wmplayer", "mspaint",
            "calc", "CalculatorApp", "SnippingTool", "ScreenSketch", "SecurityHealthService",
            "SecurityHealthSystray", "MsMpEng", "NisSrv", "OneDrive", " BarIlan", "Responsa",
            "Otzar", "OtzarHaHochma", "ToratEmet", "Tag", "InDesign", "Photoshop", "Illustrator"
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

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_CLOSE = 0x0010;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

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
                else if (action == "system-agent")
                {
                    RunSystemAgentDaemon();
                    return 0;
                }
                else if (action == "notify")
                {
                    bool createdPopupNew;
                    using (Mutex popupMutex = CreateGlobalMutex("Global\\SchoolFilterNotifyPopupMutex", out createdPopupNew))
                    {
                        if (popupMutex == null || !createdPopupNew)
                        {
                            return 0;
                        }
                        string targetName = (args.Length > 1) ? args[1] : "אתר לא מורשה";
                        string isGame = (args.Length > 2) ? args[2] : "web";
                        string roomLabel = (args.Length > 3) ? args[3] : GetConfiguredRoomDisplayLabel();
                        string csvWhitelist = (args.Length > 4) ? args[4] : "one-class.co.il,gemini.google.com,copilot.microsoft.com,classroom.google.com,edu.gov.il";
                        Application.Run(new ClassroomBlockNotificationForm(targetName, isGame == "game", roomLabel, csvWhitelist));
                    }
                    return 0;
                }
                else if (action == "watchdog")
                {
                    if (ReadLocalOverride() != "allow")
                    {
                        EnableFilter();
                    }
                    else
                    {
                        EnsureSinkholeRunning();
                    }
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
                    try
                    {
                        if (File.Exists(CachedPacFilePath))
                        {
                            string c = File.ReadAllText(CachedPacFilePath, Encoding.ASCII);
                            if (c.Contains("var FILTER_ENABLED = true;"))
                            {
                                File.WriteAllText(CachedPacFilePath, c.Replace("var FILTER_ENABLED = true;", "var FILTER_ENABLED = false;"), Encoding.ASCII);
                            }
                        }
                    }
                    catch {}
                    DisableFilter(false);
                }
                else if (action == "uninstall-cleanup")
                {
                    DisableFilter(true);
                }
                else
                {
                    SaveLocalOverride("block");
                    try
                    {
                        if (File.Exists(CachedPacFilePath))
                        {
                            string c = File.ReadAllText(CachedPacFilePath, Encoding.ASCII);
                            if (c.Contains("var FILTER_ENABLED = false;"))
                            {
                                File.WriteAllText(CachedPacFilePath, c.Replace("var FILTER_ENABLED = false;", "var FILTER_ENABLED = true;"), Encoding.ASCII);
                            }
                        }
                    }
                    catch {}
                    EnableFilter();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity.IsSystem) return true;
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static Mutex CreateGlobalMutex(string name, out bool createdNew)
        {
            createdNew = false;
            try
            {
                MutexSecurity security = new MutexSecurity();
                security.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Allow
                ));
                return new Mutex(true, name, out createdNew, security);
            }
            catch (UnauthorizedAccessException)
            {
                createdNew = false;
                return null;
            }
            catch
            {
                try
                {
                    return new Mutex(true, name, out createdNew);
                }
                catch
                {
                    createdNew = false;
                    return null;
                }
            }
        }

        private static bool IsSystemAgentRunning()
        {
            try
            {
                using (Mutex m = Mutex.OpenExisting("Global\\SchoolFilterSystemAgentMutex"))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSinkholeRunning()
        {
            try
            {
                using (Mutex m = Mutex.OpenExisting("Global\\SchoolFilterSinkholeMutex"))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                return false;
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

        private static void SaveLastExecutedCommandId(string cmdId, bool isSystemAgent)
        {
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                string path = isSystemAgent ? LastSysCommandFilePath : LastCommandFilePath;
                File.WriteAllText(path, cmdId ?? "");
            }
            catch {}
        }

        private static string ReadLastExecutedCommandId(bool isSystemAgent)
        {
            try
            {
                string path = isSystemAgent ? LastSysCommandFilePath : LastCommandFilePath;
                if (File.Exists(path))
                {
                    return File.ReadAllText(path).Trim();
                }
            }
            catch {}
            return "";
        }

        private static long GetInstallationTimestampMs()
        {
            try
            {
                string rawTs = GetConfigValue("InstalledAtMs", "");
                long parsed;
                if (!string.IsNullOrEmpty(rawTs) && long.TryParse(rawTs, out parsed) && parsed > 0)
                {
                    return parsed;
                }
                string configPath = Path.Combine(InstallDir, "config.ini");
                if (File.Exists(configPath))
                {
                    DateTime utcWrite = File.GetLastWriteTimeUtc(configPath);
                    return (long)(utcWrite - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                }
            }
            catch {}
            return 0;
        }

        private static bool ShouldExecuteRemoteCommand(string remoteCommand, string remoteCommandId, bool isSystemAgent)
        {
            if (string.IsNullOrEmpty(remoteCommand) || remoteCommand == "none") return false;
            if (remoteCommand != "uninstall" && remoteCommand != "update") return false;
            if (string.IsNullOrEmpty(remoteCommandId) || remoteCommandId == "0") return false;

            if (string.Equals(remoteCommandId, ReadLastExecutedCommandId(isSystemAgent), StringComparison.Ordinal))
            {
                return false;
            }

            long cmdTsMs;
            if (!long.TryParse(remoteCommandId, out cmdTsMs) || cmdTsMs <= 0)
            {
                return false;
            }

            long installTsMs = GetInstallationTimestampMs();
            if (installTsMs > 0 && cmdTsMs <= installTsMs + 3000)
            {
                return false;
            }

            return true;
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

        private static void NotifyBrowserSettingsChanged()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
                IntPtr result;
                SendMessageTimeout(
                    HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings",
                    SMTO_ABORTIFHUNG,
                    500,
                    out result
                );
            }
            catch {}
        }

        private static void RefreshPacSettings(long cacheBuster)
        {
            string pacUrl = GetLocalPacEndpointUrl(cacheBuster);
            SetWinInetPac(true, pacUrl);
            ApplyPacToRegistryRoot(Registry.CurrentUser, true, pacUrl);
            ApplyPacToAllLoadedUsers(true, pacUrl);
            NotifyBrowserSettingsChanged();
        }

        private static string SanitizeDocIdPart(string input)
        {
            if (string.IsNullOrEmpty(input)) return "unknown";
            StringBuilder sb = new StringBuilder();
            foreach (char c in input.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        private static string EscapeJsonStr(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static string GetEffectiveUserName()
        {
            try
            {
                string current = Environment.UserName ?? "";
                if (!WindowsIdentity.GetCurrent().IsSystem &&
                    !string.Equals(current, "SYSTEM", StringComparison.OrdinalIgnoreCase) &&
                    !current.EndsWith("$", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(current))
                {
                    return current;
                }

                string[] subKeyNames = Registry.Users.GetSubKeyNames();
                foreach (string sid in subKeyNames)
                {
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (RegistryKey volKey = Registry.Users.OpenSubKey(sid + "\\Volatile Environment", false))
                            {
                                if (volKey != null)
                                {
                                    string u = volKey.GetValue("USERNAME") as string;
                                    if (!string.IsNullOrEmpty(u)) return u;
                                }
                            }
                        }
                        catch {}
                    }
                }
            }
            catch {}
            return Environment.UserName ?? "student";
        }

        private static void ReportComputerHeartbeatToFirestore(bool filterActive)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                string roomId = GetConfigValue("RoomId", DefaultRoomId);
                string roomName = GetConfigValue("RoomName", CurrentRoomDisplayLabel);
                string instName = GetConfigValue("InstitutionName", "ישיבת נשמת התורה");
                string pcName = Environment.MachineName;
                string userName = GetEffectiveUserName();
                string docId = SanitizeDocIdPart(roomId) + "__" + SanitizeDocIdPart(pcName);
                string url = "https://firestore.googleapis.com/v1/projects/" + projectId +
                             "/databases/(default)/documents/computers/" + Uri.EscapeDataString(docId);

                string nowIso = DateTime.UtcNow.ToString("o");
                string json = "{" +
                    "\"fields\":{" +
                        "\"computerName\":{\"stringValue\":\"" + EscapeJsonStr(pcName) + "\"}," +
                        "\"userName\":{\"stringValue\":\"" + EscapeJsonStr(userName) + "\"}," +
                        "\"roomId\":{\"stringValue\":\"" + EscapeJsonStr(roomId) + "\"}," +
                        "\"roomName\":{\"stringValue\":\"" + EscapeJsonStr(roomName) + "\"}," +
                        "\"institutionName\":{\"stringValue\":\"" + EscapeJsonStr(instName) + "\"}," +
                        "\"version\":{\"stringValue\":\"3.3\"}," +
                        "\"filterActive\":{\"booleanValue\":" + (filterActive ? "true" : "false") + "}," +
                        "\"lastSeen\":{\"stringValue\":\"" + nowIso + "\"}" +
                    "}" +
                "}";

                byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "PATCH";
                req.Proxy = null;
                req.Timeout = 3000;
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = bodyBytes.Length;
                using (Stream s = req.GetRequestStream())
                {
                    s.Write(bodyBytes, 0, bodyBytes.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {}
            }
            catch {}
        }

        private static void UnregisterComputerFromFirestore()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                string roomId = GetConfigValue("RoomId", DefaultRoomId);
                string pcName = Environment.MachineName;
                string docId = SanitizeDocIdPart(roomId) + "__" + SanitizeDocIdPart(pcName);
                string url = "https://firestore.googleapis.com/v1/projects/" + projectId +
                             "/databases/(default)/documents/computers/" + Uri.EscapeDataString(docId);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "DELETE";
                req.Proxy = null;
                req.Timeout = 3000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {}
            }
            catch {}
        }

        private static void DisableFilter(bool stopDaemon)
        {
            SetWinInetPac(false, "");
            ApplyPacToRegistryRoot(Registry.CurrentUser, false, "");
            ApplyPacToAllLoadedUsers(false, "");
            ConfigureGameFirewallRules(false);

            if (stopDaemon)
            {
                UnregisterComputerFromFirestore();
                StopSinkholeServer();
            }
            else
            {
                ReportComputerHeartbeatToFirestore(false);
            }

            NotifyBrowserSettingsChanged();
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
            bool shouldBeEnabled = (ReadLocalOverride() != "allow");
            try
            {
                if (File.Exists(CachedPacFilePath))
                {
                    string cached = File.ReadAllText(CachedPacFilePath, Encoding.ASCII);
                    if (!string.IsNullOrEmpty(cached))
                    {
                        if (shouldBeEnabled && cached.Contains("var FILTER_ENABLED = false;"))
                        {
                            cached = cached.Replace("var FILTER_ENABLED = false;", "var FILTER_ENABLED = true;");
                        }
                        return cached;
                    }
                }
                string installedPac = Path.Combine(InstallDir, "filter.pac");
                if (File.Exists(installedPac))
                {
                    return File.ReadAllText(installedPac, Encoding.ASCII);
                }
            }
            catch {}

            return GenerateDynamicPacScript(shouldBeEnabled, new List<string>
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

            string[] coreInfraDomains = new string[]
            {
                "sefitrailer.github.io", "raw.githubusercontent.com", "firestore.googleapis.com"
            };
            foreach (string infra in coreInfraDomains)
            {
                if (!effectiveList.Contains(infra)) effectiveList.Add(infra);
            }

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

        private static bool ParseFirestoreRoomJson(
            string json,
            out bool filterEnabled,
            out List<string> whitelist,
            out string roomLabel,
            out string updatedAt,
            out string remoteCommand,
            out string remoteCommandId,
            out string remoteAdminUser,
            out string remoteAdminPass)
        {
            filterEnabled = true;
            whitelist = new List<string>();
            roomLabel = GetConfiguredRoomDisplayLabel();
            updatedAt = "";
            remoteCommand = "none";
            remoteCommandId = "0";
            remoteAdminUser = GetConfigValue("AdminUser", "");
            remoteAdminPass = GetConfigValue("AdminPass", "");

            if (string.IsNullOrEmpty(json)) return false;

            try
            {
                Match boolMatch = Regex.Match(json, "\"filterEnabled\"\\s*:\\s*\\{\\s*\"booleanValue\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
                if (boolMatch.Success)
                {
                    filterEnabled = string.Equals(boolMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                }

                Match rMatch = Regex.Match(json, "\"name\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                if (rMatch.Success && !string.IsNullOrEmpty(rMatch.Groups[1].Value))
                {
                    roomLabel = rMatch.Groups[1].Value;
                }

                Match uMatch = Regex.Match(json, "\"updatedAt\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                if (uMatch.Success)
                {
                    updatedAt = uMatch.Groups[1].Value;
                }

                Match cmdMatch = Regex.Match(json, "\"remoteCommand\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                if (cmdMatch.Success)
                {
                    remoteCommand = cmdMatch.Groups[1].Value.Trim().ToLowerInvariant();
                }

                Match cmdIdMatch = Regex.Match(json, "\"remoteCommandId\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                if (cmdIdMatch.Success)
                {
                    remoteCommandId = cmdIdMatch.Groups[1].Value.Trim();
                }

                Match adminUserMatch = Regex.Match(json, "\"remoteAdminUser\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]*)\"");
                if (adminUserMatch.Success && !string.IsNullOrEmpty(adminUserMatch.Groups[1].Value))
                {
                    remoteAdminUser = adminUserMatch.Groups[1].Value.Trim();
                }

                Match adminPassMatch = Regex.Match(json, "\"remoteAdminPass\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]*)\"");
                if (adminPassMatch.Success && !string.IsNullOrEmpty(adminPassMatch.Groups[1].Value))
                {
                    remoteAdminPass = adminPassMatch.Groups[1].Value;
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

        private static bool TryRunWithAdminCredentials(string exePath, string arguments, string adminUser, string adminPass)
        {
            if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPass))
            {
                return false;
            }

            // 1. Primary elevation method on Student accounts: authenticate to Windows Task Scheduler RPC using /U and /P
            // and register/trigger a SYSTEM (/RL HIGHEST) task so UAC never blocks or prompts the student.
            try
            {
                string taskName = "SchoolFilterElevatedExec";
                string trCmd = "\\\"" + exePath + "\\\" " + arguments.Replace("\"", "\\\"");
                string createArgs = string.Format(
                    "/Create /U \"{0}\" /P \"{1}\" /RU \"SYSTEM\" /RL HIGHEST /SC ONCE /ST 00:00 /TN \"{2}\" /TR \"{3}\" /F",
                    adminUser,
                    adminPass,
                    taskName,
                    trCmd
                );
                ProcessStartInfo createPsi = new ProcessStartInfo("schtasks.exe", createArgs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(createPsi))
                {
                    if (p != null && p.WaitForExit(4000) && p.ExitCode == 0)
                    {
                        string runArgs = string.Format("/Run /U \"{0}\" /P \"{1}\" /TN \"{2}\"", adminUser, adminPass, taskName);
                        ProcessStartInfo runPsi = new ProcessStartInfo("schtasks.exe", runArgs)
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (Process rp = Process.Start(runPsi))
                        {
                            if (rp != null && rp.WaitForExit(4000) && rp.ExitCode == 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch {}

            // 2. Fallback: start process directly using ProcessStartInfo UserName + SecureString Password
            try
            {
                string domain = ".";
                string user = adminUser;
                if (adminUser.Contains("\\"))
                {
                    string[] parts = adminUser.Split(new char[] { '\\' }, 2);
                    domain = parts[0];
                    user = parts[1];
                }

                SecureString securePass = new SecureString();
                foreach (char c in adminPass)
                {
                    securePass.AppendChar(c);
                }
                securePass.MakeReadOnly();

                ProcessStartInfo psi = new ProcessStartInfo(exePath, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath(),
                    Domain = domain,
                    UserName = user,
                    Password = securePass
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ExecuteRemoteUninstall(string adminUser, string adminPass)
        {
            try
            {
                Environment.CurrentDirectory = Path.GetTempPath();
            }
            catch {}

            // 1. Immediately restore direct internet and remove firewall rules
            DisableFilter(false);
            UnregisterComputerFromFirestore();

            // If running as non-admin Student and Admin credentials were provided, trigger Uninstall.exe as SYSTEM/Admin
            if (!IsAdministrator() && !string.IsNullOrEmpty(adminUser) && !string.IsNullOrEmpty(adminPass))
            {
                string uninstallerPath = Path.Combine(InstallDir, "Uninstall.exe");
                if (File.Exists(uninstallerPath))
                {
                    TryRunWithAdminCredentials(uninstallerPath, "/UNINSTALL /VERYSILENT /SUPPRESSMSGBOXES", adminUser, adminPass);
                }
            }

            // 2. Clean up startup registry, scheduled tasks, browser policies, shortcuts, and Uninstall key
            try
            {
                using (RegistryKey runKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null) runKey.DeleteValue("SchoolFilter", false);
                }
            }
            catch {}

            try
            {
                using (RegistryKey cuRunKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (cuRunKey != null) cuRunKey.DeleteValue("SchoolFilter", false);
                }
            }
            catch {}

            try
            {
                // Trigger elevated scheduled task if present
                ProcessStartInfo runUninstallTask = new ProcessStartInfo("schtasks.exe", "/Run /TN \"SchoolFilterRemoteUninstall\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(runUninstallTask))
                {
                    if (p != null) p.WaitForExit(1500);
                }
            }
            catch {}

            try
            {
                foreach (string taskName in new string[] { "SchoolFilterSystemAgent", "SchoolFilterGuard", "SchoolFilterRemoteUpdate", "SchoolFilterElevatedExec" })
                {
                    ProcessStartInfo stPsi = new ProcessStartInfo("schtasks.exe", "/Delete /TN \"" + taskName + "\" /F")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(stPsi))
                    {
                        if (p != null) p.WaitForExit(1500);
                    }
                }
            }
            catch {}

            string[] policyPaths = new string[]
            {
                @"SOFTWARE\Policies\Google\Chrome",
                @"SOFTWARE\Policies\Microsoft\Edge"
            };
            foreach (string path in policyPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("QuicAllowed", false);
                            key.DeleteValue("DnsOverHttpsMode", false);
                        }
                    }
                }
                catch {}
            }

            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SchoolFilter");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);
            }
            catch {}

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter", false);
            }
            catch {}
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter", false);
            }
            catch {}

            // 3. Spawn detached cleanup batch script to remove DataDir and InstallDir once this process exits
            try
            {
                string batchCleanup = Path.Combine(Path.GetTempPath(), "SchoolFilter_RemoteUninstall.bat");
                string cleanupScript = string.Format(
                    "@echo off\r\n" +
                    "cd /d \"%TEMP%\"\r\n" +
                    "ping 127.0.0.1 -n 2 > nul\r\n" +
                    "taskkill /F /IM SchoolFilterCtl.exe >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterSystemAgent\" /F >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterRemoteUninstall\" /F >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterElevatedExec\" /F >nul 2>&1\r\n" +
                    "icacls \"{0}\" /reset /T /C /Q >nul 2>&1\r\n" +
                    "for /L %%i in (1,1,15) do (\r\n" +
                    "    if exist \"{0}\" (\r\n" +
                    "        rmdir /S /Q \"{0}\" >nul 2>&1\r\n" +
                    "        if exist \"{0}\" ping 127.0.0.1 -n 2 > nul\r\n" +
                    "    )\r\n" +
                    ")\r\n" +
                    "if exist \"{1}\" rmdir /S /Q \"{1}\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n",
                    InstallDir,
                    DataDir
                );
                File.WriteAllText(batchCleanup, cleanupScript);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchCleanup + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };
                Process.Start(psi);
            }
            catch {}
        }

        private static bool ExecuteRemoteUpdate(string adminUser, string adminPass)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string customUpdateUrl = GetConfigValue("UpdateUrl", "");
                List<string> candidateUrls = new List<string>();
                if (!string.IsNullOrEmpty(customUpdateUrl))
                {
                    candidateUrls.Add(customUpdateUrl);
                }
                candidateUrls.Add("https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe");
                candidateUrls.Add("https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/dist/SchoolFilter_Setup.exe");

                string tempSetupPath = Path.Combine(Path.GetTempPath(), "SchoolFilter_RemoteUpdate_Setup.exe");
                bool downloaded = false;

                foreach (string url in candidateUrls)
                {
                    try
                    {
                        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || File.Exists(url))
                        {
                            string localFile = url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                                ? new Uri(url).LocalPath
                                : url;
                            if (File.Exists(localFile))
                            {
                                File.Copy(localFile, tempSetupPath, true);
                                downloaded = true;
                                break;
                            }
                        }
                        else
                        {
                            string bustUrl = url + (url.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
                            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(bustUrl);
                            req.Proxy = null;
                            req.Timeout = 15000;
                            req.UserAgent = "SchoolFilter-Updater/3.1";
                            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                            using (Stream s = resp.GetResponseStream())
                            using (FileStream fs = new FileStream(tempSetupPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                s.CopyTo(fs);
                            }
                            FileInfo fi = new FileInfo(tempSetupPath);
                            if (fi.Exists && fi.Length > 20000)
                            {
                                downloaded = true;
                                break;
                            }
                        }
                    }
                    catch {}
                }

                if (!downloaded || !File.Exists(tempSetupPath))
                {
                    return false;
                }

                string roomId = GetConfigValue("RoomId", DefaultRoomId);
                string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SchoolFilter");
                string dirArg = string.Equals(InstallDir, defaultDir, StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : " \"/DIR=" + InstallDir + "\"";
                string setupArgs = "/STUDENT /ROOM=" + roomId + dirArg + " /VERYSILENT /SUPPRESSMSGBOXES";

                try
                {
                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                    string sharedPayloadPath = Path.Combine(DataDir, "remote_update_setup.exe");
                    File.Copy(tempSetupPath, sharedPayloadPath, true);

                    if (!IsAdministrator() && !string.IsNullOrEmpty(adminUser) && !string.IsNullOrEmpty(adminPass))
                    {
                        if (TryRunWithAdminCredentials(sharedPayloadPath, setupArgs, adminUser, adminPass))
                        {
                            return true;
                        }
                    }

                    ProcessStartInfo runUpdateTask = new ProcessStartInfo("schtasks.exe", "/Run /TN \"SchoolFilterRemoteUpdate\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(runUpdateTask))
                    {
                        if (p != null && p.WaitForExit(2000) && p.ExitCode == 0)
                        {
                            return true;
                        }
                    }
                }
                catch {}

                string batchUpdate = Path.Combine(Path.GetTempPath(), "SchoolFilter_RunRemoteUpdate.bat");
                string updateScript = string.Format(
                    "@echo off\r\n" +
                    "cd /d \"%TEMP%\"\r\n" +
                    "ping 127.0.0.1 -n 2 > nul\r\n" +
                    "\"{0}\" {1}\r\n" +
                    "del /F /Q \"{0}\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n",
                    tempSetupPath,
                    setupArgs
                );
                File.WriteAllText(batchUpdate, updateScript);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchUpdate + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SpawnDelayedUserSessionRestart()
        {
            try
            {
                string ctlPath = Path.Combine(InstallDir, "SchoolFilterCtl.exe");
                string batchRestart = Path.Combine(Path.GetTempPath(), "SchoolFilter_UserRestart.bat");
                string script = string.Format(
                    "@echo off\r\n" +
                    "cd /d \"%TEMP%\"\r\n" +
                    "ping 127.0.0.1 -n 8 > nul\r\n" +
                    "if exist \"{0}\" start \"\" \"{0}\" watchdog\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n",
                    ctlPath
                );
                File.WriteAllText(batchRestart, script);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchRestart + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };
                Process.Start(psi);
            }
            catch {}
        }

        // Background daemon running as NT AUTHORITY\SYSTEM (via Scheduled Task SchoolFilterSystemAgent)
        // Ensures Remote Update, Remote Uninstall, Registry PAC sync, and Windows Firewall rules execute with full Administrator privileges
        // even when the logged-in user is a standard Student account without Admin permissions.
        private static void RunSystemAgentDaemon()
        {
            bool createdNew;
            using (Mutex mutex = CreateGlobalMutex("Global\\SchoolFilterSystemAgentMutex", out createdNew))
            {
                if (mutex == null || !createdNew)
                {
                    return;
                }

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                bool lastAppliedFirewall = false;
                bool? lastEnforcedState = null;
                string lastSeenUpdatedAt = null;
                int tickCounter = 0;
                bool running = true;

                while (running)
                {
                    try
                    {
                        string localUpdateTrigger = Path.Combine(DataDir, "trigger_update.txt");
                        if (File.Exists(localUpdateTrigger))
                        {
                            try { File.Delete(localUpdateTrigger); } catch {}
                            if (ExecuteRemoteUpdate("", ""))
                            {
                                running = false;
                                return;
                            }
                        }

                        string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                        string roomId = GetConfigValue("RoomId", DefaultRoomId);
                        string firestoreUrl = "https://firestore.googleapis.com/v1/projects/" + projectId +
                                              "/databases/(default)/documents/rooms/" + Uri.EscapeDataString(roomId);

                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(firestoreUrl);
                        req.Proxy = null;
                        req.Timeout = 4000;
                        req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                        req.Headers[HttpRequestHeader.CacheControl] = "no-cache, no-store, must-revalidate";
                        req.Headers[HttpRequestHeader.Pragma] = "no-cache";
                        req.UserAgent = "SchoolFilter-SystemAgent/3.2";

                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            string json = sr.ReadToEnd();
                            bool cloudFilterEnabled;
                            List<string> whitelist;
                            string roomLabel;
                            string roomUpdatedAt;
                            string remoteCommand;
                            string remoteCommandId;
                            string remoteAdminUser;
                            string remoteAdminPass;

                            if (ParseFirestoreRoomJson(json, out cloudFilterEnabled, out whitelist, out roomLabel, out roomUpdatedAt, out remoteCommand, out remoteCommandId, out remoteAdminUser, out remoteAdminPass))
                            {
                                if (ShouldExecuteRemoteCommand(remoteCommand, remoteCommandId, true))
                                {
                                    SaveLastExecutedCommandId(remoteCommandId, true);
                                    if (remoteCommand == "uninstall")
                                    {
                                        running = false;
                                        ExecuteRemoteUninstall(remoteAdminUser, remoteAdminPass);
                                        return;
                                    }
                                    else if (remoteCommand == "update")
                                    {
                                        if (ExecuteRemoteUpdate(remoteAdminUser, remoteAdminPass))
                                        {
                                            running = false;
                                            return;
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(roomUpdatedAt))
                                {
                                    if (lastSeenUpdatedAt == null || roomUpdatedAt != lastSeenUpdatedAt)
                                    {
                                        lastSeenUpdatedAt = roomUpdatedAt;
                                        SaveLocalOverride("auto");
                                    }
                                }
                                else if (lastSeenUpdatedAt == null)
                                {
                                    lastSeenUpdatedAt = "init";
                                    SaveLocalOverride("auto");
                                }

                                string localOverride = ReadLocalOverride();
                                bool shouldEnforce = (localOverride == "allow") ? false : ((localOverride == "block") ? true : cloudFilterEnabled);

                                try
                                {
                                    string newPac = GenerateDynamicPacScript(shouldEnforce, whitelist);
                                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                                    File.WriteAllText(CachedPacFilePath, newPac, Encoding.ASCII);
                                }
                                catch {}

                                if (shouldEnforce)
                                {
                                    if (lastEnforcedState != true || !AreAllUsersProxyEnforced())
                                    {
                                        long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                                        ApplyPacToAllLoadedUsers(true, GetLocalPacEndpointUrl(ts));
                                        NotifyBrowserSettingsChanged();
                                    }
                                    if (!lastAppliedFirewall || lastEnforcedState != true)
                                    {
                                        ConfigureGameFirewallRules(true);
                                        EnforceBrowserExtensionPolicies(true);
                                        lastAppliedFirewall = true;
                                    }
                                    PurgeUnauthorizedBrowserExtensions();
                                    EnforceInstalledGameBlock();
                                    lastEnforcedState = true;
                                }
                                else
                                {
                                    if (lastEnforcedState != false || IsAnyUserProxyEnforced())
                                    {
                                        ApplyPacToAllLoadedUsers(false, "");
                                        NotifyBrowserSettingsChanged();
                                        ReportComputerHeartbeatToFirestore(false);
                                    }
                                    if (lastAppliedFirewall || lastEnforcedState != false)
                                    {
                                        ConfigureGameFirewallRules(false);
                                        lastAppliedFirewall = false;
                                    }
                                    lastEnforcedState = false;
                                }

                                if (!IsSinkholeRunning())
                                {
                                    EnsureSinkholeRunning();
                                    if (tickCounter % 3 == 0)
                                    {
                                        ReportComputerHeartbeatToFirestore(shouldEnforce);
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    try
                    {
                        string localMode = ReadLocalOverride();
                        if (localMode == "block" || (localMode != "allow" && lastEnforcedState != false))
                        {
                            EnforceInstalledGameBlock();
                        }
                    }
                    catch {}

                    tickCounter++;
                    for (int i = 0; i < 5 && running; i++)
                    {
                        Thread.Sleep(500);
                    }
                }
            }
        }

        private static void RunSinkholeAndCloudSyncDaemon()
        {
            bool createdNew;
            using (Mutex mutex = CreateGlobalMutex("Global\\SchoolFilterSinkholeMutex", out createdNew))
            {
                if (mutex == null || !createdNew)
                {
                    return;
                }

                CurrentPacScript = LoadInitialPacScript();
                CurrentRoomDisplayLabel = GetConfiguredRoomDisplayLabel();

                long initialCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (ReadLocalOverride() != "allow")
                {
                    RefreshPacSettings(initialCacheBuster);
                    ConfigureGameFirewallRules(true);
                    EnforceBrowserExtensionPolicies(true);
                    PurgeUnauthorizedBrowserExtensions();
                    EnforceInstalledGameBlock();
                }

                TcpListener listener = null;
                bool running = true;

                Thread cloudSyncThread = new Thread(() =>
                {
                    string lastGeneratedPac = CurrentPacScript;
                    bool lastCloudFilterEnabled = true;
                    string lastSeenUpdatedAt = null;
                    List<string> currentRawWhitelist = new List<string>
                    {
                        "one-class.co.il", "*.one-class.co.il",
                        "gemini.google.com", "*.gemini.google.com",
                        "copilot.microsoft.com", "*.copilot.microsoft.com",
                        "edu.gov.il", "*.edu.gov.il",
                        "education.gov.il", "*.education.gov.il",
                        "classroom.google.com", "docs.google.com", "drive.google.com"
                    };
                    bool lastAppliedFirewallState = (ReadLocalOverride() != "allow");
                    long currentCacheBuster = initialCacheBuster;

                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2

                    int tickCounter = 0;
                    while (running)
                    {
                        try
                        {
                            if (tickCounter % 2 == 0)
                            {
                                try
                                {
                                    string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                                    string roomId = GetConfigValue("RoomId", DefaultRoomId);
                                    string firestoreUrl = "https://firestore.googleapis.com/v1/projects/" + projectId +
                                                          "/databases/(default)/documents/rooms/" + Uri.EscapeDataString(roomId);

                                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(firestoreUrl);
                                    req.Proxy = null;
                                    req.Timeout = 4000;
                                    req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                                    req.Headers[HttpRequestHeader.CacheControl] = "no-cache, no-store, must-revalidate";
                                    req.Headers[HttpRequestHeader.Pragma] = "no-cache";
                                    req.UserAgent = "SchoolFilter-RoomDaemon/3.2";

                                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                                    {
                                        string json = sr.ReadToEnd();
                                        bool cloudFilterEnabled;
                                        List<string> whitelist;
                                        string roomLabel;
                                        string roomUpdatedAt;
                                        string remoteCommand;
                                        string remoteCommandId;
                                        string remoteAdminUser;
                                        string remoteAdminPass;

                                        if (ParseFirestoreRoomJson(json, out cloudFilterEnabled, out whitelist, out roomLabel, out roomUpdatedAt, out remoteCommand, out remoteCommandId, out remoteAdminUser, out remoteAdminPass))
                                        {
                                            // Check for Remote Uninstall or Remote Update command
                                            if (ShouldExecuteRemoteCommand(remoteCommand, remoteCommandId, false))
                                            {
                                                SaveLastExecutedCommandId(remoteCommandId, false);
                                                if (remoteCommand == "uninstall")
                                                {
                                                    running = false;
                                                    try { if (listener != null) listener.Stop(); } catch {}
                                                    // Always restore current user's HKCU internet settings immediately
                                                    DisableFilter(false);
                                                    // If SYSTEM agent is not running, also run full uninstall from here
                                                    if (IsAdministrator() || !IsSystemAgentRunning())
                                                    {
                                                        ExecuteRemoteUninstall(remoteAdminUser, remoteAdminPass);
                                                    }
                                                    return;
                                                }
                                                else if (remoteCommand == "update")
                                                {
                                                    if (!IsAdministrator() && IsSystemAgentRunning())
                                                    {
                                                        // Let the SYSTEM agent perform the elevated update while we unlock SchoolFilterCtl.exe
                                                        // and schedule a user-session restart once the update finishes.
                                                        SpawnDelayedUserSessionRestart();
                                                        running = false;
                                                        try { if (listener != null) listener.Stop(); } catch {}
                                                        return;
                                                    }
                                                    else if (ExecuteRemoteUpdate(remoteAdminUser, remoteAdminPass))
                                                    {
                                                        running = false;
                                                        try { if (listener != null) listener.Stop(); } catch {}
                                                        return;
                                                    }
                                                }
                                            }

                                            CurrentRoomDisplayLabel = roomLabel;
                                            currentRawWhitelist = whitelist;
                                            CurrentRoomWhitelist = ExtractCleanBaseDomains(whitelist);
                                            lastCloudFilterEnabled = cloudFilterEnabled;

                                            // Clear any local override on initial cloud sync or whenever teacher clicks a button in the web portal (updatedAt changed)
                                            if (!string.IsNullOrEmpty(roomUpdatedAt))
                                            {
                                                if (lastSeenUpdatedAt == null || roomUpdatedAt != lastSeenUpdatedAt)
                                                {
                                                    lastSeenUpdatedAt = roomUpdatedAt;
                                                    SaveLocalOverride("auto");
                                                }
                                            }
                                            else if (lastSeenUpdatedAt == null)
                                            {
                                                lastSeenUpdatedAt = "init";
                                                SaveLocalOverride("auto");
                                            }
                                        }
                                    }
                                }
                                catch {}
                            }

                            string localOverride = ReadLocalOverride();
                            bool shouldEnforceBlock;
                            if (localOverride == "allow")
                            {
                                shouldEnforceBlock = false;
                            }
                            else if (localOverride == "block")
                            {
                                shouldEnforceBlock = true;
                            }
                            else
                            {
                                shouldEnforceBlock = lastCloudFilterEnabled;
                            }

                            IsFilterCurrentlyEnforced = shouldEnforceBlock;

                            string newPac = GenerateDynamicPacScript(shouldEnforceBlock, currentRawWhitelist);
                            CurrentPacScript = newPac;

                            bool pacChanged = (lastGeneratedPac == null || lastGeneratedPac != newPac);
                            if (pacChanged)
                            {
                                try
                                {
                                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                                    File.WriteAllText(CachedPacFilePath, newPac, Encoding.ASCII);
                                }
                                catch {}

                                currentCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                                lastGeneratedPac = newPac;

                                if (shouldEnforceBlock)
                                {
                                    RefreshPacSettings(currentCacheBuster);
                                }
                                else
                                {
                                    DisableFilter(false);
                                }
                            }

                            if (shouldEnforceBlock)
                            {
                                if (!AreAllUsersProxyEnforced())
                                {
                                    RefreshPacSettings(currentCacheBuster);
                                }
                                if (!lastAppliedFirewallState)
                                {
                                    ConfigureGameFirewallRules(true);
                                    EnforceBrowserExtensionPolicies(true);
                                    lastAppliedFirewallState = true;
                                }

                                if (tickCounter % 4 == 0)
                                {
                                    PurgeUnauthorizedBrowserExtensions();
                                }
                                EnforceInstalledGameBlock();
                                CheckAndNotifySystemKilledGame();
                            }
                            else
                            {
                                if (IsAnyUserProxyEnforced())
                                {
                                    DisableFilter(false);
                                }
                                if (lastAppliedFirewallState)
                                {
                                    ConfigureGameFirewallRules(false);
                                    lastAppliedFirewallState = false;
                                }
                            }

                            if (tickCounter % 6 == 0)
                            {
                                ReportComputerHeartbeatToFirestore(shouldEnforceBlock);
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
                                byte[] buf = new byte[4096];
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

                                // If filter is currently unlocked (IsFilterCurrentlyEnforced == false) and a browser
                                // still sent a cached proxy request to 127.0.0.1:9999, tunnel it directly so internet works immediately
                                if (!isDirectInfoPage && !IsFilterCurrentlyEnforced)
                                {
                                    if (TryTunnelUnlockedProxyRequest(c, stream, buf, read, req, blockedDomain))
                                    {
                                        return;
                                    }
                                }

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

        private static bool TryTunnelUnlockedProxyRequest(TcpClient clientSocket, NetworkStream clientStream, byte[] initialBuf, int initialRead, string reqText, string fallbackHost)
        {
            try
            {
                if (string.IsNullOrEmpty(reqText)) return false;
                string[] lines = reqText.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) return false;
                string firstLine = lines[0];

                if (firstLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = firstLine.Split(' ');
                    if (parts.Length < 2) return false;
                    string hostPort = parts[1].Trim();
                    string host = hostPort;
                    int port = 443;
                    int colonIdx = hostPort.LastIndexOf(':');
                    if (colonIdx > 0)
                    {
                        host = hostPort.Substring(0, colonIdx);
                        int parsedPort;
                        if (int.TryParse(hostPort.Substring(colonIdx + 1), out parsedPort) && parsedPort > 0)
                        {
                            port = parsedPort;
                        }
                    }

                    using (TcpClient upstream = new TcpClient())
                    {
                        IAsyncResult ar = upstream.BeginConnect(host, port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(3500)) return false;
                        upstream.EndConnect(ar);

                        clientSocket.ReceiveTimeout = 30000;
                        clientSocket.SendTimeout = 30000;
                        upstream.ReceiveTimeout = 30000;
                        upstream.SendTimeout = 30000;

                        NetworkStream upStream = upstream.GetStream();
                        byte[] okBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                        clientStream.Write(okBytes, 0, okBytes.Length);
                        clientStream.Flush();

                        Thread pumpToServer = new Thread(() =>
                        {
                            try
                            {
                                byte[] b = new byte[8192];
                                int n;
                                while ((n = clientStream.Read(b, 0, b.Length)) > 0)
                                {
                                    upStream.Write(b, 0, n);
                                    upStream.Flush();
                                }
                            }
                            catch {}
                            try { upstream.Close(); } catch {}
                        });
                        pumpToServer.IsBackground = true;
                        pumpToServer.Start();

                        byte[] downBuf = new byte[8192];
                        int r;
                        while ((r = upStream.Read(downBuf, 0, downBuf.Length)) > 0)
                        {
                            clientStream.Write(downBuf, 0, r);
                            clientStream.Flush();
                        }
                    }
                    return true;
                }
                else if (!string.IsNullOrEmpty(fallbackHost) && fallbackHost != "127.0.0.1" && fallbackHost != "localhost")
                {
                    using (TcpClient upstream = new TcpClient())
                    {
                        IAsyncResult ar = upstream.BeginConnect(fallbackHost, 80, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(3500)) return false;
                        upstream.EndConnect(ar);
                        clientSocket.ReceiveTimeout = 15000;
                        clientSocket.SendTimeout = 15000;
                        upstream.ReceiveTimeout = 15000;
                        upstream.SendTimeout = 15000;
                        NetworkStream upStream = upstream.GetStream();
                        upStream.Write(initialBuf, 0, initialRead);
                        upStream.Flush();
                        byte[] downBuf = new byte[8192];
                        int r;
                        while ((r = upStream.Read(downBuf, 0, downBuf.Length)) > 0)
                        {
                            clientStream.Write(downBuf, 0, r);
                            clientStream.Flush();
                        }
                    }
                    return true;
                }
            }
            catch {}
            return false;
        }

        private static readonly string[] SafeGoogleDefaultExtensionIds = new string[]
        {
            "ghbmnnjooekpmoecnnnilnnbdlolhkhi", // Google Docs Offline
            "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments
            "aohghmighlieiainnegkcijnfilokake", // Google Docs
            "felcaaldnbdncclmgdcncolpebgiejap", // Google Sheets
            "aapocclcgogkmnckokdopfmhonfmgoek", // Google Slides
            "apdfllckaahabafndbhieahigkjlhalf"  // Google Drive
        };

        private static readonly string[] BrowserOfflineGameTitleKeywords = new string[]
        {
            "chrome-extension://", "extension://", "chrome://dino", "edge://surf",
            "chrome://extensions", "edge://extensions", "תוספים - google chrome", "תוספים - microsoft edge",
            "unblocked games", "unblocked", "2048", "slope game", "subway surfers", "geometry dash",
            "moto x3m", "retro bowl", "1v1.lol", "cookie clicker", "run 3", "bitlife", "drive mad",
            "tunnel rush", "smash karts", "basketball stars", "temple run", "flappy bird",
            "snake game", "pacman", "pac-man", "tetris", "dino game", "dinosaur game",
            "fireboy", "watergirl", "friday night funkin", "five nights at freddy"
        };

        private static readonly string[] GameFolderKeywords = new string[]
        {
            "\\games\\", "\\game\\", "\\משחקים\\", "\\משחק\\", "\\steamapps\\", "\\epic games\\",
            "\\gog games\\", "\\riot games\\", "\\origin games\\", "\\ubisoft\\", "\\xboxgames\\",
            "\\plarium\\", "\\tlauncher\\", "\\.minecraft\\", "\\roblox\\", "\\bluestacks\\",
            "\\ldplayer\\", "\\nox\\", "\\memu\\", "\\retroarch\\", "\\dosbox\\",
            "\\microsoft.microsoftsolitairecollection", "\\microsoft.msminesweeper"
        };

        private static readonly string[] GameSignatureFiles = new string[]
        {
            "UnityPlayer.dll", "steam_api.dll", "steam_api64.dll", "steamclient.dll", "steamclient64.dll",
            "EOSSDK-Win64-Shipping.dll", "EOSSDK-Win32-Shipping.dll", "Galaxy.dll", "Galaxy64.dll",
            "binkw32.dll", "binkw64.dll", "bink2w32.dll", "bink2w64.dll",
            "fmod.dll", "fmodex.dll", "fmod64.dll", "fmodstudio.dll",
            "alleg40.dll", "alleg42.dll", "alleg44.dll",
            "raylib.dll", "love.dll", "data.win", "dosbox.conf",
            "rgss102e.dll", "rgss202e.dll", "rgss300.dll", "rgss301.dll"
        };

        private static void EnforceBrowserExtensionPolicies(bool blockExtensions)
        {
            string[] policyPaths = new string[]
            {
                @"SOFTWARE\Policies\Google\Chrome",
                @"SOFTWARE\Policies\Microsoft\Edge",
                @"SOFTWARE\Policies\BraveSoftware\Brave"
            };

            foreach (string path in policyPaths)
            {
                ApplyBrowserPolicyToHive(Registry.LocalMachine, "", path, blockExtensions);
                ApplyBrowserPolicyToHive(Registry.CurrentUser, "", path, blockExtensions);

                try
                {
                    foreach (string sid in Registry.Users.GetSubKeyNames())
                    {
                        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                            !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                        {
                            ApplyBrowserPolicyToHive(Registry.Users, sid + "\\", path, blockExtensions);
                        }
                    }
                }
                catch {}
            }
        }

        private static void ApplyBrowserPolicyToHive(RegistryKey root, string prefix, string policyPath, bool blockExtensions)
        {
            try
            {
                using (RegistryKey key = root.CreateSubKey(prefix + policyPath))
                {
                    if (key == null) return;
                    key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
                    key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);

                    if (blockExtensions)
                    {
                        key.SetValue("AllowDinosaurEasterEgg", 0, RegistryValueKind.DWord);
                        key.SetValue("AllowSurfGame", 0, RegistryValueKind.DWord);
                        key.SetValue("BlockExternalExtensions", 1, RegistryValueKind.DWord);
                        key.SetValue("DeveloperToolsAvailability", 2, RegistryValueKind.DWord);

                        using (RegistryKey blockKey = key.CreateSubKey("ExtensionInstallBlocklist"))
                        {
                            if (blockKey != null)
                            {
                                blockKey.SetValue("1", "*", RegistryValueKind.String);
                            }
                        }
                        using (RegistryKey allowKey = key.CreateSubKey("ExtensionInstallAllowlist"))
                        {
                            if (allowKey != null)
                            {
                                allowKey.SetValue("1", "ghbmnnjooekpmoecnnnilnnbdlolhkhi", RegistryValueKind.String);
                            }
                        }
                    }
                }
            }
            catch {}
        }

        private static void PurgeUnauthorizedBrowserExtensions()
        {
            bool removedAnyExtension = false;
            try
            {
                List<string> userDataRoots = new List<string>();
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    userDataRoots.Add(Path.Combine(localAppData, @"Google\Chrome\User Data"));
                    userDataRoots.Add(Path.Combine(localAppData, @"Microsoft\Edge\User Data"));
                    userDataRoots.Add(Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data"));
                }

                string usersDir = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"..\Users"));
                if (!Directory.Exists(usersDir))
                {
                    usersDir = @"C:\Users";
                }
                if (Directory.Exists(usersDir))
                {
                    foreach (string userDir in Directory.GetDirectories(usersDir))
                    {
                        string uLocal = Path.Combine(userDir, @"AppData\Local");
                        if (Directory.Exists(uLocal))
                        {
                            userDataRoots.Add(Path.Combine(uLocal, @"Google\Chrome\User Data"));
                            userDataRoots.Add(Path.Combine(uLocal, @"Microsoft\Edge\User Data"));
                            userDataRoots.Add(Path.Combine(uLocal, @"BraveSoftware\Brave-Browser\User Data"));
                        }
                    }
                }

                foreach (string userDataDir in userDataRoots)
                {
                    try
                    {
                        if (!Directory.Exists(userDataDir)) continue;
                        foreach (string profileDir in Directory.GetDirectories(userDataDir))
                        {
                            string extRoot = Path.Combine(profileDir, "Extensions");
                            if (!Directory.Exists(extRoot)) continue;

                            foreach (string extDir in Directory.GetDirectories(extRoot))
                            {
                                string extId = Path.GetFileName(extDir) ?? "";
                                bool isSafeDefault = false;
                                foreach (string safeId in SafeGoogleDefaultExtensionIds)
                                {
                                    if (string.Equals(extId, safeId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        isSafeDefault = true;
                                        break;
                                    }
                                }
                                if (!isSafeDefault)
                                {
                                    try
                                    {
                                        Directory.Delete(extDir, true);
                                        removedAnyExtension = true;
                                    }
                                    catch {}
                                }
                            }
                        }
                    }
                    catch {}
                }
            }
            catch {}

            if (removedAnyExtension)
            {
                // Restart open browsers once so deleted game extensions are immediately unloaded from memory
                foreach (string browserProc in new string[] { "chrome", "msedge", "brave" })
                {
                    try
                    {
                        foreach (Process p in Process.GetProcessesByName(browserProc))
                        {
                            try { p.Kill(); } catch {}
                            finally { try { p.Dispose(); } catch {} }
                        }
                    }
                    catch {}
                }
                RecordOrShowKilledGame("תוסף משחק בדפדפן (Chrome/Edge Extension)");
            }
        }

        private static string GetProcessExecutablePath(Process p)
        {
            try
            {
                IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(hProc, 0, sb, ref size) && size > 0)
                        {
                            return sb.ToString();
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch {}

            try
            {
                if (p.MainModule != null)
                {
                    return p.MainModule.FileName ?? "";
                }
            }
            catch {}

            return "";
        }

        private static string DetectInstalledGameOrExtension(Process p, out bool isBrowserWindowOnly)
        {
            isBrowserWindowOnly = false;
            string procName = p.ProcessName ?? "";
            if (string.IsNullOrEmpty(procName)) return null;

            string title = "";
            try { title = p.MainWindowTitle ?? ""; } catch {}

            // 1. Check browser windows for offline/extension games or chrome://extensions
            if (string.Equals(procName, "chrome", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "msedge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "brave", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "opera", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "firefox", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(title))
                {
                    string lowerTitle = title.ToLowerInvariant();
                    foreach (string kw in BrowserOfflineGameTitleKeywords)
                    {
                        if (lowerTitle.Contains(kw))
                        {
                            isBrowserWindowOnly = true;
                            return "משחק/תוסף בדפדפן (" + title + ")";
                        }
                    }
                }
                return null;
            }

            // 2. Direct known game process names
            string knownGameName;
            if (BlockedGameProcesses.TryGetValue(procName, out knownGameName))
            {
                return knownGameName;
            }

            // 3. Java-based games (Minecraft, etc.)
            if (string.Equals(procName, "javaw", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "java", StringComparison.OrdinalIgnoreCase))
            {
                if (title.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Lunar Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Badlion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("TLauncher", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Minecraft";
                }
                return null;
            }

            // 4. Never touch whitelisted system, study, office, or school processes
            if (SafeNonGameProcesses.Contains(procName))
            {
                return null;
            }

            string exePath = GetProcessExecutablePath(p);
            if (string.IsNullOrEmpty(exePath)) return null;

            string lowerPath = exePath.ToLowerInvariant();
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            if (!string.IsNullOrEmpty(winDir) && lowerPath.StartsWith(winDir))
            {
                return null;
            }
            if (lowerPath.Contains("\\schoolfilter\\") ||
                lowerPath.Contains("\\veyon\\") ||
                lowerPath.Contains("\\microsoft office\\") ||
                lowerPath.Contains("\\windows defender\\") ||
                lowerPath.Contains("\\google\\chrome\\") ||
                lowerPath.Contains("\\microsoft\\edge"))
            {
                return null;
            }

            // 5. Unreal Engine *-Shipping.exe signature
            if (procName.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase) ||
                lowerPath.EndsWith("-shipping.exe", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrEmpty(title) ? title : procName;
            }

            // 6. Game folder path keywords (\Games\, \משחקים\, \SteamApps\, etc.)
            foreach (string folderKw in GameFolderKeywords)
            {
                if (lowerPath.Contains(folderKw))
                {
                    return !string.IsNullOrEmpty(title) ? title : procName;
                }
            }

            // 7. Check executable directory and parent directory for Game Engine / Game SDK DLLs
            try
            {
                string exeDir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                {
                    if (Directory.Exists(Path.Combine(exeDir, procName + "_Data")) ||
                        Directory.Exists(Path.Combine(exeDir, "MonoBleedingEdge")) ||
                        Directory.Exists(Path.Combine(exeDir, @"Engine\Binaries")))
                    {
                        return !string.IsNullOrEmpty(title) ? title : procName;
                    }

                    foreach (string sigFile in GameSignatureFiles)
                    {
                        if (File.Exists(Path.Combine(exeDir, sigFile)))
                        {
                            return !string.IsNullOrEmpty(title) ? title : procName;
                        }
                    }

                    // SDL / DirectX / XInput bundled locally in a non-system application folder with a visible window
                    if (p.MainWindowHandle != IntPtr.Zero &&
                        (File.Exists(Path.Combine(exeDir, "SDL2.dll")) ||
                         File.Exists(Path.Combine(exeDir, "SDL.dll")) ||
                         File.Exists(Path.Combine(exeDir, "d3dx9_43.dll")) ||
                         File.Exists(Path.Combine(exeDir, "xinput1_3.dll")) ||
                         File.Exists(Path.Combine(exeDir, "physxloader.dll"))))
                    {
                        return !string.IsNullOrEmpty(title) ? title : procName;
                    }
                }
            }
            catch {}

            // 8. Check FileVersionInfo OriginalFilename (catches renamed game .exe files!)
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                if (fvi != null)
                {
                    string origName = Path.GetFileNameWithoutExtension(fvi.OriginalFilename ?? "");
                    if (!string.IsNullOrEmpty(origName) && BlockedGameProcesses.TryGetValue(origName, out knownGameName))
                    {
                        return knownGameName;
                    }
                }
            }
            catch {}

            return null;
        }

        private static void RecordOrShowKilledGame(string gameDisplayName)
        {
            if (WindowsIdentity.GetCurrent().IsSystem)
            {
                try
                {
                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                    File.WriteAllText(LastKilledGameFilePath, gameDisplayName, Encoding.UTF8);
                }
                catch {}
            }
            else
            {
                TriggerPopupNotification(gameDisplayName, true);
            }
        }

        private static void CheckAndNotifySystemKilledGame()
        {
            try
            {
                if (File.Exists(LastKilledGameFilePath))
                {
                    string killedGame = File.ReadAllText(LastKilledGameFilePath, Encoding.UTF8).Trim();
                    File.Delete(LastKilledGameFilePath);
                    if (!string.IsNullOrEmpty(killedGame))
                    {
                        TriggerPopupNotification(killedGame, true);
                    }
                }
            }
            catch {}
        }

        private static void EnforceInstalledGameBlock()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                Process[] allProcs = Process.GetProcesses();
                foreach (Process p in allProcs)
                {
                    try
                    {
                        if (p.Id <= 4 || p.Id == currentPid) continue;

                        bool isBrowserWindowOnly;
                        string gameDisplayName = DetectInstalledGameOrExtension(p, out isBrowserWindowOnly);

                        if (!string.IsNullOrEmpty(gameDisplayName))
                        {
                            if (isBrowserWindowOnly && p.MainWindowHandle != IntPtr.Zero)
                            {
                                PostMessage(p.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                            }
                            else
                            {
                                try
                                {
                                    p.Kill();
                                }
                                catch
                                {
                                    try
                                    {
                                        ProcessStartInfo tkPsi = new ProcessStartInfo("taskkill.exe", "/F /T /PID " + p.Id)
                                        {
                                            CreateNoWindow = true,
                                            UseShellExecute = false,
                                            WindowStyle = ProcessWindowStyle.Hidden
                                        };
                                        using (Process tk = Process.Start(tkPsi))
                                        {
                                            if (tk != null) tk.WaitForExit(1000);
                                        }
                                    }
                                    catch {}
                                }
                            }
                            RecordOrShowKilledGame(gameDisplayName);
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
                if (!IsForegroundWindowBrowser()) return;

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

                double minCooldown = isGame ? 5.0 : 25.0;
                double sameTargetCooldown = isGame ? 10.0 : 60.0;

                if (secondsSinceLast < minCooldown) return;
                if (string.Equals(LastNotifiedTarget, targetName, StringComparison.OrdinalIgnoreCase) && secondsSinceLast < sameTargetCooldown) return;

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
                   "<link rel='icon' type='image/svg+xml' href=\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 64 64'%3E%3Cpath d='M32 4L8 14v18c0 16.5 10.2 31.6 24 36 13.8-4.4 24-19.5 24-36V14L32 4z' fill='%23dc2626' stroke='%23fca5a5' stroke-width='2'/%3E%3Crect x='22' y='29' width='20' height='6' rx='3' fill='%23ffffff'/%3E%3C/svg%3E\">" +
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

        private static bool AreAllUsersProxyEnforced()
        {
            bool checkedAny = false;
            try
            {
                if (IsRegistryRootProxyEnforced(Registry.CurrentUser, ""))
                {
                    checkedAny = true;
                }
                else if (!WindowsIdentity.GetCurrent().IsSystem)
                {
                    return false;
                }
            }
            catch { }

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
                            checkedAny = true;
                            if (!IsRegistryRootProxyEnforced(Registry.Users, sid + "\\"))
                            {
                                return false;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return checkedAny;
        }

        private static bool IsAnyUserProxyEnforced()
        {
            try
            {
                if (HasRegistryRootAnyProxy(Registry.CurrentUser, ""))
                {
                    return true;
                }
            }
            catch { }

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
                            if (HasRegistryRootAnyProxy(Registry.Users, sid + "\\"))
                            {
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsRegistryRootProxyEnforced(RegistryKey root, string prefix)
        {
            using (RegistryKey regKey = root.OpenSubKey(prefix + @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
            {
                if (regKey == null) return false;
                string autoConfigUrl = regKey.GetValue("AutoConfigURL") as string;
                if (string.IsNullOrEmpty(autoConfigUrl) ||
                    autoConfigUrl.IndexOf("127.0.0.1:9999", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            using (RegistryKey connKey = root.OpenSubKey(prefix + @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections", false))
            {
                if (connKey == null) return false;
                byte[] data = connKey.GetValue("DefaultConnectionSettings") as byte[];
                if (data == null || data.Length < 12 || (data[8] & PROXY_TYPE_AUTO_PROXY_URL) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasRegistryRootAnyProxy(RegistryKey root, string prefix)
        {
            using (RegistryKey regKey = root.OpenSubKey(prefix + @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
            {
                if (regKey != null)
                {
                    string autoConfigUrl = regKey.GetValue("AutoConfigURL") as string;
                    if (!string.IsNullOrEmpty(autoConfigUrl)) return true;

                    object proxyEnable = regKey.GetValue("ProxyEnable");
                    if (proxyEnable is int && ((int)proxyEnable) != 0) return true;
                }
            }

            using (RegistryKey connKey = root.OpenSubKey(prefix + @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections", false))
            {
                if (connKey != null)
                {
                    byte[] data = connKey.GetValue("DefaultConnectionSettings") as byte[];
                    if (data != null && data.Length >= 12 && (data[8] & (PROXY_TYPE_PROXY | PROXY_TYPE_AUTO_PROXY_URL)) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
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
        private int remainingSeconds = 5;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

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
                Text = "הבנתי, סגור (5)",
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
            closeTimer.Interval = 1000;
            closeTimer.Tick += (s, e) =>
            {
                remainingSeconds--;
                if (remainingSeconds <= 0)
                {
                    closeTimer.Stop();
                    this.Close();
                }
                else
                {
                    closeBtn.Text = "הבנתי, סגור (" + remainingSeconds + ")";
                }
            };
            closeTimer.Start();
        }
    }
}
