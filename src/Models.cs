using System;

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
        public bool IsActive { get; set; }

        public AbsenceRecord()
        {
            WorkDate = string.Empty;
            Type = AbsenceTypes.PausaRapida;
            Note = string.Empty;
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
            copy.IsActive = IsActive;
            return copy;
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
