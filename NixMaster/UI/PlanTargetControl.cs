using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClosedXML.Excel;
using ExcelDataReader;
using NixMaster.Core;
using NixMaster.Models;

namespace NixMaster.UI
{
    public class PlanTargetControl : UserControl, IDashboardRefreshable
    {
        private readonly MainForm _host;

        private Label      _lblStatus  = null!;
        private Button     _btnRefresh = null!;
        private Button     _btnUpload  = null!;
        private Button     _btnDownload = null!;
        private Panel      _listPanel   = null!;
        private Panel      _pnlColHeader = null!;

        private class PlanRow
        {
            public DateTime Date { get; set; }
            public DailyTargets Targets { get; set; } = new();
        }

        private List<PlanRow> _rows = new();

        public PlanTargetControl(MainForm host)
        {
            _host = host;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
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
                Text      = "📅  Plan / Target",
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

            // ── Action Card ───────────────────────────────────────────────────
            var actionCard = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 80,
                BackColor = MainForm.C_Card,
                Padding   = new Padding(20, 16, 20, 16)
            };
            actionCard.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, actionCard.Width - 1, actionCard.Height - 1);
                // Top accent
                using var br = new SolidBrush(Color.FromArgb(16, 185, 129));
                e.Graphics.FillRectangle(br, 0, 0, actionCard.Width, 3);
            };

            int fx = 24;
            int fy = 22;

            actionCard.Controls.Add(new Label
            {
                Text = "MONTHLY PLAN & TARGETS",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize = true,
                Location = new Point(fx, fy + 6)
            });

            _btnDownload = new Button
            {
                Text      = "📥  Download Template",
                Location  = new Point(fx + 250, fy),
                Width     = 220,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            _btnDownload.FlatAppearance.BorderSize = 0;
            _btnDownload.Click += DownloadDemoClick;
            actionCard.Controls.Add(_btnDownload);

            _btnUpload = new Button
            {
                Text      = "📤  Upload Excel Plan",
                Location  = new Point(fx + 490, fy),
                Width     = 180,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = MainForm.C_Green,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            _btnUpload.FlatAppearance.BorderSize = 0;
            _btnUpload.Click += UploadExcelClick;
            actionCard.Controls.Add(_btnUpload);

            // ── Column Headers ────────────────────────────────────────────────────
            _pnlColHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                BackColor = Color.FromArgb(16, 22, 38),
                Padding   = new Padding(0)
            };
            _pnlColHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, _pnlColHeader.Height - 1, _pnlColHeader.Width, _pnlColHeader.Height - 1);
            };

            BuildColumnHeaders();

            // ── Scrollable list panel ─────────────────────────────────────────────
            var scroll = new Panel
            {
                Dock      = DockStyle.Fill,
                AutoScroll = true,
                BackColor  = MainForm.C_Dark,
                Padding    = new Padding(0, 4, 0, 0)
            };
            _listPanel = new Panel
            {
                AutoSize    = true,
                AutoSizeMode= AutoSizeMode.GrowAndShrink,
                BackColor   = Color.Transparent,
                Location    = new Point(0, 0)
            };
            scroll.Controls.Add(_listPanel);

            // Add in reverse order (Fill last)
            this.Controls.Add(scroll);
            this.Controls.Add(_pnlColHeader);
            this.Controls.Add(actionCard);
            this.Controls.Add(_lblStatus);
            this.Controls.Add(pnlHeader);
        }

        private void BuildColumnHeaders()
        {
            _pnlColHeader.Controls.Clear();
            var cols = new List<string> { "Date", "Main Assy", "Testing" };
            cols.AddRange(AppState.Settings.SubAssyProducts);
            
            int x = 24;
            foreach (var c in cols)
            {
                int colWidth = c == "Date" ? 140 : 160;
                _pnlColHeader.Controls.Add(new Label
                {
                    Text      = c.ToUpper(),
                    Font      = new Font("Segoe UI", 8, FontStyle.Bold),
                    ForeColor = MainForm.C_Text2,
                    AutoSize  = false,
                    Width     = colWidth,
                    AutoEllipsis = true,
                    Location  = new Point(x, 10)
                });
                x += colWidth;
            }
        }

        // ── Data Loading ──────────────────────────────────────────────────────────
        private async void LoadDataAsync()
        {
            _lblStatus.Text      = "⏳  Loading monthly plan…";
            _lblStatus.ForeColor = MainForm.C_Text2;
            _btnRefresh.Enabled  = false;

            var dailyPlans = await _host.Firebase.FetchDailyPlanAsync();
            var target = await _host.Firebase.FetchMonthlyTargetAsync();

            var dict = new Dictionary<DateTime, PlanRow>();

            // Add daily plans
            foreach (var kvp in dailyPlans)
            {
                if (DateTime.TryParse(kvp.Key, out var dt))
                {
                    dict[dt.Date] = new PlanRow { Date = dt.Date, Targets = kvp.Value };
                }
            }

            _rows = dict.Values.OrderBy(r => r.Date).ToList();
            BuildColumnHeaders(); // Rebuild headers if settings changed
            RenderList();
            
            _lblStatus.Text      = $"✔  Loaded  ·  Total Monthly Target: {target}  ·  {DateTime.Now:HH:mm:ss}";
            _lblStatus.ForeColor = MainForm.C_Green;
            _btnRefresh.Enabled  = true;
        }

        private void RenderList()
        {
            _listPanel.Controls.Clear();
            int y = 0;

            foreach (var r in _rows)
            {
                var row = BuildRow(r);
                row.Location = new Point(0, y);
                _listPanel.Controls.Add(row);
                y += row.Height + 2;
            }

            if (_rows.Count == 0)
            {
                _listPanel.Controls.Add(new Label
                {
                    Text      = "No monthly plan data available. Upload an Excel plan to populate.",
                    Font      = new Font("Segoe UI", 10),
                    ForeColor = MainForm.C_Text3,
                    AutoSize  = true,
                    Location  = new Point(24, 20)
                });
            }
        }

        private Panel BuildRow(PlanRow entry)
        {
            bool isPast = entry.Date < DateTime.Today;
            bool isToday = entry.Date == DateTime.Today;

            int requiredWidth = 24 + 140 + 160 * 2 + 160 * AppState.Settings.SubAssyProducts.Count + 50;
            var row = new Panel
            {
                Height    = 44,
                Width     = Math.Max(1000, requiredWidth),
                BackColor = isToday ? Color.FromArgb(255, 30, 58, 138) :
                            (isPast ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(22, 30, 48))
            };
            row.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
                // Left status bar
                Color barColor = isToday ? MainForm.C_Blue : (isPast ? Color.Gray : MainForm.C_Green);
                using var br = new SolidBrush(barColor);
                e.Graphics.FillRectangle(br, 0, 0, 4, row.Height);
            };

            int x = 24;

            // Date
            row.Controls.Add(new Label
            {
                Text      = entry.Date.ToString("dd MMM yyyy") + (isToday ? " (Today)" : ""),
                Font      = new Font("Segoe UI", 10, isToday ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = isToday ? Color.White : MainForm.C_Text1,
                AutoSize  = false,
                Width     = 140,
                AutoEllipsis = true,
                Location  = new Point(x, 12)
            });
            x += 140;

            // Main Assy Target
            row.Controls.Add(new Label
            {
                Text      = entry.Targets.MainTarget > 0 ? entry.Targets.MainTarget.ToString() : "—",
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = isToday ? Color.White : MainForm.C_Text1,
                AutoSize  = false,
                Width     = 160,
                Location  = new Point(x, 12)
            });
            x += 160;

            // Testing Target
            row.Controls.Add(new Label
            {
                Text      = entry.Targets.TestingTarget > 0 ? entry.Targets.TestingTarget.ToString() : "—",
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = isToday ? Color.FromArgb(186, 230, 253) : MainForm.C_Cyan,
                AutoSize  = false,
                Width     = 160,
                Location  = new Point(x, 12)
            });
            x += 160;

            // Sub Assy Targets
            foreach (var subAssy in AppState.Settings.SubAssyProducts)
            {
                int val = entry.Targets.SubAssyTargets.TryGetValue(subAssy, out int v) ? v : 0;
                row.Controls.Add(new Label
                {
                    Text      = val > 0 ? val.ToString() : "—",
                    Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = isToday ? Color.FromArgb(253, 230, 138) : MainForm.C_Orange,
                    AutoSize  = false,
                    Width     = 160,
                    Location  = new Point(x, 12)
                });
                x += 160;
            }

            return row;
        }

        // ─── Excel Operations ───────────────────────────────────────────────────

        private void DownloadDemoClick(object? sender, EventArgs e)
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                Title = "Save Demo Excel Template",
                FileName = $"PlanTarget_Template_{DateTime.Now:MMM_yyyy}.xlsx"
            };

            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Monthly Plan");
                
                var cols = new List<string> { "Date (YYYY-MM-DD)", "Main Assy Target", "Testing Target" };
                cols.AddRange(AppState.Settings.SubAssyProducts);

                // Headers
                for (int i = 0; i < cols.Count; i++)
                {
                    worksheet.Cell(1, i + 1).Value = cols[i];
                }
                
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                // Demo data
                var firstDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                worksheet.Cell(2, 1).Value = firstDay.ToString("yyyy-MM-dd");
                worksheet.Cell(2, 2).Value = 100;
                worksheet.Cell(2, 3).Value = 100;
                for (int i = 3; i < cols.Count; i++) worksheet.Cell(2, i + 1).Value = 120; // Demo sub assy target
                
                worksheet.Cell(3, 1).Value = firstDay.AddDays(1).ToString("yyyy-MM-dd");
                worksheet.Cell(3, 2).Value = 150;
                worksheet.Cell(3, 3).Value = 150;
                for (int i = 3; i < cols.Count; i++) worksheet.Cell(3, i + 1).Value = 180; // Demo sub assy target

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(sfd.FileName);

                MessageBox.Show("Template downloaded successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating template: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void UploadExcelClick(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Excel Files|*.xls;*.xlsx",
                Title = "Select Monthly Plan Excel File"
            };

            if (ofd.ShowDialog() != DialogResult.OK) return;

            _btnUpload.Enabled = false;
            _btnUpload.Text = "Uploading…";
            _lblStatus.Text = "⏳  Parsing Excel data…";

            try
            {
                var dailyPlans = new Dictionary<string, DailyTargets>();
                int totalMonthlyTarget = 0;

                using (var stream = File.Open(ofd.FileName, FileMode.Open, FileAccess.Read))
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                    {
                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                        {
                            UseHeaderRow = true
                        }
                    });

                    var table = result.Tables[0];
                    var colNames = new List<string>();
                    for (int i = 0; i < table.Columns.Count; i++)
                        colNames.Add(table.Columns[i].ColumnName);

                    foreach (DataRow row in table.Rows)
                    {
                        if (row[0] == DBNull.Value) continue;
                        
                        string dateStr = "";
                        if (row[0] is DateTime dtVal)
                        {
                            dateStr = dtVal.ToString("yyyy-MM-dd");
                        }
                        else
                        {
                            dateStr = row[0].ToString()?.Trim() ?? "";
                        }
                        
                        if (!DateTime.TryParse(dateStr, out var parsedDate)) continue;

                        var targets = new DailyTargets();

                        // Main Assy Target
                        int mt = 0;
                        if (table.Columns.Count > 1 && row[1] != DBNull.Value)
                            int.TryParse(row[1].ToString(), out mt);
                        targets.MainTarget = mt;

                        // Testing Target
                        int tt = 0;
                        if (table.Columns.Count > 2 && row[2] != DBNull.Value)
                            int.TryParse(row[2].ToString(), out tt);
                        targets.TestingTarget = tt;

                        // Sub Assy Targets (dynamic)
                        for (int i = 3; i < table.Columns.Count; i++)
                        {
                            if (row[i] != DBNull.Value && int.TryParse(row[i].ToString(), out int st))
                            {
                                targets.SubAssyTargets[colNames[i]] = st;
                            }
                        }

                        if (targets.MainTarget > 0 || targets.TestingTarget > 0 || targets.SubAssyTargets.Count > 0)
                        {
                            string formattedDate = parsedDate.ToString("yyyy-MM-dd");
                            dailyPlans[formattedDate] = targets;
                            totalMonthlyTarget += targets.MainTarget;
                        }
                    }
                }

                _lblStatus.Text = "⏳  Saving to Firebase…";
                
                var t1 = _host.Firebase.SaveDailyPlanAsync(dailyPlans);
                var t2 = _host.Firebase.SaveMonthlyTargetAsync(totalMonthlyTarget);
                
                await Task.WhenAll(t1, t2);

                string errs = "";
                if (!string.IsNullOrEmpty(t1.Result)) errs += "DailyPlan: " + t1.Result + "\n";
                if (!string.IsNullOrEmpty(t2.Result)) errs += "MonthlyTarget: " + t2.Result + "\n";

                if (!string.IsNullOrEmpty(errs))
                {
                    MessageBox.Show($"Errors occurred while saving to Firebase:\n{errs}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("Monthly plan successfully uploaded and saved to Firebase.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing Excel file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblStatus.Text = "⚠  Upload failed";
                _lblStatus.ForeColor = MainForm.C_Red;
            }
            finally
            {
                _btnUpload.Enabled = true;
                _btnUpload.Text = "📤  Upload Excel Plan";
            }
        }

        public void RefreshData() => LoadDataAsync();

        public bool SupportsAutoRefresh => false; // Loads on open + manual Refresh only
    }
}
