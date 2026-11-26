using System;
using System.Collections.Generic;
using System.Globalization;

namespace RelogioAcompanhamento
{
    public static class PatternRadarCalculator
    {
        public const int DefaultMinRequiredDays = 10;
        private const double EmaAlpha = 0.35;

        public static WeeklyFatigueContext GetWeeklyFatigueContext(
            DateTime todayDate,
            List<DailyRecord> savedDailyRecords,
            Dictionary<string, int> creditedAbsencesByDate,
            Dictionary<string, DayOverrideRecord> overridesByDate,
            DateTime bankStartDate)
        {
            WeeklyFatigueContext ctx = new WeeklyFatigueContext();
            DateTime today = todayDate.Date;
            DateTime minStart = bankStartDate.Date;

            Dictionary<string, DailyRecord> dailyMap = new Dictionary<string, DailyRecord>();
            if (savedDailyRecords != null)
            {
                foreach (DailyRecord r in savedDailyRecords)
                {
                    dailyMap[r.WorkDate] = r;
                }
            }

            // 1) Saldo fechado na semana atual (Seg até ontem)
            DateTime weekStart, weekEnd;
            HourBankCalculator.GetWeekPeriod(today, out weekStart, out weekEnd);
            if (weekStart < minStart) weekStart = minStart;

            for (DateTime d = weekStart; d < today; d = d.AddDays(1))
            {
                if (!HourBankCalculator.IsWeekday(d)) continue;
                string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                DailyRecord rec;
                dailyMap.TryGetValue(key, out rec);
                DayOverrideRecord ov;
                overridesByDate.TryGetValue(key, out ov);

                int absCred = 0;
                if (creditedAbsencesByDate != null) creditedAbsencesByDate.TryGetValue(key, out absCred);

                int worked = rec != null ? rec.TotalSeconds : 0;
                int credited = absCred;
                if (ov != null)
                {
                    if (ov.ManualWorkedSeconds > 0) worked = Math.Max(worked, ov.ManualWorkedSeconds);
                    credited += ov.CreditedSeconds;
                }

                if (worked > 0 || credited > 0 || ov != null)
                {
                    ctx.WeekClosedBalanceSeconds += HourBankCalculator.CalculateDailyDeltaSeconds(
                        worked, credited, HourBankCalculator.DailyTargetSeconds, HourBankCalculator.DailyToleranceSeconds);
                }
            }

            // 2) Sequência de dias úteis seguidos com hora extra / saída tardia (>= 20:00) voltando de ontem
            bool countingOt = true;
            bool countingLate = true;
            DateTime cursor = today.AddDays(-1);
            int maxLookback = 21;

            for (int step = 0; step < maxLookback && cursor >= minStart && (countingOt || countingLate); step++)
            {
                if (!HourBankCalculator.IsWeekday(cursor))
                {
                    cursor = cursor.AddDays(-1);
                    continue;
                }

                string key = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DailyRecord rec;
                dailyMap.TryGetValue(key, out rec);

                if (rec == null || rec.TotalSeconds <= 0)
                {
                    countingOt = false;
                    countingLate = false;
                    break;
                }

                if (countingOt)
                {
                    int delta = HourBankCalculator.CalculateDailyDeltaSeconds(
                        rec.TotalSeconds, 0, HourBankCalculator.DailyTargetSeconds, HourBankCalculator.DailyToleranceSeconds);
                    if (delta > 0)
                    {
                        ctx.ConsecutiveOvertimeDays++;
                    }
                    else
                    {
                        countingOt = false;
                    }
                }

                if (countingLate)
                {
                    int endMin = ParseTimeToMinutes(rec.LastEnd);
                    if (endMin >= 20 * 60)
                    {
                        ctx.ConsecutiveLateExitDays++;
                    }
                    else
                    {
                        countingLate = false;
                    }
                }

                cursor = cursor.AddDays(-1);
            }

            return ctx;
        }

