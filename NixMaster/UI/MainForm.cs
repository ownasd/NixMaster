using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NixMaster.Core;
using NixMaster.Data;

namespace NixMaster.UI
{
    public class MainForm : Form
    {
        // ── Services ──────────────────────────────────────────────────────────────
        public FirebaseReader Firebase { get; } = new FirebaseReader();

        // ── Layout panels ─────────────────────────────────────────────────────────
        private Panel  _sidebar  = null!;
        private Panel  _content  = null!;
        private Label  _lblSync  = null!;
        private Label  _lblClock = null!;
        private System.Windows.Forms.Timer _clockTimer       = null!;
        private System.Windows.Forms.Timer _autoRefreshTimer = null!;

        // ── Dark colour palette (shared across all controls) ──────────────────────
        public static readonly Color C_Dark   = Color.FromArgb(10,  13,  20);
        public static readonly Color C_Sidebar = Color.FromArgb(17,  24,  39);
        public static readonly Color C_Card   = Color.FromArgb(22,  30,  48);
        public static readonly Color C_Blue   = Color.FromArgb(59, 130, 246);
        public static readonly Color C_Green  = Color.FromArgb(34, 197,  94);
        public static readonly Color C_Purple = Color.FromArgb(168, 85, 247);
        public static readonly Color C_Orange = Color.FromArgb(245, 158,  11);
        public static readonly Color C_Red    = Color.FromArgb(239,  68,  68);
        public static readonly Color C_Cyan   = Color.FromArgb(  6, 182, 212);
        public static readonly Color C_Text1  = Color.FromArgb(241, 245, 249);
        public static readonly Color C_Text2  = Color.FromArgb(148, 163, 184);
        public static readonly Color C_Text3  = Color.FromArgb( 71,  85, 105);
        public static readonly Color C_Border = Color.FromArgb( 30,  41,  59);

        private Button? _activeNavBtn;

        public MainForm()
        {
            InitializeForm();
            // CRITICAL: Add _content first, then _sidebar.
            // WinForms docking processes in reverse Controls-collection order,
            // so Left-docked sidebar must be the LAST item added (processed first).
            BuildContent();
            BuildSidebar();
            StartTimers();
            LoadControl(new DashboardControl(this));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _clockTimer?.Stop();
            _autoRefreshTimer?.Stop();
            base.OnFormClosing(e);
        }

        // ─── Form setup ──────────────────────────────────────────────────────────
        private void InitializeForm()
        {
            this.Text          = $"NixMaster  —  Traceability Hub  —  {AppState.CurrentUser}";
            this.Size          = new Size(1280, 780);
            this.MinimumSize   = new Size(1100, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor     = C_Dark;
            this.AutoScaleMode = AutoScaleMode.Dpi;
        }

        // ─── Content area (added BEFORE sidebar so Fill works correctly) ──────────
        private void BuildContent()
        {
            _content = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = C_Dark
            };
            this.Controls.Add(_content);
        }

        // ─── Sidebar (added AFTER content so it docks Left on top of Fill) ────────
        private void BuildSidebar()
        {
            _sidebar = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 240,
                BackColor = C_Sidebar
            };

            // ── Logo panel ───────────────────────────────────────────────────────
            var pnlLogo = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 100,
                BackColor = C_Sidebar
            };

            // Draw bottom border line on logo panel
            pnlLogo.Paint += (s, e) =>
            {
                using var pen = new Pen(C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlLogo.Height - 1, pnlLogo.Width, pnlLogo.Height - 1);
            };

            var lblIcon = new Label
            {
                Text      = "🔗",
                Font      = new Font("Segoe UI Emoji", 20, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize  = true,
                ForeColor = C_Text1,
                Location  = new Point(16, 20),
                BackColor = Color.Transparent
            };

            var lblTitle = new Label
            {
                Text      = "NixMaster",
                Font      = new Font("Segoe UI", 13, FontStyle.Bold),
                AutoSize  = true,
                ForeColor = C_Text1,
                Location  = new Point(56, 18),
                BackColor = Color.Transparent
            };

            var lblSub = new Label
            {
                Text      = "End-to-End Traceability Hub",
                Font      = new Font("Segoe UI", 7.5f),
                AutoSize  = true,
                ForeColor = C_Text2,
                Location  = new Point(57, 44),
                BackColor = Color.Transparent
            };

            pnlLogo.Controls.Add(lblSub);
            pnlLogo.Controls.Add(lblTitle);
            pnlLogo.Controls.Add(lblIcon);

            // ── Nav panel ────────────────────────────────────────────────────────
            var pnlNav = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 440,
                BackColor = C_Sidebar,
                Padding   = new Padding(12, 16, 12, 0)
            };

            int y = 16;
            var btnDash     = NavBtn("📊   Dashboard",    ref y, () => LoadControl(new DashboardControl(this)));
            var btnSubAssy  = NavBtn("🧩   Sub Assy",     ref y, () => LoadControl(new SubAssyControl(this)));
            var btnLineStatus = NavBtn("🏭   Line Status", ref y, () => LoadControl(new LineStatusControl(this)));
            // Plan/Target removed from here, moved to Settings
            var btnChildParts= NavBtn("⚙    Child Parts",   ref y, () => LoadControl(new ChildPartsControl(this)));
            var btnRecords  = NavBtn("📋   Product Trace", ref y, () => LoadControl(new AllRecordsControl(this)));
            var btnSearch   = NavBtn("🔍   Search Unit ID",   ref y, () => LoadControl(new SearchControl(this)));
            var btnSettings = NavBtn("⚙    Settings",     ref y, () => LoadControl(new SettingsControl(this)));

