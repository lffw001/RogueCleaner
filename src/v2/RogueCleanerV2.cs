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
[assembly: AssemblyVersion("2.0.5.0")]
[assembly: AssemblyFileVersion("2.0.5.0")]

namespace RogueCleanerV2
{
    internal static class AppMeta
    {
        public const string ProductName = "流氓软件克星";
        public const string Version = "2.0.5";
        public const string AuthorName = "aakk007";
        public const string Author52PojieUrl = "https://www.52pojie.cn/?286924";
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
#if VALIDATION
            bool acceptance = HasArg(args, "--acceptance-test");
#endif

            try
            {
#if VALIDATION
                if (acceptance)
                {
                    return ValidationRunner.Run(store);
                }
#endif
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

        public bool BulkSelectable
        {
            get { return CanClean && !string.Equals(ActionKind, "InvokeUninstaller", StringComparison.OrdinalIgnoreCase); }
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
            new VendorRule { Name = "搜狗", Snark = "输入法可以输入字，但没必要输入到开机项里。", Boost = 16, Patterns = new [] { "Sogou", "搜狗", "SogouInput", "SogouPY", "SogouExplorer", "SogouCloud", "SogouIme", "SogouImeBroker", "SogouImeMgr", "SogouFlash", "SogouTips", "SogouNews", "SogouPopup", "SogouSvc", "SGImeGuard", "SogouInputPop", "SogouAd", "SogouUpdate", "SogouComMgr", "SGTool", "PinyinUp" }, BadComponents = new [] { "SogouImeBroker", "SogouInput", "SogouExplorer", "SogouFlash", "SogouTips", "SogouAd", "SogouInputPop", "SogouPopup", "SogouNews", "SGImeGuard" } },
            new VendorRule { Name = "迅雷", Snark = "下载器最爱给自己安排开机打卡。", Boost = 20, Patterns = new [] { "Xunlei", "Thunder", "迅雷", "Thunder Network", "XLService", "XLServicePlatform", "ThunderPlatform", "ThunderAgent", "ThunderStart", "ThunderBrowser", "XunleiBHO", "XunleiDownload", "XunleiMedia", "XLB", "XLLiveUD", "XLGameBox", "XMP", "TBCrash", "BrowserEngine", "迅雷下载助手" }, BadComponents = new [] { "XLService", "XLServicePlatform", "ThunderPlatform", "Xunlei.XLB", "ThunderBrowser", "ThunderStart", "XunleiBHO" } },
            new VendorRule { Name = "腾讯系", Snark = "聊天归聊天，别顺手接管浏览器和启动项。", Boost = 12, Patterns = new [] { "Tencent", "腾讯", "QQBrowser", "QQPCMgr", "QQPCMGR", "QQProtect", "QQPCRTP", "QQRepair", "QQShellExt", "TXShell", "TIM.exe", "TIM\\", "WeChat", "微信", "企业微信", "WXWork", "TencentDocs", "腾讯文档", "QQLive", "QQMusic", "TBS", "QBCore", "QBUpdate", "电脑管家" }, BadComponents = new [] { "QQPCMgr", "QQBrowser", "QQProtect", "QQPCRTP", "QQShellExt", "TXShell", "QBUpdate" } },
            new VendorRule { Name = "2345 系列", Snark = "名字像门牌号，行为像钉子户。", Boost = 25, Patterns = new [] { "2345", "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345PicViewer", "2345Kantuwang", "2345Zip", "2345Safe", "2345Protect", "2345Svc", "2345MiniPage", "2345Browser", "2345看图王", "2345好压", "王牌" }, BadComponents = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345Zip", "2345Protect", "2345MiniPage" } },
            new VendorRule { Name = "猎豹 / 金山毒霸", Snark = "安全软件当然能安全，问题是别把自己藏成常驻钉子。", Boost = 18, Patterns = new [] { "Cheetah", "猎豹", "Liebao", "Kingsoft Internet Security", "金山毒霸", "KSafe", "KSafeSvc", "KWatch", "kismain", "kavsrv", "KAV", "KSafeTray", "KMailMon", "KSoft" }, BadComponents = new [] { "KSafeSvc", "KWatch", "kavsrv", "KSafeTray", "Cheetah" } },
            new VendorRule { Name = "驱动/硬件检测工具", Snark = "修驱动可以，常驻当监工就过分了。", Boost = 18, Patterns = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "驱动精灵", "驱动人生", "MyDrivers", "DrvMgr", "DGDaemon", "DTLService", "LuDaShi", "鲁大师", "MasterLu", "LdsLite", "LdsSvc", "LdsDaemon", "ComputerZ", "HardwareProtect" }, BadComponents = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "LuDaShi", "MasterLu", "LdsSvc", "LdsDaemon" } },
            new VendorRule { Name = "国产压缩/看图工具", Snark = "压缩包还没打开，右键先被挤爆了。", Boost = 12, Patterns = new [] { "KuaiZip", "快压", "Kuaizip", "HaoZip", "好压", "2345Zip", "360压缩", "360zip", "2345Pic", "2345看图王", "XnViewShell", "KanPic", "看图王", "极速看图", "JisuPic" }, BadComponents = new [] { "KuaiZip", "Kuaizip", "HaoZip", "2345Zip", "360zip", "2345Pic" } },
            new VendorRule { Name = "国产浏览器/导航", Snark = "浏览器自己跑就行，别把下载、主页和启动项全包了。", Boost = 16, Patterns = new [] { "SogouExplorer", "搜狗高速浏览器", "QQBrowser", "360se", "360Chrome", "2345Explorer", "2345Browser", "Liebao", "猎豹浏览器", "CheetahBrowser", "Maxthon", "傲游", "UCBrowser", "UCBrowser", "TheWorld", "世界之窗", "BaiduBrowser", "百度浏览器" }, BadComponents = new [] { "SogouExplorer", "QQBrowser", "2345Explorer", "CheetahBrowser", "UCService", "BaiduBrowser" } },
            new VendorRule { Name = "Flash 中国特供组件", Snark = "Flash 都退役了，助手还想在后台上班。", Boost = 22, Patterns = new [] { "FlashHelperService", "Flash Center", "FlashCenter", "Flash大厅", "FlashHelper", "FlashRepair", "FlashService", "flash.cn" }, BadComponents = new [] { "FlashHelperService", "FlashCenter", "FlashHelper" } },
            new VendorRule { Name = "手机助手/设备助手", Snark = "连一次手机，后台服务倒是记住一辈子。", Boost = 12, Patterns = new [] { "i4Tools", "爱思助手", "Aisi", "PP助手", "PPAssistant", "91助手", "91Assistant", "Wandoujia", "豌豆荚", "BaiduMobile", "TencentMobileManager", "应用宝", "HiSuite", "华为手机助手", "MiPhoneAssistant", "小米助手" }, BadComponents = new [] { "i4Tools", "PPAssistant", "91Assistant", "Wandoujia", "TencentMobileManager" } },
            new VendorRule { Name = "国产影音/游戏大厅", Snark = "看个视频玩个游戏，不需要抢文件关联和开机席位。", Boost = 10, Patterns = new [] { "iQIYI", "爱奇艺", "Qiyi", "Youku", "优酷", "Kugou", "酷狗", "Kuwo", "酷我", "PPTV", "暴风", "Baofeng", "QQLive", "TencentVideo", "腾讯视频", "XunleiMedia", "XMP", "Bilibili", "芒果TV", "MangoTV", "WeGame", "SteamChina" }, BadComponents = new [] { "iQIYI", "Qiyi", "Youku", "Kugou", "Kuwo", "PPTV", "Baofeng", "QQLive", "TencentVideo" } },
            new VendorRule { Name = "PDF/办公捆绑工具", Snark = "读个 PDF，也别顺手接管全系统打开方式。", Boost = 10, Patterns = new [] { "JisuPDF", "极速PDF", "SwiftPDF", "迅捷PDF", "Foxit", "福昕", "CAJViewer", "CAJ", "PDFReader", "PDFSuite", "PDFMaster", "嗨格式", "HiFormat" }, BadComponents = new [] { "JisuPDF", "SwiftPDF", "PDFMaster", "HiFormat" } },
            new VendorRule { Name = "预装管家/厂商助手", Snark = "出厂自带不等于可以偷偷常驻。", Boost = 8, Patterns = new [] { "LenovoUtility", "LenovoVantage", "联想电脑管家", "LenovoPcManager", "Huawei PC Manager", "华为电脑管家", "HonorPCManager", "荣耀电脑管家", "MiService", "小米电脑管家", "MyASUS", "华硕电脑管家", "AcerCare", "Dell SupportAssist" }, BadComponents = new [] { "LenovoPcManager", "Huawei PC Manager", "HonorPCManager", "MiService", "SupportAssist" } },
            new VendorRule { Name = "弹窗广告/推广组件", Snark = "关掉没一会儿又弹，这类小广告最会装死。", Boost = 22, Patterns = new [] { "SogouNews", "SogouPopup", "SogouTips", "SogouAd", "SogouInputPop", "2345MiniPage", "MiniNews", "HotNews", "NewsPop", "PopNews", "PopWnd", "AdPop", "AdService", "AdPush", "WpsNotify", "KNotify", "BaiduTips", "BaiduNews", "QQBrowserMini", "KugouTips", "KuwoNews", "QiyiNews", "YoukuNews", "LuDaShiNews", "MasterLuMini", "DriverGeniusNews", "KuaiZipNews", "HaoZipMiniPage", "今日热点", "每日热点", "热点资讯", "迷你页", "推荐弹窗", "广告弹窗" }, BadComponents = new [] { "SogouNews", "SogouPopup", "2345MiniPage", "AdPop", "AdService", "WpsNotify", "BaiduTips", "LuDaShiNews", "KuaiZipNews" } },
            new VendorRule { Name = "守护/自动恢复组件", Snark = "你关它一次，它守护进程能把自己续上三回。", Boost = 20, Patterns = new [] { "QHWatchdog", "QHProtected", "QHActiveDefense", "SGImeGuard", "SogouImeBroker", "XLServicePlatform", "ThunderPlatform", "BaiduYunDetect", "YunDetectService", "BaiduNetdiskUtility", "QQProtect", "QQPCRTP", "2345Protect", "2345Svc", "KSafeSvc", "KWatch", "LdsDaemon", "LdsSvc", "FlashHelperService", "FlashCenter", "DriverGeniusDaemon", "DTLService", "LuDaShiDaemon" }, BadComponents = new [] { "QHWatchdog", "QHProtected", "SGImeGuard", "XLServicePlatform", "BaiduYunDetect", "QQProtect", "2345Protect", "KSafeSvc", "LdsDaemon", "FlashHelperService" } }
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

        public static bool HasBadComponent(string text)
        {
            VendorRule rule = ResolveVendorRule(text);
            if (rule == null) return false;
            foreach (string item in rule.BadComponents)
            {
                if (Contains(text, item)) return true;
            }
            return false;
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
            scanners.Add(delegate { AddRange(all, gate, sink, "隐藏卸载入口", ScanHiddenInstalledComponents()); });
            scanners.Add(delegate { AddRange(all, gate, sink, "正在运行的弹窗/守护", ScanRunningAdAndGuardProcesses()); });

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
                            string commandStateHandler = ReadString(child, "CommandStateHandler");
                            string icon = ReadString(child, "Icon");
                            string appliesTo = ReadString(child, "AppliesTo");
                            string command = ReadDefault(target, "command");
                            string clsidText = ResolveClsidRegistration(childName, display, explorerHandler, commandStateHandler);
                            string title = FriendlyContextMenuTitle(target.SubKey, childName, display, mui, explorerHandler, commandStateHandler, clsidText);
                            string text = Join(title, childName, display, mui, explorerHandler, commandStateHandler, command, icon, appliesTo, clsidText, target.SubKey);
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
                        string title = FriendlyStartupTitle(text, valueName, value);
                        list.Add(NewFinding("开机启动", title, "开机后会自动启动：" + title, target, text, 28));
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
                    if (!RuleCatalog.IsKnownVendor(text)) continue;
                    ActionTarget target = new ActionTarget { Kind = "MoveFileToBackup", FilePath = file };
                    list.Add(NewFinding("启动文件夹", Path.GetFileName(file), "开机后会从启动文件夹拉起：" + Join(Path.GetFileName(file), shortcut), target, text, 28));
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
                        string title = FriendlyBrowserTitle(text, valueName);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35));
                    }
                    foreach (string childName in SafeSubKeyNames(key))
                    {
                        ActionTarget target = CopyTarget(root);
                        target.Kind = "DeleteRegistryKey";
                        target.SubKey = root.SubKey + "\\" + childName;
                        string text = Join(childName, root.SubKey);
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        string title = FriendlyBrowserTitle(text, childName);
                        list.Add(NewFinding("浏览器插件/外部宿主", title, "浏览器可能会加载：" + title, target, text, 35));
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
                            if (!RuleCatalog.IsKnownVendor(text)) continue;
                            bool hidden = IsTruthy(systemComponent) ||
                                IsTruthy(noRemove) ||
                                string.IsNullOrWhiteSpace(display) ||
                                string.IsNullOrWhiteSpace(uninstall) ||
                                !string.IsNullOrWhiteSpace(parentKey);
                            bool suspiciousComponent = hidden || LooksLikeAdOrGuard(text) || RuleCatalog.HasBadComponent(text);
                            if (!suspiciousComponent) continue;
                            string name = string.IsNullOrWhiteSpace(display) ? childName : display;
                            string dedupeKey = Join(name, uninstall, installLocation);
                            if (!seen.Add(dedupeKey)) continue;
                            string reason = HiddenInstallReason(display, uninstall, systemComponent, noRemove, parentKey, hidden, LooksLikeAdOrGuard(text), RuleCatalog.HasBadComponent(text));
                            if (!string.IsNullOrWhiteSpace(uninstall))
                            {
                                target.Kind = "InvokeUninstaller";
                                target.UninstallCommand = uninstall;
                                target.FilePath = installLocation;
                                Finding finding = NewFinding("疑似捆绑/弹窗组件", name, "疑似捆绑、弹窗、守护或卸载入口异常：" + reason + "。工具只负责弹出它自己的卸载器，是否卸载由用户在卸载器里确认。", target, text, 16);
                                finding.Risk = RuleCatalog.HasBadComponent(text) || LooksLikeAdOrGuard(text) ? "中" : "低";
                                list.Add(finding);
                            }
                            else
                            {
                                target.Kind = "ReportOnly";
                                Finding finding = NewFinding("疑似捆绑组件/卸载入口异常", name, "安装列表可能不好找或没有正常卸载入口：" + reason + "。只提示，不一键卸载。", target, text, 5);
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
                        if (!RuleCatalog.IsKnownVendor(identity)) continue;
                        if (!LooksLikeAdOrGuard(identity)) continue;
                        ActionTarget target = new ActionTarget { Kind = "ReportOnly", FilePath = Join(name, path, "PID=" + pid) };
                        Finding finding = NewFinding("正在运行/疑似弹窗守护", name, "后台正在运行，像是弹窗、推广、守护或自动恢复组件：" + Join(name, path), target, text, 12);
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
                                if (classKey != null && RuleCatalog.IsKnownVendor(text))
                                {
                                    classTarget.Kind = "ReportOnly";
                                    string title = ext + " 默认打开：" + FriendlyHandler(defaultProgId);
                                    list.Add(NewFinding("文件关联/默认打开程序", title, "双击/打开 " + ext + " 现在会交给：" + FriendlyHandler(defaultProgId) + "。这类属于主打开方式，只提示，不一键改。", classTarget, text, 8));
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
                                    string title = ext + " 打开方式：" + FriendlyHandler(valueName);
                                    list.Add(NewFinding("文件关联/打开方式", title, "右键“打开方式”里会出现：" + FriendlyHandler(valueName) + "（影响 " + ext + " 文件）", valueTarget, text, 22));
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
                        if (!RuleCatalog.IsKnownVendor(text)) continue;
                        ActionTarget target = new ActionTarget { Kind = "DisableService", ServiceName = name };
                        string title = FriendlyServiceTitle(text, name, display);
                        Finding finding = NewFinding("后台服务", title, "后台服务会常驻或被系统拉起：" + title + "。原服务名：" + name, target, text, 42);
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
                    string title = FriendlyTaskTitle(text, name);
                    Finding finding = NewFinding("计划任务/定时拉起", title, "会按计划自动拉起：" + title + "。原任务名：" + name, target, text, 30);
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

        private static string ResolveShortcutText(string file)
        {
            try
            {
                if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return string.Empty;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(file);
                return Join(Convert.ToString(shortcut.TargetPath), Convert.ToString(shortcut.Arguments), Convert.ToString(shortcut.WorkingDirectory));
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
                "popup", "pop", "ad", "advert", "news", "hot", "tips", "tip", "notify", "push", "minipage", "mini",
                "watchdog", "daemon", "guard", "protect", "repair", "restore", "keeper", "serviceplatform",
                "弹窗", "广告", "热点", "资讯", "推荐", "迷你页", "守护", "保护", "修复", "恢复", "推送"
            };
            foreach (string token in tokens)
            {
                if (!string.IsNullOrEmpty(text) && text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
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
            if (lower.IndexOf("sogou") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷右键菜单";
            return ShortVendorName(evidence) + "右键菜单";
        }

        private static string FriendlyStartupTitle(string evidence, string name, string command)
        {
            string lower = Join(evidence, name, command).ToLowerInvariant();
            if (lower.IndexOf("360safetray") >= 0) return "360 安全卫士托盘/防护入口";
            if (lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步启动项";
            if (lower.IndexOf("sogou") >= 0 && LooksLikeAdOrGuard(lower)) return "搜狗弹窗/守护启动项";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷开机启动项";
            string human = FirstHumanText(name, Path.GetFileNameWithoutExtension(ExtractExecutableName(command)));
            return ShortVendorName(evidence) + "开机启动：" + (string.IsNullOrEmpty(human) ? "启动项" : human);
        }

        private static string FriendlyBrowserTitle(string evidence, string rawName)
        {
            string lower = Join(evidence, rawName).ToLowerInvariant();
            if (lower.IndexOf("kingsoft") >= 0 || lower.IndexOf("wps") >= 0) return "WPS/金山浏览器扩展宿主";
            if (lower.IndexOf("baidunetdisk") >= 0) return "百度网盘浏览器扩展宿主";
            if (lower.IndexOf("sogou") >= 0) return "搜狗浏览器扩展/策略";
            if (lower.IndexOf("xunlei") >= 0 || lower.IndexOf("thunder") >= 0) return "迅雷浏览器下载助手";
            if (lower.IndexOf("360") >= 0 || lower.IndexOf("qihoo") >= 0) return "360 浏览器扩展/策略";
            return ShortVendorName(evidence) + "浏览器扩展/宿主";
        }

        private static string FriendlyServiceTitle(string evidence, string name, string display)
        {
            string lower = Join(evidence, name, display).ToLowerInvariant();
            if (lower.IndexOf("q360amppl") >= 0) return "360 安全防护后台服务";
            if (lower.IndexOf("zhudongfangyu") >= 0 || lower.IndexOf("主动防御") >= 0 || lower.IndexOf("qhactivedefense") >= 0) return "360 主动防御后台服务";
            if (lower.IndexOf("baidunetdiskutility") >= 0 || lower.IndexOf("baiduyundetect") >= 0) return "百度网盘检测/同步后台服务";
            if (lower.IndexOf("wps office cloud service") >= 0 || lower.IndexOf("wpscloud") >= 0) return "WPS 云文档后台服务";
            if (lower.IndexOf("sogousvc") >= 0 || lower.IndexOf("sgimeguard") >= 0) return "搜狗输入法守护/更新服务";
            if (lower.IndexOf("xlservice") >= 0 || lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷后台/更新服务";
            string human = FirstHumanText(display, name);
            return ShortVendorName(evidence) + "后台服务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
        }

        private static string FriendlyTaskTitle(string evidence, string name)
        {
            string lower = Join(evidence, name).ToLowerInvariant();
            if (lower.IndexOf("wpsupdate") >= 0 || lower.IndexOf("wpswake") >= 0) return "WPS 更新/唤醒计划任务";
            if (lower.IndexOf("getword") >= 0 || lower.IndexOf("wordsearch") >= 0 || lower.IndexOf("searchfetch") >= 0) return "360 划词/搜索计划任务";
            if (lower.IndexOf("qihoo") >= 0 || lower.IndexOf("360") >= 0) return "360 定时扫描/拉起计划任务";
            if (lower.IndexOf("baiduyun") >= 0 || lower.IndexOf("baidunetdisk") >= 0) return "百度网盘检测/同步计划任务";
            if (lower.IndexOf("sogou") >= 0) return "搜狗更新/弹窗计划任务";
            if (lower.IndexOf("thunder") >= 0 || lower.IndexOf("xunlei") >= 0) return "迅雷更新/拉起计划任务";
            string human = FirstHumanText(name);
            return ShortVendorName(evidence) + "计划任务" + (string.IsNullOrEmpty(human) ? string.Empty : "：" + human);
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
            string backupName = native;
            if (!string.IsNullOrEmpty(target.ValueName)) backupName += "__value__" + target.ValueName;
            string path = Path.Combine(Path.Combine(batchPath, "registry"), SafeFileName(backupName) + ".reg");
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
                bool ok = RestoreResult(result, out message);
                if (ok) summary.Succeeded++;
                else summary.Failed++;
                if (!string.IsNullOrWhiteSpace(message)) summary.Messages.Add(message);
            }
            return summary;
        }

        public bool RestoreResult(CleanupResult result, out string message)
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
                    !string.IsNullOrEmpty(result.Backup) &&
                    result.Backup.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(result.Backup))
                {
                    int exitCode = RunHidden("reg.exe", "import \"" + result.Backup + "\"" + RegistryViewArg(target));
                    bool restored = target.Kind == "DeleteRegistryKey" ? RegistryHelper.KeyExists(target) : RegistryHelper.ValueExists(target);
                    message = result.Title + "：" + (restored ? "注册表已恢复。" : "注册表恢复后复核失败。reg import 退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "MoveFileToBackup" && !string.IsNullOrEmpty(result.Backup) && File.Exists(result.Backup))
                {
                    string dest = Environment.ExpandEnvironmentVariables(target.FilePath);
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (File.Exists(dest))
                    {
                        message = result.Title + "：原位置已经有同名文件，备份已保留，没有覆盖。";
                        return false;
                    }
                    File.Move(result.Backup, dest);
                    bool restored = File.Exists(dest);
                    message = result.Title + "：" + (restored ? "文件已移回原位置。" : "文件恢复后复核失败。");
                    return restored;
                }
                if (target.Kind == "DisableService" && !string.IsNullOrEmpty(result.Backup) && File.Exists(result.Backup))
                {
                    string state = File.ReadAllText(result.Backup, Encoding.UTF8);
                    string start = state.IndexOf("Auto", StringComparison.OrdinalIgnoreCase) >= 0 ? "auto" : (state.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ? "disabled" : "demand");
                    int exitCode = RunHidden("sc.exe", "config \"" + target.ServiceName + "\" start= " + start);
                    string restoredState = GetServiceState(target.ServiceName);
                    bool restored = start == "auto"
                        ? restoredState.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                        : (start == "disabled" ? restoredState.Equals("Disabled", StringComparison.OrdinalIgnoreCase) : restoredState.Equals("Manual", StringComparison.OrdinalIgnoreCase));
                    message = result.Title + "：" + (restored ? "服务启动状态已恢复。" : "服务恢复后复核失败，当前状态 " + restoredState + "，命令退出码 " + exitCode);
                    return exitCode == 0 && restored;
                }
                if (target.Kind == "DisableScheduledTask" && !string.IsNullOrEmpty(result.Backup) && Directory.Exists(result.Backup))
                {
                    string xml = Path.Combine(result.Backup, "task.xml");
                    string stateFile = Path.Combine(result.Backup, "state.txt");
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
            @"Software\Google\Chrome\NativeMessagingHosts\com.codex.roguecleaner.BaiduNetdiskImageViewer",
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexRogueCleanerTest_SogouAdComponent"
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
                Create = delegate { CreateRegistryKey(TestKeys[0], "使用360测试右键菜单", @"cmd.exe /c exit 0"); },
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
                Create = delegate { CreateRegistryKey(TestKeys[1], "WPS图片测试右键菜单", @"cmd.exe /c exit 0"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[1]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[1]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[1]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "磁盘盘符右键：WPS 云盘/磁盘入口测试",
                Vendor = "WPS / 金山",
                Area = "右键菜单",
                Needle = "CodexRogueCleanerTest_kdesktop_WPSDisk",
                Create = delegate { CreateRegistryKey(TestKeys[2], "WPS云盘/磁盘入口测试", @"cmd.exe /c exit 0"); },
                Exists = delegate { return RegistryKeyExists(TestKeys[2]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[2]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[2]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：搜狗弹窗测试项",
                Vendor = "搜狗",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_SogouInputPop",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop", BenignWindowsExecutable()); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_SogouInputPop"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "开机启动：迅雷自启测试项",
                Vendor = "迅雷",
                Area = "开机启动",
                Needle = "CodexRogueCleanerTest_ThunderStart",
                Create = delegate { SetRegistryValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart", BenignWindowsExecutable()); },
                Exists = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart"); },
                Cleaned = delegate { return !RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart"); },
                Restored = delegate { return RegistryValueExists(@"Software\Microsoft\Windows\CurrentVersion\Run", "CodexRogueCleanerTest_ThunderStart"); }
            });
            cases.Add(new ValidationCase
            {
                Name = "浏览器插件/外部宿主：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "浏览器插件/外部宿主",
                Needle = "BaiduNetdiskImageViewer",
                Create = delegate { CreateNativeHostKey(TestKeys[3]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[3]); },
                Cleaned = delegate { return !RegistryKeyExists(TestKeys[3]); },
                Restored = delegate { return RegistryKeyExists(TestKeys[3]); }
            });
            cases.Add(new ValidationCase
            {
                Name = "疑似捆绑组件：不能静默时弹出原厂卸载器",
                Vendor = "搜狗",
                Area = "疑似捆绑/弹窗组件",
                Needle = "CodexRogueCleanerTest_SogouAdComponent",
                ExpectPresentAfterCleanScan = true,
                Create = delegate { CreateUninstallEntry(TestKeys[4]); },
                Exists = delegate { return RegistryKeyExists(TestKeys[4]); },
                Cleaned = delegate { return WaitForFile(UninstallerMarkerPath(), 5000); },
                Restored = delegate { return RegistryKeyExists(TestKeys[4]); }
            });
            cases.Add(new ValidationCase
            {
                Name = ".png 打开方式：百度网盘看图测试项",
                Vendor = "百度 / 百度网盘",
                Area = "文件关联/打开方式",
                Needle = "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open",
                Create = delegate { SetRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open", string.Empty); },
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
            DeleteRegistryValue(@"Software\Classes\.png\OpenWithProgids", "CodexRogueCleanerTest.BaiduNetdiskImageViewer.open");
            try { File.Delete(UninstallerMarkerPath()); } catch { }
            RunProcess("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F");
            if (includeService) RunProcess("sc.exe", "delete \"" + ServiceName + "\"");
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

        private static void CreateUninstallEntry(string keyPath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("DisplayName", "搜狗弹窗组件 " + Marker + "_SogouAdComponent");
                key.SetValue("Publisher", "搜狗");
                key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
                key.SetValue("InstallLocation", Path.Combine(Path.GetTempPath(), Marker));
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
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    key.SetValue(name, value ?? string.Empty);
                }
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
            RunProcess("schtasks.exe", "/Create /SC DAILY /ST " + time + " /TN \"" + TaskName + "\" /TR \"" + BenignWindowsExecutable() + "\" /F");
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

        private static void CreateService()
        {
            RunProcess("sc.exe", "create \"" + ServiceName + "\" binPath= \"cmd.exe /c exit 0\" DisplayName= \"360Safe CodexRogueCleanerTest Service\" start= demand");
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
                return false;
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
        private readonly DataGridView grid = new DataGridView();
        private readonly Label summaryLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label versionLabel = new Label();
        private readonly LinkLabel authorLink = new LinkLabel();
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
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

            versionLabel.Text = "v" + AppMeta.Version;
            versionLabel.ForeColor = Color.White;
            versionLabel.BackColor = Color.FromArgb(13, 148, 136);
            versionLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            versionLabel.TextAlign = ContentAlignment.MiddleCenter;
            versionLabel.AutoSize = false;
            versionLabel.Size = new Size(78, 28);
            versionLabel.Location = new Point(title.Right + 14, 30);
            header.Controls.Add(versionLabel);

            Label sub = new Label();
            sub.Text = "单文件版：多线程扫描，清理后复核，运行数据只进“流氓软件克星数据”。";
            sub.ForeColor = Color.FromArgb(224, 242, 254);
            sub.AutoSize = true;
            sub.Location = new Point(32, 68);
            header.Controls.Add(sub);

            TableLayoutPanel toolArea = new TableLayoutPanel();
            toolArea.Dock = DockStyle.Fill;
            toolArea.RowCount = 2;
            toolArea.ColumnCount = 1;
            toolArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            toolArea.Padding = new Padding(18, 10, 18, 8);
            root.Controls.Add(toolArea, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(0);
            actions.WrapContents = false;
            toolArea.Controls.Add(actions, 0, 0);

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

            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 0, 0, 0);
            progress.Style = ProgressBarStyle.Continuous;
            progress.Visible = false;
            toolArea.Controls.Add(progress, 0, 1);

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
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
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
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            body.Controls.Add(grid, 0, 1);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.BackColor = Color.FromArgb(226, 232, 240);
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.Controls.Add(footer, 0, 3);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(18, 0, 0, 0);
            statusLabel.Text = "就绪。数据目录：" + store.Root;
            footer.Controls.Add(statusLabel, 0, 0);
            authorLink.Dock = DockStyle.Fill;
            authorLink.Text = "作者: " + AppMeta.AuthorName;
            authorLink.TextAlign = ContentAlignment.MiddleRight;
            authorLink.Padding = new Padding(0, 0, 18, 0);
            authorLink.LinkColor = Color.FromArgb(37, 99, 235);
            authorLink.ActiveLinkColor = Color.FromArgb(29, 78, 216);
            authorLink.VisitedLinkColor = Color.FromArgb(37, 99, 235);
            footer.Controls.Add(authorLink, 1, 0);

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
            grid.CellFormatting += GridCellFormatting;
            authorLink.LinkClicked += delegate { OpenAuthorLinks(); };
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
                WrapTooltipLine("用户会看到：", finding.UserVisibleName) + Environment.NewLine +
                WrapTooltipLine("影响：", finding.UserImpact) + Environment.NewLine +
                WrapTooltipLine("处理：", finding.ActionText) + Environment.NewLine +
                WrapTooltipLine("位置：", finding.TechnicalLocation) + Environment.NewLine +
                WrapTooltipLine("证据：", finding.Evidence);
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            if (!string.Equals(column.DataPropertyName, "Risk", StringComparison.OrdinalIgnoreCase)) return;
            string risk = Convert.ToString(e.Value);
            if (risk == "高")
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
            progress.MarqueeAnimationSpeed = busy ? 25 : 0;
            progress.Visible = busy;
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
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "RogueCleaner/" + AppMeta.Version);
                    client.Headers.Add("Accept", "application/vnd.github+json");
                    string json = client.DownloadString(AppMeta.LatestApiUrl);
                    string tag = ExtractJsonString(json, "tag_name");
                    string body = ExtractJsonString(json, "body");
                    string html = ExtractJsonString(json, "html_url");
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        throw new InvalidDataException("GitHub Release 信息缺少版本号。");
                    }
                    if (string.IsNullOrWhiteSpace(html)) html = AppMeta.ReleasesUrl;
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
                    MessageBox.Show(owner, "检查更新失败。\n\n可能是系统 TLS、代理或 GitHub API 限制。\n可以手动打开：" + AppMeta.ReleasesUrl + "\n\n错误：" + ConciseError(ex), "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private static string ExtractJsonString(string json, string name)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name)) return string.Empty;
            string needle = "\"" + name + "\"";
            int pos = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return string.Empty;
            pos = json.IndexOf(':', pos + needle.Length);
            if (pos < 0) return string.Empty;
            pos++;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length || json[pos] != '"') return string.Empty;
            pos++;

            StringBuilder result = new StringBuilder();
            bool escaped = false;
            for (; pos < json.Length; pos++)
            {
                char c = json[pos];
                if (escaped)
                {
                    if (c == 'n') result.Append('\n');
                    else if (c == 'r') result.Append('\r');
                    else if (c == 't') result.Append('\t');
                    else if (c == 'b') result.Append('\b');
                    else if (c == 'f') result.Append('\f');
                    else if (c == 'u' && pos + 4 < json.Length)
                    {
                        string hex = json.Substring(pos + 1, 4);
                        int code;
                        if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            result.Append((char)code);
                            pos += 4;
                        }
                    }
                    else
                    {
                        result.Append(c);
                    }
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == '"') return result.ToString();
                result.Append(c);
            }
            return string.Empty;
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
