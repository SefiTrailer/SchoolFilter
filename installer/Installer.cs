using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
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

namespace SchoolFilter.Setup
{
    public enum InstallRole
    {
        Student,
        Teacher,
        Uninstall
    }

    public sealed class RoomOption
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string InstitutionName { get; set; }

        public override string ToString()
        {
            return RoomName + "  (" + RoomId + ")";
        }
    }

    internal static class Program
    {
        private const string AppName = "SchoolFilter";
        private const string AppVersion = "3.0.0";
        private const string Publisher = "School IT Administration";
        private const string TeacherPortalUrl = "https://sefitrailer.github.io/SchoolFilter/";
        private const string FirebaseProjectId = "school-filter-2026";

        private static string TargetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
            AppName
        );

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isSilent = false;
            bool suppressMsgBoxes = false;
            bool isUninstall = false;
            bool customDirSpecified = false;
            InstallRole role = InstallRole.Student;
            bool roleExplicitlySet = false;
            string roomId = "yeshiva-lab";
            string roomName = "ישיבת נשמת התורה - חדר מחשבים";
            string institutionName = "ישיבת נשמת התורה";
            string adminUser = "";
            string adminPass = "";

            // Detect if running directly from an installed folder containing config.ini
            try
            {
                string defaultProgramFilesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
                string currentExeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(currentExeDir) && File.Exists(Path.Combine(currentExeDir, "config.ini")))
                {
                    TargetDir = currentExeDir;
                    if (!string.Equals(TargetDir, defaultProgramFilesDir, StringComparison.OrdinalIgnoreCase))
                    {
                        customDirSpecified = true;
                    }
                }
            }
            catch {}

            // First pass for custom /DIR= override
            foreach (string rawArg in args)
            {
                string trimmed = rawArg.Trim();
                if (trimmed.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed.Substring(5).Trim().Trim('"');
                    if (!string.IsNullOrEmpty(val))
                    {
                        TargetDir = Path.GetFullPath(val);
                        customDirSpecified = true;
                    }
                }
            }

            // Preserve existing room configuration if updating an existing installation
            try
            {
                string existingConfig = Path.Combine(TargetDir, "config.ini");
                if (File.Exists(existingConfig))
                {
                    foreach (string rawLine in File.ReadAllLines(existingConfig, Encoding.UTF8))
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("RoomId=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(7).Trim();
                            if (!string.IsNullOrEmpty(v)) roomId = v;
                        }
                        else if (line.StartsWith("RoomName=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(9).Trim();
                            if (!string.IsNullOrEmpty(v)) roomName = v;
                        }
                        else if (line.StartsWith("InstitutionName=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(16).Trim();
                            if (!string.IsNullOrEmpty(v)) institutionName = v;
                        }
                    }
                }
            }
            catch {}

            // If launched directly as Uninstall.exe without arguments, default to uninstall mode
            try
            {
                string exeName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
                if (string.Equals(exeName, "Uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    isUninstall = true;
                }
            }
            catch {}

            foreach (string rawArg in args)
            {
                string trimmed = rawArg.Trim();
                string arg = trimmed.ToUpperInvariant();
                if (arg == "/VERYSILENT" || arg == "/SILENT" || arg == "-S" || arg == "/S")
                {
                    isSilent = true;
                }
                else if (arg == "/SUPPRESSMSGBOXES" || arg == "-Q" || arg == "/Q")
                {
                    suppressMsgBoxes = true;
                }
                else if (arg == "/UNINSTALL" || arg == "/REMOVE" || arg == "-U" || arg == "/U")
                {
                    isUninstall = true;
                }
                else if (arg == "/TEACHER" || arg == "/MASTER" || arg == "/ROLE=TEACHER")
                {
                    role = InstallRole.Teacher;
                    roleExplicitlySet = true;
                }
                else if (arg == "/STUDENT" || arg == "/CLIENT" || arg == "/ROLE=STUDENT")
                {
                    role = InstallRole.Student;
                    roleExplicitlySet = true;
                }
                else if (arg.StartsWith("/ROOM=", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed.Substring(6).Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(val))
                    {
                        if (!string.Equals(roomId, val, StringComparison.OrdinalIgnoreCase))
                        {
                            roomName = val;
                        }
                        roomId = val;
                    }
                }
                else if (arg.StartsWith("/ADMINUSER=", StringComparison.OrdinalIgnoreCase))
                {
                    adminUser = trimmed.Substring(11).Trim().Trim('"');
                }
                else if (arg.StartsWith("/ADMINPASS=", StringComparison.OrdinalIgnoreCase))
                {
                    adminPass = trimmed.Substring(11).Trim('"');
                }
            }

            if (!IsAdministrator() && !customDirSpecified)
            {
                try
                {
                    StringBuilder argSb = new StringBuilder();
                    foreach (string a in args)
                    {
                        if (a.StartsWith("/ADMINUSER=", StringComparison.OrdinalIgnoreCase) ||
                            a.StartsWith("/ADMINPASS=", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (argSb.Length > 0) argSb.Append(" ");
                        argSb.Append("\"" + a.Replace("\"", "\\\"") + "\"");
                    }

                    string exeLoc = Assembly.GetExecutingAssembly().Location;

                    // If Admin credentials were passed via CLI (/ADMINUSER=... /ADMINPASS=...), elevate silently without UAC popup
                    if (!string.IsNullOrEmpty(adminUser) && !string.IsNullOrEmpty(adminPass))
                    {
                        try
                        {
                            string taskName = "SchoolFilterBootstrapAdmin";
                            string trCmd = "\\\"" + exeLoc + "\\\" " + argSb.ToString().Replace("\"", "\\\"");
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
                            using (Process cp = Process.Start(createPsi))
                            {
                                if (cp != null && cp.WaitForExit(4000) && cp.ExitCode == 0)
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
                                            Thread.Sleep(2000);
                                            return 0;
                                        }
                                    }
                                }
                            }
                        }
                        catch {}
                    }

                    ProcessStartInfo elevatePsi = new ProcessStartInfo(exeLoc, argSb.ToString())
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using (Process p = Process.Start(elevatePsi))
                    {
                        if (p != null)
                        {
                            p.WaitForExit();
                            return p.ExitCode;
                        }
                    }
                }
                catch
                {
                    if (!suppressMsgBoxes)
                    {
                        MessageBox.Show(
                            "נדרשות הרשאות מנהל מערכת (Administrator) להרצת ההתקנה או ההסרה.\nאנא הפעל את הקובץ כמנהל.",
                            "SchoolFilter Setup - שגיאת הרשאות",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                    return 5; // ERROR_ACCESS_DENIED
                }
            }

            try
            {
                if (isUninstall)
                {
                    return PerformUninstall(isSilent, suppressMsgBoxes);
                }

                if (!isSilent && !roleExplicitlySet)
                {
                    using (RoleSelectionForm form = new RoleSelectionForm())
                    {
                        if (form.ShowDialog() != DialogResult.OK)
                        {
                            return 2; // Cancelled
                        }
                        role = form.SelectedRole;
                        if (role == InstallRole.Uninstall)
                        {
                            return PerformUninstall(isSilent, suppressMsgBoxes);
                        }
                        if (!string.IsNullOrEmpty(form.SelectedRoomId))
                        {
                            roomId = form.SelectedRoomId;
                            roomName = form.SelectedRoomName;
                            institutionName = form.SelectedInstitutionName;
                        }
                    }
                }

                return PerformInstall(role, roomId, roomName, institutionName, isSilent, suppressMsgBoxes);
            }
            catch (Exception ex)
            {
                if (!suppressMsgBoxes)
                {
                    MessageBox.Show(
                        "אירעה שגיאה במהלך ההתקנה:\n\n" + ex.Message,
                        "SchoolFilter Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                return 1;
            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.IsSystem) return true;
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static int PerformInstall(InstallRole role, string roomId, string roomName, string institutionName, bool isSilent, bool suppressMsgBoxes)
        {
            // 1. Create target directory and reset permissions if overwriting
            if (!Directory.Exists(TargetDir))
            {
                Directory.CreateDirectory(TargetDir);
            }
            else if (IsAdministrator())
            {
                RunHiddenProcess("icacls.exe", "\"" + TargetDir + "\" /reset /T /C /Q", true);
            }

            // Ensure ProgramData\SchoolFilter is writable by background daemon
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SchoolFilter");
            try
            {
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                if (IsAdministrator())
                {
                    RunHiddenProcess("icacls.exe", "\"" + dataDir + "\" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q", true);
                }
            }
            catch {}

            // Stop any existing sinkhole process before overwriting SchoolFilterCtl.exe
            string ctlPath = Path.Combine(TargetDir, "SchoolFilterCtl.exe");
            if (File.Exists(ctlPath))
            {
                RunHiddenProcess(ctlPath, "stop-sinkhole", true);
                Thread.Sleep(200);
            }
            KillExistingControllerProcesses();

            // 2. Extract core filtering resources
            ExtractResource("SchoolFilterCtl.exe", ctlPath);
            ExtractResource("filter.pac", Path.Combine(TargetDir, "filter.pac"));
            ExtractResource("BlockGames.bat", Path.Combine(TargetDir, "BlockGames.bat"));
            ExtractResource("AllowAll.bat", Path.Combine(TargetDir, "AllowAll.bat"));

            // Ensure BlockGames.bat and AllowAll.bat reference the actual TargetDir
            try
            {
                string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
                if (!string.Equals(TargetDir, defaultDir, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string batName in new string[] { "BlockGames.bat", "AllowAll.bat" })
                    {
                        string batPath = Path.Combine(TargetDir, batName);
                        if (File.Exists(batPath))
                        {
                            string content = File.ReadAllText(batPath);
                            content = content.Replace(@"C:\Program Files\SchoolFilter", TargetDir);
                            File.WriteAllText(batPath, content);
                        }
                    }
                }
            }
            catch {}

            // Write room-specific config.ini with installation timestamp
            long installTsMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            string configContent =
                "[SchoolFilter]\r\n" +
                "RoomId=" + roomId + "\r\n" +
                "RoomName=" + roomName + "\r\n" +
                "InstitutionName=" + institutionName + "\r\n" +
                "FirebaseProjectId=" + FirebaseProjectId + "\r\n" +
                "CloudPacUrl=http://127.0.0.1:9999/filter.pac\r\n" +
                "InstalledAtMs=" + installTsMs.ToString() + "\r\n";
            File.WriteAllText(Path.Combine(TargetDir, "config.ini"), configContent, Encoding.UTF8);

            // 3. Configure Browser Policies (Disable UDP QUIC & DoH so Chrome/Edge never bypass the PAC filter)
            ConfigureBrowserPolicies(true);

            // 4. Copy running executable as uninstaller
            string currentExePath = Assembly.GetExecutingAssembly().Location;
            string uninstallerPath = Path.Combine(TargetDir, "Uninstall.exe");
            if (!string.Equals(currentExePath, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExePath, uninstallerPath, true);
            }

            // 5. Role-specific setup (Student vs Teacher)
            if (role == InstallRole.Teacher)
            {
                ExtractResource("TeacherManager.bat", Path.Combine(TargetDir, "TeacherManager.bat"));

                // Create Desktop Shortcut for Teacher
                CreateUrlShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url"),
                    TeacherPortalUrl
                );

                // Create Start Menu Shortcut
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SchoolFilter");
                if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
                CreateUrlShortcut(
                    Path.Combine(startMenuDir, "ניהול רשימה לבנה (ממשק מורה).url"),
                    TeacherPortalUrl
                );

                // Remove Student auto-lock on startup if previously installed as student
                try
                {
                    using (RegistryKey runKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null) runKey.DeleteValue("SchoolFilter", false);
                    }
                    RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterGuard\" /F", true);
                    RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterSystemAgent\" /F", true);
                }
                catch {}
            }
            else
            {
                // Student Mode: Remove any teacher shortcuts
                string teacherBat = Path.Combine(TargetDir, "TeacherManager.bat");
                if (File.Exists(teacherBat)) File.Delete(teacherBat);

                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                // Register Student Station to automatically enforce filter & cloud-sync daemon on Windows startup/login
                try
                {
                    using (RegistryKey runKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (runKey != null)
                        {
                            runKey.SetValue("SchoolFilter", "\"" + ctlPath + "\" watchdog", RegistryValueKind.String);
                        }
                    }
                }
                catch {}

                try
                {
                    using (RegistryKey cuRunKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (cuRunKey != null)
                        {
                            cuRunKey.SetValue("SchoolFilter", "\"" + ctlPath + "\" watchdog", RegistryValueKind.String);
                        }
                    }
                }
                catch {}

                if (IsAdministrator())
                {
                    try
                    {
                        string remoteUpdatePayload = Path.Combine(dataDir, "remote_update_setup.exe");
                        // Register SYSTEM agent running as NT AUTHORITY\SYSTEM so remote updates, remote uninstalls,
                        // and firewall rules execute with full Administrator privileges even on Student accounts
                        RunHiddenProcess(
                            "schtasks.exe",
                            "/Create /TN \"SchoolFilterSystemAgent\" /TR \"\\\"" + ctlPath + "\\\" system-agent\" /SC MINUTE /MO 1 /RU \"SYSTEM\" /RL HIGHEST /F",
                            true
                        );
                        RunHiddenProcess(
                            "schtasks.exe",
                            "/Create /TN \"SchoolFilterGuard\" /TR \"\\\"" + ctlPath + "\\\" watchdog\" /SC ONLOGON /RL HIGHEST /F",
                            true
                        );
                        RunHiddenProcess(
                            "schtasks.exe",
                            "/Create /TN \"SchoolFilterRemoteUninstall\" /TR \"\\\"" + uninstallerPath + "\\\" /UNINSTALL /VERYSILENT /SUPPRESSMSGBOXES\" /SC ONCE /ST 00:00 /RU \"SYSTEM\" /RL HIGHEST /F",
                            true
                        );
                        RunHiddenProcess(
                            "schtasks.exe",
                            "/Create /TN \"SchoolFilterRemoteUpdate\" /TR \"\\\"" + remoteUpdatePayload + "\\\" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES\" /SC ONCE /ST 00:00 /RU \"SYSTEM\" /RL HIGHEST /F",
                            true
                        );
                    }
                    catch {}
                }
            }

            // 6. Lock down NTFS ACLs when running as Administrator:
            // Standard Users (Students): Read & Execute ONLY (no write, no delete, no modify)
            // Administrators & SYSTEM: Full Control
            if (IsAdministrator())
            {
                ApplyStrictPermissions(TargetDir);
            }

            // 7. Register in Windows Add/Remove Programs
            RegisterUninstallEntry(uninstallerPath, role);

            // 8. If Student Station, register computer in cloud and activate the filter & cloud listener immediately!
            if (role == InstallRole.Student)
            {
                RegisterComputerInFirestore(roomId, roomName, institutionName);
                if (IsAdministrator())
                {
                    RunHiddenProcess("schtasks.exe", "/Run /TN \"SchoolFilterSystemAgent\"", true);
                }
                if (!WindowsIdentity.GetCurrent().IsSystem)
                {
                    RunHiddenProcess(ctlPath, "block", true);
                }
            }

            if (!isSilent && !suppressMsgBoxes)
            {
                string roleName = (role == InstallRole.Teacher) ? "עמדת מורה (Teacher)" : "עמדת תלמיד (Student)";
                string roleDetails = (role == InstallRole.Teacher)
                    ? "הותקנו כלי הניהול ונוצר קיצור דרך בשולחן העבודה לממשק הניהול בענן."
                    : "משויך למוסד/חדר: " + institutionName + " — " + roomName + " (" + roomId + ")\n" +
                      "הותקן והופעל מנוע החסימה והסינכרון לענן (כולל שירות עדכון/הסרה מרחוק בהרשאת SYSTEM).";

                MessageBox.Show(
                    "ההתקנה הושלמה בהצלחה!\n\n" +
                    "פרופיל הותקן: " + roleName + "\n" +
                    "תיקיית יעד: " + TargetDir + "\n\n" +
                    roleDetails + "\n" +
                    "הרשאות NTFS ננעלו: משתמשי בית הספר במצב Read & Execute בלבד.",
                    "SchoolFilter Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            return 0;
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

        private static void RegisterComputerInFirestore(string roomId, string roomName, string institutionName)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string pcName = Environment.MachineName;
                string userName = Environment.UserName;
                string docId = SanitizeDocIdPart(roomId) + "__" + SanitizeDocIdPart(pcName);
                string url = "https://firestore.googleapis.com/v1/projects/" + FirebaseProjectId +
                             "/databases/(default)/documents/computers/" + Uri.EscapeDataString(docId);

                string nowIso = DateTime.UtcNow.ToString("o");
                string json = "{" +
                    "\"fields\":{" +
                        "\"computerName\":{\"stringValue\":\"" + EscapeJsonStr(pcName) + "\"}," +
                        "\"userName\":{\"stringValue\":\"" + EscapeJsonStr(userName) + "\"}," +
                        "\"roomId\":{\"stringValue\":\"" + EscapeJsonStr(roomId) + "\"}," +
                        "\"roomName\":{\"stringValue\":\"" + EscapeJsonStr(roomName) + "\"}," +
                        "\"institutionName\":{\"stringValue\":\"" + EscapeJsonStr(institutionName) + "\"}," +
                        "\"filterActive\":{\"booleanValue\":true}," +
                        "\"installedAt\":{\"stringValue\":\"" + nowIso + "\"}," +
                        "\"lastSeen\":{\"stringValue\":\"" + nowIso + "\"}" +
                    "}" +
                "}";

                byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "PATCH";
                req.Proxy = null;
                req.Timeout = 3500;
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
                string roomId = "yeshiva-lab";
                string existingConfig = Path.Combine(TargetDir, "config.ini");
                if (File.Exists(existingConfig))
                {
                    foreach (string rawLine in File.ReadAllLines(existingConfig, Encoding.UTF8))
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("RoomId=", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(7).Trim();
                            if (!string.IsNullOrEmpty(v)) roomId = v;
                        }
                    }
                }

                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string pcName = Environment.MachineName;
                string docId = SanitizeDocIdPart(roomId) + "__" + SanitizeDocIdPart(pcName);
                string url = "https://firestore.googleapis.com/v1/projects/" + FirebaseProjectId +
                             "/databases/(default)/documents/computers/" + Uri.EscapeDataString(docId);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "DELETE";
                req.Proxy = null;
                req.Timeout = 3000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {}
            }
            catch {}
        }

        private static int PerformUninstall(bool isSilent, bool suppressMsgBoxes)
        {
            if (!isSilent)
            {
                DialogResult dr = MessageBox.Show(
                    "האם אתה בטוח שברצונך להסיר את SchoolFilter ולשחזר גישה ישירה מלאה לאינטרנט?",
                    "SchoolFilter הסרת התקנה",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (dr != DialogResult.Yes)
                {
                    return 2; // Cancelled
                }
            }

            try
            {
                Environment.CurrentDirectory = Path.GetTempPath();
            }
            catch {}

            // Remove this computer from the room's installed computers list in Firestore
            UnregisterComputerFromFirestore();

            // 1. Restore internet settings and stop sinkhole via SchoolFilterCtl.exe
            string ctlPath = Path.Combine(TargetDir, "SchoolFilterCtl.exe");
            if (File.Exists(ctlPath))
            {
                RunHiddenProcess(ctlPath, "uninstall-cleanup", true);
                Thread.Sleep(250);
            }
            KillExistingControllerProcesses();

            // 2. Remove Startup entry, Scheduled Tasks, and Browser Policies
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
                RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterSystemAgent\" /F", true);
                RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterGuard\" /F", true);
                RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterRemoteUninstall\" /F", true);
                RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterRemoteUpdate\" /F", true);
            }
            catch {}
            ConfigureBrowserPolicies(false);

            // 3. Remove desktop and start menu shortcuts
            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SchoolFilter");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);
            }
            catch {}

            // 4. Remove registry uninstallation entry
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

            // 5. Clean up ProgramData\SchoolFilter and TargetDir immediately
            string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SchoolFilter");
            try
            {
                if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
            }
            catch {}

            try
            {
                if (Directory.Exists(TargetDir))
                {
                    RunHiddenProcess("icacls.exe", "\"" + TargetDir + "\" /reset /T /C /Q", true);
                    foreach (string file in Directory.GetFiles(TargetDir))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch {}
                    }
                    try
                    {
                        Directory.Delete(TargetDir, true);
                    }
                    catch {}
                }
            }
            catch {}

            if (!isSilent && !suppressMsgBoxes)
            {
                MessageBox.Show(
                    "SchoolFilter הוסר בהצלחה מהמחשב.\nהגישה המלאה לאינטרנט שוחזרה.",
                    "SchoolFilter הסרה הושלמה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            // 6. If TargetDir or dataDir still exists (e.g. when running directly from TargetDir\Uninstall.exe),
            // launch a detached cleanup batch script AFTER the MessageBox is closed so Uninstall.exe can exit first.
            if (Directory.Exists(TargetDir) || Directory.Exists(dataDir))
            {
                try
                {
                    string batchCleanup = Path.Combine(Path.GetTempPath(), "SchoolFilter_Cleanup.bat");
                    string cleanupScript = string.Format(
                        "@echo off\r\n" +
                        "cd /d \"%TEMP%\"\r\n" +
                        "for /L %%i in (1,1,15) do (\r\n" +
                        "    if exist \"{0}\" (\r\n" +
                        "        rmdir /S /Q \"{0}\" >nul 2>&1\r\n" +
                        "        if exist \"{0}\" ping 127.0.0.1 -n 2 > nul\r\n" +
                        "    )\r\n" +
                        ")\r\n" +
                        "if exist \"{1}\" rmdir /S /Q \"{1}\" >nul 2>&1\r\n" +
                        "del \"%~f0\" >nul 2>&1\r\n",
                        TargetDir,
                        dataDir
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

            return 0;
        }

        private static void ConfigureBrowserPolicies(bool enable)
        {
            string[] policyPaths = new string[]
            {
                @"SOFTWARE\Policies\Google\Chrome",
                @"SOFTWARE\Policies\Microsoft\Edge"
            };

            foreach (string path in policyPaths)
            {
                try
                {
                    if (enable)
                    {
                        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(path))
                        {
                            if (key != null)
                            {
                                // Prevent UDP QUIC and DoH from bypassing Windows system proxy/PAC
                                key.SetValue("QuicAllowed", 0, RegistryValueKind.DWord);
                                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                            }
                        }
                    }
                    else
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
                }
                catch {}
            }
        }

        private static void KillExistingControllerProcesses()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("SchoolFilterCtl");
                foreach (Process p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                    catch {}
                }
            }
            catch {}
        }

        private static void RunHiddenProcess(string fileName, string arguments, bool wait)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (wait && p != null)
                    {
                        p.WaitForExit(5000);
                    }
                }
            }
            catch {}
        }

        private static void CreateUrlShortcut(string filePath, string targetUrl)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("[InternetShortcut]");
                    writer.WriteLine("URL=" + targetUrl);
                    writer.WriteLine("IconIndex=0");
                }
            }
            catch {}
        }

        private static void ExtractResource(string resourceName, string outputPath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded resource not found: " + resourceName);
                }

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        private static void ApplyStrictPermissions(string folderPath)
        {
            try
            {
                DirectoryInfo dInfo = new DirectoryInfo(folderPath);
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

                foreach (string filePath in Directory.GetFiles(folderPath))
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
            }
            catch
            {
                ProcessStartInfo psi = new ProcessStartInfo("icacls.exe",
                    "\"" + folderPath + "\" /inheritance:r /grant:r \"BUILTIN\\Administrators:(OI)(CI)F\" \"NT AUTHORITY\\SYSTEM:(OI)(CI)F\" \"BUILTIN\\Users:(OI)(CI)RX\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process proc = Process.Start(psi))
                {
                    proc.WaitForExit();
                }
            }
        }

        private static void RegisterUninstallEntry(string uninstallerPath, InstallRole role)
        {
            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter"))
                {
                    if (baseKey != null)
                    {
                        string displayName = "SchoolFilter (" + (role == InstallRole.Teacher ? "עמדת מורה" : "עמדת תלמיד") + ")";
                        baseKey.SetValue("DisplayName", displayName);
                        baseKey.SetValue("DisplayVersion", AppVersion);
                        baseKey.SetValue("Publisher", Publisher);
                        baseKey.SetValue("InstallLocation", TargetDir);
                        baseKey.SetValue("UninstallString", "\"" + uninstallerPath + "\" /UNINSTALL");
                        baseKey.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /UNINSTALL /VERYSILENT /SUPPRESSMSGBOXES");
                        baseKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        baseKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch {}
        }
    }

    internal class RoleSelectionForm : Form
    {
        public InstallRole SelectedRole { get; private set; }
        public string SelectedRoomId { get; private set; }
        public string SelectedRoomName { get; private set; }
        public string SelectedInstitutionName { get; private set; }

        private ComboBox cmbRooms;

        public RoleSelectionForm()
        {
            SelectedRole = InstallRole.Student;
            SelectedRoomId = "yeshiva-lab";
            SelectedRoomName = "ישיבת נשמת התורה - חדר מחשבים";
            SelectedInstitutionName = "ישיבת נשמת התורה";
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "התקנת והסרת SchoolFilter - בחירת עמדה וחדר מחשבים";
            this.Size = new Size(550, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            Label lblHeader = new Label()
            {
                Text = "ברוכים הבאים לאשף ההתקנה של SchoolFilter",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true
            };

            Label lblSub = new Label()
            {
                Text = "אנא בחר את ייעוד המחשב והחדר, או הסר התקנה קיימת (Remove):",
                Location = new Point(22, 46),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            GroupBox grpRole = new GroupBox()
            {
                Text = "פרופיל התקנה / הסרה",
                Location = new Point(20, 76),
                Size = new Size(490, 330)
            };

            RadioButton rbStudent = new RadioButton()
            {
                Text = "🎓 עמדת תלמיד (Student Station) - למחשבי הכיתה והעגלות",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 26),
                Size = new Size(450, 25),
                Checked = true
            };

            Label lblRoomPrompt = new Label()
            {
                Text = "🏫 בחר לאיזה חדר מחשבים או עגלת ניידים שייך מחשב זה:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(42, 54),
                Size = new Size(425, 22),
                ForeColor = Color.FromArgb(30, 64, 175)
            };

            cmbRooms = new ComboBox()
            {
                Location = new Point(42, 78),
                Size = new Size(420, 28),
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };

            PopulateRoomsDropdown();

            Label lblStudentDesc = new Label()
            {
                Text = "• מסתנכרן אוטומטית כל 5 שניות מול החדר שנבחר בלבד.\n• כולל חסימת משחקי דפדפן ומשחקים מותקנים על Windows בזמן שיעור.\n• מוגן מפני מחיקה או עקיפה (Read & Execute בלבד לתלמיד).",
                Location = new Point(42, 110),
                Size = new Size(425, 52),
                ForeColor = Color.DarkSlateGray
            };

            RadioButton rbTeacher = new RadioButton()
            {
                Text = "👨‍🏫 עמדת מורה (Teacher Station) - למחשב המורה בלבד",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 170),
                Size = new Size(450, 25)
            };

            Label lblTeacherDesc = new Label()
            {
                Text = "• יוצר קיצור דרך לממשק הניהול בענן (עם התחברות Google ושליטה על כל החדרים).\n• אינו נועל את מחשב המורה.",
                Location = new Point(42, 196),
                Size = new Size(425, 40),
                ForeColor = Color.DarkSlateGray
            };

            RadioButton rbUninstall = new RadioButton()
            {
                Text = "🗑️ הסרת התוכנה מהמחשב (Remove / Uninstall)",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(185, 28, 28),
                Location = new Point(20, 246),
                Size = new Size(450, 25)
            };

            Label lblUninstallDesc = new Label()
            {
                Text = "• מסיר לחלוטין את SchoolFilter מהמחשב ומשחזר גישה ישירה מלאה לאינטרנט.",
                Location = new Point(42, 272),
                Size = new Size(425, 36),
                ForeColor = Color.DarkSlateGray
            };

            Button btnInstall = new Button()
            {
                Text = "התקן כעת ⬅",
                Location = new Point(380, 420),
                Size = new Size(130, 40),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };

            Button btnRemove = new Button()
            {
                Text = "🗑️ הסר תוכנה (Remove)",
                Location = new Point(195, 420),
                Size = new Size(170, 40),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };

            Button btnCancel = new Button()
            {
                Text = "ביטול",
                Location = new Point(75, 420),
                Size = new Size(105, 40),
                DialogResult = DialogResult.Cancel
            };

            EventHandler radioChanged = (s, e) =>
            {
                cmbRooms.Enabled = rbStudent.Checked;
                lblRoomPrompt.Enabled = rbStudent.Checked;
                if (rbUninstall.Checked)
                {
                    btnInstall.Text = "הסר כעת 🗑️";
                    btnInstall.BackColor = Color.FromArgb(220, 38, 38);
                }
                else
                {
                    btnInstall.Text = "התקן כעת ⬅";
                    btnInstall.BackColor = Color.FromArgb(37, 99, 235);
                }
            };

            rbStudent.CheckedChanged += radioChanged;
            rbTeacher.CheckedChanged += radioChanged;
            rbUninstall.CheckedChanged += radioChanged;

            grpRole.Controls.Add(rbStudent);
            grpRole.Controls.Add(lblRoomPrompt);
            grpRole.Controls.Add(cmbRooms);
            grpRole.Controls.Add(lblStudentDesc);
            grpRole.Controls.Add(rbTeacher);
            grpRole.Controls.Add(lblTeacherDesc);
            grpRole.Controls.Add(rbUninstall);
            grpRole.Controls.Add(lblUninstallDesc);

            btnInstall.Click += (s, e) =>
            {
                if (rbUninstall.Checked)
                {
                    SelectedRole = InstallRole.Uninstall;
                    return;
                }
                SelectedRole = rbTeacher.Checked ? InstallRole.Teacher : InstallRole.Student;
                RoomOption opt = cmbRooms.SelectedItem as RoomOption;
                if (opt != null)
                {
                    SelectedRoomId = opt.RoomId;
                    SelectedRoomName = opt.RoomName;
                    SelectedInstitutionName = opt.InstitutionName;
                }
                else if (!string.IsNullOrEmpty(cmbRooms.Text))
                {
                    string raw = cmbRooms.Text.Trim();
                    int openParen = raw.LastIndexOf('(');
                    int closeParen = raw.LastIndexOf(')');
                    if (openParen >= 0 && closeParen > openParen)
                    {
                        SelectedRoomId = raw.Substring(openParen + 1, closeParen - openParen - 1).Trim();
                    }
                    else
                    {
                        SelectedRoomId = raw.ToLowerInvariant();
                        SelectedRoomName = raw;
                    }
                }
            };

            btnRemove.Click += (s, e) =>
            {
                SelectedRole = InstallRole.Uninstall;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            this.Controls.Add(lblHeader);
            this.Controls.Add(lblSub);
            this.Controls.Add(grpRole);
            this.Controls.Add(btnInstall);
            this.Controls.Add(btnRemove);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnInstall;
            this.CancelButton = btnCancel;
        }

        private void PopulateRoomsDropdown()
        {
            List<RoomOption> options = new List<RoomOption>();

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                    "https://firestore.googleapis.com/v1/projects/school-filter-2026/databases/(default)/documents/rooms?pageSize=100"
                );
                req.Proxy = null;
                req.Timeout = 2500;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    string[] docBlocks = json.Split(new string[] { "\"fields\"" }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < docBlocks.Length; i++)
                    {
                        string block = docBlocks[i];
                        string rId = ExtractJsonStringField(block, "roomId");
                        string rName = ExtractJsonStringField(block, "name");
                        string iName = ExtractJsonStringField(block, "institutionName");
                        if (!string.IsNullOrEmpty(rId) && !string.IsNullOrEmpty(rName))
                        {
                            options.Add(new RoomOption
                            {
                                RoomId = rId,
                                RoomName = rName,
                                InstitutionName = string.IsNullOrEmpty(iName) ? "הישיבה שלנו" : iName
                            });
                        }
                    }
                }
            }
            catch {}

            if (options.Count == 0)
            {
                options.Add(new RoomOption { RoomId = "yeshiva-lab", RoomName = "ישיבת נשמת התורה - חדר מחשבים", InstitutionName = "ישיבת נשמת התורה" });
                options.Add(new RoomOption { RoomId = "yeshiva-cart", RoomName = "ישיבת נשמת התורה - עגלת מחשבים", InstitutionName = "ישיבת נשמת התורה" });
                options.Add(new RoomOption { RoomId = "mechina-cart", RoomName = "מכינה נשמת התורה - עגלת מחשבים", InstitutionName = "ישיבת נשמת התורה" });
            }

            foreach (RoomOption opt in options)
            {
                cmbRooms.Items.Add(opt);
            }
            if (cmbRooms.Items.Count > 0)
            {
                cmbRooms.SelectedIndex = 0;
            }
        }

        private static string ExtractJsonStringField(string text, string fieldName)
        {
            Match m = Regex.Match(text, "\"" + fieldName + "\"\\s*:\\s*\\{\\s*\"stringValue\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
