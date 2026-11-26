using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RelogioAcompanhamento
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly bool _demoMode;
        private readonly CsvStorage _storage;
        private readonly AppConfig _config;
        private readonly TimeTracker _tracker;
        private readonly AlertManager _alerts;
        private readonly FloatingWidgetForm _widget;
        private readonly NotifyIcon _trayIcon;
        private readonly Timer _tickTimer;

        private HistoryForm _historyForm;
        private ToolStripMenuItem _menuShowHide;
        private ToolStripMenuItem _menuPauseResume;
        private ToolStripMenuItem _menuMeetingMode;
        private ToolStripMenuItem _menuPersonalMode;
        private ToolStripMenuItem _menuProfileClt;
        private ToolStripMenuItem _menuProfileEstagiario;
        private ToolStripMenuItem _menuAskModeOnStartup;
        private ToolStripMenuItem _menuAutoStartCounting;
        private ToolStripMenuItem _menuAlwaysOnTop;
        private ToolStripMenuItem _menuShowInTaskbar;
        private ToolStripMenuItem _menuStartWithWindows;

        private Icon _currentTrayIcon;
        private string _lastIconKey;
        private bool _exiting;

        public TrayApplicationContext(bool demoMode)
        {
            _demoMode = demoMode;
            _storage = new CsvStorage(CsvStorage.GetDefaultDataDirectory(demoMode));
            _config = _storage.LoadConfig();

            bool startPersonal = false;
            if (!demoMode && _config.AskModeOnStartup)
            {
                using (StartupModeDialogForm modeDlg = new StartupModeDialogForm())
                {
                    DialogResult res = modeDlg.ShowDialog();
                    if (res == DialogResult.OK)
                    {
                        startPersonal = modeDlg.SelectedPersonalUse;
                    }
                    else
                    {
                        // Se fechou o diálogo no X, inicia em uso pessoal por segurança
                        startPersonal = true;
                    }
                }
            }

            int regularSec = demoMode ? (_config.Profile == WorkProfile.Estagiario6h ? 45 : 60) : _config.RegularLimitMinutes * 60;
            int overtimeSec = demoMode ? (_config.Profile == WorkProfile.Estagiario6h ? 0 : 30) : _config.OvertimeLimitMinutes * 60;
            int preAlertSec = demoMode ? 15 : _config.PreAlertMinutes * 60;

            _tracker = new TimeTracker(_storage, regularSec, overtimeSec, null, 30);
            _tracker.Initialize(_config.AutoStartCounting, startPersonal);

            _alerts = new AlertManager(_storage, regularSec, overtimeSec, preAlertSec);

            ContextMenuStrip menu = BuildContextMenu();

            _widget = new FloatingWidgetForm(_config, menu, demoMode);
            _widget.PauseToggleRequested += delegate { TogglePause(); };
            _widget.HistoryRequested += delegate { OpenHistory(); };
            _widget.HideRequested += delegate { SetWidgetVisibility(false); };
            _widget.GlobalToggleRequested += delegate { ToggleWidgetVisibility(); };
            _widget.ShowExistingRequested += delegate { ShowAndFocusWidget(); };
            _widget.GeometryChanged += delegate { SaveWidgetGeometry(); };

            _trayIcon = new NotifyIcon();
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Visible = true;
            _trayIcon.MouseClick += OnTrayMouseClick;

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;

            _tickTimer = new Timer();
            _tickTimer.Interval = 1000;
            _tickTimer.Tick += OnTick;
            _tickTimer.Start();

            UpdateUiAndAlerts();

            if (_config.WidgetVisible)
            {
                _widget.Show();
            }
            else if (_config.ShowInTaskbar)
            {
                _widget.WindowState = FormWindowState.Minimized;
                _widget.Show();
            }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            _menuShowHide = new ToolStripMenuItem("Minimizar / Ocultar Relógio (Ctrl+Alt+R)");
            _menuShowHide.Click += delegate { ToggleWidgetVisibility(); };
            menu.Items.Add(_menuShowHide);

            _menuPauseResume = new ToolStripMenuItem("Pausar Contagem");
            _menuPauseResume.Click += delegate { TogglePause(); };
            menu.Items.Add(_menuPauseResume);

            ToolStripMenuItem menuPauseAs = new ToolStripMenuItem("Pausar como Ausência");
            ToolStripMenuItem absIntervalo = new ToolStripMenuItem("Intervalo / Almoço");
            absIntervalo.Click += delegate { StartTypedAbsence(AbsenceTypes.Intervalo, string.Empty); };
            ToolStripMenuItem absMedico = new ToolStripMenuItem("Ida ao Médico (Abona BH)");
            absMedico.Click += delegate { StartTypedAbsence(AbsenceTypes.Medico, string.Empty); };
            ToolStripMenuItem absRapida = new ToolStripMenuItem("Pausa Rápida");
            absRapida.Click += delegate { StartTypedAbsence(AbsenceTypes.PausaRapida, string.Empty); };
            ToolStripMenuItem absOutra = new ToolStripMenuItem("Outra Ausência...");
            absOutra.Click += delegate { StartCustomAbsenceDialog(); };
            menuPauseAs.DropDownItems.Add(absIntervalo);
            menuPauseAs.DropDownItems.Add(absMedico);
            menuPauseAs.DropDownItems.Add(absRapida);
            menuPauseAs.DropDownItems.Add(absOutra);
            menu.Items.Add(menuPauseAs);

            _menuMeetingMode = new ToolStripMenuItem("Modo Reunião (não pausar no bloqueio)");
            _menuMeetingMode.Checked = false;
            _menuMeetingMode.Click += delegate { ToggleMeetingMode(); };
            menu.Items.Add(_menuMeetingMode);

            _menuPersonalMode = new ToolStripMenuItem("Modo Uso Pessoal / Hobby (não contabilizar)");
            _menuPersonalMode.Checked = _tracker.PersonalUseMode;
            _menuPersonalMode.Click += delegate { TogglePersonalMode(); };
            menu.Items.Add(_menuPersonalMode);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem menuProfile = new ToolStripMenuItem("Jornada de Trabalho");
            _menuProfileClt = new ToolStripMenuItem("Padrão — 8h diárias (+2h extras e BH)");
            _menuProfileClt.Click += delegate { SwitchProfile(WorkProfile.Clt8h); };
            _menuProfileEstagiario = new ToolStripMenuItem("Estagiário — 6h diárias (sem extras)");
            _menuProfileEstagiario.Click += delegate { SwitchProfile(WorkProfile.Estagiario6h); };
            menuProfile.DropDownItems.Add(_menuProfileClt);
            menuProfile.DropDownItems.Add(_menuProfileEstagiario);
            menu.Items.Add(menuProfile);

            ToolStripMenuItem menuSize = new ToolStripMenuItem("Tamanho da Janela");
            ToolStripMenuItem sizeCompact = new ToolStripMenuItem("Compacto (165 x 50)");
            sizeCompact.Click += delegate { _widget.ApplyPresetSize(165, 50); };
            ToolStripMenuItem sizeDefault = new ToolStripMenuItem("Padrão (240 x 74)");
            sizeDefault.Click += delegate { _widget.ApplyPresetSize(240, 74); };
            ToolStripMenuItem sizeLarge = new ToolStripMenuItem("Grande (290 x 96)");
            sizeLarge.Click += delegate { _widget.ApplyPresetSize(290, 96); };
            menuSize.DropDownItems.Add(sizeCompact);
            menuSize.DropDownItems.Add(sizeDefault);
            menuSize.DropDownItems.Add(sizeLarge);
            menu.Items.Add(menuSize);

            ToolStripMenuItem menuAnchor = new ToolStripMenuItem("Reancorar no Canto Inferior Direito");
            menuAnchor.Click += delegate
            {
                _widget.AnchorBottomRight();
                SetWidgetVisibility(true);
            };
            menu.Items.Add(menuAnchor);

            _menuAlwaysOnTop = new ToolStripMenuItem("Sempre Visível no Topo");
            _menuAlwaysOnTop.Checked = _config.AlwaysOnTop;
            _menuAlwaysOnTop.Click += delegate
            {
                _config.AlwaysOnTop = !_config.AlwaysOnTop;
                _widget.TopMost = _config.AlwaysOnTop;
                _menuAlwaysOnTop.Checked = _config.AlwaysOnTop;
                _storage.SaveConfig(_config);
            };
            menu.Items.Add(_menuAlwaysOnTop);

            _menuShowInTaskbar = new ToolStripMenuItem("Manter na Barra de Tarefas");
            _menuShowInTaskbar.Checked = _config.ShowInTaskbar;
            _menuShowInTaskbar.Click += delegate
            {
                _config.ShowInTaskbar = !_config.ShowInTaskbar;
                _widget.ShowInTaskbar = _config.ShowInTaskbar;
                _menuShowInTaskbar.Checked = _config.ShowInTaskbar;
                if (_config.ShowInTaskbar && !_widget.Visible)
                {
                    _widget.WindowState = FormWindowState.Minimized;
                    _widget.Show();
                }
                else if (!_config.ShowInTaskbar && _widget.WindowState == FormWindowState.Minimized)
                {
                    _widget.Hide();
                }
                _storage.SaveConfig(_config);
            };
            menu.Items.Add(_menuShowInTaskbar);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem menuHistory = new ToolStripMenuItem("Ver Histórico, Ausências e Banco de Horas...");
            menuHistory.Click += delegate { OpenHistory(); };
            menu.Items.Add(menuHistory);

            ToolStripMenuItem menuAddAbsence = new ToolStripMenuItem("Lançar Ausência Manual...");
            menuAddAbsence.Click += delegate { OpenManualAbsenceDialog(); };
            menu.Items.Add(menuAddAbsence);

            ToolStripMenuItem menuFolder = new ToolStripMenuItem("Abrir Pasta dos Registros CSV");
            menuFolder.Click += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_storage.BaseDirectory) { UseShellExecute = true });
                }
                catch { }
            };
            menu.Items.Add(menuFolder);

            ToolStripMenuItem menuResetToday = new ToolStripMenuItem("Zerar Contagem de Hoje...");
            menuResetToday.Click += delegate { ConfirmResetToday(); };
            menu.Items.Add(menuResetToday);

            menu.Items.Add(new ToolStripSeparator());

            _menuAskModeOnStartup = new ToolStripMenuItem("Perguntar Modo ao Abrir (Trabalho / Pessoal)");
            _menuAskModeOnStartup.Checked = _config.AskModeOnStartup;
            _menuAskModeOnStartup.Click += delegate
            {
                _config.AskModeOnStartup = !_config.AskModeOnStartup;
                _menuAskModeOnStartup.Checked = _config.AskModeOnStartup;
                _storage.SaveConfig(_config);
            };
            menu.Items.Add(_menuAskModeOnStartup);

            _menuAutoStartCounting = new ToolStripMenuItem("Iniciar Contagem ao Abrir (sem pergunta)");
            _menuAutoStartCounting.Checked = _config.AutoStartCounting;
            _menuAutoStartCounting.Click += delegate
            {
                _config.AutoStartCounting = !_config.AutoStartCounting;
                _menuAutoStartCounting.Checked = _config.AutoStartCounting;
                _storage.SaveConfig(_config);
            };
            menu.Items.Add(_menuAutoStartCounting);

            _menuStartWithWindows = new ToolStripMenuItem("Iniciar com o Windows");
            _menuStartWithWindows.Checked = StartupManager.IsEnabled();
            _menuStartWithWindows.Click += delegate
            {
                bool target = !_menuStartWithWindows.Checked;
                if (StartupManager.SetEnabled(target))
                {
                    _menuStartWithWindows.Checked = target;
                    _config.StartWithWindows = target;
                    _storage.SaveConfig(_config);
                }
            };
            menu.Items.Add(_menuStartWithWindows);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem menuExit = new ToolStripMenuItem("Sair");
            menuExit.Click += delegate { ExitApplication("Encerrado"); };
            menu.Items.Add(menuExit);

            UpdateProfileChecks();
            return menu;
        }

        private void StartTypedAbsence(string type, string note)
        {
            if (_tracker.PersonalUseMode)
            {
                _tracker.SetPersonalUseMode(false);
            }
            _tracker.PauseManual(type, note);
            UpdateUiAndAlerts();
        }

        private void StartCustomAbsenceDialog()
        {
            DateTime now = DateTime.Now;
            using (AbsenceDialogForm dlg = new AbsenceDialogForm(
                "Pausar como Ausência",
                AbsenceTypes.Outro,
                now,
                now,
                string.Empty,
                false,
                false,
                true))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (_tracker.PersonalUseMode)
                    {
                        _tracker.SetPersonalUseMode(false);
                    }
                    _tracker.PauseManual(dlg.SelectedType, dlg.EnteredNote);
                    UpdateUiAndAlerts();
                }
            }
        }

        private void OpenManualAbsenceDialog()
        {
            DateTime now = DateTime.Now;
            DateTime defaultStart = now.AddHours(-1);
            if (defaultStart.Date != now.Date)
            {
                defaultStart = DateTime.Today;
            }

            using (AbsenceDialogForm dlg = new AbsenceDialogForm(
                "Lançar Ausência / Intervalo",
                AbsenceTypes.Medico,
                defaultStart,
                now,
                string.Empty,
                true,
                false,
                false))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _tracker.AddManualAbsence(dlg.SelectedStart, dlg.SelectedEnd, dlg.SelectedType, dlg.EnteredNote, dlg.SelectedCredited, true);
                    UpdateUiAndAlerts();
                }
            }
        }

        private void ToggleMeetingMode()
        {
            if (_tracker.PersonalUseMode)
            {
                _tracker.SetPersonalUseMode(false);
            }
            bool next = !_tracker.InMeetingMode;
            _tracker.SetMeetingMode(next);
            _menuMeetingMode.Checked = next;
            UpdateUiAndAlerts();
        }

        private void TogglePersonalMode()
        {
            _tracker.SetPersonalUseMode(!_tracker.PersonalUseMode);
            UpdateUiAndAlerts();
        }

        private void SwitchProfile(WorkProfile profile)
        {
            _config.ApplyProfile(profile);
            _storage.SaveConfig(_config);

            int regularSec = _demoMode ? (profile == WorkProfile.Estagiario6h ? 45 : 60) : _config.RegularLimitMinutes * 60;
            int overtimeSec = _demoMode ? (profile == WorkProfile.Estagiario6h ? 0 : 30) : _config.OvertimeLimitMinutes * 60;
            int preAlertSec = _demoMode ? 15 : _config.PreAlertMinutes * 60;

            _tracker.UpdateLimits(regularSec, overtimeSec);
            _alerts.UpdateLimits(regularSec, overtimeSec, preAlertSec);
            _alerts.ResetCache();

            UpdateProfileChecks();
            UpdateUiAndAlerts();
        }

        private void UpdateProfileChecks()
        {
            _menuProfileClt.Checked = (_config.Profile == WorkProfile.Clt8h);
            _menuProfileEstagiario.Checked = (_config.Profile == WorkProfile.Estagiario6h);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleWidgetVisibility();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_exiting) return;
            _tracker.Tick();
            UpdateUiAndAlerts();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            RunOnUiThread(delegate
            {
                if (_exiting) return;

                switch (e.Reason)
                {
                    case SessionSwitchReason.SessionLock:
                        _tracker.PauseBySession("BloqueioTela");
                        break;
                    case SessionSwitchReason.SessionLogoff:
                    case SessionSwitchReason.ConsoleDisconnect:
                    case SessionSwitchReason.RemoteDisconnect:
                        _tracker.PauseBySession("Logoff");
                        break;
                    case SessionSwitchReason.SessionUnlock:
                    case SessionSwitchReason.SessionLogon:
                    case SessionSwitchReason.ConsoleConnect:
                    case SessionSwitchReason.RemoteConnect:
                        _tracker.ResumeFromSession();
                        break;
                }

                UpdateUiAndAlerts();
            });
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            RunOnUiThread(delegate
            {
                if (_exiting) return;

                if (e.Mode == PowerModes.Suspend)
                {
                    _tracker.PauseBySession("Suspensao");
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    _tracker.ResumeFromSession();
                }

                UpdateUiAndAlerts();
            });
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (_widget != null && !_widget.IsDisposed && _widget.IsHandleCreated && _widget.InvokeRequired)
            {
                try
                {
                    _widget.Invoke((Action)delegate { ExitApplication("Desligamento"); });
                }
                catch
                {
                    ExitApplication("Desligamento");
                }
            }
            else
            {
                ExitApplication("Desligamento");
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (_widget != null && !_widget.IsDisposed && _widget.IsHandleCreated && _widget.InvokeRequired)
            {
                try
                {
                    _widget.BeginInvoke(action);
                }
                catch { }
            }
            else
            {
                action();
            }
        }

        private void TogglePause()
        {
            _tracker.ToggleManualPause();
            UpdateUiAndAlerts();
        }

        private void ConfirmResetToday()
        {
            DialogResult res = MessageBox.Show(
                "Deseja zerar a contagem e remover os registros e ausências de hoje (" + _tracker.CurrentDate + ")?",
                "Relógio Acompanhamento",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                _tracker.ResetToday();
                _alerts.ResetCache();
                UpdateUiAndAlerts();
            }
        }

        private bool IsWidgetShown()
        {
            return _widget.Visible && _widget.WindowState != FormWindowState.Minimized;
        }

        private void ToggleWidgetVisibility()
        {
            SetWidgetVisibility(!IsWidgetShown());
        }

        private void ShowAndFocusWidget()
        {
            SetWidgetVisibility(true);
            _widget.BringToFront();
            _widget.Activate();
        }

        private void SetWidgetVisibility(bool visible)
        {
            if (visible)
            {
                if (!_widget.Visible)
                {
                    _widget.Show();
                }
                if (_widget.WindowState == FormWindowState.Minimized)
                {
                    _widget.WindowState = FormWindowState.Normal;
                }
                _widget.BringToFront();
                _widget.Activate();
            }
            else
            {
                if (_config.ShowInTaskbar)
                {
                    if (!_widget.Visible)
                    {
                        _widget.Show();
                    }
                    _widget.WindowState = FormWindowState.Minimized;
                }
                else if (_widget.Visible)
                {
                    _widget.Hide();
                }
            }

            _config.WidgetVisible = visible;
            _storage.SaveConfig(_config);
            UpdateMenuLabels();
        }

        private void SaveWidgetGeometry()
        {
            if (_widget.WindowState != FormWindowState.Normal) return;
            _config.WidgetWidth = _widget.Width;
            _config.WidgetHeight = _widget.Height;
            _config.WidgetX = _widget.Left;
            _config.WidgetY = _widget.Top;
            _storage.SaveConfig(_config);
        }

        private void OpenHistory()
        {
            if (_historyForm == null || _historyForm.IsDisposed)
            {
                _historyForm = new HistoryForm(_tracker, _storage, _config);
                _historyForm.DataModified += delegate { UpdateUiAndAlerts(); };
                if (_currentTrayIcon != null)
                {
                    _historyForm.Icon = _currentTrayIcon;
                }
            }
            _historyForm.RefreshData();
            if (!_historyForm.Visible)
            {
                _historyForm.Show();
            }
            _historyForm.BringToFront();
            _historyForm.Activate();
        }

        private void UpdateUiAndAlerts()
        {
            int totalSec = _tracker.GetTodayTotalSeconds();
            AlertLevel level = _alerts.GetAlertLevel(totalSec);

            _widget.UpdateDisplay(
                totalSec,
                _tracker.RegularLimitSeconds,
                _tracker.OvertimeLimitSeconds,
                _tracker.State,
                level,
                _tracker.InMeetingMode,
                _tracker.PersonalUseMode,
                _tracker.ActiveAbsenceType);

            if (_tracker.State == TrackerState.Working && !_tracker.PersonalUseMode)
            {
                AlertNotification alert = _alerts.Evaluate(_tracker.CurrentDate, totalSec);
                if (alert != null)
                {
                    ShowAlert(alert);
                }
            }

            UpdateTrayStatus(totalSec, level);
            UpdateMenuLabels();

            if (_historyForm != null && !_historyForm.IsDisposed && _historyForm.Visible)
            {
                _historyForm.RefreshData();
            }
        }

        private void ShowAlert(AlertNotification alert)
        {
            ToolTipIcon icon = alert.IsCritical ? ToolTipIcon.Error : ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(8000, alert.Title, alert.Message, icon);

            if (alert.IsCritical)
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
        }

        private void UpdateTrayStatus(int totalSec, AlertLevel level)
        {
            int limitHours = Math.Max(1, _tracker.TotalLimitSeconds / 3600);
            string stateLabel;
            if (_tracker.PersonalUseMode) stateLabel = "Uso pessoal";
            else if (_tracker.State == TrackerState.PausedManual)
            {
                stateLabel = !string.IsNullOrEmpty(_tracker.ActiveAbsenceType)
                    ? AbsenceTypes.GetShortWidgetLabel(_tracker.ActiveAbsenceType)
                    : "Pausado";
            }
            else if (_tracker.State == TrackerState.PausedSession) stateLabel = "Congelado";
            else if (level == AlertLevel.LimitReached) stateLabel = string.Format("LIMITE {0}h", limitHours);
            else if (level == AlertLevel.Overtime || level == AlertLevel.PreOvertimeLimit) stateLabel = "Hora Extra";
            else if (_tracker.InMeetingMode) stateLabel = "Em reunião";
            else stateLabel = "Ativo";

            string tip = string.Format("Relógio: {0} ({1})", TimeFormat.FormatHms(totalSec), stateLabel);
            if (_demoMode)
            {
                tip = "[DEMO] " + tip;
            }
            if (tip.Length > 63)
            {
                tip = tip.Substring(0, 63);
            }
            _trayIcon.Text = tip;

            bool isPaused = _tracker.State != TrackerState.Working || _tracker.PersonalUseMode;
            string iconKey = _tracker.PersonalUseMode
                ? "personal"
                : (isPaused ? "paused" : (_tracker.InMeetingMode && level == AlertLevel.Normal ? "meeting" : level.ToString()));

            if (iconKey != _lastIconKey)
            {
                _lastIconKey = iconKey;
                Color c = GetIconColor(isPaused, level, _tracker.InMeetingMode, _tracker.PersonalUseMode);
                Icon nextIcon = IconFactory.CreateClockIcon(c, isPaused);
                Icon oldIcon = _currentTrayIcon;
                _currentTrayIcon = nextIcon;
                _trayIcon.Icon = nextIcon;
                _widget.Icon = nextIcon;
                if (oldIcon != null)
                {
                    oldIcon.Dispose();
                }
            }
        }

        private static Color GetIconColor(bool isPaused, AlertLevel level, bool inMeetingMode, bool personalMode)
        {
            if (personalMode) return Color.FromArgb(129, 140, 248);
            if (isPaused) return Color.FromArgb(148, 163, 184);
            switch (level)
            {
                case AlertLevel.PreRegular: return Color.FromArgb(234, 179, 8);
                case AlertLevel.Overtime: return Color.FromArgb(249, 115, 22);
                case AlertLevel.PreOvertimeLimit: return Color.FromArgb(234, 88, 12);
                case AlertLevel.LimitReached: return Color.FromArgb(239, 68, 68);
                default: return inMeetingMode ? Color.FromArgb(56, 189, 248) : Color.FromArgb(34, 197, 94);
            }
        }

        private void UpdateMenuLabels()
        {
            _menuShowHide.Text = IsWidgetShown()
                ? "Minimizar / Ocultar Relógio (Ctrl+Alt+R)"
                : "Mostrar Relógio (Ctrl+Alt+R)";

            if (_tracker.PersonalUseMode)
            {
                _menuPauseResume.Text = "Iniciar Trabalho";
            }
            else
            {
                _menuPauseResume.Text = _tracker.State == TrackerState.Working
                    ? "Pausar Contagem"
                    : "Retomar Contagem";
            }

            _menuMeetingMode.Checked = _tracker.InMeetingMode;
            _menuPersonalMode.Checked = _tracker.PersonalUseMode;
        }

        private void ExitApplication(string reason)
        {
            if (_exiting) return;
            _exiting = true;

            _tickTimer.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;

            // Ao encerrar manualmente um dia útil em jornada 8h com trabalho registrado, sinaliza o saldo do dia
            if (reason == "Encerrado" && _config.Profile == WorkProfile.Clt8h && !_tracker.PersonalUseMode && HourBankCalculator.IsWeekday(DateTime.Today))
            {
                int worked = _tracker.GetTodayTotalSeconds();
                int credited = _tracker.GetTodayCreditedAbsenceSeconds();
                if (worked >= 60 || credited >= 60)
                {
                    int delta = HourBankCalculator.CalculateDailyDeltaSeconds(worked, credited, HourBankCalculator.DailyTargetSeconds, HourBankCalculator.DailyToleranceSeconds);
                    string summary = string.Format(
                        "Hoje: {0} trabalhadas{1}. Saldo BH do dia: {2}",
                        TimeFormat.FormatHms(worked),
                        credited > 0 ? " + " + TimeFormat.FormatHms(credited) + " abonadas" : string.Empty,
                        TimeFormat.FormatSignedHms(delta));
                    try
                    {
                        _trayIcon.ShowBalloonTip(5000, "Resumo do Dia — Banco de Horas", summary, ToolTipIcon.Info);
                    }
                    catch { }
                }
            }

            _tracker.Shutdown(reason);
            SaveWidgetGeometry();

            _trayIcon.Visible = false;
            if (_historyForm != null && !_historyForm.IsDisposed)
            {
                _historyForm.Close();
                _historyForm.Dispose();
            }
            if (_widget != null && !_widget.IsDisposed)
            {
                _widget.Dispose();
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tickTimer.Dispose();
                _trayIcon.Dispose();
                if (_currentTrayIcon != null)
                {
                    _currentTrayIcon.Dispose();
                    _currentTrayIcon = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
