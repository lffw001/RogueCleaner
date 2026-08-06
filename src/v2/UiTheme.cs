using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal static class UiTheme
    {
        public static readonly Color Canvas = Color.FromArgb(245, 247, 250);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 228, 236);
        public static readonly Color Primary = Color.FromArgb(15, 118, 110);
        public static readonly Color PrimaryHover = Color.FromArgb(13, 148, 136);
        public static readonly Color PrimarySoft = Color.FromArgb(232, 248, 246);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(234, 88, 12);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Info = Color.FromArgb(37, 99, 235);

        public static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static void PrimaryButton(Button button, string text, Color color)
        {
            BaseButton(button, text);
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Lighten(color, 12);
            button.FlatAppearance.MouseDownBackColor = Darken(color, 12);
        }

        public static void OutlineButton(Button button, string text, Color color)
        {
            BaseButton(button, text);
            button.BackColor = Surface;
            button.ForeColor = color;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = color;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(241, 245, 249);
        }

        public static void HeaderButton(Button button, string text)
        {
            BaseButton(button, text);
            button.AutoSize = true;
            button.MinimumSize = new Size(88, 34);
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = PrimarySoft;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(209, 250, 229);
            button.Padding = new Padding(10, 0, 10, 0);
        }

        public static void NavButton(Button button, string text)
        {
            button.Text = text;
            button.Dock = DockStyle.Top;
            button.Height = 48;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(22, 0, 0, 0);
            button.Margin = new Padding(0);
            button.Font = Font(9.5F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
        }

        public static void SetNavActive(Button button, bool active)
        {
            button.BackColor = active ? PrimarySoft : Surface;
            button.ForeColor = active ? Primary : Text;
            button.Font = Font(9.5F, active ? FontStyle.Bold : FontStyle.Regular);
        }

        private static void BaseButton(Button button, string text)
        {
            button.Text = text;
            button.Height = 40;
            button.MinimumSize = new Size(108, 40);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Padding = new Padding(13, 0, 13, 0);
            button.Font = Font(9F, FontStyle.Bold);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private static Color Lighten(Color color, int delta)
        {
            return Color.FromArgb(Math.Min(255, color.R + delta), Math.Min(255, color.G + delta), Math.Min(255, color.B + delta));
        }

        private static Color Darken(Color color, int delta)
        {
            return Color.FromArgb(Math.Max(0, color.R - delta), Math.Max(0, color.G - delta), Math.Max(0, color.B - delta));
        }
    }

    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.Surface;
            Padding = new Padding(1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle border = ClientRectangle;
            border.Width -= 1;
            border.Height -= 1;
            using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawRectangle(pen, border);
        }
    }

    internal sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    internal static class UiRegression
    {
        public static List<string> Run(DataStore store)
        {
            List<string> failures = new List<string>();
            using (MainForm main = new MainForm(store, false))
            {
                main.Show();
                Application.DoEvents();
                PopulatePreviewRows(main, failures);
                Application.DoEvents();
                ValidateMainWindow(main, failures, "default");
                Capture(main, Path.Combine(store.Reports, "ui-main-default-" + store.Timestamp() + ".png"), failures);
                main.Size = main.MinimumSize;
                Application.DoEvents();
                ValidateMainWindow(main, failures, "minimum");
                Capture(main, Path.Combine(store.Reports, "ui-main-minimum-" + store.Timestamp() + ".png"), failures);
                main.Close();
            }
            ValidateAuthorDestinations(store, failures);
            foreach (float scale in new float[] { 1.25F, 1.5F, 2F }) ValidateScaledWindow(store, scale, failures);
            ValidateLiveScan(store, failures);

            Finding sample = new Finding
            {
                Risk = "低",
                Vendor = "示例厂商",
                Category = "开机启动",
                UserVisibleName = "示例反馈项目",
                UserImpact = "用于验证反馈窗体布局，不会提交到网络。",
                Evidence = "签名：示例；行为：登录时启动。",
                ActionKind = "ReportOnly",
                Target = new ActionTarget { Kind = "ReportOnly" }
            };
            using (FeedbackForm feedback = new FeedbackForm(store, sample))
            {
                feedback.Show();
                Application.DoEvents();
                ValidateVisibleButton(feedback, failures, "复制并打开 GitHub", "feedback");
                ValidateVisibleButton(feedback, failures, "仅保存本地", "feedback");
                Capture(feedback, Path.Combine(store.Reports, "ui-feedback-" + store.Timestamp() + ".png"), failures);
                feedback.Close();
            }
            using (RecoveryCenterForm recovery = new RecoveryCenterForm(store))
            {
                recovery.Show();
                Application.DoEvents();
                ValidateVisibleButton(recovery, failures, "恢复选中批次", "recovery");
                ValidateVisibleButton(recovery, failures, "关闭", "recovery");
                Capture(recovery, Path.Combine(store.Reports, "ui-recovery-" + store.Timestamp() + ".png"), failures);
                recovery.Close();
            }
            using (ContextMenuManagerForm contextMenu = new ContextMenuManagerForm(store))
            {
                contextMenu.Show();
                Application.DoEvents();
                if (contextMenu.Cursor != Cursors.Default || contextMenu.UseWaitCursor) failures.Add("context menu：枚举期间出现等待光标");
                Button refresh = FindButton(contextMenu, "刷新");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                ValidateVisibleButton(contextMenu, failures, "刷新", "context menu");
                ValidateVisibleButton(contextMenu, failures, "启用", "context menu");
                ValidateVisibleButton(contextMenu, failures, "禁用", "context menu");
                ValidateVisibleButton(contextMenu, failures, "编辑", "context menu");
                ValidateVisibleButton(contextMenu, failures, "添加", "context menu");
                ValidateVisibleButton(contextMenu, failures, "删除", "context menu");
                ValidateVisibleButton(contextMenu, failures, "专用模块", "context menu");
                ValidateVisibleButton(contextMenu, failures, "高级兼容", "context menu");
                if (refresh != null && !refresh.Enabled) failures.Add("context menu：15 秒内未完成枚举");
                SplitContainer contextSplit = FindControl<SplitContainer>(contextMenu);
                if (contextSplit == null || contextSplit.Panel2.Width < 280) failures.Add("context menu：右侧详情面板未保留足够宽度");
                Capture(contextMenu, Path.Combine(store.Reports, "ui-context-menu-" + store.Timestamp() + ".png"), failures);
                contextMenu.Close();
            }
            using (SpecialContextMenuForm special = new SpecialContextMenuForm(store))
            {
                special.Show(); Application.DoEvents();
                if (special.Cursor != Cursors.Default || special.UseWaitCursor) failures.Add("special menu：枚举期间出现等待光标");
                Button refresh = FindButton(special, "刷新");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                ValidateVisibleButton(special, failures, "刷新", "special menu");
                ValidateVisibleButton(special, failures, "启用", "special menu");
                ValidateVisibleButton(special, failures, "禁用", "special menu");
                ValidateVisibleButton(special, failures, "添加", "special menu");
                ValidateVisibleButton(special, failures, "删除", "special menu");
                if (refresh != null && !refresh.Enabled) failures.Add("special menu：15 秒内未完成枚举");
                Capture(special, Path.Combine(store.Reports, "ui-special-menu-" + store.Timestamp() + ".png"), failures);
                special.Close();
            }
            using (AdvancedContextMenuForm advanced = new AdvancedContextMenuForm(store))
            {
                advanced.Show(); Application.DoEvents();
                if (advanced.Cursor != Cursors.Default || advanced.UseWaitCursor) failures.Add("advanced menu：枚举期间出现等待光标");
                Button refresh = FindButton(advanced, "刷新");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                ValidateVisibleButton(advanced, failures, "刷新", "advanced menu");
                ValidateVisibleButton(advanced, failures, "启用 / 安装", "advanced menu");
                ValidateVisibleButton(advanced, failures, "禁用 / 移除", "advanced menu");
                ValidateVisibleButton(advanced, failures, "添加 IE 项", "advanced menu");
                ValidateVisibleButton(advanced, failures, "上移", "advanced menu");
                ValidateVisibleButton(advanced, failures, "下移", "advanced menu");
                if (refresh != null && !refresh.Enabled) failures.Add("advanced menu：15 秒内未完成枚举");
                Capture(advanced, Path.Combine(store.Reports, "ui-advanced-menu-" + store.Timestamp() + ".png"), failures);
                advanced.Close();
            }
            using (ContextMenuEditorForm editor = new ContextMenuEditorForm())
            {
                editor.Show();
                Application.DoEvents();
                ValidateVisibleButton(editor, failures, "添加", "context editor");
                ValidateVisibleButton(editor, failures, "取消", "context editor");
                Capture(editor, Path.Combine(store.Reports, "ui-context-editor-" + store.Timestamp() + ".png"), failures);
                editor.Close();
            }
            return failures;
        }

        private static void ValidateLiveScan(DataStore store, List<string> failures)
        {
            using (MainForm form = new MainForm(store, false))
            {
                form.Show();
                Application.DoEvents();
                MethodInfo startScan = typeof(MainForm).GetMethod("StartScan", BindingFlags.Instance | BindingFlags.NonPublic);
                Button scan = FindButton(form, "▶  开始扫描");
                if (startScan == null || scan == null)
                {
                    failures.Add("live scan：无法启动真实界面扫描");
                    return;
                }
                Stopwatch watch = Stopwatch.StartNew();
                startScan.Invoke(form, null);
                if (form.Cursor != Cursors.Default || form.UseWaitCursor) failures.Add("live scan：扫描开始后出现等待光标");
                while (!scan.Enabled && watch.ElapsedMilliseconds < 30000)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                Application.DoEvents();
                if (!scan.Enabled) failures.Add("live scan：30 秒内未完成");
                FieldInfo dataErrorField = typeof(MainForm).GetField("gridDataErrorCount", BindingFlags.Instance | BindingFlags.NonPublic);
                int dataErrorCount = dataErrorField == null ? -1 : Convert.ToInt32(dataErrorField.GetValue(form));
                if (dataErrorCount != 0) failures.Add("live scan：DataGridView.DataError 次数=" + dataErrorCount);
                form.Close();
            }
        }

        private static void ValidateScaledWindow(DataStore store, float scale, List<string> failures)
        {
            using (MainForm form = new MainForm(store, false))
            {
                form.CreateControl();
                form.ClientSize = new Size(1120, 700);
                form.PerformLayout();
                form.Scale(new SizeF(scale, scale));
                form.ClientSize = new Size((int)(1120 * scale), (int)(700 * scale));
                form.PerformLayout();
                ValidateMainWindow(form, failures, "scale-" + scale.ToString("0.##"));
            }
        }

        private static void ValidateMainWindow(Form form, List<string> failures, string scope)
        {
            Button scan = FindButton(form, "▶  开始扫描");
            Button clean = FindButton(form, "▣  清理勾选");
            Button update = FindButton(form, "检查更新");
            Button feedback = FindButton(form, "反馈");
            ValidateButton(form, scan, failures, "开始扫描", scope);
            ValidateButton(form, clean, failures, "清理勾选", scope);
            ValidateButton(form, update, failures, "检查更新", scope);
            ValidateButton(form, feedback, failures, "反馈", scope);
            if (update != null && feedback != null)
            {
                Rectangle updateBounds = RelativeBounds(form, update);
                Rectangle feedbackBounds = RelativeBounds(form, feedback);
                if (feedbackBounds.Left <= updateBounds.Left) failures.Add(scope + "：反馈没有位于检查更新之后");
                if (feedbackBounds.IntersectsWith(updateBounds)) failures.Add(scope + "：检查更新与反馈发生重叠");
            }
            if (scan != null && clean != null && RelativeBounds(form, scan).IntersectsWith(RelativeBounds(form, clean))) failures.Add(scope + "：开始扫描与清理勾选发生重叠");
            ValidateAuthorLayout(form, failures, scope);
            if (scope == "default") ValidateBusyCursor(form, failures);
        }

        private static void ValidateAuthorLayout(Form form, List<string> failures, string scope)
        {
            Label author = FindControlByText<Label>(form, "作者：" + AppMeta.AuthorName);
            LinkLabel poJie = FindControlByText<LinkLabel>(form, "吾爱破解");
            LinkLabel gitHub = FindControlByText<LinkLabel>(form, "GitHub");
            if (author == null)
            {
                failures.Add(scope + "：缺少普通作者署名");
            }
            else
            {
                if (author is LinkLabel) failures.Add(scope + "：作者署名仍是可点击链接");
                ValidateControlBounds(form, author, failures, "作者署名", scope);
            }
            LinkLabel[] links = new LinkLabel[] { poJie, gitHub };
            string[] names = new string[] { "吾爱破解", "GitHub" };
            for (int index = 0; index < links.Length; index++)
            {
                LinkLabel link = links[index];
                string name = names[index];
                if (link == null)
                {
                    failures.Add(scope + "：缺少入口 " + name);
                    continue;
                }
                ValidateControlBounds(form, link, failures, name, scope);
                if (link.Image == null) failures.Add(scope + "：入口缺少嵌入图标 " + name);
            }
            if (poJie != null && gitHub != null && RelativeBounds(form, poJie).IntersectsWith(RelativeBounds(form, gitHub))) failures.Add(scope + "：吾爱破解与 GitHub 入口重叠");
        }

        private static void ValidateAuthorDestinations(DataStore store, List<string> failures)
        {
            List<string> opened = new List<string>();
            using (MainForm form = new MainForm(store, false, delegate(string url) { opened.Add(url); }))
            {
                Label author = FindControlByText<Label>(form, "作者：" + AppMeta.AuthorName);
                LinkLabel poJie = FindControlByText<LinkLabel>(form, "吾爱破解");
                LinkLabel gitHub = FindControlByText<LinkLabel>(form, "GitHub");
                MethodInfo onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                if (author == null || poJie == null || gitHub == null || onClick == null)
                {
                    failures.Add("author links：无法构造点击回归");
                    return;
                }
                onClick.Invoke(author, new object[] { EventArgs.Empty });
                onClick.Invoke(poJie, new object[] { EventArgs.Empty });
                onClick.Invoke(poJie, new object[] { EventArgs.Empty });
                onClick.Invoke(gitHub, new object[] { EventArgs.Empty });
                if (opened.Count != 2)
                {
                    failures.Add("author links：作者/防连点回归启动次数=" + opened.Count);
                    return;
                }
                if (!string.Equals(opened[0], AppMeta.Author52PojieUrl, StringComparison.Ordinal)) failures.Add("author links：吾爱入口目标错误 " + opened[0]);
                if (!string.Equals(opened[1], AppMeta.AuthorGitHubUrl, StringComparison.Ordinal)) failures.Add("author links：GitHub 入口目标错误 " + opened[1]);
            }
        }

        private static void ValidateBusyCursor(Form form, List<string> failures)
        {
            MethodInfo setBusy = typeof(MainForm).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic);
            if (setBusy == null)
            {
                failures.Add("busy cursor：无法访问忙碌状态切换");
                return;
            }
            setBusy.Invoke(form, new object[] { true, "扫描性能回归" });
            Application.DoEvents();
            if (form.Cursor != Cursors.Default || form.UseWaitCursor) failures.Add("busy cursor：扫描期间仍显示等待光标");
            setBusy.Invoke(form, new object[] { false, "就绪" });
        }

        private static void PopulatePreviewRows(MainForm form, List<string> failures)
        {
            try
            {
                FieldInfo rowsField = typeof(MainForm).GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic);
                BindingList<Finding> rows = rowsField == null ? null : rowsField.GetValue(form) as BindingList<Finding>;
                if (rows == null)
                {
                    failures.Add("ui preview：无法访问结果绑定列表");
                    return;
                }
                rows.Add(PreviewFinding("高", "手机助手 / 设备助手", "后台服务", "爱思助手后台服务", "后台服务会常驻或被系统拉起。", "DisableService", @"HKLM\SYSTEM\CurrentControlSet\Services\ExampleService"));
                Application.DoEvents();
                for (int index = 0; index < 45; index++)
                {
                    string risk = index % 3 == 0 ? "中" : (index % 3 == 1 ? "低" : "仅提示");
                    string action = risk == "仅提示" ? "ReportOnly" : "DeleteRegistryKey";
                    rows.Add(PreviewFinding(risk, index % 2 == 0 ? "未知第三方" : "示例厂商", index % 2 == 0 ? "疑似捆绑/弹窗组件" : "右键菜单", "增量绑定回归项目 " + (index + 1), "用于验证扫描过程中持续加入结果时表格不会出现失效行索引。", action, @"HKCU\Software\Example\Binding" + index));
                    if (index % 5 == 4) Application.DoEvents();
                }
                DataGridView grid = FindControl<DataGridView>(form);
                if (grid != null && grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[0].Cells[1];
                Application.DoEvents();
                FieldInfo dataErrorField = typeof(MainForm).GetField("gridDataErrorCount", BindingFlags.Instance | BindingFlags.NonPublic);
                int dataErrorCount = dataErrorField == null ? -1 : Convert.ToInt32(dataErrorField.GetValue(form));
                if (dataErrorCount != 0) failures.Add("ui preview：增量绑定触发 DataGridView.DataError 次数=" + dataErrorCount);
            }
            catch (Exception ex)
            {
                failures.Add("ui preview：填充示例结果失败：" + ex.Message);
            }
        }

        private static Finding PreviewFinding(string risk, string vendor, string category, string title, string impact, string action, string location)
        {
            return new Finding
            {
                Risk = risk,
                Vendor = vendor,
                Category = category,
                UserVisibleName = title,
                UserImpact = impact,
                Evidence = "签名/发布者：" + vendor + "；行为事实：" + impact,
                ActionKind = action,
                TechnicalLocation = location,
                Target = new ActionTarget { Kind = action, SubKey = location }
            };
        }

        private static void ValidateVisibleButton(Form form, List<string> failures, string text, string scope)
        {
            ValidateButton(form, FindButton(form, text), failures, text, scope);
        }

        private static void ValidateButton(Form form, Button button, List<string> failures, string name, string scope)
        {
            if (button == null)
            {
                failures.Add(scope + "：缺少按钮 " + name);
                return;
            }
            if ((form.Visible && !button.Visible) || button.Width <= 0 || button.Height <= 0) failures.Add(scope + "：按钮不可见 " + name);
            Rectangle formBounds = form.ClientRectangle;
            Rectangle buttonBounds = RelativeBounds(form, button);
            if (!formBounds.Contains(buttonBounds)) failures.Add(scope + "：按钮越出窗口 " + name + " form=" + formBounds + " button=" + buttonBounds);
        }

        private static void ValidateControlBounds(Form form, Control control, List<string> failures, string name, string scope)
        {
            if ((form.Visible && !control.Visible) || control.Width <= 0 || control.Height <= 0) failures.Add(scope + "：控件不可见 " + name);
            Rectangle bounds = RelativeBounds(form, control);
            if (!form.ClientRectangle.Contains(bounds)) failures.Add(scope + "：控件越出窗口 " + name + " form=" + form.ClientRectangle + " control=" + bounds);
        }

        private static Button FindButton(Control root, string text)
        {
            foreach (Control child in Descendants(root))
            {
                Button button = child as Button;
                if (button != null && string.Equals(button.Text, text, StringComparison.Ordinal)) return button;
            }
            return null;
        }

        private static T FindControl<T>(Control root) where T : Control
        {
            foreach (Control child in Descendants(root))
            {
                T control = child as T;
                if (control != null) return control;
            }
            return null;
        }

        private static T FindControlByText<T>(Control root, string text) where T : Control
        {
            foreach (Control child in Descendants(root))
            {
                T control = child as T;
                if (control != null && string.Equals(control.Text, text, StringComparison.Ordinal)) return control;
            }
            return null;
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }

        private static Rectangle RelativeBounds(Control root, Control control)
        {
            Point location = control.Location;
            Control parent = control.Parent;
            while (parent != null && parent != root)
            {
                location.Offset(parent.Location);
                parent = parent.Parent;
            }
            return new Rectangle(location, control.Size);
        }

        private static void Capture(Form form, string path, List<string> failures)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                failures.Add("界面截图失败：" + ex.Message);
            }
        }
    }
}
