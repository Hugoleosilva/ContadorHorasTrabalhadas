using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class StartupModeDialogForm : Form
    {
        public bool SelectedPersonalUse { get; private set; }

        public StartupModeDialogForm()
        {
            SelectedPersonalUse = false;

            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            bool isWeekend = !HourBankCalculator.IsWeekday(DateTime.Today);

            Text = "Relógio Acompanhamento — Modo de Acesso";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            int pad = (int)Math.Round(18 * dpiScale);
            int width = (int)Math.Round(430 * dpiScale);
            int y = pad;

            Label lblTitle = new Label();
            lblTitle.Text = "Como você está acessando o computador agora?";
            lblTitle.Location = new Point(pad, y);
            lblTitle.Size = new Size(width - pad * 2, (int)Math.Round(24 * dpiScale));
            lblTitle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(248, 250, 252);
            Controls.Add(lblTitle);
            y += (int)Math.Round(30 * dpiScale);

            Label lblDesc = new Label();
            lblDesc.Text = isWeekend
                ? "Hoje é fim de semana (o Banco de Horas considera apenas segunda a sexta). Escolha Trabalho apenas se realmente for registrar expediente."
                : "Escolha Trabalho para iniciar o relógio de expediente, ou Uso Pessoal (hobby, curso, estudos) para não gerar horas trabalhadas.";
            lblDesc.Location = new Point(pad, y);
            lblDesc.Size = new Size(width - pad * 2, (int)Math.Round(44 * dpiScale));
            lblDesc.ForeColor = isWeekend ? Color.FromArgb(250, 204, 21) : Color.FromArgb(148, 163, 184);
            Controls.Add(lblDesc);
            y += (int)Math.Round(54 * dpiScale);

            int btnW = (width - pad * 2 - (int)Math.Round(10 * dpiScale)) / 2;
            int btnH = (int)Math.Round(42 * dpiScale);

            Button btnWork = new Button();
            btnWork.Text = "Iniciar Trabalho";
            btnWork.Location = new Point(pad, y);
            btnWork.Size = new Size(btnW, btnH);
            btnWork.FlatStyle = FlatStyle.Flat;
            btnWork.FlatAppearance.BorderColor = Color.FromArgb(34, 197, 94);
            btnWork.BackColor = Color.FromArgb(22, 101, 52);
            btnWork.ForeColor = Color.White;
            btnWork.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnWork.Cursor = Cursors.Hand;
            btnWork.Click += delegate
            {
                SelectedPersonalUse = false;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnWork);

            Button btnPersonal = new Button();
            btnPersonal.Text = "Uso Pessoal / Hobby / Curso";
            btnPersonal.Location = new Point(pad + btnW + (int)Math.Round(10 * dpiScale), y);
            btnPersonal.Size = new Size(btnW, btnH);
            btnPersonal.FlatStyle = FlatStyle.Flat;
            btnPersonal.FlatAppearance.BorderColor = Color.FromArgb(129, 140, 248);
            btnPersonal.BackColor = Color.FromArgb(49, 46, 129);
            btnPersonal.ForeColor = Color.White;
            btnPersonal.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnPersonal.Cursor = Cursors.Hand;
            btnPersonal.Click += delegate
            {
                SelectedPersonalUse = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnPersonal);

            AcceptButton = isWeekend ? btnPersonal : btnWork;
            ClientSize = new Size(width, y + btnH + pad);

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
