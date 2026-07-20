using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("流氓软件克星")]
[assembly: AssemblyDescription("扫描和清理 Windows 流氓右键菜单、自启动、计划任务、服务、浏览器插件和文件关联残留")]
[assembly: AssemblyCompany("aakk007")]
[assembly: AssemblyProduct("流氓软件克星")]
[assembly: AssemblyCopyright("Copyright (c) 2026 aakk007")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace RogueCleanerV2
{
    internal static class AppMeta
    {
        public const string ProductName = "流氓软件克星";
        public const string Version = "2.0.0";
        public const string Repository = "https://github.com/aakk007/RogueCleaner";
        public const string ReleasesUrl = "https://github.com/aakk007/RogueCleaner/releases";
        public const string LatestApiUrl = "https://api.github.com/repos/aakk007/RogueCleaner/releases/latest";
        public const string DataDirName = "流氓软件克星数据";
        public const string DotNetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework";
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;

            DataStore store = DataStore.CreateForExecutable(Application.ExecutablePath);
            store.Ensure();
            Logger.Initialize(store);
            bool smoke = HasArg(args, "--scan-smoke");

            try
            {
                if (smoke)
                {
                    List<Finding> findings = new ScannerEngine().ScanAll(null);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "scan-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), findings);
                    return 0;
                }
                ShortcutManager.PromptFirstRunShortcut(store);
                Application.Run(new MainForm(store));
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("启动失败", ex);
                if (!smoke) MessageBox.Show("启动失败：" + ex.Message, AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    internal sealed class DataStore
    {
        public string Root { get; private set; }
        public string Backups { get; private set; }
        public string Reports { get; private set; }
        public string Logs { get; private set; }
        public string Updates { get; private set; }
        public string Quarantine { get; private set; }
        public string State { get; private set; }

        public static DataStore CreateForExecutable(string exePath)
        {
            string exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            string root = Path.Combine(exeDir, AppMeta.DataDirName);
            return new DataStore
            {
                Root = root,
                Backups = Path.Combine(root, "backups"),
                Reports = Path.Combine(root, "reports"),
                Logs = Path.Combine(root, "logs"),
                Updates = Path.Combine(root, "updates"),
                Quarantine = Path.Combine(root, "quarantine"),
                State = Path.Combine(root, "state")
            };
        }

        public void Ensure()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Reports);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Updates);
            Directory.CreateDirectory(Quarantine);
            Directory.CreateDirectory(State);
        }

        public string StateFile(string name)
        {
            return Path.Combine(State, name);
        }

        public string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss");
        }
    }

    internal static class Logger
    {
        private static DataStore store;

        public static void Initialize(DataStore dataStore)
        {
            store = dataStore;
        }

        public static void Error(string message, Exception ex)
        {
            try
            {
                if (store == null) return;
                string path = Path.Combine(store.Logs, "error-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine + ex + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    internal sealed class Finding
    {
        public bool Selected { get; set; }
        public int Id { get; set; }
        public string Risk { get; set; }
        public int Score { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string UserVisibleName { get; set; }
        public string UserImpact { get; set; }
        public string TechnicalLocation { get; set; }
        public string ActionKind { get; set; }
        public ActionTarget Target { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool CanRestore { get; set; }
        public string Evidence { get; set; }
        public string Status { get; set; }

        public bool CanClean
        {
            get { return !string.Equals(ActionKind, "ReportOnly", StringComparison.OrdinalIgnoreCase); }
        }

        public string ActionText
        {
            get
            {
                if (string.Equals(ActionKind, "DeleteRegistryKey", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表项";
                if (string.Equals(ActionKind, "DeleteRegistryValue", StringComparison.OrdinalIgnoreCase)) return "备份后删除这条注册表值";
                if (string.Equals(ActionKind, "MoveFileToBackup", StringComparison.OrdinalIgnoreCase)) return "移动到恢复中心";
                if (string.Equals(ActionKind, "DisableService", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用服务";
                if (string.Equals(ActionKind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return "备份状态后禁用计划任务";
                return "仅提示，不一键动默认程序";
            }
        }
    }

    internal sealed class ActionTarget
    {
        public string Kind { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public string ValueName { get; set; }
        public string FilePath { get; set; }
        public string ServiceName { get; set; }
        public string TaskName { get; set; }
    }

    internal sealed class CleanupResult
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Vendor { get; set; }
        public string Category { get; set; }
        public string ActionKind { get; set; }
        public string TechnicalLocation { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public string Backup { get; set; }
        public ActionTarget Target { get; set; }
    }

    internal sealed class CleanupBatch
    {
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string Path { get; set; }
        public List<CleanupResult> Results { get; set; }
    }

    internal static class AdminUtil
    {
        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RelaunchAsAdmin()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Application.ExecutablePath;
            psi.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
            Application.Exit();
        }
    }

    internal interface IProgressSink
    {
        void Stage(string text);
        void Finding(Finding finding);
    }

    internal static class RuleCatalog
    {
        private sealed class VendorRule
        {
            public string Name;
            public string Snark;
            public int Boost;
            public string[] Patterns;
            public string[] BadComponents;
        }

        private static readonly VendorRule[] Vendors = new VendorRule[]
        {
            new VendorRule { Name = "360 系列", Snark = "右键桌面不够，还想住进开机启动。", Boost = 25, Patterns = new [] { "Qihoo", "Qihu", "奇虎", "360.cn", "360Safe", "360se", "360zip", "360Desktop", "360AlbumViewer", "360AI图片", "Safe360Ext", "SoftMgrExt", "AblumViewer", "shell360ext", "360软件管家", "360安全卫士", "360压缩", "360浏览器" }, BadComponents = new [] { "Safe360Ext", "SoftMgrExt", "AblumViewerMenuExt", "ShellExt64.dll", "shell360ext64.dll" } },
            new VendorRule { Name = "WPS / 金山", Snark = "文档软件顺手也想接管图片、云文档和右键。", Boost = 18, Patterns = new [] { "WPS", "Kingsoft", "金山", "Zhuhai Kingsoft", "kwps", "qingshell", "qingnse", "kdesktop", "photolaunch", "wpscloud", "WpsDrive", "WPS.PIC", "QingNseContextMenu", "kwpsshellext", "qingshellext" }, BadComponents = new [] { "kwpsshellext", "qingshellext", "QingNseContextMenu", "kdesktopshellext", "qkdesktopshellext", "WPS.PIC", "photolaunch.exe" } },
            new VendorRule { Name = "百度 / 百度网盘", Snark = "网盘不只同步文件，还喜欢同步到右键菜单。", Boost = 18, Patterns = new [] { "Baidu", "百度", "BaiduNetdisk", "YunShell", "BaiduYun", "BaiduNetdiskImageViewer", "cloudpic", "YunDetectService", "北京度友" }, BadComponents = new [] { "YunShellExt", "YunShellExplorerCommand", "BaiduNetdiskImageViewer", "cloudpic.dll", "imageviewer" } },
            new VendorRule { Name = "搜狗", Snark = "输入法可以输入字，但没必要输入到开机项里。", Boost = 16, Patterns = new [] { "Sogou", "搜狗", "SogouInput", "SogouPY", "SogouExplorer", "SogouCloud", "SogouIme" }, BadComponents = new [] { "SogouImeBroker", "SogouInput", "SogouExplorer" } },
            new VendorRule { Name = "迅雷", Snark = "下载器最爱给自己安排开机打卡。", Boost = 20, Patterns = new [] { "Xunlei", "Thunder", "迅雷", "Thunder Network", "XLService", "ThunderPlatform", "XLB", "BrowserEngine" }, BadComponents = new [] { "XLService", "ThunderPlatform", "Xunlei.XLB", "ThunderBrowser" } },
            new VendorRule { Name = "腾讯系", Snark = "聊天归聊天，别顺手接管浏览器和启动项。", Boost = 12, Patterns = new [] { "腾讯", "QQBrowser", "QQPCMgr", "TIM.exe", "TIM\\", "QQProtect", "电脑管家" }, BadComponents = new [] { "QQPCMgr", "QQBrowser", "QQProtect" } },
            new VendorRule { Name = "2345 系列", Snark = "名字像门牌号，行为像钉子户。", Boost = 25, Patterns = new [] { "2345", "2345Explorer", "2345Soft", "2345Pic", "2345Zip", "王牌" }, BadComponents = new [] { "2345Explorer", "2345Soft", "2345Pic", "2345Zip" } },
            new VendorRule { Name = "驱动工具", Snark = "修驱动可以，常驻当监工就过分了。", Boost = 18, Patterns = new [] { "DriverGenius", "DriverLife", "驱动精灵", "驱动人生", "LuDaShi", "鲁大师", "MasterLu" }, BadComponents = new [] { "DriverGenius", "DriverLife", "LuDaShi", "MasterLu" } },
            new VendorRule { Name = "国产压缩工具", Snark = "压缩包还没打开，右键先被挤爆了。", Boost = 12, Patterns = new [] { "KuaiZip", "快压", "HaoZip", "好压", "2345Zip", "360压缩" }, BadComponents = new [] { "KuaiZip", "HaoZip", "2345Zip", "360zip" } },
            new VendorRule { Name = "国产影音工具", Snark = "看个视频而已，不需要抢所有文件关联。", Boost = 10, Patterns = new [] { "iQIYI", "爱奇艺", "Youku", "优酷", "Kugou", "酷狗", "Kuwo", "酷我", "PPTV", "暴风" }, BadComponents = new [] { "iQIYI", "Youku", "Kugou", "PPTV" } }
        };

        public static string ResolveVendor(string text)
        {
            VendorRule rule = ResolveVendorRule(text);
            return rule == null ? "未知第三方" : rule.Name;
        }

        public static int VendorBoost(string text)
        {
            VendorRule rule = ResolveVendorRule(text);
            if (rule == null) return 0;
            int score = 35 + rule.Boost;
            foreach (string item in rule.BadComponents)
            {
                if (Contains(text, item)) return score + 30;
            }
            return score;
        }

        public static bool IsKnownVendor(string text)
        {
            return ResolveVendorRule(text) != null;
        }

        private static VendorRule ResolveVendorRule(string text)
        {
            foreach (VendorRule rule in Vendors)
            {
                foreach (string pattern in rule.Patterns)
                {
                    if (Contains(text, pattern)) return rule;
                }
            }
            return null;
        }

        private static bool Contains(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return false;
            return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class RegistryHelper
    {
        public static string NativePath(ActionTarget target)
        {
            string hive = target.Hive == "HKLM" ? "HKLM" : "HKCU";
            return hive + "\\" + target.SubKey;
        }

        public static RegistryKey OpenBase(string hive, string view, bool writable)
        {
            RegistryHive h = hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            RegistryView v = RegistryView.Default;
            if (view == "Registry64") v = RegistryView.Registry64;
            if (view == "Registry32") v = RegistryView.Registry32;
            return RegistryKey.OpenBaseKey(h, v);
        }

        public static RegistryKey OpenSubKey(ActionTarget target, bool writable)
        {
            RegistryKey root = OpenBase(target.Hive, target.View, writable);
            return root.OpenSubKey(target.SubKey, writable);
        }

        public static bool KeyExists(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, false))
            {
                return key != null;
            }
        }

        public static bool ValueExists(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, false))
            {
                if (key == null) return false;
                return key.GetValueNames().Any(delegate(string n) { return string.Equals(n, target.ValueName ?? string.Empty, StringComparison.OrdinalIgnoreCase); });
            }
        }

        public static void DeleteKey(ActionTarget target)
        {
            using (RegistryKey root = OpenBase(target.Hive, target.View, true))
            {
                root.DeleteSubKeyTree(target.SubKey, false);
            }
        }

        public static void DeleteValue(ActionTarget target)
        {
            using (RegistryKey key = OpenSubKey(target, true))
            {
                if (key != null) key.DeleteValue(target.ValueName ?? string.Empty, false);
            }
        }
    }

    internal sealed class ScannerEngine
    {
        private static readonly string[] ContextRoots = new string[]
        {
            @"Software\Classes\*\shell",
            @"Software\Classes\*\shellex\ContextMenuHandlers",
            @"Software\Classes\AllFilesystemObjects\shell",
            @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\shell",
            @"Software\Classes\Directory\shellex\ContextMenuHandlers",
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers",
            @"Software\Classes\Drive\shell",
            @"Software\Classes\Drive\shellex\ContextMenuHandlers",
            @"Software\Classes\Folder\shell",
            @"Software\Classes\Folder\shellex\ContextMenuHandlers",
            @"Software\Classes\DesktopBackground\shell",
            @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers",
            @"Software\Classes\lnkfile\shell",
            @"Software\Classes\lnkfile\shellex\ContextMenuHandlers"
        };

        private static readonly string[] StartupRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        private static readonly string[] BrowserRoots = new string[]
        {
            @"Software\Google\Chrome\Extensions",
            @"Software\Microsoft\Edge\Extensions",
            @"Software\Google\Chrome\NativeMessagingHosts",
            @"Software\Microsoft\Edge\NativeMessagingHosts",
            @"Software\Policies\Google\Chrome\ExtensionInstallForcelist",
            @"Software\Policies\Microsoft\Edge\ExtensionInstallForcelist",
            @"Software\Policies\Google\Chrome\ExtensionSettings",
            @"Software\Policies\Microsoft\Edge\ExtensionSettings"
        };

        private static readonly string[] FileExtensions = new string[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".psd", ".ico",
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".mp3", ".flac", ".wav",
            ".zip", ".rar", ".7z", ".torrent", ".xlb", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
        };

        public List<Finding> ScanAll(IProgressSink sink)
        {
            List<Finding> all = new List<Finding>();
            object gate = new object();
            List<Action> scanners = new List<Action>();

            scanners.Add(delegate { AddRange(all, gate, sink, "右键菜单", ScanContextMenus()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "开机启动", ScanStartupRegistry()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "启动文件夹", ScanStartupFolders()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "后台服务", ScanServices()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "浏览器插件", ScanBrowserExtensions()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "文件关联", ScanFileAssociations()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "计划任务", ScanScheduledTasks()); });

            List<Task> running = new List<Task>();
            foreach (Action scanner in scanners)
            {
                running.Add(Task.Factory.StartNew(scanner));
                while (running.Count >= Math.Max(2, Math.Min(4, Environment.ProcessorCount)))
                {
                    int index = Task.WaitAny(running.ToArray());
                    running.RemoveAt(index);
                }
            }
            Task.WaitAll(running.ToArray());

            List<Finding> sorted = all
                .GroupBy(delegate(Finding f) { return f.Category + "|" + f.TechnicalLocation + "|" + f.UserVisibleName; })
                .Select(delegate(IGrouping<string, Finding> g) { return g.First(); })
                .OrderBy(delegate(Finding f) { return RiskRank(f.Risk); })
                .ThenBy(delegate(Finding f) { return f.Vendor; })
                .ThenBy(delegate(Finding f) { return f.Category; })
                .ThenBy(delegate(Finding f) { return f.UserVisibleName; })
                .ToList();
            for (int i = 0; i < sorted.Count; i++) sorted[i].Id = i + 1;
            return sorted;
        }

        private static void AddRange(List<Finding> all, object gate, IProgressSink sink, string stage, List<Finding> findings)
        {
            if (sink != null) sink.Stage("扫描：" + stage + "，发现 " + findings.Count + " 项");
            lock (gate)
            {
                foreach (Finding finding in findings)
                {
                    all.Add(finding);
                    if (sink != null) sink.Finding(finding);
                }
            }
        }

        private List<Finding> ScanContextMenus()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(ContextRoots, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(root, false))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = RegistryHelper.OpenSubKey(target, false))
                        {
                            string display = ReadString(child, "");
                            string mui = ReadString(child, "MUIVerb");
                            string explorerHandler = ReadString(child, "ExplorerCommandHandler");
                            string command = ReadDefault(target, "command");
                            string title = Join(childName, display, mui, explorerHandler);
                            string text = Join(title, command, target.SubKey);
                            if (!RuleCatalog.IsKnownVendor(text)) continue;
                            list.Add(NewFinding("右键菜单", title, DescribeContextMenu(target.SubKey, title), target, text, 18));
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanStartupRegistry()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(StartupRoots, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(root, false))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        list.Add(NewFinding("开机启动", valueName, "开机后会自动启动：" + FriendlyProgram(valueName, value), target, text, 28));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanStartupFolders()
        {
            List<Finding> list = new List<Finding>();
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string text = file;
                    if (!RuleCatalog.IsKnownVendor(text)) continue;
                    ActionTarget target = new ActionTarget { Kind = "MoveFileToBackup", FilePath = file };
                    list.Add(NewFinding("启动文件夹", Path.GetFileName(file), "开机后会从启动文件夹拉起：" + Path.GetFileName(file), target, text, 28));
                }
            }
            return list;
        }

        private List<Finding> ScanBrowserExtensions()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(BrowserRoots, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(root, false))
                {
                    if (key == null) continue;
                    foreach (string valueName in SafeValueNames(key))
                    {
                        string value = Convert.ToString(key.GetValue(valueName, ""));
                        string text = Join(valueName, value, root.SubKey);
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        list.Add(NewFinding("浏览器插件/外部宿主", valueName, "浏览器可能会加载这个外部扩展/策略：" + valueName, target, text, 35));
                    }
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        string text = Join(childName, root.SubKey);
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        list.Add(NewFinding("浏览器插件/外部宿主", childName, "浏览器可能会加载这个外部扩展/宿主：" + childName, target, text, 35));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanFileAssociations()
        {
            List<Finding> list = new List<Finding>();
            foreach (string ext in FileExtensions)
            {
                foreach (ActionTarget extTarget in RegistryTargets(new string[] { @"Software\Classes\" + ext }, true, true))
                {
                    using (RegistryKey extKey = RegistryHelper.OpenSubKey(extTarget, false))
                    {
                        if (extKey == null) continue;
                        string defaultProgId = ReadString(extKey, "");
                        if (!string.IsNullOrEmpty(defaultProgId))
                        {
                            ActionTarget classTarget = CopyTarget(extTarget);
                            classTarget.Kind = "DeleteRegistryKey";
                            classTarget.SubKey = @"Software\Classes\" + defaultProgId;
                            using (RegistryKey classKey = RegistryHelper.OpenSubKey(classTarget, false))
                            {
                                string command = ReadDefault(classTarget, @"shell\open\command");
                                string text = Join(ext, defaultProgId, command);
                                if (classKey != null && RuleCatalog.IsKnownVendor(text))
                                {
                                    classTarget.Kind = "ReportOnly";
                                    list.Add(NewFinding("文件关联/默认打开程序", ext + " 默认打开：" + FriendlyHandler(defaultProgId), "双击/打开 " + ext + " 现在会交给：" + FriendlyHandler(defaultProgId) + "。这类属于主打开方式，只提示，不一键改。", classTarget, text, 8));
                                }
                            }
                        }
                        foreach (string sub in new string[] { "OpenWithList", "OpenWithProgids" })
                        {
                            ActionTarget subTarget = CopyTarget(extTarget);
                            subTarget.SubKey = extTarget.SubKey + "\\" + sub;
                            using (RegistryKey subKey = RegistryHelper.OpenSubKey(subTarget, false))
                            {
                                if (subKey == null) continue;
                                foreach (string valueName in SafeValueNames(subKey))
                                {
                                    if (string.Equals(valueName, "MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                                    string text = Join(ext, valueName, subTarget.SubKey);
                                    if (!RuleCatalog.IsKnownVendor(text)) continue;
                                    ActionTarget valueTarget = CopyTarget(subTarget);
                                    valueTarget.Kind = "DeleteRegistryValue";
                                    valueTarget.ValueName = valueName;
                                    list.Add(NewFinding("文件关联/打开方式", ext + " 打开方式：" + valueName, "右键“打开方式”里会出现：" + FriendlyHandler(valueName) + "（影响 " + ext + " 文件）", valueTarget, text, 22));
                                }
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanServices()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,DisplayName,PathName,Description,StartMode FROM Win32_Service"))
                {
                    foreach (ManagementObject svc in searcher.Get())
                    {
                        string name = Convert.ToString(svc["Name"]);
                        string display = Convert.ToString(svc["DisplayName"]);
                        string path = Convert.ToString(svc["PathName"]);
                        string desc = Convert.ToString(svc["Description"]);
                        string mode = Convert.ToString(svc["StartMode"]);
                        string text = Join(name, display, path, desc, mode);
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        ActionTarget target = new ActionTarget { Kind = "DisableService", ServiceName = name };
                        Finding finding = NewFinding("后台服务", display, "后台服务会常驻或被系统拉起：" + display, target, text, 42);
                        finding.RequiresAdmin = true;
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描服务失败", ex);
            }
            return list;
        }

        private List<Finding> ScanScheduledTasks()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return list;
                dynamic service = Activator.CreateInstance(serviceType);
                service.Connect();
                ScanTaskFolder(service.GetFolder("\\"), list);
            }
            catch (Exception ex)
            {
                Logger.Error("扫描计划任务失败", ex);
            }
            return list;
        }

        private void ScanTaskFolder(dynamic folder, List<Finding> list)
        {
            foreach (dynamic task in folder.GetTasks(1))
            {
                string name = Convert.ToString(task.Name);
                string path = Convert.ToString(task.Path);
                string text = path;
                try { text = Join(text, Convert.ToString(task.Definition.RegistrationInfo.Description)); } catch { }
                try
                {
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        try { text = Join(text, Convert.ToString(action.Path), Convert.ToString(action.Arguments)); } catch { }
                    }
                }
                catch { }
                if (RuleCatalog.IsKnownVendor(text))
                {
                    ActionTarget target = new ActionTarget { Kind = "DisableScheduledTask", TaskName = path };
                    Finding finding = NewFinding("计划任务/定时拉起", name, "会按计划自动拉起：" + name, target, text, 30);
                    finding.RequiresAdmin = true;
                    list.Add(finding);
                }
            }
            foreach (dynamic child in folder.GetFolders(0))
            {
                ScanTaskFolder(child, list);
            }
        }

        private Finding NewFinding(string category, string title, string impact, ActionTarget target, string text, int baseScore)
        {
            int score = baseScore + RuleCatalog.VendorBoost(text);
            Finding finding = new Finding();
            finding.Selected = false;
            finding.Risk = score >= 80 ? "高" : (score >= 55 ? "中" : "低");
            finding.Score = score;
            finding.Vendor = RuleCatalog.ResolveVendor(text);
            finding.Category = category;
            finding.UserVisibleName = Clean(title);
            finding.UserImpact = impact;
            finding.TechnicalLocation = DescribeTarget(target);
            finding.ActionKind = target.Kind;
            finding.Target = target;
            finding.RequiresAdmin = target.Hive == "HKLM" || target.Kind == "DisableService" || target.Kind == "DisableScheduledTask";
            finding.CanRestore = true;
            finding.Evidence = text;
            finding.Status = "待处理";
            return finding;
        }

        private static IEnumerable<ActionTarget> RegistryTargets(string[] subKeys, bool includeHkcu, bool includeHklm)
        {
            foreach (string subKey in subKeys)
            {
                if (includeHkcu) yield return new ActionTarget { Kind = "Registry", Hive = "HKCU", View = "Default", SubKey = subKey };
                if (includeHklm)
                {
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry64", SubKey = subKey };
                    yield return new ActionTarget { Kind = "Registry", Hive = "HKLM", View = "Registry32", SubKey = subKey };
                }
            }
        }

        private static ActionTarget CopyTarget(ActionTarget source)
        {
            return new ActionTarget { Kind = source.Kind, Hive = source.Hive, View = source.View, SubKey = source.SubKey, ValueName = source.ValueName, FilePath = source.FilePath, ServiceName = source.ServiceName, TaskName = source.TaskName };
        }

        private static string[] SafeSubKeyNames(RegistryKey key)
        {
            try { return key.GetSubKeyNames(); } catch { return new string[0]; }
        }

        private static string[] SafeValueNames(RegistryKey key)
        {
            try { return key.GetValueNames(); } catch { return new string[0]; }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            if (key == null) return string.Empty;
            try { return Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; }
        }

        private static string ReadDefault(ActionTarget target, string child)
        {
            ActionTarget t = CopyTarget(target);
            t.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = RegistryHelper.OpenSubKey(t, false))
            {
                return ReadString(key, "");
            }
        }

        private static int RiskRank(string risk)
        {
            if (risk == "高") return 0;
            if (risk == "中") return 1;
            return 2;
        }

        private static string DescribeTarget(ActionTarget target)
        {
            if (target.Kind == "MoveFileToBackup") return target.FilePath;
            if (target.Kind == "DisableService") return "服务：" + target.ServiceName;
            if (target.Kind == "DisableScheduledTask") return "计划任务：" + target.TaskName;
            string path = RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) path += "::" + target.ValueName;
            if (!string.IsNullOrEmpty(target.View) && target.View != "Default") path += " (" + target.View + ")";
            return path;
        }

        private static string DescribeContextMenu(string subKey, string title)
        {
            string lower = subKey.ToLowerInvariant();
            string where = "资源管理器右键菜单";
            if (lower.IndexOf("\\desktopbackground\\") >= 0 || lower.IndexOf("\\directory\\background\\") >= 0) where = "桌面/文件夹空白处右键";
            else if (lower.IndexOf("\\drive\\") >= 0) where = "磁盘盘符右键";
            else if (lower.IndexOf("\\directory\\") >= 0) where = "文件夹右键";
            else if (lower.IndexOf("\\lnkfile\\") >= 0) where = "快捷方式右键";
            else if (lower.IndexOf("\\*\\") >= 0) where = "普通文件右键";
            return where + "会出现：" + Clean(title);
        }

        private static string FriendlyProgram(string name, string command)
        {
            if (!string.IsNullOrEmpty(name)) return name;
            return command;
        }

        private static string FriendlyHandler(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘";
            if (lower.IndexOf("wps.doc") >= 0 || lower.IndexOf("wps.docx") >= 0) return "WPS 文字";
            if (lower.IndexOf("kwps.pdf") >= 0) return "WPS PDF";
            if (lower.IndexOf("wpp.ppt") >= 0) return "WPS 演示";
            if (lower.IndexOf("et.xls") >= 0) return "WPS 表格";
            if (lower.IndexOf("xunlei") >= 0) return "迅雷";
            return value;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Join(params string[] parts)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(part.Trim());
            }
            return sb.ToString();
        }
    }

    internal sealed class CleanerEngine
    {
        private readonly DataStore store;

        public CleanerEngine(DataStore store)
        {
            this.store = store;
        }

        public CleanupBatch Clean(IEnumerable<Finding> findings)
        {
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            Directory.CreateDirectory(Path.Combine(batchPath, "registry"));
            Directory.CreateDirectory(Path.Combine(batchPath, "files"));
            Directory.CreateDirectory(Path.Combine(batchPath, "services"));
            Directory.CreateDirectory(Path.Combine(batchPath, "tasks"));
            List<CleanupResult> results = new List<CleanupResult>();

            foreach (Finding finding in findings.Where(delegate(Finding f) { return f.Selected && f.CanClean; }))
            {
                CleanupResult result = CleanOne(finding, batchPath);
                results.Add(result);
            }

            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = results };
            WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            WriteJson(Path.Combine(store.Reports, "cleanup-" + id + ".json"), results);
            return batch;
        }

        private CleanupResult CleanOne(Finding finding, string batchPath)
        {
            CleanupResult result = new CleanupResult();
            result.Id = finding.Id;
            result.Title = finding.UserVisibleName;
            result.Vendor = finding.Vendor;
            result.Category = finding.Category;
            result.ActionKind = finding.ActionKind;
            result.TechnicalLocation = finding.TechnicalLocation;
            result.Target = finding.Target;
            result.Status = "Skipped";
            result.Message = "未执行。";

            try
            {
                ActionTarget target = finding.Target;
                if (target == null || string.IsNullOrEmpty(target.Kind)) throw new InvalidOperationException("缺少清理目标。");
                if (target.Kind == "DeleteRegistryKey")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    RegistryHelper.DeleteKey(target);
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "注册表键已删除。" : "复核失败：注册表键仍然存在。";
                }
                else if (target.Kind == "DeleteRegistryValue")
                {
                    result.Backup = BackupRegistry(batchPath, target);
                    RegistryHelper.DeleteValue(target);
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "注册表值已删除。" : "复核失败：注册表值仍然存在。";
                }
                else if (target.Kind == "MoveFileToBackup")
                {
                    string src = Environment.ExpandEnvironmentVariables(target.FilePath ?? string.Empty);
                    if (File.Exists(src))
                    {
                        string dest = Path.Combine(Path.Combine(batchPath, "files"), Path.GetFileName(src));
                        File.Move(src, dest);
                        result.Backup = dest;
                    }
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "文件已移动到备份。" : "复核失败：文件仍然存在。";
                }
                else if (target.Kind == "DisableService")
                {
                    string serviceFile = Path.Combine(Path.Combine(batchPath, "services"), SafeFileName(target.ServiceName) + ".json");
                    WriteText(serviceFile, GetServiceState(target.ServiceName));
                    result.Backup = serviceFile;
                    RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= disabled");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "服务已禁用。" : "复核失败：服务仍未禁用。";
                }
                else if (target.Kind == "DisableScheduledTask")
                {
                    string taskDir = Path.Combine(Path.Combine(batchPath, "tasks"), SafeFileName(target.TaskName));
                    Directory.CreateDirectory(taskDir);
                    WriteText(Path.Combine(taskDir, "task.xml"), QueryTaskXml(target.TaskName));
                    bool wasEnabled;
                    WriteText(Path.Combine(taskDir, "state.txt"), TryGetScheduledTaskEnabled(target.TaskName, out wasEnabled) && wasEnabled ? "Enabled" : "Disabled");
                    result.Backup = taskDir;
                    RunHidden("schtasks.exe", "/Change /TN \"" + target.TaskName + "\" /Disable");
                    result.Status = VerifyApplied(target) ? "Done" : "Failed";
                    result.Message = result.Status == "Done" ? "计划任务已禁用。" : "复核失败：计划任务仍未禁用。";
                }
                else
                {
                    result.Status = "Skipped";
                    result.Message = "只报告，不自动清理。";
                }
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Message = ex.Message;
                Logger.Error("清理失败：" + finding.UserVisibleName, ex);
            }
            return result;
        }

        public bool VerifyApplied(ActionTarget target)
        {
            if (target.Kind == "DeleteRegistryKey") return !RegistryHelper.KeyExists(target);
            if (target.Kind == "DeleteRegistryValue") return !RegistryHelper.ValueExists(target);
            if (target.Kind == "MoveFileToBackup") return string.IsNullOrEmpty(target.FilePath) || !File.Exists(Environment.ExpandEnvironmentVariables(target.FilePath));
            if (target.Kind == "DisableService") return IsServiceDisabled(target.ServiceName);
            if (target.Kind == "DisableScheduledTask")
            {
                bool enabled;
                return TryGetScheduledTaskEnabled(target.TaskName, out enabled) && !enabled;
            }
            return true;
        }

        private string BackupRegistry(string batchPath, ActionTarget target)
        {
            string native = RegistryHelper.NativePath(target);
            string path = Path.Combine(Path.Combine(batchPath, "registry"), SafeFileName(native) + ".reg");
            RunHidden("reg.exe", "export \"" + native + "\" \"" + path + "\" /y");
            return File.Exists(path) ? path : null;
        }

        public void RestoreBatch(CleanupBatch batch)
        {
            foreach (CleanupResult result in batch.Results)
            {
                RestoreResult(result);
            }
        }

        public void RestoreResult(CleanupResult result)
        {
            if (result == null) return;
            if (!string.IsNullOrEmpty(result.Backup) && result.Backup.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) && File.Exists(result.Backup))
            {
                RunHidden("reg.exe", "import \"" + result.Backup + "\"");
            }
            else if (result.Target != null && result.Target.Kind == "MoveFileToBackup" && !string.IsNullOrEmpty(result.Backup) && File.Exists(result.Backup))
            {
                string dest = Environment.ExpandEnvironmentVariables(result.Target.FilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (!File.Exists(dest)) File.Move(result.Backup, dest);
            }
            else if (result.Target != null && result.Target.Kind == "DisableService" && !string.IsNullOrEmpty(result.Backup) && File.Exists(result.Backup))
            {
                string state = File.ReadAllText(result.Backup, Encoding.UTF8);
                string start = state.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0 ? "auto" : "demand";
                RunHidden("sc.exe", "config \"" + result.Target.ServiceName + "\" start= " + start);
            }
            else if (result.Target != null && result.Target.Kind == "DisableScheduledTask" && !string.IsNullOrEmpty(result.Backup) && Directory.Exists(result.Backup))
            {
                string xml = Path.Combine(result.Backup, "task.xml");
                string stateFile = Path.Combine(result.Backup, "state.txt");
                if (!ScheduledTaskExists(result.Target.TaskName) && File.Exists(xml))
                {
                    RunHidden("schtasks.exe", "/Create /TN \"" + result.Target.TaskName + "\" /XML \"" + xml + "\" /F");
                }
                string state = File.Exists(stateFile) ? File.ReadAllText(stateFile, Encoding.UTF8) : "Enabled";
                RunHidden("schtasks.exe", "/Change /TN \"" + result.Target.TaskName + "\" " + (state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ? "/Disable" : "/Enable"));
            }
        }

        public List<CleanupBatch> LoadBatches()
        {
            List<CleanupBatch> list = new List<CleanupBatch>();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            foreach (string manifest in Directory.GetFiles(store.Backups, "manifest.json", SearchOption.AllDirectories))
            {
                try
                {
                    CleanupBatch batch = serializer.Deserialize<CleanupBatch>(File.ReadAllText(manifest, Encoding.UTF8));
                    if (batch != null) list.Add(batch);
                }
                catch (Exception ex)
                {
                    Logger.Error("读取恢复清单失败：" + manifest, ex);
                }
            }
            return list.OrderByDescending(delegate(CleanupBatch b) { return b.Id; }).ToList();
        }

        private static string GetServiceState(string serviceName)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name,StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return Convert.ToString(obj["StartMode"]);
                    }
                }
            }
            catch { }
            return "Unknown";
        }

        private static bool IsServiceDisabled(string serviceName)
        {
            return string.Equals(GetServiceState(serviceName), "Disabled", StringComparison.OrdinalIgnoreCase);
        }

        private static string QueryTaskXml(string taskName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + taskName + "\" /XML");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    return string.IsNullOrWhiteSpace(output) ? error : output;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("备份计划任务失败：" + taskName, ex);
                return string.Empty;
            }
        }

        private static bool ScheduledTaskExists(string taskName)
        {
            bool enabled;
            return TryGetScheduledTaskEnabled(taskName, out enabled);
        }

        private static bool TryGetScheduledTaskEnabled(string taskName, out bool enabled)
        {
            enabled = false;
            try
            {
                if (string.IsNullOrEmpty(taskName)) return false;
                string normalized = taskName.StartsWith("\\", StringComparison.Ordinal) ? taskName : "\\" + taskName;
                int slash = normalized.LastIndexOf('\\');
                string folderPath = slash <= 0 ? "\\" : normalized.Substring(0, slash);
                string name = normalized.Substring(slash + 1);
                Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null) return false;
                dynamic service = Activator.CreateInstance(serviceType);
                service.Connect();
                dynamic folder = service.GetFolder(folderPath);
                dynamic task = folder.GetTask(name);
                enabled = Convert.ToBoolean(task.Enabled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void WriteJson(string path, object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            WriteText(path, serializer.Serialize(value));
        }

        private static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text ?? string.Empty, new UTF8Encoding(true));
        }

        private static void RunHidden(string file, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(60000);
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }
    }

    internal sealed class MainForm : Form, IProgressSink
    {
        private readonly DataStore store;
        private readonly BindingList<Finding> rows = new BindingList<Finding>();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button scanButton = new Button();
        private readonly Button cleanButton = new Button();
        private readonly Button restoreButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button lowButton = new Button();
        private readonly Button updateButton = new Button();
        private readonly Button adminButton = new Button();
        private readonly TextBox searchBox = new TextBox();
        private readonly object pendingGate = new object();
        private readonly List<Finding> pending = new List<Finding>();
        private System.Windows.Forms.Timer flushTimer;

        public MainForm(DataStore store)
        {
            this.store = store;
            BuildUi();
            UpdateChecker.CheckOnStartup(store, this);
        }

        private void BuildUi()
        {
            Text = AppMeta.ProductName + " " + AppMeta.Version;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 700);
            Size = new Size(1280, 780);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 4;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.FromArgb(15, 118, 110);
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "流氓软件克星";
            title.ForeColor = Color.White;
            title.BackColor = Color.Transparent;
            title.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 20);
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "v2 单文件预览：C# 多线程扫描，清理后复核，所有运行文件只进“流氓软件克星数据”。";
            sub.ForeColor = Color.FromArgb(224, 242, 254);
            sub.AutoSize = true;
            sub.Location = new Point(32, 68);
            header.Controls.Add(sub);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(18, 12, 18, 6);
            actions.WrapContents = false;
            root.Controls.Add(actions, 0, 1);

            ConfigureButton(scanButton, "开始扫描", Color.FromArgb(14, 116, 144));
            ConfigureButton(cleanButton, "清理勾选", Color.FromArgb(220, 38, 38));
            ConfigureButton(selectAllButton, "勾选可清理", Color.FromArgb(2, 132, 199));
            ConfigureButton(lowButton, "只勾低风险", Color.FromArgb(22, 163, 74));
            ConfigureButton(restoreButton, "恢复中心", Color.FromArgb(79, 70, 229));
            ConfigureButton(updateButton, "检查更新", Color.FromArgb(234, 88, 12));
            ConfigureButton(adminButton, AdminUtil.IsAdministrator() ? "已是管理员" : "管理员重启", Color.FromArgb(71, 85, 105));
            actions.Controls.Add(scanButton);
            actions.Controls.Add(cleanButton);
            actions.Controls.Add(selectAllButton);
            actions.Controls.Add(lowButton);
            actions.Controls.Add(restoreButton);
            actions.Controls.Add(updateButton);
            actions.Controls.Add(adminButton);
            adminButton.Enabled = !AdminUtil.IsAdministrator();
            Label searchLabel = new Label();
            searchLabel.Text = "搜索";
            searchLabel.Width = 42;
            searchLabel.Height = 34;
            searchLabel.TextAlign = ContentAlignment.MiddleCenter;
            actions.Controls.Add(searchLabel);
            searchBox.Width = 260;
            searchBox.Height = 32;
            actions.Controls.Add(searchBox);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.RowCount = 2;
            body.ColumnCount = 1;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Padding = new Padding(18, 0, 18, 10);
            root.Controls.Add(body, 0, 2);

            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Text = "未扫描。";
            body.Controls.Add(summaryLabel, 0, 0);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.DataSource = rows;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersHeight = 38;
            grid.BackgroundColor = Color.White;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 118, 110);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 251, 241);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            grid.ShowCellToolTips = true;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = "选", Width = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Risk", HeaderText = "风险", Width = 60, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "厂商", Width = 130, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Category", HeaderText = "在哪里冒出来", Width = 155, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UserVisibleName", HeaderText = "用户会看到什么", Width = 240, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UserImpact", HeaderText = "影响说明", Width = 390, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ActionText", HeaderText = "工具会怎么处理", Width = 210, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TechnicalLocation", HeaderText = "技术位置", Width = 420, ReadOnly = true });
            body.Controls.Add(grid, 0, 1);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(226, 232, 240);
            root.Controls.Add(footer, 0, 3);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(18, 0, 170, 0);
            statusLabel.Text = "就绪。数据目录：" + store.Root;
            footer.Controls.Add(statusLabel);
            progress.Dock = DockStyle.Right;
            progress.Width = 150;
            progress.Style = ProgressBarStyle.Continuous;
            footer.Controls.Add(progress);

            flushTimer = new System.Windows.Forms.Timer();
            flushTimer.Interval = 200;
            flushTimer.Tick += delegate { FlushPendingRows(); };

            scanButton.Click += delegate { StartScan(); };
            cleanButton.Click += delegate { StartClean(); };
            selectAllButton.Click += delegate { SetAll(true); };
            lowButton.Click += delegate { SelectLowRisk(); };
            restoreButton.Click += delegate { new RecoveryCenterForm(store).ShowDialog(this); };
            updateButton.Click += delegate { UpdateChecker.CheckNow(this, true); };
            adminButton.Click += delegate { AdminUtil.RelaunchAsAdmin(); };
            searchBox.TextChanged += delegate { ApplyFilter(); };
            rows.ListChanged += delegate { UpdateSummary(); };
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
                if (finding != null && finding.Selected && !finding.CanClean)
                {
                    finding.Selected = false;
                    statusLabel.Text = "这项是默认打开程序归属，只提示，不参与一键清理。";
                    grid.Refresh();
                }
                UpdateSummary();
            };
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            e.ToolTipText =
                "用户会看到：" + finding.UserVisibleName + Environment.NewLine +
                "影响：" + finding.UserImpact + Environment.NewLine +
                "处理：" + finding.ActionText + Environment.NewLine +
                "位置：" + finding.TechnicalLocation + Environment.NewLine +
                "证据：" + finding.Evidence;
        }

        private static void ConfigureButton(Button button, string text, Color color)
        {
            button.Text = text;
            button.Width = 112;
            button.Height = 34;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.Margin = new Padding(0, 0, 10, 0);
        }

        private void StartScan()
        {
            SetBusy(true, "扫描中：多线程翻注册表、服务、计划任务和浏览器角落。");
            rows.Clear();
            lock (pendingGate) pending.Clear();
            flushTimer.Start();
            Task.Factory.StartNew(delegate
            {
                try
                {
                    ScannerEngine engine = new ScannerEngine();
                    List<Finding> result = engine.ScanAll(this);
                    BeginInvoke((MethodInvoker)delegate
                    {
                        flushTimer.Stop();
                        FlushPendingRows();
                        rows.Clear();
                        foreach (Finding finding in result) rows.Add(finding);
                        CleanupEngineWriteScanReport(result);
                        SetBusy(false, "扫描完成。发现 " + result.Count + " 项。");
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("扫描失败", ex);
                    BeginInvoke((MethodInvoker)delegate { SetBusy(false, "扫描失败：" + ex.Message); MessageBox.Show(ex.Message, "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error); });
                }
            });
        }

        private void StartClean()
        {
            grid.EndEdit();
            int reportOnly = rows.Count(delegate(Finding f) { return f.Selected && !f.CanClean; });
            List<Finding> selected = rows.Where(delegate(Finding f) { return f.Selected && f.CanClean; }).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(reportOnly > 0 ? "你勾到的是“只提示”项目，这类默认打开程序不参与一键清理。" : "还没勾选任何可清理项目。", AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selected.Any(delegate(Finding f) { return f.RequiresAdmin; }) && !AdminUtil.IsAdministrator())
            {
                DialogResult elevate = MessageBox.Show("你勾选的项目里有后台服务、系统注册表或计划任务，需要管理员权限。\n\n是否现在以管理员身份重启工具？", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes) AdminUtil.RelaunchAsAdmin();
                return;
            }
            int high = selected.Count(delegate(Finding f) { return f.Risk == "高"; });
            DialogResult answer = MessageBox.Show("准备清理 " + selected.Count + " 项，高风险 " + high + " 项。\n\n会先备份、再清理、最后复核和复扫。继续？", "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "清理中：先备份，再动手，最后复核。");
            Task.Factory.StartNew(delegate
            {
                try
                {
                    CleanerEngine cleaner = new CleanerEngine(store);
                    CleanupBatch batch = cleaner.Clean(selected);
                    ScannerEngine scanner = new ScannerEngine();
                    List<Finding> refreshed = scanner.ScanAll(null);
                    BeginInvoke((MethodInvoker)delegate
                    {
                        rows.Clear();
                        foreach (Finding finding in refreshed) rows.Add(finding);
                        int failed = batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Failed"; });
                        SetBusy(false, failed > 0 ? "清理后复核发现残留：" + failed + " 项。" : "清理完成，已自动复扫。");
                        MessageBox.Show("成功：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; }) + " 项\n失败/残留：" + failed + " 项\n跳过：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Skipped"; }) + " 项\n\n备份目录：" + batch.Path, failed > 0 ? "清理后仍有残留" : "清理完成", MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("清理失败", ex);
                    BeginInvoke((MethodInvoker)delegate { SetBusy(false, "清理失败：" + ex.Message); MessageBox.Show(ex.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error); });
                }
            });
        }

        public void Stage(string text)
        {
            if (!IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { statusLabel.Text = text; }); } catch { }
        }

        public void Finding(Finding finding)
        {
            lock (pendingGate) pending.Add(finding);
        }

        private void FlushPendingRows()
        {
            List<Finding> chunk;
            lock (pendingGate)
            {
                if (pending.Count == 0) return;
                chunk = pending.Take(100).ToList();
                pending.RemoveRange(0, chunk.Count);
            }
            foreach (Finding finding in chunk) rows.Add(finding);
        }

        private void SetBusy(bool busy, string status)
        {
            scanButton.Enabled = !busy;
            cleanButton.Enabled = !busy;
            restoreButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            lowButton.Enabled = !busy;
            updateButton.Enabled = !busy;
            adminButton.Enabled = !busy && !AdminUtil.IsAdministrator();
            searchBox.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progress.Value = busy ? 0 : 0;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            statusLabel.Text = status;
        }

        private void UpdateSummary()
        {
            int selected = rows.Count(delegate(Finding f) { return f.Selected; });
            int high = rows.Count(delegate(Finding f) { return f.Risk == "高"; });
            int medium = rows.Count(delegate(Finding f) { return f.Risk == "中"; });
            int low = rows.Count(delegate(Finding f) { return f.Risk == "低"; });
            int reportOnly = rows.Count(delegate(Finding f) { return !f.CanClean; });
            summaryLabel.Text = "发现 " + rows.Count + " 项，已勾选 " + selected + " 项。高风险 " + high + "，中风险 " + medium + "，低风险 " + low + "，仅提示 " + reportOnly + "。";
        }

        private void SetAll(bool value)
        {
            foreach (Finding finding in rows) finding.Selected = value && finding.CanClean;
            grid.Refresh();
            UpdateSummary();
        }

        private void SelectLowRisk()
        {
            foreach (Finding finding in rows) finding.Selected = finding.Risk == "低" && finding.CanClean;
            grid.Refresh();
            UpdateSummary();
        }

        private void ApplyFilter()
        {
            string text = searchBox.Text.Trim();
            CurrencyManager manager = (CurrencyManager)BindingContext[rows];
            manager.SuspendBinding();
            foreach (DataGridViewRow row in grid.Rows)
            {
                Finding finding = row.DataBoundItem as Finding;
                if (finding == null) continue;
                string haystack = (finding.Risk + " " + finding.Vendor + " " + finding.Category + " " + finding.UserVisibleName + " " + finding.UserImpact + " " + finding.TechnicalLocation);
                row.Visible = string.IsNullOrEmpty(text) || haystack.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            manager.ResumeBinding();
        }

        private void CleanupEngineWriteScanReport(List<Finding> findings)
        {
            string path = Path.Combine(store.Reports, "scan-" + store.Timestamp() + ".json");
            CleanerEngine.WriteJson(path, findings);
        }
    }

    internal sealed class RecoveryCenterForm : Form
    {
        private readonly DataStore store;
        private readonly ListBox batchList = new ListBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Button restoreBatchButton = new Button();
        private List<CleanupBatch> batches = new List<CleanupBatch>();

        public RecoveryCenterForm(DataStore store)
        {
            this.store = store;
            BuildUi();
            LoadBatches();
        }

        private void BuildUi()
        {
            Text = "恢复中心";
            Size = new Size(980, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);

            batchList.Dock = DockStyle.Fill;
            root.Controls.Add(batchList, 0, 0);
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = true;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            root.Controls.Add(grid, 1, 0);
            restoreBatchButton.Text = "恢复选中批次";
            restoreBatchButton.Width = 140;
            restoreBatchButton.Height = 32;
            restoreBatchButton.Margin = new Padding(12, 8, 0, 0);
            root.Controls.Add(restoreBatchButton, 0, 1);

            batchList.SelectedIndexChanged += delegate { ShowSelectedBatch(); };
            restoreBatchButton.Click += delegate { RestoreSelectedBatch(); };
        }

        private void LoadBatches()
        {
            batches = new CleanerEngine(store).LoadBatches();
            batchList.Items.Clear();
            foreach (CleanupBatch batch in batches)
            {
                int failed = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Failed"; });
                int done = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; });
                batchList.Items.Add(batch.Id + "  成功 " + done + "  失败 " + failed);
            }
            if (batchList.Items.Count > 0) batchList.SelectedIndex = 0;
        }

        private void ShowSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            grid.DataSource = batches[batchList.SelectedIndex].Results;
        }

        private void RestoreSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            DialogResult answer = MessageBox.Show("恢复批次 " + batch.Id + "？\n\n恢复会导入备份注册表或移回被隔离文件。", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                new CleanerEngine(store).RestoreBatch(batch);
                MessageBox.Show("恢复命令已执行。建议重新扫描确认。", "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败", ex);
                MessageBox.Show(ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class UpdateChecker
    {
        public static void CheckOnStartup(DataStore store, IWin32Window owner)
        {
            string marker = store.StateFile("last-update-check.txt");
            try
            {
                if (File.Exists(marker))
                {
                    DateTime last;
                    if (DateTime.TryParse(File.ReadAllText(marker), out last) && (DateTime.Now - last).TotalHours < 24) return;
                }
                File.WriteAllText(marker, DateTime.Now.ToString("o"), Encoding.UTF8);
                Task.Factory.StartNew(delegate { CheckNow(owner, false); });
            }
            catch (Exception ex)
            {
                Logger.Error("启动更新检查失败", ex);
            }
        }

        public static void CheckNow(IWin32Window owner, bool showNoUpdate)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "RogueCleaner/" + AppMeta.Version);
                    string json = client.DownloadString(AppMeta.LatestApiUrl);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> release = serializer.DeserializeObject(json) as Dictionary<string, object>;
                    string tag = release != null && release.ContainsKey("tag_name") ? Convert.ToString(release["tag_name"]) : string.Empty;
                    string body = release != null && release.ContainsKey("body") ? Convert.ToString(release["body"]) : string.Empty;
                    string html = release != null && release.ContainsKey("html_url") ? Convert.ToString(release["html_url"]) : AppMeta.ReleasesUrl;
                    string latest = tag.TrimStart('v', 'V');
                    if (IsNewer(latest, AppMeta.Version))
                    {
                        DialogResult answer = MessageBox.Show(owner, "发现新版本：" + tag + "\n当前版本：" + AppMeta.Version + "\n\n" + TrimBody(body) + "\n\n是否打开 GitHub 下载页？", "发现更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (answer == DialogResult.Yes) Process.Start(new ProcessStartInfo { FileName = html, UseShellExecute = true });
                    }
                    else if (showNoUpdate)
                    {
                        MessageBox.Show(owner, "当前已经是最新版本。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("检查更新失败", ex);
                if (showNoUpdate)
                {
                    MessageBox.Show(owner, "检查更新失败，可能是系统 TLS 或网络限制。\n\n可以手动打开：" + AppMeta.ReleasesUrl + "\n\n" + ex.Message, "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(latest, out a)) return false;
            if (!Version.TryParse(current, out b)) return false;
            return a > b;
        }

        private static string TrimBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            body = body.Replace("\r", "").Trim();
            return body.Length > 300 ? body.Substring(0, 300) + "..." : body;
        }
    }

    internal static class ShortcutManager
    {
        public static void PromptFirstRunShortcut(DataStore store)
        {
            string marker = store.StateFile("first-run-shortcut.txt");
            if (File.Exists(marker)) return;
            DialogResult answer = MessageBox.Show("是否创建桌面快捷方式？\n\n只创建快捷方式，不移动主程序。", AppMeta.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Yes) CreateDesktopShortcut();
            File.WriteAllText(marker, DateTime.Now.ToString("o"), Encoding.UTF8);
        }

        private static void CreateDesktopShortcut()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, AppMeta.ProductName + ".lnk");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = Application.ExecutablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            shortcut.Description = AppMeta.ProductName;
            shortcut.IconLocation = Application.ExecutablePath + ",0";
            shortcut.Save();
        }
    }
}
