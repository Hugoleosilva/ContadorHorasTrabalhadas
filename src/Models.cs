using System;
using System.Collections.Generic;

namespace RelogioAcompanhamento
{
    public enum TrackerState
    {
        Working,
        PausedSession,
        PausedManual
    }

    public enum AlertLevel
    {
        Normal,
        PreRegular,
        Overtime,
        PreOvertimeLimit,
        LimitReached
    }

    public enum WorkProfile
    {
        Clt8h,
        Estagiario6h
    }

    public static class AbsenceTypes
    {
        public const string Intervalo = "Intervalo";
        public const string Medico = "Medico";
        public const string PausaRapida = "PausaRapida";
        public const string BloqueioTela = "BloqueioTela";
        public const string Outro = "Outro";

        public static bool IsCreditedByDefault(string type)
        {
            return type == Medico;
        }

        public static string GetDisplayName(string type)
        {
            switch (type)
            {
                case Intervalo: return "Intervalo / Almoço";
                case Medico: return "Ida ao Médico";
                case PausaRapida: return "Pausa Rápida";
                case BloqueioTela: return "Bloqueio de Tela";
                case Outro: return "Outra Ausência";
                default: return string.IsNullOrEmpty(type) ? "Outra Ausência" : type;
            }
        }

        public static string GetShortWidgetLabel(string type)
        {
            switch (type)
            {
                case Intervalo: return "Intervalo";
                case Medico: return "Médico";
                case PausaRapida: return "Pausa";
                case BloqueioTela: return "Congelado";
                case Outro: return "Ausente";
                default: return "Pausado";
            }
        }
    }

    public static class DaySpecialTypes
    {
        public const string Feriado = "Feriado";
        public const string Atestado = "Atestado";
        public const string AbonadoTotal = "AbonadoTotal";
        public const string AbonadoParcial4h = "AbonadoParcial4h";
        public const string ImprensadoAbonado = "ImprensadoAbonado";
        public const string ImprensadoDesconto = "ImprensadoDesconto";
        public const string DescontoConfirmado = "DescontoConfirmado";
        public const string EsqueciAbrirApp = "EsqueciAbrirApp";
        public const string Outro = "Outro";

        public static string GetDisplayName(string type)
        {
            switch (type)
            {
                case Feriado: return "Feriado";
                case Atestado: return "Atestado (dia inteiro)";
                case AbonadoTotal: return "Abonado Totalmente";
                case AbonadoParcial4h: return "Abonado Parcial (4h)";
                case ImprensadoAbonado: return "Imprensado (Abonado)";
                case ImprensadoDesconto: return "Imprensado (-8h BH)";
                case DescontoConfirmado: return "Desconto / Folga BH (-8h)";
                case EsqueciAbrirApp: return "Esqueci de abrir o app";
                case Outro: return "Outra Situação";
                default: return string.IsNullOrEmpty(type) ? "Normal" : type;
            }
        }
    }

    public class WorkSession
    {
        public string WorkDate { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int DurationSeconds { get; set; }
        public string Reason { get; set; }

        public WorkSession()
        {
            WorkDate = string.Empty;
            Reason = string.Empty;
        }

        public WorkSession Clone()
        {
            WorkSession copy = new WorkSession();
            copy.WorkDate = WorkDate;
            copy.StartTime = StartTime;
            copy.EndTime = EndTime;
            copy.DurationSeconds = DurationSeconds;
            copy.Reason = Reason;
            return copy;
        }
    }

    public class AbsenceRecord
    {
        public string WorkDate { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int DurationSeconds { get; set; }
        public string Type { get; set; }
        public string Note { get; set; }
        public bool Credited { get; set; }
        public bool IsActive { get; set; }

        public AbsenceRecord()
        {
            WorkDate = string.Empty;
            Type = AbsenceTypes.PausaRapida;
            Note = string.Empty;
            Credited = false;
            IsActive = false;
        }

        public AbsenceRecord Clone()
        {
            AbsenceRecord copy = new AbsenceRecord();
            copy.WorkDate = WorkDate;
            copy.StartTime = StartTime;
            copy.EndTime = EndTime;
            copy.DurationSeconds = DurationSeconds;
            copy.Type = Type;
            copy.Note = Note;
            copy.Credited = Credited;
            copy.IsActive = IsActive;
            return copy;
        }
    }

    public class DayOverrideRecord
    {
        public string WorkDate { get; set; }
        public string Type { get; set; }
        public int CreditedSeconds { get; set; }
        public int ManualWorkedSeconds { get; set; }
        public string Note { get; set; }

        public DayOverrideRecord()
        {
            WorkDate = string.Empty;
            Type = DaySpecialTypes.Feriado;
            Note = string.Empty;
        }
    }

    public class AbsenceSummary
    {
        public int TotalSeconds { get; set; }
        public int IntervaloSeconds { get; set; }
        public int MedicoSeconds { get; set; }
        public int PausaRapidaSeconds { get; set; }
        public int BloqueioTelaSeconds { get; set; }
        public int OutroSeconds { get; set; }
    }

