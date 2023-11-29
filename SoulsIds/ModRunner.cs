using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SoulsIds
{
    public class ModRunner
    {
        private Process currentProcess;
        private readonly string modengineConfig;
        private readonly string modengineLauncher;

        public ModRunner(string modengineConfig, string modengineLauncher)
        {
            this.modengineConfig = modengineConfig;
            this.modengineLauncher = modengineLauncher;
        }

        public event EventHandler StartRunning;
        public event EventHandler FailedToStart;
        public event EventHandler DoneRunning;

        // See also ReadProfile
        public static bool TryGetDirectory(string path, out string dir)
        {
            dir = null;
            try
            {
                if (Directory.Exists(path))
                {
                    dir = path;
                    return true;
                }
                else
                {
                    string fileDir = Path.GetDirectoryName(path);
                    if (Directory.Exists(fileDir))
                    {
                        dir = fileDir;
                        return true;
                    }
                }
            }
            catch (ArgumentException) { }
            return false;
        }

        public void DeleteLaunchFile()
        {
            if (File.Exists(modengineConfig))
            {
                File.Delete(modengineConfig);
            }
        }

        // TODO: Take a different toml file
        public void CreateLaunchFile(List<string> commentLines, List<string> modDirs = null)
        {
            string formatLine(string name, string path)
            {
                return $"    {{ enabled = true, name = \"{name}\", path = {JsonConvert.ToString(path)} }},";
            }
            List<string> modLines = new List<string>();
            // TODO: Make this relative somehow? And/or able to merge other toml files, with custom names
            modLines.Add(formatLine("randomizer", Directory.GetCurrentDirectory()));
            if (modDirs != null)
            {
                foreach (string modDir in modDirs)
                {
                    DirectoryInfo modInfo = new DirectoryInfo(modDir);
                    if (modInfo.Exists)
                    {
                        modLines.Add(formatLine("mod", modInfo.FullName));
                    }
                }
            }
            string file = $@"# DO NOT MODIFY THIS FILE!
# AUTO-GENERATED
# CONTENTS WILL BE AUTOMATICALLY OVERWRITTEN

{string.Join(Environment.NewLine, commentLines.Select(line => $"# {line}"))}

[modengine]
debug = false
[extension.mod_loader]
enabled = true
loose_params = false
mods = [
{string.Join(Environment.NewLine, modLines)}
]
";
            File.WriteAllText(modengineConfig, file);
        }

        public string ReadLaunchFileText()
        {
            return File.ReadAllText(modengineConfig);
        }

        public bool IsValid() => File.Exists(modengineConfig) && File.Exists(modengineLauncher);

        private static bool? IsMaybeRunning()
        {
            try
            {
                return Process.GetProcessesByName("eldenring").Length > 0;
            }
            catch (Exception) { }
            return null;
        }

        public bool IsEldenRingRunning() => IsMaybeRunning() ?? false;
        public bool IsLaunching() => currentProcess != null;

        public async Task LaunchEldenRing()
        {
            if (currentProcess != null) return;
            using (Process process = new Process())
            {
                process.StartInfo.FileName = modengineLauncher;
                // TODO: Add ..s based on how many are present in the launcher
                process.StartInfo.Arguments = $@"-t er -c ..\..\{modengineConfig}";
                process.StartInfo.WorkingDirectory = Path.GetDirectoryName(modengineLauncher);
                process.StartInfo.UseShellExecute = false;
                process.EnableRaisingEvents = true;

                // Start
                currentProcess = process;
                DateTime startTime = DateTime.Now;
                StartRunning?.Invoke(this, new EventArgs());

                process.Start();
                await WaitForExitAsync(process);

                int timePassed = (int)(DateTime.Now - startTime).TotalMilliseconds;
                // Originally this was a way of "if exited early, warn about broken",
                // but Mod Engine in non-debug mode exits early anyway, so check it regardless.
                if (timePassed < 5000)
                {
                    int waitRest = Math.Max(0, Math.Min(5000 - timePassed, 5000));
                    await Task.Delay(waitRest);
                }
                bool probablyRunning = IsMaybeRunning() ?? true;
                if (!probablyRunning)
                {
                    FailedToStart?.Invoke(this, new EventArgs());
                }

                DoneRunning?.Invoke(this, new EventArgs());
                currentProcess = null;
            }
        }

        // https://stackoverflow.com/questions/470256/process-waitforexit-asynchronously
        private static Task WaitForExitAsync(
            Process process,
            CancellationToken cancellationToken = default)
        {
            if (process.HasExited) return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default)
            {
                cancellationToken.Register(() => tcs.SetCanceled());
            }
            return process.HasExited ? Task.CompletedTask : tcs.Task;
        }
    }
}
