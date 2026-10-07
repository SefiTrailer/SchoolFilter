using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
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
        Teacher
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

        private static readonly string TargetDir = Path.Combine(
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
            InstallRole role = InstallRole.Student;
            bool roleExplicitlySet = false;
            string roomId = "yeshiva-lab";
            string roomName = "ישיבת נשמת התורה - חדר מחשבים";
            string institutionName = "ישיבת נשמת התורה";

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
                else if (arg == "/UNINSTALL" || arg == "-U" || arg == "/U")
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
                        roomId = val;
                        roomName = val;
                    }
                }
            }

            if (!IsAdministrator())
            {
                if (!suppressMsgBoxes)
                {
                    MessageBox.Show(
                        "נדרשות הרשאות מנהל מערכת (Administrator) להרצת ההתקנה.\nאנא הפעל את הקובץ כמנהל.",
                        "SchoolFilter Setup - שגיאת הרשאות",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                return 5; // ERROR_ACCESS_DENIED
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
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static int PerformInstall(InstallRole role, string roomId, string roomName, string institutionName, bool isSilent, bool suppressMsgBoxes)
        {
            // 1. Create target directory
            if (!Directory.Exists(TargetDir))
            {
                Directory.CreateDirectory(TargetDir);
            }

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

            // Write room-specific config.ini
            string configContent =
                "[SchoolFilter]\r\n" +
                "RoomId=" + roomId + "\r\n" +
                "RoomName=" + roomName + "\r\n" +
                "InstitutionName=" + institutionName + "\r\n" +
                "FirebaseProjectId=" + FirebaseProjectId + "\r\n" +
                "CloudPacUrl=http://127.0.0.1:9999/filter.pac\r\n";
            File.WriteAllText(Path.Combine(TargetDir, "config.ini"), configContent, Encoding.UTF8);

            // 3. Configure Browser Policies (Disable UDP QUIC & DoH so Chrome/Edge never bypass the PAC filter)
            ConfigureBrowserPolicies(true);

            // 4. Role-specific setup (Student vs Teacher)
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

                    // Also register a high-privilege Scheduled Task at logon so it starts reliably on every reboot
                    RunHiddenProcess(
                        "schtasks.exe",
                        "/Create /TN \"SchoolFilterGuard\" /TR \"\\\"" + ctlPath + "\\\" watchdog\" /SC ONLOGON /RL HIGHEST /F",
                        true
                    );
                }
                catch {}
            }

            // 5. Copy running executable as uninstaller
            string currentExePath = Assembly.GetExecutingAssembly().Location;
            string uninstallerPath = Path.Combine(TargetDir, "Uninstall.exe");
            if (!string.Equals(currentExePath, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExePath, uninstallerPath, true);
            }

            // 6. Lock down NTFS ACLs:
            // Standard Users (Students): Read & Execute ONLY (no write, no delete, no modify)
            // Administrators & SYSTEM: Full Control
            ApplyStrictPermissions(TargetDir);

            // 7. Register in Windows Add/Remove Programs
            RegisterUninstallEntry(uninstallerPath, role);

            // 8. If Student Station, activate the filter & cloud listener immediately!
            if (role == InstallRole.Student)
            {
                RunHiddenProcess(ctlPath, "block", true);
            }

            if (!isSilent && !suppressMsgBoxes)
            {
                string roleName = (role == InstallRole.Teacher) ? "עמדת מורה (Teacher)" : "עמדת תלמיד (Student)";
                string roleDetails = (role == InstallRole.Teacher)
                    ? "הותקנו כלי הניהול ונוצר קיצור דרך בשולחן העבודה לממשק הניהול בענן."
                    : "משויך למוסד/חדר: " + institutionName + " — " + roomName + " (" + roomId + ")\n" +
                      "הותקן והופעל מנוע החסימה והסינכרון לענן (מתעדכן כל 5 שניות).";

                MessageBox.Show(
                    "ההתקנה הושלמה בהצלחה! (גרסה 3.0)\n\n" +
                    "פרופיל הותקן: " + roleName + "\n" +
                    "תיקיית יעד: " + TargetDir + "\n\n" +
                    roleDetails + "\n" +
                    "הרשאות NTFS ננעלו: משתמשי בית הספר במצב Read & Execute בלבד.",
                    "SchoolFilter Setup v3.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            return 0;
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

            // 1. Restore internet settings and stop sinkhole via SchoolFilterCtl.exe
            string ctlPath = Path.Combine(TargetDir, "SchoolFilterCtl.exe");
            if (File.Exists(ctlPath))
            {
                RunHiddenProcess(ctlPath, "uninstall-cleanup", true);
                Thread.Sleep(200);
            }
            KillExistingControllerProcesses();

            // 2. Remove Startup entry, Scheduled Task, and Browser Policies
            try
            {
                using (RegistryKey runKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null) runKey.DeleteValue("SchoolFilter", false);
                }
                RunHiddenProcess("schtasks.exe", "/Delete /TN \"SchoolFilterGuard\" /F", true);
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

            // 5. Clean up directory and self via detached process
            string batchCleanup = Path.Combine(Path.GetTempPath(), "SchoolFilter_Cleanup.bat");
            string cleanupScript = string.Format(
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "rmdir /S /Q \"{0}\"\r\n" +
                "del \"%~f0\"\r\n",
                TargetDir
            );
            File.WriteAllText(batchCleanup, cleanupScript);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchCleanup + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            if (!isSilent && !suppressMsgBoxes)
            {
                MessageBox.Show(
                    "SchoolFilter הוסר בהצלחה מהמחשב.\nהגישה המלאה לאינטרנט שוחזרה.",
                    "SchoolFilter הסרה הושלמה",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
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
            this.Text = "התקנת SchoolFilter v3.0 - בחירת עמדה וחדר מחשבים";
            this.Size = new Size(540, 460);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular);

            Label lblHeader = new Label()
            {
                Text = "ברוכים הבאים לאשף ההתקנה של SchoolFilter v3.0",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(20, 18),
                AutoSize = true
            };

            Label lblSub = new Label()
            {
                Text = "אנא בחר את ייעוד המחשב ואת חדר המחשבים / העגלה שאליהם הוא שייך:",
                Location = new Point(22, 50),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            GroupBox grpRole = new GroupBox()
            {
                Text = "פרופיל התקנה ושיוך לחדר",
                Location = new Point(20, 82),
                Size = new Size(480, 265)
            };

            RadioButton rbStudent = new RadioButton()
            {
                Text = "🎓 עמדת תלמיד (Student Station) - למחשבי הכיתה והעגלות",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 28),
                Size = new Size(440, 25),
                Checked = true
            };

            Label lblRoomPrompt = new Label()
            {
                Text = "🏫 בחר לאיזה חדר מחשבים או עגלת ניידים שייך מחשב זה:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(42, 58),
                Size = new Size(415, 22),
                ForeColor = Color.FromArgb(30, 64, 175)
            };

            cmbRooms = new ComboBox()
            {
                Location = new Point(42, 82),
                Size = new Size(410, 28),
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };

            PopulateRoomsDropdown();

            Label lblStudentDesc = new Label()
            {
                Text = "• מסתנכרן אוטומטית כל 5 שניות מול החדר שנבחר בלבד.\n• כולל חסימת משחקי דפדפן ומשחקים מותקנים על Windows בזמן שיעור.\n• מוגן מפני מחיקה או עקיפה (Read & Execute בלבד לתלמיד).",
                Location = new Point(42, 116),
                Size = new Size(415, 52),
                ForeColor = Color.DarkSlateGray
            };

            RadioButton rbTeacher = new RadioButton()
            {
                Text = "👨‍🏫 עמדת מורה (Teacher Station) - למחשב המורה בלבד",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 180),
                Size = new Size(440, 25)
            };

            Label lblTeacherDesc = new Label()
            {
                Text = "• יוצר קיצור דרך לממשק הניהול בענן (עם התחברות Google ושליטה על כל החדרים).\n• אינו נועל את מחשב המורה.",
                Location = new Point(42, 208),
                Size = new Size(415, 42),
                ForeColor = Color.DarkSlateGray
            };

            rbStudent.CheckedChanged += (s, e) =>
            {
                cmbRooms.Enabled = rbStudent.Checked;
                lblRoomPrompt.Enabled = rbStudent.Checked;
            };

            grpRole.Controls.Add(rbStudent);
            grpRole.Controls.Add(lblRoomPrompt);
            grpRole.Controls.Add(cmbRooms);
            grpRole.Controls.Add(lblStudentDesc);
            grpRole.Controls.Add(rbTeacher);
            grpRole.Controls.Add(lblTeacherDesc);

            Button btnInstall = new Button()
            {
                Text = "התקן כעת ⬅",
                Location = new Point(380, 362),
                Size = new Size(120, 40),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };

            Button btnCancel = new Button()
            {
                Text = "ביטול",
                Location = new Point(250, 362),
                Size = new Size(110, 40),
                DialogResult = DialogResult.Cancel
            };

            btnInstall.Click += (s, e) =>
            {
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

            this.Controls.Add(lblHeader);
            this.Controls.Add(lblSub);
            this.Controls.Add(grpRole);
            this.Controls.Add(btnInstall);
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
