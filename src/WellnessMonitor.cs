using System;

namespace RelogioAcompanhamento
{
    public class WellnessMonitor
    {
        public const int NaturalBreakResetSeconds = 5 * 60; // Pausa >= 5 min zera o timer de bem-estar

        private readonly CsvStorage _storage;
        private int _activeStreakSeconds;
        private bool _pendingAfterMeeting;
        private int _tipIndex;
        private string _mutedDate;
        private DateTime _lastTickTime;
        private bool _wasWorking;
        private DateTime _pauseStartTime;
        private AlertFlags _fatigueFlags;

        private static readonly string[] WellnessTips = new string[]
        {
            "Hora de beber água e alongar ombros e pescoço por 1 a 2 minutos.",
            "Faça uma pausa curta: levante um pouco, movimente as pernas e descanse os olhos.",
            "Regra 20-20-20: olhe para algo distante por 20 segundos e aproveite para se hidratar."
        };

        public int ActiveStreakSeconds { get { return _activeStreakSeconds; } }
        public bool PendingAfterMeeting { get { return _pendingAfterMeeting; } }

        public WellnessMonitor(CsvStorage storage)
        {
            _storage = storage;
            _activeStreakSeconds = 0;
            _pendingAfterMeeting = false;
            _tipIndex = 0;
            _mutedDate = string.Empty;
            _lastTickTime = DateTime.MinValue;
            _pauseStartTime = DateTime.MinValue;
            _wasWorking = false;
            _fatigueFlags = new AlertFlags();
        }

        public bool IsMutedForDate(string workDate)
        {
            return _mutedDate == workDate;
        }

        public void SetMutedForDate(string workDate, bool muted)
        {
            _mutedDate = muted ? workDate : string.Empty;
            if (muted)
            {
                _pendingAfterMeeting = false;
            }
        }

        public void ResetStreak()
        {
            _activeStreakSeconds = 0;
            _pendingAfterMeeting = false;
        }

        public AlertNotification Tick(
            DateTime now,
            string workDate,
            TrackerState state,
            bool personalUseMode,
            bool inMeetingMode,
            AppConfig config)
        {
            bool isWorking = (state == TrackerState.Working && !personalUseMode);

            if (!isWorking)
            {
                if (_wasWorking)
                {
                    _pauseStartTime = now;
                    _wasWorking = false;
                }
                else if (_pauseStartTime != DateTime.MinValue && (now - _pauseStartTime).TotalSeconds >= NaturalBreakResetSeconds)
                {
                    _activeStreakSeconds = 0;
                    _pendingAfterMeeting = false;
                }
                _lastTickTime = now;
                return null;
            }

            // Retornou de pausa
            if (!_wasWorking)
            {
                if (_pauseStartTime != DateTime.MinValue && (now - _pauseStartTime).TotalSeconds >= NaturalBreakResetSeconds)
                {
                    _activeStreakSeconds = 0;
                    _pendingAfterMeeting = false;
                }
                _pauseStartTime = DateTime.MinValue;
                _wasWorking = true;
                _lastTickTime = now;
                return null;
            }

            int delta = _lastTickTime == DateTime.MinValue ? 1 : (int)Math.Round((now - _lastTickTime).TotalSeconds);
            if (delta < 0) delta = 0;
            if (delta > 60) delta = 1; // Evita salto após suspensão
            _lastTickTime = now;

            if (!config.WellnessReminderEnabled || IsMutedForDate(workDate))
            {
                return null;
            }

            // Se estava segurando aviso durante Modo Reunião e a reunião acabou, dispara agora
            if (_pendingAfterMeeting && (!inMeetingMode || !config.SilenceWellnessInMeeting))
            {
                _pendingAfterMeeting = false;
                _activeStreakSeconds = 0;
                return BuildWellnessNotification(config.WellnessIntervalMinutes, true);
            }

            _activeStreakSeconds += delta;
            int intervalSec = Math.Max(60, config.WellnessIntervalMinutes * 60);

            if (_activeStreakSeconds >= intervalSec)
            {
                if (inMeetingMode && config.SilenceWellnessInMeeting)
                {
                    _pendingAfterMeeting = true;
                    return null;
                }

                _activeStreakSeconds = 0;
                return BuildWellnessNotification(config.WellnessIntervalMinutes, false);
            }

            return null;
        }

        public AlertNotification EvaluateFatigue(
            DateTime now,
            string workDate,
            TrackerState state,
            bool personalUseMode,
            bool inMeetingMode,
            int todayWorkedSeconds,
            WeeklyFatigueContext weeklyContext,
            AppConfig config)
        {
            if (!config.FatigueAlertEnabled || state != TrackerState.Working || personalUseMode)
            {
                return null;
            }

            if (inMeetingMode && config.SilenceWellnessInMeeting)
            {
                return null;
            }

            if (_fatigueFlags.WorkDate != workDate)
            {
                _fatigueFlags = _storage.LoadAlertFlags(workDate);
            }

            if (_fatigueFlags.FatigueSent)
            {
                return null;
            }

            bool isLateNight = (now.Hour > 19 || (now.Hour == 19 && now.Minute >= 30));
            bool hasOvertimeStreak = (weeklyContext != null && weeklyContext.ConsecutiveOvertimeDays >= 2 && todayWorkedSeconds >= (8 * 3600));
            bool hasLateExitStreak = (weeklyContext != null && weeklyContext.ConsecutiveLateExitDays >= 2 && todayWorkedSeconds >= (7 * 3600 + 30 * 60));

            if (!isLateNight && !hasOvertimeStreak && !hasLateExitStreak)
            {
                return null;
            }

            _fatigueFlags.FatigueSent = true;
            _storage.SaveAlertFlags(_fatigueFlags);

            if (isLateNight)
            {
                string extra = (weeklyContext != null && weeklyContext.ConsecutiveLateExitDays >= 2)
                    ? string.Format(" Você já saiu após 20h em {0} dias seguidos.", weeklyContext.ConsecutiveLateExitDays)
                    : string.Empty;
                return new AlertNotification(
                    "Alerta de bem-estar — horário estendido",
                    "Já passou das 19h30 e o expediente continua ativo." + extra + " Que tal encerrar por hoje e compensar depois?",
                    false,
                    AlertLevel.Overtime);
            }

            if (hasLateExitStreak)
            {
                return new AlertNotification(
                    "Alerta preventivo de fadiga",
                    string.Format("Você encerrou após as 20h nos últimos {0} dias úteis. Considere finalizar sua jornada no horário hoje para descansar.", weeklyContext.ConsecutiveLateExitDays),
                    false,
                    AlertLevel.Overtime);
            }

            return new AlertNotification(
                "Alerta preventivo de sobrecarga",
                string.Format("Você já acumula {0} dias seguidos com hora extra ({1} na semana). Considere encerrar o expediente agora para preservar seu ritmo.",
                    weeklyContext.ConsecutiveOvertimeDays,
                    TimeFormat.FormatSignedHms(weeklyContext.WeekClosedBalanceSeconds)),
                false,
                AlertLevel.Overtime);
        }

        private AlertNotification BuildWellnessNotification(int intervalMinutes, bool afterMeeting)
        {
            string tip = WellnessTips[_tipIndex % WellnessTips.Length];
            _tipIndex++;

            string title = afterMeeting
                ? "Reunião encerrada — pausa de bem-estar"
                : string.Format("Pausa de bem-estar ({0} min ativos)", intervalMinutes);

            return new AlertNotification(title, tip, false, AlertLevel.Normal);
        }
    }
}
