using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NixMaster.Core;
using NixMaster.Models;

namespace NixMaster.UI
{
    public class SubAssyControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;

        private Label  _lblStatus  = null!;
        private Button _btnRefresh = null!;

        private ComboBox _cboProduct = null!;
        private ComboBox _cboMonth = null!;
        private ComboBox _cboYear = null!;
        private Label _lblTotalCount = null!;

        private TableLayoutPanel _tlpCalendar = null!;
        
        // Cache the fetched data
        private Dictionary<string, List<SubAssyRecord>> _cachedData = new Dictionary<string, List<SubAssyRecord>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DailyTargets> _dailyPlans = new Dictionary<string, DailyTargets>(StringComparer.OrdinalIgnoreCase);

        public SubAssyControl(MainForm host)
        {
            _host = host;
            Build();
            this.Load += (_, __) => 
            {
                // Init combobox defaults
                _cboMonth.SelectedIndex = DateTime.Now.Month - 1;
                var currentYear = DateTime.Now.Year.ToString();
                if (_cboYear.Items.Contains(currentYear))
                    _cboYear.SelectedItem = currentYear;
                if (_cboProduct.Items.Count > 0)
                    _cboProduct.SelectedIndex = 0;

                LoadDataAsync();
            };
        }

        private void Build()
        {
            this.BackColor = MainForm.C_Dark;
            this.Padding   = new Padding(0);

            // ── Header bar ────────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 100, // Taller to fit controls
                BackColor = MainForm.C_Dark,
                Padding   = new Padding(24, 0, 16, 0)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            var lblTitle = new Label
            {
                Text      = "🧩  Sub-Assemblies Output",
                Font      = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(24, 18)
            };

            _btnRefresh = new Button
            {
                Text      = "↻  Refresh",
                Height    = 36,
                Width     = 138,
                FlatStyle = FlatStyle.Flat,
                BackColor = MainForm.C_Blue,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
                Location  = new Point(pnlHeader.Width - 148, 16)
            };
            _btnRefresh.FlatAppearance.BorderSize = 0;
            _btnRefresh.Click += (_, __) => LoadDataAsync();

            // Filters
            int fy = 55;
            int fx = 24;

            pnlHeader.Controls.Add(MakeLabel("Product:", fx, fy));
            _cboProduct = MakeCombo(fx + 65, fy, 250);
            _cboProduct.Items.AddRange(AppState.Settings.SubAssyProducts.ToArray());
            _cboProduct.SelectedIndexChanged += FilterChanged;

            fx += 330;
            pnlHeader.Controls.Add(MakeLabel("Month:", fx, fy));
            _cboMonth = MakeCombo(fx + 55, fy, 120);
            _cboMonth.Items.AddRange(new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" });
            _cboMonth.SelectedIndexChanged += FilterChanged;

            fx += 190;
            pnlHeader.Controls.Add(MakeLabel("Year:", fx, fy));
            _cboYear = MakeCombo(fx + 45, fy, 100);
            int currentY = DateTime.Now.Year;
            for (int i = 2024; i <= currentY + 2; i++) _cboYear.Items.Add(i.ToString());
            _cboYear.SelectedIndexChanged += FilterChanged;

            fx += 165;
            _lblTotalCount = new Label
            {
                Text = "Total: 0",
                Location = new Point(fx, fy + 2),
                AutoSize = true,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = MainForm.C_Cyan
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(_cboProduct);
            pnlHeader.Controls.Add(_cboMonth);
            pnlHeader.Controls.Add(_cboYear);
            pnlHeader.Controls.Add(_lblTotalCount);
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

            // ── Main Content Area ─────────────────────────────────────────────────
            var flow = new Panel
            {
                Dock          = DockStyle.Fill,
                AutoScroll    = true,
                Padding       = new Padding(24, 24, 24, 24),
                BackColor     = MainForm.C_Dark
            };

            _tlpCalendar = new TableLayoutPanel
            {
                ColumnCount = 7,
                RowCount = 6,
                Dock = DockStyle.Top,
                Height = 600,
                BackColor = Color.Transparent
            };
            // Set styles for days
            for (int i=0; i<7; i++) _tlpCalendar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));
            for (int i=0; i<6; i++) _tlpCalendar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6f));

            // Weekday Headers
            var pnlDays = new TableLayoutPanel
            {
                ColumnCount = 7,
                RowCount = 1,
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.Transparent,
                Margin = new Padding(0,0,0,10)
            };
            string[] days = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int i=0; i<7; i++) 
            {
                pnlDays.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));
                pnlDays.Controls.Add(new Label { Text = days[i], Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = MainForm.C_Text2, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill }, i, 0);
            }

            var calContainer = new Panel { Dock = DockStyle.Top, Height = 650, Padding = new Padding(0,0,0,24) };
            calContainer.Controls.Add(_tlpCalendar);
            calContainer.Controls.Add(pnlDays);

            flow.Controls.Add(calContainer);
            
            this.Controls.Add(flow);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(pnlHeader);
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y + 4),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = MainForm.C_Text2
            };
        }

        private ComboBox MakeCombo(int x, int y, int w)
        {
            return new ComboBox
            {
                Location = new Point(x, y),
                Width = w,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(10, 15, 25),
                ForeColor = MainForm.C_Text1,
                FlatStyle = FlatStyle.Flat
            };
        }

        private void FilterChanged(object? sender, EventArgs e)
        {
            RenderCalendar();
        }

        private async void LoadDataAsync()
        {
            _lblStatus.Text      = "⏳  Fetching Sub-Assembly data…";
            _lblStatus.ForeColor = MainForm.C_Text2;
            _btnRefresh.Enabled  = false;

            var tStats = _host.Firebase.FetchRawSubAssyStatsAsync();
            var tPlan = _host.Firebase.FetchDailyPlanAsync();

            await Task.WhenAll(tStats, tPlan);

            var (dict, err) = tStats.Result;

            if (!string.IsNullOrEmpty(err))
            {
                _lblStatus.Text      = $"⚠  {err}";
                _lblStatus.ForeColor = MainForm.C_Red;
                _btnRefresh.Enabled  = true;
                return;
            }

            _cachedData = dict;
            _dailyPlans = tPlan.Result;

            _lblStatus.Text      = $"✔  Data synced  ·  {DateTime.Now:HH:mm:ss}";
            _lblStatus.ForeColor = MainForm.C_Green;
            _btnRefresh.Enabled  = true;

            RenderCalendar();
        }

        private void RenderCalendar()
        {
            _tlpCalendar.SuspendLayout();
            _tlpCalendar.Controls.Clear();

            if (_cboMonth.SelectedIndex < 0 || _cboYear.SelectedIndex < 0 || _cboProduct.SelectedIndex < 0)
            {
                _tlpCalendar.ResumeLayout();
                return;
            }

            int month = _cboMonth.SelectedIndex + 1;
            int year = int.Parse(_cboYear.SelectedItem!.ToString()!);
            string product = _cboProduct.SelectedItem!.ToString()!;

            int daysInMonth = DateTime.DaysInMonth(year, month);
            DateTime firstDay = new DateTime(year, month, 1);
            int startDayOfWeek = (int)firstDay.DayOfWeek; // 0 = Sunday

            int totalMonthCount = 0;

            // Get counts for each day
            var dailyCounts = new int[daysInMonth + 1];
            if (_cachedData.TryGetValue(product, out var records))
            {
                foreach (var rec in records)
                {
                    if (string.IsNullOrEmpty(rec.ScannedAt)) continue;
                    if (DateTime.TryParse(rec.ScannedAt, out var dt))
                    {
                        if (dt.Year == year && dt.Month == month)
                        {
                            dailyCounts[dt.Day]++;
                            totalMonthCount++;
                        }
                    }
                }
            }

            _lblTotalCount.Text = $"Total: {totalMonthCount}";

            int row = 0;
            int col = startDayOfWeek;

            // Empty slots before first day
            for (int i = 0; i < startDayOfWeek; i++)
            {
                _tlpCalendar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, i, 0);
            }

            for (int day = 1; day <= daysInMonth; day++)
            {
                string dateKey = $"{year}-{month:D2}-{day:D2}";
                int targetCount = 0;
                
                if (_dailyPlans.TryGetValue(dateKey, out var dailyTarget))
                {
                    if (dailyTarget.SubAssyTargets.TryGetValue(product, out int st))
                    {
                        targetCount = st;
                    }
                }

                var pnlDay = BuildDayBox(day, dailyCounts[day], targetCount);
                _tlpCalendar.Controls.Add(pnlDay, col, row);

                col++;
                if (col > 6)
                {
                    col = 0;
                    row++;
                }
            }

            _tlpCalendar.ResumeLayout();
        }

        private Panel BuildDayBox(int day, int actualCount, int targetCount)
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4)
            };
            
            Color boxColor = MainForm.C_Card;
            Color accentColor = Color.Transparent;
            Color txtColor = MainForm.C_Text3;

            if (targetCount > 0)
            {
                if (actualCount == targetCount)
                {
                    accentColor = MainForm.C_Green;
                    boxColor = Color.FromArgb(20, 50, 30);
                    txtColor = MainForm.C_Text1;
                }
                else if (actualCount < targetCount)
                {
                    accentColor = MainForm.C_Red;
                    boxColor = Color.FromArgb(60, 25, 25);
                    txtColor = MainForm.C_Text1;
                }
                else if (actualCount > targetCount)
                {
                    accentColor = MainForm.C_Orange;
                    boxColor = Color.FromArgb(60, 45, 15);
                    txtColor = MainForm.C_Text1;
                }
            }
            else if (actualCount > 0)
            {
                accentColor = MainForm.C_Cyan;
                txtColor = MainForm.C_Cyan;
            }

            pnl.BackColor = boxColor;

            pnl.Paint += (s, e) =>
            {
                if (accentColor != Color.Transparent)
                {
                    using var br = new SolidBrush(accentColor);
                    e.Graphics.FillRectangle(br, 0, 0, pnl.Width, 3);
                }
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, pnl.Width - 1, pnl.Height - 1);
            };

            var lblDay = new Label
            {
                Text = day.ToString(),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.C_Text2,
                AutoSize = false,
                Height = 24,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(6, 6, 0, 0),
                BackColor = Color.Transparent
            };

            var lblCount = new Label
            {
                Text = actualCount.ToString(),
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = txtColor,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            
            if (targetCount > 0)
            {
                var lblPlan = new Label
                {
                    Text = $"Plan: {targetCount}",
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = MainForm.C_Text2,
                    AutoSize = true,
                    BackColor = Color.Transparent
                };
                
                pnl.Resize += (s, e) =>
                {
                    lblPlan.Location = new Point(pnl.Width - lblPlan.Width - 4, pnl.Height - lblPlan.Height - 4);
                };
                
                pnl.Controls.Add(lblPlan); // Added first = highest Z-order
            }

            pnl.Controls.Add(lblCount);
            pnl.Controls.Add(lblDay);

            return pnl;
        }

        public void RefreshData() => LoadDataAsync();

        public bool SupportsAutoRefresh => false; // Loads on open + manual Refresh only
    }
}
