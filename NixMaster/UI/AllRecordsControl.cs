using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NixMaster.Models;

namespace NixMaster.UI
{
    /// <summary>
    /// All Records screen — filterable table of every traceability record.
    /// Data is only fetched from Firebase when the user applies a date-range filter.
    /// </summary>
    public class AllRecordsControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;

        private DateTimePicker _dtFrom        = null!;
        private DateTimePicker _dtTo          = null!;
        private Button         _btnTabAll        = null!;
        private Button         _btnTabPacked     = null!;
        private Button         _btnTabTestedOk   = null!;
        private Button         _btnTabTestedNg   = null!;
        private Button         _btnTabReworked   = null!;
        private Button         _btnTabRca        = null!;
        private Button         _btnTabDispatched = null!;
        private string         _currentTab       = "All Records";
        private ComboBox       _cbShift       = null!;
        private DataGridView   _grid          = null!;
        private Label          _lblTotal      = null!;
        private Label          _lblStatusBar  = null!;
        private Panel          _pnlInstruct   = null!;

        private List<CombinedRecord> _allData      = new();
        private List<CombinedRecord> _filteredData = new();
        private bool _dataLoaded = false;

        public AllRecordsControl(MainForm host)
        {
            _host = host;
            Build();
            // Do NOT auto-load — wait for user to select a range and click Apply
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
                Text      = "📋  Product Traceability",
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

            flow.Controls.Add(FilterLabel("  Shift:"));
            _cbShift = FilterCombo(new[] { "All", "Morning", "Afternoon", "Night" }, 125);
            flow.Controls.Add(_cbShift);

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

            // ── Tabs Panel ─────────────────────────────────────────────────────────
            var pnlTabs = new FlowLayoutPanel
            {
                Dock      = DockStyle.Top,
                Height    = 84,
                BackColor = MainForm.C_Dark,
                Padding   = new Padding(24, 4, 16, 0)
            };
            _btnTabAll        = CreateTabBtn("All Records");
            _btnTabPacked     = CreateTabBtn("Packed");
            _btnTabTestedOk   = CreateTabBtn("Tested OK");
            _btnTabTestedNg   = CreateTabBtn("Tested NG");
            _btnTabReworked   = CreateTabBtn("Reworked");
            _btnTabRca        = CreateTabBtn("RCA Logged");
            _btnTabDispatched = CreateTabBtn("Dispatched");
            pnlTabs.Controls.AddRange(new Control[] { _btnTabAll, _btnTabPacked, _btnTabTestedOk, _btnTabTestedNg, _btnTabReworked, _btnTabRca, _btnTabDispatched });

            this.Controls.Add(_pnlInstruct);
            this.Controls.Add(_grid);
            this.Controls.Add(_lblStatusBar);
            this.Controls.Add(bar);
            this.Controls.Add(pnlTabs);
            this.Controls.Add(pnlHeader);
        }

        // ─── Instruction Panel ────────────────────────────────────────────────────
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
                Text      = "Search Production Records by Date Range",
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
                    "  3.  (Optional) Choose a specific Shift  (Morning / Afternoon / Night).\r\n\r\n" +
                    "  4.  Click the   🔍 Apply Filter   button.\r\n\r\n" +
                    "  5.  Use the category tabs (Packed / Tested OK / NG / Dispatched etc.)\r\n" +
                    "       to filter the loaded records by status.\r\n\r\n" +
                    "  Tip:  Keep the date range short (7 – 30 days) for faster loading\r\n" +
                    "         and lower Firebase data usage.",
                Font      = new Font("Segoe UI", 10),
                ForeColor = MainForm.C_Text2,
                AutoSize  = false,
                Width     = 580,
                Height    = 210,
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

        // ─── Fetch on Apply ───────────────────────────────────────────────────────
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

            _allData    = records;
            _dataLoaded = true;

            _pnlInstruct.Visible = false;
            _grid.Visible        = true;

            ApplyFilter();

