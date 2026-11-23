using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RelogioAcompanhamento
{
    public class CsvStorage
    {
        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        private const string DailyHeader = "Data;PrimeiroInicio;UltimoRegistro;TotalTrabalhado;HorasNormais;HorasExtras;TotalSegundos;StatusLimite";
        private const string SessionsHeader = "Data;Inicio;Fim;DuracaoSegundos;Motivo";

        public string BaseDirectory { get; private set; }

        public CsvStorage(string baseDirectory)
        {
            BaseDirectory = baseDirectory;
            EnsureDirectory();
        }

        public static string GetDefaultDataDirectory(bool demoMode)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(localAppData, "RelogioAcompanhamento");
            if (demoMode)
            {
                folder = Path.Combine(folder, "Demo");
            }
            return folder;
        }

        public void EnsureDirectory()
        {
            if (!Directory.Exists(BaseDirectory))
            {
                Directory.CreateDirectory(BaseDirectory);
            }
        }

        public string GetConfigPath()
        {
            return Path.Combine(BaseDirectory, "config.ini");
        }

        public string GetAlertFlagsPath()
        {
            return Path.Combine(BaseDirectory, "estado_alertas.ini");
        }

        public string GetDailyFilePath()
        {
            return Path.Combine(BaseDirectory, "registros_diarios.csv");
        }

        public string GetSessionsFilePath(string workDate)
        {
            string yearMonth = workDate.Length >= 7 ? workDate.Substring(0, 7) : DateTime.Now.ToString("yyyy-MM");
            return Path.Combine(BaseDirectory, string.Format("sessoes_{0}.csv", yearMonth));
        }

        public AppConfig LoadConfig()
        {
            AppConfig config = new AppConfig();
            string path = GetConfigPath();
            if (!File.Exists(path))
            {
                return config;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    bool boolVal;
                    int intVal;

                    if (key == "StartWithWindows" && bool.TryParse(val, out boolVal)) config.StartWithWindows = boolVal;
                    else if (key == "AutoStartCounting" && bool.TryParse(val, out boolVal)) config.AutoStartCounting = boolVal;
                    else if (key == "WidgetVisible" && bool.TryParse(val, out boolVal)) config.WidgetVisible = boolVal;
                    else if (key == "AlwaysOnTop" && bool.TryParse(val, out boolVal)) config.AlwaysOnTop = boolVal;
                    else if (key == "KeepInTaskbar" && bool.TryParse(val, out boolVal)) config.ShowInTaskbar = boolVal;
                    else if (key == "WidgetWidth" && int.TryParse(val, out intVal)) config.WidgetWidth = Clamp(intVal, 155, 420);
                    else if (key == "WidgetHeight" && int.TryParse(val, out intVal)) config.WidgetHeight = Clamp(intVal, 46, 140);
                    else if (key == "WidgetX" && int.TryParse(val, out intVal)) config.WidgetX = intVal;
                    else if (key == "WidgetY" && int.TryParse(val, out intVal)) config.WidgetY = intVal;
                    else if (key == "RegularLimitMinutes" && int.TryParse(val, out intVal) && intVal > 0) config.RegularLimitMinutes = intVal;
                    else if (key == "OvertimeLimitMinutes" && int.TryParse(val, out intVal) && intVal >= 0) config.OvertimeLimitMinutes = intVal;
                    else if (key == "PreAlertMinutes" && int.TryParse(val, out intVal) && intVal >= 0) config.PreAlertMinutes = intVal;
                }
            }
            catch
            {
                // Mantém padrões seguros caso o arquivo esteja corrompido
            }

            return config;
        }

        public void SaveConfig(AppConfig config)
        {
            EnsureDirectory();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Configuracoes do Relogio Acompanhamento");
            sb.AppendLine("StartWithWindows=" + config.StartWithWindows.ToString());
            sb.AppendLine("AutoStartCounting=" + config.AutoStartCounting.ToString());
            sb.AppendLine("WidgetVisible=" + config.WidgetVisible.ToString());
            sb.AppendLine("AlwaysOnTop=" + config.AlwaysOnTop.ToString());
            sb.AppendLine("KeepInTaskbar=" + config.ShowInTaskbar.ToString());
            sb.AppendLine("WidgetWidth=" + config.WidgetWidth.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WidgetHeight=" + config.WidgetHeight.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WidgetX=" + config.WidgetX.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("WidgetY=" + config.WidgetY.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RegularLimitMinutes=" + config.RegularLimitMinutes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("OvertimeLimitMinutes=" + config.OvertimeLimitMinutes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("PreAlertMinutes=" + config.PreAlertMinutes.ToString(CultureInfo.InvariantCulture));
            WriteAllTextAtomic(GetConfigPath(), sb.ToString(), Utf8Bom);
        }

        public AlertFlags LoadAlertFlags(string workDate)
        {
            AlertFlags flags = new AlertFlags();
            flags.WorkDate = workDate;
            string path = GetAlertFlagsPath();
            if (!File.Exists(path))
            {
                return flags;
            }

            try
            {
                string savedDate = string.Empty;
                bool preReg = false, reg = false, preLim = false, lim = false;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string rawLine in lines)
                {
                    int eq = rawLine.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = rawLine.Substring(0, eq).Trim();
                    string val = rawLine.Substring(eq + 1).Trim();
                    bool b;
                    if (key == "WorkDate") savedDate = val;
                    else if (key == "PreRegularSent" && bool.TryParse(val, out b)) preReg = b;
                    else if (key == "RegularSent" && bool.TryParse(val, out b)) reg = b;
                    else if (key == "PreLimitSent" && bool.TryParse(val, out b)) preLim = b;
                    else if (key == "LimitSent" && bool.TryParse(val, out b)) lim = b;
                }

                if (savedDate == workDate)
                {
                    flags.PreRegularSent = preReg;
                    flags.RegularSent = reg;
                    flags.PreLimitSent = preLim;
                    flags.LimitSent = lim;
                }
            }
            catch
            {
                // Ignora arquivo inválido
            }

            return flags;
        }

        public void SaveAlertFlags(AlertFlags flags)
        {
            EnsureDirectory();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WorkDate=" + flags.WorkDate);
            sb.AppendLine("PreRegularSent=" + flags.PreRegularSent.ToString());
            sb.AppendLine("RegularSent=" + flags.RegularSent.ToString());
            sb.AppendLine("PreLimitSent=" + flags.PreLimitSent.ToString());
            sb.AppendLine("LimitSent=" + flags.LimitSent.ToString());
            WriteAllTextAtomic(GetAlertFlagsPath(), sb.ToString(), Utf8Bom);
        }

        public List<WorkSession> LoadSessionsForDate(string workDate)
        {
            List<WorkSession> result = new List<WorkSession>();
            string path = GetSessionsFilePath(workDate);
            if (!File.Exists(path))
            {
                return result;
            }

            List<WorkSession> all = ReadSessionsFile(path);
            foreach (WorkSession session in all)
            {
                if (session.WorkDate == workDate)
                {
                    result.Add(session);
                }
            }
            return result;
        }

        public void SaveSessionsForDate(string workDate, List<WorkSession> sessionsForDate)
        {
            EnsureDirectory();
            string path = GetSessionsFilePath(workDate);
            List<WorkSession> existing = File.Exists(path) ? ReadSessionsFile(path) : new List<WorkSession>();
            List<WorkSession> merged = new List<WorkSession>();

            foreach (WorkSession s in existing)
            {
                if (s.WorkDate != workDate)
                {
                    merged.Add(s);
                }
            }
            foreach (WorkSession s in sessionsForDate)
            {
                merged.Add(s);
            }

            merged.Sort(delegate(WorkSession a, WorkSession b)
            {
                int cmp = string.CompareOrdinal(a.WorkDate, b.WorkDate);
                if (cmp != 0) return cmp;
                return a.StartTime.CompareTo(b.StartTime);
            });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(SessionsHeader);
            foreach (WorkSession s in merged)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0};{1};{2};{3};{4}",
                    Sanitize(s.WorkDate),
                    s.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    s.EndTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    s.DurationSeconds,
                    Sanitize(s.Reason)));
            }

            WriteAllTextAtomic(path, sb.ToString(), Utf8Bom);
        }

        public List<DailyRecord> LoadDailyRecords()
        {
            List<DailyRecord> records = new List<DailyRecord>();
            string path = GetDailyFilePath();
            if (!File.Exists(path))
            {
                return records;
            }

            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    string[] parts = line.Split(';');
                    if (parts.Length < 8) continue;

                    int totalSec;
                    if (!int.TryParse(parts[6], out totalSec))
                    {
                        totalSec = ParseHmsToSeconds(parts[3]);
                    }
                    int regSec = ParseHmsToSeconds(parts[4]);
                    int extraSec = ParseHmsToSeconds(parts[5]);

                    DailyRecord rec = new DailyRecord();
                    rec.WorkDate = parts[0].Trim();
                    rec.FirstStart = parts[1].Trim();
                    rec.LastEnd = parts[2].Trim();
                    rec.TotalSeconds = totalSec;
                    rec.RegularSeconds = regSec;
                    rec.OvertimeSeconds = extraSec;
                    rec.StatusLabel = parts[7].Trim();
                    records.Add(rec);
                }
            }
            catch
            {
                // Ignora leitura inválida
            }

            return records;
        }

        public void UpsertDailyRecord(DailyRecord record)
        {
            EnsureDirectory();
            List<DailyRecord> records = LoadDailyRecords();
            bool updated = false;

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].WorkDate == record.WorkDate)
                {
                    records[i] = record;
                    updated = true;
                    break;
                }
            }

            if (!updated)
            {
                records.Add(record);
            }

            records.Sort(delegate(DailyRecord a, DailyRecord b)
            {
                return string.CompareOrdinal(a.WorkDate, b.WorkDate);
            });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(DailyHeader);
            foreach (DailyRecord r in records)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0};{1};{2};{3};{4};{5};{6};{7}",
                    Sanitize(r.WorkDate),
                    Sanitize(r.FirstStart),
                    Sanitize(r.LastEnd),
                    TimeFormat.FormatHms(r.TotalSeconds),
                    TimeFormat.FormatHms(r.RegularSeconds),
                    TimeFormat.FormatHms(r.OvertimeSeconds),
                    r.TotalSeconds,
                    Sanitize(r.StatusLabel)));
            }

            WriteAllTextAtomic(GetDailyFilePath(), sb.ToString(), Utf8Bom);
        }

        public void DeleteDailyRecord(string workDate)
        {
            string path = GetDailyFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            List<DailyRecord> records = LoadDailyRecords();
            List<DailyRecord> remaining = new List<DailyRecord>();
            foreach (DailyRecord r in records)
            {
                if (r.WorkDate != workDate)
                {
                    remaining.Add(r);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(DailyHeader);
            foreach (DailyRecord r in remaining)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0};{1};{2};{3};{4};{5};{6};{7}",
                    Sanitize(r.WorkDate),
                    Sanitize(r.FirstStart),
                    Sanitize(r.LastEnd),
                    TimeFormat.FormatHms(r.TotalSeconds),
                    TimeFormat.FormatHms(r.RegularSeconds),
                    TimeFormat.FormatHms(r.OvertimeSeconds),
                    r.TotalSeconds,
                    Sanitize(r.StatusLabel)));
            }

            WriteAllTextAtomic(path, sb.ToString(), Utf8Bom);
        }

        private static List<WorkSession> ReadSessionsFile(string path)
        {
            List<WorkSession> list = new List<WorkSession>();
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    string[] parts = line.Split(';');
                    if (parts.Length < 5) continue;

                    string workDate = parts[0].Trim();
                    DateTime startDt;
                    DateTime endDt;
                    int durSec;

                    if (!DateTime.TryParseExact(workDate + " " + parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out startDt))
                    {
                        continue;
                    }
                    if (!DateTime.TryParseExact(workDate + " " + parts[2].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out endDt))
                    {
                        endDt = startDt;
                    }
                    if (!int.TryParse(parts[3].Trim(), out durSec))
                    {
                        durSec = Math.Max(0, (int)(endDt - startDt).TotalSeconds);
                    }

                    WorkSession ws = new WorkSession();
                    ws.WorkDate = workDate;
                    ws.StartTime = startDt;
                    ws.EndTime = endDt;
                    ws.DurationSeconds = Math.Max(0, durSec);
                    ws.Reason = parts[4].Trim();
                    list.Add(ws);
                }
            }
            catch
            {
                // Ignora linhas corrompidas
            }
            return list;
        }

        private static int ParseHmsToSeconds(string hms)
        {
            if (string.IsNullOrEmpty(hms)) return 0;
            string[] parts = hms.Split(':');
            if (parts.Length != 3) return 0;
            int h, m, s;
            if (int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out s))
            {
                return Math.Max(0, h * 3600 + m * 60 + s);
            }
            return 0;
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace(";", ",").Replace("\r", "").Replace("\n", "");
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static void WriteAllTextAtomic(string targetPath, string content, Encoding encoding)
        {
            string tempPath = targetPath + ".tmp";
            File.WriteAllText(tempPath, content, encoding);
            try
            {
                if (File.Exists(targetPath))
                {
                    File.Replace(tempPath, targetPath, null);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            catch
            {
                // Fallback caso o arquivo esteja aberto para leitura no Excel
                File.Copy(tempPath, targetPath, true);
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
