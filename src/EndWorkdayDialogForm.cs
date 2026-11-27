using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class EndWorkdayDialogForm : Form
    {
        public bool CloseApplicationRequested { get; private set; }

        public EndWorkdayDialogForm(int workedSeconds, int creditedSeconds, bool showHourBankDelta, int bankDeltaSeconds)
        {
            CloseApplicationRequested = false;

            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Jornada de Trabalho Encerrada";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            int pad = (int)Math.Round(18 * dpiScale);
            int width = (int)Math.Round(450 * dpiScale);
            int contentW = width - pad * 2;
            int y = pad;

            Label lblTitle = new Label();
            lblTitle.Text = "Jornada de trabalho encerrada com sucesso!";
            lblTitle.Location = new Point(pad, y);
            lblTitle.Size = new Size(contentW, (int)Math.Round(24 * dpiScale));
            lblTitle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(134, 239, 172);
            Controls.Add(lblTitle);
            y += (int)Math.Round(30 * dpiScale);

            string summary = "Trabalhado hoje: " + TimeFormat.FormatHms(workedSeconds);
            if (creditedSeconds > 0)
            {
                summary += "   |   Abonado: " + TimeFormat.FormatHms(creditedSeconds);
            }
            if (showHourBankDelta)
            {
                string tolNote = bankDeltaSeconds == 0 ? " (dentro da tolerância ±15m)" : string.Empty;
                summary += "\r\nSaldo BH do dia: " + TimeFormat.FormatSignedHms(bankDeltaSeconds) + tolNote;
            }

            Panel box = new Panel();
            box.Location = new Point(pad, y);
            box.Size = new Size(contentW, (int)Math.Round((showHourBankDelta ? 54 : 34) * dpiScale));
            box.BackColor = Color.FromArgb(30, 34, 43);
            box.Padding = new Padding((int)Math.Round(10 * dpiScale), (int)Math.Round(6 * dpiScale), (int)Math.Round(10 * dpiScale), (int)Math.Round(6 * dpiScale));

            Label lblSummary = new Label();
            lblSummary.Dock = DockStyle.Fill;
            lblSummary.Text = summary;
            lblSummary.Font = new Font("Consolas", 9.5f, FontStyle.Bold);
            lblSummary.ForeColor = Color.FromArgb(248, 250, 252);
            lblSummary.TextAlign = ContentAlignment.MiddleLeft;
            box.Controls.Add(lblSummary);
            Controls.Add(box);
            y += box.Height + (int)Math.Round(12 * dpiScale);

            Label lblDesc = new Label();
            lblDesc.Text = "A contagem de expediente foi finalizada e salva. Você pode continuar usando o computador sem contabilizar horas (Uso Pessoal) ou fechar o aplicativo.";
            lblDesc.Location = new Point(pad, y);
            lblDesc.Size = new Size(contentW, (int)Math.Round(42 * dpiScale));
            lblDesc.ForeColor = Color.FromArgb(148, 163, 184);
            Controls.Add(lblDesc);
            y += (int)Math.Round(50 * dpiScale);

            int gap = (int)Math.Round(10 * dpiScale);
            int btnW = (contentW - gap) / 2;
            int btnH = (int)Math.Round(38 * dpiScale);

            Button btnStayPersonal = new Button();
            btnStayPersonal.Text = "Continuar no PC (Uso Pessoal)";
            btnStayPersonal.Location = new Point(pad, y);
            btnStayPersonal.Size = new Size(btnW, btnH);
            btnStayPersonal.FlatStyle = FlatStyle.Flat;
            btnStayPersonal.FlatAppearance.BorderColor = Color.FromArgb(129, 140, 248);
            btnStayPersonal.BackColor = Color.FromArgb(49, 46, 129);
            btnStayPersonal.ForeColor = Color.White;
            btnStayPersonal.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnStayPersonal.Cursor = Cursors.Hand;
            btnStayPersonal.Click += delegate
            {
                CloseApplicationRequested = false;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnStayPersonal);

            Button btnCloseApp = new Button();
            btnCloseApp.Text = "Fechar Aplicativo";
            btnCloseApp.Location = new Point(pad + btnW + gap, y);
            btnCloseApp.Size = new Size(btnW, btnH);
            btnCloseApp.FlatStyle = FlatStyle.Flat;
            btnCloseApp.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btnCloseApp.BackColor = Color.FromArgb(39, 45, 58);
            btnCloseApp.ForeColor = Color.FromArgb(241, 245, 249);
            btnCloseApp.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnCloseApp.Cursor = Cursors.Hand;
            btnCloseApp.Click += delegate
            {
                CloseApplicationRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnCloseApp);

            AcceptButton = btnStayPersonal;
            ClientSize = new Size(width, y + btnH + pad);

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