            int days = (int)(to - from).TotalDays + 1;
            _lblStatusBar.Text      = $"✔  {_allData.Count} records loaded  ·  {from:dd MMM} – {to:dd MMM yyyy}  ({days} day{(days > 1 ? "s" : "")})  ·  {DateTime.Now:HH:mm:ss}";
            _lblStatusBar.ForeColor = MainForm.C_Green;
        }

        public void RefreshData()
        {
            if (_dataLoaded)
                _ = FetchAndApplyAsync();
        }

        public bool SupportsAutoRefresh => false; // Data loads on Apply Filter or manual Refresh only

        // ─── Tabs & Filter ────────────────────────────────────────────────────────
        private Button CreateTabBtn(string text)
        {
            var b = new Button
            {
                Text      = text,
                Height    = 34,
                Width     = 110,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Margin    = new Padding(0, 0, 8, 0),
                BackColor = text == _currentTab ? MainForm.C_Blue : MainForm.C_Sidebar,
                ForeColor = text == _currentTab ? Color.White : MainForm.C_Text2
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => SelectTab(text);
            return b;
        }

        private void SelectTab(string tabName)
        {
            _currentTab = tabName;
            Button[] btns = { _btnTabAll, _btnTabPacked, _btnTabTestedOk, _btnTabTestedNg, _btnTabReworked, _btnTabRca, _btnTabDispatched };
            foreach (var b in btns)
            {
                bool active = b.Text == tabName;
                b.BackColor = active ? MainForm.C_Blue : MainForm.C_Sidebar;
                b.ForeColor = active ? Color.White : MainForm.C_Text2;
            }
            if (_dataLoaded) ApplyFilter();
        }

        private void ApplyFilter()
        {
            string shift = _cbShift.Text;

            _filteredData = _allData.Where(r =>
            {
                if (shift != "All" && !string.Equals(r.Assembly?.Shift ?? "", shift, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (_currentTab == "Packed"      && !r.IsPacked)  return false;
                if (_currentTab == "Tested OK"   && (!r.IsTested || r.IsTestingNG)) return false;
                if (_currentTab == "Tested NG"   && (!r.IsTested || !r.IsTestingNG)) return false;
                if (_currentTab == "Reworked"    && !r.IsRework)  return false;
                if (_currentTab == "RCA Logged"  && !r.IsRcaCompleted) return false;
                if (_currentTab == "Dispatched"  && !r.IsDispatched) return false;
                return true;
            }).ToList();

            PopulateGrid(_filteredData);
            _lblTotal.Text = $"Records: {_filteredData.Count}";
        }

        private void ClearFilter()
        {
            _dtFrom.Value          = DateTime.Today.AddDays(-7);
            _dtTo.Value            = DateTime.Today;
            _cbShift.SelectedIndex = 0;
            _currentTab            = "All Records";

            Button[] btns = { _btnTabAll, _btnTabPacked, _btnTabTestedOk, _btnTabTestedNg, _btnTabReworked, _btnTabRca, _btnTabDispatched };
            foreach (var b in btns)
            {
                b.BackColor = b.Text == "All Records" ? MainForm.C_Blue : MainForm.C_Sidebar;
                b.ForeColor = b.Text == "All Records" ? Color.White : MainForm.C_Text2;
            }

            _allData.Clear();
            _filteredData.Clear();
            _dataLoaded          = false;
            _grid.Visible        = false;
            _pnlInstruct.Visible = true;
            _lblTotal.Text       = "";
            _lblStatusBar.Text   = "";
        }

        // ─── Grid populate ────────────────────────────────────────────────────────
        private void PopulateGrid(List<CombinedRecord> data)
        {
            _grid.Rows.Clear();
            int sr = 1;
            foreach (var r in data)
            {
                var a   = r.Assembly;
                var t   = r.Testing;
                var p   = r.Packing;

                _grid.Rows.Add(
                    sr++,
                    r.MacId,
                    a?.StationName  ?? "—",
                    a?.Timestamp    ?? "—",
                    a?.Operator     ?? "—",
                    a?.Parts?.Count ?? 0,
                    a?.Parts != null ? string.Join("; ", a.Parts.Select(kv => $"{(kv.Key.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) ? "Decision" : kv.Key)}:{kv.Value}")) : "",
                    GetRemarks(a),
                    t == null ? "—" : (r.IsTestingNG ? "❌ NG" : "✅ OK"),
                    t?.DeviceSerialNo ?? "—",
                    t?.TestingQR ?? "—",
                    string.IsNullOrEmpty(t?.DefectDetails) ? "—" : t.DefectDetails,
                    t?.TestedAt ?? "—",
                    p?.BoxNo        ?? "—",
                    p?.LongQR       ?? "—",
                    p?.ShortQR      ?? "—",
                    p?.PackedAt     ?? "—",
                    p?.PackedBy     ?? "—",
                    p?.StationName  ?? "—",
                    r.IsDispatched ? $"✅ {r.Dispatch?.DispatchDate}" : "—",
                    r.IsRcaCompleted ? "Yes" : "No",
                    r.IsPacked ? "✔  Complete" : (r.IsTestingNG ? "❌ Test NG" : (r.IsRework ? "🛠  Rework" : "⏳  In-Progress"))
                );

                var row = _grid.Rows[_grid.Rows.Count - 1];
                row.Cells["colStatus"].Style.ForeColor = r.IsPacked
                    ? MainForm.C_Green
                    : (r.IsTestingNG ? MainForm.C_Red : MainForm.C_Orange);
            }
        }

        // ─── CSV Export ───────────────────────────────────────────────────────────
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
                FileName = $"NixMaster_Records_{_dtFrom.Value:yyyyMMdd}_to_{_dtTo.Value:yyyyMMdd}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var lines = new List<string>
            {
                "Sr,Serial_No,Asm_Station,Asm_Time,Operator,Parts_Count,Parts_Detail,Decision," +
                "Test_Status,Device_SN,Test_QR,Test_Defect,Tested_At,Box_No,Long_QR,Short_QR," +
                "Packed_At,Packed_By,Pack_Station,Dispatch_Status,RCA_Done,Status"
            };

            int sr = 1;
            foreach (var r in _filteredData)
            {
                var a   = r.Assembly;
                var t   = r.Testing;
                var p   = r.Packing;
                lines.Add(string.Join(",",
                    sr++,
                    Q(r.MacId),
                    Q(a?.StationName  ?? ""),
                    Q(a?.Timestamp    ?? ""),
                    Q(a?.Operator     ?? ""),
                    a?.Parts?.Count ?? 0,
                    Q(a?.Parts != null ? string.Join("; ", a.Parts.Select(kv => $"{(kv.Key.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) ? "Decision" : kv.Key)}:{kv.Value}")) : ""),
                    Q(GetRemarks(a) == "—" ? "" : GetRemarks(a)),
                    Q(t == null ? "" : (r.IsTestingNG ? "NG" : "OK")),
                    Q(t?.DeviceSerialNo ?? ""),
                    Q(t?.TestingQR ?? ""),
                    Q(t?.DefectDetails ?? ""),
                    Q(t?.TestedAt ?? ""),
                    Q(p?.BoxNo        ?? ""),
                    Q(p?.LongQR       ?? ""),
                    Q(p?.ShortQR      ?? ""),
                    Q(p?.PackedAt     ?? ""),
                    Q(p?.PackedBy     ?? ""),
                    Q(p?.StationName  ?? ""),
                    r.IsDispatched ? "Yes" : "No",
                    r.IsRcaCompleted ? "Yes" : "No",
                    Q(r.IsPacked ? "Complete" : (r.IsTestingNG ? "Test NG" : "In-Progress"))
                ));
            }

            File.WriteAllLines(sfd.FileName, lines, System.Text.Encoding.UTF8);
            MessageBox.Show($"✔  Exported {_filteredData.Count} records.\n{sfd.FileName}", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string GetRemarks(AssemblyRecord? a)
        {
            if (a?.Parts == null) return "—";
            var k = a.Parts.Keys.FirstOrDefault(x => x.IndexOf("remark", StringComparison.OrdinalIgnoreCase) >= 0);
            return k != null ? a.Parts[k] : "—";
        }

        private static string Q(string? s) => s != null && s.Contains(',') ? $"\"{s}\"" : (s ?? "");

        // ─── Grid builder ─────────────────────────────────────────────────────────
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
                RowTemplate         = { Height = 26 },
                DefaultCellStyle    = new DataGridViewCellStyle
                {
                    BackColor          = MainForm.C_Card,
                    ForeColor          = MainForm.C_Text1,
                    Font               = new Font("Consolas", 8),
                    SelectionBackColor = Color.FromArgb(40, 80, 150),
                    SelectionForeColor = MainForm.C_Text1,
                    Padding            = new Padding(3, 0, 3, 0)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor          = Color.FromArgb(17, 24, 39),
                    ForeColor          = MainForm.C_Text2,
                    Font               = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(17, 24, 39),
                    Padding            = new Padding(4, 0, 4, 0)
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(16, 22, 36)
                },
                ColumnHeadersHeight = 32,
            };
            g.EnableHeadersVisualStyles = false;

            DataGridViewTextBoxColumn Col(string name, string header, int fw) =>
                new DataGridViewTextBoxColumn { Name = name, HeaderText = header, FillWeight = fw, MinimumWidth = 36 };

            g.Columns.Add(Col("colSr",      "#",            2));
            g.Columns.Add(Col("colMac",     "Serial No",   13));
            g.Columns.Add(Col("colAsmStn",  "Asm. Station", 9));
            g.Columns.Add(Col("colAsmTs",   "Asm. Time",   12));
            g.Columns.Add(Col("colOp",      "Operator",     8));
            g.Columns.Add(Col("colPCnt",    "Parts",        3));
            g.Columns.Add(Col("colParts",   "Parts Detail",16));
            g.Columns.Add(Col("colDecision", "Decision",     10));
            g.Columns.Add(Col("colTestOk",  "Test Stat",    6));
            g.Columns.Add(Col("colSerial",  "Device SN",   12));
            g.Columns.Add(Col("colTestQr",  "Test QR",      8));
            g.Columns.Add(Col("colTestD",   "Defect",       8));
            g.Columns.Add(Col("colTestTs",  "Tested At",   12));
            g.Columns.Add(Col("colBox",     "Box",          4));
            g.Columns.Add(Col("colLqr",     "Long QR",     12));
            g.Columns.Add(Col("colSqr",     "Short QR",     8));
            g.Columns.Add(Col("colPackTs",  "Packed At",   12));
            g.Columns.Add(Col("colPackBy",  "Packed By",    8));
            g.Columns.Add(Col("colPStn",    "Pack Stn",     7));
            g.Columns.Add(Col("colDisp",    "Dispatch",     8));
            g.Columns.Add(Col("colRca",     "RCA Logged",   7));
            g.Columns.Add(Col("colStatus",  "Status",       8));

            return g;
        }

        // ─── Filter control factories ─────────────────────────────────────────────
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

        private static ComboBox FilterCombo(string[] items, int width)
        {
            var cb = new ComboBox
            {
                Width         = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font          = new Font("Segoe UI", 9),
                Margin        = new Padding(2, 6, 2, 0)
            };
            cb.Items.AddRange(items);
            cb.SelectedIndex = 0;
            return cb;
        }

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
