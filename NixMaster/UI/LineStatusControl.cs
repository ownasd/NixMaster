using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NixMaster.Models;

namespace NixMaster.UI
{
    public class LineStatusControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;
        private Label  _lblStatus  = null!;
        private Button _btnRefresh = null!;
        private FlowLayoutPanel _blocksPanel = null!;

        // Accent colours for each cell type (dark, premium palette)
        private static readonly Color C_ProdBg      = Color.FromArgb(15, 118, 110);   // teal
        private static readonly Color C_DefectBg    = Color.FromArgb(153, 27, 27);    // deep crimson
        private static readonly Color C_InvBg       = Color.FromArgb(30, 64, 175);    // slate blue
        private static readonly Color C_Running     = Color.FromArgb(22, 163, 74);    // green
        private static readonly Color C_Stop        = Color.FromArgb(220, 38, 38);    // red
        private static readonly Color C_Shortage    = Color.FromArgb(202, 138, 4);    // amber
        private static readonly Color C_CellText    = Color.FromArgb(241, 245, 249);
        private static readonly Color C_NumBig      = Color.White;

        public LineStatusControl(MainForm host)
        {
            _host = host;
            Build();
            this.Load += (_, __) => LoadDataAsync();
        }

        private void Build()
        {
            this.BackColor = MainForm.C_Dark;
            this.Padding   = new Padding(0);

            // ── Header ────────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 64,
                BackColor = MainForm.C_Dark,
                Padding   = new Padding(24, 0, 16, 0)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };
            pnlHeader.Controls.Add(new Label
            {
                Text      = "🏭  Line Status",
                Font      = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(24, 18)
            });

            _btnRefresh = new Button
            {
                Text      = "↻  Refresh",
                Height    = 36, Width = 138,
                FlatStyle = FlatStyle.Flat,
                BackColor = MainForm.C_Blue,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
                Location  = new Point(pnlHeader.Width - 148, 14)
            };
            _btnRefresh.FlatAppearance.BorderSize = 0;
            _btnRefresh.Click += (_, __) => LoadDataAsync();
            pnlHeader.Controls.Add(_btnRefresh);
            pnlHeader.Resize += (_, __) => _btnRefresh.Left = pnlHeader.Width - 148;

