using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolFilter.Setup
{
    public enum InstallRole
    {
        Student,
        Teacher
    }

    internal static class Program
    {
        private const string AppName = "SchoolFilter";
        private const string AppVersion = "1.0.0";
        private const string Publisher = "School IT Administration";
        private const string TeacherPortalUrl = "https://sefitrailer.github.io/SchoolFilter/";

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

            foreach (string rawArg in args)
            {
                string arg = rawArg.Trim().ToUpperInvariant();
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
                    // Show interactive role selector dialog
                    using (RoleSelectionForm form = new RoleSelectionForm())
                    {
                        if (form.ShowDialog() != DialogResult.OK)
                        {
                            return 2; // Cancelled
                        }
                        role = form.SelectedRole;
                    }
                }

                return PerformInstall(role, isSilent, suppressMsgBoxes);
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

        private static int PerformInstall(InstallRole role, bool isSilent, bool suppressMsgBoxes)
        {
            // 1. Create target directory
            if (!Directory.Exists(TargetDir))
            {
                Directory.CreateDirectory(TargetDir);
            }

            // 2. Extract core filtering resources (Needed on both Student and Teacher)
            ExtractResource("filter.pac", Path.Combine(TargetDir, "filter.pac"));
            ExtractResource("BlockGames.bat", Path.Combine(TargetDir, "BlockGames.bat"));
            ExtractResource("AllowAll.bat", Path.Combine(TargetDir, "AllowAll.bat"));
            
            string configPath = Path.Combine(TargetDir, "config.ini");
            if (!File.Exists(configPath))
            {
                ExtractResource("config.ini", configPath);
            }

            // 3. Extract Teacher tools ONLY if Teacher role is selected
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
            }
            else
            {
                // In Student Mode: Clean up any teacher management files or shortcuts if they existed
                string teacherBat = Path.Combine(TargetDir, "TeacherManager.bat");
                if (File.Exists(teacherBat)) File.Delete(teacherBat);

                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);
            }

            // 4. Copy running executable as uninstaller
            string currentExePath = Assembly.GetExecutingAssembly().Location;
            string uninstallerPath = Path.Combine(TargetDir, "Uninstall.exe");
            if (!string.Equals(currentExePath, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExePath, uninstallerPath, true);
            }

            // 5. Lock down NTFS ACLs:
            // Standard Users (Students): Read & Execute ONLY (no write, no delete, no modify)
            // Administrators & SYSTEM: Full Control
            ApplyStrictPermissions(TargetDir);

            // 6. Register in Windows Add/Remove Programs
            RegisterUninstallEntry(uninstallerPath, role);

            if (!isSilent && !suppressMsgBoxes)
            {
                string roleName = (role == InstallRole.Teacher) ? "עמדת מורה (Teacher)" : "עמדת תלמיד (Student)";
                string roleDetails = (role == InstallRole.Teacher)
                    ? "הותקנו כלי הניהול ונוצר קיצור דרך בשולחן העבודה לממשק הניהול בענן."
                    : "הותקן מנוע החסימה בלבד. לתלמידים אין גישה או הרשאות לשינוי הרשימה הלבנה.";

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

            // 1. Restore internet settings immediately for current user
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("AutoConfigURL", false);
                    }
                }
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch {}

            // 2. Remove desktop and start menu shortcuts
            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "SchoolFilter - ממשק ניהול למורה.url");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SchoolFilter");
                if (Directory.Exists(startMenuDir)) Directory.Delete(startMenuDir, true);
            }
            catch {}

            // 3. Remove registry uninstallation entry
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter", false);
            }
            catch {}

            // 4. Clean up directory and self via detached process
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

    /// <summary>
    /// Interactive dialog for choosing Student vs Teacher installation role
    /// </summary>
    internal class RoleSelectionForm : Form
    {
        public InstallRole SelectedRole { get; private set; }

        public RoleSelectionForm()
        {
            SelectedRole = InstallRole.Student;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "התקנת SchoolFilter - בחירת סוג עמדה";
            this.Size = new Size(520, 390);
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
                Location = new Point(20, 20),
                AutoSize = true
            };

            Label lblSub = new Label()
            {
                Text = "אנא בחר את ייעוד המחשב שעליו מותקנת התוכנה כעת:",
                Location = new Point(22, 55),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            GroupBox grpRole = new GroupBox()
            {
                Text = "פרופיל התקנה",
                Location = new Point(20, 90),
                Size = new Size(460, 180)
            };

            RadioButton rbStudent = new RadioButton()
            {
                Text = "🎓 עמדת תלמיד (Student Station) - מומלץ למחשבי הכיתה",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 30),
                Size = new Size(420, 25),
                Checked = true
            };

            Label lblStudentDesc = new Label()
            {
                Text = "• מנוע חסימה נעול בלבד (נשלט מ-Veyon Master מרחוק).\n• לתלמידים אין שום גישה לממשק הניהול או לעריכת הרשימה.\n• הרשאות קבצים נעולות לחלוטין (Read & Execute בלבד).",
                Location = new Point(45, 60),
                Size = new Size(395, 45),
                ForeColor = Color.DarkSlateGray
            };

            RadioButton rbTeacher = new RadioButton()
            {
                Text = "👨‍🏫 עמדת מורה (Teacher Station) - למחשב המורה בכיתה",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 115),
                Size = new Size(420, 25)
            };

            Label lblTeacherDesc = new Label()
            {
                Text = "• כולל קיצור דרך לממשק הניהול בענן (הוספת/הסרת אתרים).\n• כלי עזר לשליטה ואינטגרציה עם Veyon Master.",
                Location = new Point(45, 142),
                Size = new Size(395, 30),
                ForeColor = Color.DarkSlateGray
            };

            grpRole.Controls.Add(rbStudent);
            grpRole.Controls.Add(lblStudentDesc);
            grpRole.Controls.Add(rbTeacher);
            grpRole.Controls.Add(lblTeacherDesc);

            Button btnInstall = new Button()
            {
                Text = "התקן כעת ⬅",
                Location = new Point(360, 290),
                Size = new Size(120, 38),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };

            Button btnCancel = new Button()
            {
                Text = "ביטול",
                Location = new Point(230, 290),
                Size = new Size(110, 38),
                DialogResult = DialogResult.Cancel
            };

            btnInstall.Click += (s, e) =>
            {
                SelectedRole = rbTeacher.Checked ? InstallRole.Teacher : InstallRole.Student;
            };

            this.Controls.Add(lblHeader);
            this.Controls.Add(lblSub);
            this.Controls.Add(grpRole);
            this.Controls.Add(btnInstall);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnInstall;
            this.CancelButton = btnCancel;
        }
    }
}
