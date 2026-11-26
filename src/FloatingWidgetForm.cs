using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class FloatingWidgetForm : Form
    {
        private const int HotKeyId = 9001;
        private const int ResizeGrip = 6;

        private readonly ToolTip _toolTip;
        private string _lastTooltipText;

        private int _totalSeconds;
        private int _regularLimitSeconds;
        private int _overtimeLimitSeconds;
        private TrackerState _state;
        private AlertLevel _alertLevel;
        private bool _inMeetingMode;
        private bool _personalUseMode;
        private string _activeAbsenceType;
        private bool _pulsePhase;
        private bool _isDemoMode;

        private Rectangle _btnPauseRect;
        private Rectangle _btnHistoryRect;
        private Rectangle _btnHideRect;
        private int _hoveredButton; // 0=none, 1=pause, 2=history, 3=hide

        public event EventHandler PauseToggleRequested;
        public event EventHandler HistoryRequested;
        public event EventHandler HideRequested;
        public event EventHandler GlobalToggleRequested;
        public event EventHandler ShowExistingRequested;
        public event EventHandler GeometryChanged;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_MINIMIZEBOX = 0x00020000;
                CreateParams cp = base.CreateParams;
                cp.Style |= WS_MINIMIZEBOX;
                return cp;
            }
        }

        public FloatingWidgetForm(AppConfig config, ContextMenuStrip sharedMenu, bool isDemoMode)
        {
            _isDemoMode = isDemoMode;
            _regularLimitSeconds = config.RegularLimitMinutes * 60;
            _overtimeLimitSeconds = config.OvertimeLimitMinutes * 60;
            _state = TrackerState.Working;
            _alertLevel = AlertLevel.Normal;
            _inMeetingMode = false;
            _activeAbsenceType = string.Empty;
            _toolTip = new ToolTip();
            _toolTip.InitialDelay = 250;
            _toolTip.AutoPopDelay = 4000;
            _lastTooltipText = string.Empty;

            Text = isDemoMode ? "Relógio Acompanhamento (Demo)" : "Relógio Acompanhamento";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(155, 46);
            MaximumSize = new Size(420, 140);
            Size = new Size(config.WidgetWidth, config.WidgetHeight);
            TopMost = config.AlwaysOnTop;
            ShowInTaskbar = config.ShowInTaskbar;
            BackColor = Color.FromArgb(24, 26, 32);
            Opacity = 0.96;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            ContextMenuStrip = sharedMenu;

            if (config.WidgetX >= 0 && config.WidgetY >= 0 && IsVisibleOnAnyScreen(new Rectangle(config.WidgetX, config.WidgetY, Width, Height)))
            {
                Location = new Point(config.WidgetX, config.WidgetY);
            }
            else
            {
                AnchorBottomRight();
            }

            IntPtr h = Handle;
            if (h != IntPtr.Zero)
            {
                NativeMethods.TryEnableRoundedCorners(h);
                NativeMethods.RegisterHotKey(h, HotKeyId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, NativeMethods.VK_R);
            }
        }

        public void AnchorBottomRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int margin = 12;
            Location = new Point(wa.Right - Width - margin, wa.Bottom - Height - margin);
            RaiseGeometryChanged();
        }

        public void ApplyPresetSize(int width, int height)
        {
            bool wasAnchoredRight = IsNearBottomRight();
            Size = new Size(width, height);
            if (wasAnchoredRight)
            {
                AnchorBottomRight();
            }
            else
            {
                KeepInsideScreen();
                RaiseGeometryChanged();
            }
            Invalidate();
        }

        public void UpdateDisplay(
            int totalSeconds,
            int regularLimitSeconds,
            int overtimeLimitSeconds,
            TrackerState state,
            AlertLevel alertLevel,
            bool inMeetingMode,
            bool personalUseMode,
            string activeAbsenceType)
        {
            _totalSeconds = totalSeconds;
            _regularLimitSeconds = regularLimitSeconds;
            _overtimeLimitSeconds = overtimeLimitSeconds;
            _state = state;
            _alertLevel = alertLevel;
            _inMeetingMode = inMeetingMode;
            _personalUseMode = personalUseMode;
            _activeAbsenceType = activeAbsenceType ?? string.Empty;

            if (alertLevel == AlertLevel.LimitReached && state == TrackerState.Working && !personalUseMode)
            {
                _pulsePhase = !_pulsePhase;
            }
            else
            {
                _pulsePhase = false;
            }

            string titlePrefix = _isDemoMode ? "[DEMO] " : string.Empty;
            string modeTag = _personalUseMode
                ? " [Uso Pessoal]"
                : ((_inMeetingMode && _state == TrackerState.Working) ? " [Reunião]" : string.Empty);
            string nextTitle = string.Format("{0}{1}{2} — Relógio Acompanhamento", titlePrefix, TimeFormat.FormatHms(totalSeconds), modeTag);
            if (Text != nextTitle)
            {
                Text = nextTitle;
            }

            Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try
            {
                NativeMethods.UnregisterHotKey(Handle, HotKeyId);
            }
            catch
            {
                // Ignora ao encerrar
            }
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (HideRequested != null)
                {
                    HideRequested(this, EventArgs.Empty);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            KeepInsideScreen();
            RaiseGeometryChanged();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotKeyId)
            {
                if (GlobalToggleRequested != null)
                {
                    GlobalToggleRequested(this, EventArgs.Empty);
                }
                return;
            }

            if ((uint)m.Msg == NativeMethods.WM_SHOW_EXISTING)
            {
                if (ShowExistingRequested != null)
                {
                    ShowExistingRequested(this, EventArgs.Empty);
                }
                return;
            }

            if (m.Msg == NativeMethods.WM_NCHITTEST)
            {
                Point pt = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, (m.LParam.ToInt32() >> 16) & 0xFFFF));

                if (_btnPauseRect.Contains(pt) || _btnHistoryRect.Contains(pt) || _btnHideRect.Contains(pt))
                {
                    m.Result = new IntPtr(NativeMethods.HTCLIENT);
                    return;
                }

                bool left = pt.X <= ResizeGrip;
                bool right = pt.X >= ClientSize.Width - ResizeGrip;
                bool top = pt.Y <= ResizeGrip;
                bool bottom = pt.Y >= ClientSize.Height - ResizeGrip;

                if (top && left) { m.Result = new IntPtr(NativeMethods.HTTOPLEFT); return; }
                if (top && right) { m.Result = new IntPtr(NativeMethods.HTTOPRIGHT); return; }
                if (bottom && left) { m.Result = new IntPtr(NativeMethods.HTBOTTOMLEFT); return; }
                if (bottom && right) { m.Result = new IntPtr(NativeMethods.HTBOTTOMRIGHT); return; }
                if (left) { m.Result = new IntPtr(NativeMethods.HTLEFT); return; }
                if (right) { m.Result = new IntPtr(NativeMethods.HTRIGHT); return; }
                if (top) { m.Result = new IntPtr(NativeMethods.HTTOP); return; }
                if (bottom) { m.Result = new IntPtr(NativeMethods.HTBOTTOM); return; }
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_btnPauseRect.Contains(e.Location))
            {
                if (PauseToggleRequested != null) PauseToggleRequested(this, EventArgs.Empty);
                return;
            }
            if (_btnHistoryRect.Contains(e.Location))
            {
                if (HistoryRequested != null) HistoryRequested(this, EventArgs.Empty);
                return;
            }
            if (_btnHideRect.Contains(e.Location))
            {
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
                return;
            }

            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            KeepInsideScreen();
            RaiseGeometryChanged();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left &&
                !_btnPauseRect.Contains(e.Location) &&
                !_btnHistoryRect.Contains(e.Location) &&
                !_btnHideRect.Contains(e.Location))
            {
                if (HistoryRequested != null) HistoryRequested(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int nextHover = 0;
            string tip = string.Empty;

            if (_btnPauseRect.Contains(e.Location))
            {
                nextHover = 1;
                tip = _state == TrackerState.Working ? "Pausar manualmente (botão direito para escolher tipo de ausência)" : "Retomar contagem";
            }
            else if (_btnHistoryRect.Contains(e.Location))
            {
                nextHover = 2;
                tip = "Ver histórico, registros e ausências";
            }
            else if (_btnHideRect.Contains(e.Location))
            {
                nextHover = 3;
                tip = ShowInTaskbar
                    ? "Minimizar para a barra de tarefas (Ctrl+Alt+R)"
                    : "Ocultar para a bandeja (Ctrl+Alt+R)";
            }

            Cursor = nextHover > 0 ? Cursors.Hand : Cursors.SizeAll;

            if (nextHover != _hoveredButton)
            {
                _hoveredButton = nextHover;
                Invalidate();
            }

            if (tip != _lastTooltipText)
            {
                _lastTooltipText = tip;
                _toolTip.SetToolTip(this, tip);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoveredButton != 0)
            {
                _hoveredButton = 0;
                Invalidate();
            }
            Cursor = Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            Color bgColor = Color.FromArgb(24, 26, 32);
            Color accentColor = GetAccentColor();
            Color borderColor = Color.FromArgb(46, 52, 66);

            if (_state == TrackerState.Working && _alertLevel == AlertLevel.LimitReached)
            {
                bgColor = _pulsePhase ? Color.FromArgb(46, 20, 24) : Color.FromArgb(34, 18, 22);
                borderColor = _pulsePhase ? Color.FromArgb(239, 68, 68) : Color.FromArgb(185, 28, 28);
            }
            else if (_state == TrackerState.Working && (_alertLevel == AlertLevel.Overtime || _alertLevel == AlertLevel.PreOvertimeLimit))
            {
                borderColor = Color.FromArgb(194, 98, 24);
            }
            else if (_state == TrackerState.Working && _inMeetingMode)
            {
                borderColor = Color.FromArgb(56, 189, 248);
            }

            using (SolidBrush bg = new SolidBrush(bgColor))
            {
                g.FillRectangle(bg, 0, 0, w, h);
            }

            int btnSize = Math.Max(16, Math.Min(22, (int)(h * 0.30f)));
            int btnGap = 3;
            int rightPad = 8;
            int topPad = Math.Max(6, (int)(h * 0.11f));

            _btnHideRect = new Rectangle(w - rightPad - btnSize, topPad, btnSize, btnSize);
            _btnHistoryRect = new Rectangle(_btnHideRect.Left - btnGap - btnSize, topPad, btnSize, btnSize);
            _btnPauseRect = new Rectangle(_btnHistoryRect.Left - btnGap - btnSize, topPad, btnSize, btnSize);

            DrawMiniButton(g, _btnPauseRect, _hoveredButton == 1, 1);
            DrawMiniButton(g, _btnHistoryRect, _hoveredButton == 2, 2);
            DrawMiniButton(g, _btnHideRect, _hoveredButton == 3, 3);

            int leftPad = 10;
            int dotSize = Math.Max(7, Math.Min(10, (int)(h * 0.13f)));
            int dotY = topPad + (btnSize - dotSize) / 2;

            using (SolidBrush dotBrush = new SolidBrush(accentColor))
            {
                g.FillEllipse(dotBrush, leftPad, dotY, dotSize, dotSize);
            }

            string statusText = GetStatusText();
            float statusFontPx = Math.Max(10.0f, Math.Min(13.5f, h * 0.165f));
            int statusLeft = leftPad + dotSize + 6;
            int statusMaxWidth = Math.Max(20, _btnPauseRect.Left - statusLeft - 4);

            Color statusColor = _state == TrackerState.Working
                ? (_inMeetingMode && _alertLevel == AlertLevel.Normal ? Color.FromArgb(125, 211, 252) : Color.FromArgb(203, 213, 225))
                : Color.FromArgb(148, 163, 184);

            using (Font statusFont = new Font("Segoe UI", statusFontPx, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush statusBrush = new SolidBrush(statusColor))
            using (StringFormat sf = new StringFormat())
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.LineAlignment = StringAlignment.Center;
                RectangleF statusRect = new RectangleF(statusLeft, topPad - 1, statusMaxWidth, btnSize + 2);
                g.DrawString(statusText, statusFont, statusBrush, statusRect, sf);
            }

            string timeText = TimeFormat.FormatHms(_totalSeconds);
            int barHeight = Math.Max(4, Math.Min(6, (int)(h * 0.08f)));
            int barTop = h - barHeight - 6;
            int clockTop = topPad + btnSize + 1;
            int clockAvailHeight = Math.Max(16, barTop - clockTop - 2);
            int clockAvailWidth = Math.Max(60, w - (leftPad * 2));

            float fontByHeight = clockAvailHeight * 0.90f;
            float fontByWidth = clockAvailWidth / 5.2f;
            float clockFontPx = Math.Max(14.0f, Math.Min(48.0f, Math.Min(fontByHeight, fontByWidth)));

            Color timeColor = _state == TrackerState.Working
                ? Color.FromArgb(248, 250, 252)
                : Color.FromArgb(148, 163, 184);

            using (Font clockFont = new Font("Consolas", clockFontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush timeBrush = new SolidBrush(timeColor))
            using (StringFormat sfClock = new StringFormat())
            {
                sfClock.Alignment = StringAlignment.Near;
                sfClock.LineAlignment = StringAlignment.Center;
                sfClock.FormatFlags = StringFormatFlags.NoWrap;
                RectangleF clockRect = new RectangleF(leftPad - 1, clockTop, clockAvailWidth + 2, clockAvailHeight);
                g.DrawString(timeText, clockFont, timeBrush, clockRect, sfClock);
            }

            Rectangle barRect = new Rectangle(leftPad, barTop, w - (leftPad * 2), barHeight);
            DrawProgressBar(g, barRect);

            using (Pen borderPen = new Pen(borderColor, 1.5f))
            {
                g.DrawRectangle(borderPen, 0.5f, 0.5f, w - 1.5f, h - 1.5f);
            }
        }

        private void DrawMiniButton(Graphics g, Rectangle rect, bool hovered, int type)
        {
            if (hovered)
            {
                using (SolidBrush hoverBg = new SolidBrush(Color.FromArgb(51, 58, 74)))
                {
                    g.FillRectangle(hoverBg, rect);
                }
            }

            Color iconColor = hovered ? Color.FromArgb(248, 250, 252) : Color.FromArgb(148, 163, 184);
            int cx = rect.Left + rect.Width / 2;
            int cy = rect.Top + rect.Height / 2;
            int s = Math.Max(4, rect.Width / 4);

            if (type == 1)
            {
                using (SolidBrush br = new SolidBrush(iconColor))
                {
                    if (_state == TrackerState.Working)
                    {
                        g.FillRectangle(br, cx - s + 1, cy - s, 2.2f, s * 2);
                        g.FillRectangle(br, cx + 1.5f, cy - s, 2.2f, s * 2);
                    }
                    else
                    {
                        PointF[] tri = new PointF[]
                        {
                            new PointF(cx - s + 2, cy - s),
                            new PointF(cx + s, cy),
                            new PointF(cx - s + 2, cy + s)
                        };
                        g.FillPolygon(br, tri);
                    }
                }
            }
            else if (type == 2)
            {
                using (Pen p = new Pen(iconColor, 1.4f))
                {
                    g.DrawLine(p, cx - s, cy - s + 1, cx + s, cy - s + 1);
                    g.DrawLine(p, cx - s, cy, cx + s, cy);
                    g.DrawLine(p, cx - s, cy + s - 1, cx + s, cy + s - 1);
                }
            }
            else if (type == 3)
            {
                using (Pen p = new Pen(iconColor, 1.6f))
                {
                    g.DrawLine(p, cx - s, cy + 1, cx + s, cy + 1);
                }
            }
        }

        private void DrawProgressBar(Graphics g, Rectangle rect)
        {
            if (rect.Width <= 4 || rect.Height <= 1) return;

            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(42, 47, 58)))
            {
                g.FillRectangle(trackBrush, rect);
            }

            int totalLimit = Math.Max(1, _regularLimitSeconds + _overtimeLimitSeconds);
            double regularShare = (double)_regularLimitSeconds / totalLimit;
            int regularMarkerX = rect.Left + (int)Math.Round(rect.Width * regularShare);

            double clampedRatio = Math.Min(1.0, (double)_totalSeconds / totalLimit);
            int filledWidth = (int)Math.Round(rect.Width * clampedRatio);

            if (filledWidth > 0)
            {
                int regularFilled = Math.Min(filledWidth, regularMarkerX - rect.Left);
                if (regularFilled > 0)
                {
                    Color regColor;
                    if (_state != TrackerState.Working)
                    {
                        regColor = Color.FromArgb(100, 116, 139);
                    }
                    else if (_overtimeLimitSeconds <= 0 && _alertLevel == AlertLevel.LimitReached)
                    {
                        regColor = Color.FromArgb(239, 68, 68);
                    }
                    else if (_inMeetingMode)
                    {
                        regColor = Color.FromArgb(56, 189, 248);
                    }
                    else
                    {
                        regColor = Color.FromArgb(34, 197, 94);
                    }

                    using (SolidBrush regBrush = new SolidBrush(regColor))
                    {
                        g.FillRectangle(regBrush, rect.Left, rect.Top, regularFilled, rect.Height);
                    }
                }

                int extraFilled = filledWidth - (regularMarkerX - rect.Left);
                if (extraFilled > 0 && _overtimeLimitSeconds > 0)
                {
                    Color extraColor = _alertLevel == AlertLevel.LimitReached
                        ? Color.FromArgb(239, 68, 68)
                        : Color.FromArgb(249, 115, 22);
                    using (SolidBrush extraBrush = new SolidBrush(extraColor))
                    {
                        g.FillRectangle(extraBrush, regularMarkerX, rect.Top, extraFilled, rect.Height);
                    }
                }
            }

            if (_overtimeLimitSeconds > 0 && regularMarkerX > rect.Left + 2 && regularMarkerX < rect.Right - 2)
            {
                using (Pen tickPen = new Pen(Color.FromArgb(24, 26, 32), 1.5f))
                {
                    g.DrawLine(tickPen, regularMarkerX, rect.Top, regularMarkerX, rect.Bottom);
                }
            }
        }

        private Color GetAccentColor()
        {
            if (_personalUseMode)
            {
                return Color.FromArgb(129, 140, 248);
            }

            if (_state != TrackerState.Working)
            {
                return Color.FromArgb(148, 163, 184);
            }

            switch (_alertLevel)
            {
                case AlertLevel.PreRegular:
                    return Color.FromArgb(234, 179, 8);
                case AlertLevel.Overtime:
                    return Color.FromArgb(249, 115, 22);
                case AlertLevel.PreOvertimeLimit:
                    return Color.FromArgb(234, 88, 12);
                case AlertLevel.LimitReached:
                    return Color.FromArgb(239, 68, 68);
                default:
                    return _inMeetingMode ? Color.FromArgb(56, 189, 248) : Color.FromArgb(34, 197, 94);
            }
        }

        private string GetStatusText()
        {
            string prefix = _isDemoMode ? "[DEMO] " : string.Empty;

            if (_personalUseMode)
            {
                return prefix + "Uso pessoal";
            }

            if (_state == TrackerState.PausedManual)
            {
                if (!string.IsNullOrEmpty(_activeAbsenceType))
                {
                    return prefix + AbsenceTypes.GetShortWidgetLabel(_activeAbsenceType);
                }
                return prefix + "Pausado";
            }
            if (_state == TrackerState.PausedSession)
            {
                return prefix + "Congelado";
            }

            int totalLimitHours = Math.Max(1, (_regularLimitSeconds + _overtimeLimitSeconds) / 3600);
            int regLimitHours = Math.Max(1, _regularLimitSeconds / 3600);
            int extraSec = (_overtimeLimitSeconds > 0 && _totalSeconds > _regularLimitSeconds)
                ? _totalSeconds - _regularLimitSeconds
                : 0;

            switch (_alertLevel)
            {
                case AlertLevel.LimitReached:
                    return prefix + string.Format("LIMITE {0}h!", totalLimitHours);
                case AlertLevel.PreOvertimeLimit:
                    return prefix + "Extra +" + TimeFormat.FormatHm(extraSec) + " (!)";
                case AlertLevel.Overtime:
                    return prefix + "Extra +" + TimeFormat.FormatHm(extraSec);
                case AlertLevel.PreRegular:
                    return prefix + string.Format("Quase {0}h", regLimitHours);
                default:
                    return prefix + (_inMeetingMode ? "Em reunião" : "Trabalhando");
            }
        }

        private bool IsNearBottomRight()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int distRight = Math.Abs((wa.Right - 12) - Right);
            int distBottom = Math.Abs((wa.Bottom - 12) - Bottom);
            return distRight <= 25 && distBottom <= 25;
        }

        private void KeepInsideScreen()
        {
            Screen scr = Screen.FromRectangle(Bounds);
            Rectangle wa = scr.WorkingArea;
            int x = Left;
            int y = Top;

            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            if (x + Width > wa.Right) x = Math.Max(wa.Left, wa.Right - Width);
            if (y + Height > wa.Bottom) y = Math.Max(wa.Top, wa.Bottom - Height);

            if (x != Left || y != Top)
            {
                Location = new Point(x, y);
            }
        }

        private static bool IsVisibleOnAnyScreen(Rectangle rect)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle intersect = Rectangle.Intersect(s.WorkingArea, rect);
                if (intersect.Width >= 40 && intersect.Height >= 25)
                {
                    return true;
                }
            }
            return false;
        }

        private void RaiseGeometryChanged()
        {
            if (GeometryChanged != null)
            {
                GeometryChanged(this, EventArgs.Empty);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
