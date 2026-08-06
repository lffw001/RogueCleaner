using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

namespace RogueCleanerV2
{
    internal sealed class AdvancedMenuEntry
    {
        public string Id { get; set; }
        public string Module { get; set; }
        public string Name { get; set; }
        public string Detail { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public bool Enabled { get; set; }
        public bool ReadOnly { get; set; }
        public bool RequiresAdmin { get; set; }
        public string Hive { get; set; }
        public string View { get; set; }
        public string SubKey { get; set; }
        public string ValueName { get; set; }
        public string FilePath { get; set; }
        public string Group { get; set; }
        public string PackageName { get; set; }
        public string ItemType { get; set; }
        public int Contexts { get; set; }
    }

    internal sealed class AdvancedMenuInventory
    {
        public List<AdvancedMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class AdvancedFileSnapshot
    {
        public string Path { get; set; }
        public bool Existed { get; set; }
        public byte[] Bytes { get; set; }
        public int Attributes { get; set; }
    }

    internal sealed class AdvancedMenuBackup
    {
        public string Mode { get; set; }
        public List<ContextMenuTreeBackup> Trees { get; set; }
        public List<ContextMenuToggleBackup> Values { get; set; }
        public List<AdvancedFileSnapshot> Files { get; set; }
    }

    internal sealed class EnhancedMenuRecipe
    {
        public string Id;
        public string Name;
        public string Root;
        public string Icon;
        public string Command;
    }

    internal sealed class AdvancedMenuInventoryService
    {
        internal const string IeRoot = @"Software\Microsoft\Internet Explorer\MenuExt";
        internal const string IeDisabledRoot = @"Software\Microsoft\Internet Explorer\MenuExt.RogueCleanerDisabled";
        internal const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private readonly DataStore store;
        private readonly string winxRoot;
        private readonly string ieRoot;
        private readonly string ieDisabledRoot;

        public AdvancedMenuInventoryService(DataStore store) : this(store, null, null, null) { }
        internal AdvancedMenuInventoryService(DataStore store, string winxRootOverride) : this(store, winxRootOverride, null, null) { }
        internal AdvancedMenuInventoryService(DataStore store, string winxRootOverride, string ieRootOverride, string ieDisabledRootOverride)
        {
            this.store = store;
            winxRoot = string.IsNullOrWhiteSpace(winxRootOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\WinX")
                : winxRootOverride;
            ieRoot = string.IsNullOrWhiteSpace(ieRootOverride) ? IeRoot : ieRootOverride;
            ieDisabledRoot = string.IsNullOrWhiteSpace(ieDisabledRootOverride) ? IeDisabledRoot : ieDisabledRootOverride;
        }

        public AdvancedMenuInventory Enumerate()
        {
            List<AdvancedMenuEntry> entries = new List<AdvancedMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            EnumerateWinX(entries, warnings);
            EnumerateIe(entries, warnings);
            EnumeratePackagedMenus(entries, warnings);
            EnumerateRecipes(entries);
            return new AdvancedMenuInventory
            {
                Entries = entries.OrderBy(delegate(AdvancedMenuEntry e) { return e.Module; }).ThenBy(delegate(AdvancedMenuEntry e) { return e.Group; }).ThenBy(delegate(AdvancedMenuEntry e) { return e.Name; }).ToList(),
                Warnings = warnings
            };
        }

        private void EnumerateWinX(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            AddWinXDirectory(winxRoot, true, entries, warnings);
            AddWinXDirectory(DisabledWinXDirectory(store), false, entries, warnings);
        }

        private void AddWinXDirectory(string root, bool enabled, List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (string groupDir in Directory.GetDirectories(root, "Group*"))
                {
                    string group = Path.GetFileName(groupDir);
                    foreach (string file in Directory.GetFiles(groupDir, "*.lnk").OrderBy(delegate(string p) { return Path.GetFileName(p); }))
                    {
                        string target = ShortcutTarget(file);
                        entries.Add(new AdvancedMenuEntry
                        {
                            Id = "WinX|" + (enabled ? "1" : "0") + "|" + file,
                            Module = "WinX 快捷菜单", Name = Path.GetFileNameWithoutExtension(file), Detail = target,
                            Scope = "当前用户 / " + group, Status = enabled ? "已启用" : "已禁用", Enabled = enabled,
                            FilePath = file, Group = group
                        });
                    }
                }
            }
            catch (Exception ex) { AddWarning(warnings, "WinX", root, ex); }
        }

        private void EnumerateIe(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            foreach (string view in Views())
            {
                AddIeRoot(hive, view, ieRoot, true, entries, warnings);
                AddIeRoot(hive, view, ieDisabledRoot, false, entries, warnings);
            }
        }

        private void AddIeRoot(string hive, string view, string rootPath, bool enabled, List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            ActionTarget rootTarget = Target(hive, view, rootPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenSubKey(rootTarget, false))
                {
                    if (root == null) return;
                    foreach (string name in root.GetSubKeyNames())
                    using (RegistryKey key = root.OpenSubKey(name, false))
                    {
                        if (key == null) continue;
                        string url = Convert.ToString(key.GetValue("", ""));
                        int contexts = ToInt(key.GetValue("Contexts", 0));
                        entries.Add(new AdvancedMenuEntry
                        {
                            Id = "IE|" + hive + "|" + view + "|" + rootPath + "|" + name,
                            Module = "IE 旧式菜单", Name = name, Detail = url, Contexts = contexts,
                            Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"),
                            Status = enabled ? "已启用" : "已禁用", Enabled = enabled, RequiresAdmin = hive == "HKLM",
                            Hive = hive, View = view, SubKey = rootPath + "\\" + name
                        });
                    }
                }
            }
            catch (Exception ex) { if (IsDenied(ex)) AddWarning(warnings, "IE 旧式菜单", RegistryHelper.NativePath(rootTarget), ex); else throw; }
        }

