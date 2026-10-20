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
    /// Starts EditCompileReload's reload from a polling thread, because ECR's own file watcher
    /// never fires here: under Unity's Mono on macOS a FileSystemWatcher registers and then
    /// delivers no events.
    ///
    /// <para>The poller watches the mtime of <c>Assemblies/ShiftChange.dll_orig</c>, the file a
    /// Debug build writes and the one ECR's watcher targets, since it is both trigger and
    /// payload. The reload is ECR's private <c>ProcessAssembly</c>, called by reflection. It is
    /// pure CLR work that rolls itself back on an exception, and ECR already runs it on a
    /// background thread, so a single poller thread keeps its threading assumptions.</para>
    /// </summary>
    internal static class EcrWatchdog
    {
        /// <summary>
        /// Milliseconds between checks, and so the settle time a change must survive before it
        /// fires. Keep it well above the ~150 ms the build takes to stream the file.
        /// </summary>
        internal const int PollMs = 500;

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

        /// <summary>Runs on the watchdog thread for the life of the game.</summary>
        internal static void Poll(string path, MethodInfo process)
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
                        // Any non-Debug build (Release, Media, the harness runs) deletes the
                        // file. Keep polling: when a Debug build writes it again, it is handled
                        // like any other change.
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
                        // First sighting of a new write: wait one more poll, because firing
                        // mid-write would hand ECR's AsmResolver a torn image.
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
