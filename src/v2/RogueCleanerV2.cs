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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
[assembly: AssemblyVersion("2.0.12.0")]
[assembly: AssemblyFileVersion("2.0.12.0")]

namespace RogueCleanerV2
{
    internal static class AppMeta
    {
        public const string ProductName = "流氓软件克星";
        public const string Version = "2.0.12";
        public const string AuthorName = "aakk007";
        public const string Author52PojieUrl = "https://www.52pojie.cn/home.php?mod=space&uid=286924";
        public const string AuthorGitHubUrl = "https://github.com/aakk007";
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
            bool identitySmoke = HasArg(args, "--identity-smoke");
            bool feedbackSmoke = HasArg(args, "--feedback-smoke");
            bool uiSmoke = HasArg(args, "--ui-smoke");
#if VALIDATION
            bool acceptance = HasArg(args, "--acceptance-test");
#endif

            try
            {
#if VALIDATION
                if (acceptance)
                {
                    int exitCode = ValidationRunner.Run(store);
                    Environment.ExitCode = exitCode;
                    return exitCode;
                }
#endif
                if (identitySmoke)
                {
                    List<string> failures = RuleCatalog.RunIdentitySelfTests();
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "identity-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 2;
                    return Environment.ExitCode;
                }
                if (feedbackSmoke)
                {
                    List<string> failures = FeedbackService.RunSelfTests(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "feedback-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 3;
                    return Environment.ExitCode;
                }
                if (uiSmoke)
                {
                    List<string> failures = UiRegression.Run(store);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "ui-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), failures);
                    Environment.ExitCode = failures.Count == 0 ? 0 : 4;
                    return Environment.ExitCode;
                }
                if (smoke)
                {
                    List<Finding> findings = new ScannerEngine().ScanAll(null);
                    CleanerEngine.WriteJson(Path.Combine(store.Reports, "scan-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), findings);
                    Environment.ExitCode = 0;
                    return 0;
                }
                Application.Run(new MainForm(store));
                Environment.ExitCode = 0;
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("启动失败", ex);
                if (!smoke && !identitySmoke && !feedbackSmoke && !uiSmoke) MessageBox.Show("启动失败：" + ex.Message, AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkComObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
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
        public string Feedbacks { get; private set; }

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
                State = Path.Combine(root, "state"),
                Feedbacks = Path.Combine(root, "feedback")
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
            Directory.CreateDirectory(Feedbacks);
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

    internal sealed class Finding : INotifyPropertyChanged
    {
        private bool selected;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                OnPropertyChanged("Selected");
            }
        }

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

        public string RiskDisplay
        {
            get { return CanClean ? Risk : "仅提示"; }
        }

        public bool BulkSelectable
        {
            get { return CanClean && !string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase); }
        }

        public string SelectionHint
        {
            get
            {
                if (CanClean) return "可勾选：工具会先备份，再按“工具会怎么处理”执行。";
                return "不可勾选：" + ReportOnlyActionText();
            }
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
                if (string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase)) return "弹出卸载器，用户自己确认";
                return ReportOnlyActionText();
            }
        }

        private string ReportOnlyActionText()
        {
            string category = Category ?? string.Empty;
            if (category.IndexOf("默认打开程序", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：这是双击默认打开方式，不替用户改默认应用";
            if (category.IndexOf("卸载入口", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：没有可靠卸载命令，不硬删主程序";
            if (category.IndexOf("正在运行", StringComparison.OrdinalIgnoreCase) >= 0) return "仅提示：不强杀正在运行的进程";
            return "仅提示：为避免误伤，不参与一键清理";
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
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
        public string UninstallCommand { get; set; }
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

    internal sealed class ScanErrorReport
    {
        public string StartedAt { get; set; }
        public string FailedAt { get; set; }
        public string ProductVersion { get; set; }
        public string ExecutablePath { get; set; }
        public string ExecutableDirectory { get; set; }
        public string DataDirectory { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string StackTrace { get; set; }
    }

    internal sealed class RestoreBatchResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<string> Messages { get; set; }

        public bool AllSucceeded
        {
            get { return Failed == 0; }
        }
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

    internal sealed class VendorEvidence
    {
        public readonly List<string> HumanTexts = new List<string>();
        public readonly List<string> Publishers = new List<string>();
        public readonly List<string> ProductNames = new List<string>();
        public readonly List<string> TechnicalIdentifiers = new List<string>();
        public readonly List<string> Commands = new List<string>();
        public readonly List<string> FilePaths = new List<string>();
        public readonly List<string> MsiProductCodes = new List<string>();
        public readonly List<string> OpaqueValues = new List<string>();

        public VendorEvidence AddHuman(params string[] values) { Add(HumanTexts, values); return this; }
        public VendorEvidence AddPublisher(params string[] values) { Add(Publishers, values); return this; }
        public VendorEvidence AddProduct(params string[] values) { Add(ProductNames, values); return this; }
        public VendorEvidence AddTechnical(params string[] values) { Add(TechnicalIdentifiers, values); return this; }
        public VendorEvidence AddCommand(params string[] values) { Add(Commands, values); return this; }
        public VendorEvidence AddFile(params string[] values) { Add(FilePaths, values); return this; }
        public VendorEvidence AddMsi(params string[] values) { Add(MsiProductCodes, values); return this; }
        public VendorEvidence AddOpaque(params string[] values) { Add(OpaqueValues, values); return this; }

        private static void Add(List<string> target, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!target.Contains(value, StringComparer.OrdinalIgnoreCase)) target.Add(value.Trim());
            }
        }
    }

    internal sealed class VendorIdentityResult
    {
        public string Vendor { get; set; }
        public int Confidence { get; set; }
        public bool Confirmed { get; set; }
        public bool Conflicted { get; set; }
        public string EvidenceSummary { get; set; }
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
            new VendorRule { Name = "360 系列", Snark = "右键桌面不够，还想住进开机启动。", Boost = 25, Patterns = new [] { "Qihoo", "Qihu", "奇虎", "360.cn", "360Safe", "360sd", "360rp", "360se", "360Chrome", "360zip", "360Desktop", "360DesktopLite", "360Wallpaper", "360AlbumViewer", "360AI图片", "360AI", "360Pic", "360KanPic", "360Image", "Safe360Ext", "SoftMgrExt", "AblumViewer", "AlbumViewer", "shell360ext", "QHActiveDefense", "ZhuDongFangYu", "QHWatchdog", "QHProtected", "QHWebProtection", "QHSafeTray", "360软件管家", "360安全卫士", "360压缩", "360浏览器", "360极速浏览器", "360看图" }, BadComponents = new [] { "Safe360Ext", "SoftMgrExt", "AblumViewerMenuExt", "AlbumViewerMenuExt", "ShellExt64.dll", "shell360ext64.dll", "360AI图片", "QHActiveDefense", "ZhuDongFangYu" } },
            new VendorRule { Name = "WPS / 金山", Snark = "文档软件顺手也想接管图片、云文档和右键。", Boost = 18, Patterns = new [] { "WPS Office", "WPS.", "WPS_", "WPS-", "Kingsoft", "金山", "Zhuhai Kingsoft", "kwps", "qingshell", "qingnse", "kdesktop", "kdocs", "photolaunch", "wpscloud", "WpsDrive", "WPS.PIC", "WPSPic", "WPSPhoto", "WPS图片", "QingNseContextMenu", "kwpsshellext", "qingshellext", "kdesktopshellext", "qkdesktopshellext", "WPSAI", "WPS AI", "KingsoftAI", "AiWPS", "WPS灵犀", "wpsLingxi", "lingxi", "旺仔", "Wangzai", "wpscenter", "wpsupdate", "WpsUpdateTask", "WPS Office Cloud Service", "wpscloudsvr", "ksomisc" }, BadComponents = new [] { "kwpsshellext", "qingshellext", "QingNseContextMenu", "kdesktopshellext", "qkdesktopshellext", "WPS.PIC", "WPSPic", "photolaunch.exe", "Wangzai", "wpscloudsvr" } },
            new VendorRule { Name = "百度 / 百度网盘", Snark = "网盘不只同步文件，还喜欢同步到右键菜单。", Boost = 18, Patterns = new [] { "Baidu", "百度", "BaiduNetdisk", "BaiduNetdiskUnite", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskDesktopSync", "BaiduNetdiskSync", "BaiduNetdiskUtility", "BaiduNetdiskService", "BaiduNetdiskHost", "BaiduYun", "BaiduYunDetect", "YunShell", "YunShellExt", "YunDetectService", "cloudpic", "百度网盘看图", "百度网盘同步", "北京度友" }, BadComponents = new [] { "YunShellExt", "YunShellExplorerCommand", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskUtility", "BaiduNetdiskService", "cloudpic.dll", "imageviewer" } },
            new VendorRule { Name = "夸克 / 夸克网盘", Snark = "网盘上传和 PDF 转换也来抢右键，至少别披成迅雷的马甲。", Boost = 18, Patterns = new [] { "QuarkCloudDrive", "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkNetdisk", "QuarkDisk", "QuarkPan", "QuarkPDF", "QuarkConvert", "QuarkPC", "quark.cn", "pan.quark.cn", "vt.quark.cn", "quark-pc", "external_rclick", "夸克", "夸克网盘", "夸克浏览器", "上传到夸克网盘", "夸克网盘上传" }, BadComponents = new [] { "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkPDF", "QuarkConvert", "quark-pc", "external_rclick", "上传到夸克网盘", "PDF转换", "图片转PDF", "万能转换" } },
            new VendorRule { Name = "搜狗", Snark = "输入法可以输入字，但没必要输入到开机项里。", Boost = 16, Patterns = new [] { "Sogou", "搜狗", "SogouInput", "SogouPY", "SogouExplorer", "SogouCloud", "SogouIme", "SogouImeBroker", "SogouImeMgr", "SogouFlash", "SogouTips", "SogouNews", "SogouPopup", "SogouSvc", "SGImeGuard", "SogouInputPop", "SogouAd", "SogouUpdate", "SogouComMgr", "PinyinUp" }, BadComponents = new [] { "SogouImeBroker", "SogouInput", "SogouExplorer", "SogouFlash", "SogouTips", "SogouAd", "SogouInputPop", "SogouPopup", "SogouNews", "SGImeGuard" } },
            new VendorRule { Name = "迅雷", Snark = "下载器最爱给自己安排开机打卡。", Boost = 20, Patterns = new [] { "Xunlei", "Thunder", "迅雷", "Thunder Network", "XLService", "XLServicePlatform", "ThunderPlatform", "ThunderAgent", "ThunderStart", "ThunderBrowser", "XunleiBHO", "XunleiDownload", "XunleiMedia", "Xunlei.XLB", "XLLiveUD", "XLGameBox", "TBCrash", "迅雷下载助手" }, BadComponents = new [] { "XLService", "XLServicePlatform", "ThunderPlatform", "Xunlei.XLB", "ThunderBrowser", "ThunderStart", "XunleiBHO" } },
            new VendorRule { Name = "钉钉", Snark = "办公协作可以，文件右键也要塞上传入口就过界了。", Boost = 14, Patterns = new [] { "DingTalk", "Dingtalk", "dingtalk", "DingDing", "钉钉", "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "DingTalkDrive", "DingTalkDocs", "DingTalkFile", "DingTalkOffice", "DingTalkLite", "AliDingTalk", "com.alibaba.dingtalk", "上传钉钉并打开", "上传到钉钉", "钉钉并打开", "钉盘" }, BadComponents = new [] { "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "上传钉钉并打开", "上传到钉钉", "DingTalkDrive" } },
            new VendorRule { Name = "腾讯系", Snark = "聊天归聊天，别顺手接管浏览器和启动项。", Boost = 12, Patterns = new [] { "Tencent", "腾讯", "QQBrowser", "QQPCMgr", "QQPCMGR", "QQProtect", "QQPCRTP", "QQRepair", "QQShellExt", "TXShell", "TIM.exe", "TIM\\", "WeChat", "微信", "企业微信", "WXWork", "TencentDocs", "腾讯文档", "QQLive", "QQMusic", "QBCore", "QBUpdate", "电脑管家" }, BadComponents = new [] { "QQPCMgr", "QQBrowser", "QQProtect", "QQPCRTP", "QQShellExt", "TXShell", "QBUpdate" } },
            new VendorRule { Name = "2345 系列", Snark = "名字像门牌号，行为像钉子户。", Boost = 25, Patterns = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345PicViewer", "2345Kantuwang", "2345Zip", "2345Safe", "2345Protect", "2345Svc", "2345MiniPage", "2345Browser", "2345看图王", "2345好压", "王牌" }, BadComponents = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345Zip", "2345Protect", "2345MiniPage" } },
            new VendorRule { Name = "猎豹 / 金山毒霸", Snark = "安全软件当然能安全，问题是别把自己藏成常驻钉子。", Boost = 18, Patterns = new [] { "Cheetah", "猎豹", "Liebao", "Kingsoft Internet Security", "金山毒霸", "KSafe", "KSafeSvc", "KWatch", "kismain", "kavsrv", "KSafeTray", "KMailMon", "KSoft" }, BadComponents = new [] { "KSafeSvc", "KWatch", "kavsrv", "KSafeTray", "Cheetah" } },
            new VendorRule { Name = "驱动/硬件检测工具", Snark = "修驱动可以，常驻当监工就过分了。", Boost = 18, Patterns = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "驱动精灵", "驱动人生", "MyDrivers", "DrvMgr", "DGDaemon", "DTLService", "LuDaShi", "鲁大师", "MasterLu", "LdsLite", "LdsSvc", "LdsDaemon", "ComputerZ", "HardwareProtect" }, BadComponents = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "LuDaShi", "MasterLu", "LdsSvc", "LdsDaemon" } },
            new VendorRule { Name = "Bandisoft 看图/压缩工具", Snark = "看图软件也要在右键菜单刷存在感。", Boost = 12, Patterns = new [] { "Bandisoft", "BandiView", "BandiView.exe", "Bandiview", "Honeyview", "HoneyView", "Bandizip", "BandiZip", "BandizipShellext", "BandizipShell", "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "Open with BandiView", "Browse with BandiView", "用 BandiView", "使用 BandiView" }, BadComponents = new [] { "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "BandizipShellext", "BandizipShell" } },
            new VendorRule { Name = "国产压缩/看图工具", Snark = "压缩包还没打开，右键先被挤爆了。", Boost = 12, Patterns = new [] { "KuaiZip", "快压", "Kuaizip", "HaoZip", "好压", "2345Zip", "360压缩", "360zip", "2345Pic", "2345看图王", "XnViewShell", "KanPic", "看图王", "极速看图", "JisuPic" }, BadComponents = new [] { "KuaiZip", "Kuaizip", "HaoZip", "2345Zip", "360zip", "2345Pic" } },
            new VendorRule { Name = "国产浏览器/导航", Snark = "浏览器自己跑就行，别把下载、主页和启动项全包了。", Boost = 16, Patterns = new [] { "SogouExplorer", "搜狗高速浏览器", "QQBrowser", "360se", "360Chrome", "2345Explorer", "2345Browser", "Liebao", "猎豹浏览器", "CheetahBrowser", "Maxthon", "傲游", "UCBrowser", "UCBrowser", "TheWorld", "世界之窗", "BaiduBrowser", "百度浏览器" }, BadComponents = new [] { "SogouExplorer", "QQBrowser", "2345Explorer", "CheetahBrowser", "UCService", "BaiduBrowser" } },
            new VendorRule { Name = "Flash 中国特供组件", Snark = "Flash 都退役了，助手还想在后台上班。", Boost = 22, Patterns = new [] { "FlashHelperService", "Flash Center", "FlashCenter", "Flash大厅", "FlashHelper", "FlashRepair", "FlashService", "flash.cn" }, BadComponents = new [] { "FlashHelperService", "FlashCenter", "FlashHelper" } },
            new VendorRule { Name = "手机助手/设备助手", Snark = "连一次手机，后台服务倒是记住一辈子。", Boost = 12, Patterns = new [] { "i4Tools", "爱思助手", "Aisi", "PP助手", "PPAssistant", "91助手", "91Assistant", "Wandoujia", "豌豆荚", "BaiduMobile", "TencentMobileManager", "应用宝", "HiSuite", "华为手机助手", "MiPhoneAssistant", "小米助手" }, BadComponents = new [] { "i4Tools", "PPAssistant", "91Assistant", "Wandoujia", "TencentMobileManager" } },
            new VendorRule { Name = "国产影音/游戏大厅", Snark = "看个视频玩个游戏，不需要抢文件关联和开机席位。", Boost = 10, Patterns = new [] { "iQIYI", "爱奇艺", "Qiyi", "Youku", "优酷", "Kugou", "酷狗", "Kuwo", "酷我", "PPTV", "暴风", "Baofeng", "QQLive", "TencentVideo", "腾讯视频", "XunleiMedia", "Bilibili", "芒果TV", "MangoTV", "WeGame", "SteamChina" }, BadComponents = new [] { "iQIYI", "Qiyi", "Youku", "Kugou", "Kuwo", "PPTV", "Baofeng", "QQLive", "TencentVideo" } },
            new VendorRule { Name = "PDF/办公捆绑工具", Snark = "读个 PDF，也别顺手接管全系统打开方式。", Boost = 10, Patterns = new [] { "JisuPDF", "极速PDF", "SwiftPDF", "迅捷PDF", "Foxit", "福昕", "CAJViewer", "PDFReader", "PDFSuite", "PDFMaster", "嗨格式", "HiFormat" }, BadComponents = new [] { "JisuPDF", "SwiftPDF", "PDFMaster", "HiFormat" } },
            new VendorRule { Name = "预装管家/厂商助手", Snark = "出厂自带不等于可以偷偷常驻。", Boost = 8, Patterns = new [] { "LenovoUtility", "LenovoVantage", "联想电脑管家", "LenovoPcManager", "Huawei PC Manager", "华为电脑管家", "HonorPCManager", "荣耀电脑管家", "MiService", "小米电脑管家", "MyASUS", "华硕电脑管家", "AcerCare", "Dell SupportAssist" }, BadComponents = new [] { "LenovoPcManager", "Huawei PC Manager", "HonorPCManager", "MiService", "SupportAssist" } },
            new VendorRule { Name = "弹窗广告/推广组件", Snark = "关掉没一会儿又弹，这类小广告最会装死。", Boost = 22, Patterns = new [] { "SogouNews", "SogouPopup", "SogouTips", "SogouAd", "SogouInputPop", "2345MiniPage", "MiniNews", "HotNews", "NewsPop", "PopNews", "PopWnd", "AdPop", "AdService", "AdPush", "WpsNotify", "KNotify", "BaiduTips", "BaiduNews", "QQBrowserMini", "KugouTips", "KuwoNews", "QiyiNews", "YoukuNews", "LuDaShiNews", "MasterLuMini", "DriverGeniusNews", "KuaiZipNews", "HaoZipMiniPage", "今日热点", "每日热点", "热点资讯", "迷你页", "推荐弹窗", "广告弹窗" }, BadComponents = new [] { "SogouNews", "SogouPopup", "2345MiniPage", "AdPop", "AdService", "WpsNotify", "BaiduTips", "LuDaShiNews", "KuaiZipNews" } },
            new VendorRule { Name = "守护/自动恢复组件", Snark = "你关它一次，它守护进程能把自己续上三回。", Boost = 20, Patterns = new [] { "QHWatchdog", "QHProtected", "QHActiveDefense", "SGImeGuard", "SogouImeBroker", "XLServicePlatform", "ThunderPlatform", "BaiduYunDetect", "YunDetectService", "BaiduNetdiskUtility", "QQProtect", "QQPCRTP", "2345Protect", "2345Svc", "KSafeSvc", "KWatch", "LdsDaemon", "LdsSvc", "FlashHelperService", "FlashCenter", "DriverGeniusDaemon", "DTLService", "LuDaShiDaemon" }, BadComponents = new [] { "QHWatchdog", "QHProtected", "SGImeGuard", "XLServicePlatform", "BaiduYunDetect", "QQProtect", "2345Protect", "KSafeSvc", "LdsDaemon", "FlashHelperService" } }
        };

        private sealed class CandidateScore
        {
            public VendorRule Rule;
            public int Score;
            public bool Strong;
            public readonly HashSet<string> Sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Reasons = new List<string>();
        }

        private sealed class FileIdentity
        {
            public string Path;
            public string Company;
            public string Product;
            public string Description;
            public string Signer;
            public bool SignatureValid;
        }

        private sealed class MsiIdentity
        {
            public string ProductName;
            public string Publisher;
            public string InstallLocation;
            public string LocalPackage;
        }

        private sealed class InstalledOwner
        {
            public string Root;
            public string Publisher;
            public string ProductName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }

        private static readonly object IdentityCacheGate = new object();
        private static readonly Dictionary<string, FileIdentity> FileIdentityCache = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MsiIdentity> MsiIdentityCache = new Dictionary<string, MsiIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<InstalledOwner> InstalledOwners = new List<InstalledOwner>();
        private static bool InstalledOwnersLoaded;
        private static readonly HashSet<string> SystemHosts = new HashSet<string>(new string[]
        {
            "msiexec.exe", "rundll32.exe", "regsvr32.exe", "svchost.exe", "explorer.exe", "cmd.exe",
            "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref int length);

        public static VendorIdentityResult ResolveIdentity(VendorEvidence evidence)
        {
            if (evidence == null) evidence = new VendorEvidence();
            EnrichEvidence(evidence);
            List<CandidateScore> candidates = new List<CandidateScore>();
            foreach (VendorRule rule in Vendors)
            {
                CandidateScore candidate = ScoreRule(rule, evidence);
                if (candidate.Score > 0) candidates.Add(candidate);
            }
            candidates = candidates.OrderByDescending(delegate(CandidateScore item) { return item.Strong; })
                .ThenByDescending(delegate(CandidateScore item) { return item.Score; }).ToList();
            if (candidates.Count == 0)
            {
                return UnknownIdentity(false, "没有可信厂商证据");
            }

            CandidateScore best = candidates[0];
            bool confirmed = best.Strong || (best.Score >= 70 && best.Sources.Count >= 2);
            CandidateScore conflict = candidates.Skip(1).FirstOrDefault(delegate(CandidateScore item)
            {
                bool otherConfirmed = item.Strong || (item.Score >= 70 && item.Sources.Count >= 2);
                if (!otherConfirmed) return false;
                if (best.Strong && item.Strong) return true;
                return Math.Abs(best.Score - item.Score) < 25;
            });
            if (conflict != null)
            {
                return UnknownIdentity(true, "强证据冲突：" + best.Rule.Name + " / " + conflict.Rule.Name);
            }
            if (!confirmed)
            {
                return UnknownIdentity(false, "证据不足：" + string.Join("，", best.Reasons.Take(3).ToArray()));
            }
            return new VendorIdentityResult
            {
                Vendor = best.Rule.Name,
                Confidence = Math.Min(100, best.Strong ? Math.Max(95, best.Score) : best.Score),
                Confirmed = true,
                Conflicted = false,
                EvidenceSummary = string.Join("，", best.Reasons.Distinct().Take(5).ToArray())
            };
        }

        public static bool HasBadComponent(VendorEvidence evidence, VendorIdentityResult identity)
        {
            if (evidence == null || identity == null || !identity.Confirmed) return false;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return false;
            IEnumerable<string> values = evidence.HumanTexts.Concat(evidence.ProductNames).Concat(evidence.TechnicalIdentifiers)
                .Concat(evidence.FilePaths.Select(delegate(string value) { return SafePathFileName(value); }));
            foreach (string value in values)
            {
                foreach (string pattern in rule.BadComponents)
                {
                    if (SafePatternMatch(value, pattern, true)) return true;
                }
            }
            return false;
        }

        public static int VendorBoost(VendorIdentityResult identity, bool badComponent)
        {
            if (identity == null || !identity.Confirmed) return 0;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return 0;
            return 35 + rule.Boost + (badComponent ? 30 : 0);
        }

        private static CandidateScore ScoreRule(VendorRule rule, VendorEvidence evidence)
        {
            CandidateScore candidate = new CandidateScore { Rule = rule };
            ScoreValues(candidate, rule, evidence.Publishers, "Publisher", 60, false, false);
            ScoreValues(candidate, rule, evidence.ProductNames, "Product", 45, false, false);
            ScoreValues(candidate, rule, evidence.HumanTexts, "Human", 40, false, false);
            ScoreValues(candidate, rule, evidence.TechnicalIdentifiers, "Technical", 40, true, false);
            ScoreValues(candidate, rule, evidence.FilePaths, "Path", 30, true, false);
            foreach (string path in evidence.FilePaths)
            {
                FileIdentity file = GetFileIdentity(path);
                if (file == null) continue;
                if (file.SignatureValid) ScoreValues(candidate, rule, new string[] { file.Signer }, "Signature:" + file.Path, 100, false, true);
                ScoreValues(candidate, rule, new string[] { file.Company }, "Company:" + file.Path, 60, false, false);
                ScoreValues(candidate, rule, new string[] { file.Product, file.Description }, "FileProduct:" + file.Path, 45, false, false);
            }
            return candidate;
        }

        private static void ScoreValues(CandidateScore candidate, VendorRule rule, IEnumerable<string> values, string source, int score, bool technical, bool strong)
        {
            if (values == null) return;
            int index = 0;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) { index++; continue; }
                string pattern = MatchingPattern(rule, value, technical);
                if (!string.IsNullOrEmpty(pattern))
                {
                    string sourceKey = source + ":" + index;
                    if (candidate.Sources.Add(sourceKey)) candidate.Score += score;
                    candidate.Strong = candidate.Strong || strong;
                    candidate.Reasons.Add(source.Split(':')[0] + "=" + pattern);
                }
                index++;
            }
        }

        private static string MatchingPattern(VendorRule rule, string value, bool technical)
        {
            foreach (string pattern in rule.Patterns)
            {
                if (technical && pattern.Length < 5 && pattern.All(delegate(char c) { return c < 128; })) continue;
                bool distinctive = rule.BadComponents.Any(delegate(string item) { return item.Equals(pattern, StringComparison.OrdinalIgnoreCase); });
                if (SafePatternMatch(value, pattern, technical && !distinctive)) return pattern;
            }
            return null;
        }

        private static bool SafePatternMatch(string text, string pattern, bool technical)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(pattern, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool alphaNumeric = pattern.All(delegate(char c) { return char.IsLetterOrDigit(c); });
                bool boundaryRequired = alphaNumeric && (technical || pattern.Length <= 4 || pattern.All(char.IsDigit));
                if (!boundaryRequired) return true;
                int end = index + pattern.Length;
                bool leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool rightBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (leftBoundary && rightBoundary) return true;
                start = index + 1;
            }
        }

        private static void EnrichEvidence(VendorEvidence evidence)
        {
            foreach (string command in evidence.Commands.ToArray())
            {
                string file = ExtractTargetFile(command);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
                string productCode = ExtractProductCode(command);
                if (!string.IsNullOrEmpty(productCode)) evidence.AddMsi(productCode);
            }
            foreach (string value in evidence.MsiProductCodes.ToArray())
            {
                string productCode = ExtractProductCode(value);
                if (string.IsNullOrEmpty(productCode)) continue;
                MsiIdentity msi = GetMsiIdentity(productCode);
                if (msi == null) continue;
                evidence.AddPublisher(msi.Publisher).AddProduct(msi.ProductName).AddFile(msi.LocalPackage);
                if (!string.IsNullOrWhiteSpace(msi.InstallLocation)) evidence.AddFile(msi.InstallLocation);
            }
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string file = NormalizeCandidateFile(value);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
            }
            EnrichInstalledOwnership(evidence);
        }

