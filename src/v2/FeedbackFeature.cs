using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal sealed class RecognitionFeedbackReport
    {
        public string SchemaVersion { get; set; }
        public string CreatedAt { get; set; }
        public string FeedbackType { get; set; }
        public string ExpectedResult { get; set; }
        public string ProductVersion { get; set; }
        public string WindowsVersion { get; set; }
        public string Architecture { get; set; }
        public string Category { get; set; }
        public string CurrentRisk { get; set; }
        public string CurrentAction { get; set; }
        public string Vendor { get; set; }
        public string VisibleName { get; set; }
        public string UserImpact { get; set; }
        public string TechnicalLocation { get; set; }
        public string Evidence { get; set; }
        public string FileName { get; set; }
        public string FileSha256 { get; set; }
    }

    internal sealed class SavedFeedback
    {
        public string MarkdownPath { get; set; }
        public string JsonPath { get; set; }
        public string Markdown { get; set; }
        public string IssueUrl { get; set; }
    }

    internal static class FeedbackService
    {
        private const string HiddenUser = "%USERPROFILE%";
        private const string HiddenTemp = "%TEMP%";
        private const string HiddenAccount = "[账号已隐藏]";
        private const string HiddenUrl = "[URL已隐藏]";
        private const string HiddenNetwork = "[网络地址已隐藏]";
        private const string HiddenSecret = "[敏感参数已隐藏]";

        public static RecognitionFeedbackReport CreateReport(Finding finding, string feedbackType, string expectedResult, bool includeHash)
        {
            if (finding == null) throw new ArgumentNullException("finding");

            string filePath = finding.Target == null ? string.Empty : finding.Target.FilePath;
            RecognitionFeedbackReport report = new RecognitionFeedbackReport();
            report.SchemaVersion = "1";
            report.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
            report.FeedbackType = Sanitize(TrimTo(feedbackType, 40));
            report.ExpectedResult = Sanitize(TrimTo(expectedResult, 2000));
            report.ProductVersion = AppMeta.Version;
            report.WindowsVersion = Sanitize(Environment.OSVersion.VersionString);
            report.Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            report.Category = Sanitize(finding.Category);
            report.CurrentRisk = Sanitize(finding.RiskDisplay);
            report.CurrentAction = Sanitize(finding.ActionText);
            report.Vendor = Sanitize(finding.Vendor);
            report.VisibleName = Sanitize(finding.UserVisibleName);
            report.UserImpact = Sanitize(finding.UserImpact);
            report.TechnicalLocation = Sanitize(finding.TechnicalLocation);
            report.Evidence = Sanitize(finding.Evidence);
            report.FileName = string.IsNullOrWhiteSpace(filePath) ? string.Empty : Sanitize(Path.GetFileName(filePath));
            report.FileSha256 = includeHash ? TryHashFile(filePath) : string.Empty;
            return report;
        }

        public static string BuildMarkdown(RecognitionFeedbackReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("## 识别反馈");
            text.AppendLine();
            AppendField(text, "反馈类型", report.FeedbackType);
            AppendField(text, "用户期望", report.ExpectedResult);
            AppendField(text, "软件版本", report.ProductVersion);
            AppendField(text, "Windows", report.WindowsVersion + " / " + report.Architecture);
            text.AppendLine();
            text.AppendLine("## 当前判断");
            text.AppendLine();
            AppendField(text, "类别", report.Category);
            AppendField(text, "风险/展示", report.CurrentRisk);
            AppendField(text, "动作", report.CurrentAction);
            AppendField(text, "厂商", report.Vendor);
            AppendField(text, "显示名称", report.VisibleName);
            AppendField(text, "影响说明", report.UserImpact);
            AppendField(text, "技术位置", report.TechnicalLocation);
            AppendField(text, "证据", report.Evidence);
            AppendField(text, "文件名", report.FileName);
            if (!string.IsNullOrWhiteSpace(report.FileSha256)) AppendField(text, "文件 SHA256", report.FileSha256);
            text.AppendLine();
            text.AppendLine("> 本报告由流氓软件克星在本地生成并脱敏。提交前请再次检查；GitHub Issue 只作为待验证样本，不会被客户端直接执行。 ");
            return text.ToString();
        }

        public static SavedFeedback Save(DataStore store, RecognitionFeedbackReport report)
        {
            if (store == null) throw new ArgumentNullException("store");
            Directory.CreateDirectory(store.Feedbacks);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string baseName = "recognition-feedback-" + stamp;
            string markdownPath = Path.Combine(store.Feedbacks, baseName + ".md");
            string jsonPath = Path.Combine(store.Feedbacks, baseName + ".json");
            string markdown = BuildMarkdown(report);
            File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
            CleanerEngine.WriteJson(jsonPath, report);
            return new SavedFeedback
            {
                MarkdownPath = markdownPath,
                JsonPath = jsonPath,
                Markdown = markdown,
                IssueUrl = BuildIssueUrl(report)
            };
        }

        public static string BuildIssueUrl(RecognitionFeedbackReport report)
        {
            string title = "[识别反馈][" + SafeTitle(report.FeedbackType) + "] " + SafeTitle(report.VisibleName);
            if (title.Length > 120) title = title.Substring(0, 120);
            return AppMeta.Repository + "/issues/new?template=recognition-feedback.yml&title=" + Uri.EscapeDataString(title);
        }

        internal static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Replace("\0", string.Empty);

            text = ReplaceKnown(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), HiddenUser);
            text = ReplaceKnown(text, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), HiddenTemp);
            text = ReplaceKnown(text, Environment.UserName, "[用户名已隐藏]");
            text = ReplaceKnown(text, Environment.MachineName, "[计算机名已隐藏]");
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) text = ReplaceKnown(text, identity.User.Value, "[SID已隐藏]");
            }
            catch
            {
            }

            text = Regex.Replace(text, @"(?i)[a-z]:\\users\\[^\\\s;\""']+", HiddenUser);
            text = Regex.Replace(text, @"(?i)(https?|ftp)://[^\s<>\""']+", HiddenUrl);
            text = Regex.Replace(text, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", HiddenAccount);
            text = Regex.Replace(text, @"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?", HiddenNetwork);
            text = Regex.Replace(text, @"(?i)\b(token|access_token|refresh_token|authorization|password|passwd|secret|apikey|api_key)\s*[:=]\s*[^\s;&]+", "$1=" + HiddenSecret);
            text = Regex.Replace(text, @"(?i)(--?(?:token|password|passwd|secret|api-key|apikey)\s+)[^\s]+", "$1" + HiddenSecret);
            return TrimTo(text.Trim(), 6000);
        }

        public static List<string> RunSelfTests(DataStore store)
        {
            List<string> failures = new List<string>();
            string sid = string.Empty;
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity != null && identity.User != null) sid = identity.User.Value;
            }
            catch
            {
            }

            Finding sample = new Finding
            {
                Risk = "高",
                Vendor = "测试厂商",
                Category = "开机启动",
                UserVisibleName = "测试程序",
                UserImpact = "联系 alice@example.com，访问 https://example.com/private",
                TechnicalLocation = @"C:\Users\Alice\Documents\test.exe 192.168.1.9:8080",
                Evidence = "machine=" + Environment.MachineName + "; user=" + Environment.UserName + "; sid=" + sid + "; token=secret-token",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly", FilePath = @"C:\Users\Alice\Documents\test.exe" }
            };
            RecognitionFeedbackReport report = CreateReport(sample, "误报", "这是正常软件", false);
            string all = new JavaScriptSerializer().Serialize(report) + BuildMarkdown(report);
            AssertMissing(failures, all, "Alice", "用户目录");
            AssertMissing(failures, all, "alice@example.com", "邮箱");
            AssertMissing(failures, all, "https://example.com/private", "URL");
            AssertMissing(failures, all, "192.168.1.9", "网络地址");
            AssertMissing(failures, all, "secret-token", "令牌");
            if (!string.IsNullOrWhiteSpace(Environment.UserName)) AssertMissing(failures, all, Environment.UserName, "当前用户名");
            if (!string.IsNullOrWhiteSpace(Environment.MachineName)) AssertMissing(failures, all, Environment.MachineName, "当前计算机名");
            if (!string.IsNullOrWhiteSpace(sid)) AssertMissing(failures, all, sid, "当前 SID");
            if (all.IndexOf(HiddenUser, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("用户目录没有替换为占位符");
            if (all.IndexOf(HiddenUrl, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("URL 没有替换为占位符");
            if (all.IndexOf(HiddenNetwork, StringComparison.OrdinalIgnoreCase) < 0) failures.Add("网络地址没有替换为占位符");
            SavedFeedback saved = null;
            try
            {
                saved = Save(store, report);
                string diskText = File.ReadAllText(saved.MarkdownPath, Encoding.UTF8) + File.ReadAllText(saved.JsonPath, Encoding.UTF8);
                AssertMissing(failures, diskText, "Alice", "落盘用户目录");
                AssertMissing(failures, diskText, "secret-token", "落盘令牌");
                using (FeedbackForm form = new FeedbackForm(store, sample))
                {
                    form.CreateControl();
                    if (form.ClientSize.Width < 700 || form.ClientSize.Height < 560) failures.Add("反馈窗口最小可用区域不足");
                }
            }
            catch (Exception ex)
            {
                failures.Add("反馈落盘或窗口构造失败：" + ex.Message);
            }
            finally
            {
                TryDelete(saved == null ? null : saved.MarkdownPath);
                TryDelete(saved == null ? null : saved.JsonPath);
            }
            return failures;
        }

        private static void AssertMissing(List<string> failures, string text, string secret, string label)
        {
            if (!string.IsNullOrEmpty(secret) && text.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0) failures.Add(label + "仍出现在反馈报告中");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static string ReplaceKnown(string value, string secret, string replacement)
        {
            if (string.IsNullOrWhiteSpace(secret)) return value;
            return Regex.Replace(value, Regex.Escape(secret), replacement, RegexOptions.IgnoreCase);
        }

        private static string TryHashFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return string.Empty;
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 hash = SHA256.Create())
                {
                    return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendField(StringBuilder text, string label, string value)
        {
            text.Append("- **").Append(label).Append("**：");
            text.AppendLine(string.IsNullOrWhiteSpace(value) ? "未提供" : value.Replace("\r", " ").Replace("\n", " "));
        }

        private static string SafeTitle(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "未命名" : value;
            return text.Replace("\r", " ").Replace("\n", " ").Replace("[", "（").Replace("]", "）").Trim();
        }

        private static string TrimTo(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? string.Empty;
            return value.Substring(0, maxLength) + "…";
        }
    }

    internal sealed class FeedbackForm : Form
    {
        private readonly DataStore store;
        private readonly Finding finding;
        private readonly RadioButton falsePositiveType = new RadioButton();
        private readonly RadioButton missedType = new RadioButton();
        private readonly RadioButton identityType = new RadioButton();
        private readonly RadioButton relationType = new RadioButton();
        private readonly TextBox expectedBox = new TextBox();
        private readonly CheckBox hashBox = new CheckBox();
        private readonly TextBox previewBox = new TextBox();
        private readonly Button githubButton = new Button();
        private readonly Button localButton = new Button();
        private readonly Button closeButton = new Button();
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel();
        private readonly FlowLayoutPanel typeOptions = new FlowLayoutPanel();
        private readonly FlowLayoutPanel actionButtons = new FlowLayoutPanel();
        private bool applyingResponsiveLayout;

        public FeedbackForm(DataStore store, Finding finding)
        {
            this.store = store;
            this.finding = finding;
            UiTheme.ApplyWindowIdentity(this);
            BuildUi();
            UpdatePreview();
        }

        private void BuildUi()
        {
            Text = "反馈识别问题";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            // A 1920 x 1080 work area at 200% has about 960 x 540 logical pixels.
            // Keep the whole feedback flow, including its submit actions, usable there.
            MinimumSize = new Size(760, 500);
            Size = new Size(920, 740);
            Font = UiTheme.Font(9F, FontStyle.Regular);
            BackColor = UiTheme.Canvas;

            rootLayout.Dock = DockStyle.Fill;
            rootLayout.Padding = new Padding(16);
            rootLayout.ColumnCount = 1;
            rootLayout.RowCount = 6;
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            Controls.Add(rootLayout);

            Panel heading = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Canvas };
            Label title = new Label { Text = "反馈识别问题", Dock = DockStyle.Top, Height = 32, ForeColor = UiTheme.Text, Font = UiTheme.Font(15F, FontStyle.Bold) };
            Label privacy = new Label { Text = "只生成当前项目的脱敏报告，提交前由你检查；不会在后台直接发送。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.5F, FontStyle.Regular) };
            heading.Controls.Add(privacy);
            heading.Controls.Add(title);
            rootLayout.Controls.Add(heading, 0, 0);

            typeOptions.Dock = DockStyle.Fill;
            typeOptions.FlowDirection = FlowDirection.LeftToRight;
            typeOptions.WrapContents = true;
            typeOptions.AutoScroll = false;
            typeOptions.Margin = new Padding(0);
            typeOptions.Padding = new Padding(0, 6, 0, 6);
            StyleTypeOption(falsePositiveType, "误报", "正常软件被误判");
            StyleTypeOption(missedType, "漏报", "项目没有被识别");
            StyleTypeOption(identityType, "身份错误", "厂商或产品有误");
            StyleTypeOption(relationType, "关联错误", "组件分组不正确");
            falsePositiveType.Checked = true;
            typeOptions.Controls.Add(falsePositiveType);
            typeOptions.Controls.Add(missedType);
            typeOptions.Controls.Add(identityType);
            typeOptions.Controls.Add(relationType);
            rootLayout.Controls.Add(typeOptions, 0, 1);

            CardPanel expectedCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(12, 9, 12, 10) };
            Label expectedTitle = new Label { Text = "你的判断", Dock = DockStyle.Top, Height = 25, ForeColor = UiTheme.Primary, Font = UiTheme.Font(9F, FontStyle.Bold) };
            expectedBox.Dock = DockStyle.Fill;
            expectedBox.Multiline = true;
            expectedBox.ScrollBars = ScrollBars.Vertical;
            expectedBox.MaxLength = 2000;
            expectedBox.Text = "请说明它为什么是正常软件，或你期望工具如何判断。";
            expectedBox.BorderStyle = BorderStyle.None;
            expectedBox.BackColor = UiTheme.Surface;
            expectedBox.ForeColor = UiTheme.Text;
            expectedCard.Controls.Add(expectedBox);
            expectedCard.Controls.Add(expectedTitle);
            rootLayout.Controls.Add(expectedCard, 0, 2);

            hashBox.Dock = DockStyle.Fill;
            hashBox.Text = "包含目标文件 SHA256（默认不包含，不上传文件）";
            hashBox.ForeColor = UiTheme.Muted;
            hashBox.Padding = new Padding(4, 0, 0, 0);
            rootLayout.Controls.Add(hashBox, 0, 3);

            CardPanel previewCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(12, 9, 12, 10) };
            Label previewTitle = new Label { Text = "脱敏预览  ·  下面是将复制到公开 GitHub Issue 的全部内容", Dock = DockStyle.Top, Height = 27, ForeColor = UiTheme.Primary, Font = UiTheme.Font(9F, FontStyle.Bold) };
            previewBox.Dock = DockStyle.Fill;
            previewBox.Multiline = true;
            previewBox.ReadOnly = true;
            previewBox.ScrollBars = ScrollBars.Both;
            previewBox.WordWrap = false;
            previewBox.BackColor = UiTheme.Surface;
            previewBox.ForeColor = UiTheme.Text;
            previewBox.BorderStyle = BorderStyle.None;
            previewBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
            previewCard.Controls.Add(previewBox);
            previewCard.Controls.Add(previewTitle);
            rootLayout.Controls.Add(previewCard, 0, 4);

            actionButtons.Dock = DockStyle.Fill;
            actionButtons.FlowDirection = FlowDirection.RightToLeft;
            actionButtons.WrapContents = false;
            actionButtons.AutoScroll = false;
            rootLayout.Controls.Add(actionButtons, 0, 5);
            UiTheme.OutlineButton(closeButton, "关闭", UiTheme.Muted);
            UiTheme.OutlineButton(localButton, "仅保存本地", UiTheme.Primary);
            UiTheme.PrimaryButton(githubButton, "复制并打开 GitHub", UiTheme.Primary);
            closeButton.MinimumSize = new Size(90, 40);
            localButton.MinimumSize = new Size(128, 40);
            githubButton.MinimumSize = new Size(188, 40);
            closeButton.Margin = localButton.Margin = githubButton.Margin = new Padding(8, 6, 0, 0);
            actionButtons.Controls.Add(closeButton);
            actionButtons.Controls.Add(localButton);
            actionButtons.Controls.Add(githubButton);

            falsePositiveType.CheckedChanged += delegate { UpdatePreview(); };
            missedType.CheckedChanged += delegate { UpdatePreview(); };
            identityType.CheckedChanged += delegate { UpdatePreview(); };
            relationType.CheckedChanged += delegate { UpdatePreview(); };
            expectedBox.TextChanged += delegate { UpdatePreview(); };
            hashBox.CheckedChanged += delegate { UpdatePreview(); };
            closeButton.Click += delegate { Close(); };
            localButton.Click += delegate { SaveFeedback(false); };
            githubButton.Click += delegate { SaveFeedback(true); };
            SizeChanged += delegate { ApplyResponsiveLayout(); };
            rootLayout.SizeChanged += delegate { ApplyResponsiveLayout(); };
            typeOptions.SizeChanged += delegate { ApplyResponsiveLayout(); };
            Shown += delegate
            {
                FitToWorkingArea();
                ApplyResponsiveLayout();
            };
        }

        private void FitToWorkingArea()
        {
            try
            {
                int logicalWorkHeight = UiTheme.LogicalPixels(this, Screen.FromControl(this).WorkingArea.Height);
                int maximumLogicalHeight = Math.Max(500, logicalWorkHeight - 16);
                int maximumHeight = UiTheme.DpiPixels(this, maximumLogicalHeight);
                if (Height > maximumHeight && maximumHeight >= MinimumSize.Height) Height = maximumHeight;
            }
            catch { }
        }

        private void ApplyResponsiveLayout()
        {
            if (applyingResponsiveLayout || rootLayout.IsDisposed || typeOptions.IsDisposed || typeOptions.ClientSize.Width <= 0) return;
            applyingResponsiveLayout = true;
            try
            {
                int logicalClientHeight = UiTheme.LogicalPixels(this, Math.Max(1, ClientSize.Height));
                bool compact = logicalClientHeight <= 540;
                int horizontalPadding = UiTheme.DpiPixels(this, compact ? 12 : 20);
                int verticalPadding = UiTheme.DpiPixels(this, compact ? 10 : 20);
                rootLayout.Padding = new Padding(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);
                rootLayout.RowStyles[0].Height = UiTheme.DpiPixels(this, compact ? 58 : 64);
                rootLayout.RowStyles[2].Height = UiTheme.DpiPixels(this, compact ? 100 : 114);
                rootLayout.RowStyles[3].Height = UiTheme.DpiPixels(this, compact ? 32 : 42);
                rootLayout.RowStyles[5].Height = UiTheme.DpiPixels(this, compact ? 54 : 58);

                // At normal widths all four choices stay in one row.  Near the
                // compact minimum they become two rows, and the table row grows
                // with them so no choice is hidden behind a scroll bar.
                int typePadding = UiTheme.DpiPixels(this, compact ? 4 : 8);
                int typeGap = UiTheme.DpiPixels(this, 8);
                typeOptions.Padding = new Padding(0, typePadding, 0, typePadding);
                rootLayout.PerformLayout();
                int logicalAvailableWidth = UiTheme.LogicalPixels(this, Math.Max(1, typeOptions.ClientSize.Width - typeOptions.Padding.Horizontal));
                int columns = logicalAvailableWidth >= 820 ? 4 : logicalAvailableWidth >= 360 ? 2 : 1;
                int logicalOptionWidth = Math.Max(160, (logicalAvailableWidth - columns * 8) / columns);
                int optionWidth = UiTheme.DpiPixels(this, logicalOptionWidth);
                int optionHeight = UiTheme.DpiPixels(this, compact ? 52 : 58);
                ConfigureTypeOptionLayout(falsePositiveType, optionWidth, optionHeight, typeGap, compact);
                ConfigureTypeOptionLayout(missedType, optionWidth, optionHeight, typeGap, compact);
                ConfigureTypeOptionLayout(identityType, optionWidth, optionHeight, typeGap, compact);
                ConfigureTypeOptionLayout(relationType, optionWidth, optionHeight, typeGap, compact);

                int required = UiTheme.RequiredFlowLayoutHeight(typeOptions);
                rootLayout.RowStyles[1].Height = Math.Max(UiTheme.DpiPixels(this, compact ? 62 : 78), required);
                typeOptions.MinimumSize = new Size(0, required);
                typeOptions.Height = required;
                actionButtons.MinimumSize = new Size(0, UiTheme.DpiPixels(this, compact ? 46 : 50));
                rootLayout.PerformLayout();
            }
            finally { applyingResponsiveLayout = false; }
        }

        private void ConfigureTypeOptionLayout(RadioButton option, int width, int height, int gap, bool compact)
        {
            option.MinimumSize = new Size(width, height);
            option.MaximumSize = new Size(width, height);
            option.Size = new Size(width, height);
            option.Margin = new Padding(0, 0, gap, 0);
            option.Padding = new Padding(UiTheme.DpiPixels(this, compact ? 10 : 12), 0, UiTheme.DpiPixels(this, 8), 0);
        }

        private void UpdatePreview()
        {
            RecognitionFeedbackReport report = FeedbackService.CreateReport(FindingForCurrentType(), CurrentFeedbackType(), expectedBox.Text, hashBox.Checked);
            previewBox.Text = FeedbackService.BuildMarkdown(report);
        }

        private void SaveFeedback(bool openGitHub)
        {
            try
            {
                RecognitionFeedbackReport report = FeedbackService.CreateReport(FindingForCurrentType(), CurrentFeedbackType(), expectedBox.Text, hashBox.Checked);
                SavedFeedback saved = FeedbackService.Save(store, report);
                bool copied = false;
                if (openGitHub)
                {
                    try
                    {
                        Clipboard.SetText(saved.Markdown);
                        copied = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("复制反馈内容失败", ex);
                    }

                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = saved.IssueUrl, UseShellExecute = true });
                        MessageBox.Show(this,
                            "脱敏反馈已保存。" + (copied ? "详细内容已复制，请粘贴到 GitHub 表单的“脱敏证据”字段。" : "剪贴板复制失败，请从本地 Markdown 文件复制内容。") +
                            "\n\n本地文件：\n" + saved.MarkdownPath,
                            "反馈已准备", MessageBoxButtons.OK, copied ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("打开 GitHub 反馈页失败", ex);
                        MessageBox.Show(this, "GitHub 页面打开失败，但反馈没有丢失。\n\n本地文件：\n" + saved.MarkdownPath + "\n\n错误：" + ex.Message, "已保留本地反馈", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(this, "反馈已仅保存在本机，没有发送到网络。\n\n" + saved.MarkdownPath, "已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("生成反馈失败", ex);
                MessageBox.Show(this, "生成反馈失败：" + ex.Message, "反馈失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Finding FindingForCurrentType()
        {
            if (!string.Equals(CurrentFeedbackType(), "漏报", StringComparison.OrdinalIgnoreCase)) return finding;
            return new Finding
            {
                Risk = "未判断",
                Vendor = "未知",
                Category = "未扫描到的项目",
                UserVisibleName = "未提供",
                UserImpact = "请根据用户说明复核遗漏对象。",
                Evidence = "漏报反馈不关联当前列表中的任何结果。",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly" }
            };
        }

        private string CurrentFeedbackType()
        {
            if (missedType.Checked) return "漏报";
            if (identityType.Checked) return "身份错误";
            if (relationType.Checked) return "关联错误";
            return "误报";
        }

        private static void StyleTypeOption(RadioButton option, string title, string note)
        {
            option.Appearance = Appearance.Button;
            option.Text = title + Environment.NewLine + note;
            option.TextAlign = ContentAlignment.MiddleLeft;
            option.Width = 190;
            option.Height = 58;
            option.Margin = new Padding(0, 0, 10, 0);
            option.Padding = new Padding(12, 0, 8, 0);
            option.FlatStyle = FlatStyle.Flat;
            option.FlatAppearance.BorderSize = 1;
            option.FlatAppearance.BorderColor = UiTheme.Border;
            option.FlatAppearance.CheckedBackColor = UiTheme.PrimarySoft;
            option.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
            option.BackColor = UiTheme.Surface;
            option.ForeColor = UiTheme.Text;
            option.Font = UiTheme.Font(8.5F, FontStyle.Regular);
        }
    }
}
