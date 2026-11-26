using System;
using System.Drawing;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    public class RadarDetailsForm : Form
    {
        public RadarDetailsForm(CycleSimulationResult sim)
        {
            float dpiScale = 1.0f;
            using (Graphics g = CreateGraphics())
            {
                if (g.DpiY > 0) dpiScale = g.DpiY / 96.0f;
            }

            SuspendLayout();

            Text = "Radar de Padrões & Simulador de Fechamento";
            ClientSize = new Size((int)Math.Round(700 * dpiScale), (int)Math.Round(420 * dpiScale));
            MinimumSize = new Size((int)Math.Round(600 * dpiScale), (int)Math.Round(360 * dpiScale));
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.FromArgb(241, 245, 249);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            Panel topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = (int)Math.Round(126 * dpiScale);
            topPanel.Padding = new Padding((int)Math.Round(12 * dpiScale));
            topPanel.BackColor = Color.FromArgb(30, 34, 43);

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Top;
            cards.Height = (int)Math.Round(68 * dpiScale);
            cards.ColumnCount = 3;
            cards.RowCount = 1;
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            AddSimCard(cards, 0, string.Format("PROJEÇÃO REALISTA ({0:dd/MM/yy})", sim.CycleEndDate),
                TimeFormat.FormatSignedHms(sim.RealisticProjectedBalanceSeconds),
                sim.RealisticProjectedBalanceSeconds >= 0 ? Color.FromArgb(74, 222, 128) : Color.FromArgb(248, 113, 113), dpiScale);
            AddSimCard(cards, 1, "CONSERVADOR (8h EXATAS)",
                TimeFormat.FormatSignedHms(sim.ConservativeProjectedBalanceSeconds),
                Color.FromArgb(203, 213, 225), dpiScale);
            AddSimCard(cards, 2, string.Format("META SEMANAL ({0} DIAS ÚTEIS)", sim.RemainingWeekdays),
                TimeFormat.FormatSignedHms(sim.WeeklyAdjustmentToZeroSeconds) + "/sem",
                Color.FromArgb(56, 189, 248), dpiScale);

            Label lblInsight = new Label();
            lblInsight.Dock = DockStyle.Fill;
            lblInsight.Text = sim.RecommendationText;
            lblInsight.ForeColor = Color.FromArgb(226, 232, 240);
            lblInsight.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblInsight.TextAlign = ContentAlignment.BottomLeft;
            lblInsight.AutoEllipsis = true;

            topPanel.Controls.Add(lblInsight);
            topPanel.Controls.Add(cards);

            Panel gridPanel = new Panel();
            gridPanel.Dock = DockStyle.Fill;
            gridPanel.Padding = new Padding((int)Math.Round(12 * dpiScale));

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
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(29, 32, 40);
            grid.RowTemplate.Height = 28;

            grid.Columns.Add("Dia", "Dia da Semana");
            grid.Columns.Add("Amostras", "Dias");
            grid.Columns.Add("Entrada", "Entrada (Mediana)");
            grid.Columns.Add("Saida", "Saída (Mediana)");
            grid.Columns.Add("Ema", "Saldo EMA");
            grid.Columns.Add("Vol", "Volatilidade (±)");
            grid.Columns["Amostras"].FillWeight = 45;

            foreach (WeekdayProfile p in sim.WeekdayProfiles)
            {
                int idx = grid.Rows.Add(
                    p.DayNamePt,
                    p.SampleCount,
                    PatternRadarCalculator.FormatMinutesAsHm(p.MedianStartMinutes),
                    PatternRadarCalculator.FormatMinutesAsHm(p.MedianEndMinutes),
                    TimeFormat.FormatSignedHms(p.EmaBalanceSeconds),
                    TimeFormat.FormatHms(p.StdDevBalanceSeconds));

                if (p.DayNamePt == sim.BestCompensationDayName)
                {
                    grid.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(22, 48, 42);
                }
                if (p.EmaBalanceSeconds > 0)
                {
                    grid.Rows[idx].Cells["Ema"].Style.ForeColor = Color.FromArgb(74, 222, 128);
                }
                else if (p.EmaBalanceSeconds < 0)
                {
                    grid.Rows[idx].Cells["Ema"].Style.ForeColor = Color.FromArgb(248, 113, 113);
                }
            }

            gridPanel.Controls.Add(grid);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = (int)Math.Round(46 * dpiScale);
            footer.Padding = new Padding((int)Math.Round(12 * dpiScale), (int)Math.Round(8 * dpiScale), (int)Math.Round(12 * dpiScale), (int)Math.Round(8 * dpiScale));
            footer.BackColor = Color.FromArgb(30, 34, 43);

            Label lblFootNote = new Label();
            lblFootNote.Text = string.Format("Base: {0} dias úteis registrados • Destaque verde = dia mais estável para compensar", sim.RecordedDaysCount);
            lblFootNote.Dock = DockStyle.Fill;
            lblFootNote.TextAlign = ContentAlignment.MiddleLeft;
            lblFootNote.ForeColor = Color.FromArgb(148, 163, 184);
            lblFootNote.Font = new Font("Segoe UI", 8f, FontStyle.Italic);

            Button btnClose = new Button();
            btnClose.Text = "Fechar";
            btnClose.Dock = DockStyle.Right;
            btnClose.Width = (int)Math.Round(90 * dpiScale);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btnClose.BackColor = Color.FromArgb(39, 45, 58);
            btnClose.ForeColor = Color.FromArgb(241, 245, 249);
            btnClose.Click += delegate { Close(); };

            footer.Controls.Add(lblFootNote);
            footer.Controls.Add(btnClose);

            Controls.Add(gridPanel);
            Controls.Add(topPanel);
            Controls.Add(footer);

            ResumeLayout(false);
            PerformLayout();
        }

        private static void AddSimCard(TableLayoutPanel parent, int col, string title, string value, Color valueColor, float dpiScale)
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding((int)Math.Round(3 * dpiScale));
            card.BackColor = Color.FromArgb(24, 26, 32);
            card.Padding = new Padding((int)Math.Round(8 * dpiScale), (int)Math.Round(5 * dpiScale), (int)Math.Round(8 * dpiScale), (int)Math.Round(5 * dpiScale));

            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = (int)Math.Round(16 * dpiScale);
            lblTitle.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(148, 163, 184);

            Label lblVal = new Label();
            lblVal.Text = value;
            lblVal.Dock = DockStyle.Fill;
            lblVal.Font = new Font("Consolas", 12.5f, FontStyle.Bold);
            lblVal.ForeColor = valueColor;
            lblVal.TextAlign = ContentAlignment.MiddleLeft;

            card.Controls.Add(lblVal);
            card.Controls.Add(lblTitle);
            parent.Controls.Add(card, col, 0);
        }
    }
}
