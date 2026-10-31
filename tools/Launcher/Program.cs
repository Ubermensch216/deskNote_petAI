using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DeskNote.Launcher
{
    /// <summary>
    /// Unpacks the app this exe carries, the first time, and starts it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app lives in a fixed per-user folder rather than in a temporary one, for two reasons the
    /// rest of the product depends on. Starting with Windows writes the path of the running exe into
    /// the registry, and a path that changes with every update would break it. And an app that is
    /// unpacked afresh on every start would take seconds to open, where a folder that is already
    /// there opens at once: every run after the first is a file check and a process start.
    /// </para>
    /// <para>
    /// Updating is running a newer copy of this exe. The folder is replaced as a whole, from a
    /// sibling that has been completely written first, so a power cut or a full disk leaves the
    /// version that was there before and not half of each.
    /// </para>
    /// </remarks>
    internal static class Program
    {
        private const string AppExecutable = "DeskNote.exe";
        private const string AiInstaller = "install-ai.bat";
        private const string Marker = ".payload";
        private const string InstallDirectoryVariable = "DESKNOTE_INSTALL_DIR";

        [STAThread]
        private static int Main(string[] arguments)
        {
            try
            {
                return Run(arguments);
            }
            catch (Exception ex)
            {
                Ui.Fail(ex);
                return 1;
            }
        }

        private static int Run(string[] arguments)
        {
            var installAi = arguments.Any(a => string.Equals(a, "--install-ai", StringComparison.OrdinalIgnoreCase));
            var forwarded = arguments.Where(a => !string.Equals(a, "--install-ai", StringComparison.OrdinalIgnoreCase)).ToArray();

            var payload = Payload.Open();
            if (payload == null)
            {
                Ui.Fail(new InvalidOperationException(Ui.Text("no-payload")));
                return 1;
            }

            var installDirectory = InstallDirectory();

            // Two copies started together must not unpack on top of one another: the second waits
            // for the first and then finds the work done.
            using (var gate = new Mutex(false, "Local\\DeskNote.Launcher." + Payload.Hash(installDirectory)))
            {
                var held = false;
                try
                {
                    try
                    {
                        held = gate.WaitOne(TimeSpan.FromMinutes(3));
                    }
                    catch (AbandonedMutexException)
                    {
                        held = true;
                    }

                    if (!IsInstalled(installDirectory, payload.Id))
                    {
                        Ui.Install(() => Install(payload, installDirectory), Ui.Text("preparing"));
                    }
                }
                finally
                {
                    if (held)
                    {
                        gate.ReleaseMutex();
                    }

                    payload.Dispose();
                }
            }

            if (installAi)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(installDirectory, AiInstaller))
                {
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true,
                });
                return 0;
            }

            Process.Start(new ProcessStartInfo(Path.Combine(installDirectory, AppExecutable))
            {
                WorkingDirectory = installDirectory,
                UseShellExecute = false,
                Arguments = string.Join(" ", forwarded.Select(Quote)),
            });
            return 0;
        }

        /// <summary>Where the app goes: the usual per-user programs folder, or wherever a test points it.</summary>
        private static string InstallDirectory()
        {
            var chosen = Environment.GetEnvironmentVariable(InstallDirectoryVariable);
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                return Path.GetFullPath(chosen);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "DeskNote");
        }

        private static bool IsInstalled(string directory, string payloadId)
        {
            try
            {
                return File.Exists(Path.Combine(directory, AppExecutable))
                    && File.ReadAllText(Path.Combine(directory, Marker)).Trim() == payloadId;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void Install(Payload payload, string directory)
        {
            var parent = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
            Directory.CreateDirectory(parent);

            // What an earlier, interrupted update left behind.
            foreach (var leftover in Directory.GetDirectories(parent, name + ".*"))
            {
                TryDelete(leftover);
            }

            StopRunningCopies(directory);

            var staging = Path.Combine(parent, name + ".new-" + Guid.NewGuid().ToString("N"));
            try
            {
                payload.ExtractTo(staging, Ui.Report);
                File.WriteAllText(Path.Combine(staging, Marker), payload.Id);

                var retired = Path.Combine(parent, name + ".old-" + Guid.NewGuid().ToString("N"));
                var hadPrevious = Directory.Exists(directory);
                if (hadPrevious)
                {
                    Retry(() => Directory.Move(directory, retired));
                }

                Directory.Move(staging, directory);
                if (hadPrevious)
                {
                    TryDelete(retired);
                }
            }
            catch
            {
                TryDelete(staging);
                throw;
            }
        }

        /// <summary>
        /// Closes the copy of the app that is running from the folder about to be replaced: its files
        /// are open, and Windows will not move a folder with open files in it.
        /// </summary>
        private static void StopRunningCopies(string directory)
        {
            // Compared as long paths: a process reports its exe by its full name, and the folder may
            // have been given in its 8.3 form (a test, a shortcut, a user name that is long).
            var prefix = LongPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExecutable)))
            {
                try
                {
                    var path = process.MainModule == null ? null : process.MainModule.FileName;
                    if (path == null || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception)
                {
                    // A process that is gone, or that belongs to somebody else, is not in the way.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static string LongPath(string path)
        {
            var buffer = new StringBuilder(1024);
            var length = GetLongPathName(path, buffer, buffer.Capacity);
            return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferSize);

        private static void Retry(Action action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 15)
                    {
                        throw;
                    }

                    Thread.Sleep(300);
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt >= 15)
                    {
                        throw;
                    }

                    Thread.Sleep(300);
                }
            }
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Cleaned up the next time; never worth failing a start over.
            }
        }

        private static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return argument;
            }

            var quoted = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    quoted.Append('\\', (backslashes * 2) + 1).Append('"');
                }
                else
                {
                    quoted.Append('\\', backslashes).Append(c);
                }

                backslashes = 0;
            }

            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }

    /// <summary>The app, as a zip inside this exe.</summary>
    internal sealed class Payload : IDisposable
    {
        private readonly Stream _stream;

        private Payload(string id, Stream stream)
        {
            Id = id;
            _stream = stream;
        }

        /// <summary>Names this build of the app, so a different one is told from the one on disk.</summary>
        public string Id { get; }

        public static Payload Open()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream("payload.zip");
            var id = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "PayloadId")
                .Select(a => a.Value)
                .FirstOrDefault();

            return stream == null || string.IsNullOrEmpty(id) ? null : new Payload(id, stream);
        }

        public static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant())), 0, 8)
                    .Replace("-", string.Empty);
            }
        }

        public void ExtractTo(string directory, Action<double> progress)
        {
            Directory.CreateDirectory(directory);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            using (var archive = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                var total = Math.Max(1L, entries.Sum(e => e.Length));
                long done = 0;

                foreach (var entry in entries)
                {
                    var target = Path.GetFullPath(Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

                    // An entry named ..\..\something must not be able to write outside the folder.
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(entry.FullName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    done += entry.Length;
                    progress(done / (double)total);
                }
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
