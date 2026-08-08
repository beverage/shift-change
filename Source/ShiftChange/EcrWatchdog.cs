#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using EditCompileReload;
using Verse;

namespace ShiftChange
{
    /// <summary>
    /// Drives ECR's reload pipeline from a polling thread, because Mono's
    /// FileSystemWatcher on macOS registers happily and then never delivers
    /// (convicted 2026-08-08: "Registered file watcher" in Player.log, the
    /// .dll_orig rewritten by a Debug build, no event, no reload).
    ///
    /// The poller watches the .dll_orig's mtime — the same file ECR's own
    /// watcher targets, since that file is both the trigger and the payload —
    /// and fires only after the mtime holds still for one full poll: the
    /// compiler plugin streams the file over ~150ms, and firing mid-write
    /// would feed AsmResolver a torn image. The reload itself is ECR's own
    /// private ProcessAssembly, invoked by reflection: pure CLR work with its
    /// own rollback-on-exception, which ECR already runs on a background
    /// thread by design, so a single poller thread preserves its threading
    /// assumptions trivially.
    ///
    /// Debug builds only, like everything ECR.
    /// </summary>
    internal static class EcrWatchdog
    {
        private const int PollMs = 500;

        public static void Start()
        {
            string root = LoadedModManager.RunningMods
                .FirstOrDefault(m => m.PackageId == "mrbeverage.shiftchange")?.RootDir;
            if (root == null)
            {
                EcrLog.Error("watchdog: could not find own mod root — hot reload disabled");
                return;
            }
            string path = Path.Combine(root, "Assemblies", "ShiftChange.dll_orig");

            MethodInfo process = typeof(Ecr).GetMethod("ProcessAssembly",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (process == null)
            {
                EcrLog.Error("watchdog: Ecr.ProcessAssembly not found (ECR version drift?) — hot reload disabled");
                return;
            }

            Thread thread = new Thread(() => Poll(path, process))
            {
                IsBackground = true,
                Name = "ShiftChange.EcrWatchdog",
            };
            thread.Start();
            EcrLog.Message("watchdog armed on " + path);
        }

        private static void Poll(string path, MethodInfo process)
        {
            DateTime known = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
            DateTime pending = default;
            while (true)
            {
                Thread.Sleep(PollMs);
                try
                {
                    if (!File.Exists(path))
                    {
                        // A Release build swept it mid-session. Keep polling —
                        // the next Debug build recreates it.
                        continue;
                    }
                    DateTime current = File.GetLastWriteTimeUtc(path);
                    if (current == known)
                    {
                        pending = default;
                        continue;
                    }
                    if (current != pending)
                    {
                        // First sighting of a new write — let it settle for a
                        // full poll before trusting the bytes.
                        pending = current;
                        continue;
                    }
                    known = current;
                    pending = default;
                    EcrLog.Message("watchdog: change settled, reloading");
                    process.Invoke(null, new object[] { "ShiftChange", path });
                }
                catch (Exception e)
                {
                    EcrLog.Error("watchdog: " + e);
                }
            }
        }
    }
}
#endif