        private static void EnrichInstalledOwnership(VendorEvidence evidence)
        {
            EnsureInstalledOwners();
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string normalized;
                try { normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
                catch { continue; }
                List<InstalledOwner> owners;
                lock (IdentityCacheGate) owners = InstalledOwners.ToList();
                foreach (InstalledOwner owner in owners)
                {
                    if (!normalized.StartsWith(owner.Root, StringComparison.OrdinalIgnoreCase)) continue;
                    evidence.AddPublisher(owner.Publisher).AddProduct(owner.ProductName);
                }
            }
        }

        private static void EnsureInstalledOwners()
        {
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
            }
            List<InstalledOwner> loaded = new List<InstalledOwner>();
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (root == null) continue;
                            foreach (string childName in root.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey child = root.OpenSubKey(childName))
                                    {
                                        if (child == null) continue;
                                        string product = Convert.ToString(child.GetValue("DisplayName", ""));
                                        string publisher = Convert.ToString(child.GetValue("Publisher", ""));
                                        string installLocation = Convert.ToString(child.GetValue("InstallLocation", ""));
                                        string displayIcon = Convert.ToString(child.GetValue("DisplayIcon", ""));
                                        string ownerRoot = NormalizeInstallRoot(installLocation, displayIcon);
                                        if (string.IsNullOrEmpty(ownerRoot) || (string.IsNullOrWhiteSpace(product) && string.IsNullOrWhiteSpace(publisher))) continue;
                                        loaded.Add(new InstalledOwner { Root = ownerRoot, Publisher = publisher, ProductName = product });
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
                foreach (InstalledOwner owner in loaded.OrderByDescending(delegate(InstalledOwner item) { return item.Root.Length; }))
                {
                    if (!InstalledOwners.Any(delegate(InstalledOwner item) { return item.Root.Equals(owner.Root, StringComparison.OrdinalIgnoreCase) && item.ProductName.Equals(owner.ProductName, StringComparison.OrdinalIgnoreCase); }))
                        InstalledOwners.Add(owner);
                }
                InstalledOwnersLoaded = true;
            }
        }

        private static string NormalizeInstallRoot(string installLocation, string displayIcon)
        {
            string value = installLocation;
            if (string.IsNullOrWhiteSpace(value))
            {
                string icon = NormalizeCandidateFile(displayIcon);
                if (!string.IsNullOrEmpty(icon)) value = Path.GetDirectoryName(icon);
            }
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
            catch { return string.Empty; }
        }

        private static string ExtractTargetFile(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            string first = ExtractFirstPath(expanded);
            string host = SafePathFileName(first);
            if (!SystemHosts.Contains(host)) return first;
            if (host.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) || host.Equals("regsvr32.exe", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = expanded.Substring(Math.Min(expanded.Length, expanded.IndexOf(first, StringComparison.OrdinalIgnoreCase) + first.Length)).Trim().TrimStart(',');
                string target = ExtractFirstPath(remainder);
                int comma = target.IndexOf(',');
                return comma > 0 ? target.Substring(0, comma) : target;
            }
            return string.Empty;
        }

