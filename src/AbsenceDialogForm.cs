using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class AbsenceDialogForm : Form
    {
        private readonly ComboBox _cmbType;
        private readonly DateTimePicker _dtpStart;
        private readonly DateTimePicker _dtpEnd;
        private readonly Label _lblDuration;
        private readonly CheckBox _chkCredited;
        private readonly TextBox _txtNote;
        private readonly bool _editTypeOnly;
        private readonly bool _quickNoteOnly;
        private bool _userToggledCredit;

        public string SelectedType { get; private set; }
        public DateTime SelectedStart { get; private set; }
        public DateTime SelectedEnd { get; private set; }
        public string EnteredNote { get; private set; }
        public bool SelectedCredited { get; private set; }

        private static readonly string[] TypeCodes = new string[]
        {
            AbsenceTypes.Intervalo,
            AbsenceTypes.Medico,
            AbsenceTypes.PausaRapida,
            AbsenceTypes.BloqueioTela,
            AbsenceTypes.Outro
        };

        private static readonly string[] TypeLabels = new string[]
        {
            "Intervalo / Almoço",
            "Ida ao Médico",
            "Pausa Rápida",
            "Bloqueio de Tela",
            "Outra Ausência"
        };

        public AbsenceDialogForm(
            string dialogTitle,
            string initialType,
            DateTime initialStart,
            DateTime initialEnd,
            string initialNote,
            bool initialCredited,
            bool editTypeOnly,
            bool quickNoteOnly)
        {
            _editTypeOnly = editTypeOnly;
            _quickNoteOnly = quickNoteOnly;
            _userToggledCredit = false;

            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = dialogTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            int pad = (int)Math.Round(16 * dpiScale);
            int width = (int)Math.Round(410 * dpiScale);
            int fieldWidth = width - (pad * 2);
            int y = pad;

            Label lblType = new Label();
            lblType.Text = "Tipo de ausência:";
            lblType.Location = new Point(pad, y);
            lblType.AutoSize = true;
            lblType.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(lblType);
            y += (int)Math.Round(22 * dpiScale);

            _cmbType = new ComboBox();
            _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbType.Location = new Point(pad, y);
            _cmbType.Width = fieldWidth;
            _cmbType.BackColor = Color.FromArgb(36, 41, 52);
            _cmbType.ForeColor = Color.FromArgb(248, 250, 252);
            for (int i = 0; i < TypeLabels.Length; i++)
            {
                _cmbType.Items.Add(TypeLabels[i]);
            }
            int selIdx = 0;
            for (int i = 0; i < TypeCodes.Length; i++)
            {
                if (TypeCodes[i] == initialType)
                {
                    selIdx = i;
                    break;
                }
            }
            _cmbType.SelectedIndex = selIdx;
            Controls.Add(_cmbType);
            y += (int)Math.Round(34 * dpiScale);

            _dtpStart = new DateTimePicker();
            _dtpEnd = new DateTimePicker();
            _lblDuration = new Label();

            if (!editTypeOnly && !quickNoteOnly)
            {
                int halfW = (fieldWidth - (int)Math.Round(12 * dpiScale)) / 2;

                Label lblStart = new Label();
                lblStart.Text = "Início (HH:mm):";
                lblStart.Location = new Point(pad, y);
                lblStart.AutoSize = true;
                lblStart.ForeColor = Color.FromArgb(203, 213, 225);
                Controls.Add(lblStart);

                Label lblEnd = new Label();
                lblEnd.Text = "Fim (HH:mm):";
                lblEnd.Location = new Point(pad + halfW + (int)Math.Round(12 * dpiScale), y);
                lblEnd.AutoSize = true;
                lblEnd.ForeColor = Color.FromArgb(203, 213, 225);
                Controls.Add(lblEnd);
                y += (int)Math.Round(22 * dpiScale);

                _dtpStart.Format = DateTimePickerFormat.Custom;
                _dtpStart.CustomFormat = "HH:mm";
                _dtpStart.ShowUpDown = true;
                _dtpStart.Location = new Point(pad, y);
                _dtpStart.Width = halfW;
                _dtpStart.Value = initialStart;
                _dtpStart.ValueChanged += delegate { UpdateDurationPreview(); };
                Controls.Add(_dtpStart);

                _dtpEnd.Format = DateTimePickerFormat.Custom;
                _dtpEnd.CustomFormat = "HH:mm";
                _dtpEnd.ShowUpDown = true;
                _dtpEnd.Location = new Point(pad + halfW + (int)Math.Round(12 * dpiScale), y);
                _dtpEnd.Width = halfW;
                _dtpEnd.Value = initialEnd;
                _dtpEnd.ValueChanged += delegate { UpdateDurationPreview(); };
                Controls.Add(_dtpEnd);
                y += (int)Math.Round(30 * dpiScale);

                _lblDuration.Location = new Point(pad, y);
                _lblDuration.AutoSize = true;
                _lblDuration.ForeColor = Color.FromArgb(192, 132, 252);
                _lblDuration.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                Controls.Add(_lblDuration);
                y += (int)Math.Round(24 * dpiScale);

                Label lblOverwriteInfo = new Label();
                lblOverwriteInfo.Text = "Obs.: bloqueios e sessões dentro deste período serão sobrescritos automaticamente.";
                lblOverwriteInfo.Location = new Point(pad, y);
                lblOverwriteInfo.Size = new Size(fieldWidth, (int)Math.Round(28 * dpiScale));
                lblOverwriteInfo.ForeColor = Color.FromArgb(148, 163, 184);
                lblOverwriteInfo.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
                Controls.Add(lblOverwriteInfo);
                y += (int)Math.Round(30 * dpiScale);

                UpdateDurationPreview();
            }

            _chkCredited = new CheckBox();
            _chkCredited.Text = "Abonar no Banco de Horas (ausência legal — não desconta BH)";
            _chkCredited.Location = new Point(pad, y);
            _chkCredited.AutoSize = true;
            _chkCredited.ForeColor = Color.FromArgb(134, 239, 172);
            _chkCredited.Checked = initialCredited;
            _chkCredited.Click += delegate { _userToggledCredit = true; };
            Controls.Add(_chkCredited);
            y += (int)Math.Round(28 * dpiScale);

            _cmbType.SelectedIndexChanged += delegate
            {
                if (!_userToggledCredit)
                {
                    int idx = _cmbType.SelectedIndex;
                    if (idx >= 0 && idx < TypeCodes.Length)
                    {
                        _chkCredited.Checked = AbsenceTypes.IsCreditedByDefault(TypeCodes[idx]);
                    }
                }
            };

            Label lblNote = new Label();
            lblNote.Text = "Observação (opcional):";
            lblNote.Location = new Point(pad, y);
            lblNote.AutoSize = true;
            lblNote.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(lblNote);
            y += (int)Math.Round(22 * dpiScale);

            _txtNote = new TextBox();
            _txtNote.Location = new Point(pad, y);
            _txtNote.Width = fieldWidth;
            _txtNote.BackColor = Color.FromArgb(36, 41, 52);
            _txtNote.ForeColor = Color.FromArgb(248, 250, 252);
            _txtNote.BorderStyle = BorderStyle.FixedSingle;
            _txtNote.Text = initialNote ?? string.Empty;
            Controls.Add(_txtNote);
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
            btnOk.Click += OnSaveClick;
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            ClientSize = new Size(width, y + btnH + pad);

            ResumeLayout(false);
            PerformLayout();
        }

        private void UpdateDurationPreview()
        {
            DateTime s = NormalizeToToday(_dtpStart.Value);
            DateTime e = NormalizeToToday(_dtpEnd.Value);
            int sec = (int)(e - s).TotalSeconds;
            if (sec <= 0)
            {
                _lblDuration.Text = "O horário final deve ser maior que o inicial.";
                _lblDuration.ForeColor = Color.FromArgb(248, 113, 113);
            }
            else
            {
                _lblDuration.Text = "Duração registrada: " + TimeFormat.FormatHms(sec);
                _lblDuration.ForeColor = Color.FromArgb(192, 132, 252);
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            int idx = _cmbType.SelectedIndex;
            if (idx < 0 || idx >= TypeCodes.Length) idx = 0;
            SelectedType = TypeCodes[idx];
            EnteredNote = _txtNote.Text.Trim();
            SelectedCredited = _chkCredited.Checked;

            if (!_editTypeOnly && !_quickNoteOnly)
            {
                SelectedStart = NormalizeToToday(_dtpStart.Value);
                SelectedEnd = NormalizeToToday(_dtpEnd.Value);
                if (SelectedEnd <= SelectedStart)
                {
                    MessageBox.Show(
                        "Informe um horário final maior que o horário inicial.",
                        "Lançar Ausência",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static DateTime NormalizeToToday(DateTime value)
        {
            DateTime today = DateTime.Today;
            return new DateTime(today.Year, today.Month, today.Day, value.Hour, value.Minute, 0);
        }
    }
}
