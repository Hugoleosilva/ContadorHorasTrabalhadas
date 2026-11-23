using System;
using System.Collections.Generic;
using System.Globalization;

namespace RelogioAcompanhamento
{
    public class TimeTracker
    {
        private readonly CsvStorage _storage;
        private readonly Func<DateTime> _clock;
        private int _regularLimitSeconds;
        private int _overtimeLimitSeconds;
        private readonly int _heartbeatIntervalSeconds;

        private readonly List<WorkSession> _completedSessions;
        private readonly List<AbsenceRecord> _completedAbsences;
        private WorkSession _activeSession;
        private AbsenceRecord _activeAbsence;
        private DateTime _lastPersistTime;

        public string CurrentDate { get; private set; }
        public TrackerState State { get; private set; }
        public bool InMeetingMode { get; private set; }
        public int RegularLimitSeconds { get { return _regularLimitSeconds; } }
        public int OvertimeLimitSeconds { get { return _overtimeLimitSeconds; } }
        public int TotalLimitSeconds { get { return _regularLimitSeconds + _overtimeLimitSeconds; } }

        public string ActiveAbsenceType
        {
            get { return _activeAbsence != null ? _activeAbsence.Type : string.Empty; }
        }

        public TimeTracker(
            CsvStorage storage,
            int regularLimitSeconds,
            int overtimeLimitSeconds,
            Func<DateTime> clock,
            int heartbeatIntervalSeconds)
        {
            _storage = storage;
            _regularLimitSeconds = regularLimitSeconds;
            _overtimeLimitSeconds = overtimeLimitSeconds;
            _clock = clock ?? delegate { return DateTime.Now; };
            _heartbeatIntervalSeconds = heartbeatIntervalSeconds > 0 ? heartbeatIntervalSeconds : 30;
            _completedSessions = new List<WorkSession>();
            _completedAbsences = new List<AbsenceRecord>();
            State = TrackerState.PausedSession;
            InMeetingMode = false;
            CurrentDate = _clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _lastPersistTime = DateTime.MinValue;
        }

        public void UpdateLimits(int regularLimitSeconds, int overtimeLimitSeconds)
        {
            _regularLimitSeconds = regularLimitSeconds;
            _overtimeLimitSeconds = overtimeLimitSeconds;
            Persist(_clock());
        }

        public void SetMeetingMode(bool enabled)
        {
            InMeetingMode = enabled;
        }

        public void Initialize()
        {
            Initialize(true);
        }

        public void Initialize(bool autoStart)
        {
            DateTime now = _clock();
            CurrentDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _completedSessions.Clear();
            _completedAbsences.Clear();
            _activeAbsence = null;

            List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
            foreach (WorkSession s in loaded)
            {
                if (s.Reason == "Ativo")
                {
                    s.Reason = "Recuperado";
                }
                _completedSessions.Add(s);
            }

            List<AbsenceRecord> loadedAbsences = _storage.LoadAbsencesForDate(CurrentDate);
            foreach (AbsenceRecord a in loadedAbsences)
            {
                _completedAbsences.Add(a);
            }

            if (autoStart)
            {
                StartActiveSession(now);
            }
            else
            {
                _activeSession = null;
                State = TrackerState.PausedManual;
            }

            Persist(now);
        }

        public void ResetToday()
        {
            DateTime now = _clock();
            bool wasWorking = (State == TrackerState.Working);
            CurrentDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _completedSessions.Clear();
            _completedAbsences.Clear();
            _activeSession = null;
            _activeAbsence = null;

            _storage.SaveSessionsForDate(CurrentDate, new List<WorkSession>());
            _storage.SaveAbsencesForDate(CurrentDate, new List<AbsenceRecord>());
            _storage.DeleteDailyRecord(CurrentDate);

            AlertFlags freshFlags = new AlertFlags();
            freshFlags.WorkDate = CurrentDate;
            _storage.SaveAlertFlags(freshFlags);

            if (wasWorking)
            {
                StartActiveSession(now);
                Persist(now);
            }
            else
            {
                State = TrackerState.PausedManual;
            }
        }

