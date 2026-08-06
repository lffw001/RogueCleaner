using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal sealed class SpecialMenuEntry
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
        public string ModuleDisplay { get { return SpecialMenuDisplay.Name(Module); } }
        [ScriptIgnore] public Image SoftwareIcon { get; set; }
        [ScriptIgnore] public string SoftwareName { get; set; }
        [ScriptIgnore] public string IdentityConfidence { get; set; }
        [ScriptIgnore] public string IconSource { get; set; }
        [ScriptIgnore] public string IdentityExplanation { get; set; }
        public SoftwarePresentationEvidence PresentationEvidence() { return new SoftwarePresentationEvidence { DeclaredName = Name, FilePath = FilePath, Command = Detail, TechnicalLocation = Hive + "\\" + SubKey }; }
        public void ApplyPresentation(SoftwarePresentation p) { if (p == null) return; SoftwareIcon = p.Icon; SoftwareName = p.SoftwareName; IdentityConfidence = p.Confidence; IconSource = p.IconSource; IdentityExplanation = p.Explanation; }
    }

    internal static class SpecialMenuDisplay
    {
        public static string Name(string module)
        {
            if (module == "ShellNew 新建菜单") return "新建菜单";
            if (module == "SendTo 发送到") return "发送到菜单";
            if (module == "OpenWith 打开方式") return "打开方式";
            if (module == "OpenWith 应用程序") return "打开方式应用程序";
            if (module == "GUID 屏蔽") return "组件屏蔽";
            return module;
        }

        public static string Key(string display)
        {
            if (display == "新建菜单") return "ShellNew 新建菜单";
            if (display == "发送到菜单") return "SendTo 发送到";
            if (display == "打开方式") return "OpenWith 打开方式";
            if (display == "打开方式应用程序") return "OpenWith 应用程序";
            if (display == "组件屏蔽") return "GUID 屏蔽";
            return display;
        }
    }

    internal sealed class SpecialMenuInventory
    {
        public List<SpecialMenuEntry> Entries { get; set; }
        public List<ScanWarning> Warnings { get; set; }
    }

    internal sealed class SpecialMenuBackup
    {
        public string Mode { get; set; }
        public List<ContextMenuTreeBackup> Trees { get; set; }
        public List<ContextMenuToggleBackup> Values { get; set; }
        public string OriginalFile { get; set; }
        public string ChangedFile { get; set; }
        public bool OriginalFileExisted { get; set; }
        public bool ChangedFileExisted { get; set; }
    }

    internal sealed class SpecialMenuInventoryService
    {
        private const string DisabledShellNew = "ShellNew.RogueCleanerDisabled";
        private const string DisabledOpenWith = "OpenWithProgids.RogueCleanerDisabled";
        private const string DisabledOpenWithList = "OpenWithList.RogueCleanerDisabled";
        private const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private readonly DataStore store;

        public SpecialMenuInventoryService(DataStore store) { this.store = store; }

        public SpecialMenuInventory Enumerate()
        {
            List<SpecialMenuEntry> entries = new List<SpecialMenuEntry>();
            List<ScanWarning> warnings = new List<ScanWarning>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string hive in new string[] { "HKCU", "HKLM" })
            {
                foreach (string view in new string[] { "Registry64", "Registry32" })
                {
                    EnumerateClasses(hive, view, entries, warnings, seen);
                    EnumerateApplications(hive, view, entries, warnings, seen);
                    EnumerateBlocked(hive, view, entries, warnings, seen);
                }
            }
            EnumerateSendTo(entries);
            return new SpecialMenuInventory { Entries = entries.OrderBy(delegate(SpecialMenuEntry e) { return e.Module; }).ThenBy(delegate(SpecialMenuEntry e) { return e.Name; }).ToList(), Warnings = warnings };
        }

        private void EnumerateClasses(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget classes = Target(hive, view, @"Software\Classes");
            using (RegistryKey key = Open(classes, "文件类型", warnings))
            {
                if (key == null) return;
                foreach (string extension in SafeSubKeys(key))
                {
                    if (!extension.StartsWith(".", StringComparison.Ordinal) || extension.Length > 24) continue;
                    string extensionRoot = @"Software\Classes\" + extension;
                    AddShellNew(hive, view, extension, extensionRoot + @"\ShellNew", true, entries, warnings, seen);
                    AddShellNew(hive, view, extension, extensionRoot + "\\" + DisabledShellNew, false, entries, warnings, seen);
                    AddOpenWithValues(hive, view, extension, extensionRoot + @"\OpenWithProgids", true, entries, warnings, seen);
                    AddOpenWithValues(hive, view, extension, extensionRoot + "\\" + DisabledOpenWith, false, entries, warnings, seen);
                    AddOpenWithList(hive, view, extension, extensionRoot + @"\OpenWithList", true, entries, warnings, seen);
                    AddOpenWithList(hive, view, extension, extensionRoot + "\\" + DisabledOpenWithList, false, entries, warnings, seen);
                }
            }
        }

        private void AddShellNew(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "新建菜单", warnings))
            {
                if (key == null) return;
                string id = "ShellNew|" + hive + "|" + view + "|" + subKey;
                if (!seen.Add(id)) return;
                string detail = First(Read(key, "FileName"), HasValue(key, "NullFile") ? "空白文件" : string.Empty, Read(key, "Data"), Read(key, "Command"));
                entries.Add(Entry(id, "ShellNew 新建菜单", extension, detail, hive, view, subKey, null, enabled));
            }
        }

        private void AddOpenWithValues(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "打开方式", warnings))
            {
                if (key == null) return;
                foreach (string valueName in SafeValues(key))
                {
                    string id = "OpenWith|" + hive + "|" + view + "|" + subKey + "|" + valueName;
                    if (!seen.Add(id)) continue;
                    SpecialMenuEntry entry = Entry(id, "OpenWith 打开方式", extension + " → " + valueName, valueName, hive, view, subKey, valueName, enabled);
                    entries.Add(entry);
                }
            }
        }

        private void AddOpenWithList(string hive, string view, string extension, string subKey, bool enabled, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            ActionTarget target = Target(hive, view, subKey);
            using (RegistryKey key = Open(target, "打开方式列表", warnings))
            {
                if (key == null) return;
                foreach (string valueName in SafeValues(key))
                {
                    if (string.Equals(valueName, "MRUList", StringComparison.OrdinalIgnoreCase)) continue;
                    string executable = Read(key, valueName);
                    if (string.IsNullOrWhiteSpace(executable)) continue;
                    string id = "OpenWithList|" + hive + "|" + view + "|" + subKey + "|" + valueName;
                    if (!seen.Add(id)) continue;
                    entries.Add(Entry(id, "OpenWith 打开方式", extension + " → " + executable, "打开方式列表 / " + valueName, hive, view, subKey, valueName, enabled));
                }
                foreach (string application in SafeSubKeys(key))
                {
                    string childPath = subKey + "\\" + application;
                    string id = "OpenWithListKey|" + hive + "|" + view + "|" + childPath;
                    if (!seen.Add(id)) continue;
                    entries.Add(Entry(id, "OpenWith 打开方式", extension + " → " + application, "打开方式列表子项", hive, view, childPath, string.Empty, enabled));
                }
            }
        }

        private void EnumerateApplications(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            string rootPath = @"Software\Classes\Applications";
            using (RegistryKey root = Open(Target(hive, view, rootPath), "打开方式程序", warnings))
            {
                if (root == null) return;
                foreach (string app in SafeSubKeys(root))
                {
                    string appPath = rootPath + "\\" + app;
                    using (RegistryKey key = Open(Target(hive, view, appPath), "打开方式程序", warnings))
                    {
                        if (key == null) continue;
                        string command = ReadChildDefault(hive, view, appPath + @"\shell\open\command", warnings);
                        if (string.IsNullOrWhiteSpace(command)) continue;
                        string id = "Application|" + hive + "|" + view + "|" + appPath;
                        if (!seen.Add(id)) continue;
                        SpecialMenuEntry entry = Entry(id, "OpenWith 应用程序", app, command, hive, view, appPath, "NoOpenWith", !HasValue(key, "NoOpenWith"));
                        entries.Add(entry);
                    }
                }
            }
        }

        private void EnumerateBlocked(string hive, string view, List<SpecialMenuEntry> entries, List<ScanWarning> warnings, HashSet<string> seen)
        {
            using (RegistryKey key = Open(Target(hive, view, BlockedRoot), "GUID 屏蔽", warnings))
            {
                if (key == null) return;
                foreach (string clsid in SafeValues(key))
                {
                    if (!clsid.StartsWith("{", StringComparison.Ordinal) || !seen.Add("Blocked|" + hive + "|" + view + "|" + clsid)) continue;
                    entries.Add(Entry("Blocked|" + hive + "|" + view + "|" + clsid, "GUID 屏蔽", clsid, Read(key, clsid), hive, view, BlockedRoot, clsid, false));
                }
            }
        }

        private void EnumerateSendTo(List<SpecialMenuEntry> entries)
        {
            string active = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            string disabled = DisabledSendToDirectory(store);
            foreach (string file in SafeFiles(active)) if (!string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase)) entries.Add(FileEntry("SendTo|active|" + file, Path.GetFileName(file), file, true));
            foreach (string file in SafeFiles(disabled)) entries.Add(FileEntry("SendTo|disabled|" + file, Path.GetFileName(file), file, false));
        }

        internal static string DisabledSendToDirectory(DataStore store) { return Path.Combine(store.State, "sendto-disabled"); }
        internal static string DisabledOpenWithName { get { return DisabledOpenWith; } }
        internal static string DisabledOpenWithListName { get { return DisabledOpenWithList; } }
        internal static string DisabledShellNewName { get { return DisabledShellNew; } }
        internal static string BlockedPath { get { return BlockedRoot; } }

        private static SpecialMenuEntry FileEntry(string id, string name, string path, bool enabled)
        {
            return new SpecialMenuEntry { Id = id, Module = "SendTo 发送到", Name = name, Detail = path, Scope = "当前用户 / 文件", Status = enabled ? "已启用" : "已禁用", Enabled = enabled, FilePath = path };
        }

        private static SpecialMenuEntry Entry(string id, string module, string name, string detail, string hive, string view, string subKey, string valueName, bool enabled)
        {
            return new SpecialMenuEntry { Id = id, Module = module, Name = name, Detail = detail, Hive = hive, View = view, SubKey = subKey, ValueName = valueName, Scope = (hive == "HKCU" ? "当前用户" : "所有用户") + " / " + (view == "Registry32" ? "32 位" : "64 位"), Status = enabled ? "已启用" : "已禁用", Enabled = enabled, RequiresAdmin = hive == "HKLM" };
        }

        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static RegistryKey Open(ActionTarget target, string stage, List<ScanWarning> warnings) { try { return RegistryHelper.OpenSubKey(target, false); } catch (Exception ex) { if (!(ex is System.Security.SecurityException) && !(ex is UnauthorizedAccessException)) throw; warnings.Add(new ScanWarning { Stage = stage, TechnicalLocation = RegistryHelper.NativePath(target), ErrorType = ex.GetType().FullName, Message = "访问被拒绝，已跳过。" }); return null; } }
        private static string[] SafeSubKeys(RegistryKey key) { try { return key.GetSubKeyNames(); } catch { return new string[0]; } }
        private static string[] SafeValues(RegistryKey key) { try { return key.GetValueNames(); } catch { return new string[0]; } }
        private static string[] SafeFiles(string path) { try { return Directory.Exists(path) ? Directory.GetFiles(path) : new string[0]; } catch { return new string[0]; } }
        private static string Read(RegistryKey key, string name) { try { return Convert.ToString(key.GetValue(name, "")); } catch { return string.Empty; } }
        private static string ReadChildDefault(string hive, string view, string subKey, List<ScanWarning> warnings) { using (RegistryKey key = Open(Target(hive, view, subKey), "打开方式程序", warnings)) return key == null ? string.Empty : Read(key, ""); }
        private static bool HasValue(RegistryKey key, string name) { return SafeValues(key).Any(delegate(string item) { return string.Equals(item, name, StringComparison.OrdinalIgnoreCase); }); }
        private static string First(params string[] values) { foreach (string value in values) if (!string.IsNullOrWhiteSpace(value)) return value; return string.Empty; }
    }

    internal sealed class SpecialContextMenuMutationService
    {
        private readonly DataStore store;
        public SpecialContextMenuMutationService(DataStore store) { this.store = store; }

        public CleanupBatch SetEnabled(SpecialMenuEntry entry, bool enabled)
        {
            EnsurePermission(entry);
            if (entry.Module.StartsWith("SendTo", StringComparison.Ordinal)) return MoveSendTo(entry, enabled);
            if (entry.Module.StartsWith("ShellNew", StringComparison.Ordinal)) return MoveTree(entry, enabled);
            if (entry.Module == "OpenWith 打开方式") return MoveOpenWith(entry, enabled);
            if (entry.Module == "OpenWith 应用程序") return ToggleValue(entry, enabled, "NoOpenWith");
            if (entry.Module == "GUID 屏蔽") return ToggleValue(entry, enabled, entry.ValueName);
            throw new InvalidOperationException("不支持的专用模块类型。");
        }

        public CleanupBatch Delete(SpecialMenuEntry entry)
        {
            EnsurePermission(entry);
            if (entry.Module.StartsWith("SendTo", StringComparison.Ordinal)) return DeleteFile(entry);
            if (entry.Module.StartsWith("ShellNew", StringComparison.Ordinal)) return DeleteTree(entry);
            if (entry.Module == "OpenWith 打开方式") return string.IsNullOrEmpty(entry.ValueName) ? DeleteTree(entry) : DeleteValue(entry);
            if (entry.Module == "GUID 屏蔽") return DeleteValue(entry);
            throw new InvalidOperationException("该项目只允许启用或禁用，不提供删除。");
        }

        public CleanupBatch AddShellNew(string extension, string template)
        {
            extension = NormalizeExtension(extension);
            ActionTarget target = Target("HKCU", DefaultView(), @"Software\Classes\" + extension + @"\ShellNew");
            SpecialMenuBackup backup = BackupTrees("AddShellNew", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true))
                using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (string.IsNullOrWhiteSpace(template)) key.SetValue("NullFile", string.Empty, RegistryValueKind.String); else key.SetValue("FileName", template.Trim(), RegistryValueKind.String);
                }
                return Complete(id, batchPath, backupPath, target, extension, "ShellNew 新建菜单", "AddShellNew");
            }
            catch { RestoreBackup(backup); throw; }
        }

        public CleanupBatch AddOpenWith(string extension, string progId)
        {
            extension = NormalizeExtension(extension);
            if (string.IsNullOrWhiteSpace(progId)) throw new ArgumentException("ProgID 不能为空。");
            ActionTarget target = Target("HKCU", DefaultView(), @"Software\Classes\" + extension + @"\OpenWithProgids");
            return SetValueWithBackup(target, progId.Trim(), string.Empty, extension + " → " + progId.Trim(), "OpenWith 打开方式", "AddOpenWith");
        }

        public CleanupBatch AddBlockedGuid(string clsid, string description)
        {
            Guid guid;
            if (!Guid.TryParse(clsid, out guid)) throw new ArgumentException("请输入有效的 GUID/CLSID。");
            string normalized = "{" + guid.ToString().ToUpperInvariant() + "}";
            ActionTarget target = Target("HKCU", DefaultView(), SpecialMenuInventoryService.BlockedPath);
            return SetValueWithBackup(target, normalized, description ?? string.Empty, normalized, "GUID 屏蔽", "AddBlockedGuid");
        }

        public CleanupBatch AddSendTo(string name, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("名称和目标路径不能为空。");
            string sendTo = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            Directory.CreateDirectory(sendTo);
            string file = Path.Combine(sendTo, SafeFileName(name) + ".lnk");
            if (File.Exists(file)) throw new InvalidOperationException("同名发送到项目已经存在。");
            SpecialMenuBackup backup = new SpecialMenuBackup { Mode = "AddSendTo", Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), OriginalFile = file, OriginalFileExisted = false };
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("系统没有提供 WScript.Shell，无法创建快捷方式。");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(file);
                shortcut.TargetPath = Environment.ExpandEnvironmentVariables(targetPath.Trim());
                shortcut.Save();
                if (!File.Exists(file)) throw new InvalidOperationException("创建快捷方式后复核失败。");
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = file }, name, "SendTo 发送到", "AddSendTo");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveTree(SpecialMenuEntry entry, bool enabled)
        {
            string parent = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
            string activePath = parent + @"\ShellNew";
            string disabledPath = parent + "\\" + SpecialMenuInventoryService.DisabledShellNewName;
            ActionTarget active = Target(entry.Hive, entry.View, activePath); ActionTarget disabled = Target(entry.Hive, entry.View, disabledPath);
            SpecialMenuBackup backup = BackupTrees("ToggleShellNew", active, disabled);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { MoveRegistryTree(enabled ? disabled : active, enabled ? active : disabled); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = activePath }, entry.Name, entry.Module, enabled ? "EnableShellNew" : "DisableShellNew"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveOpenWith(SpecialMenuEntry entry, bool enabled)
        {
            if (string.IsNullOrEmpty(entry.ValueName))
            {
                string listRoot = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
                string extensionRoot = listRoot.Substring(0, listRoot.LastIndexOf('\\'));
                string application = entry.SubKey.Substring(entry.SubKey.LastIndexOf('\\') + 1);
                ActionTarget activeTree = Target(entry.Hive, entry.View, extensionRoot + @"\OpenWithList\" + application);
                ActionTarget disabledTree = Target(entry.Hive, entry.View, extensionRoot + "\\" + SpecialMenuInventoryService.DisabledOpenWithListName + "\\" + application);
                SpecialMenuBackup treeBackup = BackupTrees("ToggleOpenWithListKey", activeTree, disabledTree);
                string treeBackupPath; string treeId; string treeBatchPath; SaveBackup(treeBackup, out treeBackupPath, out treeId, out treeBatchPath);
                try { MoveRegistryTree(enabled ? disabledTree : activeTree, enabled ? activeTree : disabledTree); return Complete(treeId, treeBatchPath, treeBackupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = activeTree.SubKey }, entry.Name, entry.Module, enabled ? "EnableOpenWith" : "DisableOpenWith"); }
                catch { RestoreBackup(treeBackup); throw; }
            }
            string parent = entry.SubKey.Substring(0, entry.SubKey.LastIndexOf('\\'));
            bool listMode = entry.SubKey.IndexOf("OpenWithList", StringComparison.OrdinalIgnoreCase) >= 0;
            ActionTarget active = Target(entry.Hive, entry.View, parent + (listMode ? @"\OpenWithList" : @"\OpenWithProgids"));
            ActionTarget disabled = Target(entry.Hive, entry.View, parent + "\\" + (listMode ? SpecialMenuInventoryService.DisabledOpenWithListName : SpecialMenuInventoryService.DisabledOpenWithName));
            SpecialMenuBackup backup = listMode
                ? BackupValues("ToggleOpenWith", ValueTarget(active, entry.ValueName), ValueTarget(disabled, entry.ValueName), ValueTarget(Target(entry.Hive, entry.View, active.SubKey), "MRUList"), ValueTarget(Target(entry.Hive, entry.View, disabled.SubKey), "MRUList"))
                : BackupValues("ToggleOpenWith", ValueTarget(active, entry.ValueName), ValueTarget(disabled, entry.ValueName));
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                ActionTarget source = enabled ? disabled : active; ActionTarget destination = enabled ? active : disabled;
                object value = ReadRegistryValue(source, entry.ValueName);
                WriteRegistryValue(destination, entry.ValueName, value ?? string.Empty);
                DeleteRegistryValue(source, entry.ValueName);
                if (listMode) { UpdateMru(source, entry.ValueName, false); UpdateMru(destination, entry.ValueName, true); }
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = active.SubKey, ValueName = entry.ValueName }, entry.Name, entry.Module, enabled ? "EnableOpenWith" : "DisableOpenWith");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch ToggleValue(SpecialMenuEntry entry, bool enabled, string valueName)
        {
            ActionTarget target = ValueTarget(Target(entry.Hive, entry.View, entry.SubKey), valueName);
            SpecialMenuBackup backup = BackupValues("ToggleValue", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try
            {
                bool removeValue = entry.Module == "GUID 屏蔽" ? enabled : enabled;
                if (removeValue) DeleteRegistryValue(target, valueName); else WriteRegistryValue(target, valueName, entry.Module == "GUID 屏蔽" ? "由流氓软件克星屏蔽" : string.Empty);
                return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = entry.Hive, View = entry.View, SubKey = entry.SubKey, ValueName = valueName }, entry.Name, entry.Module, enabled ? "EnableSpecial" : "DisableSpecial");
            }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch MoveSendTo(SpecialMenuEntry entry, bool enabled)
        {
            string activeDir = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            string disabledDir = SpecialMenuInventoryService.DisabledSendToDirectory(store);
            Directory.CreateDirectory(activeDir); Directory.CreateDirectory(disabledDir);
            string destination = Path.Combine(enabled ? activeDir : disabledDir, Path.GetFileName(entry.FilePath));
            if (File.Exists(destination)) throw new InvalidOperationException("目标位置已有同名文件。");
            SpecialMenuBackup backup = FileBackup("ToggleSendTo", entry.FilePath, destination);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { File.Move(entry.FilePath, destination); if (!File.Exists(destination)) throw new InvalidOperationException("移动后复核失败。"); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = destination }, entry.Name, entry.Module, enabled ? "EnableSendTo" : "DisableSendTo"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteFile(SpecialMenuEntry entry)
        {
            string id = NewBatchId();
            string batchPath = Path.Combine(store.Backups, id);
            string filesDir = Path.Combine(batchPath, "files");
            Directory.CreateDirectory(filesDir);
            string backupFile = Path.Combine(filesDir, Path.GetFileName(entry.FilePath));
            SpecialMenuBackup backup = FileBackup("DeleteSendTo", entry.FilePath, backupFile);
            string backupPath = Path.Combine(batchPath, "special-menu.json");
            CleanerEngine.WriteJson(backupPath, backup);
            try { File.Move(entry.FilePath, backupFile); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", FilePath = entry.FilePath }, entry.Name, entry.Module, "DeleteSendTo"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteTree(SpecialMenuEntry entry)
        {
            ActionTarget target = Target(entry.Hive, entry.View, entry.SubKey); SpecialMenuBackup backup = BackupTrees("DeleteTree", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) root.DeleteSubKeyTree(target.SubKey, false); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey }, entry.Name, entry.Module, "DeleteSpecialTree"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch DeleteValue(SpecialMenuEntry entry)
        {
            ActionTarget target = ValueTarget(Target(entry.Hive, entry.View, entry.SubKey), entry.ValueName); SpecialMenuBackup backup = BackupValues("DeleteValue", target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { DeleteRegistryValue(target, entry.ValueName); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey, ValueName = target.ValueName }, entry.Name, entry.Module, "DeleteSpecialValue"); }
            catch { RestoreBackup(backup); throw; }
        }

        private CleanupBatch SetValueWithBackup(ActionTarget keyTarget, string valueName, string value, string title, string category, string action)
        {
            ActionTarget target = ValueTarget(keyTarget, valueName); SpecialMenuBackup backup = BackupValues(action, target);
            string backupPath; string id; string batchPath; SaveBackup(backup, out backupPath, out id, out batchPath);
            try { WriteRegistryValue(target, valueName, value); return Complete(id, batchPath, backupPath, new ActionTarget { Kind = "RestoreSpecialMenu", Hive = target.Hive, View = target.View, SubKey = target.SubKey, ValueName = valueName }, title, category, action); }
            catch { RestoreBackup(backup); throw; }
        }

        private SpecialMenuBackup BackupTrees(string mode, params ActionTarget[] targets) { return new SpecialMenuBackup { Mode = mode, Trees = targets.Select(ContextMenuMutationService.CaptureTree).ToList(), Values = new List<ContextMenuToggleBackup>() }; }
        private SpecialMenuBackup BackupValues(string mode, params ActionTarget[] targets) { return new SpecialMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = targets.Select(delegate(ActionTarget target) { return ContextMenuMutationService.CaptureValue(target, mode); }).ToList() }; }
        private static SpecialMenuBackup FileBackup(string mode, string original, string changed) { return new SpecialMenuBackup { Mode = mode, Trees = new List<ContextMenuTreeBackup>(), Values = new List<ContextMenuToggleBackup>(), OriginalFile = original, ChangedFile = changed, OriginalFileExisted = File.Exists(original), ChangedFileExisted = File.Exists(changed) }; }

        private void SaveBackup(SpecialMenuBackup backup, out string backupPath, out string id, out string batchPath)
        {
            id = NewBatchId(); batchPath = Path.Combine(store.Backups, id); Directory.CreateDirectory(batchPath); backupPath = Path.Combine(batchPath, "special-menu.json"); CleanerEngine.WriteJson(backupPath, backup);
        }

        private CleanupBatch Complete(string id, string batchPath, string backupPath, ActionTarget target, string title, string category, string action)
        {
            if (string.IsNullOrWhiteSpace(target.Kind)) target.Kind = "RestoreSpecialMenu";
            CleanupResult result = new CleanupResult { Id = 1, Title = title, Vendor = "右键管理", Category = category, ActionKind = action, TechnicalLocation = string.IsNullOrWhiteSpace(target.SubKey) ? target.FilePath : RegistryHelper.NativePath(target), Status = "Done", Message = "专用菜单配置已修改。", Backup = backupPath, Target = target };
            CleanupBatch batch = new CleanupBatch { Id = id, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Path = batchPath, Results = new List<CleanupResult> { result } }; CleanerEngine.WriteJson(Path.Combine(batchPath, "manifest.json"), batch); return batch;
        }

        public static bool Restore(string backupPath)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            SpecialMenuBackup backup = serializer.Deserialize<SpecialMenuBackup>(File.ReadAllText(backupPath, Encoding.UTF8));
            return RestoreBackup(backup);
        }

        private static bool RestoreBackup(SpecialMenuBackup backup)
        {
            if (backup == null) return false;
            bool ok = true;
            foreach (ContextMenuTreeBackup tree in backup.Trees ?? new List<ContextMenuTreeBackup>()) ok = ContextMenuMutationService.RestoreTreeSnapshot(tree) && ok;
            foreach (ContextMenuToggleBackup value in backup.Values ?? new List<ContextMenuToggleBackup>()) ok = ContextMenuMutationService.RestoreValueSnapshot(value) && ok;
            if (!string.IsNullOrWhiteSpace(backup.OriginalFile) || !string.IsNullOrWhiteSpace(backup.ChangedFile))
            {
                try
                {
                    if (backup.OriginalFileExisted && !File.Exists(backup.OriginalFile))
                    {
                        string source = File.Exists(backup.ChangedFile) ? backup.ChangedFile : null;
                        if (source != null) { Directory.CreateDirectory(Path.GetDirectoryName(backup.OriginalFile)); File.Move(source, backup.OriginalFile); }
                    }
                    if (!string.IsNullOrWhiteSpace(backup.OriginalFile) && File.Exists(backup.OriginalFile) && !backup.OriginalFileExisted) File.Delete(backup.OriginalFile);
                    if (!string.IsNullOrWhiteSpace(backup.ChangedFile) && File.Exists(backup.ChangedFile) && !backup.ChangedFileExisted) File.Delete(backup.ChangedFile);
                    ok = File.Exists(backup.OriginalFile) == backup.OriginalFileExisted && File.Exists(backup.ChangedFile) == backup.ChangedFileExisted && ok;
                }
                catch { ok = false; }
            }
            return ok;
        }

        private static void MoveRegistryTree(ActionTarget source, ActionTarget destination)
        {
            ContextMenuTreeBackup snapshot = ContextMenuMutationService.CaptureTree(source);
            if (!snapshot.KeyExisted) throw new InvalidOperationException("源注册表项不存在。");
            ContextMenuTreeBackup destinationSnapshot = new ContextMenuTreeBackup { Target = destination, KeyExisted = true, Snapshot = snapshot.Snapshot };
            ContextMenuMutationService.RestoreTreeSnapshot(destinationSnapshot);
            using (RegistryKey root = RegistryHelper.OpenBase(source.Hive, source.View, true)) root.DeleteSubKeyTree(source.SubKey, false);
        }

        private static object ReadRegistryValue(ActionTarget target, string name) { using (RegistryKey key = RegistryHelper.OpenSubKey(target, false)) return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
        private static void WriteRegistryValue(ActionTarget target, string name, object value) { using (RegistryKey root = RegistryHelper.OpenBase(target.Hive, target.View, true)) using (RegistryKey key = root.CreateSubKey(target.SubKey, RegistryKeyPermissionCheck.ReadWriteSubTree)) key.SetValue(name, value ?? string.Empty); }
        private static void DeleteRegistryValue(ActionTarget target, string name) { using (RegistryKey key = RegistryHelper.OpenSubKey(target, true)) if (key != null) key.DeleteValue(name, false); }
        private static void UpdateMru(ActionTarget target, string token, bool add)
        {
            object raw = ReadRegistryValue(target, "MRUList");
            string current = Convert.ToString(raw) ?? string.Empty;
            current = current.Replace(token, string.Empty);
            if (add) current = token + current;
            if (current.Length == 0) DeleteRegistryValue(target, "MRUList"); else WriteRegistryValue(target, "MRUList", current);
        }
        private static ActionTarget Target(string hive, string view, string subKey) { return new ActionTarget { Hive = hive, View = view, SubKey = subKey }; }
        private static ActionTarget ValueTarget(ActionTarget target, string name) { target.ValueName = name; return target; }
        private static string DefaultView() { return Environment.Is64BitOperatingSystem ? "Registry64" : "Default"; }
        private static string NormalizeExtension(string value) { string extension = (value ?? string.Empty).Trim(); if (!extension.StartsWith(".", StringComparison.Ordinal)) extension = "." + extension; if (extension.Length < 2 || extension.Length > 24 || extension.IndexOfAny(new char[] { '\\', '/', ' ', ':' }) >= 0) throw new ArgumentException("文件扩展名无效。"); return extension; }
        private static string SafeFileName(string value) { string name = value.Trim(); foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name; }
        private static string NewBatchId() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }
        private static void EnsurePermission(SpecialMenuEntry entry) { if (entry == null) throw new ArgumentNullException("entry"); if (entry.ReadOnly) throw new InvalidOperationException("该项目为只读。"); if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) throw new UnauthorizedAccessException("该项目需要管理员权限。"); }
    }

    internal sealed class SpecialContextMenuForm : Form
    {
        private readonly DataStore store;
        private readonly BindingList<SpecialMenuEntry> rows = new BindingList<SpecialMenuEntry>();
        private readonly DataGridView grid = new BufferedDataGridView();
        private readonly ComboBox module = new ComboBox();
        private readonly Label status = new Label();
        private readonly Button refreshButton = new Button();
        private readonly Button enableButton = new Button();
        private readonly Button disableButton = new Button();
        private readonly Button deleteButton = new Button();
        private SpecialMenuInventory inventory;

        public SpecialContextMenuForm(DataStore store)
        {
            this.store = store; Text = "右键专用模块"; StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(980, 620); Size = new Size(1160, 700); BackColor = UiTheme.Canvas; Font = UiTheme.Font(9F, FontStyle.Regular); UiTheme.ApplyWindowIdentity(this); BuildUi(); Shown += delegate { RefreshRows(); };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) }; root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); Controls.Add(root);
            root.Controls.Add(UiTheme.ModuleHeader("更多右键位置", "管理“新建”“发送到”“打开方式”以及组件屏蔽等扩展入口"), 0, 0);
            FlowLayoutPanel filter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = UiTheme.Surface, Padding = new Padding(8, 6, 8, 6) }; module.DropDownStyle = ComboBoxStyle.DropDownList; module.Width = 220; module.Items.AddRange(new object[] { "全部模块", "新建菜单", "发送到菜单", "打开方式", "打开方式应用程序", "组件屏蔽" }); module.SelectedIndex = 0; filter.Controls.Add(new Label { Text = "显示模块", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(0, 5, 10, 0) }); filter.Controls.Add(module); root.Controls.Add(filter, 0, 1);
            FlowLayoutPanel bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 7, 0, 7) }; Button add = new Button(); UiTheme.ToolButton(refreshButton, "刷新列表", SystemIcons.Information); UiTheme.ToolButton(enableButton, "显示选中", SystemIcons.Shield); UiTheme.ToolButton(disableButton, "隐藏选中", SystemIcons.Warning); UiTheme.ToolButton(add, "添加项目", SystemIcons.Information); UiTheme.ToolButton(deleteButton, "删除项目", SystemIcons.Error); bar.Controls.Add(refreshButton); bar.Controls.Add(enableButton); bar.Controls.Add(disableButton); bar.Controls.Add(add); bar.Controls.Add(deleteButton); root.Controls.Add(bar, 0, 2);
            grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.DataSource = rows; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.BackgroundColor = UiTheme.Surface; grid.RowTemplate.Height = 34; grid.Columns.Add(new DataGridViewImageColumn { DataPropertyName = "SoftwareIcon", HeaderText = "", Width = 42, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ImageLayout = DataGridViewImageCellLayout.Normal }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "状态", FillWeight = 55 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "名称", FillWeight = 150 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "SoftwareName", HeaderText = "关联软件", FillWeight = 110 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ModuleDisplay", HeaderText = "模块", FillWeight = 100 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Detail", HeaderText = "详情", FillWeight = 180 }); grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Scope", HeaderText = "范围", FillWeight = 90 }); Panel gridHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface }; UiTheme.AttachModernScrollBar(gridHost, grid); root.Controls.Add(gridHost, 0, 3);
            status.Dock = DockStyle.Fill; status.ForeColor = UiTheme.Muted; root.Controls.Add(status, 0, 4);
            refreshButton.Click += delegate { RefreshRows(); }; module.SelectedIndexChanged += delegate { ApplyFilter(); }; enableButton.Click += delegate { Toggle(true); }; disableButton.Click += delegate { Toggle(false); }; deleteButton.Click += delegate { DeleteCurrent(); }; add.Click += delegate { AddCurrentModule(); }; grid.SelectionChanged += delegate { UpdateActions(); };
        }

        private static Button ButtonOf(string text, Color color) { Button button = new Button(); UiTheme.OutlineButton(button, text, color); return button; }
        private SpecialMenuEntry Current() { return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as SpecialMenuEntry; }
        private void RefreshRows()
        {
            if (!refreshButton.Enabled) return;
            refreshButton.Enabled = false; status.Text = "正在枚举专用模块……";
            Task.Factory.StartNew(delegate { return new SpecialMenuInventoryService(store).Enumerate(); }).ContinueWith(delegate(Task<SpecialMenuInventory> task)
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    refreshButton.Enabled = true;
                    if (task.IsFaulted) { Exception ex = task.Exception == null ? new InvalidOperationException("未知枚举错误。") : task.Exception.GetBaseException(); Logger.Error("专用模块枚举失败", ex); MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                    inventory = task.Result; foreach (SpecialMenuEntry entry in inventory.Entries) { entry.SoftwareIcon = SoftwarePresentationResolver.PlaceholderIcon; entry.SoftwareName = "正在识别…"; } ApplyFilter(); SoftwarePresentationQueue.Hydrate(this, inventory.Entries, delegate { grid.Invalidate(); }); status.Text = "共发现 " + inventory.Entries.Count + " 项；" + inventory.Warnings.Count + " 个位置未读取。";
                });
            });
        }
        private void ApplyFilter() { if (inventory == null) return; string selected = SpecialMenuDisplay.Key(Convert.ToString(module.SelectedItem)); rows.RaiseListChangedEvents = false; rows.Clear(); foreach (SpecialMenuEntry entry in inventory.Entries) if (selected == "全部模块" || entry.Module == selected) rows.Add(entry); rows.RaiseListChangedEvents = true; rows.ResetBindings(); UpdateActions(); }
        private void UpdateActions() { SpecialMenuEntry entry = Current(); enableButton.Enabled = entry != null && !entry.ReadOnly && !entry.Enabled; disableButton.Enabled = entry != null && !entry.ReadOnly && entry.Enabled; deleteButton.Enabled = entry != null && !entry.ReadOnly && entry.Module != "OpenWith 应用程序"; }
        private void Toggle(bool enabled) { SpecialMenuEntry entry = Current(); if (entry == null || (entry.RequiresAdmin && !EnsureAdministrator())) return; try { new SpecialContextMenuMutationService(store).SetEnabled(entry, enabled); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void DeleteCurrent() { SpecialMenuEntry entry = Current(); if (entry == null || (entry.RequiresAdmin && !EnsureAdministrator())) return; if (MessageBox.Show(this, "删除“" + entry.Name + "”？操作前会备份。", "删除专用菜单项", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; try { new SpecialContextMenuMutationService(store).Delete(entry); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private void AddCurrentModule() { string selected = SpecialMenuDisplay.Key(Convert.ToString(module.SelectedItem)); if (selected == "全部模块" || selected == "OpenWith 应用程序") { MessageBox.Show(this, "请先选择新建菜单、发送到菜单、打开方式或组件屏蔽。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; } using (SpecialMenuAddForm form = new SpecialMenuAddForm(selected)) { if (form.ShowDialog(this) != DialogResult.OK) return; try { SpecialContextMenuMutationService service = new SpecialContextMenuMutationService(store); if (selected == "ShellNew 新建菜单") service.AddShellNew(form.FirstValue, form.SecondValue); else if (selected == "SendTo 发送到") service.AddSendTo(form.FirstValue, form.SecondValue); else if (selected == "OpenWith 打开方式") service.AddOpenWith(form.FirstValue, form.SecondValue); else service.AddBlockedGuid(form.FirstValue, form.SecondValue); RefreshRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } } }
        private bool EnsureAdministrator() { if (AdminUtil.IsAdministrator()) return true; if (MessageBox.Show(this, "该项目属于所有用户范围，需要管理员权限。是否请求 Windows 管理员权限？\n\n重启后会重新打开右键管理，不会自动修改项目。", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) AdminUtil.RelaunchAsAdmin(this, store, new ElevationResumeState { Page = "右键", OpenContextMenu = true }); return false; }
    }

    internal sealed class SpecialMenuAddForm : Form
    {
        private readonly TextBox first = new TextBox(); private readonly TextBox second = new TextBox();
        public string FirstValue { get { return first.Text.Trim(); } } public string SecondValue { get { return second.Text.Trim(); } }
        public SpecialMenuAddForm(string module)
        {
            Text = "添加 " + SpecialMenuDisplay.Name(module); StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(620, 230); BackColor = UiTheme.Surface; Font = UiTheme.Font(9F, FontStyle.Regular); UiTheme.ApplyWindowIdentity(this);
            string firstLabel = module.StartsWith("ShellNew") || module.StartsWith("OpenWith") ? "文件扩展名" : module.StartsWith("SendTo") ? "显示名称" : "组件编号";
            string secondLabel = module.StartsWith("ShellNew") ? "模板文件（可空）" : module.StartsWith("OpenWith") ? "程序关联标识" : module.StartsWith("SendTo") ? "目标路径" : "说明（可空）";
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(22) }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); Controls.Add(root);
            root.Controls.Add(new Label { Text = firstLabel, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); first.Dock = DockStyle.Fill; first.Margin = new Padding(0, 10, 0, 10); root.Controls.Add(first, 1, 0); root.Controls.Add(new Label { Text = secondLabel, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); second.Dock = DockStyle.Fill; second.Margin = new Padding(0, 10, 0, 10); root.Controls.Add(second, 1, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; Button cancel = new Button(); UiTheme.OutlineButton(cancel, "取消", UiTheme.Muted); cancel.DialogResult = DialogResult.Cancel; Button ok = new Button(); UiTheme.PrimaryButton(ok, "添加", UiTheme.Primary); ok.Click += delegate { if (string.IsNullOrWhiteSpace(first.Text) || (!module.StartsWith("ShellNew") && !module.StartsWith("GUID") && string.IsNullOrWhiteSpace(second.Text))) { MessageBox.Show(this, "请填写必填项。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; } DialogResult = DialogResult.OK; }; actions.Controls.Add(cancel); actions.Controls.Add(ok); root.Controls.Add(actions, 1, 2); AcceptButton = ok; CancelButton = cancel;
        }
    }

#if VALIDATION
    internal static class SpecialContextMenuRegression
    {
        private const string Extension = ".codexroguecleanertest";
        private const string ProgId = "CodexRogueCleanerTest.OpenWith";
        private const string TestGuid = "{C0DE2026-0806-4A20-8A00-BA1D0E7D15C1}";
        private const string SendToName = "CodexRogueCleanerTest SendTo";

        public static List<string> Run(DataStore store)
        {
            List<string> failures = new List<string>();
            List<CleanupBatch> batches = new List<CleanupBatch>();
            string view = Environment.Is64BitOperatingSystem ? "Registry64" : "Default";
            string sendToFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), SendToName + ".lnk");
            try
            {
                SpecialContextMenuMutationService service = new SpecialContextMenuMutationService(store);
                CleanupBatch shellNewAdd = service.AddShellNew(Extension, string.Empty); batches.Add(shellNewAdd);
                SpecialMenuEntry shellNew = Find(store, "ShellNew 新建菜单", Extension, true);
                if (shellNew == null) failures.Add("ShellNew 添加后未被枚举。");
                else
                {
                    CleanupBatch toggle = service.SetEnabled(shellNew, false); batches.Add(toggle);
                    if (Find(store, "ShellNew 新建菜单", Extension, false) == null) failures.Add("ShellNew 禁用后未进入禁用状态。");
                    if (!new CleanerEngine(store).RestoreBatch(toggle).AllSucceeded || Find(store, "ShellNew 新建菜单", Extension, true) == null) failures.Add("ShellNew 禁用操作恢复失败。");
                }

                CleanupBatch openWithAdd = service.AddOpenWith(Extension, ProgId); batches.Add(openWithAdd);
                SpecialMenuEntry openWith = Find(store, "OpenWith 打开方式", ProgId, true);
                if (openWith == null) failures.Add("OpenWith 添加后未被枚举。");
                else
                {
                    CleanupBatch toggle = service.SetEnabled(openWith, false); batches.Add(toggle);
                    if (Find(store, "OpenWith 打开方式", ProgId, false) == null) failures.Add("OpenWith 禁用后未进入禁用状态。");
                    if (!new CleanerEngine(store).RestoreBatch(toggle).AllSucceeded || Find(store, "OpenWith 打开方式", ProgId, true) == null) failures.Add("OpenWith 禁用操作恢复失败。");
                }

                CleanupBatch guidAdd = service.AddBlockedGuid(TestGuid, "Codex GUID 屏蔽回归"); batches.Add(guidAdd);
                SpecialMenuEntry blocked = Find(store, "GUID 屏蔽", TestGuid, false);
                if (blocked == null) failures.Add("GUID 屏蔽添加后未被枚举。");
                else
                {
                    CleanupBatch toggle = service.SetEnabled(blocked, true); batches.Add(toggle);
                    if (Find(store, "GUID 屏蔽", TestGuid, false) != null) failures.Add("GUID 解除屏蔽后仍存在。");
                    if (!new CleanerEngine(store).RestoreBatch(toggle).AllSucceeded || Find(store, "GUID 屏蔽", TestGuid, false) == null) failures.Add("GUID 解除屏蔽操作恢复失败。");
                }

                CleanupBatch sendToAdd = service.AddSendTo(SendToName, Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\notepad.exe"); batches.Add(sendToAdd);
                SpecialMenuEntry sendTo = Find(store, "SendTo 发送到", SendToName, true);
                if (sendTo == null) failures.Add("SendTo 添加后未被枚举。");
                else
                {
                    CleanupBatch toggle = service.SetEnabled(sendTo, false); batches.Add(toggle);
                    if (Find(store, "SendTo 发送到", SendToName, false) == null) failures.Add("SendTo 禁用后未进入禁用区。");
                    if (!new CleanerEngine(store).RestoreBatch(toggle).AllSucceeded || Find(store, "SendTo 发送到", SendToName, true) == null) failures.Add("SendTo 禁用操作恢复失败。");
                }

                foreach (CleanupBatch addBatch in new CleanupBatch[] { sendToAdd, guidAdd, openWithAdd, shellNewAdd })
                {
                    if (!new CleanerEngine(store).RestoreBatch(addBatch).AllSucceeded) failures.Add(addBatch.Results[0].Category + " 添加操作恢复失败。");
                }
            }
            catch (Exception ex) { failures.Add("专用模块回归异常：" + ex); }
            finally
            {
                try { using (RegistryKey root = RegistryHelper.OpenBase("HKCU", view, true)) root.DeleteSubKeyTree(@"Software\Classes\" + Extension, false); } catch { }
                try { using (RegistryKey key = RegistryHelper.OpenSubKey(new ActionTarget { Hive = "HKCU", View = view, SubKey = SpecialMenuInventoryService.BlockedPath }, true)) if (key != null) key.DeleteValue(TestGuid, false); } catch { }
                try { if (File.Exists(sendToFile)) File.Delete(sendToFile); } catch { }
                try { string disabled = Path.Combine(SpecialMenuInventoryService.DisabledSendToDirectory(store), Path.GetFileName(sendToFile)); if (File.Exists(disabled)) File.Delete(disabled); } catch { }
                foreach (CleanupBatch batch in batches) try { if (Directory.Exists(batch.Path)) new CleanerEngine(store).DeleteBatchRecord(batch); } catch { }
            }
            return failures;
        }

        private static SpecialMenuEntry Find(DataStore store, string module, string namePart, bool enabled)
        {
            return new SpecialMenuInventoryService(store).Enumerate().Entries.FirstOrDefault(delegate(SpecialMenuEntry entry)
            {
                return entry.Module == module && entry.Enabled == enabled && entry.Name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }
    }
#endif
}
