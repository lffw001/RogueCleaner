using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RogueCleanerLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundleDir;
            try
            {
                bundleDir = FindBundleDirectory(baseDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "运行文件不完整。\n\n" +
                    "请先把压缩包完整解压到一个文件夹，再双击文件夹里的“流氓软件克星.exe”。\n\n" +
                    "不要在 WinRAR/压缩包预览窗口里直接双击 exe，也不要只单独复制 exe。\n\n" +
                    "如果你想只双击一个 EXE，请使用发布包里的“一键运行版”。\n\n" +
                    "缺少内容：" + ex.Message,
                    "流氓软件克星",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 2;
            }

            string scriptPath = Path.Combine(bundleDir, "RogueCleaner.ps1");
            string powerShell = FindPowerShell();
            if (powerShell == null)
            {
                MessageBox.Show(
                    "没有找到 pwsh.exe 或 powershell.exe。系统 PowerShell 不见了，工具没法启动。",
                    "流氓软件克星",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 3;
            }

            if (args.Length > 0)
            {
                return RunEngine(powerShell, baseDir, bundleDir, scriptPath, args);
            }

            try
            {
                Application.Run(new CleanerForm(powerShell, baseDir, bundleDir, scriptPath));
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "启动界面失败：" + ex.Message,
                    "流氓软件克星",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 4;
            }
        }

        internal static int RunEngine(string powerShell, string baseDir, string bundleDir, string scriptPath, params string[] scriptArgs)
        {
            string forwarded = scriptArgs == null || scriptArgs.Length == 0
                ? string.Empty
                : " " + string.Join(" ", scriptArgs.Select(Quote).ToArray());
            string arguments = "-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(scriptPath) + forwarded;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = arguments,
                WorkingDirectory = baseDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.EnvironmentVariables["ROGUE_CLEANER_HOME"] = baseDir;
            psi.EnvironmentVariables["ROGUE_CLEANER_BUNDLE"] = bundleDir;
            psi.EnvironmentVariables["ROGUE_CLEANER_RULES_DIR"] = Path.Combine(bundleDir, "rules");
            psi.EnvironmentVariables["ROGUE_CLEANER_EXE"] = Application.ExecutablePath;

            using (Process process = Process.Start(psi))
            {
                if (process == null) return 5;
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static string FindBundleDirectory(string baseDir)
        {
            string bundleDir = Path.GetFullPath(baseDir);
            string scriptPath = Path.Combine(bundleDir, "RogueCleaner.ps1");
            string rulesDir = Path.Combine(bundleDir, "rules");
            string[] ruleFiles =
            {
                Path.Combine(rulesDir, "vendors.json"),
                Path.Combine(rulesDir, "locations.json"),
                Path.Combine(rulesDir, "behaviors.json")
            };

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("RogueCleaner.ps1", scriptPath);
            }
            if (!Directory.Exists(rulesDir))
            {
                throw new DirectoryNotFoundException("rules 规则目录");
            }
            foreach (string ruleFile in ruleFiles)
            {
                if (!File.Exists(ruleFile))
                {
                    throw new FileNotFoundException(Path.GetFileName(ruleFile), ruleFile);
                }
            }
            return bundleDir;
        }

        private static string FindPowerShell()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string pwsh = Path.Combine(dir.Trim(), "pwsh.exe");
                    if (File.Exists(pwsh)) return pwsh;
                    string powershell = Path.Combine(dir.Trim(), "powershell.exe");
                    if (File.Exists(powershell)) return powershell;
                }
                catch
                {
                }
            }

            return null;
        }

        internal static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class CleanerForm : Form
    {
        private readonly string powerShell;
        private readonly string baseDir;
        private readonly string bundleDir;
        private readonly string scriptPath;
        private readonly BindingList<FindingRow> rows = new BindingList<FindingRow>();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label statusLabel = new Label();
        private readonly Label summaryLabel = new Label();
        private readonly Button scanButton = new Button();
        private readonly Button cleanButton = new Button();
        private readonly Button selectAllButton = new Button();
        private readonly Button lowButton = new Button();
        private readonly Button noneButton = new Button();
        private readonly Button reportButton = new Button();
        private readonly Button adminButton = new Button();
        private readonly TextBox searchBox = new TextBox();
        private readonly LinkLabel authorLink = new LinkLabel();
        private readonly ContextMenuStrip menuPreview = new ContextMenuStrip();
        private int menuPreviewRow = -1;
        private int menuPreviewColumn = -1;
        private string lastReportPath;
        private static readonly string[] AuthorUrls = new string[]
        {
            "https://www.52pojie.cn/?286924",
            "https://github.com/aakk007"
        };

        public CleanerForm(string powerShell, string baseDir, string bundleDir, string scriptPath)
        {
            this.powerShell = powerShell;
            this.baseDir = baseDir;
            this.bundleDir = bundleDir;
            this.scriptPath = scriptPath;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "流氓软件克星";
            MinimumSize = new Size(1080, 680);
            Size = new Size(1240, 780);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Microsoft YaHei UI", 9F);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Color.FromArgb(241, 245, 249);
            layout.ColumnCount = 1;
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            Controls.Add(layout);

            GradientPanel header = new GradientPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.StartColor = Color.FromArgb(15, 118, 110);
            header.EndColor = Color.FromArgb(30, 64, 175);
            layout.Controls.Add(header, 0, 0);

            RogueLogoControl logo = new RogueLogoControl();
            logo.Size = new Size(56, 56);
            logo.Location = new Point(26, 25);
            logo.BackColor = Color.Transparent;
            header.Controls.Add(logo);

            Label title = new Label();
            title.Text = "流氓软件克星";
            title.ForeColor = Color.White;
            title.BackColor = Color.Transparent;
            title.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(96, 22);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "默认只抓右键菜单、自启、服务、插件、文件关联这些小动作；主程序卸载项不默认扫描。";
            subtitle.ForeColor = Color.FromArgb(224, 242, 254);
            subtitle.BackColor = Color.Transparent;
            subtitle.Font = new Font("Microsoft YaHei UI", 10F);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(100, 72);
            header.Controls.Add(subtitle);

            Label badge = new Label();
            badge.Text = "先扫描  ·  后确认  ·  再清理";
            badge.ForeColor = Color.FromArgb(7, 89, 133);
            badge.BackColor = Color.FromArgb(236, 254, 255);
            badge.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Width = 200;
            badge.Height = 30;
            badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            badge.Location = new Point(ClientSize.Width - 230, 26);
            header.Controls.Add(badge);
            header.Resize += delegate { badge.Location = new Point(header.ClientSize.Width - 226, 26); };

            summaryLabel.Dock = DockStyle.Fill;
            summaryLabel.Margin = new Padding(18, 10, 18, 8);
            summaryLabel.ForeColor = Color.FromArgb(15, 23, 42);
            summaryLabel.BackColor = Color.White;
            summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
            summaryLabel.Padding = new Padding(18, 0, 18, 0);
            summaryLabel.Text = "未扫描";
            layout.Controls.Add(summaryLabel, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Margin = new Padding(18, 0, 18, 10);
            actions.Padding = new Padding(14, 12, 14, 8);
            actions.BackColor = Color.White;
            actions.WrapContents = false;
            actions.AutoScroll = true;
            layout.Controls.Add(actions, 0, 2);

            ConfigureButton(scanButton, "开始抓现行", Color.FromArgb(14, 116, 144));
            ConfigureButton(cleanButton, "清理勾选项", Color.FromArgb(220, 38, 38));
            ConfigureButton(selectAllButton, "勾选全部", Color.FromArgb(2, 132, 199));
            ConfigureButton(lowButton, "只勾低风险", Color.FromArgb(22, 163, 74));
            ConfigureButton(noneButton, "取消全选", Color.FromArgb(100, 116, 139));
            ConfigureButton(reportButton, "打开证据报告", Color.FromArgb(79, 70, 229));
            ConfigureButton(adminButton, "管理员模式", Color.FromArgb(234, 88, 12));

            Label searchLabel = new Label();
            searchLabel.Text = "搜索";
            searchLabel.ForeColor = Color.FromArgb(51, 65, 85);
            searchLabel.TextAlign = ContentAlignment.MiddleCenter;
            searchLabel.Width = 42;
            searchLabel.Height = 34;
            searchLabel.Margin = new Padding(12, 1, 4, 0);

            searchBox.Width = 220;
            searchBox.Height = 32;
            searchBox.Margin = new Padding(0, 2, 0, 0);
            searchBox.BorderStyle = BorderStyle.FixedSingle;
            actions.Controls.Add(scanButton);
            actions.Controls.Add(cleanButton);
            actions.Controls.Add(selectAllButton);
            actions.Controls.Add(lowButton);
            actions.Controls.Add(noneButton);
            actions.Controls.Add(reportButton);
            actions.Controls.Add(adminButton);
            actions.Controls.Add(searchLabel);
            actions.Controls.Add(searchBox);

            ConfigureGrid();
            grid.Margin = new Padding(18, 0, 18, 12);
            layout.Controls.Add(grid, 0, 3);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = Padding.Empty;
            footer.BackColor = Color.FromArgb(226, 232, 240);
            footer.ColumnCount = 2;
            footer.RowCount = 1;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(footer, 0, 4);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Margin = Padding.Empty;
            statusLabel.BackColor = footer.BackColor;
            statusLabel.ForeColor = Color.FromArgb(51, 65, 85);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(18, 0, 8, 0);
            statusLabel.Text = "准备好了。先点“开始抓现行”，工具只扫描，不会偷偷动手。";
            footer.Controls.Add(statusLabel, 0, 0);

            FlowLayoutPanel authorPanel = new FlowLayoutPanel();
            authorPanel.Dock = DockStyle.Fill;
            authorPanel.Margin = Padding.Empty;
            authorPanel.Padding = new Padding(0, 11, 18, 0);
            authorPanel.BackColor = footer.BackColor;
            authorPanel.FlowDirection = FlowDirection.LeftToRight;
            authorPanel.WrapContents = false;
            footer.Controls.Add(authorPanel, 1, 0);

            Label authorPrefix = new Label();
            authorPrefix.Text = "作者：";
            authorPrefix.AutoSize = true;
            authorPrefix.Margin = new Padding(0, 2, 0, 0);
            authorPrefix.ForeColor = Color.FromArgb(71, 85, 105);
            authorPrefix.BackColor = footer.BackColor;
            authorPanel.Controls.Add(authorPrefix);

            authorLink.Text = "aakk007";
            authorLink.AutoSize = true;
            authorLink.Margin = new Padding(0, 2, 0, 0);
            authorLink.BackColor = footer.BackColor;
            authorLink.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            authorLink.LinkColor = Color.FromArgb(14, 116, 144);
            authorLink.ActiveLinkColor = Color.FromArgb(234, 88, 12);
            authorLink.VisitedLinkColor = Color.FromArgb(14, 116, 144);
            authorLink.LinkBehavior = LinkBehavior.HoverUnderline;
            authorLink.Cursor = Cursors.Hand;
            authorLink.LinkClicked += delegate { OpenAuthorProfiles(); };
            authorPanel.Controls.Add(authorLink);

            scanButton.Click += delegate { StartScan(); };
            cleanButton.Click += delegate { StartClean(); };
            selectAllButton.Click += delegate { SetVisibleSelected(true); };
            lowButton.Click += delegate { SelectLowRiskOnly(); };
            noneButton.Click += delegate { SetAllSelected(false); };
            reportButton.Click += delegate { OpenReports(); };
            adminButton.Click += delegate { RestartAsAdmin(); };
            searchBox.TextChanged += delegate { ApplyFilter(); };
        }

        private void ConfigureButton(Button button, string text, Color color)
        {
            button.Text = text;
            button.Width = 116;
            button.Height = 36;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.Margin = new Padding(0, 0, 10, 0);
            button.Cursor = Cursors.Hand;
        }

        private void ConfigureGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.DataSource = rows;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.ShowCellToolTips = true;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Color.FromArgb(203, 213, 225);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(15, 118, 110);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 251, 241);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
            grid.RowTemplate.Height = 36;
            grid.CellFormatting += GridCellFormatting;
            grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
            grid.CellMouseEnter += GridCellMouseEnter;
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.MouseLeave += delegate { HideMenuPreview(); };
            grid.Scroll += delegate { HideMenuPreview(); };
            grid.CellClick += delegate { HideMenuPreview(); };
            grid.ColumnWidthChanged += delegate { HideMenuPreview(); };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty)
                {
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            grid.CellValueChanged += delegate { UpdateSummary(); };

            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = "选", Width = 46 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", HeaderText = "#", Width = 48, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Risk", HeaderText = "风险", Width = 58, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Vendor", HeaderText = "厂商", Width = 105, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HumanType", HeaderText = "它干了啥", Width = 145, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HumanSubject", HeaderText = "用户会看到/受到什么影响", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "HumanAction", HeaderText = "工具会怎么处理", Width = 165, ReadOnly = true });
            DataGridViewTextBoxColumn location = new DataGridViewTextBoxColumn { DataPropertyName = "Location", HeaderText = "技术藏身位置", Width = 260, ReadOnly = true };
            grid.Columns.Add(location);

            grid.Columns[0].ToolTipText = "点这一列表头可以把当前列表全选/全不选；只想保守一点，就点“只勾低风险”。";
            grid.Columns[3].ToolTipText = "识别到的厂商或软件来源。";
            grid.Columns[4].ToolTipText = "用人话说明它属于右键菜单、开机自启、后台服务、浏览器插件、文件关联，还是已安装软件。";
            grid.Columns[5].ToolTipText = "尽量显示用户实际看到的右键文字、打开方式名称，或这个后台项会造成的影响。";
            grid.Columns[6].ToolTipText = "清理时会执行的动作。高风险项不会默认替你勾选。";
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            menuPreview.ShowImageMargin = false;
            menuPreview.ShowCheckMargin = false;
            menuPreview.Font = new Font("Microsoft YaHei UI", 9F);
            menuPreview.BackColor = Color.White;
            menuPreview.Closed += delegate
            {
                menuPreviewRow = -1;
                menuPreviewColumn = -1;
            };
            menuPreview.MouseLeave += delegate { HideMenuPreview(); };
        }

        private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].DataPropertyName != "Risk") return;
            string risk = Convert.ToString(e.Value);
            if (risk == "高")
            {
                e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                e.CellStyle.ForeColor = Color.FromArgb(153, 27, 27);
                e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
            }
            else if (risk == "中")
            {
                e.CellStyle.BackColor = Color.FromArgb(255, 237, 213);
                e.CellStyle.ForeColor = Color.FromArgb(154, 52, 18);
                e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
            }
            else
            {
                e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                e.CellStyle.ForeColor = Color.FromArgb(22, 101, 52);
            }
        }

        private void GridCellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            FindingRow item = grid.Rows[e.RowIndex].DataBoundItem as FindingRow;
            if (item != null)
            {
                e.ToolTipText = item.GetToolTipText(grid.Columns[e.ColumnIndex].DataPropertyName);
                return;
            }
            object value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            if (value != null) e.ToolTipText = Convert.ToString(value);
        }

        private void GridCellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                HideMenuPreview();
                return;
            }

            string property = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (property != "HumanSubject" && property != "HumanType")
            {
                HideMenuPreview();
                return;
            }

            FindingRow item = grid.Rows[e.RowIndex].DataBoundItem as FindingRow;
            if (item == null)
            {
                HideMenuPreview();
                return;
            }

            List<string> previewItems = item.GetSimulatedMenuItems();
            if (previewItems.Count == 0)
            {
                HideMenuPreview();
                return;
            }

            if (menuPreview.Visible && menuPreviewRow == e.RowIndex && menuPreviewColumn == e.ColumnIndex) return;
            ShowMenuPreview(item, previewItems, e.RowIndex, e.ColumnIndex);
        }

        private void ShowMenuPreview(FindingRow item, List<string> previewItems, int rowIndex, int columnIndex)
        {
            menuPreview.Items.Clear();

            ToolStripMenuItem header = new ToolStripMenuItem("右键菜单模拟预览");
            header.Enabled = false;
            header.Font = new Font(menuPreview.Font, FontStyle.Bold);
            menuPreview.Items.Add(header);
            menuPreview.Items.Add(new ToolStripSeparator());

            foreach (string label in previewItems.Take(12))
            {
                ToolStripMenuItem menuItem = new ToolStripMenuItem(Shorten(label, 48));
                menuItem.Tag = label;
                menuItem.Click += delegate { HideMenuPreview(); };
                menuPreview.Items.Add(menuItem);
            }

            string detail = item.GetPreviewFootnote();
            if (!string.IsNullOrWhiteSpace(detail))
            {
                menuPreview.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem foot = new ToolStripMenuItem(Shorten(detail, 58));
                foot.Enabled = false;
                menuPreview.Items.Add(foot);
            }

            Rectangle cell = grid.GetCellDisplayRectangle(columnIndex, rowIndex, true);
            Point point = new Point(Math.Max(0, cell.Left + 12), Math.Max(0, cell.Bottom - 2));
            if (point.Y + 220 > grid.Height) point.Y = Math.Max(0, cell.Top - 8);
            menuPreviewRow = rowIndex;
            menuPreviewColumn = columnIndex;
            menuPreview.Show(grid, point);
        }

        private void HideMenuPreview()
        {
            if (menuPreview.Visible) menuPreview.Hide();
            menuPreviewRow = -1;
            menuPreviewColumn = -1;
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, Math.Max(0, max - 1)) + "…";
        }

        private void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex != 0) return;
            bool hasVisible = false;
            bool allVisibleSelected = true;
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                if (!gridRow.Visible) continue;
                FindingRow item = gridRow.DataBoundItem as FindingRow;
                if (item == null) continue;
                hasVisible = true;
                if (!item.Selected)
                {
                    allVisibleSelected = false;
                    break;
                }
            }
            if (!hasVisible) return;
            SetVisibleSelected(!allVisibleSelected);
        }

        private void StartScan()
        {
            RunBusy("正在扫描，别急，先把它们的尾巴摸出来。", delegate
            {
                int exit = Program.RunEngine(powerShell, baseDir, bundleDir, scriptPath, "-Scan", "-NoGui");
                if (exit != 0)
                {
                    throw new InvalidOperationException("扫描失败，退出码 " + exit);
                }
                string report = GetLatestReport();
                if (report == null)
                {
                    throw new InvalidOperationException("扫描完成但没有找到 JSON 报告。");
                }
                List<FindingRow> loaded = LoadRows(report);
                BeginInvoke((MethodInvoker)delegate
                {
                    rows.Clear();
                    foreach (FindingRow row in loaded)
                    {
                        rows.Add(row);
                    }
                    lastReportPath = report;
                    ApplyFilter();
                    UpdateSummary();
                    statusLabel.Text = "扫描完成。报告：" + report;
                });
            });
        }

        private void StartClean()
        {
            grid.EndEdit();
            List<FindingRow> selected = rows.Where(r => r.Selected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("还没勾选任何项目。工具不会替你瞎点。", "没有选择", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "准备清理 " + selected.Count + " 项。会先做备份，再动手。\n\n高风险项可能涉及服务、计划任务或主程序卸载，确认继续？",
                "确认清理",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            RunBusy("正在清理勾选项，先备份再下手。", delegate
            {
                string selectionPath = WriteSelection(selected);
                int exit = Program.RunEngine(powerShell, baseDir, bundleDir, scriptPath, "-Apply", "-Selection", selectionPath);
                string cleanupReport = GetLatestCleanupReport();
                CleanupStats cleanupStats = LoadCleanupStats(cleanupReport);

                int scanExit = Program.RunEngine(powerShell, baseDir, bundleDir, scriptPath, "-Scan", "-NoGui");
                string report = GetLatestReport();
                List<FindingRow> refreshed = scanExit == 0 && report != null
                    ? LoadRows(report)
                    : new List<FindingRow>();

                BeginInvoke((MethodInvoker)delegate
                {
                    if (scanExit == 0)
                    {
                        rows.Clear();
                        foreach (FindingRow row in refreshed)
                        {
                            rows.Add(row);
                        }
                        lastReportPath = report;
                        ApplyFilter();
                        UpdateSummary();
                    }

                    if (exit != 0 || cleanupStats.Failed > 0)
                    {
                        string detail = cleanupStats.BuildFailureText();
                        statusLabel.Text = "清理后复核发现失败/残留：" + cleanupStats.Failed + " 项。";
                        MessageBox.Show(
                            "清理没有全部成功。\n\n" +
                            "成功：" + cleanupStats.Done + " 项；失败/残留：" + cleanupStats.Failed + " 项；跳过：" + cleanupStats.Skipped + " 项。\n\n" +
                            detail + "\n\n清理报告：" + (cleanupReport ?? "未生成") + "\n复扫报告：" + (report ?? "未生成"),
                            "清理后仍有残留",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    else
                    {
                        statusLabel.Text = "清理完成，已自动复扫。当前列表是复扫后的结果。";
                        MessageBox.Show(
                            "清理完成，并已自动复扫。\n\n成功：" + cleanupStats.Done + " 项；跳过：" + cleanupStats.Skipped + " 项。\n\n清理报告：" + (cleanupReport ?? "未生成"),
                            "完成",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                });
            });
        }

        private void RunBusy(string status, Action worker)
        {
            SetBusy(true, status);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception error = null;
                try
                {
                    worker();
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    SetBusy(false, null);
                    if (error != null)
                    {
                        statusLabel.Text = error.Message;
                        MessageBox.Show(error.Message, "流氓软件克星", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                });
            });
        }

        private void SetBusy(bool busy, string status)
        {
            scanButton.Enabled = !busy;
            cleanButton.Enabled = !busy;
            selectAllButton.Enabled = !busy;
            lowButton.Enabled = !busy;
            noneButton.Enabled = !busy;
            reportButton.Enabled = !busy;
            adminButton.Enabled = !busy;
            searchBox.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!string.IsNullOrEmpty(status))
            {
                statusLabel.Text = status;
            }
        }

        private string GetLatestReport()
        {
            string reports = Path.Combine(baseDir, "reports");
            if (!Directory.Exists(reports)) return null;
            FileInfo newest = new DirectoryInfo(reports)
                .GetFiles("scan-*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest == null ? null : newest.FullName;
        }

        private string GetLatestCleanupReport()
        {
            string reports = Path.Combine(baseDir, "reports");
            if (!Directory.Exists(reports)) return null;
            FileInfo newest = new DirectoryInfo(reports)
                .GetFiles("cleanup-*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest == null ? null : newest.FullName;
        }

        private CleanupStats LoadCleanupStats(string report)
        {
            CleanupStats stats = new CleanupStats();
            stats.ReportPath = report;
            if (string.IsNullOrEmpty(report) || !File.Exists(report)) return stats;

            string json = File.ReadAllText(report);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            object parsed = serializer.DeserializeObject(json);
            IEnumerable items = parsed as IEnumerable;
            if (items == null) return stats;

            foreach (object item in items)
            {
                Dictionary<string, object> dict = item as Dictionary<string, object>;
                if (dict == null) continue;
                stats.Total++;
                string status = ToText(Get(dict, "Status"));
                if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)) stats.Done++;
                else if (string.Equals(status, "Skipped", StringComparison.OrdinalIgnoreCase)) stats.Skipped++;
                else if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    stats.Failed++;
                    string title = ToText(Get(dict, "Title"));
                    string message = ToText(Get(dict, "Message"));
                    stats.Failures.Add((title + "：" + message).Trim('：'));
                }
            }
            return stats;
        }

        private List<FindingRow> LoadRows(string report)
        {
            string json = File.ReadAllText(report);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            object parsed = serializer.DeserializeObject(json);
            IEnumerable items = parsed as IEnumerable;
            List<FindingRow> loaded = new List<FindingRow>();
            if (items == null) return loaded;

            foreach (object item in items)
            {
                Dictionary<string, object> dict = item as Dictionary<string, object>;
                if (dict == null) continue;
                loaded.Add(FindingRow.FromDictionary(dict));
            }
            return loaded;
        }

        private string WriteSelection(List<FindingRow> selected)
        {
            string reports = Path.Combine(baseDir, "reports");
            Directory.CreateDirectory(reports);
            List<Dictionary<string, object>> payload = new List<Dictionary<string, object>>();
            foreach (FindingRow row in selected)
            {
                Dictionary<string, object> item = new Dictionary<string, object>(row.Raw);
                item["Selected"] = true;
                payload.Add(item);
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string path = Path.Combine(reports, "selection-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, serializer.Serialize(payload), new System.Text.UTF8Encoding(true));
            return path;
        }

        private void ApplyFilter()
        {
            string text = searchBox.Text.Trim();
            CurrencyManager manager = (CurrencyManager)BindingContext[rows];
            manager.SuspendBinding();
            foreach (DataGridViewRow row in grid.Rows)
            {
                FindingRow item = row.DataBoundItem as FindingRow;
                if (item == null) continue;
                row.Visible = string.IsNullOrEmpty(text) || item.SearchText.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            manager.ResumeBinding();
        }

        private void SelectLowRiskOnly()
        {
            foreach (FindingRow row in rows)
            {
                row.Selected = row.Risk == "低" && row.RecommendedAction != "ReportOnly" && row.RecommendedAction != "InvokeUninstaller";
            }
            grid.Refresh();
            UpdateSummary();
        }

        private void SetAllSelected(bool value)
        {
            foreach (FindingRow row in rows)
            {
                row.Selected = value;
            }
            grid.Refresh();
            UpdateSummary();
            statusLabel.Text = value ? "已勾选全部项目。清理前还会让你二次确认。" : "已取消全部勾选。";
        }

        private void SetVisibleSelected(bool value)
        {
            int changed = 0;
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                if (!gridRow.Visible) continue;
                FindingRow item = gridRow.DataBoundItem as FindingRow;
                if (item == null) continue;
                item.Selected = value;
                changed++;
            }
            grid.Refresh();
            UpdateSummary();
            statusLabel.Text = value
                ? "已勾选当前列表 " + changed + " 项。清理前还会让你二次确认。"
                : "已取消当前列表 " + changed + " 项。";
        }

        private void UpdateSummary()
        {
            int total = rows.Count;
            int selected = rows.Count(r => r.Selected);
            int high = rows.Count(r => r.Risk == "高");
            int medium = rows.Count(r => r.Risk == "中");
            int low = rows.Count(r => r.Risk == "低");
            summaryLabel.Text = "发现 " + total + " 项，已勾选 " + selected + " 项。高风险 " + high + "，中风险 " + medium + "，低风险 " + low + "。";
        }

        private void OpenReports()
        {
            string reports = Path.Combine(baseDir, "reports");
            Directory.CreateDirectory(reports);
            Process.Start("explorer.exe", Program.Quote(reports));
        }

        private void RestartAsAdmin()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = baseDir
                };
                Process.Start(psi);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("管理员模式启动失败：" + ex.Message, "流氓软件克星", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenAuthorProfiles()
        {
            List<string> errors = new List<string>();
            try
            {
                foreach (string url in AuthorUrls)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(url + "：" + ex.Message);
                    }
                }
            }
            finally
            {
                if (errors.Count > 0)
                {
                    MessageBox.Show("打开作者链接失败：" + string.Join(Environment.NewLine, errors.ToArray()), "流氓软件克星", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static object Get(Dictionary<string, object> raw, string key)
        {
            object value;
            return raw.TryGetValue(key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value);
        }
    }

    internal sealed class CleanupStats
    {
        public string ReportPath { get; set; }
        public int Total { get; set; }
        public int Done { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<string> Failures { get; private set; }

        public CleanupStats()
        {
            Failures = new List<string>();
        }

        public string BuildFailureText()
        {
            if (Failures.Count == 0) return "没有拿到具体失败原因，直接看清理报告。";
            return string.Join(Environment.NewLine, Failures.Take(6).ToArray());
        }
    }

    internal sealed class FindingRow
    {
        public bool Selected { get; set; }
        public int Id { get; set; }
        public string Risk { get; set; }
        public string Vendor { get; set; }
        public string SourceType { get; set; }
        public string HumanType { get; set; }
        public string Title { get; set; }
        public string HumanSubject { get; set; }
        public string RecommendedAction { get; set; }
        public string HumanAction { get; set; }
        public string Location { get; set; }
        public string SearchText { get; private set; }
        public Dictionary<string, object> Raw { get; private set; }

        public static FindingRow FromDictionary(Dictionary<string, object> raw)
        {
            FindingRow row = new FindingRow();
            row.Raw = raw;
            row.Selected = ToBool(Get(raw, "Selected"));
            row.Id = ToInt(Get(raw, "Id"));
            row.Risk = ToText(Get(raw, "Risk"));
            row.Vendor = ToText(Get(raw, "Vendor"));
            row.SourceType = ToText(Get(raw, "SourceType"));
            row.Title = ToText(Get(raw, "Title"));
            row.RecommendedAction = ToText(Get(raw, "RecommendedAction"));
            row.Location = ToText(Get(raw, "Location"));
            row.HumanType = DescribeSourceType(row.SourceType, row.RecommendedAction);
            row.HumanSubject = DescribeSubject(row.SourceType, row.Title, row.Location, row.RecommendedAction, row.Vendor);
            row.HumanAction = DescribeAction(row.RecommendedAction);
            row.SearchText = string.Join(" ", new[] { row.Risk, row.Vendor, row.SourceType, row.HumanType, row.Title, row.HumanSubject, row.RecommendedAction, row.HumanAction, row.Location });
            return row;
        }

        public string GetToolTipText(string propertyName)
        {
            List<string> lines = new List<string>();
            if (propertyName == "HumanSubject" || propertyName == "HumanType")
            {
                lines.Add(HumanSubject);
                List<string> preview = GetSimulatedMenuItems();
                if (preview.Count > 0)
                {
                    lines.Add("");
                    lines.Add("鼠标停在这里会弹出模拟右键菜单。");
                    lines.Add("预览内容：");
                    foreach (string item in preview.Take(8))
                    {
                        lines.Add("  " + item);
                    }
                    string footnote = GetPreviewFootnote();
                    if (!string.IsNullOrWhiteSpace(footnote))
                    {
                        lines.Add("");
                        lines.Add(footnote);
                    }
                }
                return string.Join(Environment.NewLine, lines.ToArray());
            }
            if (propertyName == "HumanAction")
            {
                return HumanAction + Environment.NewLine + "真正清理前会再次弹窗确认，并先写备份。";
            }
            if (propertyName == "Location")
            {
                return "技术藏身位置：" + Environment.NewLine + Location + Environment.NewLine + Environment.NewLine + "这是给懂注册表/服务/任务的人核对用的，小白主要看前面的“用户会看到/受到什么影响”。";
            }
            if (propertyName == "Vendor")
            {
                return "识别到的软件来源：" + Vendor;
            }
            if (propertyName == "Risk")
            {
                return "风险等级：" + Risk + Environment.NewLine + "高风险不会默认勾选；不确定就先别清理。";
            }
            if (propertyName == "Selected")
            {
                return "勾上才会清理。点表头“选”可以全选/全不选当前列表。";
            }
            object rawValue = Get(Raw, propertyName);
            return rawValue == null ? string.Empty : Convert.ToString(rawValue);
        }

        public List<string> GetSimulatedMenuItems()
        {
            switch (SourceType)
            {
                case "ContextMenu":
                    return BuildContextMenuPreview(Title, Location, Vendor);
                case "AppxContextMenu":
                    return BuildAppxContextMenuPreview(Title);
                case "FileAssociation":
                    return BuildFileAssociationPreview(Title);
                default:
                    return new List<string>();
            }
        }

        public string GetPreviewFootnote()
        {
            if (SourceType == "ContextMenu")
            {
                string target = DescribeContextTarget(Location);
                string leaf = ExtractRegistryLeaf(Location);
                if (!string.IsNullOrWhiteSpace(leaf))
                {
                    return "出现位置：" + target + "；注册表项：" + leaf;
                }
                return "出现位置：" + target;
            }
            if (SourceType == "AppxContextMenu")
            {
                return "这是 Windows App 注册的现代右键菜单，第一版只报告，不直接拆 App 包。";
            }
            if (SourceType == "FileAssociation")
            {
                return "这是“打开方式/文件关联”，不是普通右键扩展。";
            }
            return string.Empty;
        }

        private static string DescribeSourceType(string sourceType, string action)
        {
            switch (sourceType)
            {
                case "ContextMenu":
                    return "右键菜单";
                case "AppxContextMenu":
                    return "Windows 右键菜单";
                case "StartupRegistry":
                    return "开机自启";
                case "StartupFolder":
                    return "开机自启文件夹";
                case "ScheduledTask":
                    return "计划任务/定时拉起";
                case "Service":
                    return "后台服务";
                case "BrowserExtension":
                    return "浏览器插件/外部宿主";
                case "FileAssociation":
                    return "文件关联/打开方式";
                case "InstalledApplication":
                    return "主程序卸载项";
                case "UninstallResidue":
                    return "卸载残留";
                default:
                    return string.IsNullOrWhiteSpace(sourceType) ? "未知小动作" : sourceType;
            }
        }

        private static string DescribeSubject(string sourceType, string title, string location, string action, string vendor)
        {
            string name = CleanName(title);
            switch (sourceType)
            {
                case "ContextMenu":
                    return DescribeContextMenuSubject(name, location);
                case "AppxContextMenu":
                    return DescribeAppxContextMenuSubject(name);
                case "InstalledApplication":
                    return "主程序：" + name + "（默认不扫描）";
                case "BrowserExtension":
                    return DescribeBrowserExtensionSubject(name, location, vendor);
                case "FileAssociation":
                    return DescribeFileAssociationSubject(name);
                case "Service":
                    return DescribeServiceSubject(name, vendor);
                case "ScheduledTask":
                    return DescribeScheduledTaskSubject(name, vendor);
                case "StartupRegistry":
                    return DescribeStartupSubject(name, vendor);
                case "StartupFolder":
                    return "开机后会自动运行这个文件：" + name;
                case "UninstallResidue":
                    return "残留项：" + name;
                default:
                    return string.IsNullOrWhiteSpace(name) ? "未识别名称" : name;
            }
        }

        private static string DescribeContextMenuSubject(string title, string location)
        {
            string cleaned = CleanName(title);
            if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(location))
            {
                cleaned = location.Split('\\').LastOrDefault();
            }

            string haystack = CleanName(cleaned + " " + location);
            if (haystack.IndexOf("Safe360Ext", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：使用360解除占用 / 使用360强力删除 / 使用360进行木马云查杀 / 使用360管理右键菜单";
            }
            if (haystack.IndexOf("SoftMgrExt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：360软件管家右键卸载/管理右键菜单";
            }
            if (haystack.IndexOf("BaiduNetdisk", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("百度网盘", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：百度网盘相关入口（" + DescribeContextTarget(location) + "）";
            }
            if (haystack.IndexOf("WPS", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：WPS/金山相关入口（" + DescribeContextTarget(location) + "）";
            }
            if (haystack.IndexOf("Xunlei", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("Thunder", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("迅雷", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：迅雷相关入口（" + DescribeContextTarget(location) + "）";
            }

            string visible = ExtractVisibleMenuText(cleaned);
            if (!string.IsNullOrWhiteSpace(visible))
            {
                return "右键会出现：" + visible;
            }

            string extensionName = ExtractRegistryLeaf(location);
            if (!string.IsNullOrWhiteSpace(extensionName))
            {
                return "右键会多出一个动态扩展入口（" + DescribeContextTarget(location) + "，悬停看模拟菜单）";
            }

            return "右键会多出一个动态扩展入口（悬停看模拟菜单）";
        }

        private static string DescribeAppxContextMenuSubject(string title)
        {
            string name = CleanName(title);
            if (name.IndexOf("BaiduNetdisk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：百度网盘同步/网盘相关菜单";
            }
            if (name.IndexOf("Photos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "右键会出现：AI 操作 / 借助 Designer 创建";
            }
            return "右键来源：" + name + "（App 菜单文字需在右键里核对）";
        }

        private static List<string> BuildContextMenuPreview(string title, string location, string vendor)
        {
            List<string> items = new List<string>();
            string cleaned = CleanName(title);
            string haystack = CleanName(cleaned + " " + location + " " + vendor);
            string visible = ExtractVisibleMenuText(cleaned);

            if (!string.IsNullOrWhiteSpace(visible))
            {
                AddUnique(items, visible);
            }

            if (haystack.IndexOf("Safe360Ext", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "使用 360 解除占用");
                AddUnique(items, "使用 360 强力删除");
                AddUnique(items, "使用 360 进行木马云查杀");
                AddUnique(items, "使用 360 管理右键菜单");
            }
            else if (haystack.IndexOf("SoftMgrExt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "360 软件管家右键卸载");
                AddUnique(items, "使用 360 管理右键菜单");
            }
            else if (haystack.IndexOf("BaiduNetdisk", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("百度网盘", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "上传到百度网盘");
                AddUnique(items, "同步到百度网盘");
                AddUnique(items, "使用百度网盘打开");
            }
            else if (haystack.IndexOf("WPS", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("Kingsoft", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("qingshellext", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("kwpsshellext", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (haystack.IndexOf("Open With", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("qingshellext", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddUnique(items, "用 WPS 打开");
                }
                AddUnique(items, "WPS/金山相关入口");
                AddUnique(items, "上传/同步到 WPS 云文档");
            }
            else if (haystack.IndexOf("Xunlei", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("Thunder", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("迅雷", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "使用迅雷下载");
                AddUnique(items, "添加到迅雷下载");
            }
            else if (haystack.IndexOf("Sogou", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("搜狗", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "搜狗输入法相关入口");
            }

            if (items.Count == 0)
            {
                string vendorPrefix = FriendlyVendorPrefix(vendor).Trim();
                if (string.IsNullOrWhiteSpace(vendorPrefix)) vendorPrefix = "这个软件";
                AddUnique(items, vendorPrefix + "动态右键入口");
            }

            return items;
        }

        private static List<string> BuildAppxContextMenuPreview(string title)
        {
            List<string> items = new List<string>();
            string name = CleanName(title);
            if (name.IndexOf("Photos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "AI 操作");
                AddUnique(items, "借助 Designer 创建");
                AddUnique(items, "使用“照片”编辑");
            }
            else if (name.IndexOf("BaiduNetdisk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUnique(items, "百度网盘同步");
                AddUnique(items, "百度网盘相关菜单");
            }
            else
            {
                AddUnique(items, name + " 相关右键入口");
            }
            return items;
        }

        private static List<string> BuildFileAssociationPreview(string title)
        {
            List<string> items = new List<string>();
            string name = CleanName(title);
            string ext = ExtractExtensionFromTitle(name);
            string handler = string.Empty;

            if (name.IndexOf("打开方式：", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                handler = name.Substring(name.IndexOf("打开方式：", StringComparison.OrdinalIgnoreCase) + "打开方式：".Length);
            }
            else if (name.Contains("->"))
            {
                string[] parts = name.Split(new[] { "->" }, StringSplitOptions.None);
                handler = parts.Length > 1 ? CleanName(parts[1]) : name;
            }

            string readable = DescribeHandler(handler);
            AddUnique(items, "打开");
            if (!string.IsNullOrWhiteSpace(readable))
            {
                AddUnique(items, "打开方式 > " + readable);
            }
            if (!string.IsNullOrWhiteSpace(ext))
            {
                AddUnique(items, "影响文件类型：" + ext);
            }
            return items;
        }

        private static void AddUnique(List<string> items, string value)
        {
            string cleaned = CleanName(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return;
            foreach (string item in items)
            {
                if (string.Equals(item, cleaned, StringComparison.OrdinalIgnoreCase)) return;
            }
            items.Add(cleaned);
        }

        private static string DescribeFileAssociationSubject(string title)
        {
            string name = CleanName(title);
            string ext = ExtractExtensionFromTitle(name);
            if (name.IndexOf("打开方式：", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string handler = name.Substring(name.IndexOf("打开方式：", StringComparison.OrdinalIgnoreCase) + "打开方式：".Length);
                return "右键“打开方式”里会出现：" + DescribeHandler(handler) + DescribeExtensionSuffix(ext);
            }
            if (name.Contains("->"))
            {
                string[] parts = name.Split(new[] { "->" }, StringSplitOptions.None);
                string left = parts.Length > 0 ? CleanName(parts[0]) : ext;
                string handler = parts.Length > 1 ? CleanName(parts[1]) : name;
                return "双击/打开 " + left + " 会交给：" + DescribeHandler(handler);
            }
            return "文件关联：" + name;
        }

        private static string DescribeScheduledTaskSubject(string title, string vendor)
        {
            string name = CleanName(title);
            string haystack = CleanName(name + " " + vendor);
            if (haystack.IndexOf("QihooGetWordSearchFatch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "360 会定时拉起划词/搜索相关组件。";
            }
            if (haystack.IndexOf("WpsUpdateLogonTask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS 会在用户登录后自动检查/拉起更新。";
            }
            if (haystack.IndexOf("WpsUpdateTask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS 会按计划自动检查/拉起更新。";
            }
            if (haystack.IndexOf("wpswakewnslogontask", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS 会在登录后唤醒推送/云消息组件。";
            }
            return FriendlyVendorPrefix(vendor) + "会定时拉起任务：" + name;
        }

        private static string DescribeServiceSubject(string title, string vendor)
        {
            string name = CleanName(title);
            string haystack = CleanName(name + " " + vendor);
            if (haystack.IndexOf("ZhuDongFangYu", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("主动防御", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "360 的主动防御后台服务会常驻运行。";
            }
            if (haystack.IndexOf("Q360AMPPL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "360 的安全防护组件会作为后台服务运行。";
            }
            if (haystack.IndexOf("BaiduNetdiskUtility", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "百度网盘后台工具服务会常驻运行。";
            }
            if (haystack.IndexOf("SogouSvc", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("搜狗", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "搜狗输入法后台服务会常驻运行。";
            }
            if (haystack.IndexOf("WPS Office Cloud Service", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS 云文档/同步后台服务会常驻运行。";
            }
            return FriendlyVendorPrefix(vendor) + "后台服务会常驻运行：" + name;
        }

        private static string DescribeBrowserExtensionSubject(string title, string location, string vendor)
        {
            string name = CleanName(title);
            string haystack = CleanName(name + " " + location + " " + vendor);
            if (haystack.IndexOf("com.kingsoft.chrome.extension.host", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS/金山给 Chrome/Edge 注册了外部插件通道。";
            }
            if (haystack.IndexOf("kingsoft", StringComparison.OrdinalIgnoreCase) >= 0 || haystack.IndexOf("WPS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "WPS/金山浏览器插件或外部宿主会被浏览器调用。";
            }
            return FriendlyVendorPrefix(vendor) + "浏览器插件/外部宿主：" + name;
        }

        private static string DescribeStartupSubject(string title, string vendor)
        {
            string name = CleanName(title);
            string haystack = CleanName(name + " " + vendor);
            if (haystack.IndexOf("360Safetray", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "开机后自动启动 360 安全托盘/常驻图标。";
            }
            if (haystack.IndexOf("BaiduYunDetect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "开机后自动启动百度网盘检测/守护组件。";
            }
            return FriendlyVendorPrefix(vendor) + "开机后自动启动：" + name;
        }

        private static string DescribeHandler(string handler)
        {
            string value = CleanName(handler);
            string lower = value.ToLowerInvariant();
            if (lower.Contains("baidunetdiskunite.open") || lower.Contains("baidunetdisk.open"))
            {
                return "百度网盘";
            }
            if (lower.Contains("wps.doc") || lower.Contains("wps.docx"))
            {
                return "WPS 文字";
            }
            if (lower.Contains("kwps.pdf"))
            {
                return "WPS PDF";
            }
            if (lower.Contains("wpp.ppt") || lower.Contains("wpp.pptx"))
            {
                return "WPS 演示";
            }
            if (lower.Contains("et.xls") || lower.Contains("et.xlsx"))
            {
                return "WPS 表格";
            }
            if (lower.Contains("xunlei.xlb"))
            {
                return "迅雷";
            }
            return value;
        }

        private static string DescribeExtensionSuffix(string ext)
        {
            return string.IsNullOrWhiteSpace(ext) ? string.Empty : "（影响 " + ext + " 文件）";
        }

        private static string ExtractExtensionFromTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(title, "\\.[A-Za-z0-9]{1,8}");
            return match.Success ? match.Value.ToLowerInvariant() : string.Empty;
        }

        private static string DescribeContextTarget(string location)
        {
            string value = CleanName(location);
            string lower = value.ToLowerInvariant();
            if (lower.Contains("\\desktopbackground\\") || lower.Contains("\\directory\\background\\"))
            {
                return "桌面/文件夹空白处右键";
            }
            if (lower.Contains("\\drive\\"))
            {
                return "磁盘盘符右键";
            }
            if (lower.Contains("\\directory\\"))
            {
                return "文件夹右键";
            }
            if (lower.Contains("\\lnkfile\\"))
            {
                return "快捷方式右键";
            }
            if (lower.Contains("\\allfilesystemobjects\\"))
            {
                return "文件/文件夹右键";
            }
            if (lower.Contains("\\*\\"))
            {
                return "普通文件右键";
            }
            if (lower.Contains("\\systemfileassociations\\"))
            {
                return "指定类型文件右键";
            }
            return "资源管理器右键菜单";
        }

        private static string FriendlyVendorPrefix(string vendor)
        {
            string value = CleanName(vendor);
            if (string.IsNullOrWhiteSpace(value) || value == "未知第三方") return "这个软件";
            return value + " ";
        }

        private static string ExtractVisibleMenuText(string title)
        {
            string cleaned = System.Text.RegularExpressions.Regex.Replace(title, "\\{[0-9A-Fa-f-]{36}\\}", "").Trim();
            cleaned = cleaned.Replace("ShellContextMenu Class", "").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            if (cleaned.EndsWith("Ext", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (cleaned.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0) return string.Empty;
            return cleaned;
        }

        private static string ExtractRegistryLeaf(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return string.Empty;
            string[] parts = location.Split('\\');
            return parts.Length == 0 ? string.Empty : CleanName(parts[parts.Length - 1]);
        }

        private static string CleanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return System.Text.RegularExpressions.Regex.Replace(cleaned, "\\s+", " ").Trim();
        }

        private static string DescribeAction(string action)
        {
            switch (action)
            {
                case "DeleteRegistryKey":
                    return "删除这条注册表";
                case "DeleteRegistryValue":
                    return "删除这个注册表值";
                case "DisableScheduledTask":
                    return "禁用计划任务";
                case "DisableService":
                    return "禁用后台服务";
                case "InvokeUninstaller":
                    return "调用静默卸载";
                case "MoveFileToBackup":
                    return "移到备份目录";
                case "ReportOnly":
                    return "只报告，不自动动手";
                default:
                    return string.IsNullOrWhiteSpace(action) ? "只报告" : action;
            }
        }

        private static object Get(Dictionary<string, object> raw, string key)
        {
            object value;
            return raw.TryGetValue(key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static int ToInt(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt32(value); } catch { return 0; }
        }

        private static bool ToBool(object value)
        {
            if (value == null) return false;
            try { return Convert.ToBoolean(value); } catch { return false; }
        }
    }

    internal sealed class GradientPanel : Panel
    {
        public Color StartColor { get; set; }
        public Color EndColor { get; set; }

        public GradientPanel()
        {
            DoubleBuffered = true;
            StartColor = Color.FromArgb(15, 118, 110);
            EndColor = Color.FromArgb(30, 64, 175);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }
    }

    internal sealed class RogueLogoControl : Control
    {
        public RogueLogoControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = ClientRectangle;
            int size = Math.Min(r.Width, r.Height);
            Rectangle box = new Rectangle((r.Width - size) / 2 + 2, (r.Height - size) / 2 + 2, size - 4, size - 4);

            using (GraphicsPath background = RoundedRectangle(box, 8))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(box, Color.FromArgb(5, 150, 105), Color.FromArgb(13, 148, 136), 45F))
                {
                    g.FillPath(brush, background);
                }
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.6F))
                {
                    g.DrawPath(pen, background);
                }
            }

            Rectangle lens = new Rectangle(box.Left + 8, box.Top + 10, box.Width * 48 / 100, box.Height * 48 / 100);
            using (Pen pen = new Pen(Color.White, 4.6F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, lens);
                g.DrawLine(pen, lens.Right - 3, lens.Bottom - 3, lens.Right + 12, lens.Bottom + 12);
            }

            Rectangle menu = new Rectangle(box.Left + box.Width * 42 / 100, box.Top + box.Height * 42 / 100, box.Width * 48 / 100, box.Height * 34 / 100);
            using (GraphicsPath menuPath = RoundedRectangle(menu, 6))
            {
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    g.FillPath(brush, menuPath);
                }
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240), 1.4F))
                {
                    g.DrawPath(pen, menuPath);
                }
                using (Pen pen = new Pen(Color.FromArgb(5, 150, 105), 2.4F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    int y1 = menu.Top + menu.Height * 30 / 100;
                    int y2 = menu.Top + menu.Height * 52 / 100;
                    int y3 = menu.Top + menu.Height * 74 / 100;
                    g.DrawLine(pen, menu.Left + 7, y1, menu.Right - 8, y1);
                    g.DrawLine(pen, menu.Left + 7, y2, menu.Right - 8, y2);
                    g.DrawLine(pen, menu.Left + 7, y3, menu.Right - 16, y3);
                }
            }

            Point[] tail =
            {
                new Point(menu.Left + menu.Width * 22 / 100, menu.Bottom - 1),
                new Point(menu.Left + menu.Width * 38 / 100, menu.Bottom + 8),
                new Point(menu.Left + menu.Width * 44 / 100, menu.Bottom - 1)
            };
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                g.FillPolygon(brush, tail);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

}
