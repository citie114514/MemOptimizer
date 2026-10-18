using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemOpt
{
    internal sealed class MainForm : Form
    {
        private const int LogicalWidth = 600;
        private const int LogicalHeight = 684;

        private static readonly Color Bg = Color.FromArgb(238, 242, 249);
        private static readonly Color Ink = Color.FromArgb(15, 23, 42);
        private static readonly Color Muted = Color.FromArgb(100, 116, 139);
        private static readonly Color Faint = Color.FromArgb(148, 163, 184);
        private static readonly Color Accent = Color.FromArgb(37, 99, 235);
        private static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        private static readonly Color BarBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color Amber = Color.FromArgb(245, 158, 11);
        private static readonly Color OkColor = Color.FromArgb(5, 150, 105);
        private static readonly Color BadColor = Color.FromArgb(220, 38, 38);
        private static readonly Color Line = Color.FromArgb(226, 232, 240);

        private static readonly Rectangle HeaderRect = new Rectangle(0, 0, 600, 104);
        private static readonly Rectangle StatsRect = new Rectangle(16, 116, 568, 150);
        private static readonly Rectangle OptionsRect = new Rectangle(16, 278, 568, 200);
        private static readonly Rectangle ResultRect = new Rectangle(16, 490, 568, 104);
        private static readonly Rectangle ButtonRect = new Rectangle(16, 606, 568, 48);

        private readonly CliOptions _options;
        private readonly bool _isAdmin;
        private readonly Timer _timer;
        private readonly bool[] _enabled = new bool[Optimizer.Actions.Length];
        private readonly List<StepResult> _liveSteps = new List<StepResult>();

        private float _scale = 1f;
        private MemSnapshot _snapshot;
        private MemReport _report;
        private string _runError;
        private bool _running;
        private bool _buttonHover;
        private bool _buttonDown;
        private int _hoverRow = -1;

        private readonly Font _fTitle;
        private readonly Font _fSub;
        private readonly Font _fBig;
        private readonly Font _fBigUnit;
        private readonly Font _fLabel;
        private readonly Font _fSection;
        private readonly Font _fBody;
        private readonly Font _fDesc;
        private readonly Font _fSmall;
        private readonly Font _fChip;
        private readonly Font _fBtn;
        private readonly Font _fTiny;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        public MainForm(CliOptions options)
        {
            _options = options ?? new CliOptions();
            _isAdmin = Elevation.IsAdmin;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "内存优化 · MemOptimizer";
            BackColor = Bg;
            DoubleBuffered = true;

            _scale = ComputeScale();
            ClientSize = new Size((int)Math.Round(LogicalWidth * _scale), (int)Math.Round(LogicalHeight * _scale));

            _fTitle = new Font("Microsoft YaHei UI", 28f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fSub = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBig = new Font("Segoe UI Semibold", 34f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBigUnit = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fLabel = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSection = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fBody = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fDesc = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fChip = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
            _fBtn = new Font("Microsoft YaHei UI", 15.5f, FontStyle.Bold, GraphicsUnit.Pixel);
            _fTiny = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Pixel);

            for (var i = 0; i < _enabled.Length; i++)
                _enabled[i] = (_options.Scope & Optimizer.Actions[i].Bit) != 0;

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (s, e) =>
            {
                if (_running) return;
                RefreshSnapshot();
            };
            _timer.Start();
            RefreshSnapshot();
        }

        private static float ComputeScale()
        {
            var dpiScale = 1f;
            try
            {
                var dpi = GetDpiForSystem();
                if (dpi >= 96) dpiScale = dpi / 96f;
            }
            catch { }

            var area = Screen.PrimaryScreen.WorkingArea;
            var max = Math.Min((area.Height - 64f) / LogicalHeight, (area.Width - 48f) / LogicalWidth);
            if (max <= 0) max = 1f;
            return Math.Max(0.85f, Math.Min(dpiScale, max));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_options.Autorun) BeginInvoke((Action)StartOptimization);
        }

        private void RefreshSnapshot()
        {
            try { _snapshot = MemSnapshot.Capture(); }
            catch { }
            Invalidate();
        }

        private int CurrentScope()
        {
            var scope = 0;
            for (var i = 0; i < _enabled.Length; i++)
                if (_enabled[i]) scope |= Optimizer.Actions[i].Bit;
            return scope;
        }

        // ---------------------------------------------------------------- paint

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(_scale, _scale);
            g.Clear(Bg);

            DrawHeader(g);
            DrawStats(g);
            DrawOptions(g);
            DrawResult(g);
            DrawButton(g);
            DrawFooter(g);
        }

        private void DrawHeader(Graphics g)
        {
            using (var brush = new LinearGradientBrush(HeaderRect,
                       Color.FromArgb(30, 58, 138), Color.FromArgb(59, 130, 246), 18f))
            {
                g.FillRectangle(brush, HeaderRect);
            }

            using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), 24f))
            {
                g.DrawEllipse(pen, 474, -92, 220, 220);
                g.DrawEllipse(pen, 560, 22, 120, 120);
            }

            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Color.FromArgb(199, 219, 255)))
            using (var faint = new SolidBrush(Color.FromArgb(160, 190, 240)))
            {
                g.DrawString("内存优化", _fTitle, white, 26, 10);
                g.DrawString("PCL / PCL2-CE 同源 · NT 内核内存整理", _fSub, soft, 27, 48);
                g.DrawString("不结束进程 · 不删数据 · 只回收缓存与工作集", _fSmall, faint, 28, 72);
            }

            var badgeText = _isAdmin ? "管理员" : "需要提权";
            using (var body = new SolidBrush(_isAdmin ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(255, 255, 255)))
            {
                var badgeSize = g.MeasureString(badgeText, _fSmall);
                var badge = new RectangleF(574 - (badgeSize.Width + 22), 16, badgeSize.Width + 22, 22);
                using (var path = Round(badge, 11))
                using (var fill = new SolidBrush(Color.FromArgb(_isAdmin ? 46 : 60, 255, 255, 255)))
                    g.FillPath(fill, path);
                g.DrawString(badgeText, _fSmall, body, badge.X + 11, badge.Y + 4);
            }

            using (var white = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                g.DrawString("v" + Program.Version, _fTiny, white, 556, 50);
        }

        private void DrawStats(Graphics g)
        {
            DrawCard(g, StatsRect);
            if (_snapshot == null) return;

            using (var ink = new SolidBrush(Ink))
            using (var muted = new SolidBrush(Muted))
            using (var faint = new SolidBrush(Faint))
            {
                g.DrawString("物理内存 · 实时", _fLabel, muted, 32, 128);

                var availableGb = Format.Gb(_snapshot.Available);
                var bigSize = g.MeasureString(availableGb, _fBig);
                g.DrawString(availableGb, _fBig, ink, 29, 140);
                g.DrawString("GB 可用", _fBigUnit, muted, 29 + bigSize.Width - 4, 140 + bigSize.Height - 20);

                var totalText = "总计 " + Format.Gb(_snapshot.Total) + " GB";
                var usedText = "使用率 " + _snapshot.LoadPercent + "%";
                g.DrawString(totalText, _fSmall, muted, 568 - g.MeasureString(totalText, _fSmall).Width, 132);
                g.DrawString(usedText, _fSmall, faint, 568 - g.MeasureString(usedText, _fSmall).Width, 150);
            }

            var bar = new RectangleF(32, 198, 536, 10);
            using (var clip = Round(bar, 5f))
            {
                var old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                using (var bg = new SolidBrush(Color.FromArgb(232, 238, 247))) g.FillRectangle(bg, bar);

                var total = Math.Max(1d, _snapshot.Total);
                var usedWidth = (float)(bar.Width * Math.Min(1d, _snapshot.Used / total));
                var standbyWidth = (float)(bar.Width * Math.Min(Math.Max(0d, 1d - usedWidth / bar.Width), _snapshot.Standby / total));

                using (var used = new SolidBrush(BarBlue))
                    g.FillRectangle(used, bar.X, bar.Y, usedWidth, bar.Height);
                using (var standby = new SolidBrush(Amber))
                    g.FillRectangle(standby, bar.X + usedWidth, bar.Y, Math.Max(0, standbyWidth), bar.Height);

                g.Clip = old;
            }

            using (var muted = new SolidBrush(Muted))
            using (var faint = new SolidBrush(Faint))
            {
                DrawLegend(g, 32, 214, BarBlue, "使用中", Format.Bytes(_snapshot.Used), muted);
                DrawLegend(g, 196, 214, Amber, "待命中", Format.Bytes(_snapshot.Standby), muted);
                DrawLegend(g, 368, 214, Color.FromArgb(203, 213, 225), "可用", Format.Bytes(_snapshot.Available), muted);

                var commit = "提交 " + Format.Gb(_snapshot.CommitTotal) + " / " + Format.Gb(_snapshot.CommitLimit) + " GB" +
                             "     页面 " + (_snapshot.PageSize / 1024) + " KB" +
                             "     文件缓存 " + Format.Bytes(_snapshot.SystemCache);
                g.DrawString(commit, _fSmall, faint, 32, 234);
            }
        }

        private void DrawLegend(Graphics g, float x, float y, Color color, string label, string value, Brush textBrush)
        {
            using (var swatch = new SolidBrush(color))
            using (var path = Round(new RectangleF(x, y + 3, 8, 8), 2f))
                g.FillPath(swatch, path);
            g.DrawString(label + " " + value, _fSmall, textBrush, x + 14, y);
        }

        private void DrawOptions(Graphics g)
        {
            DrawCard(g, OptionsRect);

            using (var ink = new SolidBrush(Ink))
            using (var muted = new SolidBrush(Muted))
            using (var faint = new SolidBrush(Faint))
            {
                g.DrawString("整理动作", _fSection, ink, 32, 290);
                var hint = "默认 = PCL 推荐组合";
                g.DrawString(hint, _fSmall, faint, 568 - g.MeasureString(hint, _fSmall).Width, 294);

                for (var i = 0; i < Optimizer.Actions.Length; i++)
                {
                    var action = Optimizer.Actions[i];
                    var row = OptionRow(i);

                    if (_hoverRow == i)
                    {
                        using (var path = Round(new RectangleF(row.X - 4, row.Y, row.Width, row.Height), 7f))
                        using (var fill = new SolidBrush(Color.FromArgb(246, 249, 255)))
                            g.FillPath(fill, path);
                    }

                    var box = new RectangleF(row.X, row.Y + 9, 16, 16);
                    using (var path = Round(box, 4.5f))
                    {
                        if (_enabled[i])
                        {
                            using (var fill = new SolidBrush(Accent)) g.FillPath(fill, path);
                        }
                        else
                        {
                            using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
                            using (var pen = new Pen(Color.FromArgb(203, 213, 225), 1.3f)) g.DrawPath(pen, path);
                        }
                    }
                    if (_enabled[i]) DrawCheck(g, box.X + 3.8f, box.Y + 4.2f, Color.White, 1.8f);

                    using (var title = new SolidBrush(_enabled[i] ? Ink : Muted))
                        g.DrawString(action.Title, _fBody, title, row.X + 26, row.Y - 1);
                    using (var desc = new SolidBrush(Faint))
                        g.DrawString(action.Desc, _fDesc, desc, row.X + 26, row.Y + 16);
                }

                g.DrawString("接口：NtSetSystemInformation · 全部为页级操作，不改动任何文件", _fDesc, faint, 32, 458);
            }
        }

        private void DrawResult(Graphics g)
        {
            DrawCard(g, ResultRect);

            using (var ink = new SolidBrush(Ink))
            using (var muted = new SolidBrush(Muted))
            using (var faint = new SolidBrush(Faint))
            {
                g.DrawString("本次结果", _fSection, ink, 32, 501);

                if (_running)
                {
                    using (var accent = new SolidBrush(Accent))
                        g.DrawString("正在整理…", _fSmall, accent, 568 - 68, 505);
                }
                else if (_report != null)
                {
                    var status = "耗时 " + (_report.DurationMs / 1000.0).ToString("0.0") + " s";
                    g.DrawString(status, _fSmall, faint, 568 - g.MeasureString(status, _fSmall).Width, 505);
                }

                if (!string.IsNullOrEmpty(_runError))
                {
                    using (var bad = new SolidBrush(BadColor))
                    {
                        var error = _runError.Length > 40 ? _runError.Substring(0, 39) + "…" : _runError;
                        g.DrawString("执行失败：" + error, _fBody, bad, 32, 521);
                    }
                }
                else if (_report == null)
                {
                    g.DrawString(_running ? "已提交内核请求，等待返回…" : "尚未运行 · 点击下方按钮开始", _fBody, muted, 32, 521);
                }
                else
                {
                    var delta = _report.DeltaBytes;
                    if (delta > 0)
                    {
                        using (var green = new SolidBrush(OkColor))
                        using (var big = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            var freed = "释放 " + Format.Bytes(delta);
                            g.DrawString(freed, big, green, 30, 516);
                            var width = g.MeasureString(freed, big).Width;
                            using (var rest = new SolidBrush(Muted))
                            {
                                var tail = "可用 " + Format.Gb(_report.Before.Available) + " → " +
                                           Format.Gb(_report.After.Available) + " GB";
                                g.DrawString(tail, _fSmall, rest, 32 + width + 8, 522);
                            }
                        }
                    }
                    else
                    {
                        g.DrawString("本次没有可释放的页 · 可用 " + Format.Bytes(_report.After.Available) + "（系统本身足够空闲）",
                            _fBody, muted, 32, 521);
                    }
                }

                DrawChips(g);
            }
        }

        private void DrawChips(Graphics g)
        {
            var steps = _report != null ? _report.Steps : _liveSteps;
            const float chipWidth = 128f;
            const float chipHeight = 18f;
            for (var i = 0; i < steps.Count && i < 8; i++)
            {
                var step = steps[i];
                var x = 32 + (i % 4) * (chipWidth + 8);
                var y = 546 + (i / 4) * 21;
                var rect = new RectangleF(x, y, chipWidth, chipHeight);

                var bg = step.Ok ? Color.FromArgb(236, 253, 245) : Color.FromArgb(254, 242, 242);
                var fg = step.Ok ? OkColor : BadColor;
                using (var path = Round(rect, 5f))
                using (var fill = new SolidBrush(bg))
                    g.FillPath(fill, path);

                using (var brush = new SolidBrush(fg))
                {
                    if (step.Ok) DrawCheck(g, x + 7, y + 5.5f, fg, 1.5f);
                    else
                    {
                        using (var pen = new Pen(fg, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(pen, x + 7, y + 5.5f, x + 13, y + 11.5f);
                            g.DrawLine(pen, x + 13, y + 5.5f, x + 7, y + 11.5f);
                        }
                    }

                    var title = step.Title;
                    if (g.MeasureString(title, _fChip).Width > chipWidth - 30)
                        title = title.Substring(0, Math.Max(1, title.Length - 2)) + "…";
                    g.DrawString(title, _fChip, brush, x + 19, y + 3);
                }
            }
        }

        private void DrawButton(Graphics g)
        {
            var rect = new RectangleF(ButtonRect.X, ButtonRect.Y, ButtonRect.Width, ButtonRect.Height);
            var enabled = !_running;
            var start = _buttonDown ? Color.FromArgb(30, 64, 175) : _buttonHover && enabled ? Color.FromArgb(43, 108, 240) : Accent;
            var end = _buttonDown ? Color.FromArgb(30, 58, 138) : _buttonHover && enabled ? Color.FromArgb(32, 86, 220) : AccentDark;

            using (var path = Round(rect, 12f))
            {
                if (enabled)
                {
                    using (var brush = new LinearGradientBrush(ButtonRect, start, end, 0f))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184)))
                        g.FillPath(brush, path);
                }
            }

            var text = _running ? "正在整理…" : (_isAdmin ? "开始优化" : "以管理员身份优化");
            using (var white = new SolidBrush(Color.White))
            {
                var size = g.MeasureString(text, _fBtn);
                g.DrawString(text, _fBtn, white, rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2);
            }

            if (!_running)
            {
                using (var soft = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                {
                    var hint = _isAdmin ? "约 10 s" : "将请求一次 UAC";
                    g.DrawString(hint, _fTiny, soft, rect.Right - g.MeasureString(hint, _fTiny).Width - 18, rect.Y + 17);
                }
            }
        }

        private void DrawFooter(Graphics g)
        {
            using (var faint = new SolidBrush(Faint))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("MemOptimizer v" + Program.Version + " · 基于 PCL2-CE MemSwapWorks · 只整理内存，不触碰游戏与文件",
                    _fTiny, faint, new RectangleF(16, 660, 568, 16), format);
            }
        }

        private void DrawCard(Graphics g, Rectangle rect)
        {
            using (var shadowPath = Round(new RectangleF(rect.X, rect.Y + 2, rect.Width, rect.Height), 13f))
            using (var shadow = new SolidBrush(Color.FromArgb(12, 15, 42, 92)))
                g.FillPath(shadow, shadowPath);

            using (var path = Round(rect, 13f))
            {
                using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
                using (var pen = new Pen(Line, 1f)) g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath Round(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (diameter <= 0.1f)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawCheck(Graphics g, float x, float y, Color color, float width)
        {
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new PointF(x, y + 3.4f),
                    new PointF(x + 2.6f, y + 6f),
                    new PointF(x + 7.2f, y + 0.4f)
                });
            }
        }

        private Rectangle OptionRow(int index)
        {
            var column = index < 4 ? 0 : 1;
            var row = index < 4 ? index : index - 4;
            return new Rectangle(column == 0 ? 28 : 304, 310 + row * 36, 268, 36);
        }

        private Point ToLogical(Point point)
        {
            return new Point((int)Math.Round(point.X / _scale), (int)Math.Round(point.Y / _scale));
        }

        // ----------------------------------------------------------- interaction

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = ToLogical(e.Location);

            var hoverRow = -1;
            for (var i = 0; i < Optimizer.Actions.Length; i++)
            {
                if (OptionRow(i).Contains(p)) { hoverRow = i; break; }
            }

            var hoverButton = ButtonRect.Contains(p);
            if (hoverRow != _hoverRow || hoverButton != _buttonHover)
            {
                _hoverRow = hoverRow;
                _buttonHover = hoverButton;
                Cursor = hoverRow >= 0 || hoverButton ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1 || _buttonHover)
            {
                _hoverRow = -1;
                _buttonHover = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var p = ToLogical(e.Location);
            if (ButtonRect.Contains(p))
            {
                _buttonDown = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var p = ToLogical(e.Location);
            var wasDown = _buttonDown;
            _buttonDown = false;
            Invalidate();

            if (wasDown && ButtonRect.Contains(p)) StartOptimization();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var p = ToLogical(e.Location);
            for (var i = 0; i < Optimizer.Actions.Length; i++)
            {
                if (!OptionRow(i).Contains(p)) continue;
                _enabled[i] = !_enabled[i];
                Invalidate();
                return;
            }
        }

        private async void StartOptimization()
        {
            if (_running) return;
            var scope = CurrentScope();
            if (scope == 0)
            {
                MessageBox.Show(this, "请至少勾选一个整理动作。", "内存优化", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _running = true;
            _report = null;
            _runError = null;
            _liveSteps.Clear();
            Invalidate();

            try
            {
                if (_isAdmin)
                {
                    var report = await Task.Run(() => Optimizer.Run(scope, step =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                _liveSteps.Add(step);
                                Invalidate();
                            }));
                        }
                        catch (InvalidOperationException) { }
                    }));
                    _report = report;
                }
                else
                {
                    var result = await Task.Run(() =>
                    {
                        string error;
                        var report = Elevation.RunElevated(scope, out error);
                        return new ElevatedResult { Report = report, Error = error };
                    });
                    _report = result.Report;
                    _runError = result.Error;
                }
            }
            catch (Exception ex)
            {
                _runError = ex.Message;
            }
            finally
            {
                _running = false;
                RefreshSnapshot();
            }
        }

        private sealed class ElevatedResult
        {
            public MemReport Report;
            public string Error;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_fTitle != null) _fTitle.Dispose();
                if (_fSub != null) _fSub.Dispose();
                if (_fBig != null) _fBig.Dispose();
                if (_fBigUnit != null) _fBigUnit.Dispose();
                if (_fLabel != null) _fLabel.Dispose();
                if (_fSection != null) _fSection.Dispose();
                if (_fBody != null) _fBody.Dispose();
                if (_fDesc != null) _fDesc.Dispose();
                if (_fSmall != null) _fSmall.Dispose();
                if (_fChip != null) _fChip.Dispose();
                if (_fBtn != null) _fBtn.Dispose();
                if (_fTiny != null) _fTiny.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