        public void Tick()
        {
            DateTime now = _clock();
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (today != CurrentDate)
            {
                HandleDayRollover(now, today);
                return;
            }

            if (State == TrackerState.Working && _activeSession != null)
            {
                if ((now - _lastPersistTime).TotalSeconds >= _heartbeatIntervalSeconds)
                {
                    Persist(now);
                }
            }
        }

        public void PauseBySession(string reason)
        {
            if (State != TrackerState.Working)
            {
                return;
            }

            string normalizedReason = string.IsNullOrEmpty(reason) ? "BloqueioTela" : reason;

            // Modo Reunião: não congela nem registra ausência ao bloquear tela
            if (InMeetingMode && normalizedReason == "BloqueioTela")
            {
                return;
            }

            DateTime now = _clock();
            CloseActiveSession(now, normalizedReason);
            State = TrackerState.PausedSession;

            string note = normalizedReason == "Suspensao" ? "Suspensão" : (normalizedReason == "Logoff" ? "Logoff" : string.Empty);
            StartActiveAbsence(now, AbsenceTypes.BloqueioTela, note);
            Persist(now);
        }

        public void ResumeFromSession()
        {
            if (State != TrackerState.PausedSession)
            {
                return;
            }

            DateTime now = _clock();
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (today != CurrentDate)
            {
                // Desbloqueou em outro dia: não contabiliza a noite toda como ausência
                _activeAbsence = null;
                SwitchToDate(today);
            }
            else
            {
                CloseActiveAbsence(now);
            }

            StartActiveSession(now);
            Persist(now);
        }

        public void PauseManual()
        {
            PauseManual(AbsenceTypes.PausaRapida, string.Empty);
        }

        public void PauseManual(string absenceType, string note)
        {
            DateTime now = _clock();
            string type = string.IsNullOrEmpty(absenceType) ? AbsenceTypes.PausaRapida : absenceType;

            if (State == TrackerState.Working)
            {
                CloseActiveSession(now, MapAbsenceToSessionReason(type));
                State = TrackerState.PausedManual;
                StartActiveAbsence(now, type, note);
                Persist(now);
            }
            else if (_activeAbsence != null)
            {
                // Já está pausado: permite atualizar o motivo da ausência em andamento
                _activeAbsence.Type = type;
                _activeAbsence.Note = note ?? string.Empty;
                State = TrackerState.PausedManual;
                Persist(now);
            }
            else
            {
                State = TrackerState.PausedManual;
                StartActiveAbsence(now, type, note);
                Persist(now);
            }
        }

        public void ResumeManual()
        {
            if (State == TrackerState.Working)
            {
                return;
            }

            DateTime now = _clock();
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (today != CurrentDate)
            {
                _activeAbsence = null;
                SwitchToDate(today);
            }
            else
            {
                CloseActiveAbsence(now);
            }

            StartActiveSession(now);
            Persist(now);
        }

        public void ToggleManualPause()
        {
            if (State == TrackerState.Working)
            {
                PauseManual(AbsenceTypes.PausaRapida, string.Empty);
            }
            else
            {
                ResumeManual();
            }
        }

