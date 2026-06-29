using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NixMaster.Models;
using ScottPlot.WinForms;
using ScottPlot;
using Color = System.Drawing.Color;

namespace NixMaster.UI
{
    public class DashboardControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;

        // Header controls
        private ComboBox _cmbLine = null!;
        private Button _btnRefresh = null!;
        private Label _lblStatus = null!;

        // KPI labels
        private Label _valProdQty = null!;
        private Label _valGoodQty = null!;
        private Label _valOee = null!;
        private Label _valDowntime = null!;
        private Label _valCycleTime = null!;
        
        // KPI sub-labels
        private Label _subProdQty = null!;
        private Label _subGoodQty = null!;
        private Label _subOee = null!;
        private Label _subDowntime = null!;
        private Label _subCycleTime = null!;

        // Charts
        private FormsPlot _chartDayWise = null!;
        private FormsPlot _chartStationWise = null!;

        private List<CombinedRecord> _currentData = new();
        private List<CombinedRecord> _filteredData = new();

        public DashboardControl(MainForm host)
        {
            _host = host;
            Build();
            this.Load += (_, __) => LoadDataAsync();
        }

        private void Build()
        {
            this.BackColor = MainForm.C_Dark;
            this.Padding = new Padding(0);

            // ── Header bar ────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = MainForm.C_Dark,
                Padding = new Padding(24, 0, 16, 0)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            var lblTitle = new Label
            {
                Text = "📊  Production Dashboard",
                Font = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize = true,
                Location = new Point(24, 18)
            };

            var lblLine = new Label
            {
                Text = "Product Line:",
                Font = new Font("Segoe UI", 10),
                ForeColor = MainForm.C_Text2,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(pnlHeader.Width - 400, 22)
            };

            _cmbLine = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                BackColor = MainForm.C_Card,
                ForeColor = MainForm.C_Text1,
                Width = 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(pnlHeader.Width - 300, 18)
            };
            _cmbLine.Items.Add("All Lines");
            _cmbLine.SelectedIndex = 0;
            _cmbLine.SelectedIndexChanged += (_, __) => ApplyFilterAndRefreshUI();