            // ── Status strip ──────────────────────────────────────────────────────
            _lblStatus = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 26,
                Text      = "Loading…",
                Font      = new Font("Segoe UI", 9),
                ForeColor = MainForm.C_Text2,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(24, 0, 0, 0),
                BackColor = Color.Transparent
            };

            // ── Scrollable blocks area ────────────────────────────────────────────
            _blocksPanel = new FlowLayoutPanel
            {
                Dock      = DockStyle.Fill,
                AutoScroll = true,
                BackColor  = MainForm.C_Dark,
                Padding    = new Padding(24, 20, 24, 20),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            _blocksPanel.Resize += (_, __) => ResizeBlocks();

            this.Controls.Add(_blocksPanel);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(pnlHeader);
        }

        private async void LoadDataAsync()
        {
            _lblStatus.Text      = "⏳  Fetching line status data…";
            _lblStatus.ForeColor = MainForm.C_Text2;
            _btnRefresh.Enabled  = false;

            var (stats, err) = await _host.Firebase.FetchLineStatusDataAsync();

            if (!string.IsNullOrEmpty(err))
            {
                _lblStatus.Text      = $"⚠  {err}";
                _lblStatus.ForeColor = MainForm.C_Red;
                _btnRefresh.Enabled  = true;
                return;
            }

            BuildBlocks(stats);
            _lblStatus.Text      = $"✔  Updated  ·  {DateTime.Now:HH:mm:ss}";
            _lblStatus.ForeColor = MainForm.C_Green;
            _btnRefresh.Enabled  = true;
        }

        private void BuildBlocks(List<ProductLineStats> stats)
        {
            _blocksPanel.SuspendLayout();
            _blocksPanel.Controls.Clear();

            foreach (var s in stats)
            {
                var block = BuildProductBlock(s);
                block.Margin = new Padding(0, 0, 20, 20); // Spacing between grid items
                _blocksPanel.Controls.Add(block);
            }
            
            _blocksPanel.ResumeLayout();
            ResizeBlocks();
        }

        private void ResizeBlocks()
        {
            if (_blocksPanel.Controls.Count == 0) return;
            
            // Calculate width for 2 blocks per row taking full space
            // _blocksPanel padding is (24, 20, 24, 20)
            int innerWidth = _blocksPanel.ClientSize.Width - _blocksPanel.Padding.Left - _blocksPanel.Padding.Right;
            // Each block has a right margin of 20. Two blocks mean 40 total right margin.
            int blockWidth = (innerWidth - 40) / 2;

            // Fallback to 1 block per row if the screen is too small
            if (blockWidth < 400) blockWidth = innerWidth - 20;

            _blocksPanel.SuspendLayout();
            foreach (Control c in _blocksPanel.Controls)
            {
                c.Width = blockWidth;
            }
            _blocksPanel.ResumeLayout();
        }

        private Panel BuildProductBlock(ProductLineStats s)
        {
            const int BLOCK_H = 168;
            const int TITLE_H = 48;

            var outer = new Panel
            {
                Width     = 480, // Initial, will be resized
                Height    = BLOCK_H,
                BackColor = MainForm.C_Card
            };
            outer.Paint += (_, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, outer.Width - 1, outer.Height - 1);
                // Left accent bar
                using var br = new SolidBrush(s.IsRunning ? C_Running : C_Stop);
                e.Graphics.FillRectangle(br, 0, 0, 4, outer.Height);
            };
            outer.Resize += (_, __) =>
            {
                // keep cells filling width
                RelayoutBlock(outer);
            };

            // ── Title row ─────────────────────────────────────────────────────────
            var pnlTitle = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = TITLE_H,
                BackColor = Color.FromArgb(16, 22, 38),
                Tag       = "titlebar"
            };
            pnlTitle.Paint += (_, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, TITLE_H - 1, pnlTitle.Width, TITLE_H - 1);
            };

            var lblName = new Label
            {
                Text      = s.ProductName.ToUpper(),
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(16, 14)
            };

            // Status pills
            var pillRunning  = MakePill("● LINE RUNNING",  C_Running, s.IsRunning);
            var pillStop     = MakePill("● LINE STOP",     C_Stop,    !s.IsRunning);
            var pillShortage = MakePill("⚠ PARTS SHORTAGE", C_Shortage, false);

            pillRunning.Anchor  = AnchorStyles.Right | AnchorStyles.Top;
            pillStop.Anchor     = AnchorStyles.Right | AnchorStyles.Top;
            pillShortage.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            pnlTitle.SuspendLayout();
            pnlTitle.Controls.Add(lblName);
            pnlTitle.Controls.Add(pillShortage);
            pnlTitle.Controls.Add(pillStop);
            pnlTitle.Controls.Add(pillRunning);
            pnlTitle.ResumeLayout();

            pnlTitle.Resize += (_, __) =>
            {
                int rx = pnlTitle.Width - 12;
                pillShortage.Left = rx - pillShortage.Width; rx = pillShortage.Left - 8;
                pillStop.Left     = rx - pillStop.Width;     rx = pillStop.Left - 8;
                pillRunning.Left  = rx - pillRunning.Width;
            };

            // ── Cells row ─────────────────────────────────────────────────────────
            // We'll build a TableLayoutPanel with 4 columns
            var tlp = new TableLayoutPanel
            {
                Location     = new Point(1, TITLE_H),
                Width        = outer.Width - 2,
                Height       = 120,
                ColumnCount  = 4,
                RowCount     = 1,
                BackColor    = Color.Transparent,
                Margin       = new Padding(0)
            };
            for (int i = 0; i < 4; i++)
                tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            string todayAssembledTxt = s.TodayTarget > 0 ? $"{s.TodayAssembled} / {s.TodayTarget}" : $"{s.TodayAssembled} / —";
            string todayTestedTxt = s.TodayTestingTarget > 0 ? $"{s.TodayTested} / {s.TodayTestingTarget}" : $"{s.TodayTested} / —";

            tlp.Controls.Add(BuildCell("Today's\nAssembled", todayAssembledTxt,  C_ProdBg),   0, 0);
            tlp.Controls.Add(BuildCell("Total\nProduction",  $"{s.CurrentMonthAssembled} / {s.MonthlyTarget}", C_ProdBg), 1, 0);
            tlp.Controls.Add(BuildCell("Today's\nTested",    todayTestedTxt,      Color.FromArgb(3, 105, 161)), 2, 0);
            tlp.Controls.Add(BuildCell("Today's\nDefects",   s.TodayDefects.ToString(),     C_DefectBg), 3, 0);

            outer.Controls.Add(tlp);
            outer.Controls.Add(pnlTitle);

            return outer;
        }

        private static Panel BuildCell(string label, string value, Color bg)
        {
            var pnl = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = bg,
                Margin    = new Padding(1)
            };

            var lblLabel = new Label
            {
                Text      = label.ToUpper(),
                Font      = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 230, 230, 230),
                TextAlign = ContentAlignment.TopCenter,
                Dock      = DockStyle.Top,
                Height    = 28,
                Padding   = new Padding(0, 4, 0, 0)
            };

            var lblVal = new Label
            {
                Text      = value,
                Font      = new Font("Segoe UI", value.Contains("/") ? 18 : 26, FontStyle.Bold),
                ForeColor = C_NumBig,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock      = DockStyle.Fill,
                Padding   = new Padding(0, 0, 0, 8)
            };

            pnl.Controls.Add(lblVal);
            pnl.Controls.Add(lblLabel);
            return pnl;
        }

        private static void RelayoutBlock(Panel outer) { /* sizing handled by Dock/TableLayoutPanel */ }

        private static Label MakePill(string text, Color bg, bool active)
        {
            return new Label
            {
                Text      = text,
                Font      = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = active ? bg : Color.FromArgb(40, bg.R, bg.G, bg.B),
                ForeColor = active ? Color.White : Color.FromArgb(130, 180, 180, 180),
                AutoSize  = false,
                Width     = 142,
                Height    = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                Top       = 10,
                Padding   = new Padding(4, 0, 4, 0)
            };
        }

        public void RefreshData() => LoadDataAsync();

        /// <summary>Line Status auto-refreshes on timer — it's a live dashboard.</summary>
        public bool SupportsAutoRefresh => true;
    }
}
