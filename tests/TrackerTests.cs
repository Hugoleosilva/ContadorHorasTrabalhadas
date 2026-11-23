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
            Run("AusenciasIntervaloMedicoBloqueio", TestAbsencesTrackingAndManualLaunch);
            Run("ModoReuniaoIgnoraBloqueioDeTela", TestMeetingModeIgnoresScreenLock);
            Run("PerfilEstagiario6hSemExtras", TestIntern6hProfileNoOvertime);

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
                now = now.AddMinutes(30);
                AssertEqual(1800, tracker.GetTodayTotalSeconds(), "Deve contar 30 min antes do bloqueio");

                tracker.PauseBySession("BloqueioTela");
                AssertEqual(TrackerState.PausedSession, tracker.State, "Estado deve ser PausedSession");

                now = now.AddMinutes(45);
                AssertEqual(1800, tracker.GetTodayTotalSeconds(), "Tempo bloqueado nao pode ser somado nas horas trabalhadas");

                tracker.ResumeFromSession();
                AssertEqual(TrackerState.Working, tracker.State, "Deve retomar apos desbloqueio");
                AssertEqual(2700, tracker.GetTodayAbsenceSeconds(), "45 min de bloqueio devem ser contabilizados como ausencia");

                now = now.AddMinutes(20);
                AssertEqual(3000, tracker.GetTodayTotalSeconds(), "Deve somar apenas tempo ativo (30 + 20 = 50 min)");

                tracker.Shutdown("Encerrado");
                List<DailyRecord> daily = storage.LoadDailyRecords();
                AssertEqual(1, daily.Count, "Deve gerar 1 registro diario no CSV");
                AssertEqual(3000, daily[0].TotalSeconds, "Total no CSV deve ser 3000s");
                AssertEqual(2700, daily[0].TotalAbsenceSeconds, "Ausencias no CSV devem ser 2700s");
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
                tracker.PauseManual(AbsenceTypes.Intervalo, "Almoço");
                AssertEqual(TrackerState.PausedManual, tracker.State, "Estado deve ser PausedManual");

                tracker.PauseBySession("BloqueioTela");
                now = now.AddMinutes(30);
                tracker.ResumeFromSession();

                AssertEqual(TrackerState.PausedManual, tracker.State, "Desbloqueio nao deve cancelar pausa manual");
                AssertEqual(600, tracker.GetTodayTotalSeconds(), "Tempo nao deve avançar durante pausa manual");
                AssertEqual(AbsenceTypes.Intervalo, tracker.ActiveAbsenceType, "Tipo de ausencia deve continuar Intervalo");

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
                tracker1.Tick();

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
                cfg.ApplyProfile(WorkProfile.Estagiario6h);
                cfg.WidgetWidth = 280;
                cfg.WidgetHeight = 90;
                cfg.WidgetX = 1200;
                cfg.WidgetY = 850;
                cfg.ShowInTaskbar = true;
                cfg.AlwaysOnTop = false;
                storage.SaveConfig(cfg);

                AppConfig loaded = storage.LoadConfig();
                AssertEqual(WorkProfile.Estagiario6h, loaded.Profile, "Perfil Estagiario6h deve persistir");
                AssertEqual(360, loaded.RegularLimitMinutes, "Estagiario deve ter 360 min regulares");
                AssertEqual(0, loaded.OvertimeLimitMinutes, "Estagiario deve ter 0 min extras");
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
                AssertEqual(0, tracker.GetTodayAbsenceSeconds(), "Inicio pausado nao deve gerar ausencia falsa");

                tracker.ResumeManual();
                now = now.AddMinutes(5);
                AssertEqual(300, tracker.GetTodayTotalSeconds(), "Deve contar apos clique em retomar");

                tracker.ResetToday();
                AssertEqual(0, tracker.GetTodayTotalSeconds(), "ResetToday deve zerar o contador do dia");
                AssertEqual(0, tracker.GetTodayAbsenceSeconds(), "ResetToday deve zerar ausencias do dia");
                AssertEqual(1, tracker.GetTodaySessionsSnapshot().Count, "Se estava ativo, inicia nova sessao zerada");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestAbsencesTrackingAndManualLaunch()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 23, 8, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                now = now.AddHours(2); // 10:00 (2h trabalhadas)

                // Pausa rapida de 15 min
                tracker.PauseManual(AbsenceTypes.PausaRapida, "Cafe");
                now = now.AddMinutes(15);
                tracker.ResumeManual();

                // Lancamento manual de Intervalo 1h + Ida ao Medico 1h30 (nao altera horas trabalhadas)
                tracker.AddManualAbsence(
                    new DateTime(2026, 11, 23, 12, 0, 0),
                    new DateTime(2026, 11, 23, 13, 0, 0),
                    AbsenceTypes.Intervalo,
                    "Almoco");

                tracker.AddManualAbsence(
                    new DateTime(2026, 11, 23, 14, 0, 0),
                    new DateTime(2026, 11, 23, 15, 30, 0),
                    AbsenceTypes.Medico,
                    "Consulta");

                AssertEqual(7200, tracker.GetTodayTotalSeconds(), "Ausencias nao devem alterar horas trabalhadas (2h)");
                AbsenceSummary sum = tracker.GetTodayAbsenceSummary();
                AssertEqual(900, sum.PausaRapidaSeconds, "Pausa rapida deve somar 15 min");
                AssertEqual(3600, sum.IntervaloSeconds, "Intervalo deve somar 1h");
                AssertEqual(5400, sum.MedicoSeconds, "Medico deve somar 1h30");
                AssertEqual(9900, sum.TotalSeconds, "Total de ausencias deve somar 2h45 (9900s)");

                // Reclassificar e excluir
                AssertTrue(tracker.UpdateAbsence(0, AbsenceTypes.Outro, "Reclassificado"), "UpdateAbsence deve funcionar");
                AssertTrue(tracker.DeleteAbsence(0), "DeleteAbsence deve funcionar");
                AssertEqual(9000, tracker.GetTodayAbsenceSeconds(), "Apos excluir 15 min deve restar 2h30 (9000s)");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestMeetingModeIgnoresScreenLock()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 23, 10, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                tracker.SetMeetingMode(true);
                AssertTrue(tracker.InMeetingMode, "Modo reuniao deve estar ativo");

                now = now.AddMinutes(10);
                tracker.PauseBySession("BloqueioTela"); // Usuario bloqueou PC para ir a sala de reuniao
                AssertEqual(TrackerState.Working, tracker.State, "Em modo reuniao, bloqueio de tela NAO deve pausar");

                now = now.AddMinutes(40); // 40 min na sala de reuniao com tela bloqueada
                tracker.ResumeFromSession();

                AssertEqual(3000, tracker.GetTodayTotalSeconds(), "Tempo inteiro em reuniao (50 min) deve contar como trabalhado");
                AssertEqual(0, tracker.GetTodayAbsenceSeconds(), "Bloqueio em reuniao NAO deve gerar ausencia");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestIntern6hProfileNoOvertime()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 23, 8, 0, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 6 * 3600, 0, delegate { return now; }, 30);
                AlertManager alerts = new AlertManager(storage, 6 * 3600, 0, 15 * 60);

                tracker.Initialize();
                now = now.AddHours(5).AddMinutes(45);
                AssertEqual(AlertLevel.PreRegular, alerts.GetAlertLevel(tracker.GetTodayTotalSeconds()), "5h45 deve ser PreRegular");
                AlertNotification pre = alerts.Evaluate(tracker.CurrentDate, tracker.GetTodayTotalSeconds());
                AssertTrue(pre != null && pre.Level == AlertLevel.PreRegular, "5h45 deve disparar pre-aviso de 6h");

                now = now.AddMinutes(15); // 6h00 exatas
                AssertEqual(AlertLevel.LimitReached, alerts.GetAlertLevel(tracker.GetTodayTotalSeconds()), "6h00 para estagiario deve ser LimitReached (sem extras)");
                AlertNotification lim = alerts.Evaluate(tracker.CurrentDate, tracker.GetTodayTotalSeconds());
                AssertTrue(lim != null && lim.IsCritical && lim.Level == AlertLevel.LimitReached, "6h00 deve disparar alerta critico de limite");

                now = now.AddMinutes(10); // 6h10
                AssertEqual(0, tracker.GetTodayOvertimeSeconds(), "Estagiario nunca acumula horas extras");
                DailyRecord rec = tracker.BuildDailyRecord(now);
                AssertEqual("Limite 6h Atingido", rec.StatusLabel, "Status diario deve indicar Limite 6h Atingido");
            }
            finally
            {
                CleanupDir(dir);
            }
        }
    }
}