            _btnRefresh = ActionBtn("↻  Refresh", MainForm.C_Blue);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Location = new Point(pnlHeader.Width - 138, 16);
            _btnRefresh.Click += (_, __) => LoadDataAsync();

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblLine);
            pnlHeader.Controls.Add(_cmbLine);
            pnlHeader.Controls.Add(_btnRefresh);

            pnlHeader.Resize += (_, __) =>
            {
                lblLine.Left = pnlHeader.Width - 400;
                _cmbLine.Left = pnlHeader.Width - 300;
                _btnRefresh.Left = pnlHeader.Width - 138;
            };

            _lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "Loading…",
                Font = new Font("Segoe UI", 9),
                ForeColor = MainForm.C_Text2,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(24, 0, 0, 0),
                BackColor = Color.Transparent
            };

            // ── Main Layout ─────────────────────────────────────
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = MainForm.C_Border,
                Margin = new Padding(0)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75f));

            // Left Panel (KPIs)
            var pnlKpi = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(16),
                AutoScroll = true,
                BackColor = MainForm.C_Dark
            };

            pnlKpi.Resize += (s, e) =>
            {
                foreach (Control c in pnlKpi.Controls)
                {
                    c.Width = pnlKpi.ClientSize.Width - pnlKpi.Padding.Left - pnlKpi.Padding.Right - c.Margin.Left - c.Margin.Right;
                }
            };

            (_valProdQty, _subProdQty, var c1) = AdvKpiCard("Production QTY (AC)");
            (_valGoodQty, _subGoodQty, var c2) = AdvKpiCard("Good Production QTY");
            (_valOee, _subOee, var c3) = AdvKpiCard("OEE (Overall Equipment Effectiveness)");
            (_valDowntime, _subDowntime, var c4) = AdvKpiCard("Downtime");
            (_valCycleTime, _subCycleTime, var c5) = AdvKpiCard("Cycle Time");

            pnlKpi.Controls.AddRange(new Control[] { c1, c2, c3, c4, c5 });
            mainLayout.Controls.Add(pnlKpi, 0, 0);

            // Right Panel (Charts)
            var pnlCharts = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = MainForm.C_Dark
            };
            pnlCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            pnlCharts.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _chartDayWise = new FormsPlot { Dock = DockStyle.Fill, Margin = new Padding(16, 16, 16, 8) };
            _chartStationWise = new FormsPlot { Dock = DockStyle.Fill, Margin = new Padding(16, 8, 16, 16) };

            pnlCharts.Controls.Add(_chartDayWise, 0, 0);
            pnlCharts.Controls.Add(_chartStationWise, 0, 1);
            mainLayout.Controls.Add(pnlCharts, 1, 0);

            this.Controls.Add(mainLayout);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(pnlHeader);
        }

        private async void LoadDataAsync()
        {
            _lblStatus.Text = "⏳  Fetching from Firebase…";
            _lblStatus.ForeColor = MainForm.C_Text2;
            _btnRefresh.Enabled = false;

            var (records, error) = await _host.Firebase.FetchAllAsync();
            _host.SetOnlineStatus(_host.Firebase.IsOnline);

            if (!string.IsNullOrEmpty(error))
            {
                _lblStatus.Text = $"⚠  {error}";
                _lblStatus.ForeColor = MainForm.C_Red;
                _btnRefresh.Enabled = true;
                return;
            }

            var now = DateTime.Now;
            _currentData = records.Where(r => 
            {
                if (DateTime.TryParse(r.Assembly?.Timestamp ?? "", out var asmTs))
                    return asmTs.Year == now.Year && asmTs.Month == now.Month;
                return false;
            }).ToList();

            // Populate Line Selector (Future ready)
            var lines = _currentData
                .Where(r => !string.IsNullOrEmpty(r.Assembly?.StationName))
                .Select(r => r.Assembly!.StationName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var currentSelection = _cmbLine.SelectedItem?.ToString();
            _cmbLine.Items.Clear();
            _cmbLine.Items.Add("All Lines");
            foreach (var line in lines) _cmbLine.Items.Add(line);
            
            if (currentSelection != null && _cmbLine.Items.Contains(currentSelection))
                _cmbLine.SelectedItem = currentSelection;
            else
                _cmbLine.SelectedIndex = 0;

            ApplyFilterAndRefreshUI();

            _lblStatus.Text = $"✔  {_currentData.Count} records loaded (Current Month)  ·  {now:HH:mm:ss}";
            _lblStatus.ForeColor = MainForm.C_Green;
            _btnRefresh.Enabled = true;
        }

        private void ApplyFilterAndRefreshUI()
        {
            if (_cmbLine.SelectedItem == null) return;
            string sel = _cmbLine.SelectedItem.ToString()!;
            
            if (sel == "All Lines")
                _filteredData = _currentData.ToList();
            else
                _filteredData = _currentData.Where(r => r.Assembly?.StationName == sel).ToList();

            PopulateKpis(_filteredData);
            UpdateCharts(_filteredData);
        }

        public void RefreshData() => LoadDataAsync();

        public bool SupportsAutoRefresh => false;

        private void PopulateKpis(List<CombinedRecord> data)
        {
            int total = data.Count;
            int defects = data.Count(r => r.IsTestingNG);
            int good = total - defects;

            _valProdQty.Text = total >= 1000 ? $"{total / 1000.0:F1}k" : total.ToString();
            
            int planned = (int)(total * 1.1); // Dummy planned until integrated with DailyTargets
            if (planned == 0) planned = 1;
            double actualPct = (double)(total - planned) / planned * 100.0;
            string arr = actualPct < 0 ? "↓" : "↑";
            _subProdQty.Text = $"Planned Production: {planned} | vs Actual: {actualPct:F1}% {arr}";
            _subProdQty.ForeColor = actualPct < 0 ? MainForm.C_Red : MainForm.C_Green;

            _valGoodQty.Text = good >= 1000 ? $"{good / 1000.0:F1}k" : good.ToString();
            double defectRate = total > 0 ? (double)defects / total * 100.0 : 0;
            _subGoodQty.Text = $"Defect QTY: {defects} | Defect Rate: {defectRate:F1}%";

            // Calculate Downtime & Cycle Time based on 1 hour gaps
            double totalShiftHours = 0;
            double totalDowntimeHours = 0;
            
            // Group by day to find gaps
            var byDay = data
                .Where(r => !string.IsNullOrEmpty(r.Assembly?.Timestamp))
                .Select(r => DateTime.Parse(r.Assembly!.Timestamp))
                .GroupBy(d => d.Date)
                .ToList();

            foreach (var dayGroup in byDay)
            {
                var times = dayGroup.OrderBy(t => t).ToList();
                if (times.Count < 2) continue;

                totalShiftHours += (times.Last() - times.First()).TotalHours;
                
                for (int i = 1; i < times.Count; i++)
                {
                    double gap = (times[i] - times[i - 1]).TotalHours;
                    if (gap >= 1.0) // 1 hour threshold for downtime
                    {
                        totalDowntimeHours += gap;
                    }
                }
            }

            if (totalShiftHours == 0) totalShiftHours = 8.0; // Default 8 hrs
            double availableHours = Math.Max(0, totalShiftHours - totalDowntimeHours);
            
            _valDowntime.Text = $"{totalDowntimeHours:F1} hours";
            _subDowntime.Text = $"Total Span: {totalShiftHours:F1} hours\nMachine Available: {availableHours:F1} hours";

            double cycleTimeMin = total > 0 ? (availableHours * 60.0) / total : 0;
            _valCycleTime.Text = $"{cycleTimeMin:F1} Minutes";
            _subCycleTime.Text = "";

            // OEE
            double availability = totalShiftHours > 0 ? availableHours / totalShiftHours : 0;
            double idealCycleTimeMin = 3.0; // Assume 3 min ideal
            double performance = cycleTimeMin > 0 ? idealCycleTimeMin / cycleTimeMin : 0;
            if (performance > 1.0) performance = 1.0;
            double quality = total > 0 ? (double)good / total : 0;
            double oee = availability * performance * quality * 100.0;

            _valOee.Text = $"{oee:F1}%";
            _subOee.Text = $"Availability %: {availability*100:F1}%\nPerformance %: {performance*100:F1}%\nQuality %: {quality*100:F1}%";
        }

        private void UpdateCharts(List<CombinedRecord> data)
        {
            _chartDayWise.Plot.Clear();
            _chartStationWise.Plot.Clear();

            // 1. Day wise Production
            _chartDayWise.Plot.Title("Day wise Production Performance Overview");
            
            var now = DateTime.Now;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var dayCounts = new double[daysInMonth];
            var dayLabels = new string[daysInMonth];
            double[] tickPositions = new double[daysInMonth];
            
            for(int i = 0; i < daysInMonth; i++)
            {
                dayLabels[i] = (i + 1).ToString();
                tickPositions[i] = i;
            }

            foreach (var r in data)
            {
                if (DateTime.TryParse(r.Assembly?.Timestamp ?? "", out var ts) && ts.Month == now.Month)
                {
                    dayCounts[ts.Day - 1]++;
                }
            }

            var barPlot = _chartDayWise.Plot.AddBar(dayCounts);
            barPlot.FillColor = Color.FromArgb(200, 60, 60); // Red-ish like mockup
            barPlot.ShowValuesAboveBars = true;
            barPlot.ValueFormatter = y => y > 0 ? y.ToString("N0") : "";
            barPlot.Font.Color = MainForm.C_Text1;
            barPlot.Font.Size = 12;
            barPlot.Font.Bold = true;
            
            double target = dayCounts.Length > 0 && dayCounts.Max() > 0 ? dayCounts.Max() * 1.1 : 100;
            var targetLine = _chartDayWise.Plot.AddHorizontalLine(target);
            targetLine.Color = Color.DodgerBlue;
            targetLine.LineWidth = 2;
            targetLine.LineStyle = ScottPlot.LineStyle.Dash;
            
            _chartDayWise.Plot.SetAxisLimits(yMin: 0, yMax: target * 1.2);
            _chartDayWise.Plot.XTicks(tickPositions, dayLabels);
            _chartDayWise.Plot.Style(figureBackground: MainForm.C_Dark, dataBackground: MainForm.C_Card);
            _chartDayWise.Plot.XAxis.Color(MainForm.C_Text2);
            _chartDayWise.Plot.YAxis.Color(MainForm.C_Text2);
            _chartDayWise.Plot.XAxis.TickLabelStyle(color: MainForm.C_Text2);
            _chartDayWise.Plot.YAxis.TickLabelStyle(color: MainForm.C_Text2);
            _chartDayWise.Plot.Grid(color: Color.FromArgb(40, 45, 60));
            _chartDayWise.Plot.Title("Day wise Production Performance Overview", color: MainForm.C_Text1);
            _chartDayWise.Refresh();

            // 2. Station wise Production
            
            var stations = new List<string> { "Packing", "Testing", "Main Assembly", "Camera Assy", "IR Sub Assy" };
            var goodCounts = new double[stations.Count];
            var ngCounts = new double[stations.Count];

            foreach (var r in data)
            {
                if (r.Packing != null) { goodCounts[0]++; } 
                if (r.Testing != null) { if (r.IsTestingNG) ngCounts[1]++; else goodCounts[1]++; }
                if (r.Assembly != null) goodCounts[2]++;
                if (r.CamSubAssy != null) goodCounts[3]++;
                if (r.IrSubAssy != null) goodCounts[4]++;
            }

            double[] stationTicks = new double[stations.Count];
            for (int i = 0; i < stations.Count; i++) stationTicks[i] = i;

            var barGood = _chartStationWise.Plot.AddBar(goodCounts);
            barGood.FillColor = Color.YellowGreen;
            barGood.ShowValuesAboveBars = true;
            barGood.ValueFormatter = y => y > 0 ? y.ToString("N0") : "";
            barGood.Font.Color = MainForm.C_Text1;
            barGood.Font.Size = 12;
            barGood.Font.Bold = true;
            barGood.BarWidth = 0.6;
            
            var barNg = _chartStationWise.Plot.AddBar(ngCounts);
            barNg.FillColor = Color.Red;
            barNg.ValueOffsets = goodCounts;
            barNg.ShowValuesAboveBars = true;
            barNg.ValueFormatter = y => y > 0 ? y.ToString("N0") : "";
            barNg.Font.Color = MainForm.C_Text1;
            barNg.Font.Size = 12;
            barNg.Font.Bold = true;
            barNg.BarWidth = 0.6;

            double maxStationCount = goodCounts.Zip(ngCounts, (g, n) => g + n).DefaultIfEmpty(100).Max();
            _chartStationWise.Plot.SetAxisLimits(yMin: 0, yMax: maxStationCount * 1.2);
            _chartStationWise.Plot.XTicks(stationTicks, stations.ToArray());
            _chartStationWise.Plot.Style(figureBackground: MainForm.C_Dark, dataBackground: MainForm.C_Card);
            _chartStationWise.Plot.XAxis.Color(MainForm.C_Text2);
            _chartStationWise.Plot.YAxis.Color(MainForm.C_Text2);
            _chartStationWise.Plot.XAxis.TickLabelStyle(color: MainForm.C_Text2);
            _chartStationWise.Plot.YAxis.TickLabelStyle(color: MainForm.C_Text2);
            _chartStationWise.Plot.Grid(color: Color.FromArgb(40, 45, 60));
            _chartStationWise.Plot.Title("Station wise Production Performance Overview", color: MainForm.C_Text1);
            _chartStationWise.Refresh();
        }

        private static Button ActionBtn(string text, Color back)
        {
            var b = new Button
            {
                Text = text,
                Height = 36,
                Width = 138,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static (Label val, Label sub, Panel card) AdvKpiCard(string title)
        {
            var card = new Panel
            {
                Height = 110,
                BackColor = MainForm.C_Card,
                Margin = new Padding(0, 0, 0, 16),
            };

            var lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = MainForm.C_Text2,
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 8, 0, 0)
            };

            var lblVal = new Label
            {
                Text = "0",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var lblSub = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = MainForm.C_Text3,
                Dock = DockStyle.Bottom,
                Height = 36,
                TextAlign = ContentAlignment.TopCenter,
            };

            card.Controls.Add(lblVal);
            card.Controls.Add(lblTitle);
            card.Controls.Add(lblSub);
            
            lblVal.BringToFront();

            return (lblVal, lblSub, card);
        }
    }
}
