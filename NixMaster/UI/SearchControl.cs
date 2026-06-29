using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NixMaster.Models;

namespace NixMaster.UI
{
    /// <summary>
    /// Search screen — type / scan a MAC ID to see full traceability detail.
    /// </summary>
    public class SearchControl : UserControl
    {
        private readonly MainForm _host;

        private TextBox _txtMac      = null!;
        private Panel   _pnlResult   = null!;
        private Label   _lblNotFound = null!;
        private Label   _lblLoading  = null!;
        
        private Button  _btnExport   = null!;
        private CombinedRecord? _currentRecord = null;

        public SearchControl(MainForm host)
        {
            _host = host;
            Build();
        }

        private void Build()
        {
            this.BackColor = MainForm.C_Dark;

            // ── Page header ───────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 64,
                BackColor = MainForm.C_Dark
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };
            pnlHeader.Controls.Add(new Label
            {
                Text      = "🔍  Search by Serial No",
                Font      = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(24, 18)
            });

            // ── Search bar ────────────────────────────────────────────────────────
            var pnlSearch = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 80,
                BackColor = MainForm.C_Sidebar,
                Padding   = new Padding(24, 16, 24, 0)
            };
            pnlSearch.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlSearch.Height - 1, pnlSearch.Width, pnlSearch.Height - 1);
            };

            _txtMac = new TextBox
            {
                Location    = new Point(24, 18),
                Width       = 560,
                Height      = 36,
                Font        = new Font("Consolas", 13),
                BackColor   = Color.FromArgb(10, 15, 25),
                ForeColor   = MainForm.C_Cyan,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Enter Serial No (e.g. 280526DVB0001 or 0526DVB0001)"
            };
            _txtMac.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoSearch(); };

            var btnSearch = new Button
            {
                Text      = "Search",
                Location  = new Point(596, 18),
                Width     = 110,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = MainForm.C_Blue,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            btnSearch.FlatAppearance.BorderSize = 0;
            btnSearch.MouseEnter += (_, __) => btnSearch.BackColor = Color.FromArgb(80, 150, 255);
            btnSearch.MouseLeave += (_, __) => btnSearch.BackColor = MainForm.C_Blue;
            btnSearch.Click += (_, __) => DoSearch();

            _btnExport = new Button
            {
                Text      = "Export CSV",
                Location  = new Point(718, 18),
                Width     = 110,
                Height    = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = MainForm.C_Green,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                Visible   = false
            };
            _btnExport.FlatAppearance.BorderSize = 0;
            _btnExport.Click += (_, __) => ExportCsv();

            _txtMac.Anchor  = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            btnSearch.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _btnExport.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            pnlSearch.Controls.Add(_txtMac);
            pnlSearch.Controls.Add(btnSearch);
            pnlSearch.Controls.Add(_btnExport);

            // Handle resize
            pnlSearch.Resize += (_, __) =>
            {
                _txtMac.Width   = pnlSearch.Width - 280;
                btnSearch.Left  = pnlSearch.Width - 244;
                _btnExport.Left = pnlSearch.Width - 124;
            };

            // ── Status labels ─────────────────────────────────────────────────────
            _lblLoading = StatusLabel("⏳  Searching…", MainForm.C_Text2);
            _lblNotFound = StatusLabel("❌  No record found for this Serial No.", MainForm.C_Red);
            _lblLoading.Visible  = false;
            _lblNotFound.Visible = false;

            // ── Scrollable result panel ───────────────────────────────────────────
            _pnlResult = new Panel
            {
                Dock       = DockStyle.Fill,
                BackColor  = Color.Transparent,
                AutoScroll = true,
                Padding    = new Padding(24, 16, 24, 16),
                Visible    = false
            };

            this.Controls.Add(_pnlResult);
            this.Controls.Add(_lblNotFound);
            this.Controls.Add(_lblLoading);
            this.Controls.Add(pnlSearch);
            this.Controls.Add(pnlHeader);
        }

        // ─── Search logic ─────────────────────────────────────────────────────────
        private async void DoSearch()
        {
            string query = _txtMac.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            _pnlResult.Visible   = false;
            _lblNotFound.Visible = false;
            _lblLoading.Visible  = true;
            _btnExport.Visible   = false;
            _currentRecord       = null;
            _lblLoading.Text     = "⏳  Searching by Serial No…";
            
            if (query.Length >= 11)
            {
                query = query.Substring(query.Length - 11);
            }

            CombinedRecord? record = null;
            string error = "";

            // Try FetchMacAsync first
            (record, error) = await _host.Firebase.FetchMacAsync(query);

            if (record == null)
            {
                // Fallback: search by Serial No
                _lblLoading.Text = "⏳  Searching all records by Serial No…";
                var (all, errAll) = await _host.Firebase.FetchAllAsync();
                
                if (string.IsNullOrEmpty(errAll))
                {
                    record = all.FirstOrDefault(r => 
                        string.Equals(r.Testing?.DeviceSerialNo, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.MacId, query, StringComparison.OrdinalIgnoreCase)
                    );
                    if (record != null) 
                    {
                        error = "";
                        // If fetched via FetchAllAsync, MacId might be the sanitized key (e.g. FC_23_CD_FD_22_F3). Restore colons for display.
                        if (record.MacId.Contains('_') && record.MacId.Length == 17)
                            record.MacId = record.MacId.Replace('_', ':');
                    }
                }
            }

            _lblLoading.Visible = false;
            _lblLoading.Text    = "⏳  Searching…";

            if (error == "NOT_FOUND" || record == null) { _lblNotFound.Visible = true; return; }

            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show($"Firebase error: {error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ShowResult(record);
        }

        // ─── Result display ───────────────────────────────────────────────────────
        private void ShowResult(CombinedRecord r)
        {
            _currentRecord = r;
            _btnExport.Visible = true;
            _pnlResult.Controls.Clear();

            // ── Use a FlowLayoutPanel so cards wrap and are scrollable ──
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = true,
                AutoScroll    = true,
                BackColor     = Color.Transparent,
                Padding       = new Padding(4)
            };

            // Each card is fixed-width, auto-height
            int cardW = 420;

            // Assembly card
            var aCard = BuildDetailCard("🔩  Assembly Data", MainForm.C_Green, cardW);
            if (r.Assembly != null)
            {
                var a = r.Assembly;
                AddRow(aCard, "Serial No",     r.MacId);
                AddRow(aCard, "Station",       a.StationName);
                AddRow(aCard, "Operator",      a.Operator);
                AddRow(aCard, "Assembly Time", a.Timestamp);
                AddRow(aCard, "Record ID",     a.RecordId.ToString());
                AddRow(aCard, "Status",        a.Status);
                if (a.Parts.Count > 0)
                {
                    AddSectionTitle(aCard, "Scanned Parts");
                    foreach (var kv in a.Parts)
                    {
                        string k = kv.Key.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) ? "Decision" : kv.Key;
                        AddRow(aCard, k, kv.Value, mono: true);
                    }
                }
            }
            else { AddRow(aCard, "Assembly", "No assembly record found"); }

            // Sub-Assemblies card
            bool anyAssy = r.IrSubAssy != null || r.IrIndicationSubAssy != null || r.CamSubAssy != null || r.CameraLink != null;
            var subCard = BuildDetailCard("🧩  Sub-Assemblies", anyAssy ? Color.DeepSkyBlue : MainForm.C_Orange, cardW);
            AddSectionTitle(subCard, "IR PCBA");
            if (r.IrSubAssy != null) { AddRow(subCard, "PCBA QR", r.IrSubAssy.PcbaQR, mono: true); AddRow(subCard, "Produced On", r.IrSubAssy.ScannedAt); AddRow(subCard, "Produced By", r.IrSubAssy.ScannedBy); }
            else { AddRow(subCard, "Status", "⏳  Pending"); }
            AddSectionTitle(subCard, "IR Indication");
            if (r.IrIndicationSubAssy != null) { AddRow(subCard, "PCBA QR", r.IrIndicationSubAssy.PcbaQR, mono: true); AddRow(subCard, "Produced On", r.IrIndicationSubAssy.ScannedAt); AddRow(subCard, "Produced By", r.IrIndicationSubAssy.ScannedBy); }
            else { AddRow(subCard, "Status", "⏳  Pending"); }
            AddSectionTitle(subCard, "Camera Sub-Assy");
            if (r.CamSubAssy != null) { AddRow(subCard, "PCBA QR", r.CamSubAssy.PcbaQR, mono: true); AddRow(subCard, "Produced On", r.CamSubAssy.ScannedAt); AddRow(subCard, "Produced By", r.CamSubAssy.ScannedBy); }
            else if (r.CameraLink != null) { AddRow(subCard, "SubAssy ID", r.CameraLink.SubAssyId, mono: true); AddRow(subCard, "Linked At", r.CameraLink.ScannedAt); AddRow(subCard, "Linked By", r.CameraLink.ScannedBy); }
            else { AddRow(subCard, "Status", "⏳  Pending"); }

            // Testing card
            var tCard = BuildDetailCard("🧪  Testing Data", r.IsTested ? (r.IsTestingNG ? MainForm.C_Red : Color.DodgerBlue) : MainForm.C_Orange, cardW);
            if (r.Testing != null)
            {
                var t = r.Testing;
                AddRow(tCard, "Station",     t.StationName);
                AddRow(tCard, "Operator",    t.Operator);
                AddRow(tCard, "Tested At",   t.TestedAt);
                
                string displayStatus = t.Status;
                if (string.Equals(t.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    displayStatus = "Functionality OK";
                    
                AddRow(tCard, "Test Status", displayStatus);
                if (r.IsTestingNG) AddRow(tCard, "Defect", t.DefectDetails, mono: true);
                AddSectionTitle(tCard, "Scanned Codes");
                AddRow(tCard, "Customer QR",  t.DeviceSerialNo, mono: true);
                AddRow(tCard, "Testing QR", t.TestingQR, mono: true);
            }
            else { AddRow(tCard, "Testing", "⏳  Not yet tested"); }

            // Packing card
            var pCard = BuildDetailCard("📦  Packing Data", r.IsPacked ? MainForm.C_Purple : MainForm.C_Orange, cardW);
            if (r.Packing != null)
            {
                var p = r.Packing;
                AddRow(pCard, "Box No",       p.BoxNo);
                AddRow(pCard, "Packed At",    p.PackedAt);
                AddRow(pCard, "Packed By",    p.PackedBy);
                AddRow(pCard, "Pack Station", p.StationName);
                AddRow(pCard, "Status",       p.Status);
                AddSectionTitle(pCard, "QR Codes");
                AddRow(pCard, "Long QR",  p.LongQR,  mono: true);
                AddRow(pCard, "Short QR", p.ShortQR, mono: true);
            }
            else { AddRow(pCard, "Packing", "⏳  Not yet packed"); }

            // Dispatch card
            var dCard = BuildDetailCard("🚚  Dispatch Data", r.IsDispatched ? MainForm.C_Green : MainForm.C_Orange, cardW);
            if (r.Dispatch != null)
            {
                var d = r.Dispatch;
                AddRow(dCard, "Pallet ID",    d.PalletId);
                AddRow(dCard, "Box Count",    $"{d.BoxCount} Boxes");
                AddRow(dCard, "Total Units",  $"{d.TotalUnits} Units");
                AddRow(dCard, "Dispatched At", d.DispatchDate);
                AddRow(dCard, "Dispatched By", d.DispatchedBy);
                AddRow(dCard, "Source",       d.Source);
                if (!string.IsNullOrEmpty(d.Remarks)) AddRow(dCard, "Decision", d.Remarks);
            }
            else { AddRow(dCard, "Dispatch", "⏳  Not yet dispatched"); }

            // RCA card
            var rcaCard = BuildDetailCard("📋  RCA Data", r.IsRcaCompleted ? Color.Crimson : MainForm.C_Orange, cardW);
            if (r.Rca != null)
            {
                var rca = r.Rca;
                AddRow(rcaCard, "Logged Date", rca.RcaDate);
                AddRow(rcaCard, "Root Cause",  rca.RootCause);
                AddRow(rcaCard, "Action",      rca.ActionTaken);
                AddRow(rcaCard, "Engineer",    rca.Engineer);
                AddRow(rcaCard, "Handover",    rca.HandoverTo);
                AddRow(rcaCard, "Status",      rca.Status);
            }
            else { AddRow(rcaCard, "RCA", "⏳  No RCA record"); }

            flow.Controls.AddRange(new Control[] { aCard, subCard, tCard, pCard, dCard, rcaCard });
            _pnlResult.Controls.Add(flow);
            _pnlResult.Visible = true;
        }

        // ─── Card / row builders ──────────────────────────────────────────────────
        private static Panel BuildDetailCard(string title, Color accent, int width = 0)
        {
            var card = new Panel
            {
                BackColor    = MainForm.C_Card,
                Margin       = new Padding(6),
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            if (width > 0)
            {
                card.MinimumSize = new Size(width, 0);
                card.MaximumSize = new Size(width, 0);
            }

            card.Paint += (s, e) =>
            {
                using var br = new SolidBrush(accent);
                e.Graphics.FillRectangle(br, 0, 0, card.Width, 4);
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var hdr = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 46,
                BackColor = MainForm.C_Sidebar
            };
            hdr.Paint += (s, e) =>
            {
                using var br = new SolidBrush(accent);
                e.Graphics.FillRectangle(br, 0, 0, hdr.Width, 4);
            };
            hdr.Controls.Add(new Label
            {
                Text      = title,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(14, 12)
            });
            card.Controls.Add(hdr);
            return card;
        }

        private static void AddRow(Panel card, string key, string value, bool mono = false)
        {
            value = value ?? "";
            
            var keyFont = new Font("Segoe UI", 9, FontStyle.Bold);
            var valFont = mono ? new Font("Consolas", 9) : new Font("Segoe UI", 9);

            int valX = 160; 
            int maxKeyW = valX - 20; // 140
            int valWidth = card.Width - valX - 14;
            if (valWidth < 100) valWidth = 200;

            int keyH = 20;
            int valH = 20;

            using (var g = card.CreateGraphics())
            {
                var kSz = g.MeasureString(key + ":", keyFont, maxKeyW);
                keyH = (int)Math.Ceiling(kSz.Height);

                var vSz = g.MeasureString(value, valFont, valWidth);
                valH = (int)Math.Ceiling(vSz.Height);
            }
            
            int rowH = Math.Max(30, Math.Max(keyH, valH) + 12);

            var row = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = rowH,
                BackColor = Color.Transparent
            };
            row.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 14, row.Height - 1, row.Width - 14, row.Height - 1);
            };

            var txtKey = new TextBox
            {
                Text        = key + ":",
                Location    = new Point(14, 8),
                Width       = maxKeyW,
                Height      = keyH + 6,
                Font        = keyFont,
                ForeColor   = MainForm.C_Text2,
                BackColor   = MainForm.C_Card,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Multiline   = true,
                TabStop     = false
            };

            var txtVal = new TextBox
            {
                Text        = value,
                Location    = new Point(valX, 8),
                Width       = valWidth,
                Height      = valH + 6,
                Font        = valFont,
                ForeColor   = mono ? MainForm.C_Cyan : MainForm.C_Text1,
                BackColor   = MainForm.C_Card,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Multiline   = true,
                TabStop     = false
            };

            row.Controls.Add(txtKey);
            row.Controls.Add(txtVal);
            card.Controls.Add(row);
        }

        private static void AddSectionTitle(Panel card, string text)
        {
            var lbl = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 28,
                Text      = "▸  " + text,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = MainForm.C_Text3,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(14, 0, 0, 0),
                BackColor = Color.FromArgb(13, 18, 30)
            };
            card.Controls.Add(lbl);
        }

        private static Label StatusLabel(string text, Color color) =>
            new Label
            {
                Text      = text,
                Dock      = DockStyle.Top,
                Height    = 46,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = color,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };

        private void ExportCsv()
        {
            if (_currentRecord == null) return;

            using var sfd = new SaveFileDialog
            {
                Filter = "CSV Files|*.csv",
                FileName = $"NixMaster_Unit_{_currentRecord.MacId.Replace(":", "_")}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("Section,Field,Value");

            Action<string, string, string> AddRowCsv = (sec, f, v) => sb.AppendLine($"\"{sec}\",\"{f}\",\"{v?.Replace("\"", "\"\"")}\"");

            var r = _currentRecord;
            
            if (r.Assembly != null)
            {
                AddRowCsv("Assembly", "Serial No", r.MacId);
                AddRowCsv("Assembly", "Station", r.Assembly.StationName);
                AddRowCsv("Assembly", "Operator", r.Assembly.Operator);
                AddRowCsv("Assembly", "Assembly Time", r.Assembly.Timestamp);
                AddRowCsv("Assembly", "Record ID", r.Assembly.RecordId.ToString());
                AddRowCsv("Assembly", "Status", r.Assembly.Status);
                foreach (var p in r.Assembly.Parts)
                {
                    string k = p.Key.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) ? "Decision" : p.Key;
                    AddRowCsv("Assembly Parts", k, p.Value);
                }
            }

            if (r.IrSubAssy != null) { AddRowCsv("IR PCBA", "PCBA QR", r.IrSubAssy.PcbaQR); AddRowCsv("IR PCBA", "Produced On", r.IrSubAssy.ScannedAt); AddRowCsv("IR PCBA", "Produced By", r.IrSubAssy.ScannedBy); }
            if (r.IrIndicationSubAssy != null) { AddRowCsv("IR Indication", "PCBA QR", r.IrIndicationSubAssy.PcbaQR); AddRowCsv("IR Indication", "Produced On", r.IrIndicationSubAssy.ScannedAt); AddRowCsv("IR Indication", "Produced By", r.IrIndicationSubAssy.ScannedBy); }
            if (r.CamSubAssy != null) { AddRowCsv("Camera Sub-Assy", "PCBA QR", r.CamSubAssy.PcbaQR); AddRowCsv("Camera Sub-Assy", "Produced On", r.CamSubAssy.ScannedAt); AddRowCsv("Camera Sub-Assy", "Produced By", r.CamSubAssy.ScannedBy); }
            if (r.CameraLink != null) { AddRowCsv("Camera Link", "SubAssy ID", r.CameraLink.SubAssyId); AddRowCsv("Camera Link", "Linked At", r.CameraLink.ScannedAt); AddRowCsv("Camera Link", "Linked By", r.CameraLink.ScannedBy); }

            if (r.Testing != null)
            {
                var t = r.Testing;
                AddRowCsv("Testing", "Station", t.StationName);
                AddRowCsv("Testing", "Operator", t.Operator);
                AddRowCsv("Testing", "Tested At", t.TestedAt);
                AddRowCsv("Testing", "Test Status", string.Equals(t.Status, "OK", StringComparison.OrdinalIgnoreCase) ? "Functionality OK" : t.Status);
                if (r.IsTestingNG) AddRowCsv("Testing", "Defect", t.DefectDetails);
                AddRowCsv("Testing", "Customer QR", t.DeviceSerialNo);
                AddRowCsv("Testing", "Testing QR", t.TestingQR);
            }

            if (r.Packing != null)
            {
                var p = r.Packing;
                AddRowCsv("Packing", "Box No", p.BoxNo);
                AddRowCsv("Packing", "Packed At", p.PackedAt);
                AddRowCsv("Packing", "Packed By", p.PackedBy);
                AddRowCsv("Packing", "Pack Station", p.StationName);
                AddRowCsv("Packing", "Status", p.Status);
                AddRowCsv("Packing", "Long QR", p.LongQR);
                AddRowCsv("Packing", "Short QR", p.ShortQR);
            }

            if (r.Dispatch != null)
            {
                var d = r.Dispatch;
                AddRowCsv("Dispatch", "Pallet ID", d.PalletId);
                AddRowCsv("Dispatch", "Box Count", d.BoxCount.ToString());
                AddRowCsv("Dispatch", "Total Units", d.TotalUnits.ToString());
                AddRowCsv("Dispatch", "Dispatched At", d.DispatchDate);
                AddRowCsv("Dispatch", "Dispatched By", d.DispatchedBy);
                AddRowCsv("Dispatch", "Source", d.Source);
                AddRowCsv("Dispatch", "Decision", d.Remarks);
            }

            if (r.Rca != null)
            {
                var rca = r.Rca;
                AddRowCsv("RCA", "Logged Date", rca.RcaDate);
                AddRowCsv("RCA", "Root Cause", rca.RootCause);
                AddRowCsv("RCA", "Action", rca.ActionTaken);
                AddRowCsv("RCA", "Engineer", rca.Engineer);
                AddRowCsv("RCA", "Handover", rca.HandoverTo);
                AddRowCsv("RCA", "Status", rca.Status);
            }

            try
            {
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Unit traceability data exported successfully!", "Export Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export data:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
