using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RogueCleanerV2
{
    internal enum ActionButtonRole
    {
        Primary,
        Standard,
        Warning,
        Danger,
        Close
    }

    internal static class UiTheme
    {
        public static readonly Color Canvas = Color.FromArgb(245, 247, 250);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(222, 228, 236);
        public static readonly Color Primary = Color.FromArgb(15, 118, 110);
        public static readonly Color PrimaryHover = Color.FromArgb(13, 148, 136);
        public static readonly Color PrimarySoft = Color.FromArgb(232, 248, 246);
        public static readonly Color FieldFill = Color.FromArgb(241, 245, 249);
        public static readonly Color Text = Color.FromArgb(31, 41, 55);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(234, 88, 12);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Info = Color.FromArgb(37, 99, 235);
        private static Image toggleOnImage;
        private static Image toggleOffImage;
        private static readonly ConditionalWeakTable<TextBox, TextBoxBorderRenderer> textBoxRenderers = new ConditionalWeakTable<TextBox, TextBoxBorderRenderer>();
        private static readonly ConditionalWeakTable<DataGridView, object> gridAlignmentWired = new ConditionalWeakTable<DataGridView, object>();

        public static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static void ApplyWindowIdentity(Form form)
        {
            if (form == null) return;
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { form.Icon = SystemIcons.Application; }
            form.ShowIcon = true;
            form.Shown += delegate { ApplyModernPolish(form); };
        }

        public static void PrimaryButton(Button button, string text, Color color)
        {
            BaseButton(button, text);
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Lighten(color, 12);
            button.FlatAppearance.MouseDownBackColor = Darken(color, 12);
            button.Image = CreateActionGlyph(text, Color.White);
            button.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRectangle(new Rectangle(1, 1, Math.Max(1, button.Width - 3), Math.Max(1, button.Height - 3)), 7))
                using (Pen pen = new Pen(Darken(color, 8), 1F)) e.Graphics.DrawPath(pen, path);
            };
        }

        public static void HighlightButton(Button button, string text)
        {
            BaseButton(button, text);
            button.BackColor = PrimarySoft;
            button.ForeColor = Primary;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(209, 250, 229);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(167, 243, 208);
            button.Image = CreateActionGlyph(text, Primary);
            button.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRectangle(new Rectangle(1, 1, Math.Max(1, button.Width - 3), Math.Max(1, button.Height - 3)), 7))
                using (Pen pen = new Pen(Primary, 1.35F)) e.Graphics.DrawPath(pen, path);
            };
        }

        public static void OutlineButton(Button button, string text, Color color)
        {
            BaseButton(button, text);
            button.BackColor = Surface;
            button.ForeColor = color;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = color;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 252);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(241, 245, 249);
            button.Image = CreateActionGlyph(text, color);
            button.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRectangle(new Rectangle(1, 1, Math.Max(1, button.Width - 3), Math.Max(1, button.Height - 3)), 6))
                using (Pen pen = new Pen(color, 1.25F)) e.Graphics.DrawPath(pen, path);
            };
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

        public static void ToolButton(Button button, string text, Icon icon)
        {
            BaseButton(button, text);
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Height = 34;
            button.MinimumSize = new Size(94, 34);
            button.Padding = new Padding(7, 0, 7, 0);
            button.Margin = new Padding(0, 0, 7, 0);
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.Image = CreateActionGlyph(text, ActionColor(text));
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        public static void ActionButton(Button button, string text, ActionButtonRole role)
        {
            if (role == ActionButtonRole.Primary) HighlightButton(button, text);
            else if (role == ActionButtonRole.Warning) OutlineButton(button, text, Warning);
            else if (role == ActionButtonRole.Danger) OutlineButton(button, text, Danger);
            else if (role == ActionButtonRole.Close) OutlineButton(button, text, Muted);
            else OutlineButton(button, text, Primary);

            button.MinimumSize = new Size(104, 36);
            button.AutoSize = false;
            button.Size = new Size(Math.Max(104, button.Width), 36);
            button.Padding = new Padding(9, 0, 9, 0);
            button.Margin = new Padding(0, 0, 8, 0);
            button.Tag = "ActionButton:" + role;
        }

        public static Control ModuleHeader(string title, string subtitle)
        {
            CardPanel header = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 14, 7), Margin = new Padding(0, 0, 0, 8) };
            Label titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 30, Font = Font(15F, FontStyle.Bold), ForeColor = Text, TextAlign = ContentAlignment.MiddleLeft };
            Label subtitleLabel = new Label { Text = subtitle, Dock = DockStyle.Fill, Font = Font(8.5F, FontStyle.Regular), ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            header.Controls.Add(subtitleLabel);
            header.Controls.Add(titleLabel);
            return header;
        }

        private static Image ResizeIcon(Icon icon, int width, int height)
        {
            if (icon == null) return null;
            using (Bitmap source = icon.ToBitmap())
            {
                Bitmap result = new Bitmap(width, height);
                using (Graphics graphics = Graphics.FromImage(result)) graphics.DrawImage(source, new Rectangle(0, 0, width, height));
                return result;
            }
        }

        private static Color ActionColor(string text)
        {
            Color color = Primary;
            string value = text ?? string.Empty;
            if (value.IndexOf("隐藏", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("禁用", StringComparison.OrdinalIgnoreCase) >= 0) color = Warning;
            else if (value.IndexOf("删除", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("清理", StringComparison.OrdinalIgnoreCase) >= 0) color = Danger;
            return color;
        }

        private static Image CreateActionGlyph(string text, Color color)
        {
            string value = text ?? string.Empty;
            Bitmap bitmap = new Bitmap(18, 18);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(color, 1.7F))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if (value.IndexOf("刷新", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawArc(pen, 3, 3, 12, 12, -55, 285); graphics.DrawLines(pen, new PointF[] { new PointF(14, 2.5F), new PointF(15, 7), new PointF(10.5F, 5.7F) });
                }
                else if (value.IndexOf("显示", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("启用", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("勾选", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 2.5F, 2.5F, 13, 13); graphics.DrawLines(pen, new PointF[] { new PointF(5, 9), new PointF(8, 12), new PointF(14, 6) });
                }
                else if (value.IndexOf("隐藏", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("禁用", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawEllipse(pen, 2.5F, 2.5F, 13, 13); graphics.DrawLine(pen, 5.5F, 9, 12.5F, 9);
                }
                else if (value.IndexOf("修改", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("编辑", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawLine(pen, 4, 14, 13.5F, 4.5F); graphics.DrawLine(pen, 11.5F, 3.5F, 14.5F, 6.5F); graphics.DrawLines(pen, new PointF[] { new PointF(4, 14), new PointF(3.5F, 10.5F), new PointF(7, 14.5F) });
                }
                else if (value.IndexOf("添加", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawEllipse(pen, 2.5F, 2.5F, 13, 13); graphics.DrawLine(pen, 9, 5.5F, 9, 12.5F); graphics.DrawLine(pen, 5.5F, 9, 12.5F, 9);
                }
                else if (value.IndexOf("删除", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("清理", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 5, 6, 8, 9); graphics.DrawLine(pen, 3.5F, 5, 14.5F, 5); graphics.DrawLine(pen, 7, 3, 11, 3); graphics.DrawLine(pen, 8, 8, 8, 13); graphics.DrawLine(pen, 10.5F, 8, 10.5F, 13);
                }
                else if (value.IndexOf("复制", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 5.5F, 5.5F, 9.5F, 9.5F); graphics.DrawRectangle(pen, 2.5F, 2.5F, 9.5F, 9.5F);
                }
                else if (value.IndexOf("更多", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.FillEllipse(brush, 3, 8, 2, 2); graphics.FillEllipse(brush, 8, 8, 2, 2); graphics.FillEllipse(brush, 13, 8, 2, 2);
                }
                else if (value.IndexOf("系统", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawPolygon(pen, new PointF[] { new PointF(9, 2.5F), new PointF(14, 4.5F), new PointF(13.2F, 11), new PointF(9, 15.5F), new PointF(4.8F, 11), new PointF(4, 4.5F) }); graphics.DrawLines(pen, new PointF[] { new PointF(6.5F, 9), new PointF(8.4F, 11), new PointF(12, 7) });
                }
                else if (value.IndexOf("技术", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("定位", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawEllipse(pen, 4, 4, 10, 10); graphics.DrawLine(pen, 9, 1.5F, 9, 6); graphics.DrawLine(pen, 9, 12, 9, 16.5F); graphics.DrawLine(pen, 1.5F, 9, 6, 9); graphics.DrawLine(pen, 12, 9, 16.5F, 9);
                }
                else if (value.IndexOf("向上", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("向下", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bool down = value.IndexOf("向下", StringComparison.OrdinalIgnoreCase) >= 0; float from = down ? 4 : 14; float to = down ? 14 : 4; graphics.DrawLine(pen, 9, from, 9, to); graphics.DrawLines(pen, new PointF[] { new PointF(5.5F, down ? 10.5F : 7.5F), new PointF(9, to), new PointF(12.5F, down ? 10.5F : 7.5F) });
                }
                else if (value.IndexOf("扫描", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawEllipse(pen, 2.5F, 2.5F, 10, 10); graphics.DrawLine(pen, 11, 11, 15.5F, 15.5F);
                }
                else if (value.IndexOf("恢复", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawArc(pen, 3, 3, 12, 12, -45, 280); graphics.DrawLines(pen, new PointF[] { new PointF(3, 3), new PointF(3, 8), new PointF(7.5F, 5.5F) });
                }
                else if (value.IndexOf("报告", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 4, 2.5F, 10, 13); graphics.DrawLine(pen, 6.5F, 7, 11.5F, 7); graphics.DrawLine(pen, 6.5F, 10, 11.5F, 10); graphics.DrawLine(pen, 6.5F, 13, 10, 13);
                }
                else graphics.DrawEllipse(pen, 4, 4, 10, 10);
            }
            return bitmap;
        }

        public static void NavButton(Button button, string text)
        {
            button.Text = text;
            button.Tag = text;
            button.Dock = DockStyle.Top;
            button.Height = 48;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(18, 0, 0, 0);
            button.Margin = new Padding(0);
            button.Font = Font(9.5F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Surface;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
            button.Image = CreateNavigationGlyph(text, Muted);
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        public static void SetNavActive(Button button, bool active)
        {
            button.BackColor = active ? PrimarySoft : Surface;
            button.ForeColor = active ? Primary : Text;
            button.Font = Font(9.5F, active ? FontStyle.Bold : FontStyle.Regular);
            button.Image = CreateNavigationGlyph(Convert.ToString(button.Tag), active ? Primary : Muted);
        }

        private static Image CreateNavigationGlyph(string text, Color color)
        {
            Bitmap bitmap = new Bitmap(20, 20);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(color, 1.7F))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias; pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                string value = text ?? string.Empty;
                if (value.IndexOf("总览", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawPolygon(pen, new PointF[] { new PointF(10, 2.5F), new PointF(16, 5), new PointF(15, 12.5F), new PointF(10, 17), new PointF(5, 12.5F), new PointF(4, 5) }); graphics.DrawLines(pen, new PointF[] { new PointF(7, 10), new PointF(9, 12), new PointF(13.5F, 7.5F) });
                }
                else if (value.IndexOf("启动", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawArc(pen, 3, 3, 14, 14, -55, 290); graphics.DrawLine(pen, 10, 1.8F, 10, 9); graphics.DrawLine(pen, 7.5F, 4, 10, 1.8F); graphics.DrawLine(pen, 12.5F, 4, 10, 1.8F);
                }
                else if (value.IndexOf("右键", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 5, 2.5F, 10, 15); graphics.DrawLine(pen, 10, 2.5F, 10, 8); graphics.DrawLine(pen, 5, 8, 15, 8); graphics.DrawEllipse(pen, 8.8F, 4.5F, 2.4F, 2.4F);
                }
                else if (value.IndexOf("弹窗", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("诊断", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    graphics.DrawRectangle(pen, 2.5F, 3.5F, 15, 12); graphics.DrawLine(pen, 2.5F, 7, 17.5F, 7); graphics.DrawEllipse(pen, 12.5F, 9.5F, 2.5F, 2.5F);
                }
                else
                {
                    graphics.DrawArc(pen, 3, 3, 14, 14, -45, 275); graphics.DrawLines(pen, new PointF[] { new PointF(3, 3), new PointF(3, 8), new PointF(7.5F, 5.5F) });
                }
            }
            return bitmap;
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
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            RoundControl(button, 6);
        }

        public static void ApplyModernPolish(Control root)
        {
            if (root == null) return;
            DataGridView grid = root as DataGridView;
            if (grid != null) StyleGrid(grid);
            Button button = root as Button;
            if (button != null) RoundControl(button, 6);
            ComboBox combo = root as ComboBox;
            if (combo != null) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = FieldFill; RoundControl(combo, 5); }
            TextBox box = root as TextBox;
            if (box != null && !box.Multiline)
            {
                box.BackColor = Surface;
                box.BorderStyle = BorderStyle.None;
                RoundControl(box, 5);
                textBoxRenderers.GetValue(box, delegate(TextBox item) { return new TextBoxBorderRenderer(item); });
            }
            NumericUpDown number = root as NumericUpDown;
            if (number != null) { number.BackColor = FieldFill; number.BorderStyle = BorderStyle.None; RoundControl(number, 5); }
            foreach (Control child in root.Controls) ApplyModernPolish(child);
        }

        public static void StyleGrid(DataGridView grid)
        {
            if (grid == null) return;
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Math.Max(38, grid.ColumnHeadersHeight);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = Font(9F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            grid.RowTemplate.Height = Math.Max(36, grid.RowTemplate.Height);
            grid.DefaultCellStyle.BackColor = Surface;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 254);
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.SelectionBackColor = PrimarySoft;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.RowHeadersVisible = false;
            grid.ShowCellToolTips = true;
            ApplyGridAlignment(grid);
            object marker;
            if (!gridAlignmentWired.TryGetValue(grid, out marker))
            {
                gridAlignmentWired.Add(grid, new object());
                grid.DataBindingComplete += delegate { ApplyGridAlignment(grid); };
                grid.ColumnAdded += delegate { ApplyGridAlignment(grid); };
            }
        }

        private static void ApplyGridAlignment(DataGridView grid)
        {
            if (grid == null || grid.IsDisposed) return;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }
        }

        public static ModernGridHost AttachModernScrollBar(Control container, DataGridView grid)
        {
            if (container == null || grid == null) return null;
            ModernGridHost host = new ModernGridHost(grid) { Dock = DockStyle.Fill };
            container.Controls.Add(host);
            return host;
        }

        public static Image ToggleImage(bool enabled)
        {
            if (enabled && toggleOnImage != null) return toggleOnImage;
            if (!enabled && toggleOffImage != null) return toggleOffImage;
            Bitmap bitmap = new Bitmap(34, 20);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush track = new SolidBrush(enabled ? Primary : Color.FromArgb(203, 213, 225)))
            using (SolidBrush knob = new SolidBrush(Color.White))
            using (Pen border = new Pen(enabled ? Primary : Color.FromArgb(174, 186, 201), 1F))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedRectangle(new Rectangle(1, 2, 31, 15), 8))
                {
                    graphics.FillPath(track, path);
                    graphics.DrawPath(border, path);
                }
                graphics.FillEllipse(knob, enabled ? 18 : 3, 4, 11, 11);
            }
            if (enabled) toggleOnImage = bitmap; else toggleOffImage = bitmap;
            return bitmap;
        }

        private static void RoundControl(Control control, int radius)
        {
            if (control == null) return;
            EventHandler apply = null;
            apply = delegate
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                Rectangle bounds = new Rectangle(0, 0, control.Width, control.Height);
                using (GraphicsPath path = RoundedRectangle(bounds, radius))
                {
                    Region old = control.Region;
                    control.Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            };
            control.SizeChanged -= apply;
            control.SizeChanged += apply;
            apply(control, EventArgs.Empty);
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle border = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath path = UiTheme.RoundedRectangle(border, 7))
            using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen, path);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width > 0 && Height > 0)
            {
                using (GraphicsPath path = UiTheme.RoundedRectangle(new Rectangle(0, 0, Width, Height), 8))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            Invalidate();
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

    internal sealed class SummaryCardPanel : Panel
    {
        public SummaryCardPanel()
        {
            DoubleBuffered = true;
            BackColor = UiTheme.Canvas;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath path = UiTheme.RoundedRectangle(bounds, 7))
            using (SolidBrush surface = new SolidBrush(UiTheme.Surface))
            using (Pen pen = new Pen(Color.FromArgb(203, 213, 225), 1.15F))
            {
                e.Graphics.FillPath(surface, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }
    }

    internal sealed class TextBoxBorderRenderer : NativeWindow
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private const int EmSetMargins = 0x00D3;
        private const int EcLeftMargin = 0x0001;
        private const int EcRightMargin = 0x0002;
        private readonly TextBox box;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public TextBoxBorderRenderer(TextBox box)
        {
            this.box = box;
            box.HandleCreated += HandleCreated;
            box.HandleDestroyed += HandleDestroyed;
            if (box.IsHandleCreated) Attach();
        }

        private void HandleCreated(object sender, EventArgs e) { Attach(); }
        private void HandleDestroyed(object sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }

        private void Attach()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
            AssignHandle(box.Handle);
            int margins = (8 << 16) | 8;
            SendMessage(box.Handle, EmSetMargins, (IntPtr)(EcLeftMargin | EcRightMargin), (IntPtr)margins);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmPaint && message.Msg != WmNcPaint) return;
            try
            {
                using (Graphics graphics = Graphics.FromHwnd(box.Handle))
                using (GraphicsPath path = UiTheme.RoundedRectangle(new Rectangle(1, 1, Math.Max(1, box.Width - 3), Math.Max(1, box.Height - 3)), 5))
                using (Pen pen = new Pen(Color.FromArgb(148, 163, 184), 1.15F))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawPath(pen, path);
                }
            }
            catch { }
        }
    }

    internal sealed class ModernScrollBar : Control
    {
        private int value;
        private int maximum;
        private int viewport = 1;
        private int extent = 1;
        private bool hovering;
        private bool dragging;
        private int dragOffset;

        public event EventHandler ValueChanged;
        public int MinimumThumbLength { get { return 48; } }
        public int Value { get { return value; } }

        public ModernScrollBar()
        {
            Width = 20;
            MinimumSize = new Size(20, 60);
            BackColor = UiTheme.Surface;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetMetrics(int newValue, int newMaximum, int newViewport, int newExtent)
        {
            maximum = Math.Max(0, newMaximum);
            viewport = Math.Max(1, newViewport);
            extent = Math.Max(viewport, newExtent);
            value = Math.Max(0, Math.Min(maximum, newValue));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = TrackRectangle();
            Rectangle thumb = ThumbRectangle();
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(236, 241, 246)))
            using (GraphicsPath trackPath = UiTheme.RoundedRectangle(track, 5)) e.Graphics.FillPath(trackBrush, trackPath);
            Color thumbColor = dragging ? UiTheme.Primary : (hovering ? UiTheme.PrimaryHover : Color.FromArgb(92, 119, 130));
            using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
            using (GraphicsPath thumbPath = UiTheme.RoundedRectangle(thumb, 5)) e.Graphics.FillPath(thumbBrush, thumbPath);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Rectangle thumb = ThumbRectangle();
            if (thumb.Contains(e.Location))
            {
                dragging = true;
                dragOffset = e.Y - thumb.Top;
                Capture = true;
            }
            else
            {
                SetValue(value + (e.Y < thumb.Top ? -viewport : viewport));
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = ThumbRectangle().Contains(e.Location);
            if (over != hovering) { hovering = over; Invalidate(); }
            if (!dragging || maximum <= 0) return;
            Rectangle track = TrackRectangle();
            Rectangle thumb = ThumbRectangle();
            int available = Math.Max(1, track.Height - thumb.Height);
            int top = Math.Max(track.Top, Math.Min(track.Bottom - thumb.Height, e.Y - dragOffset));
            SetValue((int)Math.Round((top - track.Top) * maximum / (double)available));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!dragging) { hovering = false; Invalidate(); }
        }

        private void SetValue(int newValue)
        {
            int bounded = Math.Max(0, Math.Min(maximum, newValue));
            if (bounded == value) return;
            value = bounded;
            Invalidate();
            EventHandler handler = ValueChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Rectangle TrackRectangle()
        {
            return new Rectangle(4, 5, Math.Max(12, Width - 8), Math.Max(10, Height - 10));
        }

        private Rectangle ThumbRectangle()
        {
            Rectangle track = TrackRectangle();
            int length = Math.Max(MinimumThumbLength, (int)Math.Round(track.Height * viewport / (double)Math.Max(1, extent)));
            length = Math.Min(track.Height, length);
            int available = Math.Max(0, track.Height - length);
            int top = track.Top + (maximum <= 0 ? 0 : (int)Math.Round(available * value / (double)maximum));
            return new Rectangle(track.X, top, track.Width, length);
        }
    }

    internal sealed class ModernGridHost : Panel
    {
        private readonly DataGridView grid;
        private readonly ModernScrollBar scrollBar = new ModernScrollBar();
        private bool syncing;

        public ModernScrollBar ModernScrollBar { get { return scrollBar; } }

        public ModernGridHost(DataGridView grid)
        {
            Name = "ModernGridHost";
            BackColor = UiTheme.Surface;
            this.grid = grid;
            grid.Dock = DockStyle.None;
            grid.ScrollBars = ScrollBars.None;
            scrollBar.Dock = DockStyle.None;
            Controls.Add(grid);
            Controls.Add(scrollBar);
            scrollBar.BringToFront();
            scrollBar.ValueChanged += delegate { ScrollGridToBar(); };
            grid.Scroll += delegate { UpdateMetrics(); };
            grid.RowsAdded += delegate { ScheduleUpdate(); };
            grid.RowsRemoved += delegate { ScheduleUpdate(); };
            grid.DataBindingComplete += delegate { ScheduleUpdate(); };
            grid.SizeChanged += delegate { ScheduleUpdate(); };
            grid.MouseEnter += delegate { if (grid.CanFocus) grid.Focus(); };
            scrollBar.MouseEnter += delegate { if (grid.CanFocus) grid.Focus(); };
            grid.MouseWheel += HandleMouseWheel;
            scrollBar.MouseWheel += HandleMouseWheel;
            VisibleChanged += delegate { ScheduleUpdate(); };
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleMouseWheel(this, e);
        }

        internal bool ScrollByWheel(int delta)
        {
            if (delta == 0 || grid.Rows.Count == 0) return false;
            int extent = grid.AllowUserToAddRows ? Math.Max(0, grid.Rows.Count - 1) : grid.Rows.Count;
            int viewport = Math.Max(1, grid.DisplayedRowCount(false));
            int maximum = Math.Max(0, extent - viewport);
            int current = grid.FirstDisplayedScrollingRowIndex < 0 ? 0 : grid.FirstDisplayedScrollingRowIndex;
            int wheelLines = SystemInformation.MouseWheelScrollLines;
            int notches = Math.Max(1, Math.Abs(delta) / Math.Max(1, SystemInformation.MouseWheelScrollDelta));
            int distance = wheelLines < 0 ? viewport : Math.Max(1, wheelLines) * notches;
            int target = Math.Max(0, Math.Min(maximum, current + (delta > 0 ? -distance : distance)));
            target = FindVisibleRow(target, delta > 0 ? -1 : 1, extent);
            if (target < 0 || target == current) return false;
            try
            {
                grid.FirstDisplayedScrollingRowIndex = target;
                UpdateMetrics();
                return true;
            }
            catch { return false; }
        }

        private void HandleMouseWheel(object sender, MouseEventArgs e)
        {
            bool moved = ScrollByWheel(e.Delta);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (moved && handled != null) handled.Handled = true;
        }

        private int FindVisibleRow(int start, int direction, int extent)
        {
            if (extent <= 0) return -1;
            int index = Math.Max(0, Math.Min(extent - 1, start));
            while (index >= 0 && index < extent)
            {
                if (grid.Rows[index].Visible) return index;
                index += direction;
            }
            return -1;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int barWidth = scrollBar.Visible ? scrollBar.Width : 0;
            scrollBar.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - barWidth), 0, barWidth, ClientSize.Height);
            grid.Bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width - barWidth), ClientSize.Height);
            ScheduleUpdate();
        }

        private void ScheduleUpdate()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((MethodInvoker)UpdateMetrics); } catch { }
        }

        private void UpdateMetrics()
        {
            if (syncing || grid.IsDisposed) return;
            syncing = true;
            try
            {
                int extent = grid.AllowUserToAddRows ? Math.Max(0, grid.Rows.Count - 1) : grid.Rows.Count;
                int viewport = Math.Max(1, grid.DisplayedRowCount(false));
                int maximum = Math.Max(0, extent - viewport);
                int first = grid.FirstDisplayedScrollingRowIndex < 0 ? 0 : grid.FirstDisplayedScrollingRowIndex;
                scrollBar.SetMetrics(Math.Min(first, maximum), maximum, viewport, Math.Max(1, extent));
                bool show = maximum > 0;
                if (scrollBar.Visible != show) { scrollBar.Visible = show; PerformLayout(); }
            }
            catch { scrollBar.Visible = false; }
            finally { syncing = false; }
        }

        private void ScrollGridToBar()
        {
            if (syncing || grid.Rows.Count == 0) return;
            syncing = true;
            try { grid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(grid.Rows.Count - 1, scrollBar.Value)); }
            catch { }
            finally { syncing = false; }
        }
    }

    internal sealed class ModernScrollPanel : Panel
    {
        private readonly ModernScrollBar scrollBar = new ModernScrollBar();
        private Control content;
        private bool syncing;

        public ModernScrollPanel()
        {
            BackColor = UiTheme.Surface;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
            scrollBar.Dock = DockStyle.Right;
            Controls.Add(scrollBar);
            scrollBar.ValueChanged += delegate { ApplyScroll(); };
            scrollBar.MouseEnter += delegate { Focus(); };
            scrollBar.MouseWheel += HandleMouseWheel;
            MouseEnter += delegate { Focus(); };
            SizeChanged += delegate { UpdateMetrics(); };
        }

        public void SetContent(Control value)
        {
            if (content != null) Controls.Remove(content);
            content = value;
            if (content == null) return;
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(content);
            content.BringToFront();
            scrollBar.BringToFront();
            content.SizeChanged += delegate { UpdateMetrics(); };
            WireWheelFocus(content);
            UpdateMetrics();
        }

        public int ContentWidth
        {
            get { return Math.Max(1, ClientSize.Width - (scrollBar.Visible ? scrollBar.Width : 0) - 4); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleMouseWheel(this, e);
        }

        internal bool ScrollByWheel(int wheelDelta)
        {
            if (!scrollBar.Visible || wheelDelta == 0) return false;
            int maximum = Math.Max(0, (content == null ? 0 : content.Height) - ClientSize.Height);
            int notches = Math.Max(1, Math.Abs(wheelDelta) / Math.Max(1, SystemInformation.MouseWheelScrollDelta));
            int distance = Math.Max(48, SystemInformation.MouseWheelScrollLines * 16) * notches;
            int next = Math.Max(0, Math.Min(maximum, scrollBar.Value + (wheelDelta > 0 ? -distance : distance)));
            if (next == scrollBar.Value) return false;
            scrollBar.SetMetrics(next, maximum, ClientSize.Height, content == null ? ClientSize.Height : content.Height);
            ApplyScroll();
            return true;
        }

        private void HandleMouseWheel(object sender, MouseEventArgs e)
        {
            bool moved = ScrollByWheel(e.Delta);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (moved && handled != null) handled.Handled = true;
        }

        private void WireWheelFocus(Control root)
        {
            if (root == null) return;
            root.MouseEnter += delegate
            {
                if (!(root is TextBoxBase) && !(root is ComboBox) && !(root is NumericUpDown)) Focus();
            };
            root.ControlAdded += delegate(object sender, ControlEventArgs e) { WireWheelFocus(e.Control); };
            foreach (Control child in root.Controls) WireWheelFocus(child);
        }

        private void UpdateMetrics()
        {
            if (syncing || content == null) return;
            syncing = true;
            try
            {
                int maximum = Math.Max(0, content.Height - ClientSize.Height);
                int current = Math.Max(0, Math.Min(maximum, -content.Top));
                scrollBar.Visible = maximum > 0;
                content.Width = ContentWidth;
                scrollBar.SetMetrics(current, maximum, ClientSize.Height, Math.Max(ClientSize.Height, content.Height));
                content.Left = 0;
                content.Top = -current;
            }
            finally { syncing = false; }
        }

        private void ApplyScroll()
        {
            if (syncing || content == null) return;
            content.Top = -scrollBar.Value;
        }
    }

    internal sealed class ModernListHost : Panel
    {
        private readonly ListBox list;
        private readonly ModernScrollBar scrollBar = new ModernScrollBar();
        private bool syncing;

        public ModernListHost(ListBox list)
        {
            this.list = list;
            BackColor = UiTheme.Surface;
            list.Dock = DockStyle.None;
            scrollBar.Dock = DockStyle.None;
            Controls.Add(list);
            Controls.Add(scrollBar);
            scrollBar.BringToFront();
            scrollBar.ValueChanged += delegate
            {
                if (syncing || list.Items.Count == 0) return;
                syncing = true;
                try { list.TopIndex = Math.Max(0, Math.Min(list.Items.Count - 1, scrollBar.Value)); }
                finally { syncing = false; }
            };
            list.MouseEnter += delegate { if (list.CanFocus) list.Focus(); };
            scrollBar.MouseEnter += delegate { if (list.CanFocus) list.Focus(); };
            list.MouseWheel += HandleMouseWheel;
            scrollBar.MouseWheel += HandleMouseWheel;
            list.SelectedIndexChanged += delegate { ScheduleRefresh(); };
            SizeChanged += delegate { RefreshMetrics(); };
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleMouseWheel(this, e);
        }

        internal bool ScrollByWheel(int delta)
        {
            if (delta == 0 || list.Items.Count == 0) return false;
            int viewport = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));
            int maximum = Math.Max(0, list.Items.Count - viewport);
            int lines = SystemInformation.MouseWheelScrollLines < 0 ? viewport : Math.Max(1, SystemInformation.MouseWheelScrollLines);
            int notches = Math.Max(1, Math.Abs(delta) / Math.Max(1, SystemInformation.MouseWheelScrollDelta));
            int next = Math.Max(0, Math.Min(maximum, list.TopIndex + (delta > 0 ? -lines * notches : lines * notches)));
            if (next == list.TopIndex) return false;
            list.TopIndex = next;
            RefreshMetrics();
            return true;
        }

        private void HandleMouseWheel(object sender, MouseEventArgs e)
        {
            bool moved = ScrollByWheel(e.Delta);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (moved && handled != null) handled.Handled = true;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int barWidth = scrollBar.Visible ? scrollBar.Width : 0;
            int nativeOverlap = scrollBar.Visible ? Math.Max(0, SystemInformation.VerticalScrollBarWidth - barWidth) : 0;
            list.Bounds = new Rectangle(0, 0, Math.Max(0, ClientSize.Width + nativeOverlap), ClientSize.Height);
            scrollBar.Bounds = new Rectangle(Math.Max(0, ClientSize.Width - barWidth), 0, barWidth, ClientSize.Height);
            scrollBar.BringToFront();
        }

        public void RefreshMetrics()
        {
            if (syncing || list.IsDisposed) return;
            syncing = true;
            try
            {
                int extent = list.Items.Count;
                int viewport = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight));
                int maximum = Math.Max(0, extent - viewport);
                int current = Math.Max(0, Math.Min(maximum, list.TopIndex));
                bool show = maximum > 0;
                if (scrollBar.Visible != show) { scrollBar.Visible = show; PerformLayout(); }
                scrollBar.SetMetrics(current, maximum, viewport, Math.Max(1, extent));
            }
            finally { syncing = false; }
        }

        private void ScheduleRefresh()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke((MethodInvoker)RefreshMetrics); } catch { }
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
                SplitContainer mainSplit = FindControl<SplitContainer>(main);
                if (mainSplit == null || mainSplit.Panel2.Width < 220 || mainSplit.Panel2.Width > 260) failures.Add("main：默认详情栏宽度应保持在 220 到 260 像素");
                ValidateModernScrollBar(main, failures, "main", true);
                Capture(main, Path.Combine(store.Reports, "ui-main-default-" + store.Timestamp() + ".png"), failures);
                main.Size = main.MinimumSize;
                Application.DoEvents();
                ValidateMainWindow(main, failures, "minimum");
                Capture(main, Path.Combine(store.Reports, "ui-main-minimum-" + store.Timestamp() + ".png"), failures);
                main.Close();
            }
            ValidateAuthorDestinations(store, failures);
            ValidateWheelRouting(failures);
            foreach (float scale in new float[] { 1.25F, 1.5F, 2F }) ValidateScaledWindow(store, scale, failures);
            ValidateLiveScan(store, failures);
            ValidateResultRefreshPresentation(store, failures);

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
                ValidateVisibleButton(recovery, failures, "清理旧记录", "recovery");
                ValidateVisibleButton(recovery, failures, "删除当前批次", "recovery");
                ValidateVisibleButton(recovery, failures, "恢复当前批次", "recovery");
                ValidateVisibleButton(recovery, failures, "关闭", "recovery");
                ValidateActionButton(recovery, failures, "清理旧记录", ActionButtonRole.Warning, "recovery");
                ValidateActionButton(recovery, failures, "删除当前批次", ActionButtonRole.Danger, "recovery");
                ValidateActionButton(recovery, failures, "恢复当前批次", ActionButtonRole.Primary, "recovery");
                ValidateActionButton(recovery, failures, "关闭", ActionButtonRole.Close, "recovery");
                Capture(recovery, Path.Combine(store.Reports, "ui-recovery-" + store.Timestamp() + ".png"), failures);
                recovery.Close();
            }
            using (ContextMenuManagerForm contextMenu = new ContextMenuManagerForm(store))
            {
                contextMenu.Show();
                Application.DoEvents();
                if (contextMenu.Cursor != Cursors.Default || contextMenu.UseWaitCursor) failures.Add("context menu：枚举期间出现等待光标");
                Button refresh = FindButton(contextMenu, "刷新列表");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                while (!contextMenu.FocusedPresentationComplete && watch.ElapsedMilliseconds < 15000)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                ValidateVisibleButton(contextMenu, failures, "刷新列表", "context menu");
                ValidateVisibleButton(contextMenu, failures, "显示选中", "context menu");
                ValidateVisibleButton(contextMenu, failures, "隐藏选中", "context menu");
                ValidateVisibleButton(contextMenu, failures, "修改名称", "context menu");
                ValidateVisibleButton(contextMenu, failures, "添加菜单", "context menu");
                ValidateVisibleButton(contextMenu, failures, "删除菜单", "context menu");
                ValidateVisibleButton(contextMenu, failures, "更多位置", "context menu");
                ValidateVisibleButton(contextMenu, failures, "系统高级", "context menu");
                ValidateActionButton(contextMenu, failures, "刷新列表", ActionButtonRole.Primary, "context menu");
                if (refresh != null && !(refresh.Parent is FlowLayoutPanel)) failures.Add("context menu：刷新列表没有放在独立工具栏中");
                foreach (ContextMenuEntry entry in contextMenu.FocusedEntries)
                {
                    if (!ChineseDisplayText.HasChinese(entry.Name)) failures.Add("context menu：右键名称仍为纯英文 " + entry.Name);
                    if (!ChineseDisplayText.HasChinese(entry.SoftwareName)) failures.Add("context menu：关联软件缺少中文用途说明 " + entry.SoftwareName);
                }
                if (refresh != null && !refresh.Enabled) failures.Add("context menu：15 秒内未完成枚举");
                SplitContainer contextSplit = FindControl<SplitContainer>(contextMenu);
                if (contextSplit == null || contextSplit.Panel2.Width < 210 || contextSplit.Panel2.Width > 250) failures.Add("context menu：右侧详情面板宽度应保持在 210 到 250 像素");
                DataGridView contextGrid = FindControl<DataGridView>(contextMenu);
                ValidateModernScrollBar(contextMenu, failures, "context menu", contextGrid != null && contextGrid.Rows.Count > 8);
                Capture(contextMenu, Path.Combine(store.Reports, "ui-context-menu-" + store.Timestamp() + ".png"), failures);
                contextMenu.Close();
            }
            using (SpecialContextMenuForm special = new SpecialContextMenuForm(store))
            {
                special.Show(); Application.DoEvents();
                if (special.Cursor != Cursors.Default || special.UseWaitCursor) failures.Add("special menu：枚举期间出现等待光标");
                Button refresh = FindButton(special, "刷新列表");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                ValidateVisibleButton(special, failures, "刷新列表", "special menu");
                ValidateVisibleButton(special, failures, "显示选中", "special menu");
                ValidateVisibleButton(special, failures, "隐藏选中", "special menu");
                ValidateVisibleButton(special, failures, "添加项目", "special menu");
                ValidateVisibleButton(special, failures, "删除项目", "special menu");
                if (refresh != null && !refresh.Enabled) failures.Add("special menu：15 秒内未完成枚举");
                Capture(special, Path.Combine(store.Reports, "ui-special-menu-" + store.Timestamp() + ".png"), failures);
                special.Close();
            }
            using (AdvancedContextMenuForm advanced = new AdvancedContextMenuForm(store))
            {
                advanced.Show(); Application.DoEvents();
                if (advanced.Cursor != Cursors.Default || advanced.UseWaitCursor) failures.Add("advanced menu：枚举期间出现等待光标");
                Button refresh = FindButton(advanced, "刷新列表");
                Stopwatch watch = Stopwatch.StartNew();
                while (refresh != null && !refresh.Enabled && watch.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                ValidateVisibleButton(advanced, failures, "刷新列表", "advanced menu");
                ValidateVisibleButton(advanced, failures, "显示或安装", "advanced menu");
                ValidateVisibleButton(advanced, failures, "隐藏或移除", "advanced menu");
                ValidateVisibleButton(advanced, failures, "添加旧式菜单", "advanced menu");
                ValidateVisibleButton(advanced, failures, "向上移动", "advanced menu");
                ValidateVisibleButton(advanced, failures, "向下移动", "advanced menu");
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
            ValidateRecoveryMaintenance(failures);
            return failures;
        }

        private static void ValidateRecoveryMaintenance(List<string> failures)
        {
            string lab = Path.Combine(Path.GetTempPath(), "RogueCleanerRecoveryRegression-" + Guid.NewGuid().ToString("N"));
            try
            {
                DataStore isolated = DataStore.CreateForExecutable(Path.Combine(lab, "验证程序.exe"));
                isolated.Ensure();
                CleanerEngine cleaner = new CleanerEngine(isolated);
                DateTime now = new DateTime(2026, 8, 6, 12, 0, 0);
                List<CleanupBatch> old = new List<CleanupBatch>();
                for (int i = 0; i < 25; i++) old.Add(new CleanupBatch { Id = "旧批次-" + i, CreatedAt = now.AddDays(-40 - i).ToString("yyyy-MM-dd HH:mm:ss"), Path = Path.Combine(isolated.Backups, "旧批次-" + i), Results = new List<CleanupResult>() });
                if (cleaner.FindOldBatchRecords(old, now, 20, 30).Count != 5) failures.Add("恢复中心：旧记录保留数量不符合“最新 20 个”规则");
                List<CleanupBatch> recent = new List<CleanupBatch>();
                for (int i = 0; i < 25; i++) recent.Add(new CleanupBatch { Id = "近期批次-" + i, CreatedAt = now.AddHours(-i).ToString("yyyy-MM-dd HH:mm:ss"), Path = Path.Combine(isolated.Backups, "近期批次-" + i), Results = new List<CleanupResult>() });
                if (cleaner.FindOldBatchRecords(recent, now, 20, 30).Count != 0) failures.Add("恢复中心：最近 30 天记录被错误列入清理范围");

                CleanupBatch target = new CleanupBatch { Id = "20260806-120001", CreatedAt = "2026-08-06 12:00:01", Path = Path.Combine(isolated.Backups, "20260806-120001"), Results = new List<CleanupResult>() };
                string neighbor = Path.Combine(isolated.Backups, "20260806-120002");
                Directory.CreateDirectory(target.Path); Directory.CreateDirectory(neighbor);
                File.WriteAllText(Path.Combine(target.Path, "manifest.json"), "{}", System.Text.Encoding.UTF8);
                string cleanupReport = Path.Combine(isolated.Reports, "cleanup-" + target.Id + ".json");
                string menuReport = Path.Combine(isolated.Reports, "context-menu-" + target.Id + ".json");
                string neighborReport = Path.Combine(isolated.Reports, "cleanup-20260806-120002.json");
                File.WriteAllText(cleanupReport, "[]"); File.WriteAllText(menuReport, "[]"); File.WriteAllText(neighborReport, "[]");
                if (cleaner.GetBatchStorageBytes(target) <= 0) failures.Add("恢复中心：未能统计批次占用空间");
                cleaner.DeleteBatchRecord(target);
                if (Directory.Exists(target.Path) || File.Exists(cleanupReport) || File.Exists(menuReport)) failures.Add("恢复中心：批次或关联报告删除后仍然存在");
                if (!Directory.Exists(neighbor) || !File.Exists(neighborReport)) failures.Add("恢复中心：删除当前批次时误删相邻记录");

                bool rejected = false;
                try { cleaner.DeleteBatchRecord(new CleanupBatch { Id = "越界测试", Path = Path.Combine(lab, "越界目录") }); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) failures.Add("恢复中心：未拒绝备份目录外的删除路径");
            }
            catch (Exception ex) { failures.Add("恢复中心清理回归异常：" + ex.GetType().Name + "：" + ex.Message); }
            finally { try { if (Directory.Exists(lab)) Directory.Delete(lab, true); } catch { } }
        }

        private static void ValidateLiveScan(DataStore store, List<string> failures)
        {
            using (MainForm form = new MainForm(store, false))
            {
                form.Show();
                Application.DoEvents();
                MethodInfo startScan = typeof(MainForm).GetMethod("StartScan", BindingFlags.Instance | BindingFlags.NonPublic);
                Button scan = FindButton(form, "开始扫描");
                if (startScan == null || scan == null)
                {
                    failures.Add("live scan：无法启动真实界面扫描");
                    return;
                }
                Stopwatch watch = Stopwatch.StartNew();
                startScan.Invoke(form, new object[] { null });
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

        private static void ValidateResultRefreshPresentation(DataStore store, List<string> failures)
        {
            using (MainForm form = new MainForm(store, false))
            {
                form.Show();
                Application.DoEvents();
                MethodInfo replaceRows = typeof(MainForm).GetMethod("ReplaceRows", BindingFlags.Instance | BindingFlags.NonPublic);
                if (replaceRows == null)
                {
                    failures.Add("result refresh：无法访问统一结果刷新入口");
                    return;
                }
                Finding finding = new Finding
                {
                    Vendor = "未知第三方",
                    UserVisibleName = "清理后图标回归",
                    Category = "开机启动",
                    ActionKind = "ReportOnly",
                    Target = new ActionTarget { FilePath = Application.ExecutablePath }
                };
                replaceRows.Invoke(form, new object[] { new Finding[] { finding } });
                Stopwatch watch = Stopwatch.StartNew();
                while (finding.SoftwareName == "正在识别…" && watch.ElapsedMilliseconds < 5000)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
                if (finding.SoftwareIcon == null) failures.Add("result refresh：统一刷新后没有软件图标");
                if (finding.SoftwareName == "正在识别…") failures.Add("result refresh：统一刷新后身份解析未完成");
                if (string.IsNullOrEmpty(finding.IconSource) || !File.Exists(finding.IconSource)) failures.Add("result refresh：统一刷新后没有可验证图标来源");
                if (form.Cursor != Cursors.Default || form.UseWaitCursor) failures.Add("result refresh：后台图标刷新出现等待光标");
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
            Button scan = FindButton(form, "开始扫描");
            Button clean = FindButton(form, "清理勾选");
            Button update = FindButton(form, "检查更新");
            Button feedback = FindButton(form, "反馈");
            string permissionText = AdminUtil.IsAdministrator() ? "管理员模式" : "请求管理员权限";
            Button permission = FindButton(form, permissionText);
            ValidateButton(form, scan, failures, "开始扫描", scope);
            ValidateButton(form, clean, failures, "清理勾选", scope);
            ValidateButton(form, update, failures, "检查更新", scope);
            ValidateButton(form, feedback, failures, "反馈", scope);
            ValidateButton(form, permission, failures, permissionText, scope);
            if (permission != null && permission.Enabled == AdminUtil.IsAdministrator()) failures.Add(scope + "：权限按钮启用状态与当前权限不一致");
            foreach (string actionText in new string[] { "开始扫描", "清理勾选", "勾选可清理", "只勾低风险", "恢复中心", "证据报告" })
            {
                Button action = FindButton(form, actionText);
                if (action == null) failures.Add(scope + "：缺少顶部操作按钮“" + actionText + "”");
                else if (action.Width < 128 || action.Height < 40) failures.Add(scope + "：顶部操作按钮“" + actionText + "”尺寸不足，可能截断文字");
            }
            if (update != null && feedback != null)
            {
                Rectangle updateBounds = RelativeBounds(form, update);
                Rectangle feedbackBounds = RelativeBounds(form, feedback);
                if (feedbackBounds.Left <= updateBounds.Left) failures.Add(scope + "：反馈没有位于检查更新之后");
                if (feedbackBounds.IntersectsWith(updateBounds)) failures.Add(scope + "：检查更新与反馈发生重叠");
            }
            if (scan != null && clean != null && RelativeBounds(form, scan).IntersectsWith(RelativeBounds(form, clean))) failures.Add(scope + "：开始扫描与清理勾选发生重叠");
            ValidateAuthorLayout(form, failures, scope);
            ValidateCompactResultGrid(form, failures, scope);
            if (scope == "default") ValidateBusyCursor(form, failures);
        }

        private static void ValidateCompactResultGrid(Form form, List<string> failures, string scope)
        {
            DataGridView resultGrid = FindControl<DataGridView>(form);
            if (resultGrid == null)
            {
                failures.Add(scope + "：缺少扫描结果表");
                return;
            }
            string[] headers = new string[] { "风险", "项目", "软件", "位置", "影响", "处理" };
            foreach (string header in headers)
            {
                DataGridViewColumn column = resultGrid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(delegate(DataGridViewColumn item) { return item.HeaderText == header; });
                if (column == null)
                {
                    failures.Add(scope + "：缺少紧凑表头 " + header);
                    continue;
                }
                int required = TextRenderer.MeasureText(header, resultGrid.ColumnHeadersDefaultCellStyle.Font ?? resultGrid.Font).Width + 18;
                if (column.Width < required) failures.Add(scope + "：表头显示不全 " + header + " width=" + column.Width + " required=" + required);
                if (column.HeaderCell.Style.Alignment != DataGridViewContentAlignment.MiddleCenter) failures.Add(scope + "：表头未固定居中 " + header);
                if (column.DefaultCellStyle.Alignment != DataGridViewContentAlignment.MiddleCenter) failures.Add(scope + "：内容未固定居中 " + header);
            }
            if (resultGrid.ColumnHeadersDefaultCellStyle.WrapMode != DataGridViewTriState.False) failures.Add(scope + "：结果表头仍允许换行");

            Finding sample = new Finding
            {
                UserVisibleName = "普通文件右键：疑似会出现“360 安全/扫描右键菜单”",
                Category = "右键菜单",
                UserImpact = "普通文件右键：疑似会出现很长的说明。后面还有完整证据。",
                ActionKind = "DeleteRegistryKey"
            };
            if (sample.CompactTitle != "360 安全/扫描右键菜单") failures.Add(scope + "：项目名称未移除重复场景前缀");
            if (sample.CompactLocation != "文件右键") failures.Add(scope + "：位置摘要不正确 " + sample.CompactLocation);
            if (sample.CompactImpact != "右键入口") failures.Add(scope + "：影响摘要不正确 " + sample.CompactImpact);
            if (sample.CompactAction != "备份删除") failures.Add(scope + "：处理摘要不正确 " + sample.CompactAction);
            int visibleColumns = resultGrid.Columns.Cast<DataGridViewColumn>().Count(delegate(DataGridViewColumn item) { return item.Visible; });
            if (form.Visible && resultGrid.DisplayedColumnCount(true) < visibleColumns) failures.Add(scope + "：结果表仍需横向滚动才能看到全部列");
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

        private static void ValidateActionButton(Form form, List<string> failures, string text, ActionButtonRole role, string scope)
        {
            Button button = FindButton(form, text);
            if (button == null) return;
            string expected = "ActionButton:" + role;
            if (!string.Equals(Convert.ToString(button.Tag), expected, StringComparison.Ordinal)) failures.Add(scope + "：按钮没有采用统一语义样式 " + text);
            if (button.Height != 36) failures.Add(scope + "：按钮高度不符合统一规范 " + text + " height=" + button.Height);
        }

        private static void ValidateModernScrollBar(Form form, List<string> failures, string scope, bool expectVisible)
        {
            ModernGridHost host = FindControl<ModernGridHost>(form);
            if (host == null)
            {
                failures.Add(scope + "：没有挂载现代滚动条");
                return;
            }
            ModernScrollBar bar = host.ModernScrollBar;
            if (bar.Width < 18) failures.Add(scope + "：滚动条宽度不足");
            if (bar.MinimumThumbLength < 42) failures.Add(scope + "：滚动滑块最小高度不足");
            if (expectVisible && !bar.Visible) failures.Add(scope + "：长列表滚动条不可见");
            DataGridView grid = FindControl<DataGridView>(host);
            if (expectVisible && grid != null && grid.Rows.Count > 8)
            {
                MethodInfo setValue = typeof(ModernScrollBar).GetMethod("SetValue", BindingFlags.Instance | BindingFlags.NonPublic);
                if (setValue == null) failures.Add(scope + "：无法验证滚动条拖动行为");
                else
                {
                    int before = grid.FirstDisplayedScrollingRowIndex;
                    setValue.Invoke(bar, new object[] { 5 });
                    Application.DoEvents();
                    if (grid.FirstDisplayedScrollingRowIndex <= before) failures.Add(scope + "：拖动滚动条没有带动列表（before=" + before + "，after=" + grid.FirstDisplayedScrollingRowIndex + "，bar=" + bar.Value + "）");
                    setValue.Invoke(bar, new object[] { 0 });
                    Application.DoEvents();
                    int wheelBefore = grid.FirstDisplayedScrollingRowIndex;
                    if (!host.ScrollByWheel(-SystemInformation.MouseWheelScrollDelta) || grid.FirstDisplayedScrollingRowIndex <= wheelBefore)
                        failures.Add(scope + "：鼠标滚轮没有带动列表");
                }
            }
        }

        private static void ValidateWheelRouting(List<string> failures)
        {
            using (Form form = new Form { Size = new Size(720, 520), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
            {
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
                form.Controls.Add(layout);

                DataGridView grid = new DataGridView { AllowUserToAddRows = false, RowHeadersVisible = false };
                grid.Columns.Add("内容", "内容");
                for (int i = 0; i < 30; i++) grid.Rows.Add("列表项目 " + i);
                Panel gridContainer = new Panel { Dock = DockStyle.Fill };
                ModernGridHost gridHost = UiTheme.AttachModernScrollBar(gridContainer, grid);
                layout.Controls.Add(gridContainer, 0, 0);

                ModernScrollPanel detail = new ModernScrollPanel { Dock = DockStyle.Fill };
                Panel detailContent = new Panel { Height = 700 };
                detailContent.Controls.Add(new Label { Text = "详情内容", AutoSize = true, Location = new Point(10, 650) });
                detail.SetContent(detailContent);
                layout.Controls.Add(detail, 0, 1);

                ListBox list = new ListBox();
                for (int i = 0; i < 30; i++) list.Items.Add("恢复批次 " + i);
                ModernListHost listHost = new ModernListHost(list) { Dock = DockStyle.Fill };
                layout.Controls.Add(listHost, 0, 2);

                form.Show();
                Application.DoEvents();
                if (!gridHost.ScrollByWheel(-SystemInformation.MouseWheelScrollDelta) || grid.FirstDisplayedScrollingRowIndex <= 0)
                    failures.Add("滚轮规范：扫描结果列表无法用鼠标滚轮向下滚动");
                if (!detail.ScrollByWheel(-SystemInformation.MouseWheelScrollDelta) || detailContent.Top >= 0)
                    failures.Add("滚轮规范：详情面板无法用鼠标滚轮向下滚动");
                if (!listHost.ScrollByWheel(-SystemInformation.MouseWheelScrollDelta) || list.TopIndex <= 0)
                    failures.Add("滚轮规范：恢复中心批次列表无法用鼠标滚轮向下滚动");
                form.Close();
            }
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
            foreach (DataGridView grid in Descendants(form).OfType<DataGridView>())
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (column.HeaderCell.Style.Alignment != DataGridViewContentAlignment.MiddleCenter || column.DefaultCellStyle.Alignment != DataGridViewContentAlignment.MiddleCenter)
                    {
                        failures.Add(form.Text + "：表格列“" + column.HeaderText + "”未保持标题和内容居中");
                        break;
                    }
                }
            }
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