        public List<int> FindOverlappingAutoAbsenceIndices(DateTime startTime, DateTime endTime)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < _completedAbsences.Count; i++)
            {
                AbsenceRecord a = _completedAbsences[i];
                if (a.Type != AbsenceTypes.BloqueioTela && a.Type != AbsenceTypes.PausaRapida)
                {
                    continue;
                }
                if (a.StartTime < endTime && a.EndTime > startTime)
                {
                    indices.Add(i);
                }
            }
            return indices;
        }

        public void AddManualAbsence(DateTime startTime, DateTime endTime, string type, string note)
        {
            AddManualAbsence(startTime, endTime, type, note, false);
        }

        public void AddManualAbsence(DateTime startTime, DateTime endTime, string type, string note, bool replaceOverlappingAuto)
        {
            if (endTime <= startTime)
            {
                return;
            }

            if (replaceOverlappingAuto)
            {
                List<int> overlapping = FindOverlappingAutoAbsenceIndices(startTime, endTime);
                for (int i = overlapping.Count - 1; i >= 0; i--)
                {
                    _completedAbsences.RemoveAt(overlapping[i]);
                }
            }

            DateTime now = _clock();
            AbsenceRecord rec = new AbsenceRecord();
            rec.WorkDate = CurrentDate;
            rec.StartTime = startTime;
            rec.EndTime = endTime;
            rec.DurationSeconds = Math.Max(1, (int)(endTime - startTime).TotalSeconds);
            rec.Type = string.IsNullOrEmpty(type) ? AbsenceTypes.Outro : type;
            rec.Note = note ?? string.Empty;
            rec.IsActive = false;

            _completedAbsences.Add(rec);
            _completedAbsences.Sort(delegate(AbsenceRecord a, AbsenceRecord b)
            {
                return a.StartTime.CompareTo(b.StartTime);
            });

            PersistAbsencesAndDaily(now);
        }

        public bool UpdateAbsence(int index, string newType, string newNote)
        {
            if (index < 0 || index >= _completedAbsences.Count)
            {
                return false;
            }

            _completedAbsences[index].Type = string.IsNullOrEmpty(newType) ? AbsenceTypes.Outro : newType;
            _completedAbsences[index].Note = newNote ?? string.Empty;
            PersistAbsencesAndDaily(_clock());
            return true;
        }

        public bool DeleteAbsence(int index)
        {
            if (index < 0 || index >= _completedAbsences.Count)
            {
                return false;
            }

            _completedAbsences.RemoveAt(index);
            PersistAbsencesAndDaily(_clock());
            return true;
        }

        public void Shutdown(string reason)
        {
            DateTime now = _clock();
            if (State == TrackerState.Working && _activeSession != null)
            {
                CloseActiveSession(now, string.IsNullOrEmpty(reason) ? "Encerrado" : reason);
                State = TrackerState.PausedSession;
            }
            else if (_activeAbsence != null && State == TrackerState.PausedManual)
            {
                // Fecha ausência manual em andamento ao sair do app
                CloseActiveAbsence(now);
            }
            else
            {
                _activeAbsence = null;
            }

            InMeetingMode = false;
            Persist(now);
        }

        public int GetTodayTotalSeconds()
        {
            return GetTodayTotalSeconds(_clock());
        }

        public int GetTodayTotalSeconds(DateTime now)
        {
            int total = 0;
            foreach (WorkSession s in _completedSessions)
            {
                total += s.DurationSeconds;
            }
            if (State == TrackerState.Working && _activeSession != null)
            {
                int current = (int)(now - _activeSession.StartTime).TotalSeconds;
                if (current > 0)
                {
                    total += current;
                }
            }
            return total;
        }

        public int GetTodayRegularSeconds()
        {
            int total = GetTodayTotalSeconds();
            return Math.Min(total, _regularLimitSeconds);
        }

        public int GetTodayOvertimeSeconds()
        {
            if (_overtimeLimitSeconds <= 0)
            {
                return 0;
            }
            int total = GetTodayTotalSeconds();
            return total > _regularLimitSeconds ? total - _regularLimitSeconds : 0;
        }

        public int GetTodayAbsenceSeconds()
        {
            return GetTodayAbsenceSeconds(_clock());
        }

        public int GetTodayAbsenceSeconds(DateTime now)
        {
            int total = 0;
            foreach (AbsenceRecord a in _completedAbsences)
            {
                total += a.DurationSeconds;
            }
            if (_activeAbsence != null && State != TrackerState.Working)
            {
                int current = (int)(now - _activeAbsence.StartTime).TotalSeconds;
                if (current > 0)
                {
                    total += current;
                }
            }
            return total;
        }

        public AbsenceSummary GetTodayAbsenceSummary()
        {
            AbsenceSummary summary = new AbsenceSummary();
            List<AbsenceRecord> list = GetTodayAbsencesSnapshot();
            foreach (AbsenceRecord a in list)
            {
                summary.TotalSeconds += a.DurationSeconds;
                switch (a.Type)
                {
                    case AbsenceTypes.Intervalo:
                        summary.IntervaloSeconds += a.DurationSeconds;
                        break;
                    case AbsenceTypes.Medico:
                        summary.MedicoSeconds += a.DurationSeconds;
                        break;
                    case AbsenceTypes.PausaRapida:
                        summary.PausaRapidaSeconds += a.DurationSeconds;
                        break;
                    case AbsenceTypes.BloqueioTela:
                        summary.BloqueioTelaSeconds += a.DurationSeconds;
                        break;
                    default:
                        summary.OutroSeconds += a.DurationSeconds;
                        break;
                }
            }
            return summary;
        }

        public List<WorkSession> GetTodaySessionsSnapshot()
        {
            DateTime now = _clock();
            List<WorkSession> snapshot = new List<WorkSession>();
            foreach (WorkSession s in _completedSessions)
            {
                snapshot.Add(s.Clone());
            }
            if (State == TrackerState.Working && _activeSession != null)
            {
                WorkSession live = _activeSession.Clone();
                live.EndTime = now;
                live.DurationSeconds = Math.Max(0, (int)(now - live.StartTime).TotalSeconds);
                live.Reason = InMeetingMode ? "EmReuniao" : "Ativo";
                snapshot.Add(live);
            }
            return snapshot;
        }

        public List<AbsenceRecord> GetTodayAbsencesSnapshot()
        {
            DateTime now = _clock();
            List<AbsenceRecord> snapshot = new List<AbsenceRecord>();
            foreach (AbsenceRecord a in _completedAbsences)
            {
                snapshot.Add(a.Clone());
            }
            if (_activeAbsence != null && State != TrackerState.Working)
            {
                AbsenceRecord live = _activeAbsence.Clone();
                live.EndTime = now;
                live.DurationSeconds = Math.Max(0, (int)(now - live.StartTime).TotalSeconds);
                live.IsActive = true;
                snapshot.Add(live);
            }
            return snapshot;
        }

        public DailyRecord BuildDailyRecord(DateTime now)
        {
            List<WorkSession> all = GetTodaySessionsSnapshot();
            int total = 0;
            string firstStart = string.Empty;
            string lastEnd = string.Empty;

            for (int i = 0; i < all.Count; i++)
            {
                WorkSession s = all[i];
                total += s.DurationSeconds;
                if (i == 0)
                {
                    firstStart = s.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }
                lastEnd = s.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }

            int regular = Math.Min(total, _regularLimitSeconds);
            int overtime = (_overtimeLimitSeconds > 0 && total > _regularLimitSeconds)
                ? total - _regularLimitSeconds
                : 0;

            int limitHours = Math.Max(1, TotalLimitSeconds / 3600);
            string status;
            if (total >= TotalLimitSeconds)
            {
                status = string.Format("Limite {0}h Atingido", limitHours);
            }
            else if (_overtimeLimitSeconds > 0 && total >= _regularLimitSeconds)
            {
                status = "Hora Extra";
            }
            else
            {
                status = "Normal";
            }

            DailyRecord record = new DailyRecord();
            record.WorkDate = CurrentDate;
            record.FirstStart = firstStart;
            record.LastEnd = lastEnd;
            record.TotalSeconds = total;
            record.RegularSeconds = regular;
            record.OvertimeSeconds = overtime;
            record.TotalAbsenceSeconds = GetTodayAbsenceSeconds(now);
            record.StatusLabel = status;
            return record;
        }

        private void HandleDayRollover(DateTime now, string newDate)
        {
            bool wasWorking = (State == TrackerState.Working && _activeSession != null);

            if (wasWorking)
            {
                DateTime previousDayEnd = DateTime.ParseExact(CurrentDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .AddHours(23).AddMinutes(59).AddSeconds(59);
                if (previousDayEnd < _activeSession.StartTime)
                {
                    previousDayEnd = _activeSession.StartTime;
                }
                CloseActiveSession(previousDayEnd, "ViradaDeDia");
                Persist(previousDayEnd);
            }

            _activeAbsence = null;
            SwitchToDate(newDate);

            if (wasWorking)
            {
                DateTime newDayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                StartActiveSession(newDayStart);
                Persist(now);
            }
        }

        private void SwitchToDate(string newDate)
        {
            CurrentDate = newDate;
            _completedSessions.Clear();
            _completedAbsences.Clear();

            List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
            foreach (WorkSession s in loaded)
            {
                _completedSessions.Add(s);
            }

            List<AbsenceRecord> loadedAbsences = _storage.LoadAbsencesForDate(CurrentDate);
            foreach (AbsenceRecord a in loadedAbsences)
            {
                _completedAbsences.Add(a);
            }
        }

        private void StartActiveSession(DateTime startTime)
        {
            WorkSession ws = new WorkSession();
            ws.WorkDate = CurrentDate;
            ws.StartTime = startTime;
            ws.EndTime = startTime;
            ws.DurationSeconds = 0;
            ws.Reason = "Ativo";
            _activeSession = ws;
            State = TrackerState.Working;
        }

        private void CloseActiveSession(DateTime endTime, string reason)
        {
            if (_activeSession == null)
            {
                return;
            }

            if (endTime < _activeSession.StartTime)
            {
                endTime = _activeSession.StartTime;
            }

            int duration = (int)(endTime - _activeSession.StartTime).TotalSeconds;
            _activeSession.EndTime = endTime;
            _activeSession.DurationSeconds = Math.Max(0, duration);
            _activeSession.Reason = reason;

            if (_activeSession.DurationSeconds > 0 || _completedSessions.Count == 0)
            {
                _completedSessions.Add(_activeSession);
            }

            _activeSession = null;
        }

        private void StartActiveAbsence(DateTime startTime, string type, string note)
        {
            AbsenceRecord rec = new AbsenceRecord();
            rec.WorkDate = CurrentDate;
            rec.StartTime = startTime;
            rec.EndTime = startTime;
            rec.DurationSeconds = 0;
            rec.Type = string.IsNullOrEmpty(type) ? AbsenceTypes.PausaRapida : type;
            rec.Note = note ?? string.Empty;
            rec.IsActive = true;
            _activeAbsence = rec;
        }

        private void CloseActiveAbsence(DateTime endTime)
        {
            if (_activeAbsence == null)
            {
                return;
            }

            if (endTime < _activeAbsence.StartTime)
            {
                endTime = _activeAbsence.StartTime;
            }

            int duration = (int)(endTime - _activeAbsence.StartTime).TotalSeconds;
            if (duration >= 1)
            {
                _activeAbsence.EndTime = endTime;
                _activeAbsence.DurationSeconds = duration;
                _activeAbsence.IsActive = false;
                _completedAbsences.Add(_activeAbsence);
            }

            _activeAbsence = null;
        }

        private static string MapAbsenceToSessionReason(string absenceType)
        {
            switch (absenceType)
            {
                case AbsenceTypes.Intervalo: return "Intervalo";
                case AbsenceTypes.Medico: return "Medico";
                case AbsenceTypes.Outro: return "AusenciaManual";
                default: return "PausaManual";
            }
        }

        private void PersistAbsencesAndDaily(DateTime now)
        {
            _storage.SaveAbsencesForDate(CurrentDate, _completedAbsences);
            if (_completedSessions.Count > 0 || _activeSession != null || _completedAbsences.Count > 0)
            {
                DailyRecord daily = BuildDailyRecord(now);
                _storage.UpsertDailyRecord(daily);
            }
        }

        private void Persist(DateTime now)
        {
            List<WorkSession> snapshot = GetTodaySessionsSnapshot();
            if (snapshot.Count > 0)
            {
                _storage.SaveSessionsForDate(CurrentDate, snapshot);
            }
            if (_completedAbsences.Count > 0)
            {
                _storage.SaveAbsencesForDate(CurrentDate, _completedAbsences);
            }
            if (snapshot.Count > 0 || _completedAbsences.Count > 0)
            {
                DailyRecord daily = BuildDailyRecord(now);
                _storage.UpsertDailyRecord(daily);
            }
            _lastPersistTime = now;
        }
    }
}
