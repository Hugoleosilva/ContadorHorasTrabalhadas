using System;
using System.Collections.Generic;
using System.Globalization;

namespace RelogioAcompanhamento
{
    public static class HourBankCalculator
    {
        public const int DailyTargetSeconds = 8 * 3600;       // 08:00:00
        public const int DailyToleranceSeconds = 15 * 60;     // ±15 min (padrão CLT escolhido)

        public static void GetWeekPeriod(DateTime referenceDate, out DateTime start, out DateTime end)
        {
            DateTime d = referenceDate.Date;
            int diffToMonday = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            start = d.AddDays(-diffToMonday);
            end = start.AddDays(4); // Segunda a Sexta
        }

        public static void GetMonthPeriod(DateTime referenceDate, out DateTime start, out DateTime end)
        {
            DateTime d = referenceDate.Date;
            if (d.Day >= 11)
            {
                start = new DateTime(d.Year, d.Month, 11);
                DateTime nextMonth = start.AddMonths(1);
                end = new DateTime(nextMonth.Year, nextMonth.Month, 10);
            }
            else
            {
                DateTime prevMonth = d.AddMonths(-1);
                start = new DateTime(prevMonth.Year, prevMonth.Month, 11);
                end = new DateTime(d.Year, d.Month, 10);
            }
        }

        public static void GetCyclePeriod(DateTime referenceDate, out DateTime start, out DateTime end, out int cycleNumber)
        {
            DateTime d = referenceDate.Date;
            DateTime c1Start = new DateTime(d.Year, 1, 11);
            DateTime c1End = new DateTime(d.Year, 7, 10);

            if (d >= c1Start && d <= c1End)
            {
                cycleNumber = 1;
                start = c1Start;
                end = c1End;
                return;
            }

            cycleNumber = 2;
            if (d > c1End)
            {
                start = new DateTime(d.Year, 7, 11);
                end = new DateTime(d.Year + 1, 1, 10);
            }
            else
            {
                // 01/01 a 10/01 pertence ao Ciclo 2 iniciado em 11/07 do ano anterior
                start = new DateTime(d.Year - 1, 7, 11);
                end = new DateTime(d.Year, 1, 10);
            }
        }

        public static bool IsWeekday(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
        }

        public static int CalculateDailyDeltaSeconds(int workedSeconds, int creditedSeconds, int targetSeconds, int toleranceSeconds)
        {
            // Abono cobre a meta de 8h (nunca gera hora extra além da meta por abono)
            int effective = Math.Max(workedSeconds, Math.Min(targetSeconds, workedSeconds + creditedSeconds));
            int diff = effective - targetSeconds;
            if (Math.Abs(diff) <= toleranceSeconds)
            {
                return 0;
            }
            return diff;
        }

        public static HourBankSnapshot BuildSnapshot(
            DateTime todayDate,
            DailyRecord todayLiveRecord,
            int todayLiveCreditedAbsenceSeconds,
            List<DailyRecord> savedDailyRecords,
            Dictionary<string, int> creditedAbsencesByDate,
            Dictionary<string, DayOverrideRecord> overridesByDate,
            DateTime bankStartDate)
        {
            DateTime today = todayDate.Date;
            DateTime minStart = bankStartDate.Date;

            DateTime weekStart, weekEnd;
            GetWeekPeriod(today, out weekStart, out weekEnd);

            DateTime monthStart, monthEnd;
            GetMonthPeriod(today, out monthStart, out monthEnd);

            DateTime cycleStart, cycleEnd;
            int cycleNumber;
            GetCyclePeriod(today, out cycleStart, out cycleEnd, out cycleNumber);

            DateTime effectiveWeekStart = weekStart < minStart ? minStart : weekStart;
            DateTime effectiveMonthStart = monthStart < minStart ? minStart : monthStart;
            DateTime effectiveCycleStart = cycleStart < minStart ? minStart : cycleStart;

            Dictionary<string, DailyRecord> dailyMap = new Dictionary<string, DailyRecord>();
            if (savedDailyRecords != null)
            {
                foreach (DailyRecord r in savedDailyRecords)
                {
                    dailyMap[r.WorkDate] = r;
                }
            }

            HourBankSnapshot snapshot = new HourBankSnapshot();
            snapshot.Week.StartDate = effectiveWeekStart;
            snapshot.Week.EndDate = weekEnd;
            snapshot.Week.PeriodTitle = string.Format("{0:dd/MM} a {1:dd/MM}", effectiveWeekStart, weekEnd);

            snapshot.Month.StartDate = effectiveMonthStart;
            snapshot.Month.EndDate = monthEnd;
            snapshot.Month.PeriodTitle = string.Format("{0:dd/MM} a {1:dd/MM}", effectiveMonthStart, monthEnd);

            snapshot.Cycle.StartDate = effectiveCycleStart;
            snapshot.Cycle.EndDate = cycleEnd;
            snapshot.Cycle.PeriodTitle = string.Format("Ciclo {0}: {1:dd/MM/yy} a {2:dd/MM/yy}", cycleNumber, effectiveCycleStart, cycleEnd);

            // Percorre todos os dias úteis já encerrados dentro do ciclo atual (até ontem)
            DateTime yesterday = today.AddDays(-1);
            DateTime scanEnd = yesterday < cycleEnd ? yesterday : cycleEnd;

            for (DateTime d = effectiveCycleStart; d <= scanEnd; d = d.AddDays(1))
            {
                if (!IsWeekday(d))
                {
                    continue;
                }

                string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DailyRecord daily;
                dailyMap.TryGetValue(key, out daily);

                DayOverrideRecord ov;
                overridesByDate.TryGetValue(key, out ov);

                int absCredited = 0;
                if (creditedAbsencesByDate != null)
                {
                    creditedAbsencesByDate.TryGetValue(key, out absCredited);
                }

                HourBankDayEntry entry = EvaluateDay(d, daily, ov, absCredited, false);
                entry.IsInCurrentMonth = (d >= effectiveMonthStart && d <= monthEnd);
                entry.IsInCurrentWeek = (d >= effectiveWeekStart && d <= weekEnd);

                snapshot.CycleDays.Add(entry);

                snapshot.Cycle.BalanceSeconds += entry.BalanceSeconds;
                if (entry.NeedsAttention) snapshot.Cycle.PendingDaysCount++;

                if (entry.IsInCurrentMonth)
                {
                    snapshot.Month.BalanceSeconds += entry.BalanceSeconds;
                    if (entry.NeedsAttention) snapshot.Month.PendingDaysCount++;
                }

                if (entry.IsInCurrentWeek)
                {
                    snapshot.Week.BalanceSeconds += entry.BalanceSeconds;
                    if (entry.NeedsAttention) snapshot.Week.PendingDaysCount++;
                }
            }

            // Dia de hoje (em andamento — exibido separadamente sem distorcer o saldo fechado)
            if (IsWeekday(today) && today >= effectiveCycleStart && today <= cycleEnd)
            {
                string todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DayOverrideRecord todayOv;
                overridesByDate.TryGetValue(todayKey, out todayOv);

                HourBankDayEntry todayEntry = EvaluateDay(today, todayLiveRecord, todayOv, todayLiveCreditedAbsenceSeconds, true);
                todayEntry.IsInCurrentMonth = true;
                todayEntry.IsInCurrentWeek = true;
                snapshot.TodayEntry = todayEntry;
            }

            // Ordena mais recente primeiro para exibição
            snapshot.CycleDays.Sort(delegate(HourBankDayEntry a, HourBankDayEntry b)
            {
                return b.Date.CompareTo(a.Date);
            });

            return snapshot;
        }

