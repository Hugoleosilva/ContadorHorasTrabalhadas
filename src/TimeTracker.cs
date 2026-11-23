using System;
using System.Collections.Generic;
using System.Globalization;

namespace RelogioAcompanhamento
{
    public class TimeTracker
    {
        private readonly CsvStorage _storage;
        private readonly Func<DateTime> _clock;
        private readonly int _regularLimitSeconds;
        private readonly int _overtimeLimitSeconds;
        private readonly int _heartbeatIntervalSeconds;

        private readonly List<WorkSession> _completedSessions;
        private WorkSession _activeSession;
        private DateTime _lastPersistTime;

        public string CurrentDate { get; private set; }
        public TrackerState State { get; private set; }
        public int RegularLimitSeconds { get { return _regularLimitSeconds; } }
        public int OvertimeLimitSeconds { get { return _overtimeLimitSeconds; } }
        public int TotalLimitSeconds { get { return _regularLimitSeconds + _overtimeLimitSeconds; } }

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
            State = TrackerState.PausedSession;
            CurrentDate = _clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _lastPersistTime = DateTime.MinValue;
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

            List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
            foreach (WorkSession s in loaded)
            {
                if (s.Reason == "Ativo")
                {
                    s.Reason = "Recuperado";
                }
                _completedSessions.Add(s);
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
            _activeSession = null;

            _storage.SaveSessionsForDate(CurrentDate, new List<WorkSession>());
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

            DateTime now = _clock();
            CloseActiveSession(now, string.IsNullOrEmpty(reason) ? "BloqueioTela" : reason);
            State = TrackerState.PausedSession;
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
                CurrentDate = today;
                _completedSessions.Clear();
                List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
                foreach (WorkSession s in loaded)
                {
                    _completedSessions.Add(s);
                }
            }

            StartActiveSession(now);
            Persist(now);
        }

        public void PauseManual()
        {
            if (State != TrackerState.Working)
            {
                return;
            }

            DateTime now = _clock();
            CloseActiveSession(now, "PausaManual");
            State = TrackerState.PausedManual;
            Persist(now);
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
                CurrentDate = today;
                _completedSessions.Clear();
                List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
                foreach (WorkSession s in loaded)
                {
                    _completedSessions.Add(s);
                }
            }

            StartActiveSession(now);
            Persist(now);
        }

        public void ToggleManualPause()
        {
            if (State == TrackerState.Working)
            {
                PauseManual();
            }
            else
            {
                ResumeManual();
            }
        }

        public void Shutdown(string reason)
        {
            DateTime now = _clock();
            if (State == TrackerState.Working && _activeSession != null)
            {
                CloseActiveSession(now, string.IsNullOrEmpty(reason) ? "Encerrado" : reason);
                State = TrackerState.PausedSession;
            }
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
            int total = GetTodayTotalSeconds();
            return total > _regularLimitSeconds ? total - _regularLimitSeconds : 0;
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
                live.Reason = "Ativo";
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
            int overtime = total > _regularLimitSeconds ? total - _regularLimitSeconds : 0;

            string status;
            if (total >= TotalLimitSeconds)
            {
                status = "Limite 10h Atingido";
            }
            else if (total >= _regularLimitSeconds)
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

            CurrentDate = newDate;
            _completedSessions.Clear();

            List<WorkSession> loaded = _storage.LoadSessionsForDate(CurrentDate);
            foreach (WorkSession s in loaded)
            {
                _completedSessions.Add(s);
            }

            if (wasWorking)
            {
                DateTime newDayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                StartActiveSession(newDayStart);
                Persist(now);
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

        private void Persist(DateTime now)
        {
            List<WorkSession> snapshot = GetTodaySessionsSnapshot();
            if (snapshot.Count > 0)
            {
                _storage.SaveSessionsForDate(CurrentDate, snapshot);
                DailyRecord daily = BuildDailyRecord(now);
                _storage.UpsertDailyRecord(daily);
            }
            _lastPersistTime = now;
        }
    }
}
