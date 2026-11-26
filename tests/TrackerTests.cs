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
            Run("AusenciasSobrescrevemSessoesEBloqueios", TestAbsencesOverwriteSessionsAndBlocks);
            Run("ModoReuniaoIgnoraBloqueioDeTela", TestMeetingModeIgnoresScreenLock);
            Run("PerfilEstagiario6hSemExtras", TestIntern6hProfileNoOvertime);
            Run("BancoDeHorasTolerancia15MinEAbono", TestHourBankToleranceAndCredit);
            Run("BancoDeHorasCiclosCorte11A10EDiasPendentes", TestHourBankPeriodsAndOverrides);
            Run("ModoUsoPessoalNaoContabiliza", TestPersonalUseModeDoesNotCount);
            Run("BemEstarTimerAtivoPausaEReuniao", TestWellnessContextualReminders);
            Run("AlertaFadigaNoturnaEContextoSemanal", TestFatigueAndEnrichedPreAlert);
            Run("RadarPadroesColdStartMenosDe10Dias", TestRadarColdStart);
            Run("SimuladorFechamentoCicloCom12Dias", TestCycleSimulatorAndWeekdayRadar);

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
                AssertEqual(600, tracker.GetTodayTotalSeconds(), "Tempo nao deve avancar durante pausa manual");
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
                cfg.AskModeOnStartup = false;
                cfg.BankStartDate = "2026-11-23";
                cfg.WidgetWidth = 280;
                cfg.WidgetHeight = 90;
                cfg.WidgetX = 1200;
                cfg.WidgetY = 850;
                cfg.ShowInTaskbar = true;
                cfg.AlwaysOnTop = false;
                storage.SaveConfig(cfg);

                AppConfig loaded = storage.LoadConfig();
                AssertEqual(WorkProfile.Estagiario6h, loaded.Profile, "Perfil Estagiario6h deve persistir");
                AssertEqual(false, loaded.AskModeOnStartup, "AskModeOnStartup deve persistir");
                AssertEqual("2026-11-23", loaded.BankStartDate, "BankStartDate deve persistir");
                AssertEqual(360, loaded.RegularLimitMinutes, "Estagiario deve ter 360 min regulares");
                AssertEqual(0, loaded.OvertimeLimitMinutes, "Estagiario deve ter 0 min extras");
                AssertEqual(280, loaded.WidgetWidth, "Largura deve persistir");
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

        private static void TestAbsencesOverwriteSessionsAndBlocks()
        {
            string dir = CreateTempDir();
            try
            {
                // Simula o cenário real: ligou PC 10:41-10:56, bloqueou ate 11:50, voltou 11:50-12:00
                DateTime now = new DateTime(2026, 11, 26, 10, 41, 0);
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize();
                now = new DateTime(2026, 11, 26, 10, 56, 0); // 15 min
                tracker.PauseBySession("BloqueioTela");

                now = new DateTime(2026, 11, 26, 11, 50, 0); // 54 min bloqueado
                tracker.ResumeFromSession();

                now = new DateTime(2026, 11, 26, 12, 0, 0); // 10 min trabalhando
                AssertEqual(1500, tracker.GetTodayTotalSeconds(), "Antes da ausencia deve ter 25 min (15+10)");
                AssertEqual(3240, tracker.GetTodayAbsenceSeconds(), "Antes da ausencia deve ter 54 min de bloqueio");

                // Lanca Ida ao Medico de 08:00 ate 11:50 -> deve sobrescrever a sessao 10:41-10:56 E o bloqueio 10:56-11:50!
                tracker.AddManualAbsence(
                    new DateTime(2026, 11, 26, 8, 0, 0),
                    new DateTime(2026, 11, 26, 11, 50, 0),
                    AbsenceTypes.Medico,
                    "Consulta Pe");

                AssertEqual(600, tracker.GetTodayTotalSeconds(), "Apos sobrescrever 08:00-11:50 deve sobrar apenas 10 min (11:50-12:00)");
                AssertEqual(13800, tracker.GetTodayAbsenceSeconds(), "Ausencia total deve ser exatas 3h50 (13800s) sem duplicar bloqueio");
                AssertEqual(13800, tracker.GetTodayCreditedAbsenceSeconds(), "Ida ao Medico deve abonar 3h50 no BH por padrao");
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
                tracker.PauseBySession("BloqueioTela");
                AssertEqual(TrackerState.Working, tracker.State, "Em modo reuniao, bloqueio de tela NAO deve pausar");

                now = now.AddMinutes(40);
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

                now = now.AddMinutes(15);
                AssertEqual(AlertLevel.LimitReached, alerts.GetAlertLevel(tracker.GetTodayTotalSeconds()), "6h00 para estagiario deve ser LimitReached");
                AlertNotification lim = alerts.Evaluate(tracker.CurrentDate, tracker.GetTodayTotalSeconds());
                AssertTrue(lim != null && lim.IsCritical && lim.Level == AlertLevel.LimitReached, "6h00 deve disparar alerta critico");

                now = now.AddMinutes(10);
                AssertEqual(0, tracker.GetTodayOvertimeSeconds(), "Estagiario nunca acumula horas extras");
                DailyRecord rec = tracker.BuildDailyRecord(now);
                AssertEqual("Limite 6h Atingido", rec.StatusLabel, "Status diario deve indicar Limite 6h Atingido");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestHourBankToleranceAndCredit()
        {
            int target = HourBankCalculator.DailyTargetSeconds;
            int tol = HourBankCalculator.DailyToleranceSeconds; // 15 min = 900s

            AssertEqual(0, HourBankCalculator.CalculateDailyDeltaSeconds(7 * 3600 + 45 * 60, 0, target, tol), "07:45 dentro da tolerancia de 15m -> 0");
            AssertEqual(-16 * 60, HourBankCalculator.CalculateDailyDeltaSeconds(7 * 3600 + 44 * 60, 0, target, tol), "07:44 passou dos 15m -> -16m inteiro (CLT)");
            AssertEqual(0, HourBankCalculator.CalculateDailyDeltaSeconds(8 * 3600 + 15 * 60, 0, target, tol), "08:15 dentro da tolerancia de 15m -> 0");
            AssertEqual(16 * 60, HourBankCalculator.CalculateDailyDeltaSeconds(8 * 3600 + 16 * 60, 0, target, tol), "08:16 passou dos 15m -> +16m inteiro (CLT)");

            // Medico abonando falta sem gerar extra
            AssertEqual(0, HourBankCalculator.CalculateDailyDeltaSeconds(4 * 3600 + 10 * 60, 3 * 3600 + 50 * 60, target, tol), "4h10 + 3h50 medico = 8h -> 0");
            AssertEqual(0, HourBankCalculator.CalculateDailyDeltaSeconds(5 * 3600, 4 * 3600, target, tol), "5h + 4h medico nao deve gerar +1h extra");
            AssertEqual(30 * 60, HourBankCalculator.CalculateDailyDeltaSeconds(8 * 3600 + 30 * 60, 1 * 3600, target, tol), "8h30 trabalhadas reais continuam dando +30m");
        }

        private static void TestHourBankPeriodsAndOverrides()
        {
            // Verifica cortes de mes (11 a 10) e ciclos semestrais (11/01-10/07 e 11/07-10/01)
            DateTime mStart, mEnd, cStart, cEnd;
            int cNum;

            HourBankCalculator.GetMonthPeriod(new DateTime(2026, 11, 26), out mStart, out mEnd);
            AssertEqual(new DateTime(2026, 11, 11), mStart, "Nov/26 inicia em 11/11/2026");
            AssertEqual(new DateTime(2026, 12, 10), mEnd, "Nov/26 fecha em 10/12/2026");

            HourBankCalculator.GetCyclePeriod(new DateTime(2027, 1, 5), out cStart, out cEnd, out cNum);
            AssertEqual(2, cNum, "05/01/2027 ainda pertence ao Ciclo 2");
            AssertEqual(new DateTime(2026, 7, 11), cStart, "Ciclo 2 inicia 11/07/2026");
            AssertEqual(new DateTime(2027, 1, 10), cEnd, "Ciclo 2 encerra 10/01/2027");

            HourBankCalculator.GetCyclePeriod(new DateTime(2027, 1, 11), out cStart, out cEnd, out cNum);
            AssertEqual(1, cNum, "11/01/2027 reinicia Ciclo 1");
            AssertEqual(new DateTime(2027, 1, 11), cStart, "Ciclo 1 inicia 11/01/2027");
            AssertEqual(new DateTime(2027, 7, 10), cEnd, "Ciclo 1 encerra 10/07/2027");

            // Simula semana 23/11 (Seg) a 26/11 (Qui = hoje)
            // 23/11: trabalhou 8h30 (+30m)
            // 24/11: sem registro -> deve descontar -8h e sinalizar NeedsAttention
            // 25/11: marcado como Feriado -> 0
            List<DailyRecord> daily = new List<DailyRecord>();
            DailyRecord d23 = new DailyRecord();
            d23.WorkDate = "2026-11-23";
            d23.TotalSeconds = 8 * 3600 + 30 * 60;
            daily.Add(d23);

            Dictionary<string, DayOverrideRecord> overrides = new Dictionary<string, DayOverrideRecord>();
            DayOverrideRecord ov25 = new DayOverrideRecord();
            ov25.WorkDate = "2026-11-25";
            ov25.Type = DaySpecialTypes.Feriado;
            ov25.CreditedSeconds = 8 * 3600;
            overrides["2026-11-25"] = ov25;

            HourBankSnapshot snap = HourBankCalculator.BuildSnapshot(
                new DateTime(2026, 11, 26),
                new DailyRecord { WorkDate = "2026-11-26", TotalSeconds = 1800 },
                0,
                daily,
                new Dictionary<string, int>(),
                overrides,
                new DateTime(2026, 11, 23));

            AssertEqual(1, snap.Week.PendingDaysCount, "24/11 sem registro deve contar como 1 pendente");
            AssertEqual(-7 * 3600 - 30 * 60, snap.Week.BalanceSeconds, "Saldo fechado deve ser +30m - 8h = -7h30");
            AssertEqual(7 * 3600 + 30 * 60, snap.Week.ToCompensateSeconds, "A compensar deve ser 7h30");

            // Agora aponta 24/11 como "Esqueci de abrir o app" com 8h00 trabalhadas
            DayOverrideRecord ov24 = new DayOverrideRecord();
            ov24.WorkDate = "2026-11-24";
            ov24.Type = DaySpecialTypes.EsqueciAbrirApp;
            ov24.ManualWorkedSeconds = 8 * 3600;
            overrides["2026-11-24"] = ov24;

            HourBankSnapshot snap2 = HourBankCalculator.BuildSnapshot(
                new DateTime(2026, 11, 26),
                new DailyRecord { WorkDate = "2026-11-26", TotalSeconds = 1800 },
                0,
                daily,
                new Dictionary<string, int>(),
                overrides,
                new DateTime(2026, 11, 23));

            AssertEqual(0, snap2.Week.PendingDaysCount, "Apos apontar 24/11 nao deve haver pendentes");
            AssertEqual(30 * 60, snap2.Week.BalanceSeconds, "Saldo deve virar +30m");
            AssertEqual(0, snap2.Week.ToCompensateSeconds, "Com saldo positivo, a compensar deve ser 0");
        }

        private static void TestPersonalUseModeDoesNotCount()
        {
            string dir = CreateTempDir();
            try
            {
                DateTime now = new DateTime(2026, 11, 28, 10, 0, 0); // Sabado
                CsvStorage storage = new CsvStorage(dir);
                TimeTracker tracker = new TimeTracker(storage, 8 * 3600, 2 * 3600, delegate { return now; }, 30);

                tracker.Initialize(true, true); // Modo uso pessoal
                AssertTrue(tracker.PersonalUseMode, "Deve iniciar em PersonalUseMode");

                now = now.AddHours(2);
                tracker.PauseBySession("BloqueioTela");
                now = now.AddMinutes(30);
                tracker.ResumeFromSession();

                AssertEqual(0, tracker.GetTodayTotalSeconds(), "Modo uso pessoal nunca gera horas trabalhadas");
                AssertEqual(0, tracker.GetTodayAbsenceSeconds(), "Modo uso pessoal nunca gera ausencias");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestWellnessContextualReminders()
        {
            string dir = CreateTempDir();
            try
            {
                CsvStorage storage = new CsvStorage(dir);
                WellnessMonitor wellness = new WellnessMonitor(storage);
                AppConfig cfg = new AppConfig();
                cfg.WellnessReminderEnabled = true;
                cfg.WellnessIntervalMinutes = 30;
                cfg.SilenceWellnessInMeeting = true;

                DateTime now = new DateTime(2026, 11, 26, 9, 0, 0);
                // Inicia trabalhando e acumula 20 minutos
                wellness.Tick(now, "2026-11-26", TrackerState.Working, false, false, cfg);
                for (int i = 1; i <= 20 * 60; i++)
                {
                    now = now.AddSeconds(1);
                    AlertNotification n = wellness.Tick(now, "2026-11-26", TrackerState.Working, false, false, cfg);
                    AssertTrue(n == null, "Nao deve disparar antes dos 30 min");
                }
                AssertEqual(1200, wellness.ActiveStreakSeconds, "Deve acumular 20 min ativos");

                // Faz pausa natural de 6 minutos -> deve zerar o timer de bem-estar!
                now = now.AddSeconds(1);
                wellness.Tick(now, "2026-11-26", TrackerState.PausedManual, false, false, cfg);
                now = now.AddMinutes(6);
                wellness.Tick(now, "2026-11-26", TrackerState.Working, false, false, cfg);
                AssertEqual(0, wellness.ActiveStreakSeconds, "Pausa >= 5 min deve zerar o contador de bem-estar");

                // Agora acumula 30 min em Modo Reuniao -> deve segurar o aviso!
                for (int i = 1; i <= 30 * 60; i++)
                {
                    now = now.AddSeconds(1);
                    AlertNotification n = wellness.Tick(now, "2026-11-26", TrackerState.Working, false, true, cfg);
                    AssertTrue(n == null, "Modo reuniao deve segurar o lembrete de bem-estar");
                }
                AssertTrue(wellness.PendingAfterMeeting, "Deve ficar pendente para disparar apos a reuniao");

                // Encerra Modo Reuniao -> deve disparar imediatamente!
                now = now.AddSeconds(1);
                AlertNotification afterMeet = wellness.Tick(now, "2026-11-26", TrackerState.Working, false, false, cfg);
                AssertTrue(afterMeet != null && afterMeet.Title.IndexOf("Reunião encerrada", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Deve disparar lembrete logo apos encerrar Modo Reuniao");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestFatigueAndEnrichedPreAlert()
        {
            string dir = CreateTempDir();
            try
            {
                CsvStorage storage = new CsvStorage(dir);
                WellnessMonitor wellness = new WellnessMonitor(storage);
                AlertManager alerts = new AlertManager(storage, 8 * 3600, 2 * 3600, 15 * 60);
                AppConfig cfg = new AppConfig();
                cfg.FatigueAlertEnabled = true;
                cfg.PreRegularAlertEnabled = true;

                WeeklyFatigueContext ctx = new WeeklyFatigueContext();
                ctx.ConsecutiveOvertimeDays = 3;
                ctx.WeekClosedBalanceSeconds = 3 * 3600 + 21 * 60;

                string hint = PatternRadarCalculator.BuildPreRegularContextHint(ctx);
                AlertNotification pre = alerts.Evaluate("2026-11-26", 7 * 3600 + 45 * 60, true, hint);
                AssertTrue(pre != null && pre.Message.IndexOf("3 dias seguidos", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Aviso de 7h45 deve vir enriquecido com contexto semanal");

                // Alerta de fadiga noturna apos 19:30 dispara 1 vez no dia
                AlertNotification fat1 = wellness.EvaluateFatigue(
                    new DateTime(2026, 11, 26, 19, 35, 0),
                    "2026-11-26",
                    TrackerState.Working,
                    false,
                    false,
                    8 * 3600,
                    ctx,
                    cfg);
                AssertTrue(fat1 != null, "Apos 19:30 deve disparar alerta de fadiga");

                AlertNotification fat2 = wellness.EvaluateFatigue(
                    new DateTime(2026, 11, 26, 19, 45, 0),
                    "2026-11-26",
                    TrackerState.Working,
                    false,
                    false,
                    8 * 3600 + 600,
                    ctx,
                    cfg);
                AssertTrue(fat2 == null, "Alerta de fadiga nao deve repetir no mesmo dia");
            }
            finally
            {
                CleanupDir(dir);
            }
        }

        private static void TestRadarColdStart()
        {
            List<DailyRecord> daily = new List<DailyRecord>();
            for (int i = 0; i < 4; i++)
            {
                DailyRecord r = new DailyRecord();
                r.WorkDate = new DateTime(2026, 11, 23).AddDays(i).ToString("yyyy-MM-dd");
                r.TotalSeconds = 8 * 3600;
                r.FirstStart = "09:00:00";
                r.LastEnd = "18:00:00";
                daily.Add(r);
            }

            CycleSimulationResult sim = PatternRadarCalculator.Analyze(
                new DateTime(2026, 11, 27),
                daily,
                new Dictionary<string, int>(),
                new Dictionary<string, DayOverrideRecord>(),
                new DateTime(2026, 11, 23),
                0);

            AssertEqual(false, sim.HasEnoughData, "Com menos de 10 dias nao deve liberar previsao");
            AssertEqual(4, sim.RecordedDaysCount, "Deve contar 4 dias registrados");
        }

        private static void TestCycleSimulatorAndWeekdayRadar()
        {
            // Cria 12 dias uteis (de 23/11 a 08/12): Seg/Qua com +1h extra, Sex calma (8h exatas)
            List<DailyRecord> daily = new List<DailyRecord>();
            DateTime start = new DateTime(2026, 11, 23);
            int added = 0;
            DateTime cursor = start;
            while (added < 12)
            {
                if (HourBankCalculator.IsWeekday(cursor))
                {
                    DailyRecord r = new DailyRecord();
                    r.WorkDate = cursor.ToString("yyyy-MM-dd");
                    r.FirstStart = "09:00:00";
                    if (cursor.DayOfWeek == DayOfWeek.Monday || cursor.DayOfWeek == DayOfWeek.Wednesday)
                    {
                        r.TotalSeconds = 9 * 3600; // +1h extra
                        r.LastEnd = "19:00:00";
                    }
                    else
                    {
                        r.TotalSeconds = 8 * 3600; // 0 saldo
                        r.LastEnd = "18:00:00";
                    }
                    daily.Add(r);
                    added++;
                }
                cursor = cursor.AddDays(1);
            }

            // Cadastra Natal (25/12/2026) como feriado futuro -> deve descontar dos dias uteis restantes
            Dictionary<string, DayOverrideRecord> overrides = new Dictionary<string, DayOverrideRecord>();
            DayOverrideRecord natal = new DayOverrideRecord();
            natal.WorkDate = "2026-12-25";
            natal.Type = DaySpecialTypes.Feriado;
            natal.CreditedSeconds = 8 * 3600;
            overrides["2026-12-25"] = natal;

            CycleSimulationResult sim = PatternRadarCalculator.Analyze(
                cursor, // 09/12/2026
                daily,
                new Dictionary<string, int>(),
                overrides,
                start,
                5 * 3600);

            AssertEqual(true, sim.HasEnoughData, "Com 12 dias deve liberar Radar e Simulador");
            AssertEqual(12, sim.RecordedDaysCount, "Deve contar 12 dias uteis");
            AssertTrue(sim.RealisticProjectedBalanceSeconds > sim.ConservativeProjectedBalanceSeconds,
                "Com historico de horas extras, projecao realista deve superar a conservadora");
            AssertTrue(sim.WeekdayProfiles.Count == 5, "Deve gerar perfil dos 5 dias uteis (Seg-Sex)");
            AssertTrue(sim.BestCompensationDayName != "Segunda-feira" && sim.BestCompensationDayName != "Quarta-feira",
                "Nao deve recomendar dias de pico de extra como melhor janela de compensacao");
        }
    }
}
