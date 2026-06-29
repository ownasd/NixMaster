using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NixMaster.Models;

namespace NixMaster.UI
{
    public class ChildPartsControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;

        private DateTimePicker _dtFrom        = null!;
        private DateTimePicker _dtTo          = null!;
        private DataGridView   _grid          = null!;
        private Label          _lblTotal      = null!;
        private Label          _lblStatusBar  = null!;
        private Panel          _pnlInstruct   = null!;

        private List<PivotedPartItem> _filteredData = new();
        private bool _dataLoaded = false;

        private class PivotedPartItem
        {
            public string MacId { get; set; } = "";
            public string ScannedAt { get; set; } = "";
            public string ScannedBy { get; set; } = "";
            public string Station { get; set; } = "";
            public Dictionary<string, string> Parts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public ChildPartsControl(MainForm host)
        {
            _host = host;
            Build();
        }

        private void Build()
        {
            this.BackColor = MainForm.C_Dark;

            // ── Page header ────────────────────────────────────────────────────────
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
                Text      = "⚙  Child Parts Traceability",
                Font      = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(24, 18)
            });

            // ── Filter toolbar ─────────────────────────────────────────────────────
            var bar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 62,
                BackColor = MainForm.C_Sidebar,
                Padding   = new Padding(16, 0, 16, 0)
            };
            bar.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };

            var flow = new FlowLayoutPanel
            {
                Dock         = DockStyle.Fill,
                BackColor    = Color.Transparent,
                WrapContents = false,
                Padding      = new Padding(0, 12, 0, 0),
                AutoSize     = false
            };

            flow.Controls.Add(FilterLabel("From:"));
            _dtFrom = DatePick(DateTime.Today.AddDays(-7));
            flow.Controls.Add(_dtFrom);

            flow.Controls.Add(FilterLabel("  To:"));
            _dtTo = DatePick(DateTime.Today);
            flow.Controls.Add(_dtTo);

            var btnApply = FilterBtn("🔍  Apply Filter", MainForm.C_Blue);
            var btnClear = FilterBtn("✕  Clear",         Color.FromArgb(50, 60, 80));
            var btnCsv   = FilterBtn("⬇  CSV",           MainForm.C_Green);

            btnApply.Width  = 120;
            btnApply.Click += async (_, __) => await FetchAndApplyAsync();
            btnClear.Click += (_, __) => ClearFilter();
            btnCsv.Click   += (_, __) => ExportCsv();

            flow.Controls.Add(new Panel { Width = 8, Height = 36, BackColor = Color.Transparent });
            flow.Controls.AddRange(new Control[] { btnApply, btnClear, btnCsv });

            _lblTotal = new Label
            {
                Text      = "",
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.C_Text2,
                AutoSize  = true,
                Margin    = new Padding(12, 10, 0, 0)
            };
            flow.Controls.Add(_lblTotal);
            bar.Controls.Add(flow);

            // ── Status strip ───────────────────────────────────────────────────────
            _lblStatusBar = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 26,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = MainForm.C_Text2,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(24, 0, 0, 0),
                BackColor = Color.Transparent
            };

            // ── Instruction Panel ──────────────────────────────────────────────────
            _pnlInstruct = BuildInstructionPanel();

            // ── Grid ──────────────────────────────────────────────────────────────
            _grid         = BuildGrid();
            _grid.Visible = false;

            this.Controls.Add(_pnlInstruct);
            this.Controls.Add(_grid);
            this.Controls.Add(_lblStatusBar);
            this.Controls.Add(bar);
            this.Controls.Add(pnlHeader);
        }

        private Panel BuildInstructionPanel()
        {
            var pnl = new Panel { Dock = DockStyle.Fill, BackColor = MainForm.C_Dark };

            var card = new Panel
            {
                Width     = 640,
                Height    = 370,
                BackColor = MainForm.C_Card
            };

            void PositionCard()
            {
                card.Left = Math.Max(0, (pnl.Width  - card.Width)  / 2);
                card.Top  = Math.Max(0, (pnl.Height - card.Height) / 2);
            }
            pnl.Resize += (_, __) => PositionCard();
            pnl.Controls.Add(card);

            var lblIcon = new Label
            {
                Text      = "🔍",
                Font      = new Font("Segoe UI", 36),
                AutoSize  = true,
                ForeColor = MainForm.C_Text1,
                Location  = new Point(280, 24)
            };

            var lblTitle = new Label
            {
                Text      = "Search Child Parts Records by Date Range",
                Font      = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = false,
                Width     = 580,
                Height    = 28,
                TextAlign = ContentAlignment.TopCenter,
                Location  = new Point(30, 88)
            };

            var div = new Panel { BackColor = MainForm.C_Border, Location = new Point(40, 124), Width = 560, Height = 1 };

            var instructions = new Label
            {
                Text =
                    "How to load records:\r\n\r\n" +
                    "  1.  Select a start date in the  \"From\"  field in the toolbar above.\r\n\r\n" +
                    "  2.  Select an end date in the  \"To\"  field.\r\n\r\n" +
                    "  3.  Click the   🔍 Apply Filter   button.\r\n\r\n" +
                    "  Tip:  Keep the date range short (7 – 30 days) for faster loading\r\n" +
                    "         and lower Firebase data usage.",
                Font      = new Font("Segoe UI", 10),
                ForeColor = MainForm.C_Text2,
                AutoSize  = false,
                Width     = 580,
                Height    = 180,
                TextAlign = ContentAlignment.TopLeft,
                Location  = new Point(30, 136)
            };

            var note = new Label
            {
                Text      = "⚡  Only the selected date range is downloaded from Firebase — saving your quota.",
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = MainForm.C_Green,
                AutoSize  = false,
                Width     = 580,
                Height    = 22,
                TextAlign = ContentAlignment.TopCenter,
                Location  = new Point(30, 338)
            };

            card.Controls.AddRange(new Control[] { lblIcon, lblTitle, div, instructions, note });
            return pnl;
        }

        private async System.Threading.Tasks.Task FetchAndApplyAsync()
        {
            DateTime from = _dtFrom.Value.Date;
            DateTime to   = _dtTo.Value.Date;

            if (from > to)
            {
                MessageBox.Show("'From' date cannot be after 'To' date.", "Invalid Range",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _lblStatusBar.Text      = "⏳  Fetching records from Firebase…";
            _lblStatusBar.ForeColor = MainForm.C_Text2;

            var (records, error) = await _host.Firebase.FetchRangeAsync(from, to);
            _host.SetOnlineStatus(_host.Firebase.IsOnline);

            if (!string.IsNullOrEmpty(error))
            {
                _lblStatusBar.Text      = $"⚠  {error}";
                _lblStatusBar.ForeColor = MainForm.C_Red;
                return;
            }

            _filteredData.Clear();

            foreach (var r in records)
            {
                if (r.Assembly?.Parts == null) continue;

                var item = new PivotedPartItem
                {
                    MacId = r.MacId,
                    ScannedAt = r.Assembly.Timestamp ?? "—",
                    ScannedBy = r.Assembly.Operator ?? "—",
                    Station = r.Assembly.StationName ?? "—"
                };

                foreach (var kv in r.Assembly.Parts)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    item.Parts[kv.Key] = kv.Value;
                }

                _filteredData.Add(item);
            }

            _dataLoaded = true;
            _pnlInstruct.Visible = false;
            _grid.Visible        = true;

            PopulateGrid(_filteredData);
            _lblTotal.Text = $"Parts: {_filteredData.Count}";

            int days = (int)(to - from).TotalDays + 1;
            _lblStatusBar.Text      = $"✔  {_filteredData.Count} parts loaded  ·  {from:dd MMM} – {to:dd MMM yyyy}  ({days} day{(days > 1 ? "s" : "")})  ·  {DateTime.Now:HH:mm:ss}";
            _lblStatusBar.ForeColor = MainForm.C_Green;
        }

        public void RefreshData()
        {
            if (_dataLoaded)
                _ = FetchAndApplyAsync();
        }

        public bool SupportsAutoRefresh => false;

        private void ClearFilter()
        {
            _dtFrom.Value          = DateTime.Today.AddDays(-7);
            _dtTo.Value            = DateTime.Today;

            _filteredData.Clear();
            _dataLoaded          = false;
            _grid.Visible        = false;
            _pnlInstruct.Visible = true;
            _lblTotal.Text       = "";
            _lblStatusBar.Text   = "";
        }

        private void PopulateGrid(List<PivotedPartItem> data)
        {
            _grid.Rows.Clear();
            _grid.Columns.Clear();

            DataGridViewTextBoxColumn Col(string name, string header, int fw) =>
                new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = fw, MinimumWidth = 36 };

            _grid.Columns.Add(Col("colSr", "#", 2));
            _grid.Columns.Add(Col("colMac", "Parent Serial No", 15));

            // Extract all unique Part Types across all records
            var uniquePartTypes = data.SelectMany(d => d.Parts.Keys)
                                      .Distinct(StringComparer.OrdinalIgnoreCase)
                                      .OrderBy(k => k)
                                      .ToList();

            foreach (var pType in uniquePartTypes)
            {
                _grid.Columns.Add(Col($"col_{pType.Replace(" ", "")}", pType, 15));
            }

            _grid.Columns.Add(Col("colStn", "Station", 10));
            _grid.Columns.Add(Col("colOp", "Operator", 10));
            _grid.Columns.Add(Col("colTs", "Assembly Time", 15));

            int sr = 1;
            foreach (var p in data)
            {
                var rowVals = new List<object>
                {
                    sr++,
                    p.MacId
                };

                foreach (var pType in uniquePartTypes)
                {
                    rowVals.Add(p.Parts.TryGetValue(pType, out var val) ? val : "");
                }

                rowVals.Add(p.Station);
                rowVals.Add(p.ScannedBy);
                rowVals.Add(p.ScannedAt);

                _grid.Rows.Add(rowVals.ToArray());
            }
        }

        private void ExportCsv()
        {
            if (!_dataLoaded || _filteredData.Count == 0)
            {
                MessageBox.Show("Please apply a date filter first, then export.", "No Data",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter   = "CSV Files|*.csv",
                FileName = $"NixMaster_ChildParts_{_dtFrom.Value:yyyyMMdd}_to_{_dtTo.Value:yyyyMMdd}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var uniquePartTypes = _filteredData.SelectMany(d => d.Parts.Keys)
                                               .Distinct(StringComparer.OrdinalIgnoreCase)
                                               .OrderBy(k => k)
                                               .ToList();

            var headers = new List<string> { "Sr", "Parent_Serial_No" };
            headers.AddRange(uniquePartTypes.Select(pt => pt.Replace(",", " ")));
            headers.Add("Station");
            headers.Add("Operator");
            headers.Add("Assembly_Time");

            var lines = new List<string> { string.Join(",", headers) };

            int sr = 1;
            foreach (var p in _filteredData)
            {
                var vals = new List<string>
                {
                    sr++.ToString(),
                    Q(p.MacId)
                };

                foreach (var pType in uniquePartTypes)
                {
                    vals.Add(Q(p.Parts.TryGetValue(pType, out var val) ? val : ""));
                }

                vals.Add(Q(p.Station));
                vals.Add(Q(p.ScannedBy));
                vals.Add(Q(p.ScannedAt));

                lines.Add(string.Join(",", vals));
            }

            File.WriteAllLines(sfd.FileName, lines, System.Text.Encoding.UTF8);
            MessageBox.Show($"✔  Exported {_filteredData.Count} records.\n{sfd.FileName}", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string Q(string? s) => s != null && s.Contains(',') ? $"\"{s}\"" : (s ?? "");

        private static DataGridView BuildGrid()
        {
            var g = new DataGridView
            {
                Dock                = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows  = false,
                ReadOnly            = true,
                SelectionMode       = DataGridViewSelectionMode.CellSelect,
                ClipboardCopyMode   = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                BackgroundColor     = MainForm.C_Dark,
                GridColor           = MainForm.C_Border,
                RowHeadersVisible   = false,
                BorderStyle         = BorderStyle.None,
                RowTemplate         = { Height = 28 },
                DefaultCellStyle    = new DataGridViewCellStyle
                {
                    BackColor          = MainForm.C_Card,
                    ForeColor          = MainForm.C_Text1,
                    Font               = new Font("Consolas", 9),
                    SelectionBackColor = Color.FromArgb(40, 80, 150),
                    SelectionForeColor = MainForm.C_Text1,
                    Padding            = new Padding(4, 0, 4, 0)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor          = Color.FromArgb(17, 24, 39),
                    ForeColor          = MainForm.C_Text2,
                    Font               = new Font("Segoe UI", 9f, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(17, 24, 39),
                    Padding            = new Padding(4, 0, 4, 0)
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(16, 22, 36)
                },
                ColumnHeadersHeight = 36,
            };
            g.EnableHeadersVisualStyles = false;

            DataGridViewTextBoxColumn Col(string name, string header, int fw) =>
                new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = fw, MinimumWidth = 36 };

            g.Columns.Add(Col("colSr",      "#",            2));
            g.Columns.Add(Col("colMac",     "Parent Serial No", 15));
            g.Columns.Add(Col("colType",    "Part Type",   10));
            g.Columns.Add(Col("colQr",      "Part QR",     30));
            g.Columns.Add(Col("colStn",     "Station",     10));
            g.Columns.Add(Col("colOp",      "Operator",    10));
            g.Columns.Add(Col("colTs",      "Scanned At",  15));

            return g;
        }

        private static Label FilterLabel(string text) =>
            new Label
            {
                Text      = text,
                AutoSize  = false,
                Width     = text.Trim().EndsWith(":") ? 50 : 60,
                Height    = 36,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.C_Text2,
                TextAlign = ContentAlignment.MiddleRight,
                Margin    = new Padding(2, 4, 2, 0)
            };

        private static DateTimePicker DatePick(DateTime value) =>
            new DateTimePicker
            {
                Value  = value,
                Width  = 118,
                Format = DateTimePickerFormat.Short,
                Font   = new Font("Segoe UI", 9),
                Margin = new Padding(2, 6, 2, 0)
            };

        private static Button FilterBtn(string text, Color back)
        {
            var b = new Button
            {
                Text      = text,
                Width     = 96,
                Height    = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Margin    = new Padding(4, 8, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
    }
}
