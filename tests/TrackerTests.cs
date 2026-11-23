using System;
using System.Collections.Generic;
using System.IO;
using RelogioAcompanhamento;

namespace RelogioAcompanhamento.Tests
{
    internal static class TrackerTests
    {
        private static int _passed = 0;
        private static int _failed = 0;

        private static int Main()
        {
            Run("AcumuloComBloqueioERetorno", TestSessionLockAndUnlock);
            Run("PausaManualNaoRetomaNoDesbloqueio", TestManualPauseSurvivesUnlock);
            Run("Alertas8hE10hDisparoUnico", TestAlertsFireOncePerDay);
            Run("ViradaDeMeiaNoite", TestDayRollover);
            Run("RecuperacaoAposQuedaHeartbeat", TestCrashRecoveryFromHeartbeat);
            Run("PersistenciaConfigCsv", TestConfigPersistence);
            Run("IniciarPausadoEZerarHoje", TestStartPausedAndResetToday);

            Console.WriteLine();
            Console.WriteLine(string.Format("Resultado: {0} passou(ram), {1} falhou(aram).", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[OK] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FALHOU] " + name + ": " + ex.Message);
            }
        }

        private static void AssertEqual(object expected, object actual, string msg)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(string.Format("{0} (esperado={1}, obtido={2})", msg, expected, actual));
            }
        }

        private static void AssertTrue(bool condition, string msg)
        {
            if (!condition)
            {
                throw new Exception(msg);
            }
        }

        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "RelogioTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void CleanupDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        private static void TestSessionLockAndUnlock()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 19, 9, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                now = now.AddMinutes(30); // 09:30 -> 1800s trabalhados
                AssertEqual(1800, tracker.GetTodayTotalSeconds(), "Deve contar 30 min antes do bloqueio");

                tracker.PauseBySession("BloqueioTela");
                AssertEqual(TrackerState.PausedSession, tracker.State, "Estado deve ser PausedSession");

                now = now.AddMinutes(45); // 45 min bloqueado (nao deve contar!)
                AssertEqual(1800, tracker.GetTodayTotalSeconds(), "Tempo bloqueado nao pode ser somado");

                tracker.ResumeFromSession();
                AssertEqual(TrackerState.Working, tracker.State, "Deve retomar apos desbloqueio");

                now = now.AddMinutes(20); // +20 min trabalhados -> 50 min = 3000s
                AssertEqual(3000, tracker.GetTodayTotalSeconds(), "Deve somar apenas tempo ativo (30 + 20 = 50 min)");

                tracker.Shutdown("Encerrado");
                List<DailyRecord> daily = storage.LoadDailyRecords();
                AssertEqual(1, daily.Count, "Deve gerar 1 registro diario no CSV");
                AssertEqual(3000, daily[0].TotalSeconds, "Total no CSV deve ser 3000s");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestManualPauseSurvivesUnlock()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 19, 10, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                now = now.AddMinutes(10);
                tracker.PauseManual();
                AssertEqual(TrackerState.PausedManual, tracker.State, "Estado deve ser PausedManual");

                // Bloqueio e desbloqueio do Windows enquanto estava em pausa manual
                tracker.PauseBySession("BloqueioTela");
                now = now.AddMinutes(30);
                tracker.ResumeFromSession();

                AssertEqual(TrackerState.PausedManual, tracker.State, "Desbloqueio nao deve cancelar pausa manual");
                AssertEqual(600, tracker.GetTodayTotalSeconds(), "Tempo nao deve avançar durante pausa manual");

                tracker.ResumeManual();
                AssertEqual(TrackerState.Working, tracker.State, "Retomada manual deve voltar a contar");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestAlertsFireOncePerDay()
        {
            string dir = CreateTempDir();
            try
            {
                CsvStorage storage = new CsvStorage(dir);
                AlertManager alerts = new AlertManager(storage, 8 * 3600, 2 * 3600, 15 * 60);
                string date = "2026-11-19";

                AssertEqual(AlertLevel.Normal, alerts.GetAlertLevel(7 * 3600), "7h deve ser Normal");
                AssertTrue(alerts.Evaluate(date, 7 * 3600) == null, "7h nao deve disparar alerta");

                AlertNotification n1 = alerts.Evaluate(date, 7 * 3600 + 45 * 60);
                AssertTrue(n1 != null && n1.Level == AlertLevel.PreRegular, "7h45 deve disparar PreRegular");
                AssertTrue(alerts.Evaluate(date, 7 * 3600 + 50 * 60) == null, "PreRegular nao deve repetir");

                AlertNotification n2 = alerts.Evaluate(date, 8 * 3600);
                AssertTrue(n2 != null && n2.Level == AlertLevel.Overtime, "8h00 deve disparar Overtime");
                AssertTrue(alerts.Evaluate(date, 8 * 3600 + 120) == null, "Overtime nao deve repetir");

                // Simula reinicio do app recarregando flags do disco
                AlertManager alertsReloaded = new AlertManager(storage, 8 * 3600, 2 * 3600, 15 * 60);
                AssertTrue(alertsReloaded.Evaluate(date, 8 * 3600 + 300) == null, "Overtime nao deve repetir apos reinicio");

                AlertNotification n3 = alertsReloaded.Evaluate(date, 9 * 3600 + 45 * 60);
                AssertTrue(n3 != null && n3.Level == AlertLevel.PreOvertimeLimit, "9h45 deve disparar PreOvertimeLimit");

                AlertNotification n4 = alertsReloaded.Evaluate(date, 10 * 3600);
                AssertTrue(n4 != null && n4.Level == AlertLevel.LimitReached && n4.IsCritical, "10h00 deve disparar LimitReached critico");
                AssertTrue(alertsReloaded.Evaluate(date, 10 * 3600 + 60) == null, "LimitReached nao deve repetir");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestDayRollover()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 19, 23, 55, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                now = new DateTime(2026, 11, 20, 0, 5, 0);
                tracker.Tick();

                AssertEqual("2026-11-20", tracker.CurrentDate, "Data deve virar para 2026-11-20");
                AssertEqual(300, tracker.GetTodayTotalSeconds(), "Novo dia deve ter 5 min (300s)");

                List<DailyRecord> records = storage.LoadDailyRecords();
                AssertEqual(2, records.Count, "Deve haver registros para os dois dias");
                AssertEqual("2026-11-19", records[0].WorkDate, "Primeiro registro deve ser dia 19");
                AssertEqual(299, records[0].TotalSeconds, "Dia 19 deve fechar em 23:59:59 (299s)");
                AssertEqual("2026-11-20", records[1].WorkDate, "Segundo registro deve ser dia 20");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestCrashRecoveryFromHeartbeat()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 19, 14, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker1 = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker1.Initialize();
                now = now.AddMinutes(15);
                tracker1.Tick(); // dispara heartbeat e grava sessao "Ativo" com 900s

                // Simula queda de energia (sem chamar Shutdown) e reabertura 10 min depois
                now = now.AddMinutes(10);
                TimeTracker tracker2 = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);
                tracker2.Initialize();

                AssertEqual(900, tracker2.GetTodayTotalSeconds(), "Deve preservar os 900s salvos no ultimo heartbeat");
                List<WorkSession> sessions = tracker2.GetTodaySessionsSnapshot();
                AssertEqual("Recuperado", sessions[0].Reason, "Sessao interrompida deve ser marcada como Recuperado");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestConfigPersistence()
        {
            string dir = CreateTempDir();
            try
            {
                CsvStorage storage = new CsvStorage(dir);
                AppConfig cfg = new AppConfig();
                cfg.WidgetWidth = 280;
                cfg.WidgetHeight = 90;
                cfg.WidgetX = 1200;
                cfg.WidgetY = 850;
                cfg.ShowInTaskbar = true;
                cfg.AlwaysOnTop = false;
                storage.SaveConfig(cfg);

                AppConfig loaded = storage.LoadConfig();
                AssertEqual(280, loaded.WidgetWidth, "Largura deve persistir");
                AssertEqual(90, loaded.WidgetHeight, "Altura deve persistir");
                AssertEqual(1200, loaded.WidgetX, "Posicao X deve persistir");
                AssertEqual(850, loaded.WidgetY, "Posicao Y deve persistir");
                AssertEqual(true, loaded.ShowInTaskbar, "ShowInTaskbar deve persistir");
                AssertEqual(false, loaded.AlwaysOnTop, "AlwaysOnTop deve persistir");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestStartPausedAndResetToday()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 19, 8, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize(false);
                AssertEqual(TrackerState.PausedManual, tracker.State, "Deve iniciar pausado quando autoStart=false");

                now = now.AddMinutes(10);
                AssertEqual(0, tracker.GetTodayTotalSeconds(), "Nao deve contar enquanto inicia pausado");

                tracker.ResumeManual();
                now = now.AddMinutes(5);
                AssertEqual(300, tracker.GetTodayTotalSeconds(), "Deve contar apos clique em retomar");

                tracker.ResetToday();
                AssertEqual(0, tracker.GetTodayTotalSeconds(), "ResetToday deve zerar o contador do dia");
                AssertEqual(1, tracker.GetTodaySessionsSnapshot().Count, "Se estava ativo, inicia nova sessao zerada");
            }
            finally
            {
                CleanupDir(dir);
            }
        }
    }
}