        public static string BuildPreRegularContextHint(WeeklyFatigueContext ctx)
        {
            if (ctx == null) return string.Empty;
            if (ctx.ConsecutiveOvertimeDays >= 2 && ctx.WeekClosedBalanceSeconds > 0)
            {
                return string.Format(
                    " Você já acumulou {0} esta semana em {1} dias seguidos estendidos. Que tal encerrar no horário hoje para descansar?",
                    TimeFormat.FormatSignedHms(ctx.WeekClosedBalanceSeconds),
                    ctx.ConsecutiveOvertimeDays);
            }
            if (ctx.WeekClosedBalanceSeconds >= 3600)
            {
                return string.Format(
                    " Você já tem {0} de saldo positivo nesta semana — ótimo momento para manter o ritmo equilibrado.",
                    TimeFormat.FormatSignedHms(ctx.WeekClosedBalanceSeconds));
            }
            return string.Empty;
        }

        public static CycleSimulationResult Analyze(
            DateTime todayDate,
            List<DailyRecord> savedDailyRecords,
            Dictionary<string, int> creditedAbsencesByDate,
            Dictionary<string, DayOverrideRecord> overridesByDate,
            DateTime bankStartDate,
            int currentCycleBalanceSeconds)
        {
            DateTime today = todayDate.Date;
            DateTime minStart = bankStartDate.Date;

            DateTime cycleStart, cycleEnd;
            int cycleNum;
            HourBankCalculator.GetCyclePeriod(today, out cycleStart, out cycleEnd, out cycleNum);

            CycleSimulationResult result = new CycleSimulationResult();
            result.MinRequiredDays = DefaultMinRequiredDays;
            result.CycleEndDate = cycleEnd;
            result.CurrentCycleBalanceSeconds = currentCycleBalanceSeconds;
            result.ConservativeProjectedBalanceSeconds = currentCycleBalanceSeconds;

            // Coleta apenas dias úteis fechados com trabalho efetivo registrado (exclui faltas/dias sem registro para não distorcer a média de rotina)
            List<DailyRecord> validDays = new List<DailyRecord>();
            if (savedDailyRecords != null)
            {
                foreach (DailyRecord r in savedDailyRecords)
                {
                    DateTime dt;
                    if (!DateTime.TryParseExact(r.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    {
                        continue;
                    }
                    if (dt < minStart || dt >= today || !HourBankCalculator.IsWeekday(dt))
                    {
                        continue;
                    }
                    if (r.TotalSeconds <= 0)
                    {
                        continue;
                    }
                    validDays.Add(r);
                }
            }

            validDays.Sort(delegate(DailyRecord a, DailyRecord b)
            {
                return string.CompareOrdinal(a.WorkDate, b.WorkDate);
            });

            result.RecordedDaysCount = validDays.Count;

            // Conta dias úteis restantes de hoje até o fim do ciclo (descontando feriados/abonos totais já cadastrados)
            Dictionary<DayOfWeek, int> remainingByDow = new Dictionary<DayOfWeek, int>();
            DayOfWeek[] weekdays = new DayOfWeek[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
            };
            foreach (DayOfWeek dow in weekdays) remainingByDow[dow] = 0;

            int remainingCount = 0;
            for (DateTime d = today; d <= cycleEnd; d = d.AddDays(1))
            {
                if (!HourBankCalculator.IsWeekday(d)) continue;
                string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                DayOverrideRecord ov;
                if (overridesByDate != null && overridesByDate.TryGetValue(key, out ov) && ov.CreditedSeconds >= HourBankCalculator.DailyTargetSeconds)
                {
                    continue; // Feriado ou abono total futuro já cadastrado
                }
                remainingCount++;
                remainingByDow[d.DayOfWeek] = remainingByDow[d.DayOfWeek] + 1;
            }
            result.RemainingWeekdays = remainingCount;

            if (validDays.Count < result.MinRequiredDays)
            {
                result.HasEnoughData = false;
                result.RealisticProjectedBalanceSeconds = currentCycleBalanceSeconds;
                result.RecommendationText = string.Format(
                    "Coletando histórico ({0}/{1} dias úteis registrados). Com {1}+ dias o Radar libera projeção de fechamento e sugestões de compensação.",
                    validDays.Count,
                    result.MinRequiredDays);
                return result;
            }

            result.HasEnoughData = true;

            // Agrupa por dia da semana
            Dictionary<DayOfWeek, List<int>> deltasByDow = new Dictionary<DayOfWeek, List<int>>();
            Dictionary<DayOfWeek, List<int>> startsByDow = new Dictionary<DayOfWeek, List<int>>();
            Dictionary<DayOfWeek, List<int>> endsByDow = new Dictionary<DayOfWeek, List<int>>();
            List<int> allDeltas = new List<int>();

            foreach (DayOfWeek dow in weekdays)
            {
                deltasByDow[dow] = new List<int>();
                startsByDow[dow] = new List<int>();
                endsByDow[dow] = new List<int>();
            }

            foreach (DailyRecord r in validDays)
            {
                DateTime dt = DateTime.ParseExact(r.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                int absCred = 0;
                if (creditedAbsencesByDate != null) creditedAbsencesByDate.TryGetValue(r.WorkDate, out absCred);

                DayOverrideRecord ov;
                int ovCred = 0;
                if (overridesByDate != null && overridesByDate.TryGetValue(r.WorkDate, out ov))
                {
                    ovCred = ov.CreditedSeconds;
                }

                int delta = HourBankCalculator.CalculateDailyDeltaSeconds(
                    r.TotalSeconds,
                    absCred + ovCred,
                    HourBankCalculator.DailyTargetSeconds,
                    HourBankCalculator.DailyToleranceSeconds);

                deltasByDow[dt.DayOfWeek].Add(delta);
                allDeltas.Add(delta);

                int sMin = ParseTimeToMinutes(r.FirstStart);
                if (sMin >= 0) startsByDow[dt.DayOfWeek].Add(sMin);

                int eMin = ParseTimeToMinutes(r.LastEnd);
                if (eMin >= 0) endsByDow[dt.DayOfWeek].Add(eMin);
            }

            int globalEma = ComputeEma(allDeltas, EmaAlpha);
            Dictionary<DayOfWeek, int> emaByDow = new Dictionary<DayOfWeek, int>();

            WeekdayProfile bestCalmDay = null;
            double bestScore = double.MaxValue;

            foreach (DayOfWeek dow in weekdays)
            {
                List<int> deltas = deltasByDow[dow];
                int ema = deltas.Count > 0 ? ComputeEma(deltas, EmaAlpha) : globalEma;
                int stdDev = deltas.Count >= 2 ? ComputeStdDev(deltas) : 0;
                emaByDow[dow] = ema;

                WeekdayProfile prof = new WeekdayProfile();
                prof.DayOfWeek = dow;
                prof.DayNamePt = GetFullDayNamePt(dow);
                prof.SampleCount = deltas.Count;
                prof.MedianStartMinutes = ComputeMedian(startsByDow[dow]);
                prof.MedianEndMinutes = ComputeMedian(endsByDow[dow]);
                prof.EmaBalanceSeconds = ema;
                prof.StdDevBalanceSeconds = stdDev;
                result.WeekdayProfiles.Add(prof);

                // Dia mais propício para compensar folga/saída antecipada: menor hora extra média + menor volatilidade
                if (deltas.Count > 0)
                {
                    double score = ema + (stdDev * 0.5);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestCalmDay = prof;
                    }
                }
            }

            // Projeção Realista ponderada por dia útil restante
            long projectedDelta = 0;
            foreach (DayOfWeek dow in weekdays)
            {
                projectedDelta += (long)remainingByDow[dow] * emaByDow[dow];
            }
            result.RealisticProjectedBalanceSeconds = currentCycleBalanceSeconds + (int)projectedDelta;

            double remainingWeeks = Math.Max(1.0, remainingCount / 5.0);
            result.WeeklyAdjustmentToZeroSeconds = (int)Math.Round(-currentCycleBalanceSeconds / remainingWeeks);

            if (bestCalmDay != null)
            {
                result.BestCompensationDayName = bestCalmDay.DayNamePt;
            }

            // Monta recomendação acionável
            if (currentCycleBalanceSeconds > 1800)
            {
                string dayTip = bestCalmDay != null
                    ? string.Format(" Melhor janela para compensar: {0} (rotina mais estável).", bestCalmDay.DayNamePt)
                    : string.Empty;
                result.RecommendationText = string.Format(
                    "No ritmo atual você fecha {0:dd/MM/yy} em {1}. Para zerar o saldo atual ({2}), abata ~{3}/semana.{4}",
                    cycleEnd,
                    TimeFormat.FormatSignedHms(result.RealisticProjectedBalanceSeconds),
                    TimeFormat.FormatSignedHms(currentCycleBalanceSeconds),
                    TimeFormat.FormatHm(Math.Abs(result.WeeklyAdjustmentToZeroSeconds)),
                    dayTip);
            }
            else if (currentCycleBalanceSeconds < -1800)
            {
                result.RecommendationText = string.Format(
                    "Saldo negativo ({0}). Para quitar até {1:dd/MM/yy} ({2} dias úteis), compense ~+{3}/semana.",
                    TimeFormat.FormatSignedHms(currentCycleBalanceSeconds),
                    cycleEnd,
                    remainingCount,
                    TimeFormat.FormatHm(Math.Abs(result.WeeklyAdjustmentToZeroSeconds)));
            }
            else
            {
                string dayTip = bestCalmDay != null
                    ? string.Format(" Dia mais estável da rotina: {0}.", bestCalmDay.DayNamePt)
                    : string.Empty;
                result.RecommendationText = string.Format(
                    "Banco equilibrado ({0}). Projeção realista até {1:dd/MM/yy}: {2}.{3}",
                    TimeFormat.FormatSignedHms(currentCycleBalanceSeconds),
                    cycleEnd,
                    TimeFormat.FormatSignedHms(result.RealisticProjectedBalanceSeconds),
                    dayTip);
            }

            return result;
        }

        private static int ComputeEma(List<int> values, double alpha)
        {
            if (values == null || values.Count == 0) return 0;
            double ema = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                ema = (alpha * values[i]) + ((1.0 - alpha) * ema);
            }
            return (int)Math.Round(ema);
        }

        private static int ComputeStdDev(List<int> values)
        {
            if (values == null || values.Count < 2) return 0;
            double mean = 0;
            foreach (int v in values) mean += v;
            mean /= values.Count;

            double sumSq = 0;
            foreach (int v in values)
            {
                double diff = v - mean;
                sumSq += diff * diff;
            }
            return (int)Math.Round(Math.Sqrt(sumSq / values.Count));
        }

        private static int ComputeMedian(List<int> values)
        {
            if (values == null || values.Count == 0) return -1;
            List<int> sorted = new List<int>(values);
            sorted.Sort();
            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 1) return sorted[mid];
            return (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static int ParseTimeToMinutes(string hms)
        {
            if (string.IsNullOrEmpty(hms)) return -1;
            string[] parts = hms.Split(':');
            if (parts.Length < 2) return -1;
            int h, m;
            if (int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m))
            {
                return h * 60 + m;
            }
            return -1;
        }

        public static string FormatMinutesAsHm(int minutesFromMidnight)
        {
            if (minutesFromMidnight < 0) return "—";
            int h = minutesFromMidnight / 60;
            int m = minutesFromMidnight % 60;
            return string.Format("{0:D2}:{1:D2}", h, m);
        }

        private static string GetFullDayNamePt(DayOfWeek dow)
        {
            switch (dow)
            {
                case DayOfWeek.Monday: return "Segunda-feira";
                case DayOfWeek.Tuesday: return "Terça-feira";
                case DayOfWeek.Wednesday: return "Quarta-feira";
                case DayOfWeek.Thursday: return "Quinta-feira";
                case DayOfWeek.Friday: return "Sexta-feira";
                default: return dow.ToString();
            }
        }
    }
}
