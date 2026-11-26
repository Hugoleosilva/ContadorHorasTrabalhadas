using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class HistoryForm : Form
    {
        private readonly TimeTracker _tracker;
        private readonly CsvStorage _storage;
        private readonly AppConfig _config;

        private readonly Label _lblTodayTotal;
        private readonly Label _lblTodayRegular;
        private readonly Label _lblTodayExtra;
        private readonly Label _lblTodayAbsences;
        private readonly Label _lblTodayStatus;
        private readonly Label _lblAbsenceBreakdown;

        private readonly DataGridView _gridSessions;
        private readonly DataGridView _gridAbsences;
        private readonly DataGridView _gridDaily;

        // Aba Banco de Horas
        private readonly Panel _bankContentPanel;
        private readonly Label _lblBankDisabledNotice;
        private readonly Label _lblBankWeekTitle;
        private readonly Label _lblBankWeekBalance;
        private readonly Label _lblBankWeekComp;
        private readonly Label _lblBankMonthTitle;
        private readonly Label _lblBankMonthBalance;
        private readonly Label _lblBankMonthComp;
        private readonly Label _lblBankCycleTitle;
        private readonly Label _lblBankCycleBalance;
        private readonly Label _lblBankCycleComp;
        private readonly ComboBox _cmbBankScope;
        private readonly DataGridView _gridBank;
        private string _lastBankSignature;

        public event EventHandler DataModified;

        public HistoryForm(TimeTracker tracker, CsvStorage storage, AppConfig config)
        {
            _tracker = tracker;
            _storage = storage;
            _config = config;

            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Relógio Acompanhamento — Registros, Ausências e Banco de Horas";
            ClientSize = new Size((int)Math.Round(840 * dpiScale), (int)Math.Round(540 * dpiScale));
            MinimumSize = new Size((int)Math.Round(700 * dpiScale), (int)Math.Round(450 * dpiScale));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Painel superior com 5 cartões de resumo do dia
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = (int)Math.Round(88 * dpiScale);
            headerPanel.Padding = new Padding(
                (int)Math.Round(12 * dpiScale),
                (int)Math.Round(8 * dpiScale),
                (int)Math.Round(12 * dpiScale),
                (int)Math.Round(8 * dpiScale));
            headerPanel.BackColor = Color.FromArgb(30, 34, 43);

            TableLayoutPanel summaryLayout = new TableLayoutPanel();
            summaryLayout.Dock = DockStyle.Fill;
            summaryLayout.ColumnCount = 5;
            summaryLayout.RowCount = 1;
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++)
            {
                summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            }

            _lblTodayTotal = CreateSummaryCard(summaryLayout, 0, "TOTAL HOJE", "00:00:00", Color.FromArgb(248, 250, 252), dpiScale);
            _lblTodayRegular = CreateSummaryCard(summaryLayout, 1, "HORAS NORMAIS", "00:00:00", Color.FromArgb(34, 197, 94), dpiScale);
            _lblTodayExtra = CreateSummaryCard(summaryLayout, 2, "HORAS EXTRAS", "00:00:00", Color.FromArgb(249, 115, 22), dpiScale);
            _lblTodayAbsences = CreateSummaryCard(summaryLayout, 3, "AUSÊNCIAS HOJE", "00:00:00", Color.FromArgb(192, 132, 252), dpiScale);
            _lblTodayStatus = CreateSummaryCard(summaryLayout, 4, "STATUS", "Normal", Color.FromArgb(56, 189, 248), dpiScale);

            headerPanel.Controls.Add(summaryLayout);

            // Abas: Sessões de Hoje | Ausências / Intervalos | Registros Diários | Banco de Horas
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(12, 6);

            // Aba 1: Sessões de Hoje
            TabPage tabToday = new TabPage("Sessões de Hoje");
            tabToday.BackColor = Color.FromArgb(24, 26, 32);
            tabToday.Padding = new Padding(8);

            _gridSessions = CreateStyledGrid();
            _gridSessions.Columns.Add("Inicio", "Início");
            _gridSessions.Columns.Add("Fim", "Fim");
            _gridSessions.Columns.Add("Duracao", "Duração");
            _gridSessions.Columns.Add("Motivo", "Evento / Status");
            tabToday.Controls.Add(_gridSessions);

            // Aba 2: Ausências / Intervalos
            TabPage tabAbsences = new TabPage("Ausências / Intervalos");
            tabAbsences.BackColor = Color.FromArgb(24, 26, 32);
            tabAbsences.Padding = new Padding(8);

            Panel absToolbar = new Panel();
            absToolbar.Dock = DockStyle.Top;
            absToolbar.Height = (int)Math.Round(38 * dpiScale);
            absToolbar.Padding = new Padding(0, 0, 0, (int)Math.Round(6 * dpiScale));

            Button btnAddAbsence = CreateActionButton("+ Lançar Ausência", (int)Math.Round(150 * dpiScale));
            btnAddAbsence.Dock = DockStyle.Left;
            btnAddAbsence.FlatAppearance.BorderColor = Color.FromArgb(168, 85, 247);
            btnAddAbsence.Click += delegate { OpenAddAbsenceDialog(); };

            Panel absSp1 = new Panel();
            absSp1.Dock = DockStyle.Left;
            absSp1.Width = (int)Math.Round(8 * dpiScale);

            Button btnEditAbsence = CreateActionButton("Justificar / Editar", (int)Math.Round(145 * dpiScale));
            btnEditAbsence.Dock = DockStyle.Left;
            btnEditAbsence.Click += delegate { OpenEditSelectedAbsenceDialog(); };

            Panel absSp2 = new Panel();
            absSp2.Dock = DockStyle.Left;
            absSp2.Width = (int)Math.Round(8 * dpiScale);

            Button btnDeleteAbsence = CreateActionButton("Excluir", (int)Math.Round(80 * dpiScale));
            btnDeleteAbsence.Dock = DockStyle.Left;
            btnDeleteAbsence.Click += delegate { DeleteSelectedAbsence(); };

            Label lblInlineHint = new Label();
            lblInlineHint.Text = "Duplo clique para justificar • Lançar ausência sobrescreve bloqueios/sessões no período";
            lblInlineHint.Dock = DockStyle.Fill;
            lblInlineHint.TextAlign = ContentAlignment.MiddleRight;
            lblInlineHint.ForeColor = Color.FromArgb(148, 163, 184);
            lblInlineHint.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            lblInlineHint.AutoEllipsis = true;

            absToolbar.Controls.Add(lblInlineHint);
            absToolbar.Controls.Add(btnDeleteAbsence);
            absToolbar.Controls.Add(absSp2);
            absToolbar.Controls.Add(btnEditAbsence);
            absToolbar.Controls.Add(absSp1);
            absToolbar.Controls.Add(btnAddAbsence);

            Panel absSummaryPanel = new Panel();
            absSummaryPanel.Dock = DockStyle.Bottom;
            absSummaryPanel.Height = (int)Math.Round(30 * dpiScale);
            absSummaryPanel.Padding = new Padding(4, (int)Math.Round(6 * dpiScale), 4, 0);

            _lblAbsenceBreakdown = new Label();
            _lblAbsenceBreakdown.Dock = DockStyle.Fill;
            _lblAbsenceBreakdown.ForeColor = Color.FromArgb(203, 213, 225);
            _lblAbsenceBreakdown.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            _lblAbsenceBreakdown.TextAlign = ContentAlignment.MiddleLeft;
            _lblAbsenceBreakdown.AutoEllipsis = true;
            absSummaryPanel.Controls.Add(_lblAbsenceBreakdown);

            _gridAbsences = CreateStyledGrid();
            _gridAbsences.Columns.Add("Inicio", "Início");
            _gridAbsences.Columns.Add("Fim", "Fim");
            _gridAbsences.Columns.Add("Duracao", "Duração");
            _gridAbsences.Columns.Add("Tipo", "Tipo");
            _gridAbsences.Columns.Add("Abonado", "Abona BH");
            _gridAbsences.Columns.Add("Observacao", "Observação");
            _gridAbsences.Columns["Abonado"].FillWeight = 60;
            _gridAbsences.CellDoubleClick += delegate { OpenEditSelectedAbsenceDialog(); };

            tabAbsences.Controls.Add(_gridAbsences);
            tabAbsences.Controls.Add(absToolbar);
            tabAbsences.Controls.Add(absSummaryPanel);

            // Aba 3: Registros Diários (CSV)
            TabPage tabDaily = new TabPage("Registros Diários (CSV)");
            tabDaily.BackColor = Color.FromArgb(24, 26, 32);
            tabDaily.Padding = new Padding(8);

            _gridDaily = CreateStyledGrid();
            _gridDaily.Columns.Add("Data", "Data");
            _gridDaily.Columns.Add("PrimeiroInicio", "1º Login");
            _gridDaily.Columns.Add("UltimoRegistro", "Último Reg.");
            _gridDaily.Columns.Add("Total", "Total");
            _gridDaily.Columns.Add("Normais", "Normais");
            _gridDaily.Columns.Add("Extras", "Extras");
            _gridDaily.Columns.Add("Ausencias", "Ausências");
            _gridDaily.Columns.Add("Status", "Status Limite");
            tabDaily.Controls.Add(_gridDaily);

            // Aba 4: Banco de Horas (Semanal / Mensal 11-10 / Ciclo 6 meses)
            TabPage tabBank = new TabPage("Banco de Horas");
            tabBank.BackColor = Color.FromArgb(24, 26, 32);
            tabBank.Padding = new Padding(8);

            _lblBankDisabledNotice = new Label();
            _lblBankDisabledNotice.Dock = DockStyle.Fill;
            _lblBankDisabledNotice.TextAlign = ContentAlignment.MiddleCenter;
            _lblBankDisabledNotice.ForeColor = Color.FromArgb(148, 163, 184);
            _lblBankDisabledNotice.Font = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            _lblBankDisabledNotice.Text = "O Banco de Horas está disponível apenas para a jornada padrão de 8h diárias (CLT).\nNo perfil Estagiário (6h), não há banco de horas nem horas extras.";
            _lblBankDisabledNotice.Visible = false;

            _bankContentPanel = new Panel();
            _bankContentPanel.Dock = DockStyle.Fill;

            // Cards de Saldo: Semanal | Mensal (11-10) | Ciclo 6 Meses
            Panel bankCardsPanel = new Panel();
            bankCardsPanel.Dock = DockStyle.Top;
            bankCardsPanel.Height = (int)Math.Round(94 * dpiScale);
            bankCardsPanel.Padding = new Padding(0, 0, 0, (int)Math.Round(6 * dpiScale));

            TableLayoutPanel bankCardsLayout = new TableLayoutPanel();
            bankCardsLayout.Dock = DockStyle.Fill;
            bankCardsLayout.ColumnCount = 3;
            bankCardsLayout.RowCount = 1;
            bankCardsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++)
            {
                bankCardsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            }

            CreateBankCard(bankCardsLayout, 0, "SEMANAL (SEG–SEX)", dpiScale, out _lblBankWeekTitle, out _lblBankWeekBalance, out _lblBankWeekComp);
            CreateBankCard(bankCardsLayout, 1, "MENSAL (11 A 10)", dpiScale, out _lblBankMonthTitle, out _lblBankMonthBalance, out _lblBankMonthComp);
            CreateBankCard(bankCardsLayout, 2, "CICLO 6 MESES", dpiScale, out _lblBankCycleTitle, out _lblBankCycleBalance, out _lblBankCycleComp);
            bankCardsPanel.Controls.Add(bankCardsLayout);

            // Barra de ferramentas do BH
            Panel bankToolbar = new Panel();
            bankToolbar.Dock = DockStyle.Top;
            bankToolbar.Height = (int)Math.Round(40 * dpiScale);
            bankToolbar.Padding = new Padding(0, (int)Math.Round(6 * dpiScale), 0, (int)Math.Round(6 * dpiScale));

            Button btnClassifyDay = CreateActionButton("Apontar / Classificar Dia...", (int)Math.Round(190 * dpiScale));
            btnClassifyDay.Dock = DockStyle.Left;
            btnClassifyDay.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
            btnClassifyDay.Click += delegate { OpenClassifySelectedDayDialog(); };

            Panel bankSp1 = new Panel();
            bankSp1.Dock = DockStyle.Left;
            bankSp1.Width = (int)Math.Round(10 * dpiScale);

            _cmbBankScope = new ComboBox();
            _cmbBankScope.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbBankScope.Dock = DockStyle.Left;
            _cmbBankScope.Width = (int)Math.Round(185 * dpiScale);
            _cmbBankScope.BackColor = Color.FromArgb(36, 41, 52);
            _cmbBankScope.ForeColor = Color.FromArgb(248, 250, 252);
            _cmbBankScope.Items.Add("Exibir: Mês Atual (11–10)");
            _cmbBankScope.Items.Add("Exibir: Ciclo Completo (6m)");
            _cmbBankScope.SelectedIndex = 0;
            _cmbBankScope.SelectedIndexChanged += delegate { _lastBankSignature = null; RefreshData(); };

            Label lblBankHint = new Label();
            lblBankHint.Text = "Tolerância ±15 min (CLT) • Duplo clique no dia para apontar feriado, atestado, imprensado ou esquecimento";
            lblBankHint.Dock = DockStyle.Fill;
            lblBankHint.TextAlign = ContentAlignment.MiddleRight;
            lblBankHint.ForeColor = Color.FromArgb(148, 163, 184);
            lblBankHint.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            lblBankHint.AutoEllipsis = true;

            bankToolbar.Controls.Add(lblBankHint);
            bankToolbar.Controls.Add(_cmbBankScope);
            bankToolbar.Controls.Add(bankSp1);
            bankToolbar.Controls.Add(btnClassifyDay);

            _gridBank = CreateStyledGrid();
            _gridBank.Columns.Add("Data", "Data");
            _gridBank.Columns.Add("Dia", "Dia");
            _gridBank.Columns.Add("Trabalhado", "Trabalhado");
            _gridBank.Columns.Add("Abonado", "Abonado");
            _gridBank.Columns.Add("Saldo", "Saldo BH");
            _gridBank.Columns.Add("Situacao", "Situação / Apontamento");
            _gridBank.Columns["Dia"].FillWeight = 45;
            _gridBank.Columns["Situacao"].FillWeight = 180;
            _gridBank.CellDoubleClick += delegate { OpenClassifySelectedDayDialog(); };

            _bankContentPanel.Controls.Add(_gridBank);
            _bankContentPanel.Controls.Add(bankToolbar);
            _bankContentPanel.Controls.Add(bankCardsPanel);

            tabBank.Controls.Add(_bankContentPanel);
            tabBank.Controls.Add(_lblBankDisabledNotice);

            tabs.TabPages.Add(tabToday);
            tabs.TabPages.Add(tabAbsences);
            tabs.TabPages.Add(tabDaily);
            tabs.TabPages.Add(tabBank);

            // Rodapé com botões de ação
            Panel footerPanel = new Panel();
            footerPanel.Dock = DockStyle.Bottom;
            footerPanel.Height = (int)Math.Round(48 * dpiScale);
            footerPanel.Padding = new Padding(
                (int)Math.Round(12 * dpiScale),
                (int)Math.Round(8 * dpiScale),
                (int)Math.Round(12 * dpiScale),
                (int)Math.Round(8 * dpiScale));
            footerPanel.BackColor = Color.FromArgb(30, 34, 43);

            Button btnOpenCsv = CreateActionButton("Abrir registros_diarios.csv", (int)Math.Round(190 * dpiScale));
            btnOpenCsv.Dock = DockStyle.Left;
            btnOpenCsv.Click += delegate { OpenFileSafe(_storage.GetDailyFilePath()); };

            Panel spacer1 = new Panel();
            spacer1.Dock = DockStyle.Left;
            spacer1.Width = (int)Math.Round(8 * dpiScale);

            Button btnOpenFolder = CreateActionButton("Abrir Pasta dos Arquivos", (int)Math.Round(175 * dpiScale));
            btnOpenFolder.Dock = DockStyle.Left;
            btnOpenFolder.Click += delegate { OpenFileSafe(_storage.BaseDirectory); };

            Button btnClose = CreateActionButton("Fechar", (int)Math.Round(90 * dpiScale));
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += delegate { Hide(); };

            Panel spacer2 = new Panel();
            spacer2.Dock = DockStyle.Right;
            spacer2.Width = (int)Math.Round(8 * dpiScale);

            Button btnRefresh = CreateActionButton("Atualizar", (int)Math.Round(95 * dpiScale));
            btnRefresh.Dock = DockStyle.Right;
            btnRefresh.Click += delegate { _lastBankSignature = null; RefreshData(); };

            footerPanel.Controls.Add(btnOpenFolder);
            footerPanel.Controls.Add(spacer1);
            footerPanel.Controls.Add(btnOpenCsv);
            footerPanel.Controls.Add(btnRefresh);
            footerPanel.Controls.Add(spacer2);
            footerPanel.Controls.Add(btnClose);

            Controls.Add(tabs);
            Controls.Add(headerPanel);
            Controls.Add(footerPanel);

            ResumeLayout(false);
            PerformLayout();

            RefreshData();
        }

        public void OpenAddAbsenceDialog()
        {
            DateTime now = DateTime.Now;
            DateTime defaultStart = now.AddHours(-1);
            if (defaultStart.Date != now.Date)
            {
                defaultStart = DateTime.Today;
            }

            using (AbsenceDialogForm dlg = new AbsenceDialogForm(
                "Lançar Ausência / Intervalo",
                AbsenceTypes.Medico,
                defaultStart,
                now,
                string.Empty,
                true,
                false,
                false))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // Padrão: sobrescreve automaticamente bloqueios e sessões no período
                    _tracker.AddManualAbsence(dlg.SelectedStart, dlg.SelectedEnd, dlg.SelectedType, dlg.EnteredNote, dlg.SelectedCredited, true);
                    RefreshData();
                    RaiseDataModified();
                }
            }
        }

        private void OpenEditSelectedAbsenceDialog()
        {
            if (_gridAbsences.SelectedRows.Count == 0)
            {
                MessageBox.Show("Selecione uma ausência na lista para justificar ou editar.", "Ausências", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow row = _gridAbsences.SelectedRows[0];
            if (!(row.Tag is int)) return;
            int idx = (int)row.Tag;
            if (idx < 0)
            {
                MessageBox.Show("Esta ausência ainda está em andamento. Retome a contagem para editá-la ou use o menu Pausar como Ausência.", "Ausências", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<AbsenceRecord> list = _tracker.GetTodayAbsencesSnapshot();
            if (idx >= list.Count) return;
            AbsenceRecord target = list[idx];

            using (AbsenceDialogForm dlg = new AbsenceDialogForm(
                "Justificar / Editar Ausência",
                target.Type,
                target.StartTime,
                target.EndTime,
                target.Note,
                target.Credited,
                true,
                false))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _tracker.UpdateAbsence(idx, dlg.SelectedType, dlg.EnteredNote, dlg.SelectedCredited);
                    RefreshData();
                    RaiseDataModified();
                }
            }
        }

        private void DeleteSelectedAbsence()
        {
            if (_gridAbsences.SelectedRows.Count == 0)
            {
                MessageBox.Show("Selecione uma ausência na lista para excluir.", "Ausências", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DataGridViewRow row = _gridAbsences.SelectedRows[0];
            if (!(row.Tag is int)) return;
            int idx = (int)row.Tag;
            if (idx < 0)
            {
                MessageBox.Show("Não é possível excluir uma ausência que ainda está em andamento.", "Ausências", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "Deseja excluir este registro de ausência?",
                "Ausências",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                _tracker.DeleteAbsence(idx);
                RefreshData();
                RaiseDataModified();
            }
        }

        private void OpenClassifySelectedDayDialog()
        {
            DateTime targetDate = DateTime.Today;
            if (_gridBank.SelectedRows.Count > 0 && _gridBank.SelectedRows[0].Tag is DateTime)
            {
                targetDate = (DateTime)_gridBank.SelectedRows[0].Tag;
            }
            else if (!HourBankCalculator.IsWeekday(targetDate))
            {
                while (!HourBankCalculator.IsWeekday(targetDate))
                {
                    targetDate = targetDate.AddDays(-1);
                }
            }

            Dictionary<string, DayOverrideRecord> overrides = _storage.LoadDayOverrides();
            string key = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            DayOverrideRecord existing;
            overrides.TryGetValue(key, out existing);

            using (DayOverrideDialogForm dlg = new DayOverrideDialogForm(targetDate, existing))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (dlg.DeleteRequested)
                    {
                        _storage.DeleteDayOverride(dlg.SelectedWorkDate);
                    }
                    else if (dlg.ResultRecord != null)
                    {
                        _storage.UpsertDayOverride(dlg.ResultRecord);
                    }
                    _lastBankSignature = null;
                    RefreshData();
                    RaiseDataModified();
                }
            }
        }

        public void RefreshData()
        {
            DateTime now = DateTime.Now;
            DailyRecord today = _tracker.BuildDailyRecord(now);

            _lblTodayTotal.Text = TimeFormat.FormatHms(today.TotalSeconds);
            _lblTodayRegular.Text = TimeFormat.FormatHms(today.RegularSeconds);
            _lblTodayExtra.Text = _tracker.OvertimeLimitSeconds > 0
                ? TimeFormat.FormatHms(today.OvertimeSeconds)
                : "Sem extra";
            _lblTodayAbsences.Text = TimeFormat.FormatHms(today.TotalAbsenceSeconds);

            if (_tracker.PersonalUseMode)
            {
                _lblTodayStatus.Text = "Uso pessoal";
                _lblTodayStatus.ForeColor = Color.FromArgb(129, 140, 248);
            }
            else if (today.TotalSeconds >= _tracker.TotalLimitSeconds)
            {
                _lblTodayStatus.Text = today.StatusLabel;
                _lblTodayStatus.ForeColor = Color.FromArgb(239, 68, 68);
            }
            else if (_tracker.OvertimeLimitSeconds > 0 && today.TotalSeconds >= _tracker.RegularLimitSeconds)
            {
                _lblTodayStatus.Text = today.StatusLabel;
                _lblTodayStatus.ForeColor = Color.FromArgb(249, 115, 22);
            }
            else if (_tracker.InMeetingMode && _tracker.State == TrackerState.Working)
            {
                _lblTodayStatus.Text = "Em reunião";
                _lblTodayStatus.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                _lblTodayStatus.Text = today.StatusLabel;
                _lblTodayStatus.ForeColor = Color.FromArgb(34, 197, 94);
            }

            // Sessões de hoje
            int selSessionIdx = _gridSessions.SelectedRows.Count > 0 ? _gridSessions.SelectedRows[0].Index : -1;
            _gridSessions.Rows.Clear();
            List<WorkSession> sessions = _tracker.GetTodaySessionsSnapshot();
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                WorkSession s = sessions[i];
                _gridSessions.Rows.Add(
                    s.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    (s.Reason == "Ativo" || s.Reason == "EmReuniao") ? "Agora" : s.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    TimeFormat.FormatHms(s.DurationSeconds),
                    TranslateReason(s.Reason));
            }
            if (selSessionIdx >= 0 && selSessionIdx < _gridSessions.Rows.Count)
            {
                _gridSessions.Rows[selSessionIdx].Selected = true;
            }

            // Ausências / Intervalos
            int selAbsenceIdx = _gridAbsences.SelectedRows.Count > 0 ? _gridAbsences.SelectedRows[0].Index : -1;
            _gridAbsences.Rows.Clear();
            List<AbsenceRecord> absences = _tracker.GetTodayAbsencesSnapshot();
            for (int i = absences.Count - 1; i >= 0; i--)
            {
                AbsenceRecord a = absences[i];
                string noteDisplay = a.Note;
                if (string.IsNullOrEmpty(noteDisplay) && !a.IsActive && a.Type == AbsenceTypes.BloqueioTela)
                {
                    noteDisplay = "(duplo clique para justificar)";
                }

                int rowIdx = _gridAbsences.Rows.Add(
                    a.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    a.IsActive ? "Agora" : a.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    TimeFormat.FormatHms(a.DurationSeconds),
                    AbsenceTypes.GetDisplayName(a.Type),
                    a.Credited ? "Sim" : "Não",
                    noteDisplay);
                _gridAbsences.Rows[rowIdx].Tag = a.IsActive ? -1 : i;
                if (a.Credited)
                {
                    _gridAbsences.Rows[rowIdx].Cells["Abonado"].Style.ForeColor = Color.FromArgb(134, 239, 172);
                }
                if (string.IsNullOrEmpty(a.Note) && !a.IsActive && a.Type == AbsenceTypes.BloqueioTela)
                {
                    _gridAbsences.Rows[rowIdx].Cells["Observacao"].Style.ForeColor = Color.FromArgb(148, 163, 184);
                }
            }
            if (selAbsenceIdx >= 0 && selAbsenceIdx < _gridAbsences.Rows.Count)
            {
                _gridAbsences.Rows[selAbsenceIdx].Selected = true;
            }

            AbsenceSummary absSum = _tracker.GetTodayAbsenceSummary();
            _lblAbsenceBreakdown.Text = string.Format(
                "Hoje — Intervalo: {0}  |  Médico: {1}  |  Pausas: {2}  |  Bloqueio: {3}  |  Outras: {4}",
                TimeFormat.FormatHms(absSum.IntervaloSeconds),
                TimeFormat.FormatHms(absSum.MedicoSeconds),
                TimeFormat.FormatHms(absSum.PausaRapidaSeconds),
                TimeFormat.FormatHms(absSum.BloqueioTelaSeconds),
                TimeFormat.FormatHms(absSum.OutroSeconds));

            // Registros diários consolidados
            int selDailyIdx = _gridDaily.SelectedRows.Count > 0 ? _gridDaily.SelectedRows[0].Index : -1;
            _gridDaily.Rows.Clear();
            List<DailyRecord> daily = _storage.LoadDailyRecords();
            bool hasToday = false;
            for (int i = 0; i < daily.Count; i++)
            {
                if (daily[i].WorkDate == today.WorkDate)
                {
                    daily[i] = today;
                    hasToday = true;
                    break;
                }
            }
            if (!hasToday && (today.TotalSeconds > 0 || today.TotalAbsenceSeconds > 0))
            {
                daily.Add(today);
            }

            for (int i = daily.Count - 1; i >= 0; i--)
            {
                DailyRecord r = daily[i];
                int idx = _gridDaily.Rows.Add(
                    r.WorkDate,
                    r.FirstStart,
                    r.LastEnd,
                    TimeFormat.FormatHms(r.TotalSeconds),
                    TimeFormat.FormatHms(r.RegularSeconds),
                    TimeFormat.FormatHms(r.OvertimeSeconds),
                    TimeFormat.FormatHms(r.TotalAbsenceSeconds),
                    r.StatusLabel);

                if (r.StatusLabel.IndexOf("Limite", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _gridDaily.Rows[idx].Cells["Status"].Style.ForeColor = Color.FromArgb(248, 113, 113);
                }
                else if (r.StatusLabel.IndexOf("Extra", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _gridDaily.Rows[idx].Cells["Status"].Style.ForeColor = Color.FromArgb(251, 146, 60);
                }
            }
            if (selDailyIdx >= 0 && selDailyIdx < _gridDaily.Rows.Count)
            {
                _gridDaily.Rows[selDailyIdx].Selected = true;
            }

            // Aba Banco de Horas
            RefreshHourBankTab(now, today, daily);
        }

        private void RefreshHourBankTab(DateTime now, DailyRecord todayLive, List<DailyRecord> savedDaily)
        {
            bool isClt8h = (_config.Profile == WorkProfile.Clt8h);
            _bankContentPanel.Visible = isClt8h;
            _lblBankDisabledNotice.Visible = !isClt8h;
            if (!isClt8h)
            {
                return;
            }

            DateTime bankStart;
            if (!DateTime.TryParseExact(_config.BankStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out bankStart))
            {
                bankStart = new DateTime(2026, 11, 23);
            }

            DateTime cycleStart, cycleEnd;
            int cycleNum;
            HourBankCalculator.GetCyclePeriod(now.Date, out cycleStart, out cycleEnd, out cycleNum);
            DateTime effStart = cycleStart < bankStart ? bankStart : cycleStart;

            Dictionary<string, int> creditedMap = _storage.LoadCreditedAbsenceSecondsByDate(effStart, now.Date);
            Dictionary<string, DayOverrideRecord> overrides = _storage.LoadDayOverrides();
            int todayLiveCredited = _tracker.GetTodayCreditedAbsenceSeconds(now);

            HourBankSnapshot snap = HourBankCalculator.BuildSnapshot(
                now.Date,
                todayLive,
                todayLiveCredited,
                savedDaily,
                creditedMap,
                overrides,
                bankStart);

            UpdateBankCard(_lblBankWeekTitle, _lblBankWeekBalance, _lblBankWeekComp, "SEMANAL (" + snap.Week.PeriodTitle + ")", snap.Week);
            UpdateBankCard(_lblBankMonthTitle, _lblBankMonthBalance, _lblBankMonthComp, "MENSAL (" + snap.Month.PeriodTitle + ")", snap.Month);
            UpdateBankCard(_lblBankCycleTitle, _lblBankCycleBalance, _lblBankCycleComp, snap.Cycle.PeriodTitle.ToUpperInvariant(), snap.Cycle);

            bool monthOnly = (_cmbBankScope.SelectedIndex == 0);

            // Evita piscar a tabela a cada segundo: só recarrega quando os dias encerrados mudarem
            string sig = string.Format("{0}|{1}|{2}|{3}|{4}",
                monthOnly,
                snap.CycleDays.Count,
                snap.Week.BalanceSeconds,
                snap.Month.BalanceSeconds,
                snap.Cycle.BalanceSeconds);

            if (sig == _lastBankSignature && _gridBank.Rows.Count > 0)
            {
                return;
            }
            _lastBankSignature = sig;

            int selBankIdx = _gridBank.SelectedRows.Count > 0 ? _gridBank.SelectedRows[0].Index : -1;
            _gridBank.Rows.Clear();

            foreach (HourBankDayEntry item in snap.CycleDays)
            {
                if (monthOnly && !item.IsInCurrentMonth)
                {
                    continue;
                }

                int rIdx = _gridBank.Rows.Add(
                    item.WorkDate,
                    item.DayOfWeekShort,
                    TimeFormat.FormatHms(item.WorkedSeconds),
                    TimeFormat.FormatHms(item.CreditedSeconds),
                    TimeFormat.FormatSignedHms(item.BalanceSeconds),
                    item.StatusText);
                _gridBank.Rows[rIdx].Tag = item.Date;

                if (item.NeedsAttention)
                {
                    _gridBank.Rows[rIdx].DefaultCellStyle.BackColor = Color.FromArgb(58, 34, 24);
                    _gridBank.Rows[rIdx].Cells["Saldo"].Style.ForeColor = Color.FromArgb(248, 113, 113);
                    _gridBank.Rows[rIdx].Cells["Situacao"].Style.ForeColor = Color.FromArgb(250, 204, 21);
                }
                else if (item.BalanceSeconds > 0)
                {
                    _gridBank.Rows[rIdx].Cells["Saldo"].Style.ForeColor = Color.FromArgb(74, 222, 128);
                }
                else if (item.BalanceSeconds < 0)
                {
                    _gridBank.Rows[rIdx].Cells["Saldo"].Style.ForeColor = Color.FromArgb(248, 113, 113);
                }
            }

            if (selBankIdx >= 0 && selBankIdx < _gridBank.Rows.Count)
            {
                _gridBank.Rows[selBankIdx].Selected = true;
            }
        }

        private static void CreateBankCard(
            TableLayoutPanel parent,
            int col,
            string title,
            float dpiScale,
            out Label lblTitle,
            out Label lblBalance,
            out Label lblComp)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding((int)Math.Round(3 * dpiScale));
            card.BackColor = Color.FromArgb(30, 34, 43);
            card.Padding = new Padding(
                (int)Math.Round(10 * dpiScale),
                (int)Math.Round(6 * dpiScale),
                (int)Math.Round(10 * dpiScale),
                (int)Math.Round(6 * dpiScale));

            lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = (int)Math.Round(17 * dpiScale);
            lblTitle.AutoEllipsis = true;
            lblTitle.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(148, 163, 184);

            lblComp = new Label();
            lblComp.Text = "A compensar: 00:00:00";
            lblComp.Dock = DockStyle.Bottom;
            lblComp.Height = (int)Math.Round(18 * dpiScale);
            lblComp.AutoEllipsis = true;
            lblComp.Font = new Font("Segoe UI", 8f, FontStyle.Regular);
            lblComp.ForeColor = Color.FromArgb(148, 163, 184);

            lblBalance = new Label();
            lblBalance.Text = "00:00:00";
            lblBalance.Dock = DockStyle.Fill;
            lblBalance.Font = new Font("Consolas", 13.5f, FontStyle.Bold);
            lblBalance.ForeColor = Color.FromArgb(248, 250, 252);
            lblBalance.TextAlign = ContentAlignment.MiddleLeft;

            card.Controls.Add(lblBalance);
            card.Controls.Add(lblTitle);
            card.Controls.Add(lblComp);
            parent.Controls.Add(card, col, 0);
        }

        private static void UpdateBankCard(Label lblTitle, Label lblBalance, Label lblComp, string titleText, HourBankPeriodSummary summary)
        {
            lblTitle.Text = titleText;
            lblBalance.Text = TimeFormat.FormatSignedHms(summary.BalanceSeconds);

            if (summary.BalanceSeconds > 0)
            {
                lblBalance.ForeColor = Color.FromArgb(74, 222, 128);
                lblComp.Text = "A compensar: 00:00:00 (saldo positivo)";
                lblComp.ForeColor = Color.FromArgb(134, 239, 172);
            }
            else if (summary.BalanceSeconds < 0)
            {
                lblBalance.ForeColor = Color.FromArgb(248, 113, 113);
                string pend = summary.PendingDaysCount > 0 ? string.Format(" ({0} pend.)", summary.PendingDaysCount) : string.Empty;
                lblComp.Text = "A compensar: " + TimeFormat.FormatHms(summary.ToCompensateSeconds) + pend;
                lblComp.ForeColor = Color.FromArgb(252, 165, 165);
            }
            else
            {
                lblBalance.ForeColor = Color.FromArgb(248, 250, 252);
                lblComp.Text = "A compensar: 00:00:00 (zerado)";
                lblComp.ForeColor = Color.FromArgb(148, 163, 184);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void RaiseDataModified()
        {
            if (DataModified != null)
            {
                DataModified(this, EventArgs.Empty);
            }
        }

        private static Label CreateSummaryCard(TableLayoutPanel parent, int column, string title, string initialValue, Color valueColor, float dpiScale)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding((int)Math.Round(3 * dpiScale));
            card.BackColor = Color.FromArgb(24, 26, 32);
            card.Padding = new Padding(
                (int)Math.Round(8 * dpiScale),
                (int)Math.Round(6 * dpiScale),
                (int)Math.Round(8 * dpiScale),
                (int)Math.Round(6 * dpiScale));

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = (int)Math.Round(18 * dpiScale);
            lblTitle.TextAlign = ContentAlignment.BottomLeft;
            lblTitle.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(148, 163, 184);

            Label lblValue = new Label();
            lblValue.Text = initialValue;
            lblValue.Dock = DockStyle.Fill;
            lblValue.AutoEllipsis = true;
            lblValue.Font = new Font("Consolas", 12.5f, FontStyle.Bold);
            lblValue.ForeColor = valueColor;
            lblValue.TextAlign = ContentAlignment.MiddleLeft;

            card.Controls.Add(lblValue);
            card.Controls.Add(lblTitle);
            parent.Controls.Add(card, column, 0);
            return lblValue;
        }

        private static DataGridView CreateStyledGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.FromArgb(24, 26, 32);
            grid.GridColor = Color.FromArgb(42, 47, 58);
            grid.EnableHeadersVisualStyles = false;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 41, 52);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(226, 232, 240);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            grid.ColumnHeadersHeight = 30;

            grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 26, 32);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(241, 245, 249);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 65, 85);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(29, 32, 40);
            grid.RowTemplate.Height = 26;

            return grid;
        }

        private static Button CreateActionButton(string text, int width)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Width = width;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btn.BackColor = Color.FromArgb(39, 45, 58);
            btn.ForeColor = Color.FromArgb(241, 245, 249);
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        private static string TranslateReason(string reason)
        {
            switch (reason)
            {
                case "Ativo": return "Em andamento";
                case "EmReuniao": return "Em reunião (ativo)";
                case "UsoPessoal": return "Troca para uso pessoal";
                case "BloqueioTela": return "Bloqueio de tela";
                case "Logoff": return "Logoff / Desconexão";
                case "Suspensao": return "Suspensão / Hibernação";
                case "PausaManual": return "Pausa rápida";
                case "Intervalo": return "Intervalo / Almoço";
                case "Medico": return "Ida ao Médico";
                case "AusenciaManual": return "Ausência lançada";
                case "Desligamento": return "Desligamento";
                case "Encerrado": return "App fechado";
                case "ViradaDeDia": return "Virada de dia (00:00)";
                case "Recuperado": return "Recuperado após queda";
                default: return reason;
            }
        }

        private static void OpenFileSafe(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("O arquivo ainda não foi gerado: " + path, "Relógio Acompanhamento", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível abrir: " + ex.Message, "Relógio Acompanhamento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
