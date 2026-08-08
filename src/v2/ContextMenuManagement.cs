using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal sealed class ContextMenuEntry
    {
        public string Id { get; set; }
        public string Scene { get; set; }
        public string Name { get; set; }
        public string RawName { get; set; }
        public string Type { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public string Command { get; set; }
        public string Icon { get; set; }
        public string Clsid { get; set; }
        public string SubCommands { get; set; }
        public string DisableValueName { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public bool Enabled { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool ReadOnly { get; set; }
        public string ReadOnlyReason { get; set; }
        public string NameReadStatus { get; set; }
        public bool AdvancedOnly { get; set; }
        [ScriptIgnore]
        public bool DynamicTitleProbeEligible { get; set; }

        [ScriptIgnore]
        public Image SoftwareIcon { get; set; }
        [ScriptIgnore]
        public string SoftwareName { get; set; }
        [ScriptIgnore]
        public string DeclaredVendor { get; set; }
        [ScriptIgnore]
        public string IdentityConfidence { get; set; }
        [ScriptIgnore]
        public string IconSource { get; set; }
        [ScriptIgnore]
        public string IdentityExplanation { get; set; }
        [ScriptIgnore]
        public bool PresentationResolved { get; set; }
        [ScriptIgnore]
        public bool IsThirdParty { get; set; }
        [ScriptIgnore]
        public Image StatusToggleIcon { get { return UiTheme.ToggleImage(Enabled); } }

        public string TechnicalLocation
        {
            get
            {
                string viewText = ChineseDisplayText.RegistryView(View);
                return Hive + "\\" + SubKey + (string.IsNullOrEmpty(viewText) ? string.Empty : "（" + viewText + "）");
            }
        }

        public SoftwarePresentationEvidence PresentationEvidence()
        {
            return new SoftwarePresentationEvidence { DeclaredName = Name, DeclaredVendor = DeclaredVendor, IconValue = Icon, Command = Command, Clsid = Clsid, TechnicalLocation = TechnicalLocation };
        }

        public void ApplyPresentation(SoftwarePresentation presentation)
        {
            if (presentation == null) return;
            SoftwareIcon = presentation.Icon; SoftwareName = presentation.SoftwareName; IdentityConfidence = presentation.Confidence; IconSource = presentation.IconSource; IdentityExplanation = presentation.Explanation;
            IsThirdParty = string.Equals(presentation.Confidence, "Confirmed", StringComparison.OrdinalIgnoreCase);
            if (IsThirdParty && DynamicTitleProbeEligible && ShouldProbeDynamicTitle(Name, RawName))
            {
                string componentPath = FirstExistingPath(IconSource, Command, Icon);
                ContextCommandProbeResult probe = ContextCommandTitleProbe.ProbeIsolated(Clsid, ProbeItemType(Scene), componentPath);
                if (probe != null && !string.IsNullOrWhiteSpace(probe.Title))
                {
                    Name = ChineseDisplayText.ContextMenuName(probe.Title);
                    NameReadStatus = "命令文字来源：" + (string.IsNullOrWhiteSpace(probe.Source) ? "右键扩展" : probe.Source) + "。";
                }
                else if (probe != null && !string.IsNullOrWhiteSpace(probe.Error)) NameReadStatus = probe.Error;
            }
            Name = ChineseDisplayText.EnsureChineseContextMenuName(Name, SoftwareName, Scene);
            PresentationResolved = true;
        }

        private static bool ShouldProbeDynamicTitle(string displayName, string rawName)
        {
            string value = (displayName ?? string.Empty).Trim();
            string lower = value.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value) || value == "名称未识别") return true;
            if (value.EndsWith("右键菜单", StringComparison.Ordinal) || value.EndsWith("右键扩展", StringComparison.Ordinal) || value.EndsWith("右键命令", StringComparison.Ordinal) || value.EndsWith("操作", StringComparison.Ordinal) || value.IndexOf("具体功能未识别", StringComparison.Ordinal) >= 0) return true;
            string raw = (rawName ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(raw) && string.Equals(value, raw, StringComparison.OrdinalIgnoreCase) && raw.IndexOf(' ') < 0 && raw.All(delegate(char character) { return character < 128; });
        }

        private static string ProbeItemType(string scene)
        {
            if ((scene ?? string.Empty).IndexOf("空白处", StringComparison.Ordinal) >= 0) return @"Directory\Background";
            if ((scene ?? string.Empty).IndexOf("文件夹", StringComparison.Ordinal) >= 0) return "Directory";
            if ((scene ?? string.Empty).IndexOf("磁盘", StringComparison.Ordinal) >= 0 || (scene ?? string.Empty).IndexOf("驱动器", StringComparison.Ordinal) >= 0) return "Drive";
            return "*";
        }

        private static string FirstExistingPath(params string[] values)
        {
            foreach (string value in values ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                string text = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                int comma = text.LastIndexOf(',');
                int iconIndex;
                if (comma > 0 && int.TryParse(text.Substring(comma + 1).Trim(), out iconIndex)) text = text.Substring(0, comma).Trim().Trim('"');
                if (File.Exists(text)) return text;
            }
            return string.Empty;
        }
    }

    internal sealed class ContextMenuInventory
    {
        public List<ContextMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class ContextMenuToggleBackup
    {
        public string Mode { get; set; }
        public ActionTarget Target { get; set; }
        public bool ValueExisted { get; set; }
        public string ValueName { get; set; }
        public object Value { get; set; }
        public string ValueKind { get; set; }
    }

    internal sealed class RegistryTreeValueSnapshot
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }
        public string[] TextArray { get; set; }
        public byte[] Bytes { get; set; }
        public long Number { get; set; }
    }

    internal sealed class RegistryTreeSnapshot
    {
        public List<RegistryTreeValueSnapshot> Values { get; set; }
        public Dictionary<string, RegistryTreeSnapshot> Children { get; set; }
    }

    internal sealed class ContextMenuTreeBackup
    {
        public ActionTarget Target { get; set; }
        public bool KeyExisted { get; set; }
        public RegistryTreeSnapshot Snapshot { get; set; }
    }

    internal sealed class ContextMenuInventoryService
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, uint outputCount, IntPtr reserved);

        private sealed class RootDefinition
        {
            public string Scene;
            public string Path;
            public string Type;
        }

        private static readonly RootDefinition[] Roots = new RootDefinition[]
        {
            Root("所有文件", @"Software\Classes\*\shell", "Shell 命令"),
            Root("所有文件", @"Software\Classes\*\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shell", "Shell 命令"),
            Root("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹", @"Software\Classes\Directory\shell", "Shell 命令"),
            Root("文件夹", @"Software\Classes\Directory\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹背景", @"Software\Classes\Directory\Background\shell", "Shell 命令"),
            Root("文件夹背景", @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("桌面背景", @"Software\Classes\DesktopBackground\shell", "Shell 命令"),
            Root("桌面背景", @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("磁盘", @"Software\Classes\Drive\shell", "Shell 命令"),
            Root("磁盘", @"Software\Classes\Drive\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("磁盘拖放", @"Software\Classes\Drive\shellex\DragDropHandlers", "Shell 扩展"),
            Root("文件夹对象", @"Software\Classes\Folder\shell", "Shell 命令"),
            Root("文件夹对象", @"Software\Classes\Folder\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("文件夹拖放", @"Software\Classes\Folder\shellex\DragDropHandlers", "Shell 扩展"),
            Root("快捷方式", @"Software\Classes\lnkfile\shell", "Shell 命令"),
            Root("快捷方式", @"Software\Classes\lnkfile\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("可执行文件", @"Software\Classes\exefile\shell", "Shell 命令"),
            Root("可执行文件", @"Software\Classes\exefile\shellex\ContextMenuHandlers", "Shell 扩展"),
            Root("未知文件", @"Software\Classes\Unknown\shell", "Shell 命令")
        };

        private const string CommandStoreRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";

        private static RootDefinition Root(string scene, string path, string type)
        {
            return new RootDefinition { Scene = scene, Path = path, Type = type };
        }

        public ContextMenuInventory Enumerate()
        {
            List<ContextMenuEntry> entries = new List<ContextMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            {
                foreach (string view in new string[] { "Registry64", "Registry32" })
                {
                    foreach (RootDefinition root in Roots) EnumerateRoot(hive, view, root, entries, warnings, seen);
                    EnumerateRoot(hive, view, Root("命令仓库", CommandStoreRoot, "命令仓库"), entries, warnings, seen, true);
                    EnumerateFileTypes(hive, view, entries, warnings, seen);
                }
            }
            return new ContextMenuInventory
            {
                Entries = entries.OrderBy(delegate(ContextMenuEntry e) { return e.Scene; }).ThenBy(delegate(ContextMenuEntry e) { return e.Name; }).ToList(),
                Warnings = warnings
            };
        }

        private void EnumerateFileTypes(string hive, string view, List<ContextMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget classes = Target(hive, view, @"Software\Classes");
            using (RegistryKey key = Open(classes, "文件类型", warnings))
            {
                if (key == null) return;
                foreach (string name in SafeNames(key))
                {
                    if (!name.StartsWith(".", StringComparison.Ordinal) || name.Length > 24) continue;
                    List<string> owners = new List<string> { name };
                    using (RegistryKey extensionKey = Open(Target(hive, view, @"Software\Classes\" + name), "文件类型", warnings))
                    {
                        string progId = Read(extensionKey, "");
                        if (!string.IsNullOrWhiteSpace(progId) && progId.IndexOf('\\') < 0) owners.Add(progId);
                    }
                    foreach (string owner in owners.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (string suffix in new string[] { @"\shell", @"\shellex\ContextMenuHandlers" })
                        {
                            RootDefinition root = Root("文件类型 " + name, @"Software\Classes\" + owner + suffix, suffix.IndexOf("shellex", StringComparison.OrdinalIgnoreCase) >= 0 ? "Shell 扩展" : "Shell 命令");
                            EnumerateRoot(hive, view, root, entries, warnings, seen, true);
                        }
                    }
                }
            }
        }

        private void EnumerateRoot(string hive, string view, RootDefinition root, List<ContextMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen, bool advancedOnly = false)
        {
            ActionTarget rootTarget = Target(hive, view, root.Path);
            using (RegistryKey key = Open(rootTarget, root.Scene, warnings))
            {
                if (key == null) return;
                foreach (string childName in SafeNames(key))
                {
                    ActionTarget childTarget = Target(hive, view, root.Path + "\\" + childName);
                    string unique = hive + "|" + view + "|" + childTarget.SubKey;
                    if (!seen.Add(unique)) continue;
                    using (RegistryKey child = Open(childTarget, root.Scene, warnings))
                    {
                        if (child == null) continue;
                        bool shellEx = root.Type == "Shell 扩展";
                        string clsid = shellEx ? Read(child, "") : Read(child, "ExplorerCommandHandler");
                        string command = shellEx ? string.Empty : ReadChildDefault(childTarget, "command", warnings);
                        string rawDisplay = First(Read(child, "MUIVerb"), Read(child, ""), childName);
                        string display = FriendlyMenuName(rawDisplay, childName, command);
                        bool hasLegacyDisable = HasValue(child, "LegacyDisable");
                        bool hasProgrammaticDisable = HasValue(child, "ProgrammaticAccessOnly");
                        bool disabled = shellEx ? IsBlocked(hive, view, clsid, warnings) : hasLegacyDisable || hasProgrammaticDisable;
                        bool ambiguousDisable = !shellEx && hasLegacyDisable && hasProgrammaticDisable;
                        entries.Add(new ContextMenuEntry
                        {
                            Id = unique,
                            Scene = root.Scene,
                            Name = display,
                            RawName = rawDisplay,
                            Type = root.Type,
                            Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"),
                            Status = disabled ? "已禁用" : "已启用",
                            Command = command,
                            Icon = ResolveEntryIcon(child, hive, view, root, childName, warnings),
                            Clsid = clsid,
                            SubCommands = Read(child, "SubCommands"),
                            DisableValueName = shellEx ? clsid : (hasProgrammaticDisable ? "ProgrammaticAccessOnly" : "LegacyDisable"),
                            Hive = hive,
                            View = view,
                            SubKey = childTarget.SubKey,
                            Enabled = !disabled,
                            RequiresAdmin = hive == "HKLM",
                            ReadOnly = (shellEx && string.IsNullOrWhiteSpace(clsid)) || ambiguousDisable,
                            ReadOnlyReason = shellEx && string.IsNullOrWhiteSpace(clsid) ? "没有读取到 CLSID，不能安全启停。" : (ambiguousDisable ? "同时存在两个禁用标记，当前版本先保持只读，避免破坏程序的条件显示逻辑。" : string.Empty),
                            DynamicTitleProbeEligible = !string.IsNullOrWhiteSpace(clsid),
                            AdvancedOnly = advancedOnly
                        });
                    }
                }
            }
        }

        internal static string FriendlyMenuName(string raw, string childName, string command)
        {
            string value = (raw ?? string.Empty).Trim();
            string lower = (value + " " + childName + " " + command).ToLowerInvariant();
            if (lower.IndexOf("safe360ext") >= 0) return "360 安全扫描";
            if (lower.IndexOf("softmgrext") >= 0) return "360 软件管家";
            if (lower.IndexOf("qingshellext") >= 0) return "上传到 WPS 云文档";
            if (lower.IndexOf("qingnsecontextmenu") >= 0) return "WPS 云文档操作菜单";
            if (lower.IndexOf("sgshellext") >= 0) return "搜狗右键菜单";
            if (lower.IndexOf("bdeunlock") >= 0 || lower.IndexOf("unlock-bde") >= 0) return "解锁 BitLocker 驱动器";
            if (lower.IndexOf("fvewiz") >= 0 || lower.IndexOf("manage-bde") >= 0) return "管理 BitLocker";
            if (value.StartsWith("@", StringComparison.Ordinal))
            {
                try
                {
                    StringBuilder resolved = new StringBuilder(512);
                    if (SHLoadIndirectString(value, resolved, (uint)resolved.Capacity, IntPtr.Zero) == 0 && resolved.Length > 0) value = resolved.ToString();
                }
                catch { }
            }
            string cleanValue = CleanMenuText(value);
            value = ChineseDisplayText.ContextMenuName(cleanValue);
            if (IsReadableMenuText(value) && (ChineseDisplayText.HasChinese(value) || !string.Equals(value, cleanValue, StringComparison.OrdinalIgnoreCase))) return value;
            string key = (childName ?? string.Empty).Trim();
            string keyLower = key.ToLowerInvariant();
            if (keyLower == "open") return "打开";
            if (keyLower == "runas" || keyLower == "runasuser") return "以管理员身份运行";
            if (keyLower == "edit") return "编辑";
            if (keyLower == "print" || keyLower == "printto") return "打印";
            if (keyLower == "share") return "共享";
            string cleanedKey = ChineseDisplayText.ContextMenuName(CleanMenuText(key));
            if (IsReadableMenuText(cleanedKey) && ChineseDisplayText.HasChinese(cleanedKey)) return cleanedKey;
            string executable = ExtractExecutable(command);
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
                    string description = First(info.FileDescription, info.ProductName);
                    if (IsReadableMenuText(description)) return ChineseDisplayText.EnsureChineseContextMenuName(CleanMenuText(description), description, string.Empty);
                }
                catch { }
            }
            return "第三方软件右键菜单";
        }

        private static string ResolveEntryIcon(RegistryKey child, string hive, string view, RootDefinition root, string childName, List<ScanWarning> warnings)
        {
            string icon = Read(child, "Icon");
            if (!string.IsNullOrWhiteSpace(icon)) return icon;

            string commandStoreId = Read(child, "CommandStore");
            if (string.IsNullOrWhiteSpace(commandStoreId)) commandStoreId = Read(child, "SubCommands");
            if (!string.IsNullOrWhiteSpace(commandStoreId) && commandStoreId.IndexOf(';') < 0)
            {
                ActionTarget commandStore = Target(hive, view, CommandStoreRoot + "\\" + commandStoreId.Trim());
                using (RegistryKey commandKey = Open(commandStore, "命令仓库图标", warnings))
                {
                    icon = Read(commandKey, "Icon");
                    if (!string.IsNullOrWhiteSpace(icon)) return icon;
                }
            }

            if (root.Type == "命令仓库")
            {
                ActionTarget commandStore = Target(hive, view, CommandStoreRoot + "\\" + childName);
                using (RegistryKey commandKey = Open(commandStore, "命令仓库图标", warnings)) return Read(commandKey, "Icon");
            }
            return string.Empty;
        }

        private static string CleanMenuText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&").Trim();
        }

        private static bool IsReadableMenuText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
            if (value.StartsWith("@", StringComparison.Ordinal) || value.IndexOf("System32", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (value.IndexOf("\\", StringComparison.Ordinal) >= 0 || value.IndexOf("{", StringComparison.Ordinal) >= 0) return false;
            return value.Any(delegate(char character) { return char.IsLetter(character) || character > 127; });
        }

        private static string ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string text = Environment.ExpandEnvironmentVariables(command.Trim());
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = text.IndexOf('"', 1);
                return end > 1 ? text.Substring(1, end - 1) : string.Empty;
            }
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe >= 0 ? text.Substring(0, exe + 4).Trim() : string.Empty;
        }

        private static bool IsBlocked(string hive, string view, string clsid, List<ScanWarning> warnings)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return false;
            ActionTarget target = Target(hive, view, @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
            using (RegistryKey key = Open(target, "GUID 屏蔽", warnings))
            {
                return key != null && HasValue(key, clsid);
            }
        }

        private static string ReadChildDefault(ActionTarget target, string child, List<ScanWarning> warnings)
        {
            ActionTarget childTarget = Target(target.Hive, target.View, target.SubKey + "\\" + child);
            using (RegistryKey key = Open(childTarget, "Shell 命令", warnings)) { return Read(key, ""); }
        }

        private static RegistryKey Open(ActionTarget target, string stage, List<ScanWarning> warnings)
        {
            try { return RegistryHelper.OpenSubKey(target, false); }
            catch (Exception ex)
            {
                if (!(ex is SecurityException) && !(ex is UnauthorizedAccessException)) throw;
                warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = RegistryHelper.NativePath(target), ErrorType = ex.GetType().FullName, Message = "访问被系统拒绝，已跳过。" });
                return null;
            }
        }

        private static string[] SafeNames(RegistryKey key) { try { return key.GetSubKeyNames(); } catch { return new string[0]; } }
        private static string Read(RegistryKey key, string name) { try { return key == null ? string.Empty : Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; } }
        private static bool HasValue(RegistryKey key, string name) { try { return key != null && key.GetValueNames().Any(delegate(string item) { return string.Equals(item, name, StringComparison.OrdinalIgnoreCase); }); } catch { return false; } }
        private static string First(params string[] values) { foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value; return string.Empty; }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
    }

    internal sealed class ContextMenuDiscoveryService
    {
        private readonly DataStore store;

        public ContextMenuDiscoveryService(DataStore store)
        {
            this.store = store;
        }

        public ContextMenuInventory Enumerate(bool probePackagedTitles)
        {
            ContextMenuInventory result = new ContextMenuInventoryService().Enumerate();
            AdvancedMenuInventory packagedInventory = new AdvancedMenuInventoryService(store).EnumeratePackagedOnly(probePackagedTitles);
            if (packagedInventory.Warnings != null) result.Warnings.AddRange(packagedInventory.Warnings);
            foreach (AdvancedMenuEntry packaged in packagedInventory.Entries)
            {
                result.Entries.Add(new ContextMenuEntry
                {
                    Id = "Packaged|" + packaged.Id,
                    Scene = PackagedScene(packaged.ItemType),
                    Name = ChineseDisplayText.ContextMenuName(packaged.Name),
                    DeclaredVendor = packaged.PublisherName,
                    RawName = packaged.Name,
                    Type = "现代右键扩展",
                    Scope = packaged.Scope,
                    Status = packaged.Status,
                    Command = packaged.FilePath,
                    Icon = string.IsNullOrWhiteSpace(packaged.CommandIcon) ? packaged.FilePath : packaged.CommandIcon,
                    Clsid = packaged.ValueName,
                    DisableValueName = packaged.ValueName,
                    Hive = packaged.Hive,
                    View = packaged.View,
                    SubKey = packaged.SubKey,
                    Enabled = packaged.Enabled,
                    RequiresAdmin = false,
                    ReadOnly = string.IsNullOrWhiteSpace(packaged.ValueName),
                    ReadOnlyReason = string.IsNullOrWhiteSpace(packaged.ValueName) ? "没有读取到组件编号，不能安全显示或隐藏。" : string.Empty,
                    NameReadStatus = string.IsNullOrWhiteSpace(packaged.CommandTitle) ? packaged.TitleProbeStatus : "已从右键扩展读取资源管理器实际命令文字。",
                    AdvancedOnly = false
                });
            }
            result.Entries = result.Entries.OrderBy(delegate(ContextMenuEntry entry) { return entry.Scene; }).ThenBy(delegate(ContextMenuEntry entry) { return entry.Name; }).ToList();
            return result;
        }

        private static string PackagedScene(string itemType)
        {
            if (string.Equals(itemType, "*", StringComparison.OrdinalIgnoreCase)) return "文件右键";
            if (string.Equals(itemType, "Directory", StringComparison.OrdinalIgnoreCase)) return "文件夹右键";
            if (string.Equals(itemType, @"Directory\Background", StringComparison.OrdinalIgnoreCase)) return "文件夹空白处右键";
            if (string.Equals(itemType, "Drive", StringComparison.OrdinalIgnoreCase)) return "磁盘右键";
            return "文件资源管理器右键";
        }
    }

    internal sealed class ContextMenuMutationService
    {
        private static readonly string[] WritableRoots = new string[]
        {
            @"Software\Classes\*\shell",
            @"Software\Classes\AllFilesystemObjects\shell",
            @"Software\Classes\Directory\shell",
            @"Software\Classes\Directory\Background\shell",
            @"Software\Classes\DesktopBackground\shell",
            @"Software\Classes\Drive\shell",
            @"Software\Classes\Folder\shell",
            @"Software\Classes\lnkfile\shell",
            @"Software\Classes\exefile\shell",
            @"Software\Classes\Unknown\shell",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell"
        };

        private readonly DataStore store;
        public ContextMenuMutationService(DataStore store) { this.store = store; }

        public CleanupBatch SetEnabled(ContextMenuEntry entry, bool enabled)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (entry.ReadOnly) throw new InvalidOperationException(entry.ReadOnlyReason);
            if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。");
            bool shellEx = string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
            ActionTarget target = shellEx
                ? new ActionTarget { Kind = "RestoreContextMenuToggle", Hive = entry.Hive, View = entry.View, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked", ValueName = entry.Clsid }
                : new ActionTarget { Kind = "RestoreContextMenuToggle", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = string.IsNullOrWhiteSpace(entry.DisableValueName) ? "LegacyDisable" : entry.DisableValueName };
            string id = NewBatchId();
            string batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            string backupPath = Path.Combine(batchPath, "context-menu-toggle.json");
            ContextMenuToggleBackup backup = CaptureValue(target, shellEx ? "ShellExBlocked" : "LegacyDisable");
            CleanerEngine.WriteJson(backupPath, backup);
            Apply(target, shellEx, enabled);
            bool actualEnabled = shellEx ? !ValueExists(target) : !ValueExists(target);
            if (actualEnabled != enabled)
            {
                Restore(backupPath);
                throw new InvalidOperationException("写入后复核失败，已尝试回滚。");
            }
            CleanupResult result = new CleanupResult
            {
                Id = 1,
                Title = entry.Name,
                Vendor = "右键管理",
                Category = entry.Scene + " / " + entry.Type,
                ActionKind = enabled ? "EnableContextMenu" : "DisableContextMenu",
                TechnicalLocation = entry.TechnicalLocation,
                Status = "Done",
                Message = enabled ? "右键项已启用。" : "右键项已禁用。",
                Backup = backupPath,
                Target = target
            };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } };
            CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            CleanerEngine.WriteJson(Path.Combine(store.Reports, "context-menu-" + id + ".json"), result);
            return batch;
        }

        public CleanupBatch Edit(ContextMenuEntry entry, string displayName, string icon, string command, string subCommands)
        {
            EnsureWritableEntry(entry);
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("显示名称不能为空。");
            if (string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("右键扩展由组件编号驱动，当前编辑器不改写它的注册结构。");
            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(subCommands) && string.IsNullOrWhiteSpace(entry.Clsid)) throw new ArgumentException("命令、子菜单引用和 ExplorerCommandHandler 不能同时为空。");
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey };
            return MutateTree(target, entry.Name, entry.Scene + " / " + entry.Type, "EditContextMenu", delegate(RegistryKey key)
            {
                key.SetValue("MUIVerb", displayName.Trim(), RegistryValueKind.String);
                SetOrDelete(key, "Icon", icon);
                SetOrDelete(key, "SubCommands", subCommands);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (RegistryKey commandKey = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) commandKey.SetValue("", command.Trim(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteSubKeyTree("command", false);
                }
            }, delegate
            {
                using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
                {
                    return key != null && string.Equals(Convert.ToString(key.GetValue("MUIVerb", "")), displayName.Trim(), StringComparison.Ordinal);
                }
            });
        }

        public CleanupBatch Add(string scene, string rootSubKey, string keyName, string displayName, string icon, string command, string subCommands)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("显示名称不能为空。");
            string safeName = SafeKeyName(string.IsNullOrWhiteSpace(keyName) ? displayName : keyName);
            if (string.IsNullOrWhiteSpace(safeName)) throw new ArgumentException("注册表项名称无效。");
            string normalizedRoot = NormalizeWritableRoot(rootSubKey);
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = "HKCU", View = Environment.Is64BitOperatingSystem ? "Registry64" : "Default", SubKey = normalizedRoot + "\\" + safeName };
            using (RegistryKey existing = RegistryHelper.OpenSubKey(target, false)) if (existing != null) throw new InvalidOperationException("同名菜单项已经存在：" + safeName);
            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(subCommands)) throw new ArgumentException("命令和子菜单引用不能同时为空。");
            return MutateTree(target, displayName.Trim(), scene, "AddContextMenu", delegate(RegistryKey key)
            {
                key.SetValue("MUIVerb", displayName.Trim(), RegistryValueKind.String);
                SetOrDelete(key, "Icon", icon);
                SetOrDelete(key, "SubCommands", subCommands);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    using (RegistryKey commandKey = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) commandKey.SetValue("", command.Trim(), RegistryValueKind.String);
                }
            }, delegate { using (RegistryKey key = RegistryHelper.OpenSubKey(target, false)) return key != null; });
        }

        public CleanupBatch Delete(ContextMenuEntry entry)
        {
            if (entry != null && string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Windows 打包右键扩展只允许显示或隐藏，不能删除应用包注册。");
            EnsureWritableEntry(entry);
            ActionTarget target = new ActionTarget { Kind = "RestoreContextMenuTree", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey };
            ContextMenuTreeBackup backup = CaptureTree(target);
            if (!backup.KeyExisted) throw new InvalidOperationException("目标菜单项已经不存在。");
            string id;
            string batchPath;
            string backupPath;
            PrepareTreeBackup(target, backup, out id, out batchPath, out backupPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) root.DeleteSubKeyTree(target.SubKey, false);
                using (RegistryKey verify = RegistryHelper.OpenSubKey(target, false)) if (verify != null) throw new InvalidOperationException("删除后复核失败。");
                return CompleteTreeBatch(id, batchPath, backupPath, target, entry.Name, entry.Scene + " / " + entry.Type, "DeleteContextMenu", "右键项已备份并删除。");
            }
            catch
            {
                RestoreTree(backupPath);
                throw;
            }
        }

        private CleanupBatch MutateTree(ActionTarget target, string title, string category, string actionKind, Action<RegistryKey> mutation, Func<bool> verify)
        {
            ContextMenuTreeBackup backup = CaptureTree(target);
            string id;
            string batchPath;
            string backupPath;
            PrepareTreeBackup(target, backup, out id, out batchPath, out backupPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) mutation(key);
                if (!verify()) throw new InvalidOperationException("写入后复核失败。");
                return CompleteTreeBatch(id, batchPath, backupPath, target, title, category, actionKind, "右键菜单配置已修改。");
            }
            catch
            {
                RestoreTree(backupPath);
                throw;
            }
        }

        private void PrepareTreeBackup(ActionTarget target, ContextMenuTreeBackup backup, out string id, out string batchPath, out string backupPath)
        {
            id = NewBatchId();
            batchPath = Path.Combine(store.Backups, id);
            Directory.CreateDirectory(batchPath);
            backupPath = Path.Combine(batchPath, "context-menu-tree.json");
            CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch CompleteTreeBatch(string id, string batchPath, string backupPath, ActionTarget target, string title, string category, string actionKind, string message)
        {
            CleanupResult result = new CleanupResult { Id = 1, Title = title, Vendor = "右键管理", Category = category, ActionKind = actionKind, TechnicalLocation = RegistryHelper.NativePath(target), Status = "Done", Message = message, Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } };
            CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch);
            CleanerEngine.WriteJson(Path.Combine(store.Reports, "context-menu-" + id + ".json"), result);
            return batch;
        }

        internal static ContextMenuTreeBackup CaptureTree(ActionTarget target)
        {
            ContextMenuTreeBackup backup = new ContextMenuTreeBackup { Target = target };
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                backup.KeyExisted = key != null;
                if (key != null) backup.Snapshot = CaptureNode(key);
            }
            return backup;
        }

        private static RegistryTreeSnapshot CaptureNode(RegistryKey key)
        {
            RegistryTreeSnapshot node = new RegistryTreeSnapshot { Values = new List<RegistryTreeValueSnapshot>(), Children = new Dictionary<string, RegistryTreeSnapshot>(StringComparer.OrdinalIgnoreCase) };
            foreach (string valueName in key.GetValueNames())
            {
                RegistryValueKind kind = key.GetValueKind(valueName);
                object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryTreeValueSnapshot item = new RegistryTreeValueSnapshot { Name = valueName, Kind = kind.ToString() };
                if (kind == RegistryValueKind.Binary) item.Bytes = value as byte[];
                else if (kind == RegistryValueKind.MultiString) item.TextArray = value as string[];
                else if (kind == RegistryValueKind.DWord || kind == RegistryValueKind.QWord) item.Number = Convert.ToInt64(value);
                else item.Text = Convert.ToString(value);
                node.Values.Add(item);
            }
            foreach (string childName in key.GetSubKeyNames())
            {
                using (RegistryKey child = key.OpenSubKey(childName, false)) if (child != null) node.Children[childName] = CaptureNode(child);
            }
            return node;
        }

        public static bool RestoreTree(string backupPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            ContextMenuTreeBackup backup = serializer.Deserialize<ContextMenuTreeBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreTreeSnapshot(backup);
        }

        internal static bool RestoreTreeSnapshot(ContextMenuTreeBackup backup)
        {
            if (backup == null || backup.Target == null) return false;
            using (RegistryKey root = RegistryHelper.OpenBase(backup.Target.Hive, backup.Target.View, true))
            {
                root.DeleteSubKeyTree(backup.Target.SubKey, false);
                if (backup.KeyExisted)
                {
                    using (RegistryKey key = root.CreateSubKey(backup.Target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) RestoreNode(key, backup.Snapshot);
                }
            }
            using (RegistryKey verify = RegistryHelper.OpenSubKey(backup.Target, false)) return backup.KeyExisted ? verify != null : verify == null;
        }

        internal static void RestoreNode(RegistryKey key, RegistryTreeSnapshot node)
        {
            if (node == null) return;
            foreach (RegistryTreeValueSnapshot item in node.Values ?? new List<RegistryTreeValueSnapshot>())
            {
                RegistryValueKind kind = ParseKind(item.Kind);
                object value = kind == RegistryValueKind.Binary ? (object)(item.Bytes ?? new byte[0]) : kind == RegistryValueKind.MultiString ? (object)(item.TextArray ?? new string[0]) : kind == RegistryValueKind.DWord ? (object)Convert.ToInt32(item.Number) : kind == RegistryValueKind.QWord ? (object)item.Number : (object)(item.Text ?? string.Empty);
                key.SetValue(item.Name ?? string.Empty, value, kind);
            }
            foreach (KeyValuePair<string, RegistryTreeSnapshot> child in node.Children ?? new Dictionary<string, RegistryTreeSnapshot>())
            {
                using (RegistryKey childKey = key.CreateSubKey(child.Key, RegistryKeyPermissionCheck.ReadWriteSubTree)) RestoreNode(childKey, child.Value);
            }
        }

        private static void EnsureWritableEntry(ContextMenuEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。");
            bool regular = WritableRoots.Any(delegate(string root) { return entry.SubKey.Equals(root, StringComparison.OrdinalIgnoreCase) || entry.SubKey.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase); });
            bool shellExtension = entry.SubKey.StartsWith(@"Software\Classes\", StringComparison.OrdinalIgnoreCase) &&
                (entry.SubKey.IndexOf(@"\shellex\ContextMenuHandlers\", StringComparison.OrdinalIgnoreCase) >= 0 || entry.SubKey.IndexOf(@"\shellex\DragDropHandlers\", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!regular && !shellExtension) throw new InvalidOperationException("该注册表位置不在受控编辑范围内。");
        }

        private static string NormalizeWritableRoot(string rootSubKey)
        {
            string match = WritableRoots.FirstOrDefault(delegate(string item) { return string.Equals(item, rootSubKey, StringComparison.OrdinalIgnoreCase); });
            if (match == null) throw new InvalidOperationException("不支持向该位置添加菜单项。");
            return match;
        }

        private static void SetOrDelete(RegistryKey key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) key.DeleteValue(name, false); else key.SetValue(name, value.Trim(), RegistryValueKind.String);
        }

        private static string SafeKeyName(string value)
        {
            string text = (value ?? string.Empty).Trim();
            foreach (char invalid in new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) text = text.Replace(invalid, '_');
            return text.Length > 80 ? text.Substring(0, 80) : text;
        }

        private static string NewBatchId() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }

        internal static ContextMenuToggleBackup CaptureValue(ActionTarget target, string mode)
        {
            ContextMenuToggleBackup backup = new ContextMenuToggleBackup { Mode = mode, Target = target, ValueName = target.ValueName };
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                if (key == null) return backup;
                string actualName = key.GetValueNames().FirstOrDefault(delegate(string name) { return string.Equals(name, target.ValueName, StringComparison.OrdinalIgnoreCase); });
                backup.ValueExisted = actualName != null;
                if (backup.ValueExisted)
                {
                    backup.Value = key.GetValue(actualName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    backup.ValueKind = key.GetValueKind(actualName).ToString();
                }
            }
            return backup;
        }

        private static void Apply(ActionTarget target, bool shellEx, bool enabled)
        {
            using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
            using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (enabled) key.DeleteValue(target.ValueName, false);
                else key.SetValue(target.ValueName, shellEx ? "由流氓软件克星禁用" : string.Empty, RegistryValueKind.String);
            }
        }

        internal static bool SetShellExtensionBlocked(ActionTarget target, bool blocked)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ValueName)) return false;
            Apply(target, true, !blocked);
            return ValueExists(target) == blocked;
        }

        private static bool ValueExists(ActionTarget target)
        {
            using (RegistryKey key = RegistryHelper.OpenSubKey(target, false))
            {
                return key != null && key.GetValueNames().Any(delegate(string name) { return string.Equals(name, target.ValueName, StringComparison.OrdinalIgnoreCase); });
            }
        }

        public static bool Restore(string backupPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            ContextMenuToggleBackup backup = serializer.Deserialize<ContextMenuToggleBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreValueSnapshot(backup);
        }

        internal static bool RestoreValueSnapshot(ContextMenuToggleBackup backup)
        {
            if (backup == null || backup.Target == null) return false;
            using (RegistryKey root = RegistryHelper.OpenBase(backup.Target.Hive, backup.Target.View, true))
            using (RegistryKey key = root.CreateSubKey(backup.Target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (!backup.ValueExisted) key.DeleteValue(backup.ValueName, false);
                else key.SetValue(backup.ValueName, backup.Value ?? string.Empty, ParseKind(backup.ValueKind));
            }
            return ValueExists(backup.Target) == backup.ValueExisted;
        }

        private static RegistryValueKind ParseKind(string value)
        {
            RegistryValueKind kind;
            return Enum.TryParse<RegistryValueKind>(value, out kind) ? kind : RegistryValueKind.String;
        }
    }

    internal sealed class ContextMenuManagerForm : Form
    {
        private readonly DataStore store;
        private readonly BindingList<ContextMenuEntry> rows = new BindingList<ContextMenuEntry>();
        private readonly DataGridView grid = new BufferedDataGridView();
        private readonly Label status = new Label();
        private readonly Label inventorySummary = new Label();
        private readonly Label details = new Label();
        private readonly ModernScrollPanel detailScroll = new ModernScrollPanel();
        private readonly SplitContainer split = new SplitContainer();
        private readonly Button enableButton = new Button();
        private readonly Button disableButton = new Button();
        private readonly Button editButton = new Button();
        private readonly Button addButton = new Button();
        private readonly Button deleteButton = new Button();
        private readonly Button refreshButton = new Button();
        private readonly Button specialButton = new Button();
        private readonly Button advancedButton = new Button();
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly FlowLayoutPanel toolsLayout = new FlowLayoutPanel();
        private ContextMenuInventory inventory;
        private int presentationCandidateCount;
        private bool presentationUiComplete;
        private bool applyingResponsiveLayout;

        internal bool FocusedPresentationComplete
        {
            get { return inventory != null && presentationUiComplete; }
        }

        internal IList<ContextMenuEntry> FocusedEntries
        {
            get { return rows.ToList(); }
        }

        public ContextMenuManagerForm(DataStore store)
        {
            this.store = store;
            Text = "右键菜单管理";
            StartPosition = FormStartPosition.CenterParent;
            // 900 x 500 logical pixels fits within a 1080p work area at 200%.
            // The toolbar reflows below instead of hiding actions in a scroll bar.
            MinimumSize = new Size(900, 500);
            Size = new Size(1180, 720);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Canvas;
            Font = UiTheme.Font(9F, FontStyle.Regular);
            UiTheme.ApplyWindowIdentity(this);
            BuildUi();
            Shown += delegate
            {
                ApplyResponsiveLayout();
                RefreshInventory();
            };
        }

        private void BuildUi()
        {
            rootLayout.Dock = DockStyle.Fill; rootLayout.ColumnCount = 1; rootLayout.RowCount = 4; rootLayout.Padding = new Padding(16);
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(rootLayout);
            TableLayoutPanel heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), BackColor = UiTheme.PrimarySoft, Padding = new Padding(16, 8, 10, 8) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            heading.Controls.Add(new Label { Text = "右键菜单管理", Dock = DockStyle.Fill, Font = UiTheme.Font(17F, FontStyle.Bold), ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            inventorySummary.Text = "正在读取右键菜单…"; inventorySummary.Dock = DockStyle.Fill; inventorySummary.ForeColor = UiTheme.Muted; inventorySummary.TextAlign = ContentAlignment.MiddleLeft;
            heading.Controls.Add(inventorySummary, 0, 1);
            rootLayout.Controls.Add(heading, 0, 0);

            toolsLayout.Dock = DockStyle.Fill; toolsLayout.WrapContents = true; toolsLayout.AutoScroll = false; toolsLayout.Margin = new Padding(0); toolsLayout.Padding = new Padding(0, 7, 0, 7); toolsLayout.BackColor = UiTheme.Canvas;
            UiTheme.ActionButton(refreshButton, "刷新列表", ActionButtonRole.Primary);
            UiTheme.ActionButton(enableButton, "显示选中", ActionButtonRole.Standard); UiTheme.ActionButton(disableButton, "隐藏选中", ActionButtonRole.Warning);
            UiTheme.ActionButton(editButton, "修改名称", ActionButtonRole.Standard); UiTheme.ActionButton(addButton, "添加菜单", ActionButtonRole.Standard); UiTheme.ActionButton(deleteButton, "删除菜单", ActionButtonRole.Danger);
            UiTheme.ActionButton(specialButton, "更多位置", ActionButtonRole.Standard); UiTheme.ActionButton(advancedButton, "系统高级", ActionButtonRole.Standard);
            Button copy = new Button(); UiTheme.ActionButton(copy, "复制信息", ActionButtonRole.Standard);
            Button location = new Button(); UiTheme.ActionButton(location, "技术位置", ActionButtonRole.Standard);
            toolsLayout.Controls.Add(refreshButton); toolsLayout.Controls.Add(enableButton); toolsLayout.Controls.Add(disableButton); toolsLayout.Controls.Add(editButton); toolsLayout.Controls.Add(addButton); toolsLayout.Controls.Add(deleteButton); toolsLayout.Controls.Add(copy); toolsLayout.Controls.Add(specialButton); toolsLayout.Controls.Add(advancedButton); toolsLayout.Controls.Add(location);
            rootLayout.Controls.Add(toolsLayout, 0, 1);
            split.Dock = DockStyle.Fill; split.Orientation = Orientation.Vertical; split.SplitterDistance = 850; split.FixedPanel = FixedPanel.Panel2; split.SplitterWidth = 8;
            grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.DataSource = rows; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.BackgroundColor = UiTheme.Surface; grid.BorderStyle = BorderStyle.None; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowTemplate.Height = 40;
            grid.Columns.Add(new DataGridViewImageColumn { DataPropertyName = "SoftwareIcon", HeaderText = "", Width = 42, MinimumWidth = 42, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ImageLayout = DataGridViewImageCellLayout.Normal, DefaultCellStyle = new DataGridViewCellStyle { NullValue = SoftwarePresentationResolver.PlaceholderIcon } });
            grid.Columns.Add(new DataGridViewImageColumn { DataPropertyName = "StatusToggleIcon", HeaderText = "显示", Width = 64, MinimumWidth = 64, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ImageLayout = DataGridViewImageCellLayout.Normal });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "右键里显示的名称", FillWeight = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Scene", HeaderText = "什么时候会出现", FillWeight = 110 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "SoftwareName", HeaderText = "关联软件", FillWeight = 115 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Scope", HeaderText = "影响范围", FillWeight = 90 });
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
            UiTheme.AttachModernScrollBar(split.Panel1, grid);
            details.AutoSize = false; details.BackColor = UiTheme.Surface; details.ForeColor = UiTheme.Text; details.Font = UiTheme.Font(9F, FontStyle.Regular); details.Padding = new Padding(0, 4, 8, 8); details.TextAlign = ContentAlignment.TopLeft;
            detailScroll.Dock = DockStyle.Fill; detailScroll.SetContent(details);
            CardPanel detailCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 10, 10), BackColor = UiTheme.Surface };
            Label detailTitle = new Label { Text = "项目详情", Dock = DockStyle.Top, Height = 32, Font = UiTheme.Font(11F, FontStyle.Bold), ForeColor = UiTheme.Primary, TextAlign = ContentAlignment.MiddleLeft };
            detailCard.Controls.Add(detailScroll); detailCard.Controls.Add(detailTitle);
            split.Panel2.Padding = new Padding(4, 0, 0, 0); split.Panel2.Controls.Add(detailCard); rootLayout.Controls.Add(split, 0, 2);
            detailScroll.SizeChanged += delegate { ResizeDetailsContent(); };
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = UiTheme.Muted; status.Text = "正在准备枚举。"; rootLayout.Controls.Add(status, 0, 3);
            refreshButton.Click += delegate { RefreshInventory(); };
            grid.SelectionChanged += delegate { ShowDetails(); }; enableButton.Click += delegate { Toggle(true); }; disableButton.Click += delegate { Toggle(false); };
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 1) { grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex]; ContextMenuEntry item = Current(); if (item != null && !item.ReadOnly) Toggle(!item.Enabled); } };
            editButton.Click += delegate { EditCurrent(); }; addButton.Click += delegate { AddNew(); }; deleteButton.Click += delegate { DeleteCurrent(); };
            specialButton.Click += delegate { using (SpecialContextMenuForm form = new SpecialContextMenuForm(store)) form.ShowDialog(this); };
            advancedButton.Click += delegate { using (AdvancedContextMenuForm form = new AdvancedContextMenuForm(store)) form.ShowDialog(this); };
            copy.Click += delegate { if (!string.IsNullOrEmpty(details.Text)) Clipboard.SetText(details.Text); };
            location.Click += delegate { ContextMenuEntry entry = Current(); if (entry != null) { Clipboard.SetText(entry.TechnicalLocation); Process.Start("regedit.exe"); status.Text = "注册表位置已复制，并已打开注册表编辑器。"; } };
            SizeChanged += delegate { ApplyResponsiveLayout(); };
            toolsLayout.SizeChanged += delegate { ApplyResponsiveLayout(); };
        }

        private void ApplyResponsiveLayout()
        {
            if (applyingResponsiveLayout || rootLayout.IsDisposed || toolsLayout.IsDisposed || toolsLayout.ClientSize.Width <= 0) return;
            applyingResponsiveLayout = true;
            try
            {
                int logicalWidth = UiTheme.LogicalPixels(this, Math.Max(1, ClientSize.Width));
                bool compact = logicalWidth < 1020;
                rootLayout.RowStyles[0].Height = UiTheme.DpiPixels(this, compact ? 74 : 82);
                rootLayout.RowStyles[3].Height = UiTheme.DpiPixels(this, 32);
                int required = UiTheme.RequiredFlowLayoutHeight(toolsLayout);
                rootLayout.RowStyles[1].Height = Math.Max(UiTheme.DpiPixels(this, 52), required);
                toolsLayout.MinimumSize = new Size(0, required);
                toolsLayout.Height = required;
                rootLayout.PerformLayout();

                int available = Math.Max(1, split.ClientSize.Width - split.SplitterWidth);
                int desiredPanel2 = UiTheme.DpiPixels(this, compact ? 210 : 235);
                int desiredPanel1 = UiTheme.DpiPixels(this, compact ? 360 : 480);
                split.Panel2MinSize = Math.Min(desiredPanel2, Math.Max(UiTheme.DpiPixels(this, 160), available / 2));
                int maximumPanel1 = Math.Max(1, available - split.Panel2MinSize);
                split.Panel1MinSize = Math.Min(desiredPanel1, maximumPanel1);
                split.SplitterDistance = maximumPanel1;
            }
            finally { applyingResponsiveLayout = false; }
        }

        private void RefreshInventory()
        {
            if (!refreshButton.Enabled) return;
            refreshButton.Enabled = false;
            status.Text = "正在枚举当前用户、所有用户以及 32/64 位右键入口……";
            Task.Factory.StartNew<ContextMenuInventory>(delegate { return EnumerateAllMenus(); }).ContinueWith(delegate(Task<ContextMenuInventory> task)
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    refreshButton.Enabled = true;
                    if (task.IsFaulted)
                    {
                        Exception ex = task.Exception == null ? new InvalidOperationException("未知枚举错误。") : task.Exception.GetBaseException();
                        Logger.Error("枚举右键菜单失败", ex); MessageBox.Show(this, ex.Message, "右键菜单管理", MessageBoxButtons.OK, MessageBoxIcon.Error); return;
                    }
                    inventory = task.Result;
                    foreach (ContextMenuEntry entry in inventory.Entries) { entry.SoftwareIcon = SoftwarePresentationResolver.PlaceholderIcon; entry.SoftwareName = "正在识别…"; entry.PresentationResolved = false; entry.IsThirdParty = false; }
                    List<ContextMenuEntry> presentationCandidates = inventory.Entries.Where(delegate(ContextMenuEntry item) { return !item.AdvancedOnly; }).ToList();
                    presentationCandidateCount = presentationCandidates.Count;
                    presentationUiComplete = false;
                    ApplyFilter();
                    SoftwarePresentationQueue.Hydrate(this, presentationCandidates, delegate { ApplyFilter(); grid.Invalidate(); ShowDetails(); });
                });
            });
        }

        private ContextMenuInventory EnumerateAllMenus()
        {
            return new ContextMenuDiscoveryService(store).Enumerate(true);
        }

        private void ApplyFilter()
        {
            if (inventory == null) return;
            rows.RaiseListChangedEvents = false; rows.Clear();
            foreach (ContextMenuEntry entry in inventory.Entries)
            {
                if (entry.AdvancedOnly || !entry.PresentationResolved || !entry.IsThirdParty) continue;
                rows.Add(entry);
            }
            rows.RaiseListChangedEvents = true; rows.ResetBindings();
            int resolved = inventory.Entries.Count(delegate(ContextMenuEntry item) { return !item.AdvancedOnly && item.PresentationResolved; });
            int visible = rows.Count;
            int enabled = rows.Count(delegate(ContextMenuEntry item) { return item.Enabled; });
            int hiddenSystem = inventory.Entries.Count(delegate(ContextMenuEntry item) { return !item.AdvancedOnly && item.PresentationResolved && !item.IsThirdParty; });
            int hiddenInternal = inventory.Entries.Count - presentationCandidateCount;
            presentationUiComplete = resolved >= presentationCandidateCount;
            inventorySummary.Text = "第三方菜单 " + visible + " 项  ·  已显示 " + enabled + "  ·  已隐藏 " + (visible - enabled) + "  ·  系统内置不显示";
            status.Text = resolved < presentationCandidateCount ? "正在识别软件来源 " + resolved + " / " + presentationCandidateCount + "……" : "已隐藏 " + hiddenSystem + " 项系统菜单、" + hiddenInternal + " 项内部技术记录；" + inventory.Warnings.Count + " 个受保护位置未读取。";
            ShowDetails();
        }

        private ContextMenuEntry Current() { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as ContextMenuEntry; }
        private void ShowDetails()
        {
            ContextMenuEntry e = Current();
            details.Text = e == null ? "请选择一个项目。" : "这是什么\r\n" + e.Name + (string.IsNullOrWhiteSpace(e.NameReadStatus) ? string.Empty : "\r\n" + e.NameReadStatus) + "\r\n\r\n属于哪个软件\r\n" + (string.IsNullOrEmpty(e.SoftwareName) ? "来源未确认" : e.SoftwareName) + "\r\n" + (string.IsNullOrEmpty(e.IdentityExplanation) ? "正在识别软件来源…" : e.IdentityExplanation) + "\r\n\r\n在哪里出现\r\n" + e.Scene + "（" + e.Scope + "）\r\n\r\n显示或隐藏的影响\r\n" + (e.Enabled ? "当前会显示；隐藏后只移除右键入口，不卸载对应软件。" : "当前已隐藏；显示后会恢复右键入口。") + "\r\n\r\n技术详情\r\n原始名称：" + (string.IsNullOrWhiteSpace(e.RawName) ? "无" : e.RawName) + "\r\n类型：" + ChineseDisplayText.ContextMenuType(e.Type) + "\r\n执行命令：" + (string.IsNullOrWhiteSpace(e.Command) ? "无" : e.Command) + "\r\n组件编号：" + (string.IsNullOrWhiteSpace(e.Clsid) ? "无" : e.Clsid) + "\r\n注册表位置：" + e.TechnicalLocation + (e.ReadOnly ? "\r\n只读原因：" + e.ReadOnlyReason : string.Empty);
            ResizeDetailsContent();
            enableButton.Enabled = e != null && !e.ReadOnly && !e.Enabled; disableButton.Enabled = e != null && !e.ReadOnly && e.Enabled;
            editButton.Enabled = e != null && !e.ReadOnly && !string.Equals(e.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase) && !string.Equals(e.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
            deleteButton.Enabled = e != null && !string.Equals(e.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
        }

        private void ResizeDetailsContent()
        {
            int width = Math.Max(220, detailScroll.ContentWidth);
            details.Width = width;
            using (Graphics graphics = details.CreateGraphics())
            {
                SizeF measured = graphics.MeasureString(details.Text + "\r\n", details.Font, Math.Max(120, width - 12));
                details.Height = Math.Max(detailScroll.ClientSize.Height, (int)Math.Ceiling(measured.Height) + 18);
            }
        }

        private void Toggle(bool enabled)
        {
            ContextMenuEntry entry = Current(); if (entry == null) return;
            if (entry.RequiresAdmin && !EnsureAdministrator()) return;
            if (MessageBox.Show(this, "将“" + entry.Name + "”" + (enabled ? "启用" : "禁用") + "？\n\n工具会先保存原值，操作后可在恢复中心还原。", "确认右键菜单操作", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { new ContextMenuMutationService(store).SetEnabled(entry, enabled); RefreshInventory(); status.Text = "已" + (enabled ? "启用：" : "禁用：") + entry.Name + "，恢复记录已生成。"; }
            catch (Exception ex) { Logger.Error("修改右键菜单失败", ex); MessageBox.Show(this, ex.Message, "修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void EditCurrent()
        {
            ContextMenuEntry entry = Current(); if (entry == null) return;
            if (entry.RequiresAdmin && !EnsureAdministrator()) return;
            using (ContextMenuEditorForm form = new ContextMenuEditorForm(entry))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try { new ContextMenuMutationService(store).Edit(entry, form.DisplayName, form.IconText, form.CommandText, form.SubCommandsText); RefreshInventory(); status.Text = "已编辑：" + form.DisplayName + "，恢复记录已生成。"; }
                catch (Exception ex) { Logger.Error("编辑右键菜单失败", ex); MessageBox.Show(this, ex.Message, "编辑失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void AddNew()
        {
            using (ContextMenuEditorForm form = new ContextMenuEditorForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try { new ContextMenuMutationService(store).Add(form.SceneName, form.RootSubKey, form.KeyName, form.DisplayName, form.IconText, form.CommandText, form.SubCommandsText); RefreshInventory(); status.Text = "已添加：" + form.DisplayName + "，恢复记录已生成。"; }
                catch (Exception ex) { Logger.Error("添加右键菜单失败", ex); MessageBox.Show(this, ex.Message, "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void DeleteCurrent()
        {
            ContextMenuEntry entry = Current(); if (entry == null) return;
            if (entry.RequiresAdmin && !EnsureAdministrator()) return;
            if (MessageBox.Show(this, "确定删除“" + entry.Name + "”？\n\n完整注册表结构会先进入恢复中心。", "删除右键菜单", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { new ContextMenuMutationService(store).Delete(entry); RefreshInventory(); status.Text = "已删除：" + entry.Name + "，可在恢复中心还原。"; }
            catch (Exception ex) { Logger.Error("删除右键菜单失败", ex); MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private bool EnsureAdministrator()
        {
            if (AdminUtil.IsAdministrator()) return true;
            if (MessageBox.Show(this, "该项目属于所有用户范围，需要管理员权限。是否请求 Windows 管理员权限？\n\n重启后会重新打开右键管理，不会自动修改项目。没有管理员凭据时仍可选择“否”，继续查看和导出信息。", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                AdminUtil.RelaunchAsAdmin(this, store, new ElevationResumeState { Page = "右键", OpenContextMenu = true });
            return false;
        }
    }

    internal sealed class ContextMenuLocationOption
    {
        public string Scene { get; set; }
        public string RootSubKey { get; set; }
        public override string ToString() { return Scene; }
    }

    internal sealed class ContextMenuEditorForm : Form
    {
        private readonly bool addMode;
        private readonly ComboBox location = new ComboBox();
        private readonly TextBox keyName = new TextBox();
        private readonly TextBox displayName = new TextBox();
        private readonly TextBox icon = new TextBox();
        private readonly TextBox command = new TextBox();
        private readonly TextBox subCommands = new TextBox();

        public string SceneName { get { ContextMenuLocationOption option = location.SelectedItem as ContextMenuLocationOption; return option == null ? string.Empty : option.Scene; } }
        public string RootSubKey { get { ContextMenuLocationOption option = location.SelectedItem as ContextMenuLocationOption; return option == null ? string.Empty : option.RootSubKey; } }
        public string KeyName { get { return keyName.Text.Trim(); } }
        public string DisplayName { get { return displayName.Text.Trim(); } }
        public string IconText { get { return icon.Text.Trim(); } }
        public string CommandText { get { return command.Text.Trim(); } }
        public string SubCommandsText { get { return subCommands.Text.Trim(); } }

        public ContextMenuEditorForm() : this(null) { }

        public ContextMenuEditorForm(ContextMenuEntry entry)
        {
            addMode = entry == null;
            Text = addMode ? "添加右键菜单" : "编辑右键菜单";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 540);
            Size = new Size(760, 590);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9F, FontStyle.Regular);
            UiTheme.ApplyWindowIdentity(this);
            BuildUi();
            if (entry != null)
            {
                displayName.Text = entry.Name;
                icon.Text = entry.Icon;
                command.Text = entry.Command;
                subCommands.Text = entry.SubCommands;
                keyName.Text = entry.SubKey.Substring(entry.SubKey.LastIndexOf('\\') + 1);
                location.Items.Add(new ContextMenuLocationOption { Scene = entry.Scene, RootSubKey = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\')) });
                location.SelectedIndex = 0;
                location.Enabled = false;
                keyName.Enabled = false;
            }
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(22) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); Controls.Add(root);
            location.DropDownStyle = ComboBoxStyle.DropDownList; location.Dock = DockStyle.Fill;
            if (addMode)
            {
                foreach (ContextMenuLocationOption option in Locations()) location.Items.Add(option);
                location.SelectedIndex = 0;
            }
            AddRow(root, 0, "作用位置", location);
            AddRow(root, 1, "内部项名称", keyName);
            AddRow(root, 2, "显示名称", displayName);
            AddRow(root, 3, "图标", icon);
            AddRow(root, 4, "执行命令", command);
            AddRow(root, 5, "子菜单引用", subCommands);
            Label help = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Text = "普通菜单填写执行命令；级联子菜单填写 CommandStore 项名称，多个名称用分号分隔。\r\n图标和子菜单均可留空。添加操作默认写入当前用户，不影响其他账户。", Padding = new Padding(0, 10, 0, 0) };
            root.Controls.Add(help, 1, 6);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button cancel = new Button(); UiTheme.OutlineButton(cancel, "取消", UiTheme.Muted); cancel.DialogResult = DialogResult.Cancel;
            Button ok = new Button(); UiTheme.PrimaryButton(ok, addMode ? "添加" : "保存", UiTheme.Primary); ok.Click += ValidateAndClose;
            actions.Controls.Add(cancel); actions.Controls.Add(ok); root.Controls.Add(actions, 1, 7); AcceptButton = ok; CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel root, int row, string title, Control control)
        {
            root.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Text }, 0, row);
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 8, 0, 8); root.Controls.Add(control, 1, row);
        }

        private void ValidateAndClose(object sender, EventArgs e)
        {
            if (location.SelectedItem == null) { MessageBox.Show(this, "请选择作用位置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (string.IsNullOrWhiteSpace(displayName.Text)) { MessageBox.Show(this, "请输入显示名称。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (string.IsNullOrWhiteSpace(command.Text) && string.IsNullOrWhiteSpace(subCommands.Text)) { MessageBox.Show(this, "执行命令和子菜单引用至少填写一项。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            DialogResult = DialogResult.OK;
        }

        private static ContextMenuLocationOption[] Locations()
        {
            return new ContextMenuLocationOption[]
            {
                Option("所有文件", @"Software\Classes\*\shell"),
                Option("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shell"),
                Option("文件夹", @"Software\Classes\Directory\shell"),
                Option("文件夹背景", @"Software\Classes\Directory\Background\shell"),
                Option("桌面背景", @"Software\Classes\DesktopBackground\shell"),
                Option("磁盘", @"Software\Classes\Drive\shell"),
                Option("文件夹对象", @"Software\Classes\Folder\shell"),
                Option("快捷方式", @"Software\Classes\lnkfile\shell"),
                Option("可执行文件", @"Software\Classes\exefile\shell"),
                Option("未知文件", @"Software\Classes\Unknown\shell"),
                Option("命令仓库", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell")
            };
        }

        private static ContextMenuLocationOption Option(string scene, string root) { return new ContextMenuLocationOption { Scene = scene, RootSubKey = root }; }
    }

    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "关于";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 330);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9F, FontStyle.Regular);
            UiTheme.ApplyWindowIdentity(this);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = AppMeta.ProductName + "  v" + AppMeta.Version, Dock = DockStyle.Fill, Font = UiTheme.Font(17F, FontStyle.Bold), ForeColor = UiTheme.Text }, 0, 0);
            layout.Controls.Add(new Label { Text = "作者：" + AppMeta.AuthorName + "    许可证：MIT", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 1);
            layout.Controls.Add(new Label { Text = "右键菜单管理功能的覆盖范围和交互设计参考 ContextMenuManager。\r\n原作者：蓝点lilac。\r\n\r\n本项目未复制或嵌入其 GPLv3 源码、资源字典和图片，相关功能依据 Windows Shell 与注册表行为独立实现。", Dock = DockStyle.Fill, ForeColor = UiTheme.Text }, 0, 2);
            LinkLabel upstream = new LinkLabel { Text = "打开 BluePointLilac / ContextMenuManager", Dock = DockStyle.Fill, LinkColor = UiTheme.Primary };
            upstream.LinkClicked += delegate { Process.Start(new ProcessStartInfo { FileName = "https://github.com/BluePointLilac/ContextMenuManager", UseShellExecute = true }); };
            layout.Controls.Add(upstream, 0, 3);
            Button close = new Button(); UiTheme.PrimaryButton(close, "关闭", UiTheme.Primary); close.Dock = DockStyle.Right; close.DialogResult = DialogResult.OK; layout.Controls.Add(close, 0, 4); AcceptButton = close;
        }
    }

#if VALIDATION
    internal static class ContextMenuManagementRegression
    {
        private const string TestSubKey = @"Software\Classes\*\shell\CodexRogueCleanerTest_ContextManager";
        private const string AddedSubKey = @"Software\Classes\Directory\Background\shell\CodexRogueCleanerTest_AddedMenu";
        private const string CommandStoreSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell\CodexRogueCleanerTest.Command";
        private const string SubmenuSubKey = @"Software\Classes\Directory\Background\shell\CodexRogueCleanerTest_Submenu";
        private const string ShellExtensionSubKey = @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\CodexRogueCleanerTest_SogouShellExt";
        private const string ShellExtensionClsid = "{C0DE2026-0806-4A20-8A00-50A0B10C0001}";
        private const string ShellExtensionClsidKey = @"Software\Classes\CLSID\{C0DE2026-0806-4A20-8A00-50A0B10C0001}";
        private const string ShellExtensionBlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

        public static List<string> Run(DataStore store)
        {
            List<string> failures = new List<string>();
            List<CleanupBatch> batches = new List<CleanupBatch>();
            ActionTarget target = new ActionTarget { Hive = "HKCU", View = "Registry64", SubKey = TestSubKey };
            try
            {
                if (ContextMenuInventoryService.FriendlyMenuName("Open Folder as PyCharm Project", "", "") != "作为 PyCharm 项目打开文件夹") failures.Add("右键菜单英文标题未转换为中文：PyCharm");
                if (ContextMenuInventoryService.FriendlyMenuName("Edit with PyCharm", "", "") != "使用 PyCharm 编辑") failures.Add("右键菜单英文标题未转换为中文：Edit with PyCharm");
                if (ContextMenuInventoryService.FriendlyMenuName("Notepad++ Context menu", "", "") != "Notepad++ 右键菜单") failures.Add("右键菜单英文标题未转换为中文：Notepad++ Context menu");
                if (ContextMenuInventoryService.FriendlyMenuName("Open Git Bash here", "", "") != "在此处打开 Git Bash") failures.Add("右键菜单英文标题未转换为中文：Git Bash");
                if (ContextMenuInventoryService.FriendlyMenuName("Open Git GUI Here", "", "") != "在此处打开 Git 图形界面") failures.Add("右键菜单英文标题未转换为中文：Git 图形界面");
                if (ChineseDisplayText.SoftwareName("WPS Office") != "WPS / 金山") failures.Add("软件名称未转换为中文用途：WPS Office");
                if (!ChineseDisplayText.HasChinese(ChineseDisplayText.EnsureChineseContextMenuName("Unmapped Plugin Action", "Example Plugin", "所有文件"))) failures.Add("未知英文右键名称没有进入中文安全回退");
                if (ChineseDisplayText.ContextMenuType("Shell 扩展") != "右键扩展" || ChineseDisplayText.ContextMenuType("Shell 命令") != "右键命令") failures.Add("右键菜单技术类型未转换为中文");
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(TestSubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                using (RegistryKey command = key.CreateSubKey("command"))
                {
                    key.SetValue("MUIVerb", "Codex 右键管理回归项", RegistryValueKind.String);
                    command.SetValue("", "cmd.exe /c echo CodexRogueCleanerTest", RegistryValueKind.String);
                }
                ContextMenuEntry entry = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.SubKey.EndsWith("CodexRogueCleanerTest_ContextManager", StringComparison.OrdinalIgnoreCase); });
                if (entry == null) { failures.Add("右键管理枚举未发现测试项。"); return failures; }
                CleanupBatch batch = new ContextMenuMutationService(store).SetEnabled(entry, false); batches.Add(batch);
                ContextMenuEntry disabled = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.Id == entry.Id; });
                if (disabled == null || disabled.Enabled) failures.Add("测试项禁用后复核失败。");
                RestoreBatchResult restored = new CleanerEngine(store).RestoreBatch(batch);
                if (!restored.AllSucceeded) failures.Add("恢复中心未能恢复右键管理测试项。");
                ContextMenuEntry enabled = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.Id == entry.Id; });
                if (enabled == null || !enabled.Enabled) failures.Add("测试项恢复后仍处于禁用状态。");
                new CleanerEngine(store).DeleteBatchRecord(batch); batches.Remove(batch);

                batch = new ContextMenuMutationService(store).Edit(entry, "Codex 已编辑右键项", "shell32.dll,1", "cmd.exe /c echo edited", string.Empty); batches.Add(batch);
                ContextMenuEntry edited = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.Id == entry.Id; });
                if (edited == null || edited.Name != "Codex 已编辑右键项" || edited.Command.IndexOf("echo edited", StringComparison.OrdinalIgnoreCase) < 0) failures.Add("右键项编辑后枚举复核失败。");
                if (!new CleanerEngine(store).RestoreBatch(batch).AllSucceeded) failures.Add("编辑操作未能从恢复中心还原。");
                ContextMenuEntry editRestored = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.Id == entry.Id; });
                if (editRestored == null || editRestored.Name != "Codex 右键管理回归项") failures.Add("编辑恢复后原名称未还原。");
                new CleanerEngine(store).DeleteBatchRecord(batch); batches.Remove(batch);

                using (RegistryKey root = RegistryHelper.OpenBase("HKCU", "Default", true))
                using (RegistryKey handler = root.CreateSubKey(ShellExtensionSubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) handler.SetValue("", ShellExtensionClsid, RegistryValueKind.String);
                using (RegistryKey root = RegistryHelper.OpenBase("HKCU", "Default", true))
                using (RegistryKey clsid = root.CreateSubKey(ShellExtensionClsidKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) clsid.SetValue("", "搜狗右键测试扩展 CodexRogueCleanerTest", RegistryValueKind.String);
                using (RegistryKey root = RegistryHelper.OpenBase("HKCU", "Default", true))
                using (RegistryKey server = root.CreateSubKey(ShellExtensionClsidKey + @"\InprocServer32", RegistryKeyPermissionCheck.ReadWriteSubTree)) server.SetValue("", @"C:\CodexRogueCleanerTest\Sogou\SogouShellExt.dll", RegistryValueKind.String);
                Finding shellFinding = new ScannerEngine().ScanAll(null).FirstOrDefault(delegate(Finding item) { return item.Target != null && string.Equals(item.Target.SourceSubKey, ShellExtensionSubKey, StringComparison.OrdinalIgnoreCase); });
                if (shellFinding == null || shellFinding.ActionKind != "DisableShellExtension") failures.Add("Shell 扩展未进入通用 CLSID 禁用流程。");
                else
                {
                    shellFinding.Selected = true;
                    CleanupBatch shellBatch = new CleanerEngine(store).Clean(new List<Finding> { shellFinding }); batches.Add(shellBatch);
                    ActionTarget blockedTarget = new ActionTarget { Hive = "HKCU", View = "Default", SubKey = ShellExtensionBlockedKey, ValueName = ShellExtensionClsid };
                    if (!RegistryHelper.KeyExists(new ActionTarget { Hive = "HKCU", View = "Default", SubKey = ShellExtensionSubKey }) || !RegistryHelper.ValueExists(blockedTarget)) failures.Add("Shell 扩展清理没有保留原注册项并写入 Blocked。");
                    Finding governed = new ScannerEngine().ScanAll(null).FirstOrDefault(delegate(Finding item) { return item.Target != null && string.Equals(item.Target.SourceSubKey, ShellExtensionSubKey, StringComparison.OrdinalIgnoreCase); });
                    if (governed == null || governed.Status != "已治理" || governed.CanClean) failures.Add("已屏蔽 Shell 扩展没有按“已治理”只读状态显示。");
                    if (!new CleanerEngine(store).RestoreBatch(shellBatch).AllSucceeded) failures.Add("Shell 扩展禁用状态未能从恢复中心还原。");
                    Finding restoredFinding = new ScannerEngine().ScanAll(null).FirstOrDefault(delegate(Finding item) { return item.Target != null && string.Equals(item.Target.SourceSubKey, ShellExtensionSubKey, StringComparison.OrdinalIgnoreCase); });
                    if (RegistryHelper.ValueExists(blockedTarget) || restoredFinding == null || restoredFinding.ActionKind != "DisableShellExtension") failures.Add("Shell 扩展恢复后没有重新进入可治理状态。");
                    new CleanerEngine(store).DeleteBatchRecord(shellBatch); batches.Remove(shellBatch);
                }

                batch = new ContextMenuMutationService(store).Add("文件夹背景", @"Software\Classes\Directory\Background\shell", "CodexRogueCleanerTest_AddedMenu", "Codex 新增菜单", string.Empty, "cmd.exe /c echo added", string.Empty); batches.Add(batch);
                ContextMenuEntry added = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.SubKey.Equals(AddedSubKey, StringComparison.OrdinalIgnoreCase); });
                if (added == null || added.Command.IndexOf("echo added", StringComparison.OrdinalIgnoreCase) < 0) failures.Add("新增右键项未被枚举到。");
                CleanupBatch deleteBatch = new ContextMenuMutationService(store).Delete(added); batches.Add(deleteBatch);
                if (new ContextMenuInventoryService().Enumerate().Entries.Any(delegate(ContextMenuEntry item) { return item.SubKey.Equals(AddedSubKey, StringComparison.OrdinalIgnoreCase); })) failures.Add("右键项删除后仍能枚举到。");
                if (!new CleanerEngine(store).RestoreBatch(deleteBatch).AllSucceeded) failures.Add("删除操作未能从恢复中心还原。");
                if (!new ContextMenuInventoryService().Enumerate().Entries.Any(delegate(ContextMenuEntry item) { return item.SubKey.Equals(AddedSubKey, StringComparison.OrdinalIgnoreCase); })) failures.Add("删除恢复后右键项没有重新出现。");

                CleanupBatch storeBatch = new ContextMenuMutationService(store).Add("命令仓库", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell", "CodexRogueCleanerTest.Command", "Codex 仓库命令", string.Empty, "cmd.exe /c echo store", string.Empty); batches.Add(storeBatch);
                CleanupBatch submenuBatch = new ContextMenuMutationService(store).Add("文件夹背景", @"Software\Classes\Directory\Background\shell", "CodexRogueCleanerTest_Submenu", "Codex 级联菜单", string.Empty, string.Empty, "CodexRogueCleanerTest.Command"); batches.Add(submenuBatch);
                ContextMenuEntry submenu = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.SubKey.Equals(SubmenuSubKey, StringComparison.OrdinalIgnoreCase); });
                ContextMenuEntry storeEntry = new ContextMenuInventoryService().Enumerate().Entries.FirstOrDefault(delegate(ContextMenuEntry item) { return item.SubKey.Equals(CommandStoreSubKey, StringComparison.OrdinalIgnoreCase); });
                if (submenu == null || submenu.SubCommands != "CodexRogueCleanerTest.Command" || storeEntry == null) failures.Add("CommandStore/级联子菜单枚举复核失败。");
            }
            catch (Exception ex) { failures.Add("右键管理回归异常：" + ex); }
            finally
            {
                try { using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) root.DeleteSubKeyTree(TestSubKey, false); } catch { }
                foreach (string subKey in new string[] { AddedSubKey, CommandStoreSubKey, SubmenuSubKey, ShellExtensionSubKey, ShellExtensionClsidKey }) try { using (RegistryKey root = RegistryHelper.OpenBase("HKCU", "Default", true)) root.DeleteSubKeyTree(subKey, false); } catch { }
                try { using (RegistryKey root = RegistryHelper.OpenBase("HKCU", "Default", true)) using (RegistryKey key = root.OpenSubKey(ShellExtensionBlockedKey, true)) if (key != null) key.DeleteValue(ShellExtensionClsid, false); } catch { }
                foreach (CleanupBatch cleanupBatch in batches) try { new CleanerEngine(store).DeleteBatchRecord(cleanupBatch); } catch { }
            }
            return failures;
        }
    }
#endif
}
