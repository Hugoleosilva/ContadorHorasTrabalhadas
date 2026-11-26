using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class DayOverrideDialogForm : Form
    {
        private readonly DateTimePicker _dtpDate;
        private readonly ComboBox _cmbType;
        private readonly Label _lblHoursLabel;
        private readonly DateTimePicker _dtpHours;
        private readonly TextBox _txtNote;

        public DayOverrideRecord ResultRecord { get; private set; }
        public bool DeleteRequested { get; private set; }
        public string SelectedWorkDate { get; private set; }

        private static readonly string[] TypeCodes = new string[]
        {
            DaySpecialTypes.Feriado,
            DaySpecialTypes.Atestado,
            DaySpecialTypes.AbonadoTotal,
            DaySpecialTypes.AbonadoParcial4h,
            DaySpecialTypes.ImprensadoAbonado,
            DaySpecialTypes.ImprensadoDesconto,
            DaySpecialTypes.DescontoConfirmado,
            DaySpecialTypes.EsqueciAbrirApp,
            DaySpecialTypes.Outro
        };

        private static readonly string[] TypeLabels = new string[]
        {
            "Feriado (abona 8h — saldo 00:00)",
            "Atestado — dia inteiro (abona 8h)",
            "Abonado Totalmente (abona 8h)",
            "Abonado Parcialmente — meio expediente (abona 4h)",
            "Imprensado — Abonado (abona 8h)",
            "Imprensado — Desconto 8h no BH",
            "Desconto Confirmado / Folga BH (-8h)",
            "Esqueci de abrir o app (informar horas trabalhadas)",
            "Outra Situação (informar horas abonadas)"
        };

        public DayOverrideDialogForm(DateTime initialDate, DayOverrideRecord existing)
        {
            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Apontar / Classificar Dia no Banco de Horas";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            int pad = (int)Math.Round(16 * dpiScale);
            int width = (int)Math.Round(450 * dpiScale);
            int fieldW = width - pad * 2;
            int y = pad;

            Label lblDate = new Label();
            lblDate.Text = "Data (segunda a sexta):";
            lblDate.Location = new Point(pad, y);
            lblDate.AutoSize = true;
            lblDate.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(lblDate);
            y += (int)Math.Round(22 * dpiScale);

            _dtpDate = new DateTimePicker();
            _dtpDate.Format = DateTimePickerFormat.Short;
            _dtpDate.Location = new Point(pad, y);
            _dtpDate.Width = (int)Math.Round(150 * dpiScale);
            _dtpDate.Value = initialDate.Date;
            Controls.Add(_dtpDate);
            y += (int)Math.Round(34 * dpiScale);

            Label lblType = new Label();
            lblType.Text = "Situação do dia:";
            lblType.Location = new Point(pad, y);
            lblType.AutoSize = true;
            lblType.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(lblType);
            y += (int)Math.Round(22 * dpiScale);

            _cmbType = new ComboBox();
            _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbType.Location = new Point(pad, y);
            _cmbType.Width = fieldW;
            _cmbType.BackColor = Color.FromArgb(36, 41, 52);
            _cmbType.ForeColor = Color.FromArgb(248, 250, 252);
            for (int i = 0; i < TypeLabels.Length; i++)
            {
                _cmbType.Items.Add(TypeLabels[i]);
            }

            int selIdx = 0;
            if (existing != null)
            {
                for (int i = 0; i < TypeCodes.Length; i++)
                {
                    if (TypeCodes[i] == existing.Type)
                    {
                        selIdx = i;
                        break;
                    }
                }
            }
            _cmbType.SelectedIndex = selIdx;
            _cmbType.SelectedIndexChanged += delegate { UpdateHoursInputState(); };
            Controls.Add(_cmbType);
            y += (int)Math.Round(34 * dpiScale);

            _lblHoursLabel = new Label();
            _lblHoursLabel.Text = "Horas (HH:mm):";
            _lblHoursLabel.Location = new Point(pad, y);
            _lblHoursLabel.AutoSize = true;
            _lblHoursLabel.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(_lblHoursLabel);
            y += (int)Math.Round(22 * dpiScale);

            _dtpHours = new DateTimePicker();
            _dtpHours.Format = DateTimePickerFormat.Custom;
            _dtpHours.CustomFormat = "HH:mm";
            _dtpHours.ShowUpDown = true;
            _dtpHours.Location = new Point(pad, y);
            _dtpHours.Width = (int)Math.Round(120 * dpiScale);

            int initSec = 8 * 3600;
            if (existing != null)
            {
                if (existing.Type == DaySpecialTypes.EsqueciAbrirApp && existing.ManualWorkedSeconds > 0)
                {
                    initSec = existing.ManualWorkedSeconds;
                }
                else if (existing.Type == DaySpecialTypes.Outro)
                {
                    initSec = existing.CreditedSeconds;
                }
            }
            int ih = Math.Min(23, Math.Max(0, initSec / 3600));
            int im = Math.Min(59, Math.Max(0, (initSec % 3600) / 60));
            _dtpHours.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, ih, im, 0);
            Controls.Add(_dtpHours);
            y += (int)Math.Round(34 * dpiScale);

            Label lblNote = new Label();
            lblNote.Text = "Observação (opcional):";
            lblNote.Location = new Point(pad, y);
            lblNote.AutoSize = true;
            lblNote.ForeColor = Color.FromArgb(203, 213, 225);
            Controls.Add(lblNote);
            y += (int)Math.Round(22 * dpiScale);

            _txtNote = new TextBox();
            _txtNote.Location = new Point(pad, y);
            _txtNote.Width = fieldW;
            _txtNote.BackColor = Color.FromArgb(36, 41, 52);
            _txtNote.ForeColor = Color.FromArgb(248, 250, 252);
            _txtNote.BorderStyle = BorderStyle.FixedSingle;
            _txtNote.Text = existing != null ? existing.Note : string.Empty;
            Controls.Add(_txtNote);
            y += (int)Math.Round(38 * dpiScale);

            int btnW = (int)Math.Round(100 * dpiScale);
            int btnH = (int)Math.Round(30 * dpiScale);

            if (existing != null)
            {
                Button btnRemove = new Button();
                btnRemove.Text = "Remover Apontamento";
                btnRemove.Size = new Size((int)Math.Round(155 * dpiScale), btnH);
                btnRemove.Location = new Point(pad, y);
                btnRemove.FlatStyle = FlatStyle.Flat;
                btnRemove.FlatAppearance.BorderColor = Color.FromArgb(220, 38, 38);
                btnRemove.BackColor = Color.FromArgb(69, 26, 26);
                btnRemove.ForeColor = Color.FromArgb(254, 202, 202);
                btnRemove.Click += delegate
                {
                    DeleteRequested = true;
                    SelectedWorkDate = _dtpDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(btnRemove);
            }

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

            UpdateHoursInputState();
            ResumeLayout(false);
            PerformLayout();
        }

        private void UpdateHoursInputState()
        {
            string code = GetSelectedTypeCode();
            if (code == DaySpecialTypes.EsqueciAbrirApp)
            {
                _lblHoursLabel.Text = "Horas trabalhadas no dia (HH:mm):";
                _dtpHours.Enabled = true;
            }
            else if (code == DaySpecialTypes.Outro)
            {
                _lblHoursLabel.Text = "Horas abonadas no dia (HH:mm — 00:00 para desconto):";
                _dtpHours.Enabled = true;
            }
            else
            {
                _lblHoursLabel.Text = "Horas definidas automaticamente pela situação acima:";
                _dtpHours.Enabled = false;
            }
        }

        private string GetSelectedTypeCode()
        {
            int idx = _cmbType.SelectedIndex;
            if (idx < 0 || idx >= TypeCodes.Length) return DaySpecialTypes.Feriado;
            return TypeCodes[idx];
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            DateTime date = _dtpDate.Value.Date;
            if (!HourBankCalculator.IsWeekday(date))
            {
                MessageBox.Show(
                    "O Banco de Horas considera apenas dias úteis (segunda a sexta).",
                    "Banco de Horas",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string code = GetSelectedTypeCode();
            int creditedSec = 0;
            int manualWorkedSec = 0;
            int pickedSec = _dtpHours.Value.Hour * 3600 + _dtpHours.Value.Minute * 60;

            switch (code)
            {
                case DaySpecialTypes.Feriado:
                case DaySpecialTypes.Atestado:
                case DaySpecialTypes.AbonadoTotal:
                case DaySpecialTypes.ImprensadoAbonado:
                    creditedSec = 8 * 3600;
                    break;
                case DaySpecialTypes.AbonadoParcial4h:
                    creditedSec = 4 * 3600;
                    break;
                case DaySpecialTypes.ImprensadoDesconto:
                case DaySpecialTypes.DescontoConfirmado:
                    creditedSec = 0;
                    break;
                case DaySpecialTypes.EsqueciAbrirApp:
                    manualWorkedSec = pickedSec;
                    break;
                case DaySpecialTypes.Outro:
                    creditedSec = pickedSec;
                    break;
            }

            DayOverrideRecord rec = new DayOverrideRecord();
            rec.WorkDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            rec.Type = code;
            rec.CreditedSeconds = creditedSec;
            rec.ManualWorkedSeconds = manualWorkedSec;
            rec.Note = _txtNote.Text.Trim();

            SelectedWorkDate = rec.WorkDate;
            ResultRecord = rec;
            DeleteRequested = false;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
