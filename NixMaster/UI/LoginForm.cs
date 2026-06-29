using System;
using System.Drawing;
using System.Windows.Forms;
using NixMaster.Core;

namespace NixMaster.UI
{
    public class LoginForm : Form
    {
        private TextBox txtUser  = null!;
        private Button  btnLogin = null!;

        // ─── Palette ──────────────────────────────────────────────────────────────
        private static readonly Color C_Dark  = Color.FromArgb(10,  13, 20);
        private static readonly Color C_Card  = Color.FromArgb(22,  30, 48);
        private static readonly Color C_Blue  = Color.FromArgb(59, 130, 246);
        private static readonly Color C_Text1 = Color.FromArgb(241, 245, 249);
        private static readonly Color C_Text2 = Color.FromArgb(148, 163, 184);
        private static readonly Color C_Border = Color.FromArgb(40, 60, 100);

        public LoginForm()
        {
            Build();
        }

        private void Build()
        {
            this.Text            = "NixMaster — Login";
            this.Size            = new Size(480, 370);
            this.MinimumSize     = new Size(480, 370);
            this.MaximumSize     = new Size(480, 370);
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.BackColor       = C_Dark;
            this.AutoScaleMode   = AutoScaleMode.Dpi;

            // ── Header panel ──────────────────────────────────────────────────────
            var pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 110,
                BackColor = C_Dark
            };

            // Draw bottom separator
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(30, 41, 59), 1);
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            var lblIcon = new Label
            {
                Text      = "🔗",
                Font      = new Font("Segoe UI Emoji", 22, FontStyle.Regular, GraphicsUnit.Point),
                AutoSize  = true,
                ForeColor = C_Text1,
                Location  = new Point(32, 28)
            };

            var lblTitle = new Label
            {
                Text      = "NixMaster",
                Font      = new Font("Segoe UI", 20, FontStyle.Bold),
                AutoSize  = true,
                ForeColor = C_Text1,
                Location  = new Point(84, 22)
            };

            var lblSub = new Label
            {
                Text      = "End-to-End Traceability Hub",
                Font      = new Font("Segoe UI", 9.5f),
                AutoSize  = true,
                ForeColor = C_Text2,
                Location  = new Point(85, 60)
            };

            pnlHeader.Controls.Add(lblSub);
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblIcon);

            // ── Body panel ────────────────────────────────────────────────────────
            var pnlBody = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = C_Dark,
                Padding   = new Padding(32, 24, 32, 24)
            };

            // Card
            var card = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = C_Card
            };
            card.Paint += (s, e) =>
            {
                using var pen = new Pen(C_Border, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                // Blue top accent
                using var br = new SolidBrush(C_Blue);
                e.Graphics.FillRectangle(br, 0, 0, card.Width, 3);
            };

            // Input label
            var lblUser = new Label
            {
                Text      = "Operator Name",
                Font      = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = C_Text2,
                AutoSize  = true,
                Location  = new Point(24, 32)
            };

            // Input box
            txtUser = new TextBox
            {
                Location    = new Point(24, 58),
                Width       = card.Width - 48,
                Anchor      = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Height      = 34,
                Font        = new Font("Segoe UI", 12),
                BackColor   = Color.FromArgb(10, 15, 25),
                ForeColor   = C_Text1,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtUser.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };

            // Login button
            btnLogin = new Button
            {
                Text      = "Login  →",
                Location  = new Point(24, 112),
                Width     = card.Width - 48,
                Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Height    = 44,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_Blue,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor    = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.Click += (s, e) => DoLogin();

            // Hover effect on login button
            btnLogin.MouseEnter += (s, e) => btnLogin.BackColor = Color.FromArgb(80, 150, 255);
            btnLogin.MouseLeave += (s, e) => btnLogin.BackColor = C_Blue;

            card.Controls.Add(lblUser);
            card.Controls.Add(txtUser);
            card.Controls.Add(btnLogin);

            pnlBody.Controls.Add(card);
            this.Controls.Add(pnlBody);
            this.Controls.Add(pnlHeader);
        }

        private void DoLogin()
        {
            string name = txtUser.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter your operator name.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppState.CurrentUser = name;
            var main = new MainForm();
            main.FormClosed += (_, __) => this.Close();
            main.Show();
            this.Hide();
        }
    }
}