        private void EnumeratePackagedMenus(List<AdvancedMenuEntry> entries, List<ScanWarning> warnings)
        {
            HashSet<string> packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default))
                using (RegistryKey index = classes.OpenSubKey(@"PackagedCom\ClassIndex", false))
                {
                    if (index != null)
                    foreach (string clsid in index.GetSubKeyNames())
                    using (RegistryKey classKey = index.OpenSubKey(clsid, false))
                        if (classKey != null) foreach (string package in classKey.GetSubKeyNames()) packages.Add(package);
                }
            }
            catch (Exception ex) { AddWarning(warnings, "现代菜单", @"HKCR\PackagedCom\ClassIndex", ex); return; }

            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string package in packages)
            {
                string manifest = Path.Combine(windowsApps, package, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;
                try
                {
                    XmlDocument xml = new XmlDocument(); xml.XmlResolver = null; xml.Load(manifest);
                    XmlNode identity = xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
                    string packageName = identity == null || identity.Attributes["Name"] == null ? package : identity.Attributes["Name"].Value;
                    XmlNodeList verbs = xml.SelectNodes("//*[local-name()='Extension' and @Category='windows.fileExplorerContextMenus']//*[local-name()='ItemType']/*[local-name()='Verb']");
                    foreach (XmlNode verb in verbs)
                    {
                        string clsid = Attribute(verb, "Clsid"); Guid parsed;
                        if (!Guid.TryParse(clsid, out parsed)) continue;
                        string normalized = "{" + parsed.ToString().ToUpperInvariant() + "}";
                        string itemType = verb.ParentNode == null ? string.Empty : Attribute(verb.ParentNode, "Type");
                        string id = package + "|" + normalized + "|" + itemType;
                        if (!seen.Add(id)) continue;
                        bool blocked = RegistryValueExists("HKCU", DefaultView(), BlockedRoot, normalized);
                        entries.Add(new AdvancedMenuEntry
                        {
                            Id = "UWP|" + id, Module = "现代 / UWP 菜单", Name = packageName + " · " + Attribute(verb, "Id"),
                            Detail = normalized + (string.IsNullOrWhiteSpace(itemType) ? string.Empty : " / " + itemType),
                            Scope = "当前用户 / Windows 11", Status = blocked ? "已禁用" : "已启用", Enabled = !blocked,
                            Hive = "HKCU", View = DefaultView(), SubKey = BlockedRoot, ValueName = normalized,
                            PackageName = package, ItemType = itemType
                        });
                    }
                }
                catch (UnauthorizedAccessException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
                catch (System.Security.SecurityException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
                catch (XmlException ex) { AddWarning(warnings, "现代菜单", manifest, ex); }
            }
        }

        private void EnumerateRecipes(List<AdvancedMenuEntry> entries)
        {
            foreach (EnhancedMenuRecipe recipe in Recipes())
            {
                bool installed = RegistryKeyExists("HKCU", DefaultView(), recipe.Root);
                entries.Add(new AdvancedMenuEntry
                {
                    Id = "Recipe|" + recipe.Id, Module = "安全增强菜单", Name = recipe.Name,
                    Detail = installed ? recipe.Command : "内置安全配方，安装前会完整备份目标项。",
                    Scope = "当前用户", Status = installed ? "已安装" : "未安装", Enabled = installed,
                    Hive = "HKCU", View = DefaultView(), SubKey = recipe.Root, ValueName = recipe.Id
                });
            }
        }

        internal static List<EnhancedMenuRecipe> Recipes()
        {
            string exe = Application.ExecutablePath.Replace("\"", string.Empty);
            return new List<EnhancedMenuRecipe>
            {
                new EnhancedMenuRecipe { Id = "CopyPath", Name = "复制完整路径", Root = @"Software\Classes\*\shell\RogueCleaner.CopyPath", Icon = exe + ",0", Command = "\"" + exe + "\" --copy-path \"%1\"" },
                new EnhancedMenuRecipe { Id = "OpenNotepad", Name = "用记事本打开", Root = @"Software\Classes\*\shell\RogueCleaner.OpenNotepad", Icon = "notepad.exe,0", Command = "notepad.exe \"%1\"" },
                new EnhancedMenuRecipe { Id = "CommandPrompt", Name = "在此处打开命令提示符", Root = @"Software\Classes\Directory\Background\shell\RogueCleaner.CommandPrompt", Icon = "cmd.exe,0", Command = "cmd.exe /s /k pushd \"%V\"" }
            };
        }

        internal static string DisabledWinXDirectory(DataStore store) { return Path.Combine(store.State, "winx-disabled"); }
        internal string WinXRoot { get { return winxRoot; } }
        private static string ShortcutTarget(string file) { try { Type type = Type.GetTypeFromProgID("WScript.Shell"); if (type == null) return string.Empty; dynamic shell = Activator.CreateInstance(type); dynamic shortcut = shell.CreateShortcut(file); return Convert.ToString(shortcut.TargetPath); } catch { return string.Empty; } }
        private static string[] Views() { return Environment.Is64BitOperatingSystem ? new string[] { "Registry64", "Registry32" } : new string[] { "Default" }; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
        private static int ToInt(object value) { try { return Convert.ToInt32(value); } catch { return 0; } }
        private static string Attribute(XmlNode node, string name) { return node == null || node.Attributes == null || node.Attributes[name] == null ? string.Empty : node.Attributes[name].Value; }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static bool RegistryKeyExists(string hive, string view, string subKey) { try { using (RegistryKey key = RegistryHelper.OpenSubKey(Target(hive, view, subKey), false)) return key != null; } catch { return false; } }
        private static bool RegistryValueExists(string hive, string view, string subKey, string name) { try { using (RegistryKey key = RegistryHelper.OpenSubKey(Target(hive, view, subKey), false)) return key != null && key.GetValueNames().Any(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); }); } catch { return false; } }
        private static bool IsDenied(Exception ex) { return ex is UnauthorizedAccessException || ex is System.Security.SecurityException; }
        private static void AddWarning(List<ScanWarning> warnings, string stage, string location, Exception ex) { warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = location, ErrorType = ex.GetType().FullName, Message = "无法读取，已跳过：" + ex.Message }); }
    }

    internal sealed class AdvancedContextMenuMutationService
    {
        private readonly DataStore store;
        private readonly string winxRoot;
        private readonly string ieRoot;
        private readonly string ieDisabledRoot;
        public AdvancedContextMenuMutationService(DataStore store) : this(store, null, null, null) { }
        internal AdvancedContextMenuMutationService(DataStore store, string winxRootOverride) : this(store, winxRootOverride, null, null) { }
        internal AdvancedContextMenuMutationService(DataStore store, string winxRootOverride, string ieRootOverride, string ieDisabledRootOverride)
        {
            this.store = store;
            winxRoot = string.IsNullOrWhiteSpace(winxRootOverride) ? new AdvancedMenuInventoryService(store).WinXRoot : winxRootOverride;
            ieRoot = string.IsNullOrWhiteSpace(ieRootOverride) ? AdvancedMenuInventoryService.IeRoot : ieRootOverride;
            ieDisabledRoot = string.IsNullOrWhiteSpace(ieDisabledRootOverride) ? AdvancedMenuInventoryService.IeDisabledRoot : ieDisabledRootOverride;
        }

        public CleanupBatch SetEnabled(AdvancedMenuEntry entry, bool enabled)
        {
            Ensure(entry);
            if (entry.Module == "WinX 快捷菜单") return ToggleWinX(entry, enabled);
            if (entry.Module == "IE 旧式菜单") return ToggleIe(entry, enabled);
            if (entry.Module == "现代 / UWP 菜单") return ToggleUwp(entry, enabled);
            if (entry.Module == "安全增强菜单") return ToggleRecipe(entry, enabled);
            throw new InvalidOperationException("不支持的高级菜单类型。");
        }

        public CleanupBatch Delete(AdvancedMenuEntry entry)
        {
            Ensure(entry);
            if (entry.Module == "WinX 快捷菜单") return DeleteFile(entry);
            if (entry.Module == "IE 旧式菜单") return DeleteTree(entry);
            if (entry.Module == "安全增强菜单") return ToggleRecipe(entry, false);
            throw new InvalidOperationException("现代应用菜单只允许启用或禁用，不修改应用包注册。");
        }

        public CleanupBatch MoveWinX(AdvancedMenuEntry entry, int direction)
        {
            Ensure(entry); if (entry.Module != "WinX 快捷菜单" || !entry.Enabled) throw new InvalidOperationException("只能调整已启用的 WinX 项。");
            string dir = Path.GetDirectoryName(entry.FilePath);
            string[] files = Directory.GetFiles(dir, "*.lnk").OrderBy(delegate(string p) { return Path.GetFileName(p); }, StringComparer.OrdinalIgnoreCase).ToArray();
            int index = Array.FindIndex(files, delegate(string p) { return string.Equals(p, entry.FilePath, StringComparison.OrdinalIgnoreCase); });
            int other = index + direction; if (index < 0 || other < 0 || other >= files.Length) throw new InvalidOperationException("已经位于当前分组边界。");
            string first = files[index], second = files[other]; string[] firstParts = WinXNameParts(first), secondParts = WinXNameParts(second);
            string firstNew = Path.Combine(dir, secondParts[0] + " - " + firstParts[1] + ".lnk"); string secondNew = Path.Combine(dir, firstParts[0] + " - " + secondParts[1] + ".lnk");
            string temp1 = Path.Combine(dir, ".roguecleaner-" + Guid.NewGuid().ToString("N") + ".tmp"); string temp2 = temp1 + "2";
            AdvancedMenuBackup backup = BackupFiles("ReorderWinX", first, second, firstNew, secondNew, temp1, temp2); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                File.Move(first, temp1); File.Move(second, temp2); File.Move(temp1, firstNew); File.Move(temp2, secondNew);
                return Complete(id, batchPath, backupPath, entry, direction < 0 ? "MoveWinXUp" : "MoveWinXDown", "WinX 顺序已调整；重新打开 Win+X 菜单后生效。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        public CleanupBatch AddOrEditIe(AdvancedMenuEntry existing, string name, string url, int contexts)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\\') >= 0) throw new ArgumentException("菜单名称无效。");
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("脚本或页面地址不能为空。");
            string view = Environment.Is64BitOperatingSystem ? "Registry64" : "Default";
            ActionTarget destination = Target(existing == null ? "HKCU" : existing.Hive, existing == null ? view : existing.View, (existing == null ? ieRoot : Parent(existing.SubKey)) + "\\" + name.Trim());
            List<ActionTarget> targets = new List<ActionTarget> { destination };
            if (existing != null && !string.Equals(existing.SubKey, destination.SubKey, StringComparison.OrdinalIgnoreCase)) targets.Add(Target(existing.Hive, existing.View, existing.SubKey));
            AdvancedMenuBackup backup = BackupTrees(existing == null ? "AddIE" : "EditIE", targets.ToArray()); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                if (existing != null && !string.Equals(existing.SubKey, destination.SubKey, StringComparison.OrdinalIgnoreCase)) DeleteIeTree(Target(existing.Hive, existing.View, existing.SubKey));
                RegAdd(destination, string.Empty, url.Trim(), "REG_SZ");
                RegAdd(destination, "Contexts", contexts.ToString(), "REG_DWORD");
                AdvancedMenuEntry resultEntry = existing ?? new AdvancedMenuEntry { Module = "IE 旧式菜单", Name = name, Hive = destination.Hive, View = destination.View, SubKey = destination.SubKey };
                resultEntry.Name = name.Trim(); resultEntry.SubKey = destination.SubKey;
                return Complete(id, batchPath, backupPath, resultEntry, existing == null ? "AddIE" : "EditIE", "IE 旧式菜单配置已保存。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleWinX(AdvancedMenuEntry entry, bool enabled)
        {
            string destinationRoot = enabled ? winxRoot : AdvancedMenuInventoryService.DisabledWinXDirectory(store);
            string destination = Path.Combine(destinationRoot, entry.Group, Path.GetFileName(entry.FilePath));
            if (File.Exists(destination)) throw new InvalidOperationException("目标位置已经存在同名 WinX 项。");
            AdvancedMenuBackup backup = BackupFiles("ToggleWinX", entry.FilePath, destination); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(entry.FilePath, destination); return Complete(id, batchPath, backupPath, entry, enabled ? "EnableWinX" : "DisableWinX", "WinX 项已移动；重新打开 Win+X 菜单后生效。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteFile(AdvancedMenuEntry entry)
        {
            string destination = Path.Combine(store.Backups, "advanced-files", Guid.NewGuid().ToString("N"), Path.GetFileName(entry.FilePath));
            AdvancedMenuBackup backup = BackupFiles("DeleteWinX", entry.FilePath, destination); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Move(entry.FilePath, destination); return Complete(id, batchPath, backupPath, entry, "DeleteWinX", "WinX 项已备份并删除。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleIe(AdvancedMenuEntry entry, bool enabled)
        {
            string name = entry.SubKey.Substring(entry.SubKey.LastIndexOf('\\') + 1);
            ActionTarget active = Target(entry.Hive, entry.View, ieRoot + "\\" + name);
            ActionTarget disabled = Target(entry.Hive, entry.View, ieDisabledRoot + "\\" + name);
            AdvancedMenuBackup backup = BackupTrees("ToggleIE", active, disabled); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { MoveIeTree(enabled ? disabled : active, enabled ? active : disabled); return Complete(id, batchPath, backupPath, entry, enabled ? "EnableIE" : "DisableIE", "IE 旧式菜单状态已修改。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleUwp(AdvancedMenuEntry entry, bool enabled)
        {
            ActionTarget target = Target("HKCU", DefaultView(), AdvancedMenuInventoryService.BlockedRoot); target.ValueName = entry.ValueName;
            AdvancedMenuBackup backup = BackupValues("ToggleUWP", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    if (enabled) key.DeleteValue(target.ValueName, false); else key.SetValue(target.ValueName, entry.PackageName ?? "由流氓软件克星禁用", RegistryValueKind.String);
                return Complete(id, batchPath, backupPath, entry, enabled ? "EnableUWP" : "DisableUWP", "仅修改当前用户的 Shell 扩展屏蔽状态；应用包注册保持不变。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleRecipe(AdvancedMenuEntry entry, bool enabled)
        {
            EnhancedMenuRecipe recipe = AdvancedMenuInventoryService.Recipes().FirstOrDefault(delegate(EnhancedMenuRecipe r) { return r.Id == entry.ValueName; });
            if (recipe == null) throw new InvalidOperationException("找不到内置配方。");
            ActionTarget target = Target("HKCU", DefaultView(), recipe.Root);
            AdvancedMenuBackup backup = BackupTrees("ToggleRecipe", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                {
                    root.DeleteSubKeyTree(target.SubKey, false);
                    if (enabled)
                    using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        key.SetValue("MUIVerb", recipe.Name, RegistryValueKind.String); key.SetValue("Icon", recipe.Icon, RegistryValueKind.String);
                        using (RegistryKey command = key.CreateSubKey("command", RegistryKeyPermissionCheck.ReadWriteSubTree)) command.SetValue("", recipe.Command, RegistryValueKind.String);
                    }
                }
                return Complete(id, batchPath, backupPath, entry, enabled ? "InstallRecipe" : "RemoveRecipe", enabled ? "安全增强菜单已安装。" : "安全增强菜单已移除。");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteTree(AdvancedMenuEntry entry)
        {
            ActionTarget target = Target(entry.Hive, entry.View, entry.SubKey); AdvancedMenuBackup backup = BackupTrees("DeleteIE", target); string backupPath, id, batchPath; Save(backup, out backupPath, out id, out batchPath);
            try { DeleteIeTree(target); return Complete(id, batchPath, backupPath, entry, "DeleteIE", "IE 旧式菜单已备份并删除。"); }
            catch { RestoreBackup(backup); throw; }
        }

        private static void MoveTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source); if (!snapshot.KeyExisted) throw new InvalidOperationException("源注册表项不存在。");
            ContextMenuMutationService.RestoreTreeSnapshot(new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot });
            using (RegistryKey root = RegistryHelper.OpenBase(source.Hive, source.View, true)) root.DeleteSubKeyTree(source.SubKey, false);
        }

        private static void MoveIeTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source); if (!snapshot.KeyExisted) throw new InvalidOperationException("源 IE 菜单项不存在。");
            DeleteIeTree(destination); RestoreIeTree(new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot }); DeleteIeTree(source);
        }

        private AdvancedMenuBackup BackupTrees(string mode, params ActionTarget[] targets) { return new AdvancedMenuBackup { Mode = mode, Trees = targets.Select(ContextMenuMutationService.CaptureTree).ToList(), Values = new List<ContextMenuToggleBackup>(), Files = new List<AdvancedFileSnapshot>() }; }
        private AdvancedMenuBackup BackupValues(string mode, params ActionTarget[] targets) { return new AdvancedMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = targets.Select(delegate(ActionTarget t) { return ContextMenuMutationService.CaptureValue(t, mode); }).ToList(), Files = new List<AdvancedFileSnapshot>() }; }
        private static AdvancedMenuBackup BackupFiles(string mode, params string[] paths) { return new AdvancedMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), Files = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(CaptureFile).ToList() }; }
        private static AdvancedFileSnapshot CaptureFile(string path) { bool exists = File.Exists(path); return new AdvancedFileSnapshot { Path = path, Existed = exists, Bytes = exists ? File.ReadAllBytes(path) : null, Attributes = exists ? (int)File.GetAttributes(path) : 0 }; }

        private void Save(AdvancedMenuBackup backup, out string backupPath, out string id, out string batchPath)
        {
            id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); batchPath = Path.Combine(store.Backups, id); Directory.CreateDirectory(batchPath); backupPath = Path.Combine(batchPath, "advanced-menu.json"); CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch Complete(string id, string batchPath, string backupPath, AdvancedMenuEntry entry, string action, string message)
        {
            ActionTarget target = new ActionTarget { Kind = "RestoreAdvancedMenu", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = entry.ValueName, FilePath = entry.FilePath };
            string location = !string.IsNullOrWhiteSpace(entry.FilePath) ? entry.FilePath : (!string.IsNullOrWhiteSpace(entry.SubKey) ? RegistryHelper.NativePath(target) : entry.Detail);
            CleanupResult result = new CleanupResult { Id = 1, Title = entry.Name, Vendor = "右键管理", Category = entry.Module, ActionKind = action, TechnicalLocation = location, Status = "Done", Message = message, Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } }; CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch); return batch;
        }

        public static bool Restore(string backupPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return RestoreBackup(serializer.Deserialize<AdvancedMenuBackup>(File.ReadAllText(backupPath, Encoding.UTF8)));
        }

        private static bool RestoreBackup(AdvancedMenuBackup backup)
        {
            if (backup == null) return false; bool ok = true;
            foreach (ContextMenuTreeBackup tree in backup.Trees ?? new List<ContextMenuTreeBackup>()) ok = IsIeTree(tree.Target) ? RestoreIeTree(tree) && ok : ContextMenuMutationService.RestoreTreeSnapshot(tree) && ok;
            foreach (ContextMenuToggleBackup value in backup.Values ?? new List<ContextMenuToggleBackup>()) ok = ContextMenuMutationService.RestoreValueSnapshot(value) && ok;
            foreach (AdvancedFileSnapshot file in backup.Files ?? new List<AdvancedFileSnapshot>())
            {
                try
                {
                    if (!file.Existed) { if (File.Exists(file.Path)) File.Delete(file.Path); }
                    else { Directory.CreateDirectory(Path.GetDirectoryName(file.Path)); File.WriteAllBytes(file.Path, file.Bytes ?? new byte[0]); File.SetAttributes(file.Path, (FileAttributes)file.Attributes); }
                    ok = (File.Exists(file.Path) == file.Existed) && ok;
                }
                catch { ok = false; }
            }
            return ok;
        }

        private static bool IsIeTree(ActionTarget target) { return target != null && target.SubKey != null && (target.SubKey.Equals(AdvancedMenuInventoryService.IeRoot, StringComparison.OrdinalIgnoreCase) || target.SubKey.StartsWith(AdvancedMenuInventoryService.IeRoot + "\\", StringComparison.OrdinalIgnoreCase) || target.SubKey.Equals(AdvancedMenuInventoryService.IeDisabledRoot, StringComparison.OrdinalIgnoreCase) || target.SubKey.StartsWith(AdvancedMenuInventoryService.IeDisabledRoot + "\\", StringComparison.OrdinalIgnoreCase)); }
        private static bool RestoreIeTree(ContextMenuTreeBackup tree)
        {
            if (tree == null || tree.Target == null) return false;
            DeleteIeTree(tree.Target); if (tree.KeyExisted) RestoreIeNode(tree.Target, tree.Snapshot);
            using (RegistryKey verify = RegistryHelper.OpenSubKey(tree.Target, false)) return tree.KeyExisted ? verify != null : verify == null;
        }
        private static void RestoreIeNode(ActionTarget target, RegistryTreeSnapshot node)
        {
            RunReg("add " + Quote(Native(target)) + " /f" + ViewArg(target), true);
            if (node == null) return;
            foreach (RegistryTreeValueSnapshot value in node.Values ?? new List<RegistryTreeValueSnapshot>())
            {
                string type = "REG_SZ", data = value.Text ?? string.Empty;
                if (value.Kind == RegistryValueKind.DWord.ToString()) { type = "REG_DWORD"; data = Convert.ToInt32(value.Number).ToString(); }
                else if (value.Kind == RegistryValueKind.QWord.ToString()) { type = "REG_QWORD"; data = value.Number.ToString(); }
                else if (value.Kind == RegistryValueKind.ExpandString.ToString()) type = "REG_EXPAND_SZ";
                else if (value.Kind == RegistryValueKind.MultiString.ToString()) { type = "REG_MULTI_SZ"; data = string.Join("\\0", value.TextArray ?? new string[0]); }
                else if (value.Kind == RegistryValueKind.Binary.ToString()) { type = "REG_BINARY"; data = BitConverter.ToString(value.Bytes ?? new byte[0]).Replace("-", string.Empty); }
                RegAdd(target, value.Name ?? string.Empty, data, type);
            }
            foreach (KeyValuePair<string, RegistryTreeSnapshot> child in node.Children ?? new Dictionary<string, RegistryTreeSnapshot>()) RestoreIeNode(Target(target.Hive, target.View, target.SubKey + "\\" + child.Key), child.Value);
        }
        private static void RegAdd(ActionTarget target, string name, string data, string type)
        {
            string selector = string.IsNullOrEmpty(name) ? "/ve" : "/v " + Quote(name);
            RunReg("add " + Quote(Native(target)) + " " + selector + " /t " + type + " /d " + Quote(data ?? string.Empty) + " /f" + ViewArg(target), true);
        }
        private static void DeleteIeTree(ActionTarget target) { RunReg("delete " + Quote(Native(target)) + " /f" + ViewArg(target), false); }
        private static string Native(ActionTarget target) { return (target.Hive == "HKLM" ? "HKLM\\" : "HKCU\\") + target.SubKey; }
        private static string ViewArg(ActionTarget target) { return target.View == "Registry32" ? " /reg:32" : target.View == "Registry64" ? " /reg:64" : string.Empty; }
        private static string Quote(string value) { return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\""; }
        private static void RunReg(string arguments, bool required)
        {
            ProcessStartInfo info = new ProcessStartInfo { FileName = "reg.exe", Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
            using (Process process = Process.Start(info)) { string output = process.StandardOutput.ReadToEnd(); string error = process.StandardError.ReadToEnd(); process.WaitForExit(); if (required && process.ExitCode != 0) throw new InvalidOperationException("reg.exe 写入失败，退出码 " + process.ExitCode + "：" + (string.IsNullOrWhiteSpace(error) ? output : error).Trim()); }
        }

        private static void Ensure(AdvancedMenuEntry entry) { if (entry == null) throw new ArgumentNullException("entry"); if (entry.ReadOnly) throw new InvalidOperationException("该项目为只读。"); if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目属于所有用户范围，需要管理员权限。"); }
        private static string[] WinXNameParts(string path) { string name = Path.GetFileNameWithoutExtension(path); int split = name.IndexOf(" - ", StringComparison.Ordinal); return split > 0 ? new string[] { name.Substring(0, split), name.Substring(split + 3) } : new string[] { name, name }; }
        private static string Parent(string path) { return path.Substring(0, path.LastIndexOf('\\')); }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
    }

    internal sealed class AdvancedContextMenuForm : Form
    {
        private readonly DataStore store; private readonly BindingList<AdvancedMenuEntry> rows = new BindingList<AdvancedMenuEntry>(); private readonly DataGridView grid = new BufferedDataGridView();
        private readonly ComboBox module = new ComboBox(); private readonly Label status = new Label(); private readonly Button refresh = new Button(); private readonly Button enable = new Button(); private readonly Button disable = new Button(); private readonly Button edit = new Button(); private readonly Button add = new Button(); private readonly Button delete = new Button(); private readonly Button up = new Button(); private readonly Button down = new Button();
        private AdvancedMenuInventory inventory;

        public AdvancedContextMenuForm(DataStore store)
        {
            this.store = store; Text = "高级右键兼容"; StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(1020, 640); Size = new Size(1220, 720); BackColor = UiTheme.Canvas; Font = UiTheme.Font(9F, FontStyle.Regular); BuildUi(); Shown += delegate { RefreshRows(); };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(18) }; root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); Controls.Add(root);
            root.Controls.Add(new Label { Text = "WinX · Windows 11 现代菜单 · IE 旧式菜单 · 安全增强配方", Dock = DockStyle.Fill, Font = UiTheme.Font(14F, FontStyle.Bold), ForeColor = UiTheme.Text }, 0, 0);
            FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; module.DropDownStyle = ComboBoxStyle.DropDownList; module.Width = 205; module.Items.AddRange(new object[] { "全部模块", "WinX 快捷菜单", "现代 / UWP 菜单", "IE 旧式菜单", "安全增强菜单" }); module.SelectedIndex = 0;
            ButtonStyle(refresh, "刷新", UiTheme.Primary); ButtonStyle(enable, "启用 / 安装", UiTheme.Success); ButtonStyle(disable, "禁用 / 移除", UiTheme.Danger); ButtonStyle(edit, "编辑", UiTheme.Primary); ButtonStyle(add, "添加 IE 项", UiTheme.Success); ButtonStyle(delete, "删除", UiTheme.Danger); ButtonStyle(up, "上移", UiTheme.Muted); ButtonStyle(down, "下移", UiTheme.Muted);
            foreach (Control c in new Control[] { module, refresh, enable, disable, edit, add, delete, up, down }) bar.Controls.Add(c); root.Controls.Add(bar, 0, 1);
            grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.DataSource = rows; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.BackgroundColor = UiTheme.Surface;
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "状态", FillWeight = 55 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "名称", FillWeight = 145 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Module", HeaderText = "模块", FillWeight = 95 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Detail", HeaderText = "详情", FillWeight = 190 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Scope", HeaderText = "范围", FillWeight = 90 }); root.Controls.Add(grid, 0, 2); status.Dock = DockStyle.Fill; status.ForeColor = UiTheme.Muted; root.Controls.Add(status, 0, 3);
            refresh.Click += delegate { RefreshRows(); }; module.SelectedIndexChanged += delegate { ApplyFilter(); }; grid.SelectionChanged += delegate { UpdateActions(); }; enable.Click += delegate { Toggle(true); }; disable.Click += delegate { Toggle(false); }; delete.Click += delegate { DeleteCurrent(); }; edit.Click += delegate { EditCurrent(); }; add.Click += delegate { EditIe(null); }; up.Click += delegate { MoveSelected(-1); }; down.Click += delegate { MoveSelected(1); };
        }

        private static void ButtonStyle(Button button, string text, Color color) { UiTheme.OutlineButton(button, text, color); }
        private AdvancedMenuEntry Current() { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as AdvancedMenuEntry; }
        private void RefreshRows()
        {
            if (!refresh.Enabled) return; refresh.Enabled = false; status.Text = "正在后台枚举高级菜单，不阻塞鼠标……";
            Task.Factory.StartNew(delegate { return new AdvancedMenuInventoryService(store).Enumerate(); }).ContinueWith(delegate(Task<AdvancedMenuInventory> task)
            {
                if (IsDisposed || !IsHandleCreated) return; BeginInvoke((MethodInvoker)delegate { refresh.Enabled = true; if (task.IsFaulted) { Exception ex = task.Exception.GetBaseException(); Logger.Error("高级菜单枚举失败", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; } inventory = task.Result; ApplyFilter(); status.Text = "共发现 " + inventory.Entries.Count + " 项；" + inventory.Warnings.Count + " 个位置已安全跳过。现代菜单仅列出包清单明确声明的 File Explorer 命令。"; });
            });
        }
        private void ApplyFilter() { if (inventory == null) return; string selected = Convert.ToString(module.SelectedItem); rows.RaiseListChangedEvents = false; rows.Clear(); foreach (AdvancedMenuEntry e in inventory.Entries) if (selected == "全部模块" || e.Module == selected) rows.Add(e); rows.RaiseListChangedEvents = true; rows.ResetBindings(); UpdateActions(); }
        private void UpdateActions() { AdvancedMenuEntry e = Current(); enable.Enabled = e != null && !e.ReadOnly && !e.Enabled; disable.Enabled = e != null && !e.ReadOnly && e.Enabled; edit.Enabled = e != null && e.Module == "IE 旧式菜单"; delete.Enabled = e != null && (e.Module == "WinX 快捷菜单" || e.Module == "IE 旧式菜单" || (e.Module == "安全增强菜单" && e.Enabled)); up.Enabled = down.Enabled = e != null && e.Module == "WinX 快捷菜单" && e.Enabled; add.Enabled = Convert.ToString(module.SelectedItem) == "全部模块" || Convert.ToString(module.SelectedItem) == "IE 旧式菜单"; }
        private void Toggle(bool value) { AdvancedMenuEntry e = Current(); if (e == null) return; try { new AdvancedContextMenuMutationService(store).SetEnabled(e, value); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void DeleteCurrent() { AdvancedMenuEntry e = Current(); if (e == null) return; if (MessageBox.Show(this, "删除“" + e.Name + "”？操作前会完整备份。", "高级右键兼容", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; try { new AdvancedContextMenuMutationService(store).Delete(e); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void EditCurrent() { AdvancedMenuEntry e = Current(); if (e != null && e.Module == "IE 旧式菜单") EditIe(e); }
        private void EditIe(AdvancedMenuEntry e) { using (IeMenuEditorForm form = new IeMenuEditorForm(e)) { if (form.ShowDialog(this) != DialogResult.OK) return; try { new AdvancedContextMenuMutationService(store).AddOrEditIe(e, form.MenuName, form.Url, form.Contexts); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } } }
        private void MoveSelected(int direction) { AdvancedMenuEntry e = Current(); if (e == null) return; try { new AdvancedContextMenuMutationService(store).MoveWinX(e, direction); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "调整失败", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
    }

    internal sealed class IeMenuEditorForm : Form
    {
        private readonly TextBox name = new TextBox(); private readonly TextBox url = new TextBox(); private readonly NumericUpDown contexts = new NumericUpDown();
        public string MenuName { get { return name.Text.Trim(); } } public string Url { get { return url.Text.Trim(); } } public int Contexts { get { return Convert.ToInt32(contexts.Value); } }
        public IeMenuEditorForm(AdvancedMenuEntry entry)
        {
            Text = entry == null ? "添加 IE 旧式菜单" : "编辑 IE 旧式菜单"; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(680, 270); BackColor = UiTheme.Surface; Font = UiTheme.Font(9F, FontStyle.Regular);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(22) }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55)); Controls.Add(root);
            root.Controls.Add(new Label { Text = "菜单名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); root.Controls.Add(name, 1, 0); root.Controls.Add(new Label { Text = "脚本 / 页面地址", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); root.Controls.Add(url, 1, 1); root.Controls.Add(new Label { Text = "Contexts 位掩码", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2); contexts.Maximum = int.MaxValue; root.Controls.Add(contexts, 1, 2); name.Dock = url.Dock = contexts.Dock = DockStyle.Fill; name.Margin = url.Margin = contexts.Margin = new Padding(0, 10, 0, 10);
            if (entry != null) { name.Text = entry.Name; url.Text = entry.Detail; contexts.Value = Math.Max(0, entry.Contexts); }
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; Button cancel = new Button(); UiTheme.OutlineButton(cancel, "取消", UiTheme.Muted); cancel.DialogResult = DialogResult.Cancel; Button ok = new Button(); UiTheme.PrimaryButton(ok, "保存", UiTheme.Primary); ok.Click += delegate { if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(url.Text)) { MessageBox.Show(this, "名称和地址不能为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; } DialogResult = DialogResult.OK; }; actions.Controls.Add(cancel); actions.Controls.Add(ok); root.Controls.Add(actions, 1, 3); AcceptButton = ok; CancelButton = cancel;
        }
    }

#if VALIDATION
    internal static class AdvancedContextMenuRegression
    {
        private const string IeName = "CodexRogueCleanerTest_IE_Menu";
        private const string UwpGuid = "{C0DE2026-0806-4A20-8A00-BA1D0E7D15C2}";
        private const string TestIeRoot = @"Software\RogueCleanerValidation\MenuExt";
        private const string TestIeDisabledRoot = @"Software\RogueCleanerValidation\MenuExt.Disabled";
        public static List<string> Run(DataStore store)
        {
            List<string> failures = new List<string>(); List<CleanupBatch> batches = new List<CleanupBatch>(); string view = Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; string lab = Path.Combine(store.State, "advanced-winx-smoke");
            try
            {
                Directory.CreateDirectory(Path.Combine(lab, "Group1")); File.WriteAllBytes(Path.Combine(lab, "Group1", "1 - First.lnk"), new byte[] { 1, 2, 3 }); File.WriteAllBytes(Path.Combine(lab, "Group1", "2 - Second.lnk"), new byte[] { 4, 5, 6 });
                AdvancedContextMenuMutationService labService = new AdvancedContextMenuMutationService(store, lab); AdvancedMenuEntry winx = new AdvancedMenuInventoryService(store, lab).Enumerate().Entries.FirstOrDefault(delegate(AdvancedMenuEntry e) { return e.Module == "WinX 快捷菜单" && e.Enabled; });
                if (winx == null) failures.Add("WinX 实验目录未被枚举。"); else { CleanupBatch b = labService.SetEnabled(winx, false); batches.Add(b); if (new AdvancedMenuInventoryService(store, lab).Enumerate().Entries.Any(delegate(AdvancedMenuEntry e) { return e.FilePath == winx.FilePath && e.Enabled; })) failures.Add("WinX 禁用后仍处于启用目录。"); if (!new CleanerEngine(store).RestoreBatch(b).AllSucceeded || !File.Exists(winx.FilePath)) failures.Add("WinX 禁用恢复失败。"); CleanupBatch order = labService.MoveWinX(winx, 1); batches.Add(order); if (!File.Exists(Path.Combine(lab, "Group1", "2 - First.lnk")) || !File.Exists(Path.Combine(lab, "Group1", "1 - Second.lnk"))) failures.Add("WinX 下移没有交换顺序前缀。"); if (!new CleanerEngine(store).RestoreBatch(order).AllSucceeded || !File.Exists(winx.FilePath)) failures.Add("WinX 顺序恢复失败。"); }

                AdvancedContextMenuMutationService service = new AdvancedContextMenuMutationService(store);
                AdvancedContextMenuMutationService ieService = new AdvancedContextMenuMutationService(store, null, TestIeRoot, TestIeDisabledRoot);
                CleanupBatch ie = ieService.AddOrEditIe(null, IeName, "about:blank", 1); batches.Add(ie); AdvancedMenuEntry ieEntry = new AdvancedMenuInventoryService(store, null, TestIeRoot, TestIeDisabledRoot).Enumerate().Entries.FirstOrDefault(delegate(AdvancedMenuEntry e) { return e.Module == "IE 旧式菜单" && e.Name == IeName && e.Enabled; });
                if (ieEntry == null) failures.Add("IE 菜单添加后未被枚举。"); else { CleanupBatch toggle = ieService.SetEnabled(ieEntry, false); batches.Add(toggle); if (!new AdvancedMenuInventoryService(store, null, TestIeRoot, TestIeDisabledRoot).Enumerate().Entries.Any(delegate(AdvancedMenuEntry e) { return e.Module == "IE 旧式菜单" && e.Name == IeName && !e.Enabled; })) failures.Add("IE 菜单禁用状态未被枚举。"); if (!new CleanerEngine(store).RestoreBatch(toggle).AllSucceeded) failures.Add("IE 菜单禁用恢复失败。"); }

                AdvancedMenuEntry fake = new AdvancedMenuEntry { Module = "现代 / UWP 菜单", Name = "回归现代菜单", Enabled = true, Hive = "HKCU", View = view, SubKey = AdvancedMenuInventoryService.BlockedRoot, ValueName = UwpGuid, PackageName = "Codex.Test" };
                CleanupBatch uwp = service.SetEnabled(fake, false); batches.Add(uwp); using (RegistryKey key = RegistryHelper.OpenSubKey(new ActionTarget { Hive = "HKCU", View = view, SubKey = AdvancedMenuInventoryService.BlockedRoot }, false)) if (key == null || !key.GetValueNames().Any(delegate(string n) { return n == UwpGuid; })) failures.Add("现代菜单屏蔽值未写入。"); if (!new CleanerEngine(store).RestoreBatch(uwp).AllSucceeded) failures.Add("现代菜单屏蔽恢复失败。");

                AdvancedMenuEntry recipe = new AdvancedMenuInventoryService(store).Enumerate().Entries.First(delegate(AdvancedMenuEntry e) { return e.Id == "Recipe|OpenNotepad"; }); CleanupBatch recipeBatch = service.SetEnabled(recipe, true); batches.Add(recipeBatch); if (!new AdvancedMenuInventoryService(store).Enumerate().Entries.Any(delegate(AdvancedMenuEntry e) { return e.Id == "Recipe|OpenNotepad" && e.Enabled; })) failures.Add("安全增强配方安装后未被枚举。"); if (!new CleanerEngine(store).RestoreBatch(recipeBatch).AllSucceeded) failures.Add("安全增强配方恢复失败。");
                if (new AdvancedMenuInventoryService(store).Enumerate().Entries.Any(delegate(AdvancedMenuEntry e) { return e.Module == "现代 / UWP 菜单" && string.IsNullOrWhiteSpace(e.ValueName); })) failures.Add("现代菜单枚举出现缺少 CLSID 的项目。");
                if (!new CleanerEngine(store).RestoreBatch(ie).AllSucceeded) failures.Add("IE 菜单添加恢复失败。");
            }
            catch (Exception ex) { failures.Add("高级菜单回归异常（用户=" + System.Security.Principal.WindowsIdentity.GetCurrent().Name + "，管理员=" + AdminUtil.IsAdministrator() + "）：" + ex); }
            finally
            {
                try { if (Directory.Exists(lab)) Directory.Delete(lab, true); } catch { }
                try { using (RegistryKey root = RegistryHelper.OpenBase("HKCU", view, true)) { root.DeleteSubKeyTree(@"Software\RogueCleanerValidation", false); using (RegistryKey blocked = root.OpenSubKey(AdvancedMenuInventoryService.BlockedRoot, true)) if (blocked != null) blocked.DeleteValue(UwpGuid, false); foreach (EnhancedMenuRecipe r in AdvancedMenuInventoryService.Recipes()) if (r.Id == "OpenNotepad") root.DeleteSubKeyTree(r.Root, false); } } catch { }
                foreach (CleanupBatch batch in batches) try { if (Directory.Exists(batch.Path)) new CleanerEngine(store).DeleteBatchRecord(batch); } catch { }
            }
            return failures;
        }
    }
#endif
}
