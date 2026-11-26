using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class WellnessSettingsForm : Form
    {
        private readonly CheckBox _chkWellnessEnabled;
        private readonly ComboBox _cmbInterval;
        private readonly CheckBox _chkSilenceMeeting;
        private readonly CheckBox _chkPreRegular;
        private readonly CheckBox _chkFatigue;
        private readonly CheckBox _chkMuteToday;

        public bool WellnessEnabled { get; private set; }
        public int WellnessIntervalMinutes { get; private set; }
        public bool SilenceInMeeting { get; private set; }
        public bool PreRegularEnabled { get; private set; }
        public bool FatigueEnabled { get; private set; }
        public bool MuteToday { get; private set; }

        private static readonly int[] Intervals = new int[] { 30, 45, 60 };

        public WellnessSettingsForm(AppConfig config, bool isMutedToday)
        {
            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Bem-Estar e Lembretes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            int pad = (int)Math.Round(18 * dpiScale);
            int width = (int)Math.Round(520 * dpiScale);
            int y = pad;

            Label lblHeader = new Label();
            lblHeader.Text = "Lembretes sensíveis ao contexto (só contam em tempo ativo)";
            lblHeader.Location = new Point(pad, y);
            lblHeader.Size = new Size(width - pad * 2, (int)Math.Round(22 * dpiScale));
            lblHeader.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblHeader.ForeColor = Color.FromArgb(125, 211, 252);
            Controls.Add(lblHeader);
            y += (int)Math.Round(28 * dpiScale);

            _chkWellnessEnabled = new CheckBox();
            _chkWellnessEnabled.Text = "Ativar lembrete de hidratação / alongamento a cada:";
            _chkWellnessEnabled.Location = new Point(pad, y + (int)Math.Round(2 * dpiScale));
            _chkWellnessEnabled.AutoSize = true;
            _chkWellnessEnabled.Checked = config.WellnessReminderEnabled;
            Controls.Add(_chkWellnessEnabled);

            _cmbInterval = new ComboBox();
            _cmbInterval.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbInterval.Location = new Point(width - pad - (int)Math.Round(96 * dpiScale), y);
            _cmbInterval.Width = (int)Math.Round(96 * dpiScale);
            _cmbInterval.BackColor = Color.FromArgb(36, 41, 52);
            _cmbInterval.ForeColor = Color.FromArgb(248, 250, 252);
            _cmbInterval.Items.Add("30 min");
            _cmbInterval.Items.Add("45 min");
            _cmbInterval.Items.Add("60 min");
            int selIdx = 1;
            for (int i = 0; i < Intervals.Length; i++)
            {
                if (Intervals[i] == config.WellnessIntervalMinutes)
                {
                    selIdx = i;
                    break;
                }
            }
            _cmbInterval.SelectedIndex = selIdx;
            _cmbInterval.Enabled = _chkWellnessEnabled.Checked;
            _chkWellnessEnabled.CheckedChanged += delegate { _cmbInterval.Enabled = _chkWellnessEnabled.Checked; };
            Controls.Add(_cmbInterval);
            _cmbInterval.BringToFront();
            y += (int)Math.Round(34 * dpiScale);

            Label lblResetHint = new Label();
            lblResetHint.Text = "Pausas naturais ou bloqueios de tela ≥ 5 min zeram o contador automaticamente.";
            lblResetHint.Location = new Point(pad + (int)Math.Round(18 * dpiScale), y);
            lblResetHint.Size = new Size(width - pad * 2 - (int)Math.Round(18 * dpiScale), (int)Math.Round(20 * dpiScale));
            lblResetHint.ForeColor = Color.FromArgb(148, 163, 184);
            lblResetHint.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            Controls.Add(lblResetHint);
            y += (int)Math.Round(26 * dpiScale);

            _chkSilenceMeeting = new CheckBox();
            _chkSilenceMeeting.Text = "Silenciar avisos durante o Modo Reunião (disparar ao encerrar)";
            _chkSilenceMeeting.Location = new Point(pad, y);
            _chkSilenceMeeting.AutoSize = true;
            _chkSilenceMeeting.Checked = config.SilenceWellnessInMeeting;
            Controls.Add(_chkSilenceMeeting);
            y += (int)Math.Round(28 * dpiScale);

            _chkPreRegular = new CheckBox();
            _chkPreRegular.Text = "Alertar 15 min antes do fim da jornada normal (com resumo da semana)";
            _chkPreRegular.Location = new Point(pad, y);
            _chkPreRegular.AutoSize = true;
            _chkPreRegular.Checked = config.PreRegularAlertEnabled;
            Controls.Add(_chkPreRegular);
            y += (int)Math.Round(28 * dpiScale);

            _chkFatigue = new CheckBox();
            _chkFatigue.Text = "Alerta preventivo de fadiga (após 19h30 ou 2+ dias seguidos de extra)";
            _chkFatigue.Location = new Point(pad, y);
            _chkFatigue.AutoSize = true;
            _chkFatigue.Checked = config.FatigueAlertEnabled;
            Controls.Add(_chkFatigue);
            y += (int)Math.Round(30 * dpiScale);

            _chkMuteToday = new CheckBox();
            _chkMuteToday.Text = "Silenciar lembretes de hidratação/alongamento pelo resto de hoje";
            _chkMuteToday.Location = new Point(pad, y);
            _chkMuteToday.AutoSize = true;
            _chkMuteToday.ForeColor = Color.FromArgb(250, 204, 21);
            _chkMuteToday.Checked = isMutedToday;
            Controls.Add(_chkMuteToday);
            y += (int)Math.Round(38 * dpiScale);

            int btnW = (int)Math.Round(100 * dpiScale);
            int btnH = (int)Math.Round(30 * dpiScale);

            Button btnCancel = new Button();
            btnCancel.Text = "Cancelar";
            btnCancel.Size = new Size(btnW, btnH);
            btnCancel.Location = new Point(width - pad - btnW, y);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btnCancel.BackColor = Color.FromArgb(39, 45, 58);
            btnCancel.ForeColor = Color.FromArgb(241, 245, 249);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Button btnOk = new Button();
            btnOk.Text = "Salvar";
            btnOk.Size = new Size(btnW, btnH);
            btnOk.Location = new Point(btnCancel.Left - (int)Math.Round(8 * dpiScale) - btnW, y);
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderColor = Color.FromArgb(34, 197, 94);
            btnOk.BackColor = Color.FromArgb(22, 101, 52);
            btnOk.ForeColor = Color.White;
            btnOk.Click += delegate
            {
                WellnessEnabled = _chkWellnessEnabled.Checked;
                int idx = _cmbInterval.SelectedIndex;
                WellnessIntervalMinutes = (idx >= 0 && idx < Intervals.Length) ? Intervals[idx] : 45;
                SilenceInMeeting = _chkSilenceMeeting.Checked;
                PreRegularEnabled = _chkPreRegular.Checked;
                FatigueEnabled = _chkFatigue.Checked;
                MuteToday = _chkMuteToday.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ClientSize = new Size(width, y + btnH + pad);

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
