using System;
using System.Drawing;
using System.Windows.Forms;
using NixMaster.Core;

namespace NixMaster.UI
{
    public class SettingsControl : UserControl
    {
        private readonly MainForm _host;

        private TextBox  _txtUrl      = null!;
        private TextBox  _txtNode     = null!;
        private TextBox  _txtInterval = null!;
        private CheckBox _chkAuto     = null!;
        private TextBox  _txtSubAssyProducts = null!;
        private DataGridView _dgvLines = null!;

        public SettingsControl(MainForm host)
        {
            _host = host;
            Build();
            this.Load += (_, __) => PopulateFields();
        }

        private void Build()
        {
            this.BackColor  = MainForm.C_Dark;
            this.AutoScroll = true;
            this.Padding    = new Padding(0);

            // ── Page header ───────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 64,
                BackColor = Color.Transparent,
                Padding   = new Padding(28, 16, 16, 0)
            };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };
            pnlHeader.Controls.Add(new Label
            {
                Text      = "⚙   Settings",
                Font      = new Font("Segoe UI", 15, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(28, 16)
            });

            // ── Scrollable content wrapper ────────────────────────────────────────
            var scroll = new Panel
            {
                Dock      = DockStyle.Fill,
                AutoScroll = true,
                BackColor  = MainForm.C_Dark,
                Padding    = new Padding(28, 20, 28, 20)
            };

            // ── Firebase settings card ─────────────────────────────────────────────
            var fbCard = MakeCard("🔥  Firebase Configuration", MainForm.C_Blue);

            int cy = 52;

            FieldLabel(fbCard, "Database URL", cy); cy += 24;
            _txtUrl = FieldInput(fbCard, cy, width: 600); cy += 40;
            FieldHint(fbCard, "e.g.  https://your-project-default-rtdb.firebaseio.com", cy); cy += 30;

            FieldLabel(fbCard, "Root Node Path", cy); cy += 24;
            _txtNode = FieldInput(fbCard, cy, width: 320); cy += 40;
            FieldHint(fbCard, "Default: EndToEndTraceability", cy); cy += 32;

            FieldLabel(fbCard, "Auto-Refresh Interval (seconds)", cy); cy += 24;
            _txtInterval = FieldInput(fbCard, cy, width: 100); cy += 40;

            _chkAuto = new CheckBox
            {
                Text      = "Enable Auto-Refresh",
                Location  = new Point(24, cy),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 10),
                ForeColor = MainForm.C_Text1
            };
            fbCard.Controls.Add(_chkAuto); cy += 44;

            // Buttons row
            var btnSave = MakeBtn("💾  Save Settings", MainForm.C_Blue,  new Point(24, cy));
            var btnTest = MakeBtn("🔗  Test Connection", MainForm.C_Orange, new Point(168, cy));