            pnlNav.Controls.AddRange(new Control[] { btnDash, btnSubAssy, btnLineStatus, btnChildParts, btnRecords, btnSearch, btnSettings });
            SetActiveNav(btnDash);

            // ── Bottom info panel ──────────────────────────────────────────────
            var pnlBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 90,
                BackColor = C_Sidebar,
                Padding   = new Padding(14, 10, 14, 10)
            };
            pnlBottom.Paint += (s, e) =>
            {
                using var pen = new Pen(C_Border, 1);
                e.Graphics.DrawLine(pen, 0, 0, pnlBottom.Width, 0);
            };

            _lblSync = new Label
            {
                Text      = "● Live Connected",
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = C_Green,
                AutoSize  = true,
                Location  = new Point(14, 14)
            };

            var lblUser = new Label
            {
                Text      = $"👤  {AppState.CurrentUser}",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = C_Text2,
                AutoSize  = true,
                Location  = new Point(14, 38)
            };

            _lblClock = new Label
            {
                Text      = DateTime.Now.ToString("HH:mm:ss"),
                Font      = new Font("Segoe UI", 8),
                ForeColor = C_Text3,
                AutoSize  = true,
                Location  = new Point(14, 60)
            };

            pnlBottom.Controls.AddRange(new Control[] { _lblSync, lblUser, _lblClock });

            // ── Assemble sidebar ──────────────────────────────────────────────
            _sidebar.Controls.Add(pnlBottom);
            _sidebar.Controls.Add(pnlNav);
            _sidebar.Controls.Add(pnlLogo);

            this.Controls.Add(_sidebar);
        }

        // ─── Load a control into the content area ─────────────────────────────────
        public void LoadControl(UserControl ctrl)
        {
            _content.Controls.Clear();
            ctrl.Dock = DockStyle.Fill;
            _content.Controls.Add(ctrl);
        }

        // ─── Online/offline indicator ─────────────────────────────────────────────
        public void SetOnlineStatus(bool online)
        {
            if (_lblSync.InvokeRequired)
                _lblSync.Invoke(() => SetOnlineStatus(online));
            else
            {
                _lblSync.Text      = online ? "● Live Connected" : "● Offline";
                _lblSync.ForeColor = online ? C_Green : C_Red;
            }
        }

        // ─── Timers ───────────────────────────────────────────────────────────────
        private void StartTimers()
        {
            _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _clockTimer.Tick += (_, __) => _lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();

            _autoRefreshTimer = new System.Windows.Forms.Timer
            {
                Interval = AppState.Settings.RefreshInterval * 1000
            };
            // Auto-refresh only fires for screens that explicitly opt-in (SupportsAutoRefresh = true).
            // Line Status opts in; AllRecords, Search, SubAssy, Dashboard etc. do NOT.
            _autoRefreshTimer.Tick += (_, __) =>
            {
                if (_content.Controls.Count > 0 &&
                    _content.Controls[0] is IDashboardRefreshable r &&
                    r.SupportsAutoRefresh)
                    r.RefreshData();
            };
            if (AppState.Settings.AutoRefresh)
                _autoRefreshTimer.Start();
        }

        public void RestartAutoRefresh()
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Interval = AppState.Settings.RefreshInterval * 1000;
            if (AppState.Settings.AutoRefresh)
                _autoRefreshTimer.Start();
        }

        // ─── Nav button factory ───────────────────────────────────────────────────
        private Button NavBtn(string text, ref int y, Action onClick)
        {
            var b = new Button
            {
                Text      = text,
                Location  = new Point(0, y),
                Width     = 216,
                Height    = 48,
                FlatStyle = FlatStyle.Flat,
                ForeColor = C_Text2,
                BackColor = C_Sidebar,
                Font      = new Font("Segoe UI", 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(18, 0, 0, 0),
                Cursor    = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (_, __) => { if (b != _activeNavBtn) b.BackColor = Color.FromArgb(28, 38, 58); };
            b.MouseLeave += (_, __) => { if (b != _activeNavBtn) b.BackColor = C_Sidebar; };
            b.Click      += (_, __) => { SetActiveNav(b); onClick(); };
            y += 52;
            return b;
        }

        private void SetActiveNav(Button b)
        {
            if (_activeNavBtn != null)
            {
                _activeNavBtn.BackColor = C_Sidebar;
                _activeNavBtn.ForeColor = C_Text2;
            }
            _activeNavBtn           = b;
            b.BackColor = Color.FromArgb(24, 48, 96);
            b.ForeColor = Color.FromArgb(147, 197, 253);
        }
    }

    /// <summary>
    /// Implement this on controls that support manual or auto refresh.
    /// SupportsAutoRefresh = true only for screens that should refresh on a timer (Line Status).
    /// All other screens set SupportsAutoRefresh = false (default) — they load once on open.
    /// </summary>
    public interface IDashboardRefreshable
    {
        void RefreshData();
        /// <summary>Returns true only for Line Status. All other screens return false.</summary>
        bool SupportsAutoRefresh { get; }
    }
}