    public class DailyRecord
    {
        public string WorkDate { get; set; }
        public string FirstStart { get; set; }
        public string LastEnd { get; set; }
        public int TotalSeconds { get; set; }
        public int RegularSeconds { get; set; }
        public int OvertimeSeconds { get; set; }
        public int TotalAbsenceSeconds { get; set; }
        public string StatusLabel { get; set; }

        public DailyRecord()
        {
            WorkDate = string.Empty;
            FirstStart = string.Empty;
            LastEnd = string.Empty;
            StatusLabel = "Normal";
        }
    }

    public class HourBankDayEntry
    {
        public DateTime Date { get; set; }
        public string WorkDate { get; set; }
        public string DayOfWeekShort { get; set; }
        public int WorkedSeconds { get; set; }
        public int CreditedSeconds { get; set; }
        public int BalanceSeconds { get; set; }
        public string StatusText { get; set; }
        public bool NeedsAttention { get; set; }
        public bool IsToday { get; set; }
        public bool IsInCurrentMonth { get; set; }
        public bool IsInCurrentWeek { get; set; }

        public HourBankDayEntry()
        {
            WorkDate = string.Empty;
            DayOfWeekShort = string.Empty;
            StatusText = string.Empty;
        }
    }

    public class HourBankPeriodSummary
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PeriodTitle { get; set; }
        public int BalanceSeconds { get; set; }
        public int PendingDaysCount { get; set; }

        public int ToCompensateSeconds
        {
            get { return BalanceSeconds < 0 ? -BalanceSeconds : 0; }
        }

        public HourBankPeriodSummary()
        {
            PeriodTitle = string.Empty;
        }
    }

    public class HourBankSnapshot
    {
        public HourBankPeriodSummary Week { get; set; }
        public HourBankPeriodSummary Month { get; set; }
        public HourBankPeriodSummary Cycle { get; set; }
        public HourBankDayEntry TodayEntry { get; set; }
        public List<HourBankDayEntry> CycleDays { get; set; }

        public HourBankSnapshot()
        {
            Week = new HourBankPeriodSummary();
            Month = new HourBankPeriodSummary();
            Cycle = new HourBankPeriodSummary();
            CycleDays = new List<HourBankDayEntry>();
        }
    }

    public class AlertFlags
    {
        public string WorkDate { get; set; }
        public bool PreRegularSent { get; set; }
        public bool RegularSent { get; set; }
        public bool PreLimitSent { get; set; }
        public bool LimitSent { get; set; }

        public AlertFlags()
        {
            WorkDate = string.Empty;
        }
    }

    public class AlertNotification
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public bool IsCritical { get; set; }
        public AlertLevel Level { get; set; }

        public AlertNotification(string title, string message, bool isCritical, AlertLevel level)
        {
            Title = title;
            Message = message;
            IsCritical = isCritical;
            Level = level;
        }
    }

    public class AppConfig
    {
        public WorkProfile Profile { get; set; }
        public bool StartWithWindows { get; set; }
        public bool AutoStartCounting { get; set; }
        public bool AskModeOnStartup { get; set; }
        public string BankStartDate { get; set; }
        public bool WidgetVisible { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool ShowInTaskbar { get; set; }
        public int WidgetWidth { get; set; }
        public int WidgetHeight { get; set; }
        public int WidgetX { get; set; }
        public int WidgetY { get; set; }
        public int RegularLimitMinutes { get; set; }
        public int OvertimeLimitMinutes { get; set; }
        public int PreAlertMinutes { get; set; }

        public AppConfig()
        {
            Profile = WorkProfile.Clt8h;
            StartWithWindows = false;
            AutoStartCounting = true;
            AskModeOnStartup = true;
            BankStartDate = "2026-11-23";
            WidgetVisible = true;
            AlwaysOnTop = true;
            ShowInTaskbar = true;
            WidgetWidth = 240;
            WidgetHeight = 74;
            WidgetX = -1;
            WidgetY = -1;
            RegularLimitMinutes = 480;   // 8 horas
            OvertimeLimitMinutes = 120;  // +2 horas extras (CLT)
            PreAlertMinutes = 15;        // Pré-aviso 15 minutos antes
        }

        public void ApplyProfile(WorkProfile profile)
        {
            Profile = profile;
            if (profile == WorkProfile.Estagiario6h)
            {
                RegularLimitMinutes = 360;  // 6 horas (Estagiário)
                OvertimeLimitMinutes = 0;   // Sem horas extras
            }
            else
            {
                RegularLimitMinutes = 480;  // 8 horas (CLT)
                OvertimeLimitMinutes = 120; // +2 horas extras
            }
        }
    }

    public static class TimeFormat
    {
        public static string FormatHms(int totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            return string.Format("{0:D2}:{1:D2}:{2:D2}", hours, minutes, seconds);
        }

        public static string FormatSignedHms(int signedSeconds)
        {
            if (signedSeconds == 0)
            {
                return "00:00:00";
            }
            string sign = signedSeconds > 0 ? "+" : "-";
            int abs = signedSeconds > 0 ? signedSeconds : -signedSeconds;
            return sign + FormatHms(abs);
        }

        public static string FormatHm(int totalSeconds)
        {
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            return string.Format("{0:D2}h{1:D2}", hours, minutes);
        }
    }
}