        private static HourBankDayEntry EvaluateDay(
            DateTime date,
            DailyRecord daily,
            DayOverrideRecord ov,
            int absenceCreditedSeconds,
            bool isToday)
        {
            HourBankDayEntry entry = new HourBankDayEntry();
            entry.Date = date;
            entry.WorkDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            entry.DayOfWeekShort = GetDayOfWeekPt(date.DayOfWeek);
            entry.IsToday = isToday;

            int workedFromApp = daily != null ? daily.TotalSeconds : 0;
            int worked = workedFromApp;
            int credited = absenceCreditedSeconds;
            bool hasWork = workedFromApp > 0;

            if (ov != null)
            {
                if (ov.ManualWorkedSeconds > 0)
                {
                    worked = Math.Max(worked, ov.ManualWorkedSeconds);
                    hasWork = true;
                }
                credited += ov.CreditedSeconds;

                entry.WorkedSeconds = worked;
                entry.CreditedSeconds = credited;
                entry.BalanceSeconds = CalculateDailyDeltaSeconds(worked, credited, DailyTargetSeconds, DailyToleranceSeconds);
                entry.NeedsAttention = false;

                string label = DaySpecialTypes.GetDisplayName(ov.Type);
                entry.StatusText = string.IsNullOrEmpty(ov.Note) ? label : (label + " — " + ov.Note);
                return entry;
            }

            entry.WorkedSeconds = worked;
            entry.CreditedSeconds = credited;

            if (!isToday && !hasWork && credited == 0)
            {
                entry.BalanceSeconds = -DailyTargetSeconds;
                entry.NeedsAttention = true;
                entry.StatusText = "Sem registro (clique para apontar)";
                return entry;
            }

            entry.BalanceSeconds = CalculateDailyDeltaSeconds(worked, credited, DailyTargetSeconds, DailyToleranceSeconds);
            entry.NeedsAttention = false;

            if (isToday)
            {
                entry.StatusText = "Em andamento";
            }
            else if (entry.BalanceSeconds == 0)
            {
                entry.StatusText = credited > 0 ? "Normal (com abono)" : "Dentro da tolerância (±15m)";
            }
            else if (entry.BalanceSeconds > 0)
            {
                entry.StatusText = "Crédito no BH";
            }
            else
            {
                entry.StatusText = "Débito no BH";
            }

            return entry;
        }

        private static string GetDayOfWeekPt(DayOfWeek dow)
        {
            switch (dow)
            {
                case DayOfWeek.Monday: return "Seg";
                case DayOfWeek.Tuesday: return "Ter";
                case DayOfWeek.Wednesday: return "Qua";
                case DayOfWeek.Thursday: return "Qui";
                case DayOfWeek.Friday: return "Sex";
                case DayOfWeek.Saturday: return "Sáb";
                default: return "Dom";
            }
        }
    }
}
