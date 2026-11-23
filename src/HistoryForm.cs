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

        private readonly Label _lblTodayTotal;
        private readonly Label _lblTodayRegular;
        private readonly Label _lblTodayExtra;
        private readonly Label _lblTodayStatus;
        private readonly DataGridView _gridSessions;
        private readonly DataGridView _gridDaily;

        public HistoryForm(TimeTracker tracker, CsvStorage storage)
        {
            _tracker = tracker;
            _storage = storage;

            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Relógio Acompanhamento — Registros e Histórico";
            ClientSize = new Size((int)Math.Round(700 * dpiScale), (int)Math.Round(460 * dpiScale));
            MinimumSize = new Size((int)Math.Round(580 * dpiScale), (int)Math.Round(380 * dpiScale));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Painel superior com resumo do dia
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
            summaryLayout.ColumnCount = 4;
            summaryLayout.RowCount = 1;
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++)
            {
                summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            }

            _lblTodayTotal = CreateSummaryCard(summaryLayout, 0, "TOTAL HOJE", "00:00:00", Color.FromArgb(248, 250, 252), dpiScale);
            _lblTodayRegular = CreateSummaryCard(summaryLayout, 1, "HORAS NORMAIS", "00:00:00", Color.FromArgb(34, 197, 94), dpiScale);
            _lblTodayExtra = CreateSummaryCard(summaryLayout, 2, "HORAS EXTRAS", "00:00:00", Color.FromArgb(249, 115, 22), dpiScale);
            _lblTodayStatus = CreateSummaryCard(summaryLayout, 3, "STATUS", "Normal", Color.FromArgb(56, 189, 248), dpiScale);

            headerPanel.Controls.Add(summaryLayout);

            // Abas: Sessões de Hoje | Registros Diários
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(12, 6);

            TabPage tabToday = new TabPage("Sessões de Hoje");
            tabToday.BackColor = Color.FromArgb(24, 26, 32);
            tabToday.Padding = new Padding(8);

            _gridSessions = CreateStyledGrid();
            _gridSessions.Columns.Add("Inicio", "Início");
            _gridSessions.Columns.Add("Fim", "Fim");
            _gridSessions.Columns.Add("Duracao", "Duração");
            _gridSessions.Columns.Add("Motivo", "Evento / Status");
            tabToday.Controls.Add(_gridSessions);

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
            _gridDaily.Columns.Add("Status", "Status Limite");
            tabDaily.Controls.Add(_gridDaily);

            tabs.TabPages.Add(tabToday);
            tabs.TabPages.Add(tabDaily);

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
            btnRefresh.Click += delegate { RefreshData(); };

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

        public void RefreshData()
        {
            DateTime now = DateTime.Now;
            DailyRecord today = _tracker.BuildDailyRecord(now);

            _lblTodayTotal.Text = TimeFormat.FormatHms(today.TotalSeconds);
            _lblTodayRegular.Text = TimeFormat.FormatHms(today.RegularSeconds);
            _lblTodayExtra.Text = TimeFormat.FormatHms(today.OvertimeSeconds);
            _lblTodayStatus.Text = today.StatusLabel;

            if (today.TotalSeconds >= _tracker.TotalLimitSeconds)
            {
                _lblTodayStatus.ForeColor = Color.FromArgb(239, 68, 68);
            }
            else if (today.TotalSeconds >= _tracker.RegularLimitSeconds)
            {
                _lblTodayStatus.ForeColor = Color.FromArgb(249, 115, 22);
            }
            else
            {
                _lblTodayStatus.ForeColor = Color.FromArgb(34, 197, 94);
            }

            // Sessões de hoje
            _gridSessions.Rows.Clear();
            List<WorkSession> sessions = _tracker.GetTodaySessionsSnapshot();
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                WorkSession s = sessions[i];
                _gridSessions.Rows.Add(
                    s.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    s.Reason == "Ativo" ? "Agora" : s.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    TimeFormat.FormatHms(s.DurationSeconds),
                    TranslateReason(s.Reason));
            }

            // Registros diários consolidados
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
            if (!hasToday && today.TotalSeconds > 0)
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

        private static Label CreateSummaryCard(TableLayoutPanel parent, int column, string title, string initialValue, Color valueColor, float dpiScale)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding((int)Math.Round(4 * dpiScale));
            card.BackColor = Color.FromArgb(24, 26, 32);
            card.Padding = new Padding(
                (int)Math.Round(10 * dpiScale),
                (int)Math.Round(6 * dpiScale),
                (int)Math.Round(10 * dpiScale),
                (int)Math.Round(6 * dpiScale));

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = (int)Math.Round(18 * dpiScale);
            lblTitle.TextAlign = ContentAlignment.BottomLeft;
            lblTitle.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(148, 163, 184);

            Label lblValue = new Label();
            lblValue.Text = initialValue;
            lblValue.Dock = DockStyle.Fill;
            lblValue.AutoEllipsis = true;
            lblValue.Font = new Font("Consolas", 13f, FontStyle.Bold);
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
                case "BloqueioTela": return "Bloqueio de tela";
                case "Logoff": return "Logoff / Desconexão";
                case "Suspensao": return "Suspensão / Hibernação";
                case "PausaManual": return "Pausa manual";
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
