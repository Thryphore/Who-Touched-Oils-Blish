using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeimosOilSummary.Models;

namespace DeimosOilSummary.Services {

    /// <summary>
    /// Watches ArcDPS combat log folders for new Deimos .zevtc/.evtc files and parses oil hits.
    /// </summary>
    public sealed class LogWatcher : IDisposable {

        private static readonly string[] LogExtensions = { ".zevtc", ".evtc" };

        private readonly ConcurrentDictionary<string, byte> _seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher[] _watchers = Array.Empty<FileSystemWatcher>();
        private Timer _pollTimer;
        private int _parseGate;
        private bool _disposed;

        public event EventHandler<OilHitSummary> SummaryReady;
        public event EventHandler<string>        StatusChanged;

        public string[] WatchedPaths { get; private set; } = Array.Empty<string>();

        public void Start() {
            WatchedPaths = DiscoverLogRoots();
            if (WatchedPaths.Length == 0) {
                RaiseStatus("No ArcDPS log folder found. Expected …/addons/arcdps/arcdps.cbtlogs");
                return;
            }

            RaiseStatus("Watching: " + string.Join("; ", WatchedPaths));

            _watchers = WatchedPaths.Select(CreateWatcher).Where(w => w != null).ToArray();

            // Seed seen set with existing files so we only report new pulls after enable
            foreach (string root in WatchedPaths) {
                foreach (string file in EnumerateLogFiles(root)) {
                    _seen.TryAdd(file, 0);
                }
            }

            _pollTimer = new Timer(_ => PollForNewLogs(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public OilHitSummary ParseLatestDeimos() {
            string latest = WatchedPaths
                .SelectMany(EnumerateLogFiles)
                .Where(IsDeimosPath)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null) {
                return new OilHitSummary {
                    Success      = false,
                    ErrorMessage = "No Deimos logs found yet."
                };
            }

            return EvtcOilParser.Parse(latest);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _pollTimer?.Dispose();
            _pollTimer = null;

            foreach (FileSystemWatcher watcher in _watchers) {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers = Array.Empty<FileSystemWatcher>();
        }

        private FileSystemWatcher CreateWatcher(string root) {
            try {
                if (!Directory.Exists(root)) {
                    return null;
                }

                var watcher = new FileSystemWatcher(root) {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024
                };

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Renamed += (s, e) => QueueFile(e.FullPath);
                watcher.EnableRaisingEvents = true;
                return watcher;
            } catch (Exception) {
                return null;
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e) {
            QueueFile(e.FullPath);
        }

        private void QueueFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            string ext = Path.GetExtension(path);
            if (!LogExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            if (!IsDeimosPath(path)) {
                return;
            }

            // ArcDPS may still be writing; wait then parse
            Task.Run(async () => {
                await Task.Delay(2500).ConfigureAwait(false);
                if (!WaitUntilStable(path, TimeSpan.FromSeconds(20))) {
                    return;
                }

                if (!_seen.TryAdd(path, 0)) {
                    return;
                }

                ParseAndRaise(path);
            });
        }

        private void PollForNewLogs() {
            if (_disposed) {
                return;
            }

            foreach (string root in WatchedPaths) {
                foreach (string file in EnumerateLogFiles(root)) {
                    if (!IsDeimosPath(file)) {
                        continue;
                    }

                    if (!_seen.TryAdd(file, 0)) {
                        continue;
                    }

                    if (!WaitUntilStable(file, TimeSpan.FromSeconds(5))) {
                        _seen.TryRemove(file, out _);
                        continue;
                    }

                    ParseAndRaise(file);
                }
            }
        }

        private void ParseAndRaise(string path) {
            if (Interlocked.CompareExchange(ref _parseGate, 1, 0) != 0) {
                // Serialize parses; re-queue via poll if busy
                _seen.TryRemove(path, out _);
                return;
            }

            try {
                RaiseStatus("Parsing " + Path.GetFileName(path) + "…");
                OilHitSummary summary = EvtcOilParser.Parse(path);
                if (!summary.Success) {
                    RaiseStatus(summary.ErrorMessage ?? "Parse failed.");
                    return;
                }

                RaiseStatus($"Deimos oil summary ready ({summary.TotalHits} hit(s)).");
                SummaryReady?.Invoke(this, summary);
            } catch (Exception ex) {
                RaiseStatus("Parse error: " + ex.Message);
                _seen.TryRemove(path, out _);
            } finally {
                Interlocked.Exchange(ref _parseGate, 0);
            }
        }

        private void RaiseStatus(string message) {
            StatusChanged?.Invoke(this, message);
        }

        private static bool IsDeimosPath(string path) {
            // ArcDPS folders look like "Deimos (17154)"
            string name = path ?? string.Empty;
            return name.IndexOf("Deimos", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("(17154)", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool WaitUntilStable(string path, TimeSpan timeout) {
            DateTime deadline = DateTime.UtcNow + timeout;
            long lastSize = -1;

            while (DateTime.UtcNow < deadline) {
                try {
                    var info = new FileInfo(path);
                    if (!info.Exists) {
                        Thread.Sleep(250);
                        continue;
                    }

                    long size = info.Length;
                    if (size > 0 && size == lastSize) {
                        // Try exclusive open
                        using (FileStream fs = info.Open(FileMode.Open, FileAccess.Read, FileShare.Read)) {
                            return true;
                        }
                    }

                    lastSize = size;
                } catch (IOException) {
                    // still writing
                }

                Thread.Sleep(400);
            }

            return false;
        }

        private static string[] DiscoverLogRoots() {
            var candidates = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Guild Wars 2", "addons", "arcdps", "arcdps.cbtlogs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Guild Wars 2", "addons", "arcdps.cbtlogs"),
                // OneDrive Documents (common when Documents is redirected)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "OneDrive", "Documents", "Guild Wars 2", "addons", "arcdps", "arcdps.cbtlogs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "OneDrive", "Documents", "Guild Wars 2", "addons", "arcdps.cbtlogs")
            };

            return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IEnumerable<string> EnumerateLogFiles(string root) {
            if (!Directory.Exists(root)) {
                yield break;
            }

            string[] files = Array.Empty<string>();
            try {
                files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            } catch (Exception) {
                yield break;
            }

            foreach (string file in files) {
                string ext = Path.GetExtension(file);
                if (LogExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) {
                    yield return file;
                }
            }
        }

    }

}
