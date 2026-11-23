using System;

namespace RelogioAcompanhamento
{
    public class AlertManager
    {
        private readonly CsvStorage _storage;
        private readonly int _regularLimitSeconds;
        private readonly int _overtimeLimitSeconds;
        private readonly int _preAlertSeconds;
        private AlertFlags _flags;

        public AlertManager(CsvStorage storage, int regularLimitSeconds, int overtimeLimitSeconds, int preAlertSeconds)
        {
            _storage = storage;
            _regularLimitSeconds = regularLimitSeconds;
            _overtimeLimitSeconds = overtimeLimitSeconds;
            _preAlertSeconds = preAlertSeconds;
            _flags = new AlertFlags();
        }

        public int TotalLimitSeconds
        {
            get { return _regularLimitSeconds + _overtimeLimitSeconds; }
        }

        public void ResetCache()
        {
            _flags = new AlertFlags();
        }

        public AlertLevel GetAlertLevel(int totalWorkedSeconds)
        {
            int totalLimit = TotalLimitSeconds;
            int preOvertimeLimit = Math.Max(_regularLimitSeconds, totalLimit - _preAlertSeconds);
            int preRegular = Math.Max(0, _regularLimitSeconds - _preAlertSeconds);

            if (totalWorkedSeconds >= totalLimit)
            {
                return AlertLevel.LimitReached;
            }
            if (_overtimeLimitSeconds > 0 && _preAlertSeconds > 0 && totalWorkedSeconds >= preOvertimeLimit)
            {
                return AlertLevel.PreOvertimeLimit;
            }
            if (totalWorkedSeconds >= _regularLimitSeconds)
            {
                return AlertLevel.Overtime;
            }
            if (_preAlertSeconds > 0 && totalWorkedSeconds >= preRegular)
            {
                return AlertLevel.PreRegular;
            }
            return AlertLevel.Normal;
        }

        public AlertNotification Evaluate(string workDate, int totalWorkedSeconds)
        {
            if (_flags.WorkDate != workDate)
            {
                _flags = _storage.LoadAlertFlags(workDate);
            }

            int totalLimit = TotalLimitSeconds;
            int preOvertimeLimit = Math.Max(_regularLimitSeconds, totalLimit - _preAlertSeconds);
            int preRegular = Math.Max(0, _regularLimitSeconds - _preAlertSeconds);

            AlertNotification notification = null;
            bool changed = false;

            if (totalWorkedSeconds >= totalLimit && !_flags.LimitSent)
            {
                _flags.PreRegularSent = true;
                _flags.RegularSent = true;
                _flags.PreLimitSent = true;
                _flags.LimitSent = true;
                changed = true;
                notification = new AlertNotification(
                    "Limite legal atingido!",
                    string.Format("Você completou {0} ({1} + {2} extras). Encerre sua jornada para respeitar o limite legal.",
                        TimeFormat.FormatHm(totalLimit),
                        TimeFormat.FormatHm(_regularLimitSeconds),
                        TimeFormat.FormatHm(_overtimeLimitSeconds)),
                    true,
                    AlertLevel.LimitReached);
            }
            else if (_overtimeLimitSeconds > 0 && _preAlertSeconds > 0 && totalWorkedSeconds >= preOvertimeLimit && !_flags.PreLimitSent)
            {
                _flags.PreRegularSent = true;
                _flags.RegularSent = true;
                _flags.PreLimitSent = true;
                changed = true;
                int remainingMinutes = Math.Max(1, (int)Math.Ceiling((totalLimit - totalWorkedSeconds) / 60.0));
                notification = new AlertNotification(
                    "Atenção: limite legal próximo",
                    string.Format("Restam cerca de {0} min para atingir o limite de {1} diárias (+{2} extras).",
                        remainingMinutes,
                        TimeFormat.FormatHm(totalLimit),
                        TimeFormat.FormatHm(_overtimeLimitSeconds)),
                    false,
                    AlertLevel.PreOvertimeLimit);
            }
            else if (totalWorkedSeconds >= _regularLimitSeconds && !_flags.RegularSent)
            {
                _flags.PreRegularSent = true;
                _flags.RegularSent = true;
                changed = true;
                notification = new AlertNotification(
                    "Jornada diária concluída",
                    string.Format("Você completou {0} trabalhadas. A partir de agora conta hora extra (limite legal: +{1}).",
                        TimeFormat.FormatHm(_regularLimitSeconds),
                        TimeFormat.FormatHm(_overtimeLimitSeconds)),
                    false,
                    AlertLevel.Overtime);
            }
            else if (_preAlertSeconds > 0 && totalWorkedSeconds >= preRegular && !_flags.PreRegularSent)
            {
                _flags.PreRegularSent = true;
                changed = true;
                int remainingMinutes = Math.Max(1, (int)Math.Ceiling((_regularLimitSeconds - totalWorkedSeconds) / 60.0));
                notification = new AlertNotification(
                    "Fim da jornada se aproximando",
                    string.Format("Faltam cerca de {0} min para completar {1} trabalhadas.",
                        remainingMinutes,
                        TimeFormat.FormatHm(_regularLimitSeconds)),
                    false,
                    AlertLevel.PreRegular);
            }

            if (changed)
            {
                _storage.SaveAlertFlags(_flags);
            }

            return notification;
        }
    }
}
