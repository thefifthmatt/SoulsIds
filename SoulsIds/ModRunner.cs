using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SoulsIds
{
    public class ModRunner
    {
        private Process currentProcess;
        private readonly string modengineConfig;
        private readonly string modengineLauncher;
        private readonly string modengineDllConfig;

        public ModRunner(string modengineConfig, string modengineLauncher, string modengineDllConfig = null)
        {
            this.modengineConfig = modengineConfig;
            this.modengineLauncher = modengineLauncher;
            this.modengineDllConfig = modengineDllConfig;
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

        private static string FormatModLine(string name, string path)
        {
            return $"    {{ enabled = true, name = \"{name}\", path = {JsonConvert.ToString(path)} }},";
        }

        public void CreateLaunchFile(List<string> commentLines, MergedMods mods = null, List<string> extraDlls = null)
        {
            List<string> modLines = new List<string>();
            List<string> dllLines = new List<string>();
            modLines.Add(FormatModLine("randomizer", Directory.GetCurrentDirectory()));
            if (extraDlls != null)
            {
                foreach (string path in extraDlls)
                {
                    FileInfo modInfo = new FileInfo(path);
                    if (modInfo.Exists)
                    {
                        dllLines.Add($"    {JsonConvert.ToString(modInfo.FullName)},");
                    }
                }
            }
            if (mods != null)
            {
                foreach (string modDir in mods.Dirs)
                {
                    DirectoryInfo modInfo = new DirectoryInfo(modDir);
                    if (modInfo.Exists)
                    {
                        modLines.Add(FormatModLine("mod", modInfo.FullName));
                    }
                }
                foreach (string path in mods.ExternalDlls)
                {
                    FileInfo modInfo = new FileInfo(path);
                    if (modInfo.Exists)
                    {
                        dllLines.Add($"    {JsonConvert.ToString(modInfo.FullName)},");
                    }
                }
            }
            string file = $@"# DO NOT MODIFY THIS FILE!
# AUTO-GENERATED
# CONTENTS WILL BE AUTOMATICALLY OVERWRITTEN

{string.Join(Environment.NewLine, commentLines.Select(line => $"# {line}"))}

[modengine]
debug = false
external_dlls = [
{string.Join(Environment.NewLine, dllLines)}
]
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

        private string MakeLaunchFileForZip(MergedMods mods)
        {
            List<string> modLines = new List<string>();
            List<string> dllLines = new List<string>();
            DirectoryInfo currentDir = new DirectoryInfo(".");
            modLines.Add(FormatModLine("randomizer", currentDir.Name));
            if (mods != null)
            {
                // Add relative paths directly
                foreach (string modDir in mods.Dirs)
                {
                    modLines.Add(FormatModLine("mod", modDir));
                }
                foreach (string path in mods.ExternalDlls)
                {
                    dllLines.Add($"    {JsonConvert.ToString(path)},");
                }
            }
            return $@"[modengine]
debug = false
external_dlls = [
{string.Join(Environment.NewLine, dllLines)}
]
[extension.mod_loader]
enabled = true
loose_params = false
mods = [
{string.Join(Environment.NewLine, modLines)}
]
";
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

        public async Task LaunchEldenRing(List<string> extraDlls = null)
        {
            if (currentProcess != null) return;
            string launchFile = modengineConfig;
            if (extraDlls != null && extraDlls.Count > 0 && File.Exists(modengineConfig))
            {
                if (modengineDllConfig == null) throw new Exception($"Internal error: extra dlls given but no dll config name is defined");
                MergedMods.AddExternalDlls(modengineConfig, modengineDllConfig, extraDlls);
                launchFile = modengineDllConfig;
            }
            using (Process process = new Process())
            {
                process.StartInfo.FileName = modengineLauncher;
                // TODO: Add ..s based on how many are present in the launcher
                process.StartInfo.Arguments = $@"-t er -c ..\..\{launchFile}";
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

        // Maybe support more in the future, but be cautious about bad distributed exes here
        private static readonly HashSet<string> validModEngineSHA256 = new HashSet<string>
        {
            "f3741c94bcb7ce7e3764f097e548021f0d1b05e40675ecd6bc2fd51b9eaf9668",
            "bf4578407c4b41590d457a1c715b8d12c495f927dcef91d0c1ed9ce646f5120d",
        };

        // Should be run with current directory == base mod dir
        public void ZipModEngineDir(string runName, string zipPath, MergedMods fullMods, Dictionary<string, List<string>> explicitFiles)
        {
            if (!explicitFiles.ContainsKey("")) throw new Exception("Internal error: no mod list file provided for current mod");
            // Calculate merged mods
            DirectoryInfo mainDirInfo = new DirectoryInfo(".");
            List<string> validBaseDirs = new List<string> { mainDirInfo.FullName };
            List<string> modPaths = new List<string>();
            List<string> dllPaths = new List<string>();
            foreach (string dir in fullMods.Dirs)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(dir);
                if (modPaths.Contains(dirInfo.Name) || dirInfo.Name == mainDirInfo.Name) continue;
                modPaths.Add(dirInfo.Name);
                validBaseDirs.Add(dirInfo.FullName);
            }
            foreach (string path in fullMods.ExternalDlls)
            {
                FileInfo fileInfo = new FileInfo(path);
                foreach (string baseDir in validBaseDirs)
                {
                    if (path.StartsWith(baseDir))
                    {
                        DirectoryInfo baseDirInfo = new DirectoryInfo(baseDir);
                        // Make it relative to parent since that's where the toml file will be
                        dllPaths.Add(GetRelativePath(fileInfo, baseDirInfo.Parent));
                        break;
                    }
                }
            }
            MergedMods mods = new MergedMods(modPaths, dllPaths);
            // First create two files: toml file and bat file. As part of the mod runner setup, assume the ME directory is writeable.
            // modEngineConfig "config_eldenringrandomizer.toml", modEngineLauncher @"diste\ModEngine\modengine2_launcher.exe"
            string meDir = Path.GetDirectoryName(modengineLauncher);
            string tomlPath = Path.Combine(meDir, modengineConfig + ".auto");
            File.WriteAllText(tomlPath, MakeLaunchFileForZip(mods));
            string batName = $"launchmod_{runName}.bat";
            string batPath = Path.Combine(meDir, batName + ".auto");
            File.WriteAllText(batPath, $@".\modengine2_launcher.exe -t er -c .\{modengineConfig}{Environment.NewLine}");
            string meHash = GetSHA256Hash(modengineLauncher);
            if (!validModEngineSHA256.Contains(meHash))
            {
                throw new Exception($"Will not zip untrusted {modengineLauncher} (hash {meHash}). Please use the one that ships with randomizer.");
            }
            // Now the zips
            string zipMainDir = Path.GetFileNameWithoutExtension(zipPath);
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
            {
                while (zip.Entries.Count > 0)
                {
                    zip.Entries[zip.Entries.Count - 1].Delete();
                }
                // Map from zip path to real file path
                SortedDictionary<string, string> zipEntryFiles = new SortedDictionary<string, string>();
                void addFile(DirectoryInfo baseDirInfo, FileInfo info)
                {
                    // Filter out files that can't possibly be parts of mods (probably). Especially try to prevent zip recursion
                    if (info.Extension == ".zip") return;
                    string relPath = GetRelativePath(info, baseDirInfo).Replace(@"\", @"/");
                    zipEntryFiles[relPath] = info.FullName;
                }
                void addDir(DirectoryInfo baseDirInfo, DirectoryInfo info)
                {
                    foreach (FileInfo fileInfo in info.GetFiles("*", SearchOption.AllDirectories))
                    {
                        addFile(baseDirInfo, fileInfo);
                    }
                }
                void addExplicitFiles(DirectoryInfo baseDirInfo, List<string> files)
                {
                    foreach (string file in files)
                    {
                        FileInfo fileInfo = new FileInfo(file);
                        addFile(baseDirInfo, fileInfo);
                    }
                }
                // First add this mod. File expansion has been done for this mod - this optionally applies to other mods for fog->item/enemy.
                DirectoryInfo mainRoot = mainDirInfo.Parent;
                addExplicitFiles(mainRoot, explicitFiles[""]);
                // Other mods, hopefully no conflicts
                foreach (string dir in fullMods.Dirs)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dir);
                    if (!validBaseDirs.Contains(dirInfo.FullName)) continue;
                    if (explicitFiles.TryGetValue(dirInfo.Name, out List<string> dirFiles))
                    {
                        addExplicitFiles(dirInfo.Parent, dirFiles);
                    }
                    else
                    {
                        addDir(dirInfo.Parent, dirInfo);
                    }
                }
                // Mod engine
                DirectoryInfo meFolder = new DirectoryInfo(Path.Combine(meDir, "modengine2"));
                addDir(meFolder.Parent, meFolder);
                zipEntryFiles[modengineConfig] = tomlPath;
                zipEntryFiles[batName] = batPath;
                zipEntryFiles[Path.GetFileName(modengineLauncher)] = modengineLauncher;
                // Finally add them in sorted order
                foreach ((string relPath, string fullPath) in zipEntryFiles)
                {
                    bool alreadyCompressed = fullPath.EndsWith(".dcx") || fullPath.EndsWith("regulation.bin");
                    zip.CreateEntryFromFile(fullPath, $@"{zipMainDir}/{relPath}", alreadyCompressed ? CompressionLevel.Fastest : CompressionLevel.SmallestSize);
                }
            }
        }

        private static string GetRelativePath(FileSystemInfo file, DirectoryInfo dir)
        {
            string dirPrefix = dir.FullName + @"\";
            string fullPath = file.FullName;
            if (fullPath.IndexOf(dirPrefix) != 0) throw new Exception($"Error creating zip: [{fullPath}] lacking prefix [{dirPrefix}]");
            return fullPath.Substring(dirPrefix.Length);
        }

        private static readonly SHA256 Hasher = SHA256.Create();
        public static string GetSHA256Hash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = Hasher.ComputeHash(stream);
                return string.Join("", hash.Select(x => $"{x:x2}"));
            }
        }
    }
}
