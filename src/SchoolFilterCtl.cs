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
using System.Security.Cryptography;
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
        private static volatile int ActiveSinkholePort = 9999;
        private const string SystemAgentMutexName = "Global\\SchoolFilterSystemAgentMutex_v361";
        private const string SinkholeMutexName = "Global\\SchoolFilterSinkholeMutex_v362";

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
            "supabase.co", "supabase.in", "r2.dev", "lovable.dev",
            "googleusercontent.com", "withgoogle.com", "recaptcha.net",
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
            { "Mahjong", "Mahjong" },

            // VPN & Proxy Bypasser processes
            { "psiphon", "Psiphon VPN" },
            { "psiphon3", "Psiphon VPN" },
            { "ultrasurf", "Ultrasurf Proxy" },
            { "u995", "Ultrasurf Proxy" },
            { "u1000", "Ultrasurf Proxy" },
            { "windscribe", "Windscribe VPN" },
            { "protonvpn", "ProtonVPN" },
            { "openvpn", "OpenVPN" },
            { "wireguard", "WireGuard VPN" },
            { "warp-svc", "Cloudflare WARP VPN" },
            { "warp-taskbar", "Cloudflare WARP VPN" },
            { "Cloudflare WARP", "Cloudflare WARP VPN" },
            { "tor", "Tor Browser / Onion Proxy" },
            { "torbrowser", "Tor Browser" },
            { "lantern", "Lantern Proxy" },
            { "freegate", "Freegate Proxy" },
            { "outline", "Outline VPN" },

            // Additional Local Games & Emulators
            { "osu!", "osu! Rhythm Game" },
            { "osu", "osu! Rhythm Game" },
            { "Brotato", "Brotato" },
            { "VampireSurvivors", "Vampire Survivors" },
            { "Stumble Guys", "Stumble Guys" },
            { "StumbleGuys", "Stumble Guys" },
            { "samp", "GTA San Andreas Multiplayer (SAMP)" },
            { "mta_sa", "Multi Theft Auto (MTA)" },
            { "Multi Theft Auto", "Multi Theft Auto (MTA)" },
            { "proxy_sa", "GTA SAMP Proxy" },
            { "PrismLauncher", "Minecraft (PrismLauncher)" },
            { "MultiMC", "Minecraft (MultiMC)" },
            { "CurseForge", "CurseForge Game Mod Manager" },
            { "ModrinthApp", "Modrinth Minecraft" },
            { "flashplayer", "Adobe Flash Player Game" },
            { "flashplayer_32_sa", "Flash Player Game" },
            { "SAFlashPlayer", "Flash Player Game" },
            { "VisualBoyAdvance", "VisualBoyAdvance (GBA Emulator)" },
            { "VisualBoyAdvance-M", "VBA-M Emulator" },
            { "vba", "GBA Emulator" },
            { "mGBA", "mGBA Game Boy Emulator" },
            { "DeSmuME", "DeSmuME Nintendo DS Emulator" },
            { "melonDS", "melonDS Nintendo DS Emulator" },
            { "snes9x", "Snes9x SNES Emulator" },
            { "snes9x-x64", "Snes9x SNES Emulator" },
            { "zsnesw", "ZSNES SNES Emulator" },
            { "fceux", "FCEUX NES Emulator" },
            { "Nestopia", "Nestopia NES Emulator" },
            { "epsxe", "ePSXe PlayStation Emulator" },
            { "DuckStation", "DuckStation PS1 Emulator" },
            { "Citra", "Citra 3DS Emulator" },
            { "yuzu", "Yuzu Switch Emulator" },
            { "ryujinx", "Ryujinx Switch Emulator" },
            { "RPCS3", "RPCS3 PS3 Emulator" },
            { "xemu", "Xemu Xbox Emulator" },
            { "Cemu", "Cemu Wii U Emulator" },
            { "teeworlds", "Teeworlds" },
            { "DDNet", "DDNet" },
            { "xonotic", "Xonotic" },
            { "openttd", "OpenTTD" },
            { "OpenRA.Game", "OpenRA" },
            { "assaultcube", "AssaultCube" },
            { "supertuxkart", "SuperTuxKart" },
            { "CrossyRoad", "Crossy Road" },
            { "AngryBirds", "Angry Birds" },
            { "CutTheRope", "Cut the Rope" },
            { "BadPiggies", "Bad Piggies" },

            // Microsoft Store & Windows Gaming Platform
            { "WinStore.App", "חנות האפליקציות של מייקרוסופט (Microsoft Store)" },
            { "XboxApp", "Xbox Gaming App" },
            { "XboxPcApp", "Xbox Gaming App" },
            { "XboxGameBar", "Xbox Game Bar" },
            { "GameBar", "Xbox Game Bar" },
            { "GameBarFT", "Xbox Game Bar" },
            { "GameBarPresenceWriter", "Xbox Game Bar" },

            // Asphalt & Gameloft Windows Store Games
            { "Asphalt9_w10_x64_rtl", "Asphalt 9: Legends" },
            { "Asphalt9_w8", "Asphalt 9: Legends" },
            { "Asphalt9", "Asphalt 9: Legends" },
            { "Asphalt8_w8", "Asphalt 8: Airborne" },
            { "Asphalt8", "Asphalt 8: Airborne" },
            { "AsphaltLegends", "Asphalt Legends Unite" },
            { "AsphaltLegendsUnite", "Asphalt Legends Unite" },
            { "Asphalt_w10", "Asphalt" },
            { "Asphalt", "Asphalt" },
            { "A9", "Asphalt 9" },
            { "ModernCombat5", "Modern Combat 5" },
            { "MC5", "Modern Combat 5" },
            { "SniperFury", "Sniper Fury" },
            { "MinionRush", "Minion Rush" },
            { "DragonManiaLegends", "Dragon Mania Legends" },
            { "DML", "Dragon Mania Legends" },

            // Additional popular Microsoft Store Games
            { "HillClimbRacing", "Hill Climb Racing" },
            { "HillClimbRacing2", "Hill Climb Racing 2" },
            { "SubwaySurfers", "Subway Surfers" },
            { "SubwaySurfers_uwp", "Subway Surfers" },
            { "CandyCrushSaga", "Candy Crush Saga" },
            { "CandyCrushSodaSaga", "Candy Crush Soda" },
            { "CandyCrushFriends", "Candy Crush Friends" },
            { "FarmHeroesSaga", "Farm Heroes Saga" },
            { "AngryBirds2", "Angry Birds 2" },
            { "AngryBirdsClassic", "Angry Birds" },
            { "FruitNinja", "Fruit Ninja" },
            { "TempleRun", "Temple Run" },
            { "TempleRun2", "Temple Run 2" },
            { "SonicDash", "Sonic Dash" },
            { "ForzaHorizon4", "Forza Horizon 4" },
            { "ForzaHorizon5", "Forza Horizon 5" },
            { "ForzaMotorsport", "Forza Motorsport" },
            { "wotblitz", "World of Tanks Blitz" },
            { "MagicTiles3", "Magic Tiles 3" },
            { "PixelGun3D", "Pixel Gun 3D" },
            { "MicrosoftSolitaireCollection", "Solitaire Collection" },
            { "MicrosoftMinesweeper", "Microsoft Minesweeper" },
            { "MicrosoftMahjong", "Microsoft Mahjong" },
            { "RobloxPlayer", "Roblox" },
            { "Roblox", "Roblox" }
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
            "SecurityHealthSystray", "MsMpEng", "NisSrv", "OneDrive", "BarIlan", "Responsa",
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

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        private static extern int DnsFlushResolverCache();

        private static volatile bool IsFilterCurrentlyEnforced = true;
        private static DateTime LastNotificationTime = DateTime.MinValue;
        private static string LastNotifiedTarget = "";
        private static string LastSeenKilledGameToken = "";
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
                    if (!IsAdministrator()) return 5;
                    RunSystemAgentDaemon();
                    return 0;
                }
                else if (action == "notify")
                {
                    bool createdPopupNew;
                    using (Mutex popupMutex = CreateScopedMutex("Local\\SchoolFilterNotifyPopupMutex", out createdPopupNew, false))
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
                    bool createdWatchdog;
                    using (Mutex wdMutex = CreateScopedMutex("Local\\SchoolFilterUserWatchdogMutex", out createdWatchdog, false))
                    {
                        if (wdMutex == null || !createdWatchdog)
                        {
                            return 0;
                        }

                        if (ReadLocalOverride() != "allow")
                        {
                            EnableFilter();
                        }
                        else
                        {
                            EnsureSinkholeRunning();
                        }

                        RunUserSessionWatchdogLoop();
                    }
                    return 0;
                }
                else if (action == "stop-sinkhole")
                {
                    if (!IsAdministrator()) return 5;
                    return 0;
                }
                else if (action == "allow" || action == "unblock" || action == "off" || action == "restore")
                {
                    // Security hardening: only Administrators / SYSTEM may disable the filter locally
                    if (!IsAdministrator())
                    {
                        return 5;
                    }
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
                    if (!IsAdministrator())
                    {
                        return 5;
                    }
                    DisableFilter(true);
                }
                else
                {
                    if (IsAdministrator())
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
                    }
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

        private static Mutex CreateScopedMutex(string name, out bool createdNew, bool systemAndAdminsOnly)
        {
            createdNew = false;
            try
            {
                MutexSecurity security = new MutexSecurity();
                security.SetAccessRuleProtection(true, false);

                security.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Allow
                ));
                security.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    MutexRights.FullControl,
                    AccessControlType.Allow
                ));

                // Always grant BuiltinUsers Synchronize and ReadPermissions across sessions so any session can check status
                security.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    MutexRights.Synchronize | MutexRights.ReadPermissions,
                    AccessControlType.Allow
                ));

                if (!systemAndAdminsOnly)
                {
                    SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User;
                    if (currentSid != null)
                    {
                        security.AddAccessRule(new MutexAccessRule(
                            currentSid,
                            MutexRights.FullControl,
                            AccessControlType.Allow
                        ));
                    }
                }

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

        private static void LockDataDirectoryPermissions()
        {
            if (!IsAdministrator()) return;
            try
            {
                if (!Directory.Exists(DataDir))
                {
                    Directory.CreateDirectory(DataDir);
                }

                DirectoryInfo dInfo = new DirectoryInfo(DataDir);
                DirectorySecurity dSecurity = new DirectorySecurity();
                dSecurity.SetAccessRuleProtection(true, false);

                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));

                dInfo.SetAccessControl(dSecurity);

                foreach (string filePath in Directory.GetFiles(DataDir))
                {
                    try
                    {
                        FileInfo fInfo = new FileInfo(filePath);
                        FileSecurity fSecurity = new FileSecurity();
                        fSecurity.SetAccessRuleProtection(true, false);
                        fSecurity.AddAccessRule(new FileSystemAccessRule(
                            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                            FileSystemRights.FullControl,
                            AccessControlType.Allow
                        ));
                        fSecurity.AddAccessRule(new FileSystemAccessRule(
                            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                            FileSystemRights.FullControl,
                            AccessControlType.Allow
                        ));
                        fSecurity.AddAccessRule(new FileSystemAccessRule(
                            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                            FileSystemRights.ReadAndExecute,
                            AccessControlType.Allow
                        ));
                        fInfo.SetAccessControl(fSecurity);
                    }
                    catch {}
                }
            }
            catch {}
        }

        private static bool IsSystemAgentRunning()
        {
            try
            {
                using (Mutex m = Mutex.OpenExisting(SystemAgentMutexName, MutexRights.Synchronize))
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
            foreach (string mutexName in new string[] {
                "Global\\SchoolFilterSinkholeMutex_v362",
                "Global\\SchoolFilterSinkholeMutex_v361",
                "Global\\SchoolFilterSinkholeMutex_v360",
                "Global\\SchoolFilterSinkholeMutex_v350",
                "Local\\SchoolFilterSinkholeMutex_v360",
                SinkholeMutexName
            })
            {
                try
                {
                    using (Mutex m = Mutex.OpenExisting(mutexName, MutexRights.Synchronize))
                    {
                        return true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return true;
                }
                catch {}
            }
            return false;
        }

        private static volatile string InMemoryOverrideMode = null;

        private static void SaveLocalOverride(string mode)
        {
            if (!IsAdministrator() &&
                !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "block", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            InMemoryOverrideMode = (mode ?? "auto").Trim().ToLowerInvariant();
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                if (File.Exists(StateFilePath))
                {
                    File.SetAttributes(StateFilePath, FileAttributes.Normal);
                }
                File.WriteAllText(StateFilePath, InMemoryOverrideMode);
            }
            catch {}
        }

        private static string ReadLocalOverride()
        {
            if (!string.IsNullOrEmpty(InMemoryOverrideMode))
            {
                return InMemoryOverrideMode;
            }
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
                if (isSystemAgent || IsAdministrator())
                {
                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                    string path = isSystemAgent ? LastSysCommandFilePath : LastCommandFilePath;
                    File.WriteAllText(path, cmdId ?? "");
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\SchoolFilter"))
                    {
                        if (key != null) key.SetValue("LastCommandId", cmdId ?? "", RegistryValueKind.String);
                    }
                }
            }
            catch {}
        }

        private static string ReadLastExecutedCommandId(bool isSystemAgent)
        {
            try
            {
                if (!isSystemAgent && !IsAdministrator())
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\SchoolFilter", false))
                    {
                        if (key != null)
                        {
                            string v = key.GetValue("LastCommandId") as string;
                            if (!string.IsNullOrEmpty(v)) return v.Trim();
                        }
                    }
                }
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
            return "http://127.0.0.1:" + ActiveSinkholePort + "/filter.pac?v=" + cacheBuster.ToString();
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
                try { DnsFlushResolverCache(); } catch {}
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

                string nowIso = DateTime.UtcNow.ToString("o");
                string rtdbUrl = "https://" + projectId + "-default-rtdb.firebaseio.com/computers/" + Uri.EscapeDataString(docId) + ".json";
                string rtdbJson = "{" +
                    "\"computerName\":\"" + EscapeJsonStr(pcName) + "\"," +
                    "\"userName\":\"" + EscapeJsonStr(userName) + "\"," +
                    "\"roomId\":\"" + EscapeJsonStr(roomId) + "\"," +
                    "\"roomName\":\"" + EscapeJsonStr(roomName) + "\"," +
                    "\"institutionName\":\"" + EscapeJsonStr(instName) + "\"," +
                    "\"version\":\"3.6.2\"," +
                    "\"filterActive\":" + (filterActive ? "true" : "false") + "," +
                    "\"lastSeen\":\"" + nowIso + "\"," +
                    "\"lastSeenMs\":{\".sv\":\"timestamp\"}" +
                "}";

                byte[] bodyBytes = Encoding.UTF8.GetBytes(rtdbJson);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(rtdbUrl);
                req.Method = "PUT";
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

                try
                {
                    string rtdbUrl = "https://" + projectId + "-default-rtdb.firebaseio.com/computers/" + Uri.EscapeDataString(docId) + ".json";
                    HttpWebRequest rtdbReq = (HttpWebRequest)WebRequest.Create(rtdbUrl);
                    rtdbReq.Method = "DELETE";
                    rtdbReq.Proxy = null;
                    rtdbReq.Timeout = 3000;
                    using (HttpWebResponse resp = (HttpWebResponse)rtdbReq.GetResponse()) {}
                }
                catch {}

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
            EnforceBrowserExtensionPolicies(false);

            if (stopDaemon)
            {
                UnregisterComputerFromFirestore();
            }
            else
            {
                ReportComputerHeartbeatToFirestore(false);
            }

            NotifyBrowserSettingsChanged();
        }

        private static void EnsureAppContainerLoopbackExemption()
        {
            if (!IsAdministrator()) return;
            try
            {
                ProcessStartInfo psi1 = new ProcessStartInfo("CheckNetIsolation.exe", "LoopbackExempt -a -n=Microsoft.MicrosoftEdge_8wekyb3d8bbwe")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi1))
                {
                    if (p != null) p.WaitForExit(1000);
                }
                ProcessStartInfo psi2 = new ProcessStartInfo("CheckNetIsolation.exe", "LoopbackExempt -a -n=Microsoft.Win32WebViewHost_cw5n1h2txyewy")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi2))
                {
                    if (p != null) p.WaitForExit(1000);
                }
            }
            catch {}
        }

        private static void ConfigureGameFirewallRules(bool blockGames)
        {
            try
            {
                RunNetsh("advfirewall firewall delete rule name=\"SchoolFilter_BlockGameUDP\"");
                RunNetsh("advfirewall firewall delete rule name=\"SchoolFilter_BlockGameTCP\"");
                RunNetsh("advfirewall firewall delete rule name=\"SchoolFilter_BlockQUIC\"");

                if (blockGames)
                {
                    EnsureAppContainerLoopbackExemption();
                    RunNetsh("advfirewall firewall add rule name=\"SchoolFilter_BlockQUIC\" dir=out action=block protocol=UDP remoteport=443");
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
                string myExePath = Assembly.GetExecutingAssembly().Location;
                int myPid = Process.GetCurrentProcess().Id;
                foreach (Process p in Process.GetProcessesByName("SchoolFilterCtl"))
                {
                    try
                    {
                        if (p.Id == myPid) continue;
                        string otherPath = GetProcessExecutablePath(p);
                        if (!string.IsNullOrEmpty(otherPath) &&
                            !string.Equals(otherPath, myExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(800);
                        }
                    }
                    catch {}
                }
            }
            catch {}

            if (IsSinkholeRunning())
            {
                return;
            }

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(exePath, "sinkhole")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(50);
                    if (IsSinkholeRunning())
                    {
                        break;
                    }
                }
            }
            catch {}
        }

        private static bool IsSafePacDomainPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern.Length > 120) return false;
            foreach (char c in pattern)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '*'))
                {
                    return false;
                }
            }
            return true;
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

            // Only allow the web portal & Firebase/Firestore API for teacher management (no raw.githubusercontent.com in browser!)
            string[] coreInfraDomains = new string[]
            {
                "sefitrailer.github.io", "firestore.googleapis.com",
                "*.firebaseio.com", "*.firebasedatabase.app"
            };
            foreach (string infra in coreInfraDomains)
            {
                if (!effectiveList.Contains(infra)) effectiveList.Add(infra);
            }

            // 1. Google Suite & Authentication Helper Expansion (Gemini, Classroom, Docs, Drive, Accounts, etc.)
            bool hasGoogle = effectiveList.Exists(d => {
                string s = (d ?? "").ToLowerInvariant();
                return s.Contains("google.com") || s.Contains("gemini") || s.Contains("classroom");
            });
            if (hasGoogle)
            {
                string[] googleHelpers = new string[]
                {
                    "accounts.google.com", "*.accounts.google.com",
                    "myaccount.google.com", "*.myaccount.google.com",
                    "accounts.youtube.com",
                    "gemini.google.com", "*.gemini.google.com",
                    "bard.google.com", "*.bard.google.com",
                    "classroom.google.com", "*.classroom.google.com",
                    "docs.google.com", "*.docs.google.com",
                    "drive.google.com", "*.drive.google.com",
                    "sheets.google.com", "*.sheets.google.com",
                    "slides.google.com", "*.slides.google.com",
                    "forms.google.com", "*.forms.google.com",
                    "apis.google.com",
                    "*.googleapis.com", "googleapis.com",
                    "*.gstatic.com", "gstatic.com",
                    "*.googleusercontent.com", "googleusercontent.com",
                    "*.clients6.google.com", "clients6.google.com",
                    "clients1.google.com", "clients2.google.com", "clients3.google.com", "clients4.google.com", "clients5.google.com",
                    "play.google.com", "play.googleapis.com",
                    "passkeys.google.com", "fido.google.com",
                    "csp.withgoogle.com",
                    "recaptcha.net", "*.recaptcha.net", "www.recaptcha.net",
                    "lh3.google.com", "ogs.google.com"
                };
                foreach (string h in googleHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 2. Microsoft Copilot & Education Authentication Helper Expansion
            bool hasCopilot = effectiveList.Exists(d => (d ?? "").IndexOf("copilot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hasCopilot)
            {
                string[] copilotHelpers = new string[]
                {
                    "copilot.microsoft.com", "*.copilot.microsoft.com",
                    "copilot.cloud.microsoft", "*.cloud.microsoft",
                    "sydney.bing.com", "edgeservices.bing.com", "*.bing.net",
                    "login.live.com", "login.microsoftonline.com", "account.microsoft.com",
                    "*.msauth.net", "*.msftauth.net", "auth.gfx.ms", "aadcdn.msauth.net",
                    "msedge.api.cdp.microsoft.com", "config.edge.skype.com",
                    "challenges.cloudflare.com",
                    "assets.msn.com", "ai.microsoftol.com", "fpt.dfp.microsoft.com"
                };
                foreach (string h in copilotHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 3. Ministry of Education (EduLogin & Government SSO) Helper Expansion
            bool hasEdu = effectiveList.Exists(d => {
                string s = (d ?? "").ToLowerInvariant();
                return s.Contains("edu.gov.il") || s.Contains("education.gov.il");
            });
            if (hasEdu)
            {
                string[] eduHelpers = new string[]
                {
                    "edu.gov.il", "*.edu.gov.il",
                    "education.gov.il", "*.education.gov.il",
                    "gov.il", "*.gov.il",
                    "cet.ac.il", "*.cet.ac.il"
                };
                foreach (string h in eduHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 4. One-Class School System Helper Expansion
            bool hasOneClass = effectiveList.Exists(d => (d ?? "").IndexOf("one-class.co.il", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hasOneClass)
            {
                string[] oneClassHelpers = new string[]
                {
                    "*.supabase.co", "supabase.co",
                    "*.supabase.in", "supabase.in",
                    "*.r2.dev",
                    "fonts.googleapis.com", "fonts.gstatic.com",
                    "accounts.google.com", "*.accounts.google.com",
                    "myaccount.google.com", "*.myaccount.google.com",
                    "accounts.youtube.com",
                    "apis.google.com", "*.googleapis.com", "googleapis.com",
                    "ssl.gstatic.com", "www.gstatic.com", "*.gstatic.com", "gstatic.com",
                    "*.googleusercontent.com", "googleusercontent.com",
                    "play.google.com", "play.googleapis.com",
                    "passkeys.google.com", "fido.google.com", "csp.withgoogle.com",
                    "recaptcha.net", "*.recaptcha.net", "www.recaptcha.net",
                    "connector-gateway.lovable.dev", "api.elevenlabs.io",
                    "challenges.cloudflare.com",
                    "edu.gov.il", "*.edu.gov.il",
                    "education.gov.il", "*.education.gov.il",
                    "gov.il", "*.gov.il",
                    "cet.ac.il", "*.cet.ac.il",
                    "mashov.info", "*.mashov.info"
                };
                foreach (string h in oneClassHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 5. Wikipedia Helper Expansion
            bool hasWikipedia = effectiveList.Exists(d => (d ?? "").IndexOf("wikipedia.org", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hasWikipedia)
            {
                string[] wikiHelpers = new string[]
                {
                    "wikimedia.org", "*.wikimedia.org", "upload.wikimedia.org"
                };
                foreach (string h in wikiHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 6. Canva Helper Expansion
            bool hasCanva = effectiveList.Exists(d => (d ?? "").IndexOf("canva.com", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hasCanva)
            {
                string[] canvaHelpers = new string[]
                {
                    "canva-assets.com", "*.canva-assets.com"
                };
                foreach (string h in canvaHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            // 7. Kahoot Helper Expansion
            bool hasKahoot = effectiveList.Exists(d => (d ?? "").IndexOf("kahoot", StringComparison.OrdinalIgnoreCase) >= 0);
            if (hasKahoot)
            {
                string[] kahootHelpers = new string[]
                {
                    "kahoot.it", "*.kahoot.it", "kahoot.com", "*.kahoot.com"
                };
                foreach (string h in kahootHelpers)
                {
                    if (!effectiveList.Contains(h)) effectiveList.Add(h);
                }
            }

            List<string> sanitizedList = new List<string>();
            foreach (string rawItem in effectiveList)
            {
                string cleanItem = (rawItem ?? "").Trim().ToLowerInvariant();
                if (IsSafePacDomainPattern(cleanItem) && !sanitizedList.Contains(cleanItem))
                {
                    sanitizedList.Add(cleanItem);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("function FindProxyForURL(url, host) {");
            sb.AppendLine("    host = host.toLowerCase();");
            sb.AppendLine("    var FILTER_ENABLED = " + (filterEnabled ? "true" : "false") + ";");
            sb.AppendLine("    if (!FILTER_ENABLED) { return \"DIRECT\"; }");
            sb.AppendLine("    if ((host === \"www.google.com\" || host === \"google.com\") && url.indexOf(\"/recaptcha\") >= 0) { return \"DIRECT\"; }");
            sb.AppendLine("    if (isPlainHostName(host) || shExpMatch(host, \"*.local\") || shExpMatch(host, \"localhost\") || shExpMatch(host, \"127.*\") || shExpMatch(host, \"10.*\") || shExpMatch(host, \"192.168.*\") || shExpMatch(host, \"172.16.*\") || shExpMatch(host, \"172.17.*\") || shExpMatch(host, \"172.18.*\") || shExpMatch(host, \"172.19.*\") || shExpMatch(host, \"172.2*.*\") || shExpMatch(host, \"172.30.*\") || shExpMatch(host, \"172.31.*\")) {");
            sb.AppendLine("        return \"DIRECT\";");
            sb.AppendLine("    }");
            sb.AppendLine("    var whitelist = [");
            for (int i = 0; i < sanitizedList.Count; i++)
            {
                string item = sanitizedList[i];
                string comma = (i < sanitizedList.Count - 1) ? "," : "";
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
            sb.AppendLine("    return \"PROXY 127.0.0.1:" + ActiveSinkholePort + "\";");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static bool IsTechnicalHelperDomain(string clean)
        {
            if (string.IsNullOrEmpty(clean)) return true;
            if (clean.StartsWith("accounts.") || clean.StartsWith("myaccount.") || clean.StartsWith("oauth.")) return true;
            if (clean.EndsWith(".googleapis.com") || clean == "googleapis.com") return true;
            if (clean.EndsWith(".gstatic.com") || clean == "gstatic.com") return true;
            if (clean.EndsWith(".googleusercontent.com") || clean == "googleusercontent.com") return true;
            if (clean.EndsWith(".supabase.co") || clean == "supabase.co") return true;
            if (clean.EndsWith(".supabase.in") || clean == "supabase.in") return true;
            if (clean.EndsWith(".r2.dev") || clean == "r2.dev") return true;
            if (clean.EndsWith(".msauth.net") || clean == "msauth.net") return true;
            if (clean.EndsWith(".msftauth.net") || clean == "msftauth.net") return true;
            if (clean.EndsWith(".bing.net") || clean == "bing.net") return true;
            if (clean.EndsWith(".wikimedia.org") || clean == "wikimedia.org") return true;
            if (clean.EndsWith(".canva-assets.com") || clean == "canva-assets.com") return true;
            if (clean == "apis.google.com" || clean == "play.google.com" || clean == "play.googleapis.com" ||
                clean == "passkeys.google.com" || clean == "fido.google.com" || clean == "csp.withgoogle.com" ||
                clean == "lh3.google.com" || clean == "ogs.google.com" || clean == "recaptcha.net" ||
                clean == "challenges.cloudflare.com" || clean == "connector-gateway.lovable.dev" ||
                clean == "api.elevenlabs.io" || clean == "auth.gfx.ms" || clean == "aadcdn.msauth.net" ||
                clean == "assets.msn.com" || clean == "ai.microsoftol.com" || clean == "fpt.dfp.microsoft.com" ||
                clean == "config.edge.skype.com" || clean == "sydney.bing.com" || clean == "edgeservices.bing.com" ||
                clean == "accounts.youtube.com" || clean == "clients6.google.com" || clean == "clients1.google.com" ||
                clean == "clients2.google.com" || clean == "clients3.google.com" || clean == "clients4.google.com" ||
                clean == "clients5.google.com" || clean == "copilot.cloud.microsoft" || clean == "cloud.microsoft" ||
                clean == "bard.google.com")
            {
                return true;
            }
            return false;
        }

        private static List<string> ExtractCleanBaseDomains(List<string> rawWhitelist)
        {
            List<string> baseDomains = new List<string>();
            if (rawWhitelist == null) return baseDomains;
            foreach (string item in rawWhitelist)
            {
                string clean = item.Trim().ToLowerInvariant();
                if (clean.StartsWith("*.")) clean = clean.Substring(2);
                if (string.IsNullOrEmpty(clean) || IsTechnicalHelperDomain(clean) || !IsSafePacDomainPattern(clean)) continue;
                if (!baseDomains.Contains(clean))
                {
                    baseDomains.Add(clean);
                }
            }
            return baseDomains;
        }

        private static string FetchCloudRoomJson(string projectId, string roomId, string userAgent)
        {
            string rtdbUrl = "https://" + projectId + "-default-rtdb.firebaseio.com/rooms/" + Uri.EscapeDataString(roomId) + ".json";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(rtdbUrl);
                req.Proxy = null;
                req.Timeout = 3500;
                req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
                req.Headers[HttpRequestHeader.CacheControl] = "no-cache, no-store, must-revalidate";
                req.Headers[HttpRequestHeader.Pragma] = "no-cache";
                req.UserAgent = userAgent;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    if (!string.IsNullOrEmpty(json) && json.Trim() != "null")
                    {
                        return json;
                    }
                }
            }
            catch {}

            string firestoreUrl = "https://firestore.googleapis.com/v1/projects/" + projectId +
                                  "/databases/(default)/documents/rooms/" + Uri.EscapeDataString(roomId);
            HttpWebRequest fsReq = (HttpWebRequest)WebRequest.Create(firestoreUrl);
            fsReq.Proxy = null;
            fsReq.Timeout = 3500;
            fsReq.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            fsReq.Headers[HttpRequestHeader.CacheControl] = "no-cache, no-store, must-revalidate";
            fsReq.Headers[HttpRequestHeader.Pragma] = "no-cache";
            fsReq.UserAgent = userAgent;

            using (HttpWebResponse fsResp = (HttpWebResponse)fsReq.GetResponse())
            using (StreamReader fsSr = new StreamReader(fsResp.GetResponseStream(), Encoding.UTF8))
            {
                return fsSr.ReadToEnd();
            }
        }

        private static bool ParseFirestoreRoomJson(
            string json,
            out bool filterEnabled,
            out List<string> whitelist,
            out string roomLabel,
            out string updatedAt,
            out string remoteCommand,
            out string remoteCommandId)
        {
            filterEnabled = true;
            whitelist = new List<string>();
            roomLabel = GetConfiguredRoomDisplayLabel();
            updatedAt = "";
            remoteCommand = "none";
            remoteCommandId = "0";

            if (string.IsNullOrEmpty(json) || json.Trim() == "null") return false;

            try
            {
                // Support both clean RTDB JSON ("filterEnabled":true) and Firestore JSON ("filterEnabled":{"booleanValue":true})
                Match boolMatch = Regex.Match(json, "\"filterEnabled\"\\s*:\\s*(?:\\{\\s*\"booleanValue\"\\s*:\\s*)?(true|false)", RegexOptions.IgnoreCase);
                if (boolMatch.Success)
                {
                    filterEnabled = string.Equals(boolMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
                }

                // Support per-computer lock/unlock override ("computerOverrides": { "NISHMAS05": "open" | "locked" })
                string myPc = (Environment.MachineName ?? "").Trim();
                if (!string.IsNullOrEmpty(myPc))
                {
                    int ovIdx = json.IndexOf("\"computerOverrides\"", StringComparison.OrdinalIgnoreCase);
                    if (ovIdx >= 0)
                    {
                        string afterOv = json.Substring(ovIdx);
                        Match pcOvMatch = Regex.Match(
                            afterOv,
                            "\"" + Regex.Escape(myPc) + "\"\\s*:\\s*(?:\\{\\s*\"stringValue\"\\s*:\\s*)?\"(open|locked|allow|block)\"",
                            RegexOptions.IgnoreCase
                        );
                        if (pcOvMatch.Success)
                        {
                            string ovMode = pcOvMatch.Groups[1].Value.ToLowerInvariant();
                            if (ovMode == "open" || ovMode == "allow")
                            {
                                filterEnabled = false;
                            }
                            else if (ovMode == "locked" || ovMode == "block")
                            {
                                filterEnabled = true;
                            }
                        }
                    }
                }

                Match rMatch = Regex.Match(json, "\"name\"\\s*:\\s*(?:\\{\\s*\"stringValue\"\\s*:\\s*)?\"([^\"]+)\"");
                if (rMatch.Success && !string.IsNullOrEmpty(rMatch.Groups[1].Value) && !rMatch.Groups[1].Value.StartsWith("projects/"))
                {
                    roomLabel = rMatch.Groups[1].Value;
                }

                Match uMatch = Regex.Match(json, "\"updatedAt\"\\s*:\\s*(?:\\{\\s*\"stringValue\"\\s*:\\s*)?\"([^\"]+)\"");
                if (uMatch.Success)
                {
                    updatedAt = uMatch.Groups[1].Value;
                }

                Match cmdMatch = Regex.Match(json, "\"remoteCommand\"\\s*:\\s*(?:\\{\\s*\"stringValue\"\\s*:\\s*)?\"([^\"]+)\"");
                if (cmdMatch.Success)
                {
                    remoteCommand = cmdMatch.Groups[1].Value.Trim().ToLowerInvariant();
                }

                Match cmdIdMatch = Regex.Match(json, "\"remoteCommandId\"\\s*:\\s*(?:\\{\\s*\"stringValue\"\\s*:\\s*)?\"([^\"]+)\"");
                if (cmdIdMatch.Success)
                {
                    remoteCommandId = cmdIdMatch.Groups[1].Value.Trim();
                }

                int wlIdx = json.IndexOf("\"whitelist\"", StringComparison.OrdinalIgnoreCase);
                if (wlIdx >= 0)
                {
                    int arrStart = json.IndexOf("[", wlIdx);
                    int arrEnd = (arrStart >= 0) ? json.IndexOf("]", arrStart) : -1;
                    if (arrStart >= 0 && arrEnd > arrStart)
                    {
                        string wlSection = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                        MatchCollection fsMatches = Regex.Matches(wlSection, "\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
                        if (fsMatches.Count > 0)
                        {
                            foreach (Match m in fsMatches)
                            {
                                string domain = m.Groups[1].Value.Trim().ToLowerInvariant();
                                if (!string.IsNullOrEmpty(domain))
                                {
                                    string baseDom = domain.StartsWith("*.") ? domain.Substring(2) : domain;
                                    if (IsSafePacDomainPattern(baseDom))
                                    {
                                        if (!whitelist.Contains(baseDom)) whitelist.Add(baseDom);
                                        if (!whitelist.Contains("*." + baseDom)) whitelist.Add("*." + baseDom);
                                    }
                                }
                            }
                        }
                        else
                        {
                            MatchCollection rtdbMatches = Regex.Matches(wlSection, "\"([^\"]+)\"");
                            foreach (Match m in rtdbMatches)
                            {
                                string domain = m.Groups[1].Value.Trim().ToLowerInvariant();
                                if (!string.IsNullOrEmpty(domain))
                                {
                                    string baseDom = domain.StartsWith("*.") ? domain.Substring(2) : domain;
                                    if (IsSafePacDomainPattern(baseDom))
                                    {
                                        if (!whitelist.Contains(baseDom)) whitelist.Add(baseDom);
                                        if (!whitelist.Contains("*." + baseDom)) whitelist.Add("*." + baseDom);
                                    }
                                }
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

        private static void ExecuteRemoteUninstall()
        {
            if (!IsAdministrator()) return;

            try
            {
                Environment.CurrentDirectory = Path.GetTempPath();
            }
            catch {}

            // 1. Immediately restore direct internet and remove firewall rules
            DisableFilter(false);
            UnregisterComputerFromFirestore();

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
                foreach (string taskName in new string[] { "SchoolFilterSystemAgent", "SchoolFilterGuard", "SchoolFilterRemoteUninstall", "SchoolFilterRemoteUpdate", "SchoolFilterElevatedExec", "SchoolFilterBootstrapAdmin" })
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
                @"SOFTWARE\Policies\Microsoft\Edge",
                @"SOFTWARE\Policies\BraveSoftware\Brave"
            };
            foreach (string path in policyPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("ProxyMode", false);
                            key.DeleteValue("QuicAllowed", false);
                            key.DeleteValue("DnsOverHttpsMode", false);
                            key.DeleteValue("AllowDinosaurEasterEgg", false);
                            key.DeleteValue("AllowSurfGame", false);
                            key.DeleteValue("BlockExternalExtensions", false);
                            key.DeleteValue("DeveloperToolsAvailability", false);
                            key.DeleteSubKeyTree("ExtensionInstallBlocklist", false);
                            key.DeleteSubKeyTree("ExtensionInstallAllowlist", false);
                        }
                    }
                }
                catch {}
            }
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Policies\Mozilla\Firefox", false);
            }
            catch {}

            try
            {
                using (RegistryKey storeKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsStore", true))
                {
                    if (storeKey != null)
                    {
                        storeKey.DeleteValue("RemoveWindowsStore", false);
                    }
                }
            }
            catch {}

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

            // 3. Spawn detached cleanup batch script from protected Windows\Temp directory
            try
            {
                string sysTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (!Directory.Exists(sysTemp)) sysTemp = Path.GetTempPath();
                string batchCleanup = Path.Combine(sysTemp, "SchoolFilter_RemoteUninstall_" + Guid.NewGuid().ToString("N") + ".bat");
                string cleanupScript = string.Format(
                    "@echo off\r\n" +
                    "cd /d \"{2}\"\r\n" +
                    "ping 127.0.0.1 -n 2 > nul\r\n" +
                    "taskkill /F /IM SchoolFilterCtl.exe >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterSystemAgent\" /F >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterRemoteUninstall\" /F >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterRemoteUpdate\" /F >nul 2>&1\r\n" +
                    "schtasks /Delete /TN \"SchoolFilterElevatedExec\" /F >nul 2>&1\r\n" +
                    "icacls \"{0}\" /reset /T /C /Q >nul 2>&1\r\n" +
                    "icacls \"{1}\" /reset /T /C /Q >nul 2>&1\r\n" +
                    "for /L %%i in (1,1,15) do (\r\n" +
                    "    if exist \"{0}\" (\r\n" +
                    "        rmdir /S /Q \"{0}\" >nul 2>&1\r\n" +
                    "        if exist \"{0}\" ping 127.0.0.1 -n 2 > nul\r\n" +
                    "    )\r\n" +
                    ")\r\n" +
                    "if exist \"{1}\" rmdir /S /Q \"{1}\" >nul 2>&1\r\n" +
                    "del \"%~f0\" >nul 2>&1\r\n",
                    InstallDir,
                    DataDir,
                    sysTemp
                );
                File.WriteAllText(batchCleanup, cleanupScript);

                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchCleanup + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = sysTemp
                };
                Process.Start(psi);
            }
            catch {}
        }

        private static string ComputeFileSha256Hex(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static bool VerifyDownloadedInstaller(string filePath, string sourceUrl)
        {
            try
            {
                FileInfo fi = new FileInfo(filePath);
                if (!fi.Exists || fi.Length < 40000 || fi.Length > 25 * 1024 * 1024)
                {
                    return false;
                }

                // 1. If a companion .sha256 digest file is published on the HTTPS server, strictly enforce hash match
                try
                {
                    string shaUrl = sourceUrl + ".sha256?t=" + DateTime.UtcNow.Ticks;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(shaUrl);
                    req.Proxy = null;
                    req.Timeout = 5000;
                    req.UserAgent = "SchoolFilter-Updater/3.6.2";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string rawHashText = (sr.ReadToEnd() ?? "").Trim();
                        string expectedHash = rawHashText.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim().ToLowerInvariant();
                        if (expectedHash.Length == 64 && Regex.IsMatch(expectedHash, "^[0-9a-f]{64}$"))
                        {
                            string actualHash = ComputeFileSha256Hex(filePath).ToLowerInvariant();
                            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                        }
                    }
                }
                catch
                {
                    // If .sha256 companion file is not yet published on the static host, fall through to strict binary structure verification
                }

                // 2. Verify PE header & .NET Assembly structure without executing any code (prevents third-party binaries)
                byte[] rawBytes = File.ReadAllBytes(filePath);
                if (rawBytes.Length < 40000 || rawBytes[0] != (byte)'M' || rawBytes[1] != (byte)'Z')
                {
                    return false;
                }

                Assembly asm = Assembly.ReflectionOnlyLoad(rawBytes);
                if (asm == null) return false;

                Type setupType = asm.GetType("SchoolFilter.Setup.Program", false, false);
                if (setupType == null) return false;

                string[] resources = asm.GetManifestResourceNames();
                bool hasCtlResource = false;
                bool hasPacResource = false;
                if (resources != null)
                {
                    foreach (string res in resources)
                    {
                        if (string.Equals(res, "SchoolFilterCtl.exe", StringComparison.OrdinalIgnoreCase)) hasCtlResource = true;
                        if (string.Equals(res, "filter.pac", StringComparison.OrdinalIgnoreCase)) hasPacResource = true;
                    }
                }

                return hasCtlResource && hasPacResource;
            }
            catch
            {
                return false;
            }
        }

        private static bool ExecuteRemoteUpdate()
        {
            // Security hardening: only SYSTEM / Administrator may download and execute an update
            if (!IsAdministrator())
            {
                return false;
            }

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                ServicePointManager.ServerCertificateValidationCallback = null;

                // Strictly hardcoded official HTTPS endpoints only (no config.ini override, no file:// or local paths)
                string[] candidateUrls = new string[]
                {
                    "https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe",
                    "https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/dist/SchoolFilter_Setup.exe"
                };

                LockDataDirectoryPermissions();
                string securePayloadPath = Path.Combine(DataDir, "remote_update_setup.exe");
                bool downloadedAndVerified = false;

                foreach (string url in candidateUrls)
                {
                    try
                    {
                        if (File.Exists(securePayloadPath))
                        {
                            File.SetAttributes(securePayloadPath, FileAttributes.Normal);
                            File.Delete(securePayloadPath);
                        }

                        string bustUrl = url + "?t=" + DateTime.UtcNow.Ticks;
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(bustUrl);
                        req.Proxy = null;
                        req.Timeout = 15000;
                        req.UserAgent = "SchoolFilter-Updater/3.6.2";
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        using (Stream s = resp.GetResponseStream())
                        using (FileStream fs = new FileStream(securePayloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            s.CopyTo(fs);
                        }

                        if (VerifyDownloadedInstaller(securePayloadPath, url))
                        {
                            downloadedAndVerified = true;
                            break;
                        }
                        else
                        {
                            try { File.Delete(securePayloadPath); } catch {}
                        }
                    }
                    catch {}
                }

                if (!downloadedAndVerified || !File.Exists(securePayloadPath))
                {
                    return false;
                }

                LockDataDirectoryPermissions();

                string roomId = SanitizeDocIdPart(GetConfigValue("RoomId", DefaultRoomId));
                string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SchoolFilter");
                string dirArg = string.Equals(InstallDir, defaultDir, StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : " \"/DIR=" + InstallDir + "\"";
                string setupArgs = "/STUDENT /ROOM=" + roomId + dirArg + " /VERYSILENT /SUPPRESSMSGBOXES";

                // Execute the verified installer directly from the SYSTEM-locked directory
                ProcessStartInfo psi = new ProcessStartInfo(securePayloadPath, setupArgs)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = DataDir
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
                if (!File.Exists(ctlPath)) return;
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 8 > nul & \"" + ctlPath + "\" watchdog")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = InstallDir
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
            if (!IsAdministrator()) return;

            bool createdNew;
            using (Mutex mutex = CreateScopedMutex(SystemAgentMutexName, out createdNew, true))
            {
                if (mutex == null || !createdNew)
                {
                    return;
                }

                // If no _v350 sinkhole is running yet, any other SchoolFilterCtl process is a legacy process holding port 9999; terminate it
                if (!IsSinkholeRunning())
                {
                    try
                    {
                        int myPid = Process.GetCurrentProcess().Id;
                        foreach (Process p in Process.GetProcessesByName("SchoolFilterCtl"))
                        {
                            try
                            {
                                if (p.Id != myPid)
                                {
                                    p.Kill();
                                    p.WaitForExit(1000);
                                }
                            }
                            catch {}
                        }
                    }
                    catch {}
                }

                // Ensure ProgramData\SchoolFilter is strictly locked to SYSTEM & Administrators only
                LockDataDirectoryPermissions();

                // Add Windows Defender exclusions so SchoolFilter is never falsely flagged
                try
                {
                    ProcessStartInfo mpPsi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"try { Add-MpPreference -ExclusionPath '" + InstallDir + "','" + DataDir + "' -ExclusionProcess 'SchoolFilterCtl.exe','SchoolFilter_Setup.exe' -ErrorAction SilentlyContinue } catch {}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using (Process p = Process.Start(mpPsi))
                    {
                        if (p != null) p.WaitForExit(3000);
                    }
                }
                catch {}

                // Clean up any legacy insecure scheduled tasks or trigger files from older versions
                try
                {
                    string legacyTrigger = Path.Combine(DataDir, "trigger_update.txt");
                    if (File.Exists(legacyTrigger)) File.Delete(legacyTrigger);
                    foreach (string legacyTask in new string[] { "SchoolFilterRemoteUninstall", "SchoolFilterRemoteUpdate", "SchoolFilterElevatedExec", "SchoolFilterBootstrapAdmin" })
                    {
                        ProcessStartInfo stPsi = new ProcessStartInfo("schtasks.exe", "/Delete /TN \"" + legacyTask + "\" /F")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        };
                        using (Process p = Process.Start(stPsi))
                        {
                            if (p != null) p.WaitForExit(1000);
                        }
                    }
                }
                catch {}

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                bool lastAppliedFirewall = false;
                bool? lastEnforcedState = null;
                string lastSeenUpdatedAt = null;
                int tickCounter = 0;
                bool running = true;

                // Report initial heartbeat immediately at SYSTEM agent startup
                ReportComputerHeartbeatToFirestore(ReadLocalOverride() != "allow");

                while (running)
                {
                    try
                    {
                        string projectId = GetConfigValue("FirebaseProjectId", DefaultFirebaseProjectId);
                        string roomId = GetConfigValue("RoomId", DefaultRoomId);
                        string json = FetchCloudRoomJson(projectId, roomId, "SchoolFilter-SystemAgent/3.6.2");
                        bool cloudFilterEnabled;
                        List<string> whitelist;
                        string roomLabel;
                        string roomUpdatedAt;
                        string remoteCommand;
                        string remoteCommandId;

                        if (ParseFirestoreRoomJson(json, out cloudFilterEnabled, out whitelist, out roomLabel, out roomUpdatedAt, out remoteCommand, out remoteCommandId))
                        {
                            if (ShouldExecuteRemoteCommand(remoteCommand, remoteCommandId, true))
                            {
                                SaveLastExecutedCommandId(remoteCommandId, true);
                                if (remoteCommand == "uninstall")
                                {
                                    running = false;
                                    ExecuteRemoteUninstall();
                                    return;
                                }
                                else if (remoteCommand == "update")
                                {
                                    if (ExecuteRemoteUpdate())
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
                                    ReportComputerHeartbeatToFirestore(true);
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
                                    EnforceBrowserExtensionPolicies(false);
                                    lastAppliedFirewall = false;
                                }
                                lastEnforcedState = false;
                            }

                            if (!IsSinkholeRunning())
                            {
                                EnsureSinkholeRunning();
                            }

                            if (tickCounter % 6 == 0)
                            {
                                ReportComputerHeartbeatToFirestore(shouldEnforce);
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

        private static void RunUserSessionWatchdogLoop()
        {
            int counter = 0;
            while (true)
            {
                try
                {
                    string localOverride = ReadLocalOverride();
                    bool shouldBeActive = (localOverride != "allow");

                    try
                    {
                        if (File.Exists(CachedPacFilePath))
                        {
                            string pacContent = File.ReadAllText(CachedPacFilePath, Encoding.ASCII);
                            if (pacContent.Contains("var FILTER_ENABLED = false;"))
                            {
                                shouldBeActive = false;
                            }
                            else if (pacContent.Contains("var FILTER_ENABLED = true;"))
                            {
                                shouldBeActive = true;
                            }
                        }
                    }
                    catch {}

                    if (localOverride == "allow") shouldBeActive = false;
                    else if (localOverride == "block") shouldBeActive = true;

                    // Enforce HKCU proxy in current user session
                    using (RegistryKey inetKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                    {
                        if (inetKey != null)
                        {
                            string currUrl = inetKey.GetValue("AutoConfigURL") as string;
                            string targetUrl = GetLocalPacEndpointUrl(counter);
                            if (shouldBeActive)
                            {
                                if (string.IsNullOrEmpty(currUrl) || currUrl.IndexOf("127.0.0.1:" + ActiveSinkholePort, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    ApplyPacToRegistryRoot(Registry.CurrentUser, true, targetUrl);
                                    NotifyBrowserSettingsChanged();
                                }
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(currUrl))
                                {
                                    ApplyPacToRegistryRoot(Registry.CurrentUser, false, "");
                                    NotifyBrowserSettingsChanged();
                                }
                            }
                        }
                    }

                    if (shouldBeActive)
                    {
                        EnforceInstalledGameBlock();
                    }

                    if (!IsSinkholeRunning())
                    {
                        EnsureSinkholeRunning();
                    }
                }
                catch {}

                counter++;
                Thread.Sleep(2500);
            }
        }

        private static void RunSinkholeAndCloudSyncDaemon()
        {
            if (IsSinkholeRunning())
            {
                return;
            }

            bool createdNew;
            using (Mutex mutex = CreateScopedMutex(SinkholeMutexName, out createdNew, IsAdministrator()))
            {
                if (mutex == null || !createdNew)
                {
                    return;
                }

                // If running elevated and no _v350 SystemAgent is active yet, clean up any legacy pre-v350 SchoolFilterCtl process holding port 9999
                if (IsAdministrator() && !IsSystemAgentRunning())
                {
                    try
                    {
                        int myPid = Process.GetCurrentProcess().Id;
                        foreach (Process p in Process.GetProcessesByName("SchoolFilterCtl"))
                        {
                            try
                            {
                                if (p.Id != myPid)
                                {
                                    p.Kill();
                                    p.WaitForExit(1000);
                                }
                            }
                            catch {}
                        }
                    }
                    catch {}
                }

                CurrentPacScript = LoadInitialPacScript();
                CurrentRoomDisplayLabel = GetConfiguredRoomDisplayLabel();

                long initialCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                ReportComputerHeartbeatToFirestore(ReadLocalOverride() != "allow");
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
                                    string json = FetchCloudRoomJson(projectId, roomId, "SchoolFilter-RoomDaemon/3.6.2");
                                    bool cloudFilterEnabled;
                                    List<string> whitelist;
                                    string roomLabel;
                                    string roomUpdatedAt;
                                    string remoteCommand;
                                    string remoteCommandId;

                                    if (ParseFirestoreRoomJson(json, out cloudFilterEnabled, out whitelist, out roomLabel, out roomUpdatedAt, out remoteCommand, out remoteCommandId))
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
                                                if (IsAdministrator())
                                                {
                                                    ExecuteRemoteUninstall();
                                                }
                                                return;
                                            }
                                            else if (remoteCommand == "update")
                                            {
                                                if (!IsAdministrator())
                                                {
                                                    // Let the SYSTEM agent perform the elevated update while we unlock SchoolFilterCtl.exe
                                                    // and schedule a user-session restart once the update finishes.
                                                    SpawnDelayedUserSessionRestart();
                                                    running = false;
                                                    try { if (listener != null) listener.Stop(); } catch {}
                                                    return;
                                                }
                                                else if (ExecuteRemoteUpdate())
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
                                if (IsAdministrator())
                                {
                                    try
                                    {
                                        if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                                        File.WriteAllText(CachedPacFilePath, newPac, Encoding.ASCII);
                                    }
                                    catch {}
                                }

                                currentCacheBuster = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                                lastGeneratedPac = newPac;

                                if (shouldEnforceBlock)
                                {
                                    RefreshPacSettings(currentCacheBuster);
                                    ReportComputerHeartbeatToFirestore(true);
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
                                    EnforceBrowserExtensionPolicies(false);
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

                while (running)
                {
                    try
                    {
                        try
                        {
                            ActiveSinkholePort = SinkholePort;
                            listener = new TcpListener(IPAddress.Loopback, ActiveSinkholePort);
                            listener.Start();
                        }
                        catch
                        {
                            if (listener != null)
                            {
                                try { listener.Stop(); } catch {}
                                listener = null;
                            }
                            try
                            {
                                using (TcpClient legacyClient = new TcpClient())
                                {
                                    IAsyncResult ar = legacyClient.BeginConnect(IPAddress.Loopback, SinkholePort, null, null);
                                    if (ar.AsyncWaitHandle.WaitOne(150))
                                    {
                                        legacyClient.EndConnect(ar);
                                        NetworkStream ns = legacyClient.GetStream();
                                        byte[] stopBytes = Encoding.ASCII.GetBytes("STOP_SINKHOLE\r\n\r\n");
                                        ns.Write(stopBytes, 0, stopBytes.Length);
                                        ns.Flush();
                                    }
                                }
                                Thread.Sleep(250);
                            }
                            catch {}

                            try
                            {
                                ActiveSinkholePort = SinkholePort;
                                listener = new TcpListener(IPAddress.Loopback, ActiveSinkholePort);
                                listener.Start();
                            }
                            catch
                            {
                                if (listener != null)
                                {
                                    try { listener.Stop(); } catch {}
                                    listener = null;
                                }
                                ActiveSinkholePort = 9998;
                                listener = new TcpListener(IPAddress.Loopback, ActiveSinkholePort);
                                listener.Start();
                                if (!string.IsNullOrEmpty(CurrentPacScript))
                                {
                                    CurrentPacScript = CurrentPacScript.Replace("127.0.0.1:9999", "127.0.0.1:9998");
                                }
                                if (IsFilterCurrentlyEnforced)
                                {
                                    long cb = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                                    RefreshPacSettings(cb);
                                }
                            }
                        }

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

                                    // Security hardening: STOP_SINKHOLE over unauthenticated TCP is removed so local processes cannot kill the filter

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
                                    // still sent a cached proxy request to 127.0.0.1:9999, tunnel it directly to public HTTP/HTTPS (ports 80/443 only)
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
                    catch
                    {
                        if (listener != null)
                        {
                            try { listener.Stop(); } catch {}
                            listener = null;
                        }
                        if (running)
                        {
                            Thread.Sleep(2000);
                        }
                    }
                }
            }
        }

        private static bool IsPrivateOrLoopbackHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            string h = host.Trim().ToLowerInvariant();
            if (h == "localhost" || h.EndsWith(".local") || h.StartsWith("127.") || h.StartsWith("10.") || h.StartsWith("192.168.") || h == "::1" || h == "[::1]")
            {
                return true;
            }
            IPAddress ip;
            if (IPAddress.TryParse(h, out ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                byte[] b = ip.GetAddressBytes();
                if (b.Length == 4)
                {
                    if (b[0] == 10 || b[0] == 127 || b[0] == 0) return true;
                    if (b[0] == 192 && b[1] == 168) return true;
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                    if (b[0] == 169 && b[1] == 254) return true;
                }
            }
            return false;
        }

        private static bool TryTunnelUnlockedProxyRequest(TcpClient clientSocket, NetworkStream clientStream, byte[] initialBuf, int initialRead, string reqText, string fallbackHost)
        {
            try
            {
                if (IsFilterCurrentlyEnforced || string.IsNullOrEmpty(reqText)) return false;
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

                    // Security hardening: only allow standard HTTPS (443) or HTTP (80) to non-private hosts
                    if ((port != 443 && port != 80) || IsPrivateOrLoopbackHost(host))
                    {
                        return false;
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
                else if (!string.IsNullOrEmpty(fallbackHost) && !IsPrivateOrLoopbackHost(fallbackHost))
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

        private static readonly string[] GameWindowTitleKeywords = new string[]
        {
            "asphalt", "legends unite", "gameloft",
            "hill climb", "subway surfers", "candy crush", "geometry dash",
            "stumble guys", "brawl stars", "brawlhalla", "clash royale", "clash of clans",
            "fruit ninja", "temple run", "angry birds", "sonic dash",
            "forza horizon", "forza motorsport", "modern combat", "sniper fury",
            "minion rush", "dragon mania", "roblox", "minecraft", "fortnite",
            "counter-strike", "half-life", "valorant", "league of legends",
            "overwatch", "hearthstone", "genshin impact", "honkai: star rail",
            "grand theft auto", "fivem", "need for speed", "euro truck",
            "call of duty", "battlefield", "rocket league", "fall guys", "among us",
            "terraria", "solitaire collection", "minesweeper", "purble place"
        };

        private static readonly string[] GameFolderKeywords = new string[]
        {
            "\\games\\", "\\game\\", "\\משחקים\\", "\\משחק\\", "\\steamapps\\", "\\epic games\\",
            "\\gog games\\", "\\riot games\\", "\\origin games\\", "\\ubisoft\\", "\\xboxgames\\",
            "\\plarium\\", "\\tlauncher\\", "\\.minecraft\\", "\\roblox\\", "\\bluestacks\\",
            "\\ldplayer\\", "\\nox\\", "\\memu\\", "\\retroarch\\", "\\dosbox\\",
            "\\microsoft.microsoftsolitairecollection", "\\microsoft.msminesweeper",
            "\\gameloft\\", "\\asphalt\\", "\\a278ab0d."
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

            // Lock down Windows Settings Proxy toggle so students cannot change proxy settings in Windows GUI
            try
            {
                using (RegistryKey ieKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Internet Explorer\Control Panel"))
                {
                    if (ieKey != null)
                    {
                        if (blockExtensions)
                        {
                            ieKey.SetValue("Proxy", 1, RegistryValueKind.DWord);
                        }
                        else
                        {
                            ieKey.DeleteValue("Proxy", false);
                        }
                    }
                }
            }
            catch {}

            // Lock down Microsoft Store so students cannot browse or download games during class
            try
            {
                using (RegistryKey storeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\WindowsStore"))
                {
                    if (storeKey != null)
                    {
                        if (blockExtensions)
                        {
                            storeKey.SetValue("RemoveWindowsStore", 1, RegistryValueKind.DWord);
                        }
                        else
                        {
                            storeKey.DeleteValue("RemoveWindowsStore", false);
                        }
                    }
                }
            }
            catch {}

            // Enforce Mozilla Firefox proxy & DoH policies so Firefox cannot bypass Windows system proxy
            try
            {
                using (RegistryKey ffKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Mozilla\Firefox"))
                {
                    if (ffKey != null)
                    {
                        if (blockExtensions)
                        {
                            ffKey.SetValue("DisableDeveloperTools", 1, RegistryValueKind.DWord);
                            using (RegistryKey proxyKey = ffKey.CreateSubKey("Proxy"))
                            {
                                if (proxyKey != null)
                                {
                                    proxyKey.SetValue("Mode", "autoConfig", RegistryValueKind.String);
                                    proxyKey.SetValue("AutoConfigURL", "http://127.0.0.1:" + ActiveSinkholePort + "/filter.pac", RegistryValueKind.String);
                                    proxyKey.SetValue("Locked", 1, RegistryValueKind.DWord);
                                }
                            }
                            using (RegistryKey dohKey = ffKey.CreateSubKey("DNSOverHTTPS"))
                            {
                                if (dohKey != null)
                                {
                                    dohKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
                                    dohKey.SetValue("Locked", 1, RegistryValueKind.DWord);
                                }
                            }
                        }
                        else
                        {
                            ffKey.DeleteValue("DisableDeveloperTools", false);
                            using (RegistryKey proxyKey = ffKey.CreateSubKey("Proxy"))
                            {
                                if (proxyKey != null)
                                {
                                    proxyKey.SetValue("Mode", "system", RegistryValueKind.String);
                                    proxyKey.DeleteValue("AutoConfigURL", false);
                                    proxyKey.DeleteValue("Locked", false);
                                }
                            }
                            using (RegistryKey dohKey = ffKey.CreateSubKey("DNSOverHTTPS"))
                            {
                                if (dohKey != null)
                                {
                                    dohKey.DeleteValue("Enabled", false);
                                    dohKey.DeleteValue("Locked", false);
                                }
                            }
                        }
                    }
                }
            }
            catch {}
        }

        private static void ApplyBrowserPolicyToHive(RegistryKey root, string prefix, string policyPath, bool blockExtensions)
        {
            try
            {
                using (RegistryKey key = root.CreateSubKey(prefix + policyPath))
                {
                    if (key == null) return;

                    if (blockExtensions)
                    {
                        key.SetValue("ProxyMode", "pac_script", RegistryValueKind.String);
                        key.SetValue("ProxyPacUrl", "http://127.0.0.1:" + ActiveSinkholePort + "/filter.pac", RegistryValueKind.String);
                        key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
                        key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
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
                    else
                    {
                        key.SetValue("ProxyMode", "system", RegistryValueKind.String);
                        key.DeleteValue("ProxyPacUrl", false);
                        key.SetValue("QuicAllowed", 1, RegistryValueKind.DWord);
                        key.DeleteValue("DnsOverHttpsMode", false);
                        key.DeleteValue("AllowDinosaurEasterEgg", false);
                        key.DeleteValue("AllowSurfGame", false);
                        key.DeleteValue("BlockExternalExtensions", false);
                        key.DeleteValue("DeveloperToolsAvailability", false);
                        try { key.DeleteSubKeyTree("ExtensionInstallBlocklist", false); } catch {}
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
                // Note: Do not forcefully kill open browsers so students' open work/tabs are never lost.
                // Group Policy (ExtensionInstallBlocklist) prevents game extensions from executing.
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

            // 1. Direct known game process names
            string knownGameName;
            if (BlockedGameProcesses.TryGetValue(procName, out knownGameName))
            {
                return knownGameName;
            }

            // 2. Java-based games (Minecraft, etc.)
            if (string.Equals(procName, "javaw", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(procName, "java", StringComparison.OrdinalIgnoreCase))
            {
                if (title.IndexOf("Minecraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Lunar Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Badlion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("TLauncher", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Feather Client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Salwyrr", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Prism", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("OptiFine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Forge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf("Fabric", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Minecraft";
                }
                return null;
            }

            // Special check for UWP container ApplicationFrameHost (gracefully close store/game frame)
            if (string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(title))
                {
                    string lowerTitle = title.ToLowerInvariant();
                    if (lowerTitle.Contains("חנות") || lowerTitle.Contains("microsoft store") ||
                        lowerTitle.Contains("asphalt") || lowerTitle.Contains("legends unite"))
                    {
                        isBrowserWindowOnly = true;
                        return "חלון חנות/משחק (" + title + ")";
                    }
                    foreach (string kw in GameWindowTitleKeywords)
                    {
                        if (lowerTitle.Contains(kw))
                        {
                            isBrowserWindowOnly = true;
                            return "חלון משחק (" + title + ")";
                        }
                    }
                }
                return null;
            }

            string exePath = GetProcessExecutablePath(p);
            if (!string.IsNullOrEmpty(exePath))
            {
                string lowerPath = exePath.ToLowerInvariant();
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
                if (!string.IsNullOrEmpty(winDir) && lowerPath.StartsWith(winDir + "\\") && !lowerPath.Contains("\\temp\\"))
                {
                    return null;
                }
                if (lowerPath.StartsWith(InstallDir.ToLowerInvariant()))
                {
                    return null;
                }

                // Microsoft Store (UWP / WindowsApps) game packages
                if (lowerPath.Contains("\\windowsapps\\"))
                {
                    // Whitelist safe built-in Windows utilities
                    if (lowerPath.Contains("\\microsoft.windowscalculator") ||
                        lowerPath.Contains("\\microsoft.screensketch") ||
                        lowerPath.Contains("\\microsoft.windowssnippingtool") ||
                        lowerPath.Contains("\\microsoft.paint") ||
                        lowerPath.Contains("\\microsoft.photos") ||
                        lowerPath.Contains("\\microsoft.windowsnotepad") ||
                        lowerPath.Contains("\\microsoft.windowsterminal") ||
                        lowerPath.Contains("\\microsoft.windowscamera") ||
                        lowerPath.Contains("\\microsoft.windowsaudiostudio") ||
                        lowerPath.Contains("\\microsoft.windows.soundrecorder") ||
                        lowerPath.Contains("\\microsoft.yourphone") ||
                        lowerPath.Contains("\\microsoft.ui.xaml") ||
                        lowerPath.Contains("\\microsoft.vclibs") ||
                        lowerPath.Contains("\\microsoft.net.native"))
                    {
                        return null;
                    }

                    if (lowerPath.Contains("\\microsoft.windowsstore") || procName.Equals("WinStore.App", StringComparison.OrdinalIgnoreCase))
                    {
                        return "חנות האפליקציות של מייקרוסופט (Microsoft Store)";
                    }

                    if (lowerPath.Contains("asphalt") ||
                        lowerPath.Contains("gameloft") ||
                        lowerPath.Contains("a278ab0d") || // Gameloft Store publisher ID
                        lowerPath.Contains("king.com") ||
                        lowerPath.Contains("fingersoft") ||
                        lowerPath.Contains("rovio") ||
                        lowerPath.Contains("supercell") ||
                        lowerPath.Contains("playrix") ||
                        lowerPath.Contains("electronicarts") ||
                        lowerPath.Contains("subwaysurfers") ||
                        lowerPath.Contains("hillclimb") ||
                        lowerPath.Contains("templerun") ||
                        lowerPath.Contains("fruitninja") ||
                        lowerPath.Contains(".minecraft") ||
                        lowerPath.Contains("roblox") ||
                        lowerPath.Contains("forza") ||
                        lowerPath.Contains("solitaire") ||
                        lowerPath.Contains("minesweeper") ||
                        lowerPath.Contains("xbox"))
                    {
                        return !string.IsNullOrEmpty(title) ? title : procName;
                    }
                }

                // 3. Check FileVersionInfo OriginalFilename BEFORE SafeNonGameProcesses (catches games renamed to vlc.exe, python.exe, chrome.exe, etc.!)
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
                        string internalName = Path.GetFileNameWithoutExtension(fvi.InternalName ?? "");
                        if (!string.IsNullOrEmpty(internalName) && BlockedGameProcesses.TryGetValue(internalName, out knownGameName))
                        {
                            return knownGameName;
                        }
                    }
                }
                catch {}

                // 4. Unreal Engine *-Shipping.exe signature
                if (procName.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase) ||
                    lowerPath.EndsWith("-shipping.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrEmpty(title) ? title : procName;
                }

                // 5. Check executable directory for Game Engine / Game SDK DLLs BEFORE SafeNonGameProcesses
                // (so renaming a Unity/Steam game to vlc.exe or firefox.exe still gets caught!)
                try
                {
                    string exeDir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                    {
                        if (Directory.Exists(Path.Combine(exeDir, procName + "_Data")) ||
                            Directory.Exists(Path.Combine(exeDir, "MonoBleedingEdge")) ||
                            Directory.Exists(Path.Combine(exeDir, @"Engine\Binaries")) ||
                            File.Exists(Path.Combine(exeDir, procName + ".pck")) ||
                            File.Exists(Path.Combine(exeDir, "data.pck")) ||
                            Directory.Exists(Path.Combine(exeDir, "pygame")) ||
                            File.Exists(Path.Combine(exeDir, "pygame.dll")))
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

                // 6. Game folder path keywords (\Games\, \משחקים\, \SteamApps\, etc.)
                foreach (string folderKw in GameFolderKeywords)
                {
                    if (lowerPath.Contains(folderKw))
                    {
                        return !string.IsNullOrEmpty(title) ? title : procName;
                    }
                }
            }

            // 7. Check browser windows for offline/extension games or chrome://extensions
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

            // 8. Whitelisted system, study, office, or school processes (verified above not to be renamed games)
            if (SafeNonGameProcesses.Contains(procName))
            {
                return null;
            }

            // 9. Check window title of any desktop / UWP process against known game titles
            if (!string.IsNullOrEmpty(title))
            {
                string lowerTitle = title.ToLowerInvariant();
                foreach (string kw in GameWindowTitleKeywords)
                {
                    if (lowerTitle.Contains(kw))
                    {
                        return "משחק (" + title + ")";
                    }
                }
            }

            return null;
        }

        private static void RecordOrShowKilledGame(string gameDisplayName)
        {
            if (WindowsIdentity.GetCurrent().IsSystem)
            {
                try
                {
                    if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                    long nowSec = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    File.WriteAllText(LastKilledGameFilePath, nowSec.ToString() + "|" + gameDisplayName, Encoding.UTF8);
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
                    string raw = File.ReadAllText(LastKilledGameFilePath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(raw) && !string.Equals(raw, LastSeenKilledGameToken, StringComparison.Ordinal))
                    {
                        LastSeenKilledGameToken = raw;
                        string killedGame = raw;
                        int pipeIdx = raw.IndexOf('|');
                        if (pipeIdx > 0)
                        {
                            long eventSec;
                            long nowSec = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                            if (long.TryParse(raw.Substring(0, pipeIdx), out eventSec))
                            {
                                // Only notify if the game was killed within the last 15 seconds
                                if (Math.Abs(nowSec - eventSec) > 15) return;
                            }
                            killedGame = raw.Substring(pipeIdx + 1).Trim();
                        }
                        if (!string.IsNullOrEmpty(killedGame))
                        {
                            TriggerPopupNotification(killedGame, true);
                        }
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
                    autoConfigUrl.IndexOf("127.0.0.1:" + ActiveSinkholePort, StringComparison.OrdinalIgnoreCase) < 0)
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
                try
                {
                    ApplyPacToRegistryRoot(Registry.CurrentUser, enable, pacUrl);
                }
                catch {}

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
