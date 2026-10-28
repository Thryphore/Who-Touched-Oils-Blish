using System;
using System.IO;
using System.Linq;
using DeimosOilSummary.Services;

namespace ParseSmokeTest {

    internal static class Program {
        private static int Main(string[] args) {
            string path = args.Length > 0
                ? args[0]
                : Directory.GetFiles(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            @"OneDrive\Documents\Guild Wars 2\addons\arcdps\arcdps.cbtlogs\Deimos (17154)"),
                        "*.zevtc")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                Console.WriteLine("No Deimos log found.");
                return 1;
            }

            Console.WriteLine("Parsing: " + path);
            var result = EvtcOilParser.Parse(path);
            Console.WriteLine($"Success={result.Success} TotalHits={result.TotalHits} Error={result.ErrorMessage}");
            Console.WriteLine("First touches:");
            foreach (var t in result.FirstTouches) {
                Console.WriteLine($"  Oil {t.OilNumber}: {t.DisplayName}");
            }
            Console.WriteLine("Hit totals:");
            foreach (var p in result.Players) {
                Console.WriteLine($"  {p.DisplayName} = {p.HitCount}");
            }

            return result.Success ? 0 : 2;
        }
    }

}
