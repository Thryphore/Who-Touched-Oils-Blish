using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using DeimosOilSummary.Controls;
using DeimosOilSummary.Models;
using DeimosOilSummary.Services;
using Microsoft.Xna.Framework;

namespace DeimosOilSummary {

    [Export(typeof(Module))]
    public class DeimosOilSummaryModule : Module {

        private static readonly Logger Logger = Logger.GetLogger<DeimosOilSummaryModule>();

        private SettingEntry<bool>  _showOnNewLog;
        private SettingEntry<bool>  _showPanel;
        private SettingEntry<Point> _panelLocation;
        private SettingCollection   _internalSettings;

        private LogWatcher      _watcher;
        private OilSummaryPanel _panel;
        private CornerIcon      _cornerIcon;

        private OilHitSummary _pendingSummary;
        private string        _pendingStatus;
        private readonly object _pendingLock = new object();

        [ImportingConstructor]
        public DeimosOilSummaryModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings) {
            _showOnNewLog = settings.DefineSetting(
                "ShowOnNewLog",
                true,
                () => "Show panel when a new Deimos log is parsed",
                () => "Automatically open the oil summary after each Deimos pull.");

            _showPanel = settings.DefineSetting(
                "ShowPanel",
                true,
                () => "Show oil summary panel",
                () => "Toggle the Deimos oil summary overlay.");

            _showPanel.SettingChanged += OnShowPanelChanged;

            _internalSettings = settings.AddSubCollection("Internal");
            _panelLocation = _internalSettings.DefineSetting("PanelLocation", new Point(40, 240));
        }

        protected override async Task LoadAsync() {
            _panel = new OilSummaryPanel {
                Parent   = GameService.Graphics.SpriteScreen,
                Location = _panelLocation.Value,
                Visible  = _showPanel.Value
            };

            _panel.Moved += (s, e) => { _panelLocation.Value = _panel.Location; };
            _panel.ParseLatestClicked += OnParseLatestClicked;

            _cornerIcon = new CornerIcon {
                Icon             = AsyncTexture2D.FromAssetId(155150),
                BasicTooltipText = "Deimos Oil Summary",
                Priority         = 1847291054,
                Parent           = GameService.Graphics.SpriteScreen
            };

            _cornerIcon.Click += (s, e) => {
                _showPanel.Value = !_showPanel.Value;
            };

            _watcher = new LogWatcher();
            _watcher.SummaryReady  += OnSummaryReady;
            _watcher.StatusChanged += OnStatusChanged;
            _watcher.Start();

            Logger.Info("Deimos Oil Summary loaded. Watching {0} log root(s).", _watcher.WatchedPaths.Length);
            await Task.CompletedTask;
        }

        protected override void Update(GameTime gameTime) {
            OilHitSummary summary = null;
            string status = null;

            lock (_pendingLock) {
                if (_pendingSummary != null) {
                    summary = _pendingSummary;
                    _pendingSummary = null;
                }

                if (_pendingStatus != null) {
                    status = _pendingStatus;
                    _pendingStatus = null;
                }
            }

            if (_panel == null) {
                return;
            }

            if (status != null) {
                _panel.SetStatus(status);
            }

            if (summary != null) {
                _panel.Apply(summary);

                if (_showOnNewLog.Value) {
                    _showPanel.Value = true;
                    _panel.Visible = true;
                }

                string toast = summary.Success && summary.TotalHits == 0
                    ? "Deimos: no oil hits"
                    : summary.Success
                        ? $"Deimos oils: {summary.TotalHits} hit(s) — see panel"
                        : "Deimos oil parse failed";

                ScreenNotification.ShowNotification(
                    toast,
                    summary.Success
                        ? ScreenNotification.NotificationType.Info
                        : ScreenNotification.NotificationType.Warning);
            }
        }

        protected override void Unload() {
            if (_showPanel != null) {
                _showPanel.SettingChanged -= OnShowPanelChanged;
            }

            if (_panel != null) {
                _panel.ParseLatestClicked -= OnParseLatestClicked;
            }

            if (_watcher != null) {
                _watcher.SummaryReady  -= OnSummaryReady;
                _watcher.StatusChanged -= OnStatusChanged;
                _watcher.Dispose();
                _watcher = null;
            }

            _cornerIcon?.Dispose();
            _cornerIcon = null;

            _panel?.Dispose();
            _panel = null;
        }

        private void OnShowPanelChanged(object sender, ValueChangedEventArgs<bool> e) {
            if (_panel != null) {
                _panel.Visible = e.NewValue;
            }
        }

        private void OnStatusChanged(object sender, string status) {
            lock (_pendingLock) {
                _pendingStatus = status;
            }
        }

        private void OnSummaryReady(object sender, OilHitSummary summary) {
            lock (_pendingLock) {
                _pendingSummary = summary;
            }
        }

        private void OnParseLatestClicked(object sender, EventArgs e) {
            if (_watcher == null || _panel == null) {
                return;
            }

            OilHitSummary summary = _watcher.ParseLatestDeimos();
            _panel.Apply(summary);
            _showPanel.Value = true;
            _panel.Visible = true;
        }

    }

}
