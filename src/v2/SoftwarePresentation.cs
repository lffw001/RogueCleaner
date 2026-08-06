using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal sealed class SoftwarePresentationEvidence
    {
        public string DeclaredName { get; set; }
        public string DeclaredVendor { get; set; }
        public string IconValue { get; set; }
        public string Command { get; set; }
        public string FilePath { get; set; }
        public string ServiceName { get; set; }
        public string Clsid { get; set; }
        public string TechnicalLocation { get; set; }
    }

    internal sealed class SoftwarePresentation
    {
        public Image Icon { get; set; }
        public string SoftwareName { get; set; }
        public string Vendor { get; set; }
        public string Confidence { get; set; }
        public string IconSource { get; set; }
        public string Explanation { get; set; }
    }

    internal static class SoftwarePresentationResolver
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr[] largeIcons, IntPtr[] smallIcons, uint iconCount);

        private sealed class IconCandidate
        {
            public string Path;
            public int Index;
            public bool Explicit;
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Image> IconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> RepresentativeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> InstalledRepresentativeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Image fallbackIcon;

        public static Image PlaceholderIcon
        {
            get
            {
                lock (Sync)
                {
                    if (fallbackIcon == null)
                    {
                        try
                        {
                            using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)) fallbackIcon = Resize(icon.ToBitmap(), 20, 20);
                        }
                        catch { fallbackIcon = Resize(SystemIcons.Application.ToBitmap(), 20, 20); }
                    }
                    return fallbackIcon;
                }
            }
        }

        public static SoftwarePresentation Resolve(SoftwarePresentationEvidence evidence)
        {
            evidence = evidence ?? new SoftwarePresentationEvidence();
            IconCandidate declaredIcon = ParseIconCandidate(evidence.IconValue);
            string path = FirstExistingExecutable(evidence.FilePath, evidence.Command);
            string reason = string.Empty;

            if (string.IsNullOrEmpty(path) && declaredIcon != null)
            {
                path = declaredIcon.Path;
                reason = "来自菜单声明的图标资源";
            }

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(evidence.ServiceName))
            {
                path = ResolveServiceBinary(evidence.ServiceName);
                if (!string.IsNullOrEmpty(path)) reason = "来自服务注册信息";
            }
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(evidence.Clsid))
            {
                path = ResolveClsidBinary(evidence.Clsid);
                if (!string.IsNullOrEmpty(path)) reason = "来自右键扩展注册信息";
            }

            string vendor = CleanIdentity(evidence.DeclaredVendor);
            string name = CleanIdentity(evidence.DeclaredName);
            string confidence = "Unknown";
            if (!string.IsNullOrEmpty(path))
            {
                string fileName = Path.GetFileName(path);
                bool windows = IsWindowsBinary(path);
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                    if (string.IsNullOrEmpty(vendor)) vendor = CleanIdentity(info.CompanyName);
                    string product = CleanIdentity(info.ProductName);
                    if (!string.IsNullOrEmpty(product)) name = product;
                }
                catch { }
                if (!windows && IsWindowsAppsBinary(path) && IsMicrosoftVendor(vendor)) windows = true;
                if (windows)
                {
                    if (string.IsNullOrEmpty(vendor)) vendor = "微软 / Windows";
                    name = "Windows 系统组件";
                    confidence = "System";
                }
                else
                {
                    if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(fileName);
                    confidence = "Confirmed";
                }
                if (string.IsNullOrEmpty(reason)) reason = "来自实际执行文件";
            }

            IconCandidate iconCandidate = declaredIcon;
            if (iconCandidate == null && !string.IsNullOrEmpty(path)) iconCandidate = new IconCandidate { Path = path, Index = 0, Explicit = false };
            if (iconCandidate != null && iconCandidate.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !IsWindowsBinary(iconCandidate.Path))
            {
                string representative = FindRepresentativeExecutable(iconCandidate.Path, evidence, name, vendor);
                if (!string.IsNullOrEmpty(representative)) iconCandidate = new IconCandidate { Path = representative, Index = 0, Explicit = false };
            }

            if (string.IsNullOrEmpty(name)) name = "来源未确认";
            if (string.IsNullOrEmpty(vendor)) vendor = confidence == "System" ? "微软 / Windows" : "来源未确认";
            string iconPath = iconCandidate == null ? string.Empty : iconCandidate.Path;
            return new SoftwarePresentation
            {
                Icon = string.IsNullOrEmpty(iconPath) ? PlaceholderIcon : IconFor(iconPath, iconCandidate.Index),
                SoftwareName = ChineseDisplayText.SoftwareName(name),
                Vendor = vendor,
                Confidence = confidence,
                IconSource = iconPath,
                Explanation = string.IsNullOrEmpty(path) ? "没有找到可验证的程序文件，未猜测软件来源" : reason + "：" + path + (string.IsNullOrEmpty(iconPath) || string.Equals(iconPath, path, StringComparison.OrdinalIgnoreCase) ? string.Empty : "；图标取自同软件主程序：" + iconPath)
            };
        }

        private static IconCandidate ParseIconCandidate(string value)
        {
            string path = FirstExistingFile(value);
            if (string.IsNullOrEmpty(path)) return null;
            int index = 0;
            if (!string.IsNullOrWhiteSpace(value))
            {
                Match match = Regex.Match(Environment.ExpandEnvironmentVariables(value), @",\s*(?<i>-?\d+)\s*$");
                if (match.Success) int.TryParse(match.Groups["i"].Value, out index);
            }
            return new IconCandidate { Path = path, Index = index, Explicit = true };
        }

        private static string FindRepresentativeExecutable(string componentPath, SoftwarePresentationEvidence evidence, string softwareName, string vendor)
        {
            lock (Sync)
            {
                string cached;
                if (RepresentativeCache.TryGetValue(componentPath, out cached)) return cached;
            }

            string installed = FindInstalledRepresentativeExecutable(softwareName, vendor, evidence);
            if (!string.IsNullOrEmpty(installed))
            {
                lock (Sync) RepresentativeCache[componentPath] = installed;
                return installed;
            }

            string selected = string.Empty;
            int bestScore = int.MinValue;
            try
            {
                DirectoryInfo directory = new FileInfo(componentPath).Directory;
                string componentName = Path.GetFileNameWithoutExtension(componentPath);
                string evidenceText = JoinEvidence(evidence).ToLowerInvariant();
                for (int level = 0; directory != null && level < 3; level++, directory = directory.Parent)
                {
                    FileInfo[] candidates;
                    try { candidates = directory.EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly).Take(80).ToArray(); }
                    catch { continue; }
                    foreach (FileInfo candidate in candidates)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(candidate.Name);
                        string lower = baseName.ToLowerInvariant();
                        int score = 100 - level * 12;
                        if (string.Equals(baseName, componentName, StringComparison.OrdinalIgnoreCase)) score += 100;
                        if (string.Equals(baseName, directory.Name, StringComparison.OrdinalIgnoreCase)) score += 80;
                        if (evidenceText.IndexOf(lower) >= 0 && lower.Length >= 4) score += 35;
                        if (Regex.IsMatch(lower, "uninst|uninstall|setup|update|helper|crash|report|repair|notify|toast|installer|inst$", RegexOptions.IgnoreCase)) score -= 70;
                        if (score > bestScore) { bestScore = score; selected = candidate.FullName; }
                    }
                }
            }
            catch { selected = string.Empty; }
            lock (Sync) RepresentativeCache[componentPath] = selected;
            return selected;
        }

        private static string FindInstalledRepresentativeExecutable(string softwareName, string vendor, SoftwarePresentationEvidence evidence)
        {
            string cacheKey = NormalizeIdentity(vendor) + "|" + NormalizeIdentity(softwareName);
            lock (Sync)
            {
                string cached;
                if (InstalledRepresentativeCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            string selected = string.Empty;
            int bestScore = 0;
            string evidenceText = softwareName + " " + JoinEvidence(evidence);
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (RegistryView view in Environment.Is64BitOperatingSystem ? new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 } : new RegistryView[] { RegistryView.Default })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false))
                    {
                        if (uninstall == null) continue;
                        foreach (string subKeyName in uninstall.GetSubKeyNames())
                        using (RegistryKey entry = uninstall.OpenSubKey(subKeyName, false))
                        {
                            if (entry == null) continue;
                            string displayName = Convert.ToString(entry.GetValue("DisplayName", string.Empty));
                            string publisher = Convert.ToString(entry.GetValue("Publisher", string.Empty));
                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            int score = PublisherScore(vendor, publisher) + IdentityTokenScore(evidenceText, displayName);
                            if (score < 90 || score < bestScore) continue;
                            string candidate = FirstExistingFile(Convert.ToString(entry.GetValue("DisplayIcon", string.Empty)));
                            if (string.IsNullOrEmpty(candidate) || !candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                candidate = BestExecutableInInstallLocation(Convert.ToString(entry.GetValue("InstallLocation", string.Empty)), displayName);
                            }
                            if (string.IsNullOrEmpty(candidate)) continue;
                            bestScore = score;
                            selected = candidate;
                        }
                    }
                }
                catch { }
            }

            lock (Sync) InstalledRepresentativeCache[cacheKey] = selected;
            return selected;
        }

        private static int PublisherScore(string expected, string actual)
        {
            string left = NormalizeIdentity(expected);
            string right = NormalizeIdentity(actual);
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return 0;
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return 100;
            return left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0 || right.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0 ? 70 : 0;
        }

        private static int IdentityTokenScore(string expected, string actual)
        {
            HashSet<string> left = IdentityTokens(expected);
            HashSet<string> right = IdentityTokens(actual);
            int shared = left.Count(delegate(string token) { return right.Contains(token); });
            return Math.Min(100, shared * 25);
        }

        private static HashSet<string> IdentityTokens(string value)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] ignored = { "shell", "extension", "context", "menu", "handler", "software", "windows", "component", "组件", "菜单", "右键" };
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"[A-Za-z0-9\u4E00-\u9FFF]{2,}"))
            {
                string token = match.Value.ToLowerInvariant();
                if (token.All(delegate(char character) { return char.IsDigit(character); })) continue;
                if (ignored.Contains(token, StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(token);
            }
            return result;
        }

        private static string NormalizeIdentity(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9\u4e00-\u9fff]+", string.Empty);
        }

        private static string BestExecutableInInstallLocation(string installLocation, string displayName)
        {
            if (string.IsNullOrWhiteSpace(installLocation)) return string.Empty;
            string directory = Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"'));
            if (!Directory.Exists(directory)) return string.Empty;
            string selected = string.Empty;
            int bestScore = int.MinValue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Take(80))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    int score = IdentityTokenScore(displayName, name);
                    if (Regex.IsMatch(name, "unins|uninstall|setup|update|helper|crash|report|repair|notify|toast|installer", RegexOptions.IgnoreCase)) score -= 100;
                    try { score += IdentityTokenScore(displayName, FileVersionInfo.GetVersionInfo(file).ProductName); } catch { }
                    if (score > bestScore) { bestScore = score; selected = file; }
                }
            }
            catch { return string.Empty; }
            return bestScore >= 25 ? selected : string.Empty;
        }

        private static string JoinEvidence(SoftwarePresentationEvidence evidence)
        {
            return string.Join(" ", new string[] { evidence.DeclaredName, evidence.DeclaredVendor, evidence.IconValue, evidence.Command, evidence.FilePath, evidence.TechnicalLocation }.Where(delegate(string value) { return !string.IsNullOrWhiteSpace(value); }).ToArray());
        }

        private static string ResolveServiceBinary(string serviceName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    if (key == null) return string.Empty;
                    string imagePath = Convert.ToString(key.GetValue("ImagePath", string.Empty));
                    string resolved = FirstExistingExecutable(imagePath);
                    if (!string.IsNullOrEmpty(resolved) && !string.Equals(Path.GetFileName(resolved), "svchost.exe", StringComparison.OrdinalIgnoreCase)) return resolved;
                    using (RegistryKey parameters = key.OpenSubKey("Parameters", false))
                    {
                        string serviceDll = parameters == null ? string.Empty : Convert.ToString(parameters.GetValue("ServiceDll", string.Empty));
                        string dll = FirstExistingFile(serviceDll);
                        return !string.IsNullOrEmpty(dll) ? dll : resolved;
                    }
                }
            }
            catch { return string.Empty; }
        }

        private static string ResolveClsidBinary(string clsid)
        {
            string clean = clsid.Trim();
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view))
                    {
                        foreach (string leaf in new string[] { "LocalServer32", "InprocServer32" })
                        using (RegistryKey key = root.OpenSubKey("CLSID\\" + clean + "\\" + leaf, false))
                        {
                            string value = key == null ? string.Empty : Convert.ToString(key.GetValue(string.Empty, string.Empty));
                            string path = FirstExistingFile(value);
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        private static string FirstExistingExecutable(params string[] values)
        {
            foreach (string value in values)
            {
                string path = ExtractFile(value, true);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return string.Empty;
        }

        private static string FirstExistingFile(params string[] values)
        {
            foreach (string value in values)
            {
                string path = ExtractFile(value, false);
                if (!string.IsNullOrEmpty(path)) return path;
            }
            return string.Empty;
        }

        private static string ExtractFile(string value, bool executableOnly)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            Match match = Regex.Match(expanded, "(?:\\\"(?<p>[^\\\"]+?\\.(?:exe|dll|ico))\\\"|(?<p>[A-Za-z]:\\\\[^\\r\\n,;]+?\\.(?:exe|dll|ico)))", RegexOptions.IgnoreCase);
            string path = match.Success ? match.Groups["p"].Value : expanded.Trim(' ', '\"');
            int comma = path.LastIndexOf(',');
            if (comma > 2 && Regex.IsMatch(path.Substring(comma + 1), @"^\s*-?\d+\s*$")) path = path.Substring(0, comma).Trim();
            path = path.Trim(' ', '\"');
            if (executableOnly && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            try { return File.Exists(path) ? Path.GetFullPath(path) : string.Empty; }
            catch { return string.Empty; }
        }

        private static Image IconFor(string path, int index)
        {
            string key;
            try { key = path.ToUpperInvariant() + "|" + index + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
            catch { return PlaceholderIcon; }
            lock (Sync)
            {
                Image cached;
                if (IconCache.TryGetValue(key, out cached)) return cached;
                Image image = null;
                try
                {
                    IntPtr[] small = new IntPtr[1];
                    if (ExtractIconEx(path, index, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    {
                        try { using (Icon icon = (Icon)Icon.FromHandle(small[0]).Clone()) image = Resize(icon.ToBitmap(), 20, 20); }
                        finally { DestroyIcon(small[0]); }
                    }
                }
                catch { }
                if (image == null)
                {
                    try { using (Icon icon = Icon.ExtractAssociatedIcon(path)) if (icon != null) image = Resize(icon.ToBitmap(), 20, 20); }
                    catch { }
                }
                if (image == null) image = PlaceholderIcon;
                IconCache[key] = image;
                return image;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Image Resize(Image source, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return bitmap;
        }

        private static bool IsWindowsBinary(string path)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsAppsBinary(string path)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMicrosoftVendor(string vendor)
        {
            return !string.IsNullOrWhiteSpace(vendor) && (vendor.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 || vendor.IndexOf("微软", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string CleanIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Trim();
            if (clean == "未知第三方" || clean == "未知" || clean == "仅提示") return string.Empty;
            string lower = clean.ToLowerInvariant();
            if (lower.IndexOf("todo") >= 0 || lower.IndexOf("<产品名>") >= 0 || lower.IndexOf("<product") >= 0) return string.Empty;
            return clean;
        }
    }

    internal static class SoftwarePresentationQueue
    {
        public static void Hydrate(Form owner, IList<Finding> items, Action repaint)
        {
            Queue(owner, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Form owner, IList<ContextMenuEntry> items, Action repaint)
        {
            Queue(owner, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Form owner, IList<CleanupResult> items, Action repaint)
        {
            Queue(owner, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Form owner, IList<SpecialMenuEntry> items, Action repaint)
        {
            Queue(owner, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        public static void Hydrate(Form owner, IList<AdvancedMenuEntry> items, Action repaint)
        {
            Queue(owner, items.Count, delegate(int index) { items[index].ApplyPresentation(SoftwarePresentationResolver.Resolve(items[index].PresentationEvidence())); }, repaint);
        }

        private static void Queue(Form owner, int count, Action<int> resolver, Action repaint)
        {
            if (owner == null || count == 0) return;
            Task.Factory.StartNew(delegate
            {
                for (int i = 0; i < count; i++)
                {
                    try { resolver(i); } catch { }
                    if ((i + 1) % 24 == 0) Repaint(owner, repaint);
                }
                Repaint(owner, repaint);
            });
        }

        private static void Repaint(Form owner, Action repaint)
        {
            try
            {
                if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke((MethodInvoker)delegate { if (!owner.IsDisposed && repaint != null) repaint(); });
            }
            catch { }
        }
    }

    internal static class SoftwarePresentationRegression
    {
        public static List<string> Run()
        {
            List<string> failures = new List<string>();
            if (ContextMenuInventoryService.FriendlyMenuName("Edit with PyCharm", "", "") != "使用 PyCharm 编辑") failures.Add("中文展示：Edit with PyCharm 未翻译");
            if (ContextMenuInventoryService.FriendlyMenuName("Notepad++ Context menu", "", "") != "Notepad++ 右键菜单") failures.Add("中文展示：Notepad++ Context menu 未翻译");
            if (ChineseDisplayText.SoftwareName("WPS Office") != "WPS / 金山") failures.Add("中文展示：WPS Office 未显示中文软件名");
            if (!ChineseDisplayText.HasChinese(ChineseDisplayText.EnsureChineseContextMenuName("Unmapped Plugin Action", "Example Plugin", "所有文件"))) failures.Add("中文展示：未知英文菜单未进入中文回退");
            if (ProductRemovalPolicy.Classify("搜狗输入法 16.6.0正式版", "Sogou Input", @"D:\Program Files (x86)\SogouInput", "SGTool.exe", "Uninstall.exe", false, false, true) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：搜狗输入法主体被当成附带产品");
            if (ProductRemovalPolicy.Classify("360安全卫士", "360安全卫士", @"D:\Program Files (x86)\360\360Safe", "360Safe.exe", "uninst.exe", false, false, false) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：360 安全卫士主体被当成附带产品");
            if (ProductRemovalPolicy.Classify("WPS Office", "WPS Office", @"D:\Program Files\WPS Office", "wps.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：WPS 主体被当成附带产品");
            if (ProductRemovalPolicy.Classify("百度网盘", "BaiduNetdisk", @"D:\Program Files\BaiduNetdisk", "BaiduNetdisk.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：百度网盘主体被当成附带产品");
            if (ProductRemovalPolicy.Classify("微信", "WeChat", @"D:\Program Files\Tencent\WeChat", "WeChat.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：微信主体被当成附带产品");
            if (ProductRemovalPolicy.Classify("某厂商浏览器", "ExampleBrowser", @"D:\Program Files\ExampleBrowser", "browser.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.Ignore) failures.Add("定向卸载边界：普通独立浏览器仅凭名称被当成流氓附带产品");
            if (ProductRemovalPolicy.Classify("360桌面助手", "360DesktopLite", @"D:\Program Files (x86)\360\DesktopLite", "DesktopLite.exe", "uninst.exe", false, false, false) != ProductRemovalDisposition.TargetIndependentProduct) failures.Add("定向卸载边界：360 桌面独立产品未被识别");
            if (ProductRemovalPolicy.Classify("小鸟壁纸", "BirdWallpaper", @"D:\Program Files\BirdWallpaper", "BirdWallpaper.exe", "uninst.exe", false, false, false) != ProductRemovalDisposition.TargetIndependentProduct) failures.Add("定向卸载边界：小鸟壁纸独立产品未被识别");
            if (ProductRemovalPolicy.Classify("某厂商热点资讯", "ExampleHotNews", @"D:\Program Files\ExampleHotNews", "hotnews.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.TargetIndependentProduct) failures.Add("定向卸载边界：通用独立热点产品未被识别");
            if (ProductRemovalPolicy.Classify("360 游戏大厅", "360GameHall", @"D:\Program Files (x86)\360\GameHall", "gamehall.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.TargetIndependentProduct) failures.Add("定向卸载边界：360 游戏大厅独立产品未被识别");
            if (ProductRemovalPolicy.Classify("YY 游戏中心", "YYGameCenter", @"D:\Program Files (x86)\YY\GameCenter", "gamecenter.exe", "uninstall.exe", false, false, false) != ProductRemovalDisposition.TargetIndependentProduct) failures.Add("定向卸载边界：YY 游戏中心独立产品未被识别");
            if (ProductRemovalPolicy.Classify("", "SogouInputPop", @"D:\Program Files (x86)\SogouInput", "SogouInputPop.exe", "", true, true, true) != ProductRemovalDisposition.ReportComponentOnly) failures.Add("定向卸载边界：无独立卸载项的弹窗组件应只报告");
            if (ProductRemovalPolicy.IsAbnormalPersistence("SogouSvc", @"D:\Program Files (x86)\SogouInput\SogouSvc.exe", false)) failures.Add("持久化边界：搜狗输入法基础服务被当成异常守护服务");
            if (!ProductRemovalPolicy.IsAbnormalPersistence("SGImeGuard", @"D:\Program Files (x86)\SogouInput\SGImeGuard.exe", true)) failures.Add("持久化边界：搜狗守护服务未被识别");
            VendorIdentityResult confirmedMenuVendor = new VendorIdentityResult { Vendor = "WPS / 金山", Confirmed = true, Conflicted = false };
            ContextMenuEntry enabledExtension = new ContextMenuEntry { Scene = "所有文件", Type = "Shell 扩展", Enabled = true, Clsid = "{11111111-1111-1111-1111-111111111111}" };
            if (ContextMenuDiagnosisPolicy.Classify(enabledExtension, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.ActionableExtension) failures.Add("右键诊断一致性：启用的已确认扩展未进入精确禁用");
            ContextMenuEntry disabledExtension = new ContextMenuEntry { Scene = "快捷方式", Type = "Shell 扩展", Enabled = false, Clsid = "{22222222-2222-2222-2222-222222222222}" };
            if (ContextMenuDiagnosisPolicy.Classify(disabledExtension, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.Governed) failures.Add("右键诊断一致性：已禁用扩展未保留为已治理");
            disabledExtension.Enabled = true;
            if (ContextMenuDiagnosisPolicy.Classify(disabledExtension, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.ActionableExtension) failures.Add("右键诊断一致性：重新启用的扩展未恢复为可处理");
            ContextMenuEntry fileTypeCommand = new ContextMenuEntry { Scene = "文件类型 .wps", Type = "Shell 命令", Enabled = true, AdvancedOnly = true, SubKey = @"Software\Classes\.wps\shell\UploadToCloud" };
            if (ContextMenuDiagnosisPolicy.Classify(fileTypeCommand, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.ActionableCommand) failures.Add("右键诊断一致性：文件类型专属菜单未进入诊断桥接");
            ContextMenuEntry coreFileVerb = new ContextMenuEntry { Scene = "文件类型 .wps", Type = "Shell 命令", Enabled = true, AdvancedOnly = true, SubKey = @"Software\Classes\WPS.Document\shell\open" };
            if (ContextMenuDiagnosisPolicy.Classify(coreFileVerb, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.Ignore) failures.Add("右键诊断一致性：文件格式基础打开命令被误判为额外插件");
            ContextMenuEntry modernWithoutClsid = new ContextMenuEntry { Scene = "文件右键", Type = "现代右键扩展", Enabled = true, ReadOnly = true };
            if (ContextMenuDiagnosisPolicy.Classify(modernWithoutClsid, confirmedMenuVendor) != ContextMenuDiagnosisDisposition.ReportOnly) failures.Add("右键诊断一致性：缺少组件编号的现代菜单未降级为只提示");
            VendorIdentityResult unknownMenuVendor = new VendorIdentityResult { Vendor = "未知第三方", Confirmed = false };
            if (ContextMenuDiagnosisPolicy.Classify(enabledExtension, unknownMenuVendor) != ContextMenuDiagnosisDisposition.Ignore) failures.Add("右键诊断一致性：未知来源右键进入了清理链");
            VendorIdentityResult baiduPackaged = RuleCatalog.ResolveIdentity(new VendorEvidence().AddPublisher("北京度友科技有限公司").AddHuman("百度网盘 动态右键命令"));
            if (!baiduPackaged.Confirmed || baiduPackaged.Vendor != "百度 / 百度网盘") failures.Add("右键诊断一致性：百度网盘现代右键的中文发布者未被识别");
            SoftwarePresentation known = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { DeclaredName = AppMeta.ProductName, Command = "\"" + Application.ExecutablePath + "\" --identity-smoke" });
            if (known.Icon == null) failures.Add("软件身份图标：已存在 EXE 未提取到图标");
            if (string.IsNullOrEmpty(known.IconSource) || !File.Exists(known.IconSource)) failures.Add("软件身份图标：已存在 EXE 未保留可验证来源");
            if (known.Confidence == "Unknown") failures.Add("软件身份图标：已存在 EXE 被标为未知");
            SoftwarePresentation unknown = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { DeclaredName = "", DeclaredVendor = "未知第三方", Command = @"Z:\CodexMissing\not-found.exe" });
            if (unknown.Icon == null) failures.Add("软件身份图标：未知来源没有占位图标");
            if (unknown.Confidence != "Unknown" || unknown.SoftwareName != "来源未确认") failures.Add("软件身份图标：未知来源发生猜测性归属");
            string windowsCommand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (!File.Exists(windowsCommand)) windowsCommand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            SoftwarePresentation system = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { Command = windowsCommand });
            if (system.Confidence != "System") failures.Add("软件身份图标：Windows 自带项目未识别为系统组件");

            string testRoot = Path.Combine(Path.GetTempPath(), "RogueCleanerIconRegression-" + Guid.NewGuid().ToString("N"));
            try
            {
                string componentDirectory = Path.Combine(testRoot, "Utils");
                Directory.CreateDirectory(componentDirectory);
                string component = Path.Combine(componentDirectory, "ShellHandler.dll");
                string mainProgram = Path.Combine(testRoot, "ProductMain.exe");
                File.Copy(Application.ExecutablePath, component, true);
                File.Copy(Application.ExecutablePath, mainProgram, true);
                SoftwarePresentation shell = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { DeclaredName = "测试软件右键菜单", Command = component });
                if (!string.Equals(shell.IconSource, mainProgram, StringComparison.OrdinalIgnoreCase)) failures.Add("软件身份图标：Shell 扩展未回退到同软件主程序图标");
                SoftwarePresentation explicitDll = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { DeclaredName = "测试软件右键菜单", Command = component, IconValue = component });
                if (!string.Equals(explicitDll.IconSource, mainProgram, StringComparison.OrdinalIgnoreCase)) failures.Add("软件身份图标：显式 DLL 图标错误阻止了主程序图标回退");
            }
            catch (Exception ex) { failures.Add("软件身份图标：Shell 扩展回归异常：" + ex.GetType().Name); }
            finally
            {
                try { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); } catch { }
            }

            try
            {
                string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                string package = Directory.Exists(windowsApps) ? Directory.GetDirectories(windowsApps, "ScooterSoftware.BeyondCompare.5.ShellExt_*").OrderByDescending(delegate(string value) { return value; }).FirstOrDefault() : string.Empty;
                string component = string.IsNullOrEmpty(package) ? string.Empty : Path.Combine(package, "BcShellEx64.dll");
                if (File.Exists(component))
                {
                    SoftwarePresentation beyond = SoftwarePresentationResolver.Resolve(new SoftwarePresentationEvidence { DeclaredName = "Beyond Compare", DeclaredVendor = "Scooter Software", Command = component, IconValue = component });
                    if (!string.Equals(Path.GetFileName(beyond.IconSource), "BCompare.exe", StringComparison.OrdinalIgnoreCase)) failures.Add("软件身份图标：本机 Beyond Compare 外壳组件未关联到 BCompare.exe");
                }
            }
            catch (Exception ex) { failures.Add("软件身份图标：本机打包外壳图标回归异常：" + ex.GetType().Name); }
            return failures;
        }
    }
}