        private static string ExtractFirstPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = value.IndexOf('\"', 1);
                if (close > 1) return value.Substring(1, close - 1);
            }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return value.Substring(0, exe + 4).Trim().Trim('\"');
            int dll = value.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            if (dll >= 0) return value.Substring(0, dll + 4).Trim().Trim('\"');
            int comma = value.IndexOf(',');
            if (comma > 0) return value.Substring(0, comma).Trim().Trim('\"');
            return value.Trim('\"');
        }

        private static string NormalizeCandidateFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string path = ExtractFirstPath(Environment.ExpandEnvironmentVariables(value));
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                if (Directory.Exists(path)) return string.Empty;
                return Path.GetFullPath(path);
            }
            catch { return string.Empty; }
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static string ExtractProductCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int start = value.IndexOf('{');
            while (start >= 0)
            {
                int end = value.IndexOf('}', start + 1);
                if (end < 0) return string.Empty;
                string candidate = value.Substring(start, end - start + 1);
                Guid parsed;
                if (Guid.TryParse(candidate, out parsed)) return parsed.ToString("B").ToUpperInvariant();
                start = value.IndexOf('{', end + 1);
            }
            Guid direct;
            return Guid.TryParse(value.Trim(), out direct) ? direct.ToString("B").ToUpperInvariant() : string.Empty;
        }

        private static FileIdentity GetFileIdentity(string value)
        {
            string path = NormalizeCandidateFile(value);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            lock (IdentityCacheGate)
            {
                FileIdentity cached;
                if (FileIdentityCache.TryGetValue(path, out cached)) return cached;
            }
            FileIdentity identity = new FileIdentity { Path = path };
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                identity.Company = version.CompanyName;
                identity.Product = version.ProductName;
                identity.Description = version.FileDescription;
            }
            catch { }
            identity.SignatureValid = IsTrustedFile(path);
            if (identity.SignatureValid)
            {
                try
                {
                    using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    {
                        identity.Signer = certificate.Subject;
                    }
                }
                catch { identity.SignatureValid = false; }
            }
            lock (IdentityCacheGate) FileIdentityCache[path] = identity;
            return identity;
        }

        private static bool IsTrustedFile(string path)
        {
            IntPtr filePointer = IntPtr.Zero;
            IntPtr dataPointer = IntPtr.Zero;
            try
            {
                WinTrustFileInfo file = new WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    FilePath = path,
                    FileHandle = IntPtr.Zero,
                    KnownSubject = IntPtr.Zero
                };
                filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(file, filePointer, false);
                WinTrustData data = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    RevocationChecks = 0,
                    UnionChoice = 1,
                    FileInfo = filePointer,
                    StateAction = 0,
                    ProviderFlags = 0x00001000,
                    UiContext = 0
                };
                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, dataPointer, false);
                return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, dataPointer) == 0;
            }
            catch { return false; }
            finally
            {
                if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
                if (filePointer != IntPtr.Zero) Marshal.FreeHGlobal(filePointer);
            }
        }

        private static MsiIdentity GetMsiIdentity(string productCode)
        {
            lock (IdentityCacheGate)
            {
                MsiIdentity cached;
                if (MsiIdentityCache.TryGetValue(productCode, out cached)) return cached;
            }
            MsiIdentity identity = new MsiIdentity
            {
                ProductName = MsiProperty(productCode, "ProductName"),
                Publisher = MsiProperty(productCode, "Publisher"),
                InstallLocation = MsiProperty(productCode, "InstallLocation"),
                LocalPackage = MsiProperty(productCode, "LocalPackage")
            };
            lock (IdentityCacheGate) MsiIdentityCache[productCode] = identity;
            return identity;
        }

        private static string MsiProperty(string productCode, string property)
        {
            try
            {
                int length = 0;
                uint first = MsiGetProductInfo(productCode, property, null, ref length);
                if (first != 0 && first != 234) return string.Empty;
                StringBuilder value = new StringBuilder(length + 1);
                uint result = MsiGetProductInfo(productCode, property, value, ref length);
                return result == 0 ? value.ToString() : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static VendorIdentityResult UnknownIdentity(bool conflicted, string reason)
        {
            return new VendorIdentityResult { Vendor = "未知第三方", Confidence = 0, Confirmed = false, Conflicted = conflicted, EvidenceSummary = reason };
        }

        public static List<string> RunIdentitySelfTests()
        {
            List<string> failures = new List<string>();
            AssertUnknown(failures, "Corel GUID 不得命中 2345", new VendorEvidence()
                .AddHuman("CorelDRAW Graphics Suite 2021 - IPM Content BR (x64)")
                .AddPublisher("Corel Corporation").AddMsi("{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "裸 2345 不能确认厂商", new VendorEvidence().AddHuman("2345").AddTechnical("A9F234554C2C"));
            AssertUnknown(failures, "系统 MSI 宿主不能成为厂商证据", new VendorEvidence()
                .AddCommand(@"C:\Windows\System32\msiexec.exe /I{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque("{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "Thunderbird 不能命中迅雷", new VendorEvidence()
                .AddHuman("Mozilla Thunderbird").AddTechnical("Thunderbird").AddFile(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe"));
            AssertUnknown(failures, "普通 TBS/XMP/KAV/CAJ 缩写不能确认厂商", new VendorEvidence()
                .AddHuman("TBS XMP KAV CAJ").AddTechnical("TBS_XMP_KAV_CAJ"));

            VendorIdentityResult sogou = ResolveIdentity(new VendorEvidence().AddHuman("搜狗输入法").AddPublisher("Sogou.com").AddProduct("Sogou Input Method"));
            if (!sogou.Confirmed || sogou.Vendor != "搜狗") failures.Add("明确 Publisher+产品名未识别为搜狗");

            VendorIdentityResult conflict = ResolveIdentity(new VendorEvidence()
                .AddPublisher("Sogou.com", "Thunder Network Technologies")
                .AddProduct("Sogou Input Method", "Xunlei Thunder Download"));
            if (!conflict.Conflicted || conflict.Confirmed) failures.Add("相互冲突的强组合证据未被阻断");

            foreach (VendorRule rule in Vendors)
            {
                foreach (string pattern in rule.Patterns)
                {
                    VendorIdentityResult opaque = ResolveIdentity(new VendorEvidence()
                        .AddOpaque("GUID-{A9F" + pattern + "55C2C}", @"HKLM\Software\Classes\" + pattern));
                    if (opaque.Confirmed) failures.Add("不透明字段误命中：" + rule.Name + " / " + pattern);

                    VendorIdentityResult pathOnly = ResolveIdentity(new VendorEvidence()
                        .AddFile(@"C:\Unrelated\A9F" + pattern + @"55C2C\tool.exe"));
                    if (pathOnly.Confirmed) failures.Add("单一路径片段误命中：" + rule.Name + " / " + pattern);
                }
            }
            return failures;
        }

        private static void AssertUnknown(List<string> failures, string name, VendorEvidence evidence)
        {
            VendorIdentityResult result = ResolveIdentity(evidence);
            if (result.Confirmed || result.Vendor != "未知第三方") failures.Add(name + "：实际为 " + result.Vendor + "，" + result.EvidenceSummary);
        }

        public static string ResolveVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Vendor;
        }

        public static int VendorBoost(string text)
        {
            VendorIdentityResult identity = ResolveIdentity(new VendorEvidence().AddHuman(text));
            return VendorBoost(identity, false);
        }

        public static bool IsKnownVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Confirmed;
        }

        public static bool HasBadComponent(string text)
        {
            VendorEvidence evidence = new VendorEvidence().AddHuman(text).AddTechnical(text);
            VendorIdentityResult identity = ResolveIdentity(evidence);
            return HasBadComponent(evidence, identity);
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
            @"Software\Classes\lnkfile\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\image\shell",
            @"Software\Classes\SystemFileAssociations\image\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\video\shell",
            @"Software\Classes\SystemFileAssociations\video\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\audio\shell",
            @"Software\Classes\SystemFileAssociations\audio\shellex\ContextMenuHandlers",
            @"Software\Classes\SystemFileAssociations\text\shell",
            @"Software\Classes\SystemFileAssociations\text\shellex\ContextMenuHandlers",
            @"Software\Classes\CompressedFolder\shell",
            @"Software\Classes\CompressedFolder\shellex\ContextMenuHandlers"
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
            @"Software\Mozilla\NativeMessagingHosts",
            @"Software\Policies\Google\Chrome\ExtensionInstallForcelist",
            @"Software\Policies\Microsoft\Edge\ExtensionInstallForcelist",
            @"Software\Policies\Google\Chrome\ExtensionSettings",
            @"Software\Policies\Microsoft\Edge\ExtensionSettings"
        };

        private static readonly string[] InstalledRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        private static readonly string[] ExplorerNamespaceRoots = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\NetworkNeighborhood\NameSpace"
        };

        private static readonly string[] ExplorerNamespaceClsidRoots = new string[]
        {
            @"Software\Classes\CLSID"
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
            scanners.Add(delegate { AddRange(all, gate, sink, "此电脑入口", ScanExplorerNamespaces()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "开机启动", ScanStartupRegistry()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "启动文件夹", ScanStartupFolders()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "后台服务", ScanServices()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "浏览器插件", ScanBrowserExtensions()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "文件关联", ScanFileAssociations()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "计划任务", ScanScheduledTasks()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "隐藏卸载入口", ScanHiddenInstalledComponents()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "正在运行的弹窗/守护", ScanRunningAdAndGuardProcesses()); });

            Parallel.Invoke(new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(2, Math.Min(4, Environment.ProcessorCount))
            }, scanners.ToArray());

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
                            string commandStateHandler = ReadString(child, "CommandStateHandler");
                            string icon = ReadString(child, "Icon");
                            string appliesTo = ReadString(child, "AppliesTo");
                            string command = ReadDefault(target, "command");
                            string clsidText = ResolveClsidRegistration(childName, display, explorerHandler, commandStateHandler);
                            string title = FriendlyContextMenuTitle(target.SubKey, childName, display, mui, explorerHandler, commandStateHandler, clsidText);
                            string text = Join(title, childName, display, mui, explorerHandler, commandStateHandler, command, icon, appliesTo, clsidText, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(title, display, mui)
                                .AddTechnical(childName, explorerHandler, commandStateHandler, clsidText).AddCommand(command, clsidText)
                                .AddFile(icon).AddOpaque(appliesTo, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                            list.Add(NewFinding("右键菜单", title, DescribeContextMenu(target.SubKey, title), target, text, 18, identity, badComponent));
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
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddCommand(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyStartupTitle(text, valueName, value, identity.Vendor);
                        list.Add(NewFinding("开机启动", title, "开机后会自动启动：" + title, target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
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
                    string shortcut = ResolveShortcutText(file);
                    string text = Join(file, shortcut);
                    VendorEvidence evidence = new VendorEvidence().AddHuman(Path.GetFileNameWithoutExtension(file)).AddCommand(shortcut).AddOpaque(file);
                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                    if (!identity.Confirmed) continue;
                    ActionTarget target = new ActionTarget { Kind = "MoveFileToBackup", FilePath = file };
                    list.Add(NewFinding("启动文件夹", Path.GetFileName(file), "开机后会从启动文件夹拉起：" + Join(Path.GetFileName(file), shortcut), target, text, 28, identity, RuleCatalog.HasBadComponent(evidence, identity)));
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
                        VendorEvidence evidence = new VendorEvidence().AddHuman(valueName).AddTechnical(valueName).AddCommand(value).AddFile(value).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryValue";
                        target.ValueName = valueName;
                        string title = FriendlyBrowserTitle(text, valueName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        string childDefault;
                        using (RegistryKey child = RegistryHelper.OpenSubKey(target, false))
                        {
                            childDefault = ReadString(child, "");
                        }
                        string text = Join(childName, childDefault, root.SubKey);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(childName).AddTechnical(childName).AddCommand(childDefault).AddFile(childDefault).AddOpaque(root.SubKey);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        string title = FriendlyBrowserTitle(text, childName, identity.Vendor);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanExplorerNamespaces()
        {
            List<Finding> list = new List<Finding>();
            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceRoots, true, true))
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
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string targetFolder = ReadString(child, "TargetFolderPath");
                            string clsidText = ResolveClsidRegistration(childName, display, localized, itemName);
                            string text = Join(childName, display, localized, itemName, targetFolder, ReadString(child, "CodexMarker"), clsidText, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName).AddTechnical(clsidText)
                                .AddCommand(clsidText).AddFile(targetFolder).AddOpaque(childName, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            string title = FriendlyExplorerNamespaceTitle(target.SubKey, childName, display, localized, itemName, clsidText);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会在“此电脑”、资源管理器导航栏或网络位置里显示入口：" + title + "。清理只移除入口注册表，不卸载主程序。", target, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }

            foreach (ActionTarget root in RegistryTargets(ExplorerNamespaceClsidRoots, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(root, false))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget clsidTarget = CopyTarget(root);
                        clsidTarget.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = RegistryHelper.OpenSubKey(clsidTarget, false))
                        {
                            string pinned = ReadString(child, "System.IsPinnedToNameSpaceTree");
                            if (!IsTruthy(pinned)) continue;
                            string display = ReadString(child, "");
                            string localized = ReadString(child, "LocalizedString");
                            string itemName = ReadString(child, "System.ItemNameDisplay");
                            string infoTip = ReadString(child, "InfoTip");
                            string icon = ReadChildDefault(clsidTarget, "DefaultIcon");
                            string server = Join(ReadChildDefault(clsidTarget, "InprocServer32"), ReadChildDefault(clsidTarget, "LocalServer32"));
                            string targetFolder = ReadChildValue(clsidTarget, @"Instance\InitPropertyBag", "TargetFolderPath");
                            string text = Join(childName, display, localized, itemName, infoTip, pinned, icon, server, targetFolder, ReadString(child, "CodexMarker"), clsidTarget.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddHuman(display, localized, itemName, infoTip).AddTechnical(server)
                                .AddCommand(server).AddFile(icon, targetFolder).AddOpaque(childName, pinned, clsidTarget.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            if (!identity.Confirmed) continue;
                            ActionTarget valueTarget = CopyTarget(clsidTarget);
                            valueTarget.Kind = "DeleteRegistryValue";
                            valueTarget.ValueName = "System.IsPinnedToNameSpaceTree";
                            string title = FriendlyExplorerNamespaceTitle(clsidTarget.SubKey, childName, display, localized, itemName, text);
                            list.Add(NewFinding("此电脑/资源管理器入口", title, "会把入口固定到资源管理器导航栏或“此电脑”附近：" + title + "。清理只取消固定入口，不卸载主程序。", valueTarget, text, 18, identity, RuleCatalog.HasBadComponent(evidence, identity)));
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanHiddenInstalledComponents()
        {
            List<Finding> list = new List<Finding>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActionTarget root in RegistryTargets(InstalledRoots, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(root, false))
                {
                    if (key == null) continue;
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.SubKey = root.SubKey + "\\" + childName;
                        using (RegistryKey child = RegistryHelper.OpenSubKey(target, false))
                        {
                            if (child == null) continue;
                            string display = ReadString(child, "DisplayName");
                            string publisher = ReadString(child, "Publisher");
                            string installLocation = ReadString(child, "InstallLocation");
                            string displayIcon = ReadString(child, "DisplayIcon");
                            string uninstall = ReadString(child, "UninstallString");
                            string quietUninstall = ReadString(child, "QuietUninstallString");
                            string systemComponent = ReadString(child, "SystemComponent");
                            string noRemove = ReadString(child, "NoRemove");
                            string parentKey = ReadString(child, "ParentKeyName");
                            string releaseType = ReadString(child, "ReleaseType");
                            string text = Join(childName, display, publisher, installLocation, displayIcon, uninstall, quietUninstall, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorEvidence evidence = new VendorEvidence().AddPublisher(publisher).AddProduct(display)
                                .AddFile(installLocation, displayIcon).AddCommand(uninstall, quietUninstall).AddMsi(childName, uninstall, quietUninstall)
                                .AddOpaque(childName, systemComponent, noRemove, parentKey, releaseType, target.SubKey);
                            VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                            bool hidden = IsTruthy(systemComponent) ||
                                IsTruthy(noRemove) ||
                                string.IsNullOrWhiteSpace(display) ||
                                string.IsNullOrWhiteSpace(uninstall) ||
                                !string.IsNullOrWhiteSpace(parentKey);
                            string behaviorText = Join(display, publisher, SafePathFileName(installLocation), SafePathFileName(displayIcon));
                            bool adOrGuard = LooksLikeAdOrGuard(behaviorText);
                            bool badComponent = RuleCatalog.HasBadComponent(evidence, identity);
                            bool suspiciousComponent = adOrGuard || badComponent;
                            if (!suspiciousComponent) continue;
                            string name = string.IsNullOrWhiteSpace(display) ? childName : display;
                            string dedupeKey = Join(name, uninstall, installLocation);
                            if (!seen.Add(dedupeKey)) continue;
                            string reason = HiddenInstallReason(display, uninstall, systemComponent, noRemove, parentKey, hidden, adOrGuard, badComponent);
                            if (identity.Confirmed && !identity.Conflicted && !string.IsNullOrWhiteSpace(uninstall))
                            {
                                target.Kind = "InvokeUninstaller";
                                target.UninstallCommand = uninstall;
                                target.FilePath = installLocation;
                                Finding finding = NewFinding("疑似捆绑/弹窗组件", name, "疑似捆绑、弹窗、守护或卸载入口异常：" + reason + "。工具只负责弹出它自己的卸载器，是否卸载由用户在卸载器里确认。", target, text, 16, identity, badComponent);
                                finding.Risk = badComponent || adOrGuard ? "中" : "低";
                                list.Add(finding);
                            }
                            else
                            {
                                target.Kind = "ReportOnly";
                                string vendorNote = identity.Conflicted ? "厂商强证据冲突，" : (!identity.Confirmed ? "厂商身份无法可靠确认，" : string.Empty);
                                Finding finding = NewFinding("疑似捆绑组件/卸载入口异常", name, vendorNote + "检测到独立异常行为：" + reason + "。只提示，不一键卸载。", target, text, 5, identity, badComponent);
                                finding.Risk = "低";
                                list.Add(finding);
                            }
                        }
                    }
                }
            }
            return list;
        }

        private List<Finding> ScanRunningAdAndGuardProcesses()
        {
            List<Finding> list = new List<Finding>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId,Name,ExecutablePath,CommandLine FROM Win32_Process"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string pid = Convert.ToString(process["ProcessId"]);
                        string name = Convert.ToString(process["Name"]);
                        string path = Convert.ToString(process["ExecutablePath"]);
                        string command = Convert.ToString(process["CommandLine"]);
                        string identity = Join(name, path);
                        string text = Join(pid, name, path, command);
                        if (!LooksLikeAdOrGuard(identity)) continue;
                        VendorEvidence evidence = new VendorEvidence().AddHuman(name).AddTechnical(name).AddFile(path).AddCommand(command).AddOpaque(pid);
                        VendorIdentityResult vendorIdentity = RuleCatalog.ResolveIdentity(evidence);
                        ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = Join(name, path, "PID=" + pid) };
                        Finding finding = NewFinding("正在运行/疑似弹窗守护", name, "后台正在运行，像是弹窗、推广、守护或自动恢复组件：" + Join(name, path), target, text, 12, vendorIdentity, RuleCatalog.HasBadComponent(evidence, vendorIdentity));
                        finding.Risk = "低";
                        list.Add(finding);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("扫描运行进程失败", ex);
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
                                VendorEvidence evidence = new VendorEvidence().AddTechnical(defaultProgId).AddCommand(command).AddOpaque(ext, classTarget.SubKey);
                                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                if (classKey != null && identity.Confirmed)
                                {
                                    classTarget.Kind = "ReportOnly";
                                    string title = ext + " 默认打开：" + FriendlyHandler(defaultProgId);
                                    list.Add(NewFinding("文件关联/默认打开程序", title, "双击/打开 " + ext + " 现在会交给：" + FriendlyHandler(defaultProgId) + "。这类属于主打开方式，只提示，不一键改。", classTarget, text, 8, identity, RuleCatalog.HasBadComponent(evidence, identity)));
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
                                    ActionTarget progTarget = CopyTarget(extTarget);
                                    progTarget.SubKey = @"Software\Classes\" + valueName;
                                    string command = ReadDefault(progTarget, @"shell\open\command");
                                    string text = Join(ext, valueName, command, subTarget.SubKey);
                                    VendorEvidence evidence = new VendorEvidence().AddTechnical(valueName).AddCommand(command).AddOpaque(ext, subTarget.SubKey);
                                    VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                                    if (!identity.Confirmed) continue;
                                    ActionTarget valueTarget = CopyTarget(subTarget);
                                    valueTarget.Kind = "DeleteRegistryValue";
                                    valueTarget.ValueName = valueName;
                                    string title = ext + " 打开方式：" + FriendlyHandler(valueName);
                                    list.Add(NewFinding("文件关联/打开方式", title, "右键“打开方式”里会出现：" + FriendlyHandler(valueName) + "（影响 " + ext + " 文件）", valueTarget, text, 22, identity, RuleCatalog.HasBadComponent(evidence, identity)));
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
                        if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsWindowsNativeService(name, display, path, desc)) continue;
                        string text = Join(name, display, path, desc, mode);
                        VendorEvidence evidence = new VendorEvidence().AddHuman(display, desc).AddTechnical(name).AddCommand(path).AddOpaque(mode);
                        VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                        if (!identity.Confirmed) continue;
                        ActionTarget target = new ActionTarget { Kind = "DisableService", ServiceName = name };
                        string title = FriendlyServiceTitle(text, name, display, identity.Vendor);
                        Finding finding = NewFinding("后台服务", title, "后台服务会常驻或被系统拉起：" + title + "。原服务名：" + name, target, text, 42, identity, RuleCatalog.HasBadComponent(evidence, identity));
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

        private static bool IsWindowsNativeService(string name, string display, string path, string desc)
        {
            string lowerPath = (Environment.ExpandEnvironmentVariables(path ?? string.Empty)).Trim().Trim('"').ToLowerInvariant();
            if (lowerPath.IndexOf(@"\windows\system32\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.IndexOf(@"\windows\syswow64\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (lowerPath.StartsWith("svchost.exe", StringComparison.OrdinalIgnoreCase)) return true;

            string text = Join(name, display, desc).ToLowerInvariant();
            bool windowsName = text.IndexOf("windows ") >= 0 || text.IndexOf("microsoft ") >= 0 || text.IndexOf("windows ") >= 0;
            bool systemPath = lowerPath.IndexOf(@"\windows\system32\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lowerPath.IndexOf(@"\windows\syswow64\", StringComparison.OrdinalIgnoreCase) >= 0;
            return systemPath && windowsName;
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
                bool enabled = true;
                try { enabled = Convert.ToBoolean(task.Enabled); } catch { }
                if (!enabled) continue;
                string name = Convert.ToString(task.Name);
                string path = Convert.ToString(task.Path);
                string text = path;
                string description = string.Empty;
                try { description = Convert.ToString(task.Definition.RegistrationInfo.Description); text = Join(text, description); } catch { }
                VendorEvidence evidence = new VendorEvidence().AddHuman(description).AddTechnical(name).AddOpaque(path);
                try
                {
                    foreach (dynamic action in task.Definition.Actions)
                    {
                        try
                        {
                            string actionPath = Convert.ToString(action.Path);
                            string arguments = Convert.ToString(action.Arguments);
                            text = Join(text, actionPath, arguments);
                            evidence.AddFile(actionPath).AddCommand(Join(actionPath, arguments));
                        }
                        catch { }
                    }
                }
                catch { }
                VendorIdentityResult identity = RuleCatalog.ResolveIdentity(evidence);
                if (identity.Confirmed)
                {
                    ActionTarget target = new ActionTarget { Kind = "DisableScheduledTask", TaskName = path };
                    string title = FriendlyTaskTitle(text, name, identity.Vendor);
                    Finding finding = NewFinding("计划任务/定时拉起", title, "会按计划自动拉起：" + title + "。原任务名：" + name, target, text, 30, identity, RuleCatalog.HasBadComponent(evidence, identity));
                    finding.RequiresAdmin = true;
                    list.Add(finding);
                }
            }
            foreach (dynamic child in folder.GetFolders(0))
            {
                ScanTaskFolder(child, list);
            }
        }

        private Finding NewFinding(string category, string title, string impact, ActionTarget target, string text, int baseScore, VendorIdentityResult identity, bool badComponent)
        {
            int score = baseScore + RuleCatalog.VendorBoost(identity, badComponent);
            Finding finding = new Finding();
            finding.Selected = false;
            bool reportOnly = string.Equals(target.Kind, "ReportOnly", StringComparison.OrdinalIgnoreCase);
            finding.Risk = reportOnly ? "低" : (score >= 80 ? "高" : (score >= 55 ? "中" : "低"));
            finding.Score = reportOnly ? Math.Min(score, 20) : score;
            finding.Vendor = identity == null ? "未知第三方" : identity.Vendor;
            finding.Category = category;
            finding.UserVisibleName = Clean(title);
            finding.UserImpact = impact;
            finding.TechnicalLocation = DescribeTarget(target);
            finding.ActionKind = target.Kind;
            finding.Target = target;
            finding.RequiresAdmin = target.Hive == "HKLM" || target.Kind == "DisableService" || target.Kind == "DisableScheduledTask";
            finding.CanRestore = true;
            finding.Evidence = Join(text, identity == null ? string.Empty : "身份依据：" + identity.EvidenceSummary);
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
            return new ActionTarget { Kind = source.Kind, Hive = source.Hive, View = source.View, SubKey = source.SubKey, ValueName = source.ValueName, FilePath = source.FilePath, ServiceName = source.ServiceName, TaskName = source.TaskName, UninstallCommand = source.UninstallCommand };
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

        private static string ResolveClsidRegistration(params string[] values)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string value in values)
            {
                foreach (string clsid in ExtractClsids(value))
                {
                    string info = ReadClsidInfo(clsid);
                    if (string.IsNullOrEmpty(info)) continue;
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(info);
                }
            }
            return sb.ToString();
        }

        private static IEnumerable<string> ExtractClsids(string value)
        {
            if (string.IsNullOrEmpty(value)) yield break;
            int start = 0;
            while (start < value.Length)
            {
                int open = value.IndexOf('{', start);
                if (open < 0) yield break;
                int close = value.IndexOf('}', open + 1);
                if (close < 0) yield break;
                string clsid = value.Substring(open, close - open + 1);
                if (clsid.Length >= 38) yield return clsid;
                start = close + 1;
            }
        }

        private static string ReadClsidInfo(string clsid)
        {
            List<string> parts = new List<string>();
            string subKey = @"Software\Classes\CLSID\" + clsid;
            foreach (ActionTarget target in RegistryTargets(new string[] { subKey }, true, true))
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
                {
                    if (key == null) continue;
                    parts.Add(ReadString(key, ""));
                    parts.Add(ReadChildDefault(target, "InprocServer32"));
                    parts.Add(ReadChildDefault(target, "LocalServer32"));
                    parts.Add(ReadChildDefault(target, "ProgID"));
                }
            }
            return Join(parts.ToArray());
        }

        private static string ReadChildDefault(ActionTarget target, string child)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = RegistryHelper.OpenSubKey(childTarget, false))
            {
                return ReadString(key, "");
            }
        }

        private static string ReadChildValue(ActionTarget target, string child, string valueName)
        {
            ActionTarget childTarget = CopyTarget(target);
            childTarget.SubKey = target.SubKey + "\\" + child;
            using (RegistryKey key = RegistryHelper.OpenSubKey(childTarget, false))
            {
                return ReadString(key, valueName);
            }
        }

        private static string ResolveShortcutText(string file)
        {
            try
            {
                if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                IShellLinkW link = (IShellLinkW)new ShellLinkComObject();
                ((IPersistFile)link).Load(file, 0);
                StringBuilder target = new StringBuilder(1024);
                StringBuilder args = new StringBuilder(1024);
                StringBuilder workingDirectory = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                link.GetArguments(args, args.Capacity);
                link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
                return Join(target.ToString(), args.ToString(), workingDirectory.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string HiddenInstallReason(string display, string uninstall, string systemComponent, string noRemove, string parentKey, bool hidden, bool adOrGuard, bool badComponent)
        {
            List<string> reasons = new List<string>();
            if (string.IsNullOrWhiteSpace(display)) reasons.Add("没有显示名称");
            if (string.IsNullOrWhiteSpace(uninstall)) reasons.Add("没有卸载命令");
            if (IsTruthy(systemComponent)) reasons.Add("标记为系统组件，控制面板可能隐藏");
            if (IsTruthy(noRemove)) reasons.Add("标记为不可移除");
            if (!string.IsNullOrWhiteSpace(parentKey)) reasons.Add("挂在其它组件下面");
            if (!hidden && adOrGuard) reasons.Add("命中弹窗/守护特征");
            if (!hidden && badComponent) reasons.Add("命中已知捆绑组件特征");
            return reasons.Count == 0 ? "疑似捆绑组件" : string.Join("，", reasons.ToArray());
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
            if (target.Kind == "ReportOnly" && !string.IsNullOrWhiteSpace(target.FilePath)) return target.FilePath;
            if (target.Kind == "ReportOnly" && string.IsNullOrWhiteSpace(target.SubKey)) return "只报告";
            string path = RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) path += "::" + target.ValueName;
            if (!string.IsNullOrEmpty(target.View) && target.View != "Default") path += " (" + target.View + ")";
            return path;
        }

        private static bool LooksLikeAdOrGuard(string text)
        {
            string[] tokens = new string[]
            {
                "popup", "adpopup", "adservice", "adpush", "advert", "hotnews", "newsfeed", "notifycenter", "pushservice", "minipage",
                "watchdog", "daemon", "guardservice", "protectservice", "keeper", "serviceplatform",
                "弹窗", "广告", "热点", "资讯", "推荐", "迷你页", "守护", "保护", "修复", "恢复", "推送"
            };
            foreach (string token in tokens)
            {
                if (ContainsBehaviorToken(text, token)) return true;
            }
            return false;
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static bool ContainsBehaviorToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool ascii = token.All(delegate(char c) { return c < 128; });
                if (!ascii || token.Length >= 7) return true;
                int end = index + token.Length;
                bool left = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (left && right) return true;
                start = index + 1;
            }
        }

        private static string FriendlyContextMenuTitle(string subKey, string childName, string display, string mui, string explorerHandler, string commandStateHandler, string clsidText)
        {
            string where = ContextWhereShort(subKey);
            string candidate = FirstHumanText(display, mui);
            if (!string.IsNullOrEmpty(candidate))
            {
                return where + "：会出现“" + candidate + "”";
            }

            string evidence = Join(childName, display, mui, explorerHandler, commandStateHandler, clsidText);
            string feature = FriendlyContextMenuFeature(evidence);
            return where + "：疑似会出现“" + feature + "”";
        }

        private static string FriendlyContextMenuFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("softmgrext") >= 0) return "360 软件管家右键菜单";
            if (lower.IndexOf("safe360ext") >= 0) return "360 安全/扫描右键菜单";
            if (lower.IndexOf("360ai") >= 0) return "360AI 图片右键菜单";
            if (lower.IndexOf("360alb") >= 0 || lower.IndexOf("albumviewer") >= 0 || lower.IndexOf("ablumviewer") >= 0) return "360 看图右键菜单";
            if (lower.IndexOf("qingshell") >= 0 || lower.IndexOf("qingnse") >= 0 || lower.IndexOf("kwpsshell") >= 0) return "WPS/金山相关右键菜单";
            if (lower.IndexOf("kdesktop") >= 0 || lower.IndexOf("wpsdrive") >= 0) return "WPS 云文档/磁盘右键菜单";
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘右键菜单";
            bool quarkEvidence = lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0 || lower.IndexOf("vt.quark.cn") >= 0 || lower.IndexOf("external_rclick") >= 0;
            if (lower.IndexOf("quarkclouddrive.upload") >= 0 || lower.IndexOf("上传到夸克") >= 0) return "夸克网盘上传右键菜单";
            if (lower.IndexOf("quarkclouddrive.backup") >= 0) return "夸克网盘备份右键菜单";
            if (quarkEvidence && (lower.IndexOf("quarkpdf") >= 0 || lower.IndexOf("quarkconvert") >= 0 || lower.IndexOf("pdf转换") >= 0 || lower.IndexOf("图片转pdf") >= 0 || lower.IndexOf("万能转换") >= 0 || lower.IndexOf("external_rclick") >= 0 || lower.IndexOf("vt.quark.cn") >= 0)) return "夸克 PDF/万能转换右键菜单";
            if (quarkEvidence) return "夸克右键菜单";
            if (lower.IndexOf("sogou") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷右键菜单";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉文件上传右键菜单";
            if (lower.IndexOf("bandiview") >= 0 || lower.IndexOf("honeyview") >= 0) return "BandiView/Honeyview 看图右键菜单";
            if (lower.IndexOf("bandizip") >= 0 || lower.IndexOf("bandisoft") >= 0) return "Bandisoft 右键菜单";
            return ShortVendorName(evidence) + "右键菜单";
        }

        private static string FriendlyStartupTitle(string evidence, string name, string command, string vendor)
        {
            string lower = Join(evidence, name, command).ToLowerInvariant();
            if (lower.IndexOf("360safetray") >= 0) return "360 安全卫士托盘/防护入口";
            if (lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步启动项";
            if (lower.IndexOf("sogou") >= 0 && LooksLikeAdOrGuard(lower)) return "搜狗弹窗/守护启动项";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷开机启动项";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉开机启动项";
            string human = FirstHumanText(name, Path.GetFileNameWithoutExtension(ExtractExecutableName(command)));
            return ShortVendorName(vendor, evidence) + "开机启动：" + (string.IsNullOrEmpty(human) ? "启动项" : human);
        }

        private static string FriendlyBrowserTitle(string evidence, string rawName, string vendor)
        {
            string lower = Join(evidence, rawName).ToLowerInvariant();
            if (lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("wps") >= 0) return "WPS/金山浏览器扩展宿主";
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘浏览器扩展宿主";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克浏览器/网盘外部宿主";
            if (lower.IndexOf("sogou") >= 0) return "搜狗浏览器扩展/策略";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷浏览器下载助手";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉浏览器扩展/外部宿主";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0) return "360 浏览器扩展/策略";
            if (lower.IndexOf("bandisoft") >= 0 || lower.IndexOf("bandiview") >= 0 || lower.IndexOf("bandizip") >= 0) return "Bandisoft 浏览器/外部宿主";
            return ShortVendorName(vendor, evidence) + "浏览器扩展/宿主";
        }

        private static string FriendlyExplorerNamespaceTitle(string subKey, string childName, string display, string localized, string itemName, string evidence)
        {
            string where = ExplorerNamespaceWhereShort(subKey);
            string human = FirstHumanText(display, localized, itemName);
            if (string.IsNullOrWhiteSpace(human)) human = FriendlyExplorerNamespaceFeature(Join(childName, evidence));
            return where + "：会出现“" + human + "”";
        }

        private static string FriendlyExplorerNamespaceFeature(string evidence)
        {
            string lower = (evidence ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf("baidunetdisk") >= 0 || lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("yunshell") >= 0) return "百度网盘入口";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘入口";
            if (lower.IndexOf("wps") >= 0 || lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("金山") >= 0) return "WPS/金山云盘入口";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("迅雷") >= 0) return "迅雷云盘/下载入口";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0 || lower.IndexOf("钉盘") >= 0) return "钉钉/钉盘入口";
            if (lower.IndexOf("tencent") >= 0 || lower.IndexOf("qq") >= 0 || lower.IndexOf("腾讯") >= 0) return "腾讯系云盘入口";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0 || lower.IndexOf("奇虎") >= 0) return "360 云盘/同步入口";
            return ShortVendorName(evidence) + "入口";
        }

        private static string ExplorerNamespaceWhereShort(string subKey)
        {
            string lower = (subKey ?? string.Empty).ToLowerInvariant();
            if (lower.IndexOf(@"\mycomputer\namespace") >= 0) return "此电脑";
            if (lower.IndexOf(@"\networkneighborhood\namespace") >= 0) return "网络位置";
            if (lower.IndexOf(@"\desktop\namespace") >= 0) return "桌面/导航栏";
            if (lower.IndexOf(@"\classes\clsid\") >= 0) return "资源管理器导航栏";
            return "资源管理器";
        }

        private static string FriendlyServiceTitle(string evidence, string name, string display, string vendor)
        {
            string lower = Join(evidence, name, display).ToLowerInvariant();
            if (lower.IndexOf("q360amppl") >= 0) return "360 安全防护后台服务";
            if (lower.IndexOf("zhudongfangyu") >= 0 || lower.IndexOf("主动防御") >= 0 || lower.IndexOf("qhactivedefense") >= 0) return "360 主动防御后台服务";
            if (lower.IndexOf("baidunetdiskutility") >= 0 || lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步后台服务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘后台服务";
            if (lower.IndexOf("wps office cloud service") >= 0 || lower.IndexOf("wpscloud") >= 0) return "WPS 云文档后台服务";
            if (lower.IndexOf("sogousvc") >= 0 || lower.IndexOf("sgimeguard") >= 0) return "搜狗输入法守护/更新服务";
            if (lower.IndexOf("xlservice") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷后台/更新服务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉后台/更新服务";
            string human = FirstHumanText(display, name);
            return ShortVendorName(vendor, evidence) + "后台服务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FriendlyTaskTitle(string evidence, string name, string vendor)
        {
            string lower = Join(evidence, name).ToLowerInvariant();
            if (lower.IndexOf("wpsupdate") >= 0 || lower.IndexOf("wpswake") >= 0) return "WPS 更新/唤醒计划任务";
            if (lower.IndexOf("getword") >= 0 || lower.IndexOf("wordsearch") >= 0 || lower.IndexOf("searchfetch") >= 0) return "360 划词/搜索计划任务";
            if (lower.IndexOf("qihoo") >= 0 || lower.IndexOf("360") >= 0) return "360 定时扫描/拉起计划任务";
            if (lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("baidunetdisk") >= 0) return "百度网盘检测/同步计划任务";
            if (lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘更新/拉起计划任务";
            if (lower.IndexOf("sogou") >= 0) return "搜狗更新/弹窗计划任务";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷更新/拉起计划任务";
            if (lower.IndexOf("dingtalk") >= 0 || lower.IndexOf("钉钉") >= 0) return "钉钉更新/拉起计划任务";
            string human = FirstHumanText(name);
            return ShortVendorName(vendor, evidence) + "计划任务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FirstHumanText(params string[] values)
        {
            foreach (string value in values)
            {
                string cleaned = Clean(value);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                if (LooksTechnicalName(cleaned)) continue;
                return cleaned;
            }
            return string.Empty;
        }

        private static bool LooksTechnicalName(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower)) return true;
            if (lower.IndexOf("{") >= 0 || lower.IndexOf("}") >= 0) return true;
            if (lower.IndexOf(".dll") >= 0 || lower.IndexOf(".exe") >= 0 || lower.IndexOf("\\") >= 0 || lower.IndexOf("/") >= 0) return true;
            string[] tokens = new string[] { "shellext", "safe360ext", "softmgrext", "contextmenu", "qingshell", "qingnse", "clsid", "com.", "native", "handler", "class" };
            foreach (string token in tokens)
            {
                if (lower.IndexOf(token) >= 0) return true;
            }
            bool hasLetter = false;
            bool hasChinese = false;
            int digits = 0;
            foreach (char c in value)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLetter = true;
                if (c >= 0x4e00 && c <= 0x9fff) hasChinese = true;
                if (char.IsDigit(c)) digits++;
            }
            return hasLetter && !hasChinese && digits >= 3 && value.Length >= 8;
        }

        private static string ShortVendorName(string evidence)
        {
            string vendor = RuleCatalog.ResolveVendor(evidence);
            if (vendor == "未知第三方") return "第三方软件";
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ShortVendorName(string vendor, string evidence)
        {
            if (string.IsNullOrWhiteSpace(vendor) || vendor == "未知第三方") return ShortVendorName(evidence);
            return vendor.Replace(" 系列", string.Empty).Replace(" / ", "/");
        }

        private static string ExtractExecutableName(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            command = Environment.ExpandEnvironmentVariables(command.Trim().Trim('"'));
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return command.Substring(0, exe + 4).Trim('"');
            int split = command.IndexOf(' ');
            return split > 0 ? command.Substring(0, split).Trim('"') : command.Trim('"');
        }

        private static string ContextWhereShort(string subKey)
        {
            string lower = subKey.ToLowerInvariant();
            if (lower.IndexOf("\\desktopbackground\\") >= 0 || lower.IndexOf("\\directory\\background\\") >= 0) return "桌面/文件夹空白处右键";
            if (lower.IndexOf("\\drive\\") >= 0) return "磁盘盘符右键";
            if (lower.IndexOf("\\directory\\") >= 0) return "文件夹右键";
            if (lower.IndexOf("\\lnkfile\\") >= 0) return "快捷方式右键";
            if (lower.IndexOf("\\*\\") >= 0) return "普通文件右键";
            return "资源管理器右键";
        }

        private static string DescribeContextMenu(string subKey, string title)
        {
            return Clean(title);
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
            if (lower.IndexOf("quarkclouddrive") >= 0 || lower.IndexOf("quark") >= 0 || lower.IndexOf("夸克") >= 0) return "夸克网盘";
            if (lower.IndexOf("bandiview") >= 0) return "BandiView 看图";
            if (lower.IndexOf("honeyview") >= 0) return "Honeyview 看图";
            if (lower.IndexOf("bandizip") >= 0) return "Bandizip 压缩";
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
                else if (target.Kind == "InvokeUninstaller")
                {
                    LaunchUninstaller(target.UninstallCommand);
                    result.Status = "Launched";
                    result.Message = "已弹出卸载器。请在卸载器窗口里自己确认卸载，完成后重新扫描。";
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
            if (target.Kind == "InvokeUninstaller") return true;
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
            string path = Path.Combine(Path.Combine(batchPath, "registry"), RegistryBackupFileName(target));
            int exitCode = RunHidden("reg.exe", "export \"" + native + "\" \"" + path + "\" /y" + RegistryViewArg(target));
            if (exitCode != 0) Logger.Error("注册表备份失败：" + native, new InvalidOperationException("reg export 退出码 " + exitCode));
            return File.Exists(path) ? path : null;
        }

        private static string RegistryViewArg(ActionTarget target)
        {
            if (target == null) return string.Empty;
            if (string.Equals(target.View, "Registry32", StringComparison.OrdinalIgnoreCase)) return " /reg:32";
            if (string.Equals(target.View, "Registry64", StringComparison.OrdinalIgnoreCase)) return " /reg:64";
            return string.Empty;
        }

        public RestoreBatchResult RestoreBatch(CleanupBatch batch)
        {
            RestoreBatchResult summary = new RestoreBatchResult
            {
                Messages = new List<string>()
            };
            if (batch == null || batch.Results == null) return summary;
            foreach (CleanupResult result in batch.Results)
            {
                summary.Total++;
                string message;
                bool ok = RestoreResult(batch, result, out message);
                if (ok) summary.Succeeded++;
                else summary.Failed++;
                if (!string.IsNullOrWhiteSpace(message)) summary.Messages.Add(message);
            }
            return summary;
        }

        public bool RestoreResult(CleanupResult result, out string message)
        {
            return RestoreResult(null, result, out message);
        }

        private bool RestoreResult(CleanupBatch batch, CleanupResult result, out string message)
        {
            message = string.Empty;
            if (result == null)
            {
                message = "空恢复项，已跳过。";
                return true;
            }
            if (!string.Equals(result.Status, "Done", StringComparison.OrdinalIgnoreCase))
            {
                message = result.Title + "：原清理结果为 " + result.Status + "，无需恢复。";
                return true;
            }
            try
            {
                if (result.Target == null || string.IsNullOrEmpty(result.Target.Kind))
                {
                    message = result.Title + "：缺少恢复目标。";
                    return false;
                }

                ActionTarget target = result.Target;
                if ((target.Kind == "DeleteRegistryKey" || target.Kind == "DeleteRegistryValue") &&
                    target != null)
                {
                    string registryBackup = ResolveRegistryBackupPath(batch, result, target);
                    if (string.IsNullOrEmpty(registryBackup))
                    {
                        message = result.Title + "：旧版清理记录没有找到注册表备份文件，无法完整恢复。";
                        return false;
                    }
                    int exitCode = RunHidden("reg.exe", "import \"" + registryBackup + "\"" + RegistryViewArg(target));
                    bool restored = target.Kind == "DeleteRegistryKey" ? RegistryHelper.KeyExists(target) : RegistryHelper.ValueExists(target);
                    message = result.Title + "：" + (restored ? "注册表已恢复。" : "注册表恢复后复核失败。reg import 退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                string backup = ResolveExistingBackupPath(batch, result);
                if (target.Kind == "MoveFileToBackup" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string dest = Environment.ExpandEnvironmentVariables(target.FilePath);
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (File.Exists(dest))
                    {
                        message = result.Title + "：原位置已经有同名文件，备份已保留，没有覆盖。";
                        return false;
                    }
                    File.Move(backup, dest);
                    bool restored = File.Exists(dest);
                    message = result.Title + "：" + (restored ? "文件已移回原位置。" : "文件恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableService" && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    string state = File.ReadAllText(backup, Encoding.UTF8);
                    string start = state.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0 ? "auto" : (state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ? "disabled" : "demand");
                    int exitCode = RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= " + start);
                    string restoredState = GetServiceState(target.ServiceName);
                    bool restored = start == "auto"
                        ? restoredState.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                        : (start == "disabled" ? restoredState.Equals("Disabled", StringComparison.OrdinalIgnoreCase) : restoredState.Equals("Manual", StringComparison.OrdinalIgnoreCase));
                    message = result.Title + "：" + (restored ? "服务启动状态已恢复。" : "服务恢复后复核失败，当前状态 " + restoredState + "，命令退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "DisableScheduledTask" && !string.IsNullOrEmpty(backup) && Directory.Exists(backup))
                {
                    string xml = Path.Combine(backup, "task.xml");
                    string stateFile = Path.Combine(backup, "state.txt");
                    if (!ScheduledTaskExists(target.TaskName) && File.Exists(xml))
                    {
                        int createCode = RunHidden("schtasks.exe", "/Create /TN \"" + target.TaskName + "\" /XML \"" + xml + "\" /F");
                        if (createCode != 0)
                        {
                            message = result.Title + "：计划任务重建失败，退出码 " + createCode;
                            return false;
                        }
                    }
                    string state = File.Exists(stateFile) ? File.ReadAllText(stateFile, Encoding.UTF8) : "Enabled";
                    bool shouldDisable = state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
                    int changeCode = RunHidden("schtasks.exe", "/Change /TN \"" + target.TaskName + "\" " + (shouldDisable ? "/Disable" : "/Enable"));
                    bool enabled;
                    bool exists = TryGetScheduledTaskEnabled(target.TaskName, out enabled);
                    bool restored = exists && (shouldDisable ? !enabled : enabled);
                    message = result.Title + "：" + (restored ? "计划任务状态已恢复。" : "计划任务恢复后复核失败，命令退出码 " + changeCode);
                    return changeCode == 0 && restored;
                }

                message = result.Title + "：没有可用备份，无法恢复。";
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败：" + result.Title, ex);
                message = result.Title + "：" + ex.Message;
                return false;
            }
        }

        private string ResolveExistingBackupPath(CleanupBatch batch, CleanupResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Backup)) return null;
            string backup = Environment.ExpandEnvironmentVariables(result.Backup);
            if (File.Exists(backup) || Directory.Exists(backup)) return backup;
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && !Path.IsPathRooted(backup))
            {
                string combined = Path.Combine(batch.Path, backup);
                if (File.Exists(combined) || Directory.Exists(combined)) return combined;
            }
            if (batch != null && !string.IsNullOrWhiteSpace(batch.Path) && Directory.Exists(batch.Path))
            {
                string name = Path.GetFileName(backup);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    foreach (string candidate in Directory.GetFiles(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (File.Exists(candidate)) return candidate;
                    }
                    foreach (string candidate in Directory.GetDirectories(batch.Path, name, SearchOption.AllDirectories))
                    {
                        if (Directory.Exists(candidate)) return candidate;
                    }
                }
            }
            return null;
        }

        private string ResolveRegistryBackupPath(CleanupBatch batch, CleanupResult result, ActionTarget target)
        {
            string direct = ResolveExistingBackupPath(batch, result);
            if (!string.IsNullOrEmpty(direct) && direct.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) && File.Exists(direct)) return direct;
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path) || target == null) return null;

            string registryDir = Path.Combine(batch.Path, "registry");
            if (!Directory.Exists(registryDir)) return null;

            string currentName = RegistryBackupFileName(target);
            string currentPath = Path.Combine(registryDir, currentName);
            if (File.Exists(currentPath)) return currentPath;

            string legacyPath = Path.Combine(registryDir, LegacyRegistryBackupFileName(target));
            if (File.Exists(legacyPath)) return legacyPath;

            string needle = RegistryFileNeedle(target);
            if (!string.IsNullOrEmpty(needle))
            {
                foreach (string file in Directory.GetFiles(registryDir, "*.reg", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return file;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static string RegistryBackupFileName(ActionTarget target)
        {
            return CompactBackupFileName(RegistryBackupRawName(target), ".reg");
        }

        private static string LegacyRegistryBackupFileName(ActionTarget target)
        {
            return SafeFileName(RegistryBackupRawName(target)) + ".reg";
        }

        private static string RegistryBackupRawName(ActionTarget target)
        {
            string backupName = RegistryHelper.NativePath(target);
            if (!string.IsNullOrEmpty(target.ValueName)) backupName += "__value__" + target.ValueName;
            return backupName;
        }

        private static string RegistryFileNeedle(ActionTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.SubKey)) return string.Empty;
            string hive = string.Equals(target.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
            return "[" + hive + "\\" + target.SubKey + "]";
        }

        private static string CompactBackupFileName(string raw, string extension)
        {
            string safe = SafeFileName(raw);
            if (safe.Length <= 120) return safe + extension;
            string prefix = safe.Substring(0, Math.Min(56, safe.Length));
            string suffix = safe.Substring(Math.Max(0, safe.Length - 44));
            return prefix + "__" + ShortHash(raw) + "__" + suffix + extension;
        }

        private static string ShortHash(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length && builder.Length < 12; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public void DeleteBatchRecord(CleanupBatch batch)
        {
            if (batch == null || string.IsNullOrWhiteSpace(batch.Path)) return;
            string backupRoot = Path.GetFullPath(store.Backups).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string batchPath = Path.GetFullPath(batch.Path);
            if (!batchPath.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("恢复记录路径不在备份目录下，拒绝删除：" + batchPath);
            }
            if (Directory.Exists(batchPath)) Directory.Delete(batchPath, true);
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
                return TryGetScheduledTaskEnabledFromXml(taskName, out enabled);
            }
        }

        private static bool TryGetScheduledTaskEnabledFromXml(string taskName, out bool enabled)
        {
            enabled = false;
            string xml = QueryTaskXml(taskName);
            if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (xml.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                enabled = false;
                return true;
            }
            enabled = true;
            return true;
        }

        internal static void WriteJson(string path, object value)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            WriteText(path, serializer.Serialize(value));
        }

        private static void WriteText(string path, string text)
        {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tempPath = Path.Combine(dir, Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(tempPath, text ?? string.Empty, new UTF8Encoding(true));
            if (File.Exists(fullPath))
            {
                string backupPath = tempPath + ".bak";
                File.Replace(tempPath, fullPath, backupPath, true);
                try { File.Delete(backupPath); } catch { }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }
        }

        private static int RunHidden(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { }
                        Logger.Error("命令执行超时：" + file + " " + args, new TimeoutException("等待 60 秒仍未退出。"));
                        return -1;
                    }
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("命令执行失败：" + file + " " + args, ex);
                return -1;
            }
        }

        private static void LaunchUninstaller(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("没有卸载命令。");
            string file;
            string args;
            SplitCommandLine(Environment.ExpandEnvironmentVariables(command), out file, out args);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = file;
            psi.Arguments = args;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static void SplitCommandLine(string command, out string file, out string args)
        {
            command = (command ?? string.Empty).Trim();
            file = command;
            args = string.Empty;
            if (command.Length == 0) return;
            if (command[0] == '"')
            {
                int close = command.IndexOf('"', 1);
                if (close > 0)
                {
                    file = command.Substring(1, close - 1);
                    args = command.Substring(close + 1).Trim();
                    return;
                }
            }
            foreach (string extension in new string[] { ".exe", ".cmd", ".bat", ".com" })
            {
                int exeEnd = command.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
                if (exeEnd > 0)
                {
                    exeEnd += extension.Length;
                    file = command.Substring(0, exeEnd).Trim();
                    args = command.Substring(exeEnd).Trim();
                    return;
                }
            }
            int split = command.IndexOf(' ');
            if (split > 0)
            {
                file = command.Substring(0, split);
                args = command.Substring(split + 1).Trim();
            }
        }

        private static string SafeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }
    }

#if VALIDATION
    internal sealed class ValidationReport
    {
        public string StartedAt { get; set; }
        public string CompletedAt { get; set; }
        public string ExecutableDirectory { get; set; }
        public bool IsAdministrator { get; set; }
        public bool AllRunnableCasesPassed { get; set; }
        public bool HasAdminSkippedCases { get; set; }
        public string Summary { get; set; }
        public List<ValidationCaseResult> Cases { get; set; }
    }

    internal sealed class ValidationCaseResult
    {
        public string Name { get; set; }
        public string Vendor { get; set; }
        public string Area { get; set; }
        public string Needle { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool SetupSucceeded { get; set; }
        public string SetupMessage { get; set; }
        public bool DetectedBeforeClean { get; set; }
        public bool CleanVerified { get; set; }
        public bool RestoreVerified { get; set; }
        public string CleanupStatus { get; set; }
        public string Result { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ValidationCase
    {
        public string Name;
        public string Vendor;
        public string Area;
        public string Needle;
        public bool RequiresAdmin;
        public bool ExpectPresentAfterCleanScan;
        public bool SetupSucceeded;
        public string SetupMessage;
        public Action Create;
        public Func<bool> Exists;
        public Func<bool> Cleaned;
        public Func<bool> Restored;
    }

    internal static class ValidationRunner
    {
        private const string Marker = "CodexRogueCleanerTest";
        private const string TaskName = "\\CodexRogueCleanerTest_360Safe_Task";
        private const string ServiceName = "CodexRogueCleanerTest360Svc";
        private static readonly string[] TestKeys = new string[]
        {
            @"Software\Classes\Directory\Background\shell\CodexRogueCleanerTest_360Safe_RightMenu",
            @"Software\Classes\*\shell\CodexRogueCleanerTest_WPSPic_RightMenu",
            @"Software\Classes\Drive\shell\CodexRogueCleanerTest_kdesktop_WPSDisk",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{C0DE2026-0721-4A20-8A00-BA1D0E7D15C0}",
            @"Software\Google\Chrome\NativeMessagingHosts\com.codex.roguecleaner.BaiduNetdiskImageViewer",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexRogueCleanerTest_SogouAdComponent",
            @"Software\Classes\*\shell\CodexRogueCleanerTest_DingTalkUpload",
            @"Software\Classes\CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"
        };

        public static int Run(DataStore store)
        {
            ValidationReport report = new ValidationReport();
            report.StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report.ExecutableDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            report.IsAdministrator = AdminUtil.IsAdministrator();
            List<ValidationCase> testCases = BuildCases(report.IsAdministrator);
            report.Cases = new List<ValidationCaseResult>();

            try
            {
                CleanupArtifacts(report.IsAdministrator);
                foreach (ValidationCase testCase in testCases)
                {
                    if (testCase.RequiresAdmin && !report.IsAdministrator)
                    {
                        testCase.SetupSucceeded = false;
                        testCase.SetupMessage = "需要管理员权限，当前未创建模拟工件。";
                        continue;
                    }
                    try
                    {
                        testCase.Create();
                        testCase.SetupSucceeded = testCase.Exists();
                        testCase.SetupMessage = testCase.SetupSucceeded ? "模拟工件创建成功。" : "创建命令返回后未回读到模拟工件。";
                    }
                    catch (Exception ex)
                    {
                        testCase.SetupSucceeded = false;
                        testCase.SetupMessage = ex.GetType().Name + ": " + ex.Message;
                    }
                }

                List<Finding> findings = new ScannerEngine().ScanAll(null);
                List<Finding> matched = findings
                    .Where(delegate(Finding f) { return ContainsTestMarker(f); })
                    .ToList();
                foreach (Finding finding in matched)
                {
                    finding.Selected = finding.CanClean;
                }

                CleanerEngine cleaner = new CleanerEngine(store);
                CleanupBatch batch = cleaner.Clean(matched);
                List<Finding> afterClean = new ScannerEngine().ScanAll(null);
                Dictionary<string, bool> cleanedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, bool> absentAfterCleanScanStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (ValidationCase testCase in testCases)
                {
                    if (testCase.SetupSucceeded)
                    {
                        cleanedStates[testCase.Needle] = testCase.Cleaned();
                        absentAfterCleanScanStates[testCase.Needle] = !afterClean.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                    }
                }
                cleaner.RestoreBatch(batch);
                List<Finding> afterRestore = new ScannerEngine().ScanAll(null);

                foreach (ValidationCase testCase in testCases)
                {
                    ValidationCaseResult result = BuildCaseResult(testCase);
                    if (testCase.RequiresAdmin && !report.IsAdministrator)
                    {
                        result.Result = "Skipped";
                        result.Message = "需要管理员权限创建和禁用模拟服务，本次非管理员运行，未执行。";
                        report.HasAdminSkippedCases = true;
                    }
                    else if (!testCase.SetupSucceeded)
                    {
                        result.Result = "SetupFailed";
                        result.Message = "模拟工件创建失败，未进入清理验证：" + testCase.SetupMessage;
                    }
                    else
                    {
                        result.DetectedBeforeClean = matched.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                        CleanupResult cleanup = batch.Results.FirstOrDefault(delegate(CleanupResult r) { return Contains(r, testCase.Needle); });
                        result.CleanupStatus = cleanup == null ? "MissingCleanupResult" : cleanup.Status + ": " + cleanup.Message;
                        result.CleanVerified = cleanedStates.ContainsKey(testCase.Needle) && cleanedStates[testCase.Needle];
                        result.RestoreVerified = testCase.Restored();
                        bool absentAfterCleanScan = absentAfterCleanScanStates.ContainsKey(testCase.Needle) && absentAfterCleanScanStates[testCase.Needle];
                        bool presentAfterRestoreScan = afterRestore.Any(delegate(Finding f) { return Contains(f, testCase.Needle); });
                        bool cleanScanOk = testCase.ExpectPresentAfterCleanScan ? !absentAfterCleanScan : absentAfterCleanScan;
                        bool pass = result.DetectedBeforeClean && result.CleanVerified && result.RestoreVerified && cleanScanOk && presentAfterRestoreScan;
                        result.Result = pass ? "Pass" : "Fail";
                        result.Message = pass
                            ? (testCase.ExpectPresentAfterCleanScan ? "扫描命中、卸载器已启动、条目保留给用户卸载确认。" : "扫描命中、清理后回读消失、恢复后回读出现。")
                            : "验收失败：Detected=" + result.DetectedBeforeClean + ", Cleaned=" + result.CleanVerified + ", Restored=" + result.RestoreVerified + ", ScanAbsentAfterClean=" + absentAfterCleanScan + ", ExpectedPresentAfterCleanScan=" + testCase.ExpectPresentAfterCleanScan + ", ScanPresentAfterRestore=" + presentAfterRestoreScan;
                    }
                    report.Cases.Add(result);
                }

                report.AllRunnableCasesPassed = report.Cases.All(delegate(ValidationCaseResult c) { return c.Result == "Pass" || c.Result == "Skipped"; });
                report.Summary = "RunnableCases=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result != "Skipped"; }) +
                    ", Passed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Pass"; }) +
                    ", Failed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Fail"; }) +
                    ", SetupFailed=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "SetupFailed"; }) +
                    ", Skipped=" + report.Cases.Count(delegate(ValidationCaseResult c) { return c.Result == "Skipped"; });
                return report.Cases.Any(delegate(ValidationCaseResult c) { return c.Result == "Fail" || c.Result == "SetupFailed"; }) ? 1 : (report.HasAdminSkippedCases ? 2 : 0);
            }
            catch (Exception ex)
            {
                report.AllRunnableCasesPassed = false;
                report.Summary = "验收异常：" + ex.Message;
                Logger.Error("自动验收失败", ex);
                return 1;
            }
            finally
            {
                report.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string path = Path.Combine(store.Reports, "acceptance-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                CleanerEngine.WriteJson(path, report);
                CleanupArtifacts(report.IsAdministrator);
            }
        }

        private static List<ValidationCase> BuildCases(bool isAdmin)
        {
            List<ValidationCase> cases = new List<ValidationCase>();
            cases.Add(new ValidationCase
            {
                Name = "普通文件夹空白处右键：使用360测试右键菜单",
                Vendor = "360 系列",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_360Safe_RightMenu",
                Create = delegate { CreateRegistryKey(TestKeys[0], "使用360测试右键菜单", @"C:\CodexRogueCleanerTest\360Safe\360tray.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[0]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[0]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[0]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "普通文件右键：WPS 图片测试右键菜单",
                Vendor = "WPS / 金山",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_WPSPic_RightMenu",
                Create = delegate { CreateRegistryKey(TestKeys[1], "WPS图片测试右键菜单", @"C:\CodexRogueCleanerTest\Kingsoft\WPS Office\WPSPic.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[1]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[1]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[1]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "普通文件右键：上传钉钉并打开",
                Vendor = "钉钉",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_DingTalkUpload",
                Create = delegate { CreateRegistryKey(TestKeys[6], "上传钉钉并打开", @"cmd.exe /c exit 0"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[6]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[6]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[6]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "磁盘盘符右键：WPS 云盘/磁盘入口测试",
                Vendor = "WPS / 金山",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_kdesktop_WPSDisk",
                Create = delegate { CreateRegistryKey(TestKeys[2], "WPS云盘/磁盘入口测试", @"C:\CodexRogueCleanerTest\Kingsoft\WPS Office\kdesktop.exe"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[2]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[2]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[2]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "此电脑入口：百度网盘测试图标",
                Vendor = "百度 / 百度网盘",
                Area = "此电脑/资源管理器入口",
                Needle = "CodexRogueCleanerTest_BaiduNetdisk_ThisPC",
                Create = delegate { CreateExplorerNamespaceKey(TestKeys[3], "百度网盘同步入口 " + Marker + "_BaiduNetdisk_ThisPC"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[3]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[3]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[3]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：搜狗弹窗测试项",
                Vendor = "搜狗",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_SogouInputPop",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop", @"C:\CodexRogueCleanerTest\Sogou\SogouInputPop.exe"); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：迅雷自启测试项",
                Vendor = "迅雷",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_ThunderStart",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart", @"C:\CodexRogueCleanerTest\Xunlei\ThunderStart.exe"); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "浏览器插件/外部宿主：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "浏览器插件/外部宿主",
                Needle = "BaiduNetdiskImageViewer",
                Create = delegate { CreateNativeHostKey(TestKeys[4]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[4]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[4]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[4]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "疑似捆绑组件：不能静默时弹出原厂卸载器",
                Vendor = "搜狗",
                Area = "疑似捆绑/弹窗组件",
                Needle = "CodexRogueCleanerTest_SogouAdComponent",
                ExpectPresentAfterCleanScan = true,
                Create = delegate { CreateUninstallEntry(TestKeys[5]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[5]); },
                Cleaned = delegate { return WaitForFile(UninstallerMarkerPath(), 5000); },
                Restored = delegate { return RegistryKeyExists(TestKeys[5]); }
            });
            cases.Add(new ValidationCase
            {
                Name = ".png 打开方式：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "文件关联/打开方式",
                Needle = "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open",
                Create = delegate { CreateOpenWithProgId(); },
                Exists = delegate { return RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); },
                Restored = delegate { return RegistryValueExists(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "计划任务：360 定时拉起测试项",
                Vendor = "360 系列",
                Area = "计划任务/定时拉起",
                Needle = "CodexRogueCleanerTest_360Safe_Task",
                RequiresAdmin = true,
                Create = delegate { CreateScheduledTask(); },
                Exists = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled); },
                Cleaned = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled) && !enabled; },
                Restored = delegate { bool enabled; return TryGetScheduledTaskEnabled(TaskName, out enabled) && enabled; }
            });
            cases.Add(new ValidationCase
            {
                Name = "后台服务：360 服务测试项",
                Vendor = "360 系列",
                Area = "后台服务",
                Needle = ServiceName,
                RequiresAdmin = true,
                Create = delegate { CreateService(); },
                Exists = delegate { return ServiceExists(ServiceName); },
                Cleaned = delegate { return ServiceStartMode(ServiceName).Equals("Disabled", StringComparison.OrdinalIgnoreCase); },
                Restored = delegate { return ServiceStartMode(ServiceName).Equals("Manual", StringComparison.OrdinalIgnoreCase); }
            });
            return cases;
        }

        private static ValidationCaseResult BuildCaseResult(ValidationCase testCase)
        {
            return new ValidationCaseResult
            {
                Name = testCase.Name,
                Vendor = testCase.Vendor,
                Area = testCase.Area,
                Needle = testCase.Needle,
                RequiresAdmin = testCase.RequiresAdmin,
                SetupSucceeded = testCase.SetupSucceeded,
                SetupMessage = testCase.SetupMessage,
                Result = "Pending"
            };
        }

        private static void CleanupArtifacts(bool includeService)
        {
            foreach (string key in TestKeys) DeleteRegistryKey(key);
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_SogouInputPop");
            DeleteRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "CodexRogueCleanerTest_ThunderStart");
            DeleteRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open");
            try { File.Delete(UninstallerMarkerPath()); } catch { }
            RunProcess("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F");
            WaitUntil(delegate { bool enabled; return !TryGetScheduledTaskEnabled(TaskName, out enabled); }, 5000);
            if (includeService)
            {
                RunProcess("sc.exe", "delete \"" + ServiceName + "\"");
                WaitUntil(delegate { return !ServiceExists(ServiceName); }, 5000);
            }
            try
            {
                string validationRoot = Path.Combine(Path.GetTempPath(), Marker);
                if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
            }
            catch { }
        }

        private static bool ContainsTestMarker(Finding finding)
        {
            return Contains(finding, Marker) ||
                Contains(finding, "BaiduNetdiskImageViewer") ||
                Contains(finding, "SogouInputPop") ||
                Contains(finding, "ThunderStart");
        }

        private static bool Contains(Finding finding, string value)
        {
            if (finding == null) return false;
            string text = string.Join(" ", new string[] { finding.UserVisibleName, finding.UserImpact, finding.TechnicalLocation, finding.Evidence, finding.ActionKind });
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(CleanupResult result, string value)
        {
            if (result == null) return false;
            string text = string.Join(" ", new string[] { result.Title, result.Category, result.TechnicalLocation, result.Message, result.ActionKind });
            if (result.Target != null) text = text + " " + result.Target.SubKey + " " + result.Target.ValueName + " " + result.Target.TaskName + " " + result.Target.ServiceName;
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CreateRegistryKey(string keyPath, string title, string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", title);
                key.SetValue("MUIVerb", title);
                key.SetValue("CodexMarker", Marker);
            }
            using (RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(keyPath + "\\command"))
            {
                commandKey.SetValue("", command);
            }
        }

        private static void CreateNativeHostKey(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", @"C:\CodexRogueCleanerTest\BaiduNetdiskImageViewer.json");
                key.SetValue("Description", Marker + " BaiduNetdiskImageViewer");
            }
        }

        private static void CreateOpenWithProgId()
        {
            SetRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open", string.Empty);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TestKeys[7] + @"\shell\open\command"))
            {
                key.SetValue("", @"C:\CodexRogueCleanerTest\BaiduNetdisk\BaiduNetdiskImageViewer.exe ""%1""");
            }
        }

        private static void CreateExplorerNamespaceKey(string keyPath, string title)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("", title);
                key.SetValue("System.ItemNameDisplay", title);
                key.SetValue("TargetFolderPath", @"C:\CodexRogueCleanerTest\BaiduNetdisk");
                key.SetValue("CodexMarker", Marker);
            }
        }

        private static void CreateUninstallEntry(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("DisplayName", "搜狗弹窗组件 " + Marker + "_SogouAdComponent");
                key.SetValue("Publisher", "搜狗");
                key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
                key.SetValue("InstallLocation", Path.Combine(Path.GetTempPath(), Marker));
                key.SetValue("DisplayIcon", Path.Combine(Path.GetTempPath(), Marker, "Sogou", "SogouAd.exe"));
                key.SetValue("UninstallString", "cmd.exe /c echo CodexRogueCleanerTest_Uninstaller_Launched> \"" + UninstallerMarkerPath() + "\"");
            }
        }

        private static string UninstallerMarkerPath()
        {
            return Path.Combine(Path.GetTempPath(), "CodexRogueCleanerTest_UninstallerLaunched.txt");
        }

        private static bool WaitForFile(string path, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (File.Exists(path)) return true;
                Thread.Sleep(100);
            }
            return File.Exists(path);
        }

        private static void SetRegistryValue(string keyPath, string name, string value)
        {
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\" + keyPath, name, value ?? string.Empty, RegistryValueKind.String);
            }
            catch (UnauthorizedAccessException)
            {
                string args = "add \"HKCU\\" + keyPath + "\" /v \"" + name + "\" /t REG_SZ /d \"" + (value ?? string.Empty) + "\" /f";
                int exitCode = RunProcess("reg.exe", args);
                if (exitCode != 0) throw;
            }
        }

        private static void DeleteRegistryKey(string keyPath)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); } catch { }
        }

        private static void DeleteRegistryValue(string keyPath, string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    if (key != null) key.DeleteValue(name, false);
                }
            }
            catch { }
        }

        private static bool RegistryKeyExists(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                return key != null;
            }
        }

        private static bool RegistryValueExists(string keyPath, string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, false))
            {
                return key != null && key.GetValueNames().Any(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
            }
        }

        private static void CreateScheduledTask()
        {
            string time = DateTime.Now.AddMinutes(10).ToString("HH:mm");
            string executable = CreateValidationExecutable("360Safe", "360SafeTask.exe");
            int exitCode = RunProcess("schtasks.exe", "/Create /SC DAILY /ST " + time + " /TN \"" + TaskName + "\" /TR \"" + executable + "\" /F");
            if (exitCode != 0) throw new InvalidOperationException("schtasks 创建失败，退出码 " + exitCode);
            WaitUntil(delegate
            {
                bool enabled;
                return TryGetScheduledTaskEnabled(TaskName, out enabled);
            }, 5000);
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(100);
            }
            return condition();
        }

        private static string BenignWindowsExecutable()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windows, "System32\\notepad.exe");
        }

        private static string CreateValidationExecutable(string vendorFolder, string fileName)
        {
            string path = Path.Combine(Path.GetTempPath(), Marker, vendorFolder, fileName);
            string parent = Path.GetDirectoryName(path);
            if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
            File.Copy(BenignWindowsExecutable(), path, true);
            return path;
        }

        private static void CreateService()
        {
            int exitCode = RunProcess("sc.exe", "create \"" + ServiceName + "\" binPath= \"cmd.exe /c exit 0\" DisplayName= \"360Safe CodexRogueCleanerTest Service\" start= demand");
            if (exitCode != 0) throw new InvalidOperationException("sc create 创建失败，退出码 " + exitCode);
            WaitUntil(delegate { return ServiceExists(ServiceName); }, 5000);
        }

        private static bool ServiceExists(string serviceName)
        {
            return !string.Equals(ServiceStartMode(serviceName), "Missing", StringComparison.OrdinalIgnoreCase);
        }

        private static string ServiceStartMode(string serviceName)
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
            return "Missing";
        }

        private static bool TryGetScheduledTaskEnabled(string taskName, out bool enabled)
        {
            enabled = false;
            try
            {
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
                string xml = RunProcessWithOutput("schtasks.exe", "/Query /TN \"" + taskName + "\" /XML");
                if (string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0) return false;
                if (xml.IndexOf("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    enabled = false;
                    return true;
                }
                enabled = true;
                return true;
            }
        }

        private static string RunProcessWithOutput(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(60000))
                    {
                        try { process.Kill(); } catch { }
                        return string.Empty;
                    }
                    return string.IsNullOrWhiteSpace(output) ? error : output;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int RunProcess(string file, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(file, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit(60000);
                    return process.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }
    }

#endif

    internal sealed class MainForm : Form, IProgressSink
    {
        private readonly DataStore store;
        private readonly BindingList<Finding> rows = new BindingList<Finding>();
        private readonly DataGridView grid = new BufferedDataGridView();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label versionLabel = new Label();
        private readonly LinkLabel authorLink = new LinkLabel();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button scanButton = new Button();
        private readonly Button cleanButton = new Button();
        private readonly Button restoreButton = new Button();
        private readonly Button reportButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button lowButton = new Button();
        private readonly Button updateButton = new Button();
        private readonly Button adminButton = new Button();
        private readonly Button feedbackButton = new Button();
        private readonly Button overviewNavButton = new Button();
        private readonly Button startupNavButton = new Button();
        private readonly Button contextNavButton = new Button();
        private readonly Button diagnoseNavButton = new Button();
        private readonly Button recoveryNavButton = new Button();
        private readonly Label totalCardValue = new Label();
        private readonly Label suggestionCardValue = new Label();
        private readonly Label manageableCardValue = new Label();
        private readonly Label unknownCardValue = new Label();
        private readonly Label detailTitleLabel = new Label();
        private readonly Label detailMetaLabel = new Label();
        private readonly Label detailIdentityLabel = new Label();
        private readonly Label detailBehaviorLabel = new Label();
        private readonly Label detailReasonLabel = new Label();
        private readonly Label detailImpactLabel = new Label();
        private readonly TextBox detailLocationBox = new TextBox();
        private int gridDataErrorCount;
        private readonly Button copyDetailButton = new Button();
        private readonly SplitContainer contentSplit = new SplitContainer();
        private readonly TextBox searchBox = new TextBox();
        private string latestEvidenceReportPath;
        private bool isBusy;
        private string activeCategoryFilter = "总览";

        public MainForm(DataStore store)
            : this(store, true)
        {
        }

        internal MainForm(DataStore store, bool checkUpdates)
        {
            this.store = store;
            BuildUi();
            if (checkUpdates) UpdateChecker.CheckOnStartup(store, this);
        }

        private void BuildUi()
        {
            Text = AppMeta.ProductName + " " + AppMeta.Version;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 700);
            Size = new Size(1360, 820);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Canvas;
            Font = UiTheme.Font(9F, FontStyle.Regular);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 4;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            Controls.Add(root);

            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.BackColor = UiTheme.Surface;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            header.Padding = new Padding(22, 8, 18, 7);
            root.Controls.Add(header, 0, 0);

            FlowLayoutPanel brand = new FlowLayoutPanel();
            brand.Dock = DockStyle.Fill;
            brand.WrapContents = false;
            brand.FlowDirection = FlowDirection.LeftToRight;
            brand.Margin = new Padding(0);
            Label title = new Label { Text = "流氓软件克星", AutoSize = true, ForeColor = UiTheme.Text, Font = UiTheme.Font(17F, FontStyle.Bold), Margin = new Padding(0, 5, 12, 0) };
            brand.Controls.Add(title);
            versionLabel.Text = "v" + AppMeta.Version;
            versionLabel.ForeColor = Color.White;
            versionLabel.BackColor = UiTheme.Primary;
            versionLabel.Font = UiTheme.Font(8.5F, FontStyle.Bold);
            versionLabel.TextAlign = ContentAlignment.MiddleCenter;
            versionLabel.AutoSize = false;
            versionLabel.Size = new Size(66, 24);
            versionLabel.Margin = new Padding(0, 7, 0, 0);
            brand.Controls.Add(versionLabel);
            header.Controls.Add(brand, 0, 0);

            FlowLayoutPanel headerActions = new FlowLayoutPanel();
            headerActions.Dock = DockStyle.Fill;
            headerActions.AutoSize = false;
            headerActions.WrapContents = false;
            headerActions.FlowDirection = FlowDirection.RightToLeft;
            headerActions.Margin = new Padding(0);
            UiTheme.HeaderButton(adminButton, AdminUtil.IsAdministrator() ? "已是管理员" : "管理员重启");
            UiTheme.HeaderButton(feedbackButton, "反馈");
            UiTheme.HeaderButton(updateButton, "检查更新");
            adminButton.Margin = feedbackButton.Margin = updateButton.Margin = new Padding(4, 4, 0, 0);
            headerActions.Controls.Add(adminButton);
            headerActions.Controls.Add(feedbackButton);
            headerActions.Controls.Add(updateButton);
            header.Controls.Add(headerActions, 1, 0);
            adminButton.Enabled = !AdminUtil.IsAdministrator();
            feedbackButton.Enabled = true;
            latestEvidenceReportPath = FindLatestEvidenceReport();

            TableLayoutPanel command = new TableLayoutPanel();
            command.Dock = DockStyle.Fill;
            command.BackColor = UiTheme.Surface;
            command.RowCount = 2;
            command.ColumnCount = 1;
            command.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            command.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));
            command.Padding = new Padding(22, 11, 18, 0);
            root.Controls.Add(command, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            UiTheme.PrimaryButton(scanButton, "▶  开始扫描", UiTheme.Primary);
            UiTheme.OutlineButton(cleanButton, "▣  清理勾选", UiTheme.Danger);
            UiTheme.OutlineButton(selectAllButton, "勾选可清理", UiTheme.Primary);
            UiTheme.OutlineButton(lowButton, "只勾低风险", UiTheme.Success);
            UiTheme.OutlineButton(restoreButton, "恢复中心", Color.FromArgb(79, 70, 229));
            UiTheme.OutlineButton(reportButton, "证据报告", UiTheme.Info);
            scanButton.MinimumSize = new Size(126, 40);
            cleanButton.MinimumSize = new Size(126, 40);
            actions.Controls.Add(scanButton);
            actions.Controls.Add(cleanButton);
            actions.Controls.Add(selectAllButton);
            actions.Controls.Add(lowButton);
            actions.Controls.Add(restoreButton);
            actions.Controls.Add(reportButton);
            command.Controls.Add(actions, 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0);
            progress.Style = ProgressBarStyle.Continuous;
            progress.Visible = false;
            command.Controls.Add(progress, 0, 1);

            TableLayoutPanel workspace = new TableLayoutPanel();
            workspace.Dock = DockStyle.Fill;
            workspace.ColumnCount = 2;
            workspace.RowCount = 1;
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            workspace.Margin = new Padding(0);
            root.Controls.Add(workspace, 0, 2);
            workspace.Controls.Add(BuildNavigation(), 0, 0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 3;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Padding = new Padding(14, 12, 14, 12);
            content.Margin = new Padding(0);
            workspace.Controls.Add(content, 1, 0);

            TableLayoutPanel cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.Controls.Add(CreateSummaryCard("发现项目", totalCardValue, UiTheme.Info, "本次扫描总数"), 0, 0);
            cards.Controls.Add(CreateSummaryCard("建议处理", suggestionCardValue, UiTheme.Danger, "存在可恢复处理动作"), 1, 0);
            cards.Controls.Add(CreateSummaryCard("可管理", manageableCardValue, UiTheme.Success, "正常第三方或低风险项"), 2, 0);
            cards.Controls.Add(CreateSummaryCard("仅提示 / 未知", unknownCardValue, UiTheme.Muted, "不进入批量清理"), 3, 0);
            content.Controls.Add(cards, 0, 0);

            TableLayoutPanel filterBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = UiTheme.Canvas, Margin = new Padding(0) };
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.ForeColor = UiTheme.Muted;
            summaryLabel.Text = "未扫描。";
            filterBar.Controls.Add(summaryLabel, 0, 0);
            Label searchLabel = new Label { Text = "搜索", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted };
            filterBar.Controls.Add(searchLabel, 1, 0);
            searchBox.Dock = DockStyle.Fill;
            searchBox.Margin = new Padding(8, 8, 0, 8);
            searchBox.BorderStyle = BorderStyle.FixedSingle;
            searchBox.Font = UiTheme.Font(9F, FontStyle.Regular);
            searchBox.Text = string.Empty;
            filterBar.Controls.Add(searchBox, 2, 0);
            content.Controls.Add(filterBar, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.DataSource = rows;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.EditMode = DataGridViewEditMode.EditProgrammatically;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = Math.Max(36, GridRowHeight());
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Math.Max(40, GridHeaderHeight());
            grid.BackgroundColor = UiTheme.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = UiTheme.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Text;
            grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.Font(9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.BackColor = UiTheme.Surface;
            grid.DefaultCellStyle.ForeColor = UiTheme.Text;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.PrimarySoft;
            grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.ShowCellToolTips = true;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = string.Empty, Width = 42, MinimumWidth = 42, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, TrueValue = true, FalseValue = false, ThreeState = false, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "RiskDisplay", HeaderText = "层级", Width = 76, MinimumWidth = 72, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UserVisibleName", HeaderText = "名称", FillWeight = 155, MinimumWidth = 115, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "厂商", FillWeight = 105, MinimumWidth = 90, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Category", HeaderText = "位置 / 来源", FillWeight = 105, MinimumWidth = 95, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UserImpact", HeaderText = "影响", FillWeight = 190, MinimumWidth = 145, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ActionText", HeaderText = "操作", Width = 116, MinimumWidth = 108, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
                column.DefaultCellStyle.Alignment = column is DataGridViewCheckBoxColumn ? DataGridViewContentAlignment.MiddleCenter : DataGridViewContentAlignment.MiddleLeft;
            }

            contentSplit.Dock = DockStyle.Fill;
            contentSplit.Orientation = Orientation.Vertical;
            contentSplit.FixedPanel = FixedPanel.Panel2;
            contentSplit.SplitterWidth = 8;
            contentSplit.BackColor = UiTheme.Canvas;
            CardPanel gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            gridCard.Controls.Add(grid);
            contentSplit.Panel1.Controls.Add(gridCard);
            contentSplit.Panel2.Controls.Add(BuildDetailPanel());
            content.Controls.Add(contentSplit, 0, 2);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(238, 242, 247);
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            root.Controls.Add(footer, 0, 3);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(14, 0, 0, 0);
            statusLabel.ForeColor = UiTheme.Muted;
            statusLabel.Text = "就绪。数据目录：" + store.Root;
            footer.Controls.Add(statusLabel, 0, 0);
            authorLink.Dock = DockStyle.Fill;
            authorLink.Text = "作者: " + AppMeta.AuthorName;
            authorLink.TextAlign = ContentAlignment.MiddleRight;
            authorLink.Padding = new Padding(0, 0, 14, 0);
            authorLink.LinkColor = UiTheme.Info;
            authorLink.ActiveLinkColor = Color.FromArgb(29, 78, 216);
            authorLink.VisitedLinkColor = UiTheme.Info;
            footer.Controls.Add(authorLink, 1, 0);

            scanButton.Click += delegate { StartScan(); };
            cleanButton.Click += delegate { StartClean(); };
            selectAllButton.Click += delegate { SetAll(true); };
            lowButton.Click += delegate { SelectLowRisk(); };
            restoreButton.Click += delegate { new RecoveryCenterForm(store).ShowDialog(this); };
            reportButton.Click += delegate { OpenEvidenceReport(); };
            updateButton.Click += delegate { UpdateChecker.CheckNow(store, this, true); };
            adminButton.Click += delegate { AdminUtil.RelaunchAsAdmin(); };
            feedbackButton.Click += delegate { ShowFeedbackForCurrentRow(); };
            searchBox.TextChanged += delegate { ApplyFilter(); };
            rows.ListChanged += delegate { UpdateSummary(); };
            grid.DataError += GridDataError;
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            grid.CellFormatting += GridCellFormatting;
            grid.CellClick += GridCellClick;
            grid.SelectionChanged += delegate { feedbackButton.Enabled = !isBusy; UpdateDetails(); };
            grid.KeyDown += GridKeyDown;
            authorLink.LinkClicked += delegate { OpenAuthorLinks(); };
            copyDetailButton.Click += delegate { CopyCurrentDetails(); };
            Shown += delegate
            {
                if (contentSplit.Width > 850)
                {
                    contentSplit.Panel1MinSize = 500;
                    contentSplit.Panel2MinSize = 300;
                    contentSplit.SplitterDistance = Math.Max(500, contentSplit.Width - 350);
                }
                UpdateDetails();
            };
        }

        private Control BuildNavigation()
        {
            CardPanel nav = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0) };
            TableLayoutPanel items = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0, 16, 0, 0), Margin = new Padding(0) };
            items.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) items.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            items.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            UiTheme.NavButton(overviewNavButton, "⌂   总览");
            UiTheme.NavButton(startupNavButton, "◉   启动项管理");
            UiTheme.NavButton(contextNavButton, "☷   右键管理");
            UiTheme.NavButton(diagnoseNavButton, "▣   弹窗 / 流氓诊断");
            UiTheme.NavButton(recoveryNavButton, "↶   恢复中心");
            foreach (Button button in new Button[] { overviewNavButton, startupNavButton, contextNavButton, diagnoseNavButton, recoveryNavButton }) button.Dock = DockStyle.Fill;
            items.Controls.Add(overviewNavButton, 0, 0);
            items.Controls.Add(startupNavButton, 0, 1);
            items.Controls.Add(contextNavButton, 0, 2);
            items.Controls.Add(diagnoseNavButton, 0, 3);
            items.Controls.Add(recoveryNavButton, 0, 4);
            nav.Controls.Add(items);
            SetNavigation("总览");
            overviewNavButton.Click += delegate { SetNavigation("总览"); };
            startupNavButton.Click += delegate { SetNavigation("启动项"); };
            contextNavButton.Click += delegate { SetNavigation("右键"); };
            diagnoseNavButton.Click += delegate { SetNavigation("诊断"); };
            recoveryNavButton.Click += delegate { new RecoveryCenterForm(store).ShowDialog(this); };
            return nav;
        }

        private Control CreateSummaryCard(string title, Label valueLabel, Color color, string note)
        {
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(16, 10, 12, 8) };
            Label titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 22, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.5F, FontStyle.Regular) };
            valueLabel.Text = "0";
            valueLabel.Dock = DockStyle.Left;
            valueLabel.Width = 66;
            valueLabel.ForeColor = color;
            valueLabel.Font = UiTheme.Font(20F, FontStyle.Bold);
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            Label noteLabel = new Label { Text = note, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8F, FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            Panel row = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
            row.Controls.Add(noteLabel);
            row.Controls.Add(valueLabel);
            card.Controls.Add(row);
            card.Controls.Add(titleLabel);
            return card;
        }

        private Control BuildDetailPanel()
        {
            CardPanel panel = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(0), Margin = new Padding(0) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            detailTitleLabel.Dock = DockStyle.Top;
            detailTitleLabel.Height = 30;
            detailTitleLabel.Font = UiTheme.Font(12F, FontStyle.Bold);
            detailTitleLabel.ForeColor = UiTheme.Text;
            detailTitleLabel.AutoEllipsis = true;
            detailMetaLabel.Dock = DockStyle.Fill;
            detailMetaLabel.ForeColor = UiTheme.Muted;
            detailMetaLabel.AutoEllipsis = true;
            Panel detailHead = new Panel { Dock = DockStyle.Fill };
            detailHead.Controls.Add(detailMetaLabel);
            detailHead.Controls.Add(detailTitleLabel);
            layout.Controls.Add(detailHead, 0, 0);

            Panel sections = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(0) };
            TableLayoutPanel sectionStack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = false, ColumnCount = 1, RowCount = 5, Margin = new Padding(0), Padding = new Padding(0), Height = 590 };
            sectionStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            sectionStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            Control identityCard = CreateDetailCard("身份依据", detailIdentityLabel, 116);
            Control behaviorCard = CreateDetailCard("行为事实", detailBehaviorLabel, 116);
            Control reasonCard = CreateDetailCard("建议原因", detailReasonLabel, 100);
            Control impactCard = CreateDetailCard("处理影响", detailImpactLabel, 100);
            foreach (Control card in new Control[] { identityCard, behaviorCard, reasonCard, impactCard }) card.Dock = DockStyle.Fill;
            sectionStack.Controls.Add(identityCard, 0, 0);
            sectionStack.Controls.Add(behaviorCard, 0, 1);
            sectionStack.Controls.Add(reasonCard, 0, 2);
            sectionStack.Controls.Add(impactCard, 0, 3);
            CardPanel locationCard = new CardPanel { Dock = DockStyle.Fill, Height = 108, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(12, 9, 12, 10) };
            Label locationTitle = new Label { Text = "技术位置", Dock = DockStyle.Top, Height = 24, ForeColor = UiTheme.Primary, Font = UiTheme.Font(9F, FontStyle.Bold) };
            detailLocationBox.Dock = DockStyle.Fill;
            detailLocationBox.Multiline = true;
            detailLocationBox.ReadOnly = true;
            detailLocationBox.BorderStyle = BorderStyle.None;
            detailLocationBox.BackColor = UiTheme.Surface;
            detailLocationBox.ForeColor = UiTheme.Muted;
            detailLocationBox.ScrollBars = ScrollBars.Vertical;
            locationCard.Controls.Add(detailLocationBox);
            locationCard.Controls.Add(locationTitle);
            sectionStack.Controls.Add(locationCard, 0, 4);
            sections.Controls.Add(sectionStack);
            layout.Controls.Add(sections, 0, 1);
            UiTheme.OutlineButton(copyDetailButton, "复制详情", UiTheme.Primary);
            copyDetailButton.Dock = DockStyle.Fill;
            copyDetailButton.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(copyDetailButton, 0, 2);
            panel.Controls.Add(layout);
            sections.SizeChanged += delegate
            {
                int width = Math.Max(240, sections.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
                sectionStack.Width = width;
            };
            return panel;
        }

        private static Control CreateDetailCard(string title, Label body, int height)
        {
            CardPanel card = new CardPanel { Width = 312, Height = height, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(12, 9, 12, 10) };
            Label titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = UiTheme.Primary, Font = UiTheme.Font(9F, FontStyle.Bold) };
            body.Dock = DockStyle.Fill;
            body.ForeColor = UiTheme.Text;
            body.Font = UiTheme.Font(8.5F, FontStyle.Regular);
            body.AutoEllipsis = true;
            body.TextAlign = ContentAlignment.TopLeft;
            card.Controls.Add(body);
            card.Controls.Add(titleLabel);
            return card;
        }

        private void GridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex == 0) ToggleRowSelection(e.RowIndex);
        }

        private void ShowFeedbackForRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[rowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            using (FeedbackForm form = new FeedbackForm(store, finding)) form.ShowDialog(this);
        }

        private void ShowFeedbackForCurrentRow()
        {
            if (grid.CurrentRow != null)
            {
                ShowFeedbackForRow(grid.CurrentRow.Index);
                return;
            }
            Finding empty = new Finding
            {
                Risk = "未判断",
                Vendor = "未知",
                Category = "未扫描到的项目",
                UserVisibleName = "未提供",
                UserImpact = "请在反馈说明中描述遗漏的启动项、右键项或组件。",
                Evidence = "没有关联现有扫描结果。",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly" }
            };
            using (FeedbackForm form = new FeedbackForm(store, empty)) form.ShowDialog(this);
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space || grid.CurrentCell == null || grid.CurrentCell.RowIndex < 0) return;
            ToggleRowSelection(grid.CurrentCell.RowIndex);
            e.Handled = true;
        }

        private void ToggleRowSelection(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[rowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            if (!finding.CanClean)
            {
                finding.Selected = false;
                statusLabel.Text = finding.SelectionHint + " 鼠标悬停这一行可以看完整原因。";
                grid.InvalidateRow(rowIndex);
                UpdateSummary();
                return;
            }
            finding.Selected = !finding.Selected;
            statusLabel.Text = (finding.Selected ? "已勾选：" : "已取消：") + finding.UserVisibleName;
            grid.InvalidateRow(rowIndex);
            UpdateSummary();
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
            if (finding == null) return;
            e.ToolTipText =
                WrapTooltipLine("勾选：", finding.SelectionHint) + Environment.NewLine +
                WrapTooltipLine("用户会看到：", finding.UserVisibleName) + Environment.NewLine +
                WrapTooltipLine("影响：", finding.UserImpact) + Environment.NewLine +
                WrapTooltipLine("处理：", finding.ActionText) + Environment.NewLine +
                WrapTooltipLine("位置：", finding.TechnicalLocation) + Environment.NewLine +
                WrapTooltipLine("证据：", finding.Evidence);
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
            Finding finding = grid.Rows[e.RowIndex].DataBoundItem as Finding;
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (finding != null && !finding.CanClean)
            {
                e.CellStyle.BackColor = Color.FromArgb(248, 250, 252);
                e.CellStyle.ForeColor = Color.FromArgb(100, 116, 139);
                e.CellStyle.SelectionBackColor = Color.FromArgb(226, 232, 240);
                e.CellStyle.SelectionForeColor = Color.FromArgb(51, 65, 85);
            }
            if (!string.Equals(column.DataPropertyName, "RiskDisplay", StringComparison.OrdinalIgnoreCase))
            {
                e.CellStyle.Alignment = column is DataGridViewCheckBoxColumn ? DataGridViewContentAlignment.MiddleCenter : DataGridViewContentAlignment.MiddleLeft;
                return;
            }
            string risk = Convert.ToString(e.Value);
            if (risk == "仅提示")
            {
                e.CellStyle.BackColor = Color.FromArgb(241, 245, 249);
                e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
            else if (risk == "高")
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(185, 28, 28);
            }
            else if (risk == "中")
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
            }
            else if (risk == "低")
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void GridDataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // BindingList 批量刷新时 CurrencyManager 可能在一个消息循环内仍保留旧行索引。
            // 该状态会在绑定重置完成后自行恢复，不应弹出 WinForms 默认异常对话框。
            gridDataErrorCount++;
            e.ThrowException = false;
            e.Cancel = true;
        }

        private static string WrapTooltipLine(string label, string value)
        {
            return WrapTooltipText(label + (value ?? string.Empty), 86);
        }

        private static string WrapTooltipText(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            StringBuilder builder = new StringBuilder();
            int line = 0;
            foreach (char c in text.Replace("\r", string.Empty))
            {
                if (c == '\n')
                {
                    builder.AppendLine();
                    line = 0;
                    continue;
                }
                if (line >= width)
                {
                    builder.AppendLine();
                    line = 0;
                }
                builder.Append(c);
                line++;
                if (line >= 52 && (c == '\\' || c == '/' || c == ';' || c == '；' || c == '，' || c == '。'))
                {
                    builder.AppendLine();
                    line = 0;
                }
            }
            return builder.ToString().TrimEnd();
        }

        private static void OpenAuthorLinks()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = AppMeta.Author52PojieUrl, UseShellExecute = true });
                Process.Start(new ProcessStartInfo { FileName = AppMeta.AuthorGitHubUrl, UseShellExecute = true });
            }
            catch { }
        }

        private int GridRowHeight()
        {
            return Math.Max(34, TextRenderer.MeasureText("国", grid.Font).Height + 16);
        }

        private int GridHeaderHeight()
        {
            return Math.Max(40, TextRenderer.MeasureText("国", grid.ColumnHeadersDefaultCellStyle.Font ?? Font).Height + 18);
        }

        private void StartScan()
        {
            SetBusy(true, "扫描中：多线程翻注册表、服务、计划任务和浏览器角落。");
            string scanStartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Stopwatch scanWatch = Stopwatch.StartNew();
            ReplaceRows(new Finding[0]);
            Task.Factory.StartNew(delegate
            {
                try
                {
                    ScannerEngine engine = new ScannerEngine();
                    List<Finding> result = engine.ScanAll(this);
                    BeginInvoke((MethodInvoker)delegate
                    {
                        scanWatch.Stop();
                        ReplaceRows(result);
                        latestEvidenceReportPath = CleanupEngineWriteScanReport(result);
                        SetBusy(false, "扫描完成，用时 " + scanWatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒。发现 " + result.Count + " 项。证据报告：" + Path.GetFileName(latestEvidenceReportPath));
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("扫描失败", ex);
                    string errorReport = null;
                    try
                    {
                        errorReport = WriteScanErrorReport(ex, scanStartedAt);
                        latestEvidenceReportPath = errorReport;
                    }
                    catch (Exception reportEx)
                    {
                        Logger.Error("写入扫描失败报告失败", reportEx);
                    }
                    BeginInvoke((MethodInvoker)delegate
                    {
                        scanWatch.Stop();
                        string suffix = string.IsNullOrWhiteSpace(errorReport) ? string.Empty : "\n\n已写入失败报告：\n" + errorReport;
                        SetBusy(false, "扫描失败：" + ex.Message + (string.IsNullOrWhiteSpace(errorReport) ? string.Empty : "。失败报告：" + Path.GetFileName(errorReport)));
                        MessageBox.Show(ex.Message + suffix + "\n\n也可以点击“打开证据报告”查看最近一次报告。", "扫描失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
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
                MessageBox.Show(reportOnly > 0 ? "你勾到的是“仅提示”项目。默认打开程序、无可靠卸载命令或正在运行的进程不会参与一键清理，避免误改默认应用或硬删主程序。" : "还没勾选任何可清理项目。", AppMeta.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (selected.Any(delegate(Finding f) { return f.RequiresAdmin; }) && !AdminUtil.IsAdministrator())
            {
                DialogResult elevate = MessageBox.Show("你勾选的项目里有后台服务、系统注册表或计划任务，需要管理员权限。\n\n是否现在以管理员身份重启工具？", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes) AdminUtil.RelaunchAsAdmin();
                return;
            }
            int high = selected.Count(delegate(Finding f) { return f.Risk == "高"; });
            int uninstallers = selected.Count(delegate(Finding f) { return f.ActionKind == "InvokeUninstaller"; });
            string uninstallNote = uninstallers > 0 ? "\n\n其中 " + uninstallers + " 项会弹出原厂卸载器，工具不会自动点卸载，需要你在卸载窗口里自己确认。" : string.Empty;
            DialogResult answer = MessageBox.Show("准备处理 " + selected.Count + " 项，高风险 " + high + " 项。" + uninstallNote + "\n\n会先备份、再处理、最后复核和复扫。继续？", "确认处理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "处理中：先备份，再动手，最后复核。");
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
                        int launched = batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
                        SetBusy(false, failed > 0 ? "处理后复核发现残留：" + failed + " 项。" : "处理完成，已自动复扫。");
                        MessageBox.Show("成功清理：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; }) + " 项\n已弹出卸载器：" + launched + " 项\n失败/残留：" + failed + " 项\n跳过：" + batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Skipped"; }) + " 项\n\n备份目录：" + batch.Path, failed > 0 ? "处理后仍有残留" : "处理完成", MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
            // 扫描线程只报告阶段；结果完成后一次性绑定，避免扫描期间反复重绘整张表。
        }

        private void ReplaceRows(IEnumerable<Finding> findings)
        {
            rows.RaiseListChangedEvents = false;
            try
            {
                rows.Clear();
                foreach (Finding finding in findings) rows.Add(finding);
            }
            finally
            {
                rows.RaiseListChangedEvents = true;
                rows.ResetBindings();
            }
            UpdateSummary();
            UpdateDetails();
        }

        private void SetBusy(bool busy, string status)
        {
            isBusy = busy;
            scanButton.Enabled = !busy;
            cleanButton.Enabled = !busy;
            restoreButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            lowButton.Enabled = !busy;
            reportButton.Enabled = !busy;
            updateButton.Enabled = !busy;
            adminButton.Enabled = !busy && !AdminUtil.IsAdministrator();
            feedbackButton.Enabled = !busy;
            searchBox.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            progress.MarqueeAnimationSpeed = busy ? 25 : 0;
            progress.Visible = busy;
            progress.Value = busy ? 0 : 0;
            Cursor = Cursors.Default;
            UseWaitCursor = false;
            statusLabel.Text = status;
        }

        private void UpdateSummary()
        {
            int selected = rows.Count(delegate(Finding f) { return f.Selected && f.CanClean; });
            int high = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "高"; });
            int medium = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "中"; });
            int low = rows.Count(delegate(Finding f) { return f.CanClean && f.Risk == "低"; });
            int reportOnly = rows.Count(delegate(Finding f) { return !f.CanClean; });
            int cleanable = high + medium + low;
            int manageable = Math.Max(0, rows.Count - cleanable - reportOnly);
            totalCardValue.Text = rows.Count.ToString();
            suggestionCardValue.Text = cleanable.ToString();
            manageableCardValue.Text = manageable.ToString();
            unknownCardValue.Text = reportOnly.ToString();
            summaryLabel.Text = "共 " + rows.Count + " 项  ·  可清理 " + cleanable + "  ·  已勾选 " + selected + "  ·  高 " + high + " / 中 " + medium + " / 低 " + low + "  ·  仅提示 " + reportOnly;
        }

        private void SetAll(bool value)
        {
            foreach (Finding finding in rows) finding.Selected = value && finding.BulkSelectable;
            grid.Refresh();
            UpdateSummary();
        }

        private void SelectLowRisk()
        {
            foreach (Finding finding in rows) finding.Selected = finding.Risk == "低" && finding.BulkSelectable;
            grid.Refresh();
            UpdateSummary();
        }

        private void ApplyFilter()
        {
            string text = searchBox.Text.Trim();
            CurrencyManager manager = (CurrencyManager)BindingContext[rows];
            manager.SuspendBinding();
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
            {
                Finding finding = row.DataBoundItem as Finding;
                if (finding == null) continue;
                string haystack = (finding.RiskDisplay + " " + finding.Vendor + " " + finding.Category + " " + finding.UserVisibleName + " " + finding.UserImpact + " " + finding.ActionText + " " + finding.TechnicalLocation);
                bool textMatches = string.IsNullOrEmpty(text) || haystack.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visible = textMatches && CategoryMatches(finding, activeCategoryFilter);
            }
            manager.ResumeBinding();
            UpdateDetails();
        }

        private void SetNavigation(string filter)
        {
            activeCategoryFilter = filter;
            UiTheme.SetNavActive(overviewNavButton, filter == "总览");
            UiTheme.SetNavActive(startupNavButton, filter == "启动项");
            UiTheme.SetNavActive(contextNavButton, filter == "右键");
            UiTheme.SetNavActive(diagnoseNavButton, filter == "诊断");
            UiTheme.SetNavActive(recoveryNavButton, false);
            if (IsHandleCreated) ApplyFilter();
        }

        private static bool CategoryMatches(Finding finding, string filter)
        {
            if (finding == null || string.IsNullOrEmpty(filter) || filter == "总览") return true;
            string category = finding.Category ?? string.Empty;
            if (filter == "启动项") return ContainsAny(category, "启动", "后台服务", "计划任务", "定时拉起");
            if (filter == "右键") return ContainsAny(category, "右键", "资源管理器", "文件关联", "打开方式", "此电脑");
            if (filter == "诊断") return ContainsAny(category, "弹窗", "捆绑", "守护", "正在运行", "浏览器插件", "卸载入口");
            return true;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void UpdateDetails()
        {
            Finding finding = null;
            try
            {
                DataGridViewRow currentRow = grid.CurrentRow;
                if (currentRow != null && currentRow.Index >= 0 && currentRow.Index < grid.Rows.Count)
                {
                    finding = currentRow.DataBoundItem as Finding;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // 增量绑定正在切换 CurrencyManager 位置，下一次 SelectionChanged 会刷新详情。
            }
            catch (IndexOutOfRangeException)
            {
                // 同上：忽略一个消息循环内的瞬时旧索引。
            }
            catch (InvalidOperationException)
            {
                // 表格正在重置绑定，暂时显示空详情。
            }
            if (finding == null)
            {
                detailTitleLabel.Text = "选择一个扫描项目";
                detailMetaLabel.Text = "这里会显示身份、行为、建议和处理影响。";
                detailIdentityLabel.Text = "尚未选择项目。";
                detailBehaviorLabel.Text = "尚未选择项目。";
                detailReasonLabel.Text = "尚未选择项目。";
                detailImpactLabel.Text = "尚未选择项目。";
                detailLocationBox.Text = string.Empty;
                copyDetailButton.Enabled = false;
                return;
            }
            detailTitleLabel.Text = finding.UserVisibleName;
            detailMetaLabel.Text = finding.Vendor + "  ·  " + finding.Category + "  ·  " + finding.RiskDisplay;
            detailIdentityLabel.Text = "厂商：" + finding.Vendor + Environment.NewLine + "证据：" + ShortDetail(finding.Evidence, 230);
            detailBehaviorLabel.Text = ShortDetail(finding.UserImpact, 260);
            detailReasonLabel.Text = finding.CanClean ? "当前结果提供可恢复的处理动作；请结合证据确认后再勾选。" : "当前结果仅作提示，不参与一键清理。";
            detailImpactLabel.Text = finding.ActionText + Environment.NewLine + finding.SelectionHint;
            detailLocationBox.Text = finding.TechnicalLocation ?? string.Empty;
            copyDetailButton.Enabled = true;
        }

        private static string ShortDetail(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未读取到更多信息。";
            string text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "…";
        }

        private void CopyCurrentDetails()
        {
            Finding finding = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as Finding;
            if (finding == null) return;
            string text = "名称：" + finding.UserVisibleName + Environment.NewLine +
                "厂商：" + finding.Vendor + Environment.NewLine +
                "来源：" + finding.Category + Environment.NewLine +
                "风险：" + finding.RiskDisplay + Environment.NewLine +
                "影响：" + finding.UserImpact + Environment.NewLine +
                "处理：" + finding.ActionText + Environment.NewLine +
                "位置：" + finding.TechnicalLocation + Environment.NewLine +
                "证据：" + finding.Evidence;
            try
            {
                Clipboard.SetText(text);
                statusLabel.Text = "已复制当前项目详情。";
            }
            catch (Exception ex)
            {
                Logger.Error("复制项目详情失败", ex);
                MessageBox.Show(this, "复制失败：" + ex.Message, "复制详情", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private string CleanupEngineWriteScanReport(List<Finding> findings)
        {
            string path = Path.Combine(store.Reports, "scan-" + store.Timestamp() + ".json");
            CleanerEngine.WriteJson(path, findings);
            return path;
        }

        private string WriteScanErrorReport(Exception ex, string startedAt)
        {
            string path = Path.Combine(store.Reports, "scan-error-" + store.Timestamp() + ".json");
            ScanErrorReport report = new ScanErrorReport
            {
                StartedAt = startedAt,
                FailedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProductVersion = AppMeta.Version,
                ExecutablePath = Application.ExecutablePath,
                ExecutableDirectory = Path.GetDirectoryName(Application.ExecutablePath),
                DataDirectory = store.Root,
                ErrorType = ex.GetType().FullName,
                ErrorMessage = ex.Message,
                StackTrace = ex.ToString()
            };
            CleanerEngine.WriteJson(path, report);
            return path;
        }

        private void OpenEvidenceReport()
        {
            string path = HasEvidenceReport() ? latestEvidenceReportPath : FindLatestEvidenceReport();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                latestEvidenceReportPath = path;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    statusLabel.Text = "已打开证据报告：" + path;
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Error("打开证据报告失败", ex);
                    MessageBox.Show("证据报告已生成，但打开失败：\n\n" + ex.Message + "\n\n路径：" + path, "打开证据报告失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(store.Reports);
                Process.Start(new ProcessStartInfo { FileName = store.Reports, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error("打开报告目录失败", ex);
            }
            MessageBox.Show("还没有扫描证据报告。\n\n请先点击“开始扫描”。报告会保存到：\n" + store.Reports, "没有证据报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool HasEvidenceReport()
        {
            if (string.IsNullOrWhiteSpace(latestEvidenceReportPath) || !File.Exists(latestEvidenceReportPath))
            {
                latestEvidenceReportPath = FindLatestEvidenceReport();
            }
            return !string.IsNullOrWhiteSpace(latestEvidenceReportPath) && File.Exists(latestEvidenceReportPath);
        }

        private string FindLatestEvidenceReport()
        {
            try
            {
                if (!Directory.Exists(store.Reports)) return null;
                DirectoryInfo dir = new DirectoryInfo(store.Reports);
                FileInfo latest = dir.GetFiles("scan-*.json")
                    .Where(delegate(FileInfo file) { return !file.Name.StartsWith("scan-smoke-", StringComparison.OrdinalIgnoreCase); })
                    .OrderByDescending(delegate(FileInfo file) { return file.LastWriteTimeUtc; })
                    .FirstOrDefault();
                return latest == null ? null : latest.FullName;
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class RecoveryCenterForm : Form
    {
        private readonly DataStore store;
        private readonly ListBox batchList = new ListBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Button restoreBatchButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label emptyLabel = new Label();
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
            Size = new Size(1060, 680);
            MinimumSize = new Size(980, 620);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.FromArgb(15, 118, 110);
            root.Controls.Add(header, 0, 0);

            Label title = new Label();
            title.Text = "恢复中心";
            title.ForeColor = Color.White;
            title.BackColor = Color.Transparent;
            title.Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 18);
            header.Controls.Add(title);

            Label version = new Label();
            version.Text = "v" + AppMeta.Version;
            version.ForeColor = Color.White;
            version.BackColor = Color.FromArgb(13, 148, 136);
            version.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            version.TextAlign = ContentAlignment.MiddleCenter;
            version.AutoSize = false;
            version.Size = new Size(78, 28);
            version.Location = new Point(160, 27);
            header.Controls.Add(version);

            Label sub = new Label();
            sub.Text = "这里放的是清理前备份。恢复前看清批次，恢复后建议重新扫描一次。";
            sub.ForeColor = Color.FromArgb(224, 242, 254);
            sub.BackColor = Color.Transparent;
            sub.AutoSize = true;
            sub.Location = new Point(32, 62);
            header.Controls.Add(sub);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.Padding = new Padding(18, 14, 18, 12);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 1);

            Panel leftPanel = new Panel();
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.BackColor = Color.White;
            leftPanel.BorderStyle = BorderStyle.FixedSingle;
            leftPanel.Padding = new Padding(14, 12, 14, 14);
            body.Controls.Add(leftPanel, 0, 0);

            TableLayoutPanel leftLayout = new TableLayoutPanel();
            leftLayout.Dock = DockStyle.Fill;
            leftLayout.RowCount = 3;
            leftLayout.ColumnCount = 1;
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.Controls.Add(leftLayout);

            Label batchTitle = new Label();
            batchTitle.Text = "备份批次";
            batchTitle.Dock = DockStyle.Fill;
            batchTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            batchTitle.ForeColor = Color.FromArgb(15, 23, 42);
            batchTitle.TextAlign = ContentAlignment.MiddleLeft;
            leftLayout.Controls.Add(batchTitle, 0, 0);

            Label batchHint = new Label();
            batchHint.Text = "选中一个批次，右侧看具体恢复内容。";
            batchHint.Dock = DockStyle.Fill;
            batchHint.ForeColor = Color.FromArgb(71, 85, 105);
            batchHint.TextAlign = ContentAlignment.MiddleLeft;
            leftLayout.Controls.Add(batchHint, 0, 1);

            batchList.Dock = DockStyle.Fill;
            batchList.BorderStyle = BorderStyle.None;
            batchList.BackColor = Color.White;
            batchList.ForeColor = Color.FromArgb(15, 23, 42);
            batchList.DrawMode = DrawMode.OwnerDrawFixed;
            batchList.ItemHeight = 58;
            batchList.IntegralHeight = false;
            leftLayout.Controls.Add(batchList, 0, 2);

            TableLayoutPanel rightLayout = new TableLayoutPanel();
            rightLayout.Dock = DockStyle.Fill;
            rightLayout.RowCount = 2;
            rightLayout.ColumnCount = 1;
            rightLayout.Margin = new Padding(12, 0, 0, 0);
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(rightLayout, 1, 0);

            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.BackColor = Color.FromArgb(226, 232, 240);
            summaryLabel.ForeColor = Color.FromArgb(15, 23, 42);
            summaryLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            summaryLabel.Padding = new Padding(14, 0, 0, 0);
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Text = "正在读取备份批次...";
            rightLayout.Controls.Add(summaryLabel, 0, 0);

            Panel gridPanel = new Panel();
            gridPanel.Dock = DockStyle.Fill;
            gridPanel.BackColor = Color.White;
            gridPanel.BorderStyle = BorderStyle.FixedSingle;
            rightLayout.Controls.Add(gridPanel, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToResizeColumns = false;
            grid.AllowUserToResizeRows = false;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersHeight = 38;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 118, 110);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 251, 241);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            grid.ShowCellToolTips = true;
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "结果", FillWeight = 72, MinimumWidth = 58 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "厂商", FillWeight = 105, MinimumWidth = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Category", HeaderText = "来源", FillWeight = 130, MinimumWidth = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Title", HeaderText = "恢复对象", FillWeight = 220, MinimumWidth = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Message", HeaderText = "当时处理结果", FillWeight = 230, MinimumWidth = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TechnicalLocation", HeaderText = "技术位置", FillWeight = 230, MinimumWidth = 150 });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            gridPanel.Controls.Add(grid);

            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.BackColor = Color.White;
            emptyLabel.ForeColor = Color.FromArgb(71, 85, 105);
            emptyLabel.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.Text = "暂时没有备份批次。\n清理过项目以后，这里会出现可恢复记录。";
            emptyLabel.Visible = false;
            gridPanel.Controls.Add(emptyLabel);
            emptyLabel.BringToFront();

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(226, 232, 240);
            footer.ColumnCount = 3;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            root.Controls.Add(footer, 0, 2);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(18, 0, 0, 0);
            statusLabel.Text = "就绪。";
            footer.Controls.Add(statusLabel, 0, 0);

            ConfigureRecoveryButton(restoreBatchButton, "恢复选中批次", Color.FromArgb(79, 70, 229));
            restoreBatchButton.Dock = DockStyle.Fill;
            restoreBatchButton.Margin = new Padding(0, 6, 10, 6);
            footer.Controls.Add(restoreBatchButton, 1, 0);

            ConfigureRecoveryButton(closeButton, "关闭", Color.FromArgb(71, 85, 105));
            closeButton.Dock = DockStyle.Fill;
            closeButton.Margin = new Padding(0, 6, 18, 6);
            footer.Controls.Add(closeButton, 2, 0);

            batchList.SelectedIndexChanged += delegate { ShowSelectedBatch(); };
            batchList.DrawItem += BatchListDrawItem;
            grid.CellFormatting += GridCellFormatting;
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            restoreBatchButton.Click += delegate { RestoreSelectedBatch(); };
            closeButton.Click += delegate { Close(); };
        }

        private void LoadBatches()
        {
            batches = new CleanerEngine(store).LoadBatches();
            batchList.Items.Clear();
            foreach (CleanupBatch batch in batches)
            {
                int failed = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Failed"; });
                int done = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Done"; });
                int launched = batch.Results == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
                int total = batch.Results == null ? 0 : batch.Results.Count;
                batchList.Items.Add(new BatchListItem(batch, "批次 " + batch.Id, "共 " + total + " 项，成功 " + done + "，弹窗 " + launched + "，失败 " + failed));
            }
            if (batchList.Items.Count > 0) batchList.SelectedIndex = 0;
            else
            {
                summaryLabel.Text = "没有备份批次。";
                statusLabel.Text = "还没有清理记录，所以恢复中心是空的。";
                restoreBatchButton.Enabled = false;
                grid.DataSource = null;
                grid.Visible = false;
                emptyLabel.Visible = true;
                emptyLabel.BringToFront();
            }
        }

        private void ShowSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            List<CleanupResult> results = batch.Results ?? new List<CleanupResult>();
            int done = results.Count(delegate(CleanupResult r) { return r.Status == "Done"; });
            int failed = results.Count(delegate(CleanupResult r) { return r.Status == "Failed"; });
            int launched = results.Count(delegate(CleanupResult r) { return r.Status == "Launched"; });
            int skipped = results.Count(delegate(CleanupResult r) { return r.Status == "Skipped"; });
            grid.DataSource = new BindingList<CleanupResult>(results);
            summaryLabel.Text = "批次 " + batch.Id + "    时间 " + batch.CreatedAt + "    成功 " + done + "，弹窗 " + launched + "，失败 " + failed + "，跳过 " + skipped;
            statusLabel.Text = "备份目录：" + batch.Path;
            restoreBatchButton.Enabled = results.Count > 0;
            grid.Visible = results.Count > 0;
            emptyLabel.Visible = results.Count == 0;
            if (emptyLabel.Visible) emptyLabel.BringToFront();
        }

        private void RestoreSelectedBatch()
        {
            if (batchList.SelectedIndex < 0 || batchList.SelectedIndex >= batches.Count) return;
            CleanupBatch batch = batches[batchList.SelectedIndex];
            if (BatchNeedsAdmin(batch) && !AdminUtil.IsAdministrator())
            {
                DialogResult elevate = MessageBox.Show("这个批次里有系统注册表、后台服务或计划任务，恢复需要管理员权限。\n\n是否现在以管理员身份重启工具？", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes) AdminUtil.RelaunchAsAdmin();
                return;
            }
            DialogResult answer = MessageBox.Show("恢复批次 " + batch.Id + "？\n\n恢复会导入备份注册表或移回被隔离文件。", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                CleanerEngine cleaner = new CleanerEngine(store);
                RestoreBatchResult result = cleaner.RestoreBatch(batch);
                string detail = string.Join(Environment.NewLine, result.Messages.Take(8).ToArray());
                if (result.AllSucceeded)
                {
                    cleaner.DeleteBatchRecord(batch);
                    LoadBatches();
                    statusLabel.Text = "恢复成功，已删除该批次恢复记录。建议重新扫描确认。";
                    MessageBox.Show("恢复成功：" + result.Succeeded + "/" + result.Total + " 项。\n\n该批次记录已从恢复中心删除。\n\n建议重新扫描确认。", "恢复完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    statusLabel.Text = "恢复未完全成功，批次记录已保留。";
                    MessageBox.Show("恢复未完全成功。\n\n成功：" + result.Succeeded + " 项\n失败：" + result.Failed + " 项\n\n失败记录已保留在恢复中心，方便你再次尝试。\n\n" + detail, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("恢复失败", ex);
                MessageBox.Show(ex.Message, "恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool BatchNeedsAdmin(CleanupBatch batch)
        {
            if (batch == null || batch.Results == null) return false;
            foreach (CleanupResult result in batch.Results)
            {
                ActionTarget target = result == null ? null : result.Target;
                if (target == null) continue;
                if (string.Equals(target.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(target.Kind, "DisableService", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(target.Kind, "DisableScheduledTask", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void BatchListDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? Color.FromArgb(204, 251, 241) : Color.White;
            Color titleColor = selected ? Color.FromArgb(15, 118, 110) : Color.FromArgb(15, 23, 42);
            Color subColor = Color.FromArgb(71, 85, 105);
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected)
            {
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(15, 118, 110)))
                {
                    e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.Left, e.Bounds.Top + 6, 4, e.Bounds.Height - 12));
                }
            }
            BatchListItem item = batchList.Items[e.Index] as BatchListItem;
            string title = item == null ? Convert.ToString(batchList.Items[e.Index]) : item.Title;
            string subtitle = item == null ? string.Empty : item.Subtitle;
            Rectangle titleRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 8, e.Bounds.Width - 20, 22);
            Rectangle subRect = new Rectangle(e.Bounds.Left + 14, e.Bounds.Top + 32, e.Bounds.Width - 20, 20);
            TextRenderer.DrawText(e.Graphics, title, new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), titleRect, titleColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, subtitle, Font, subRect, subColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (Pen line = new Pen(Color.FromArgb(226, 232, 240)))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 10, e.Bounds.Bottom - 1);
            }
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (!string.Equals(column.DataPropertyName, "Status", StringComparison.OrdinalIgnoreCase)) return;
            string status = Convert.ToString(e.Value);
            if (status == "Done")
            {
                e.Value = "已处理";
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
            }
            else if (status == "Failed")
            {
                e.Value = "失败";
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(185, 28, 28);
            }
            else if (status == "Launched")
            {
                e.Value = "已弹窗";
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
            }
            else if (status == "Skipped")
            {
                e.Value = "已跳过";
                e.CellStyle.BackColor = Color.FromArgb(226, 232, 240);
                e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
            e.FormattingApplied = true;
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            CleanupResult result = grid.Rows[e.RowIndex].DataBoundItem as CleanupResult;
            if (result == null) return;
            e.ToolTipText =
                "恢复对象：" + result.Title + Environment.NewLine +
                "处理结果：" + result.Message + Environment.NewLine +
                "技术位置：" + result.TechnicalLocation + Environment.NewLine +
                "备份文件：" + result.Backup;
        }

        private static void ConfigureRecoveryButton(Button button, string text, Color color)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        }

        private sealed class BatchListItem
        {
            public readonly CleanupBatch Batch;
            public readonly string Title;
            public readonly string Subtitle;

            public BatchListItem(CleanupBatch batch, string title, string subtitle)
            {
                Batch = batch;
                Title = title;
                Subtitle = subtitle;
            }

            public override string ToString()
            {
                return Title;
            }
        }
    }

    internal sealed class GitHubReleaseInfo
    {
        public string tag_name { get; set; }
        public string body { get; set; }
        public string html_url { get; set; }
        public List<GitHubReleaseAsset> assets { get; set; }
    }

    internal sealed class GitHubReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
        public long size { get; set; }
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
                Task.Factory.StartNew(delegate { CheckNow(store, owner, false); });
            }
            catch (Exception ex)
            {
                Logger.Error("启动更新检查失败", ex);
            }
        }

        public static void CheckNow(DataStore store, IWin32Window owner, bool showNoUpdate)
        {
            try
            {
                if (store == null) throw new ArgumentNullException("store");
                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "RogueCleaner/" + AppMeta.Version);
                    client.Headers.Add("Accept", "application/vnd.github+json");
                    GitHubReleaseInfo release = LoadLatestRelease(client);
                    string tag = release == null ? string.Empty : release.tag_name;
                    string body = release == null ? string.Empty : release.body;
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        throw new InvalidDataException("GitHub Release 信息缺少版本号。");
                    }
                    string latest = tag.TrimStart('v', 'V');
                    if (IsNewer(latest, AppMeta.Version))
                    {
                        GitHubReleaseAsset asset = FindExeAsset(release);
                        if (asset == null)
                        {
                            throw new InvalidDataException("这个版本没有可自动更新的 exe 资产。发布时需要附带 RogueCleaner-*.exe。");
                        }

                        DialogResult answer = MessageBox.Show(owner,
                            "发现新版本：" + tag +
                            "\n当前版本：" + AppMeta.Version +
                            "\n\n" + TrimBody(body) +
                            "\n\n是否现在下载并自动重启更新？",
                            "发现更新",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information,
                            MessageBoxDefaultButton.Button1);
                        if (answer == DialogResult.Yes)
                        {
                            DownloadAndRestart(store, client, asset, tag, owner);
                        }
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
                    MessageBox.Show(owner, "检查更新失败。\n\n可能是系统 TLS、代理、GitHub API 限制，或者本次发布缺少直出 exe 更新包。\n\n错误：" + ConciseError(ex), "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static GitHubReleaseInfo LoadLatestRelease(WebClient client)
        {
            string json = client.DownloadString(AppMeta.LatestApiUrl);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            return serializer.Deserialize<GitHubReleaseInfo>(json);
        }

        private static GitHubReleaseAsset FindExeAsset(GitHubReleaseInfo release)
        {
            if (release == null || release.assets == null) return null;
            GitHubReleaseAsset fallback = null;
            foreach (GitHubReleaseAsset asset in release.assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.name) || string.IsNullOrWhiteSpace(asset.browser_download_url)) continue;
                if (!asset.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (fallback == null) fallback = asset;
                if (asset.name.IndexOf("RogueCleaner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    asset.name.IndexOf(AppMeta.ProductName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return asset;
                }
            }
            return fallback;
        }

        private static void DownloadAndRestart(DataStore store, WebClient client, GitHubReleaseAsset asset, string tag, IWin32Window owner)
        {
            if (!CanWriteToExecutableDirectory())
            {
                MessageBox.Show(owner,
                    "当前目录没有覆盖主程序的权限。\n\n请把软件放到桌面或普通文件夹，或者以管理员身份启动后再检查更新。",
                    "无法自动更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory(store.Updates);
            string safeTag = SafeFileName(tag.TrimStart('v', 'V'));
            string downloadPath = Path.Combine(store.Updates, "RogueCleaner-update-" + safeTag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".exe");
            try
            {
                if (File.Exists(downloadPath)) File.Delete(downloadPath);
                client.Headers["Accept"] = "application/octet-stream";
                client.DownloadFile(asset.browser_download_url, downloadPath);
                ValidateDownloadedExe(downloadPath);
                MessageBox.Show(owner,
                    "新版本已下载完成。\n\n点确定后软件会关闭，自动替换主程序并重新打开。",
                    "准备重启更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                StartHiddenUpdater(store, downloadPath);
                Environment.Exit(0);
            }
            catch
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); } catch { }
                throw;
            }
        }

        private static bool CanWriteToExecutableDirectory()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                string probe = Path.Combine(exeDir, ".roguecleaner-update-write-test-" + Process.GetCurrentProcess().Id + ".tmp");
                File.WriteAllText(probe, DateTime.Now.ToString("o"), Encoding.UTF8);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ValidateDownloadedExe(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists || file.Length < 65536) throw new InvalidDataException("下载到的文件太小，不像完整 exe。");
            using (FileStream stream = File.OpenRead(path))
            {
                int b1 = stream.ReadByte();
                int b2 = stream.ReadByte();
                if (b1 != 'M' || b2 != 'Z') throw new InvalidDataException("下载到的不是 Windows exe 文件。");
            }
        }

        private static void StartHiddenUpdater(DataStore store, string downloadedExe)
        {
            string currentExe = Application.ExecutablePath;
            string safeVersion = SafeFileName(AppMeta.Version);
            string scriptPath = Path.Combine(store.Updates, "apply-update-from-" + safeVersion + ".cmd");
            string logPath = Path.Combine(store.Updates, "update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            string script =
                "@echo off\r\n" +
                "setlocal\r\n" +
                "set \"SRC=" + downloadedExe + "\"\r\n" +
                "set \"DST=" + currentExe + "\"\r\n" +
                "set \"LOG=" + logPath + "\"\r\n" +
                "set TRY=0\r\n" +
                ":copyagain\r\n" +
                "copy /y \"%SRC%\" \"%DST%\" >nul 2>>\"%LOG%\"\r\n" +
                "if not errorlevel 1 goto launch\r\n" +
                "set /a TRY+=1\r\n" +
                "if %TRY% GEQ 90 goto fail\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                "goto copyagain\r\n" +
                ":launch\r\n" +
                "start \"\" \"%DST%\"\r\n" +
                "del \"%SRC%\" >nul 2>nul\r\n" +
                "del \"%~f0\" >nul 2>nul\r\n" +
                "exit /b 0\r\n" +
                ":fail\r\n" +
                "echo %date% %time% 更新失败，无法覆盖主程序。>>\"%LOG%\"\r\n" +
                "start \"\" \"%DST%\"\r\n" +
                "exit /b 1\r\n";
            File.WriteAllText(scriptPath, script, Encoding.Default);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + scriptPath + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(psi);
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

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "update";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        }

        private static string ConciseError(Exception ex)
        {
            string message = ex == null ? string.Empty : ex.Message;
            WebException web = ex as WebException;
            if (web != null && web.Status != WebExceptionStatus.UnknownError)
            {
                message = "网络请求失败：" + web.Status;
            }
            message = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.IndexOf("\"url\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("\"assets_url\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("\"tag_name\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("{", StringComparison.Ordinal) >= 0)
            {
                message = "GitHub 返回内容解析失败，详情已写入日志。";
            }
            if (string.IsNullOrWhiteSpace(message)) message = "未知错误，详情已写入日志。";
            return message.Length > 160 ? message.Substring(0, 160) + "..." : message;
        }
    }

}
