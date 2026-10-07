using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SchoolFilter.Controller
{
    internal static class Program
    {
        private const string DefaultCloudPacUrl = "https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/src/filter.pac";
        private const int SinkholePort = 9999;

        private static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SchoolFilter"
        );

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

        private static int Main(string[] args)
        {
            string action = (args.Length > 0) ? args[0].Trim().ToLowerInvariant() : "block";

            try
            {
                if (action == "sinkhole")
                {
                    RunSinkholeServer();
                    return 0;
                }
                else if (action == "stop-sinkhole")
                {
                    StopSinkholeServer();
                    return 0;
                }
                else if (action == "allow" || action == "unblock" || action == "off" || action == "restore")
                {
                    DisableFilter();
                }
                else
                {
                    EnableFilter();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static string GetBasePacUrl()
        {
            string baseUrl = DefaultCloudPacUrl;
            try
            {
                string configPath = Path.Combine(InstallDir, "config.ini");
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith(";") || line.StartsWith("#") || !line.Contains("="))
                            continue;

                        int idx = line.IndexOf('=');
                        string key = line.Substring(0, idx).Trim();
                        string val = line.Substring(idx + 1).Trim();
                        if (string.Equals(key, "CloudPacUrl", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                        {
                            baseUrl = val;
                            break;
                        }
                    }
                }
            }
            catch {}
            return baseUrl;
        }

        private static string GetConfiguredPacUrl()
        {
            string baseUrl = GetBasePacUrl();

            // Append cache-busting timestamp to HTTP/HTTPS URLs so WinINet & Chrome/Edge
            // always fetch the latest cloud whitelist immediately without stale cache.
            if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                string sep = baseUrl.Contains("?") ? "&" : "?";
                return baseUrl + sep + "t=" + ts.ToString();
            }

            return baseUrl;
        }

        private static void EnableFilter()
        {
            // 1. Ensure local sinkhole listener on 127.0.0.1:9999 is active so browsers receive HTTP 403 Forbidden
            // and NEVER trigger WinINet/Chromium dead-proxy failover to DIRECT!
            EnsureSinkholeRunning();

            RefreshPacSettings();
        }

        private static void RefreshPacSettings()
        {
            string pacUrl = GetConfiguredPacUrl();

            // Apply to current session via WinINet API (updates DefaultConnectionSettings & notifies browsers)
            SetWinInetPac(true, pacUrl);

            // Apply directly to HKEY_CURRENT_USER
            ApplyPacToRegistryRoot(Registry.CurrentUser, true, pacUrl);

            // Apply to ALL logged-in user hives in HKEY_USERS (crucial when run by Veyon Service as SYSTEM or Admin)
            ApplyPacToAllLoadedUsers(true, pacUrl);

            // Broadcast settings change after all registry updates
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        private static void DisableFilter()
        {
            // 1. Disable in current session via WinINet API
            SetWinInetPac(false, "");

            // 2. Remove from HKEY_CURRENT_USER
            ApplyPacToRegistryRoot(Registry.CurrentUser, false, "");

            // 3. Remove from ALL logged-in user hives in HKEY_USERS
            ApplyPacToAllLoadedUsers(false, "");

            // 4. Stop background sinkhole server
            StopSinkholeServer();

            // 5. Broadcast settings change after all registry updates
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
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
                        return; // Already listening!
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

                // Wait up to 400ms for sinkhole to bind
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

        private static void RunSinkholeServer()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\SchoolFilterSinkholeMutex", out createdNew))
            {
                if (!createdNew)
                {
                    return; // Another instance is already running
                }

                TcpListener listener = null;
                bool running = true;

                // Background watcher thread: checks GitHub cloud PAC every 20 seconds for live teacher changes
                Thread cloudSyncThread = new Thread(() =>
                {
                    string lastPacContent = null;
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                    while (running)
                    {
                        try
                        {
                            string baseUrl = GetBasePacUrl();
                            if (baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                long ts = DateTime.UtcNow.Ticks;
                                string checkUrl = baseUrl + (baseUrl.Contains("?") ? "&" : "?") + "nocache=" + ts;
                                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(checkUrl);
                                req.Proxy = null; // Direct check, bypass proxy
                                req.Timeout = 4000;
                                req.UserAgent = "SchoolFilter-Sync/1.0";
                                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                                {
                                    string content = sr.ReadToEnd();
                                    if (!string.IsNullOrEmpty(content))
                                    {
                                        if (lastPacContent != null && lastPacContent != content)
                                        {
                                            // Cloud PAC changed! Refresh WinINet & browsers immediately!
                                            RefreshPacSettings();
                                        }
                                        lastPacContent = content;
                                    }
                                }
                            }
                        }
                        catch {}

                        for (int i = 0; i < 20 && running; i++)
                        {
                            Thread.Sleep(1000);
                        }
                    }
                });
                cloudSyncThread.IsBackground = true;
                cloudSyncThread.Start();

                try
                {
                    listener = new TcpListener(IPAddress.Loopback, SinkholePort);
                    listener.Start();

                    string htmlBody = "<!DOCTYPE html><html dir='rtl' lang='he'><head><meta charset='utf-8'><title>האתר חסום - SchoolFilter</title><style>body{font-family:'Segoe UI',Tahoma,sans-serif;background:#f8fafc;color:#0f172a;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;text-align:center}.box{background:#fff;padding:40px;border-radius:16px;box-shadow:0 4px 12px rgba(0,0,0,0.08);max-width:460px;border:1px solid #e2e8f0}h1{color:#dc2626;margin-top:0;font-size:24px}p{color:#475569;line-height:1.6}</style></head><body><div class='box'><h1>🛑 הגלישה לאתר זה חסומה</h1><p>מחשב זה נמצא כעת במצב למידה (רשימה לבנה בלבד).</p><p>ניתן לגלוש לאתרי הלימוד המאושרים על ידי המורה בלבד.</p></div></body></html>";
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(htmlBody);
                    string header = "HTTP/1.1 403 Forbidden\r\n" +
                                    "Content-Type: text/html; charset=utf-8\r\n" +
                                    "Content-Length: " + bodyBytes.Length + "\r\n" +
                                    "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                                    "Connection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                    byte[] fullResponse = new byte[headerBytes.Length + bodyBytes.Length];
                    Buffer.BlockCopy(headerBytes, 0, fullResponse, 0, headerBytes.Length);
                    Buffer.BlockCopy(bodyBytes, 0, fullResponse, headerBytes.Length, bodyBytes.Length);

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
                                byte[] buf = new byte[512];
                                int read = 0;
                                try
                                {
                                    read = stream.Read(buf, 0, buf.Length);
                                }
                                catch {}

                                if (read > 0)
                                {
                                    string req = Encoding.ASCII.GetString(buf, 0, read);
                                    if (req.StartsWith("STOP_SINKHOLE", StringComparison.Ordinal))
                                    {
                                        running = false;
                                        try { listener.Stop(); } catch {}
                                        return;
                                    }
                                }

                                stream.Write(fullResponse, 0, fullResponse.Length);
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
}
