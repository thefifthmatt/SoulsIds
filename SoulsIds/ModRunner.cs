using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SoulsIds.GameSpec;

namespace SoulsIds
{
    public class ModRunner
    {
        // Shared
        private Process currentProcess;
        private bool me3;
        private readonly FromGame game;
        private readonly string processName;
        // Modengine2
        private readonly string optName;
        private readonly string modengineConfig;
        private readonly string modengineLauncher;
        private readonly string modengineDllConfig;
        // Modengine3
        private readonly string supportsGame;
        private readonly string me3Config;
        private string me3Launcher;

        // Could probably use a builder, and me3 could be an enum
        public ModRunner(FromGame game, string modengineConfig = null, string modengineLauncher = null, string modengineDllConfig = null, string me3Config = null, bool me3 = false)
        {
            this.game = game;
            this.me3 = me3;
            if (game == FromGame.ER)
            {
                processName = "eldenring";
                optName = "er";
                supportsGame = "elden-ring";
            }
            else if (game == FromGame.DS3)
            {
                processName = "DarkSoulsIII";
                optName = "ds3";
                // me3 crashes often
                supportsGame = "ds3";
            }
            else if (game == FromGame.DS1R)
            {
                optName = "dsr";
                processName = "DarkSoulsRemastered";
            }
            else if (game == FromGame.NR)
            {
                supportsGame = "nightreign";
                processName = "nightreign";
                me3 = true;
            }
            else throw new ArgumentException($"Unsupported game {game}");
            if (supportsGame != null)
            {
                me3Launcher = GetAssociatedExecutable(".me3");
                this.me3Config = me3Config;
                if (me3Config == null)
                {
                    throw new Exception($"Launcher path required for {game}");
                }
            }
            else if (me3)
            {
                throw new Exception($"me3 not supported for {game}");
            }
            if (optName != null)
            {
                this.modengineConfig = modengineConfig;
                this.modengineDllConfig = modengineDllConfig;
                this.modengineLauncher = modengineLauncher;
                if (modengineLauncher == null || modengineConfig == null)
                {
                    throw new ArgumentException($"Launcher path required for {game}");
                }
            }
            else if (!me3)
            {
                throw new Exception($"ModEngine2 not supported for {game}");
            }
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

        public void SetMe3(bool me3)
        {
            if (me3 == this.me3)
            {
                return;
            }
            DeleteLaunchFile();
            this.me3 = me3;
            if (me3)
            {
                // Try again if previously failed
                me3Launcher ??= GetAssociatedExecutable(".me3");
            }
        }

        private string ActiveConfigPath() => me3 ? me3Config : modengineConfig;

        public void DeleteLaunchFile()
        {
            if (modengineConfig != null && File.Exists(modengineConfig))
            {
                File.Delete(modengineConfig);
            }
            if (me3Config != null && File.Exists(me3Config))
            {
                File.Delete(me3Config);
            }
        }

        private string FormatModEngineLine(string name, string path, string prev = null)
        {
            if (me3)
            {
                string extra = "";
                if (prev != null)
                {
                    extra = $"load_before = [ {{\"id\" = \"{prev}\", \"optional\" = false}} ]{Environment.NewLine}";
                }
                return $"[[packages]]{Environment.NewLine}id = \"{name}\"{Environment.NewLine}path = '''{path}'''{Environment.NewLine}{extra}{Environment.NewLine}";
            }
            else
            {
                return $"    {{ enabled = true, name = \"{name}\", path = {JsonConvert.ToString(path)} }},";
            }
        }

        private string FormatDllLine(string path)
        {
            if (me3)
            {
                string extra = "";
                string name = Path.GetFileName(path);
                if (name.Equals("NightreignRandomizerHelper.dll", StringComparison.OrdinalIgnoreCase))
                {
                    extra = $"# Needed for fmg hook{Environment.NewLine}load_before = [ {{\"id\" = \"nrsc.dll\", \"optional\" = true}} ]{Environment.NewLine}load_early = true{Environment.NewLine}";
                }
                else if (name.Equals("nrsc.dll", StringComparison.OrdinalIgnoreCase))
                {
                    extra = $"load_early = true{Environment.NewLine}";
                }
                return $"[[natives]]{Environment.NewLine}path = '''{path}'''{Environment.NewLine}{extra}{Environment.NewLine}";
            }
            else
            {
                return $"    {JsonConvert.ToString(path)},";
            }
        }

        public void CreateLaunchFile(List<string> commentLines, string mainDirectory, MergedMods mods = null, List<string> extraDlls = null)
        {
            List<string> modLines = new List<string>();
            List<string> dllLines = new List<string>();
            string mainMod = "randomizer";
            modLines.Add(FormatModEngineLine(mainMod, mainDirectory));
            string prevMod = mainMod;
            if (extraDlls != null)
            {
                foreach (string path in extraDlls)
                {
                    FileInfo modInfo = new FileInfo(path);
                    if (modInfo.Exists)
                    {
                        dllLines.Add(FormatDllLine(modInfo.FullName));
                    }
                }
            }
            if (mods != null)
            {
                int modNum = 1;
                foreach (string modDir in mods.Dirs)
                {
                    DirectoryInfo modInfo = new DirectoryInfo(modDir);
                    if (modInfo.Exists)
                    {
                        string modName = $"mod{modNum++}";
                        modLines.Add(FormatModEngineLine(modName, modInfo.FullName, prevMod));
                        prevMod = modName;
                    }
                }
                // TODO this doesn't really work for me3
                foreach (string path in mods.ExternalDlls)
                {
                    FileInfo modInfo = new FileInfo(path);
                    if (modInfo.Exists)
                    {
                        dllLines.Add(FormatDllLine(modInfo.FullName));
                    }
                }
            }
            string file;
            if (me3)
            {
                file = $@"# WARNING: THIS FILE IS AUTOGENERATED
# CONTENTS WILL BE AUTOMATICALLY OVERWRITTEN

{string.Join("", commentLines.Select(line => $"# {line}{Environment.NewLine}"))}

profileVersion = ""v1""

{string.Join("", dllLines)}
{string.Join("", modLines)}

[[supports]]
game = ""{supportsGame}""
";
            }
            else
            {
                file = $@"# DO NOT MODIFY THIS FILE!
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
            }
            File.WriteAllText(ActiveConfigPath(), file);
        }

        public string ReadLaunchFileText()
        {
            return File.ReadAllText(ActiveConfigPath());
        }

        // me2 only
        private string MakeLaunchFileForZip(MergedMods mods)
        {
            List<string> modLines = new List<string>();
            List<string> dllLines = new List<string>();
            DirectoryInfo currentDir = new DirectoryInfo(".");
            string prevMod = "randomizer";
            modLines.Add(FormatModEngineLine("randomizer", currentDir.Name));
            if (mods != null)
            {
                // Add relative paths directly
                int modNum = 1;
                foreach (string modDir in mods.Dirs)
                {
                    string modName = $"mod{modNum++}";
                    modLines.Add(FormatModEngineLine("mod", modDir, prevMod));
                    prevMod = modName;
                }
                foreach (string path in mods.ExternalDlls)
                {
                    dllLines.Add($"    {JsonConvert.ToString(path)},");
                }
            }
            if (me3)
            {
                return $@"profileVersion = ""v1""

{string.Join("", dllLines)}
{string.Join("", modLines)}

[[supports]]
game = ""{supportsGame}""
";
            }
            else
            {
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
        }

        public bool IsValid() => File.Exists(ActiveConfigPath()) && (me3 ? File.Exists(me3Launcher) : File.Exists(modengineLauncher));

        private bool? IsMaybeRunning()
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch (Exception) { }
            return null;
        }

        public bool IsGameRunning() => IsMaybeRunning() ?? false;
        public bool IsLaunching() => currentProcess != null;

        public void CreateDllLaunchFile(List<string> extraDlls)
        {
            if (modengineDllConfig == null) throw new Exception($"Internal error: extra dlls given but no dll config name is defined");
            MergedMods.AddExternalDlls(modengineConfig, modengineDllConfig, extraDlls);
        }

        // https://stackoverflow.com/questions/10174156/open-file-with-associated-application
        // However: https://stackoverflow.com/questions/770023/how-do-i-get-file-type-information-based-on-extension-not-mime-in-c-sharp
        private static readonly bool launchExplorer = false;
        public async Task LaunchGame(List<string> extraDlls = null)
        {
            if (currentProcess != null) return;
            string launchFile = ActiveConfigPath();
            if (!me3 && extraDlls != null && File.Exists(modengineConfig))
            {
                // For Elden Ring, this flow is done on launch, to avoid rerunning randomizer for separate dll list
                // DS1 passes extraDlls into the launch file
                CreateDllLaunchFile(extraDlls);
                launchFile = modengineDllConfig;
            }
            using (Process process = new Process())
            {
                bool exitsAfterStart = true;
                if (me3)
                {
                    if (launchExplorer)
                    {
                        process.StartInfo.FileName = "explorer";
                        process.StartInfo.Arguments = launchFile;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(me3Launcher))
                        {
                            throw new Exception($"Cannot launch game: No me3 launcher was found associated with .me3 format");
                        }
                        process.StartInfo.FileName = me3Launcher;
                        process.StartInfo.Arguments = $@"launch -p {launchFile}";
                        process.StartInfo.UseShellExecute = false;
                        exitsAfterStart = false;
                    }
                }
                else
                {
                    process.StartInfo.FileName = modengineLauncher;
                    // TODO: Add ..s based on how many are present in the launcher
                    // TODO: Add game directory here? Escaping path names is a mess
                    process.StartInfo.Arguments = $@"-t {optName} -c ..\..\{launchFile}";
                    process.StartInfo.WorkingDirectory = Path.GetDirectoryName(modengineLauncher);
                    process.StartInfo.UseShellExecute = false;
                }
                process.EnableRaisingEvents = true;

                // Start
                currentProcess = process;
                DateTime startTime = DateTime.Now;
                StartRunning?.Invoke(this, new EventArgs());

                process.Start();
                if (exitsAfterStart)
                {
                    await WaitForExitAsync(process);
                }

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

        // Maybe support more in the future, but be cautious about bad distributed exes here (also me3 option)
        private static readonly HashSet<string> validModEngineSHA256 = new HashSet<string>
        {
            "f3741c94bcb7ce7e3764f097e548021f0d1b05e40675ecd6bc2fd51b9eaf9668",
            "bf4578407c4b41590d457a1c715b8d12c495f927dcef91d0c1ed9ce646f5120d",
        };

        // Should be run with current directory == base mod dir
        public void ZipModEngineDir(string runName, string zipPath, MergedMods fullMods, IList<string> mainDlls, IDictionary<string, List<string>> explicitFiles)
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
            foreach (string path in mainDlls.Concat(fullMods.ExternalDlls))
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
            // For ME3, the directory is still used as scratch space for now
            Dictionary<string, string> zipModEngineFiles = new();
            string meDir = Path.GetDirectoryName(modengineLauncher ?? throw new Exception($"No modengine directory available to zip mod"));
            if (me3)
            {
                string meConfigPath = Path.Combine(meDir, me3Config + ".auto");
                File.WriteAllText(meConfigPath, MakeLaunchFileForZip(mods));
                zipModEngineFiles[me3Config] = meConfigPath;
            }
            else
            {
                string meConfigPath = Path.Combine(meDir, modengineConfig + ".auto");
                File.WriteAllText(meConfigPath, MakeLaunchFileForZip(mods));
                string batName = $"launchmod_{runName}.bat";
                string batPath = Path.Combine(meDir, batName + ".auto");
                File.WriteAllText(batPath, $@".\modengine2_launcher.exe -t {optName} -c .\{modengineConfig}{Environment.NewLine}");
                string meHash = GetSHA256Hash(modengineLauncher);
                if (!validModEngineSHA256.Contains(meHash))
                {
                    throw new Exception($"Will not zip untrusted {modengineLauncher} (hash {meHash}). Please use the one that ships with randomizer.");
                }
                zipModEngineFiles[modengineConfig] = meConfigPath;
                zipModEngineFiles[batName] = batPath;
                zipModEngineFiles[Path.GetFileName(modengineLauncher)] = modengineLauncher;
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
                foreach ((string relPath, string fullPath) in zipModEngineFiles)
                {
                    zipEntryFiles[relPath] = fullPath;
                }
                if (!me3)
                {
                    DirectoryInfo meFolder = new DirectoryInfo(Path.Combine(meDir, "modengine2"));
                    addDir(meFolder.Parent, meFolder);
                }
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

        [DllImport("Shlwapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern uint AssocQueryString(int flags, int str, string pszAssoc, string pszExtra, [Out] StringBuilder pszOut, [In][Out] ref uint pcchOut);

        public static string GetAssociatedExecutable(string ext)
        {
            try
            {
                uint pcchOut = 0;
                AssocQueryString(0x40, 2, ext, null, null, ref pcchOut);

                StringBuilder pszOut = new StringBuilder((int)pcchOut);
                AssocQueryString(0x40, 2, ext, null, pszOut, ref pcchOut);
                return pszOut.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }
    }
}
