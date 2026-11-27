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
        public bool PersonalUseMode { get; private set; }
        public bool WorkdayEnded { get; private set; }
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
            PersonalUseMode = false;
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

        public void SetPersonalUseMode(bool enabled)
        {
            DateTime now = _clock();
            if (enabled)
            {
                if (State == TrackerState.Working && _activeSession != null)
                {
                    CloseActiveSession(now, "UsoPessoal");
                }
                _activeAbsence = null;
                InMeetingMode = false;
                PersonalUseMode = true;
                State = TrackerState.PausedManual;
                Persist(now);
            }
            else
            {
                PersonalUseMode = false;
                WorkdayEnded = false;
                ResumeManual();
            }
        }

        public void EndWorkday()
        {
            DateTime now = _clock();
            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
            {
                CloseActiveSession(now, "FimDeJornada");
            }
            else if (_activeAbsence != null && State == TrackerState.PausedManual && !PersonalUseMode)
            {
                CloseActiveAbsence(now);
            }
            else
            {
                _activeAbsence = null;
            }

            InMeetingMode = false;
            PersonalUseMode = true;
            WorkdayEnded = true;
            State = TrackerState.PausedManual;
            Persist(now);
        }

        public void Initialize()
        {
            Initialize(true, false);
        }

        public void Initialize(bool autoStart)
        {
            Initialize(autoStart, false);
        }

        public void Initialize(bool autoStart, bool startInPersonalMode)
        {
            DateTime now = _clock();
            CurrentDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _completedSessions.Clear();
            _completedAbsences.Clear();
            _activeAbsence = null;
            PersonalUseMode = startInPersonalMode;
            WorkdayEnded = false;

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

            if (startInPersonalMode)
            {
                _activeSession = null;
                State = TrackerState.PausedManual;
            }
            else if (autoStart)
            {
                StartActiveSession(now);
            }
            else
            {
                _activeSession = null;
                State = TrackerState.PausedManual;
            }

            if (!startInPersonalMode)
            {
                Persist(now);
            }
        }

        public void ResetToday()
        {
            DateTime now = _clock();
            bool wasWorking = (State == TrackerState.Working && !PersonalUseMode);
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

            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
            {
                if ((now - _lastPersistTime).TotalSeconds >= _heartbeatIntervalSeconds)
                {
                    Persist(now);
                }
            }
        }

        public void PauseBySession(string reason)
        {
            if (PersonalUseMode || State != TrackerState.Working)
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
            StartActiveAbsence(now, AbsenceTypes.BloqueioTela, note, false);
            Persist(now);
        }

        public void ResumeFromSession()
        {
            if (PersonalUseMode || State != TrackerState.PausedSession)
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

        public void PauseManual()
        {
            PauseManual(AbsenceTypes.PausaRapida, string.Empty);
        }

        public void PauseManual(string absenceType, string note)
        {
            if (PersonalUseMode)
            {
                return;
            }

            DateTime now = _clock();
            string type = string.IsNullOrEmpty(absenceType) ? AbsenceTypes.PausaRapida : absenceType;
            bool credited = AbsenceTypes.IsCreditedByDefault(type);

            if (State == TrackerState.Working)
            {
                CloseActiveSession(now, MapAbsenceToSessionReason(type));
                State = TrackerState.PausedManual;
                StartActiveAbsence(now, type, note, credited);
                Persist(now);
            }
            else if (_activeAbsence != null)
            {
                _activeAbsence.Type = type;
                _activeAbsence.Note = note ?? string.Empty;
                _activeAbsence.Credited = credited;
                State = TrackerState.PausedManual;
                Persist(now);
            }
            else
            {
                State = TrackerState.PausedManual;
                StartActiveAbsence(now, type, note, credited);
                Persist(now);
            }
        }

        public void ResumeManual()
        {
            if (State == TrackerState.Working && !PersonalUseMode)
            {
                return;
            }

            PersonalUseMode = false;
            WorkdayEnded = false;

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
            if (PersonalUseMode)
            {
                ResumeManual();
                return;
            }

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
            AddManualAbsence(startTime, endTime, type, note, AbsenceTypes.IsCreditedByDefault(type), true);
        }

        public void AddManualAbsence(DateTime startTime, DateTime endTime, string type, string note, bool credited, bool overwriteOverlaps)
        {
            if (endTime <= startTime)
            {
                return;
            }

            DateTime now = _clock();

            if (overwriteOverlaps)
            {
                // 1) Sobrescreve bloqueios / pausas automáticas sobrepostas na aba Ausências
                List<int> overlappingAbs = FindOverlappingAutoAbsenceIndices(startTime, endTime);
                for (int i = overlappingAbs.Count - 1; i >= 0; i--)
                {
                    _completedAbsences.RemoveAt(overlappingAbs[i]);
                }

                // 2) Sobrescreve / recorta sessões trabalhadas que caíram dentro do período da ausência
                TrimWorkSessionsForAbsence(startTime, endTime, now);
            }

            AbsenceRecord rec = new AbsenceRecord();
            rec.WorkDate = CurrentDate;
            rec.StartTime = startTime;
            rec.EndTime = endTime;
            rec.DurationSeconds = Math.Max(1, (int)(endTime - startTime).TotalSeconds);
            rec.Type = string.IsNullOrEmpty(type) ? AbsenceTypes.Outro : type;
            rec.Note = note ?? string.Empty;
            rec.Credited = credited;
            rec.IsActive = false;

            _completedAbsences.Add(rec);
            _completedAbsences.Sort(delegate(AbsenceRecord a, AbsenceRecord b)
            {
                return a.StartTime.CompareTo(b.StartTime);
            });

            Persist(now);
        }

        public bool UpdateAbsence(int index, string newType, string newNote)
        {
            return UpdateAbsence(index, newType, newNote, AbsenceTypes.IsCreditedByDefault(newType));
        }

        public bool UpdateAbsence(int index, string newType, string newNote, bool credited)
        {
            if (index < 0 || index >= _completedAbsences.Count)
            {
                return false;
            }

            _completedAbsences[index].Type = string.IsNullOrEmpty(newType) ? AbsenceTypes.Outro : newType;
            _completedAbsences[index].Note = newNote ?? string.Empty;
            _completedAbsences[index].Credited = credited;
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
            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
            {
                CloseActiveSession(now, string.IsNullOrEmpty(reason) ? "Encerrado" : reason);
                State = TrackerState.PausedSession;
            }
            else if (_activeAbsence != null && State == TrackerState.PausedManual && !PersonalUseMode)
            {
                CloseActiveAbsence(now);
            }
            else
            {
                _activeAbsence = null;
            }

            InMeetingMode = false;
            if (!PersonalUseMode || _completedSessions.Count > 0 || _completedAbsences.Count > 0)
            {
                Persist(now);
            }
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
            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
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

        public int GetTodayCreditedAbsenceSeconds()
        {
            return GetTodayCreditedAbsenceSeconds(_clock());
        }

        public int GetTodayCreditedAbsenceSeconds(DateTime now)
        {
            int total = 0;
            foreach (AbsenceRecord a in _completedAbsences)
            {
                if (a.Credited)
                {
                    total += a.DurationSeconds;
                }
            }
            if (_activeAbsence != null && State != TrackerState.Working && _activeAbsence.Credited)
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
            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
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

        private void TrimWorkSessionsForAbsence(DateTime absStart, DateTime absEnd, DateTime now)
        {
            List<WorkSession> updated = new List<WorkSession>();
            foreach (WorkSession s in _completedSessions)
            {
                if (s.EndTime <= absStart || s.StartTime >= absEnd)
                {
                    updated.Add(s);
                    continue;
                }

                // Parte anterior à ausência
                if (s.StartTime < absStart)
                {
                    int beforeDur = (int)(absStart - s.StartTime).TotalSeconds;
                    if (beforeDur > 0)
                    {
                        WorkSession before = s.Clone();
                        before.EndTime = absStart;
                        before.DurationSeconds = beforeDur;
                        updated.Add(before);
                    }
                }

                // Parte posterior à ausência
                if (s.EndTime > absEnd)
                {
                    int afterDur = (int)(s.EndTime - absEnd).TotalSeconds;
                    if (afterDur > 0)
                    {
                        WorkSession after = s.Clone();
                        after.StartTime = absEnd;
                        after.DurationSeconds = afterDur;
                        updated.Add(after);
                    }
                }
            }

            _completedSessions.Clear();
            foreach (WorkSession s in updated)
            {
                _completedSessions.Add(s);
            }

            // Ajusta sessão ativa se o intervalo da ausência cobriu o início dela
            if (State == TrackerState.Working && _activeSession != null && !PersonalUseMode)
            {
                if (_activeSession.StartTime < absEnd && now > absStart)
                {
                    if (_activeSession.StartTime < absStart)
                    {
                        WorkSession before = _activeSession.Clone();
                        before.EndTime = absStart;
                        before.DurationSeconds = Math.Max(0, (int)(absStart - before.StartTime).TotalSeconds);
                        before.Reason = "AusenciaManual";
                        if (before.DurationSeconds > 0)
                        {
                            _completedSessions.Add(before);
                        }
                    }

                    DateTime newStart = absEnd > now ? now : absEnd;
                    _activeSession.StartTime = newStart;
                    _activeSession.EndTime = now;
                    _activeSession.DurationSeconds = Math.Max(0, (int)(now - newStart).TotalSeconds);
                }
            }
        }

        private void HandleDayRollover(DateTime now, string newDate)
        {
            bool wasWorking = (State == TrackerState.Working && _activeSession != null && !PersonalUseMode);

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

        private void StartActiveAbsence(DateTime startTime, string type, string note, bool credited)
        {
            AbsenceRecord rec = new AbsenceRecord();
            rec.WorkDate = CurrentDate;
            rec.StartTime = startTime;
            rec.EndTime = startTime;
            rec.DurationSeconds = 0;
            rec.Type = string.IsNullOrEmpty(type) ? AbsenceTypes.PausaRapida : type;
            rec.Note = note ?? string.Empty;
            rec.Credited = credited;
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
            _storage.SaveSessionsForDate(CurrentDate, snapshot);
            _storage.SaveAbsencesForDate(CurrentDate, _completedAbsences);

            if (snapshot.Count > 0 || _completedAbsences.Count > 0)
            {
                DailyRecord daily = BuildDailyRecord(now);
                _storage.UpsertDailyRecord(daily);
            }
            _lastPersistTime = now;
        }
    }
}