            btnSave.Click += SaveClick;
            btnTest.Click += async (_, __) =>
            {
                AppState.Settings.FirebaseUrl = _txtUrl.Text.Trim().TrimEnd('/');
                var (ok, msg) = await _host.Firebase.TestConnectionAsync();
                MessageBox.Show(msg, "Firebase Test", MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };

            fbCard.Controls.AddRange(new Control[] { btnSave, btnTest });
            fbCard.Height = cy + 60;

            // ── Lines Configuration Card ───────────────────────────────────────────
            var linesCard = MakeCard("🏭  Production Lines", MainForm.C_Green);
            int ly = 52;

            _dgvLines = new DataGridView
            {
                Location            = new Point(24, ly),
                Width               = 630,
                Height              = 180,
                AllowUserToAddRows  = true,
                AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor     = Color.FromArgb(10, 15, 25),
                GridColor           = MainForm.C_Border,
                RowHeadersVisible   = false,
                BorderStyle         = BorderStyle.FixedSingle,
                DefaultCellStyle    = new DataGridViewCellStyle
                {
                    BackColor          = Color.FromArgb(10, 15, 25),
                    ForeColor          = MainForm.C_Text1,
                    Font               = new Font("Segoe UI", 9),
                    SelectionBackColor = Color.FromArgb(40, 80, 150)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor          = Color.FromArgb(17, 24, 39),
                    ForeColor          = MainForm.C_Text2,
                    Font               = new Font("Segoe UI", 9, FontStyle.Bold)
                }
            };
            _dgvLines.EnableHeadersVisualStyles = false;
            _dgvLines.Columns.Add("colName", "Line Name");
            _dgvLines.Columns.Add("colUrl", "Firebase URL");
            _dgvLines.Columns.Add("colNode", "Node Path");

            var btnAddLine = MakeBtn("➕  Add Line", MainForm.C_Blue, new Point(24, ly + 190));
            var btnDelLine = MakeBtn("🗑  Remove Selected", Color.FromArgb(180, 80, 0), new Point(174, ly + 190));
            btnAddLine.Click += (_, __) => _dgvLines.Rows.Add("New Line", "https://...", "EndToEndTraceability");
            btnDelLine.Click += (_, __) =>
            {
                if (_dgvLines.CurrentRow != null && !_dgvLines.CurrentRow.IsNewRow)
                    _dgvLines.Rows.Remove(_dgvLines.CurrentRow);
            };

            linesCard.Controls.AddRange(new Control[] { btnAddLine, btnDelLine, _dgvLines });
            ly += 250;
            linesCard.Height = ly + 20;
            linesCard.Location = new Point(0, fbCard.Height + 20);

            // ── Sub-Assemblies Card ──────────────────────────────────────────────
            var subCard = MakeCard("🧩  Sub-Assemblies Configuration", MainForm.C_Cyan);
            int sy = 52;
            FieldLabel(subCard, "Tracked Products (Comma separated)", sy); sy += 24;
            _txtSubAssyProducts = FieldInput(subCard, sy, width: 600); sy += 40;
            FieldHint(subCard, "e.g. IR PCBA, IR INDICATION, CAMERA SUB ASSY", sy); sy += 30;
            subCard.Height = sy + 20;
            subCard.Location = new Point(0, fbCard.Height + linesCard.Height + 40);

            // ── Admin Tools Card ──────────────────────────────────────────────
            var adminCard = MakeCard("🔐  Admin Tools", Color.FromArgb(180, 80, 0));
            int ay = 52;

            // Description
            adminCard.Controls.Add(new Label
            {
                Text      = "Recalculate Metrics scans ALL Firebase records and corrects the\n" +
                            "Metrics node (Today / Month / AllTime production & dispatch counts).\n" +
                            "Run this if dashboard numbers look incorrect.",
                Location  = new Point(24, ay),
                Width     = 620,
                Height    = 54,
                Font      = new Font("Segoe UI", 9),
                ForeColor = MainForm.C_Text2
            }); ay += 62;

            // Progress label
            var lblMetricStatus = new Label
            {
                Text      = "",
                Location  = new Point(24, ay),
                Width     = 600,
                Height    = 20,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = MainForm.C_Text2
            };
            adminCard.Controls.Add(lblMetricStatus); ay += 28;

            // Button
            var btnRecalc = MakeBtn("🔄  Recalculate", Color.FromArgb(160, 60, 0), new Point(24, ay));
            btnRecalc.Width = 160;
            btnRecalc.Click += async (_, __) =>
            {
                // Admin password prompt
                using var pwdForm = new Form
                {
                    Text            = "Admin Authentication",
                    Width           = 360,
                    Height          = 170,
                    StartPosition   = FormStartPosition.CenterParent,
                    BackColor       = MainForm.C_Dark,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox     = false,
                    MinimizeBox     = false
                };
                var pwdLabel = new Label
                {
                    Text      = "🔒  Enter Admin Password:",
                    Location  = new Point(20, 20),
                    AutoSize  = true,
                    Font      = new Font("Segoe UI", 10, FontStyle.Bold),
                    ForeColor = MainForm.C_Text1
                };
                var pwdBox = new TextBox
                {
                    Location        = new Point(20, 48),
                    Width           = 300,
                    Font            = new Font("Segoe UI", 10),
                    BackColor       = Color.FromArgb(10, 15, 25),
                    ForeColor       = MainForm.C_Text1,
                    PasswordChar    = '•',
                    BorderStyle     = BorderStyle.FixedSingle
                };
                var btnOk = new Button
                {
                    Text      = "OK",
                    Location  = new Point(20, 86),
                    Width     = 80,
                    Height    = 32,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = MainForm.C_Blue,
                    ForeColor = Color.White,
                    Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                    DialogResult = DialogResult.OK
                };
                btnOk.FlatAppearance.BorderSize = 0;
                var btnCancel = new Button
                {
                    Text         = "Cancel",
                    Location     = new Point(110, 86),
                    Width        = 80,
                    Height       = 32,
                    FlatStyle    = FlatStyle.Flat,
                    BackColor    = Color.FromArgb(50, 60, 80),
                    ForeColor    = Color.White,
                    Font         = new Font("Segoe UI", 9),
                    DialogResult = DialogResult.Cancel
                };
                btnCancel.FlatAppearance.BorderSize = 0;
                pwdForm.AcceptButton = btnOk;
                pwdForm.CancelButton = btnCancel;
                pwdForm.Controls.AddRange(new Control[] { pwdLabel, pwdBox, btnOk, btnCancel });

                if (pwdForm.ShowDialog(this) != DialogResult.OK) return;

                const string AdminPassword = "nixadmin@2025";
                if (pwdBox.Text != AdminPassword)
                {
                    MessageBox.Show("❌  Incorrect password. Access denied.", "Auth Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Confirmed — run recalculation
                btnRecalc.Enabled    = false;
                lblMetricStatus.Text = "⏳  Starting…";
                lblMetricStatus.ForeColor = MainForm.C_Text2;

                var progress = new Progress<string>(msg =>
                {
                    lblMetricStatus.Text      = msg;
                    lblMetricStatus.ForeColor = MainForm.C_Text2;
                });

                var (ok, summary) = await _host.Firebase.RecalculateMetricsAsync(progress);
                _host.SetOnlineStatus(_host.Firebase.IsOnline);

                btnRecalc.Enabled         = true;
                lblMetricStatus.Text      = ok ? "✅  Done!" : "⚠  Failed";
                lblMetricStatus.ForeColor = ok ? MainForm.C_Green : MainForm.C_Red;

                MessageBox.Show(summary,
                    ok ? "Metrics Recalculated" : "Recalculation Failed",
                    MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            };
            adminCard.Controls.Add(btnRecalc);
            ay += 52;

            var btnPlanTarget = MakeBtn("📅  Manage Plan/Target", MainForm.C_Purple, new Point(24, ay));
            btnPlanTarget.Width = 200;
            btnPlanTarget.Click += (_, __) => _host.LoadControl(new PlanTargetControl(_host));
            adminCard.Controls.Add(btnPlanTarget);
            ay += 52;

            adminCard.Height   = ay + 16;
            adminCard.Location = new Point(0, fbCard.Height + linesCard.Height + subCard.Height + 60);

            // ── Info card ────────────────────────────────────────────────
            var infoCard = MakeCard("📖  Firebase Data Structure", MainForm.C_Purple);
            int iy = 52;
            InfoRow(infoCard, "Assembly App writes to:", "EndToEndTraceability/{MAC_ID}/AssemblyApp.json", iy); iy += 32;
            InfoRow(infoCard, "Packing App writes to: ", "EndToEndTraceability/{MAC_ID}/PackingApp.json",  iy); iy += 32;
            InfoRow(infoCard, "NixMaster reads:       ", "EndToEndTraceability.json  (full node)",         iy); iy += 32;
            InfoRow(infoCard, "Common key:            ", "MAC ID — shared across both apps",               iy); iy += 20;
            infoCard.Height = iy + 28;
            infoCard.Location = new Point(0, fbCard.Height + linesCard.Height + subCard.Height + adminCard.Height + 80);

            var inner = new Panel
            {
                AutoSize        = true,
                AutoSizeMode    = AutoSizeMode.GrowAndShrink,
                BackColor       = Color.Transparent,
                Location        = new Point(0, 0)
            };
            inner.Controls.Add(infoCard);
            inner.Controls.Add(adminCard);
            inner.Controls.Add(subCard);
            inner.Controls.Add(linesCard);
            inner.Controls.Add(fbCard);

            scroll.Controls.Add(inner);

            this.Controls.Add(scroll);
            this.Controls.Add(pnlHeader);
        }

        // ─── Populate / Save ──────────────────────────────────────────────────────
        private void PopulateFields()
        {
            _txtUrl.Text      = AppState.Settings.FirebaseUrl;
            _txtNode.Text     = AppState.Settings.NodePath;
            _txtInterval.Text = AppState.Settings.RefreshInterval.ToString();
            _chkAuto.Checked  = AppState.Settings.AutoRefresh;
            _txtSubAssyProducts.Text = string.Join(", ", AppState.Settings.SubAssyProducts);
            
            _dgvLines.Rows.Clear();
            foreach (var line in AppState.Settings.Lines)
            {
                _dgvLines.Rows.Add(line.LineName, line.FirebaseUrl, line.NodePath);
            }
        }

        private void SaveClick(object? s, EventArgs e)
        {
            string url = _txtUrl.Text.Trim();
            if (!url.StartsWith("https://") && !url.StartsWith("http://"))
            {
                MessageBox.Show("Firebase URL must start with https://", "Validation",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppState.Settings.FirebaseUrl     = url.TrimEnd('/');
            AppState.Settings.NodePath        = string.IsNullOrWhiteSpace(_txtNode.Text)
                                                ? "EndToEndTraceability"
                                                : _txtNode.Text.Trim().Trim('/');
            AppState.Settings.RefreshInterval = int.TryParse(_txtInterval.Text, out int iv)
                                                ? Math.Max(10, iv) : 30;
            AppState.Settings.AutoRefresh     = _chkAuto.Checked;

            var products = _txtSubAssyProducts.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var prodList = new System.Collections.Generic.List<string>();
            foreach (var p in products)
            {
                var trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed)) prodList.Add(trimmed);
            }
            if (prodList.Count == 0) prodList.AddRange(new[] { "IR PCBA SUB ASSy", "IR INDICATION PCBA SUB ASSY", "Camera sub assy" });
            AppState.Settings.SubAssyProducts = prodList;

            var newLines = new System.Collections.Generic.List<LineConfig>();
            foreach (DataGridViewRow row in _dgvLines.Rows)
            {
                if (row.IsNewRow) continue;
                string name = row.Cells["colName"].Value?.ToString() ?? "";
                string furl = row.Cells["colUrl"].Value?.ToString() ?? "";
                string node = row.Cells["colNode"].Value?.ToString() ?? "";
                
                if (!string.IsNullOrWhiteSpace(name))
                {
                    newLines.Add(new LineConfig
                    {
                        LineName = name.Trim(),
                        FirebaseUrl = furl.Trim().TrimEnd('/'),
                        NodePath = string.IsNullOrWhiteSpace(node) ? "EndToEndTraceability" : node.Trim().Trim('/')
                    });
                }
            }
            AppState.Settings.Lines = newLines;

            AppState.SaveSettings();
            _host.RestartAutoRefresh();
            MessageBox.Show("✔  Settings saved successfully.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ─── UI helpers ───────────────────────────────────────────────────────────
        private static Panel MakeCard(string title, Color accent)
        {
            var card = new Panel
            {
                Location  = new Point(0, 0),
                Width     = 680,
                BackColor = MainForm.C_Card
            };
            card.Paint += (s, e) =>
            {
                // Accent top strip
                using var br = new SolidBrush(accent);
                e.Graphics.FillRectangle(br, 0, 0, card.Width, 3);
                // Border
                using var pen = new Pen(MainForm.C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            card.Controls.Add(new Label
            {
                Text      = title,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = MainForm.C_Text1,
                AutoSize  = true,
                Location  = new Point(20, 16)
            });
            return card;
        }

        private static void FieldLabel(Panel card, string text, int y) =>
            card.Controls.Add(new Label
            {
                Text      = text,
                Location  = new Point(24, y),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.C_Text2
            });

        private static void FieldHint(Panel card, string text, int y) =>
            card.Controls.Add(new Label
            {
                Text      = text,
                Location  = new Point(24, y),
                AutoSize  = true,
                Font      = new Font("Segoe UI", 8),
                ForeColor = MainForm.C_Text3
            });

        private static TextBox FieldInput(Panel card, int y, int width)
        {
            var t = new TextBox
            {
                Location    = new Point(24, y),
                Width       = width,
                Font        = new Font("Segoe UI", 10),
                BackColor   = Color.FromArgb(10, 15, 25),
                ForeColor   = MainForm.C_Text1,
                BorderStyle = BorderStyle.FixedSingle
            };
            card.Controls.Add(t);
            return t;
        }

        private static Button MakeBtn(string text, Color back, Point loc)
        {
            var b = new Button
            {
                Text      = text,
                Location  = loc,
                Width     = 140,
                Height    = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static void InfoRow(Panel card, string label, string value, int y)
        {
            card.Controls.Add(new Label
            {
                Text      = label,
                Location  = new Point(24, y),
                Width     = 180,
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = MainForm.C_Text2
            });
            card.Controls.Add(new Label
            {
                Text         = value,
                Location     = new Point(210, y),
                Width        = 460,
                AutoEllipsis = true,
                Font         = new Font("Consolas", 9),
                ForeColor    = MainForm.C_Cyan
            });
        }
    }
}
