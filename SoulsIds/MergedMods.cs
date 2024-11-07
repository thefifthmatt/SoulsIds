using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Tommy;

namespace SoulsIds
{
    public class MergedMods
    {
        private List<string> dirs;
        private List<string> externalDlls;

        // These directories should be validated beforehand, and ideally fully specified
        public MergedMods(string dir = null)
        {
            if (dir != null)
            {
                dirs = new List<string> { dir };
            }
        }

        public MergedMods(List<string> dirs, List<string> externalDlls)
        {
            if (dirs != null && dirs.Count > 0)
            {
                this.dirs = dirs;
            }
            if (externalDlls != null && externalDlls.Count > 0)
            {
                this.externalDlls = externalDlls;
            }
        }

        public bool Resolve(string relPath, out string absPath)
        {
            absPath = null;
            if (dirs == null)
            {
                return false;
            }
            foreach (string dir in dirs)
            {
                string cand = Path.Combine(dir, relPath);
                if (File.Exists(cand))
                {
                    absPath = cand;
                    return true;
                }
            }
            return false;
        }

        public IEnumerable<string> Dirs => dirs ?? new List<string>();
        public int Count => dirs == null ? 0 : dirs.Count;

        // These won't be merged, but they can be added to custom launcher
        public IEnumerable<string> ExternalDlls => externalDlls ?? new List<string>();

        public static MergedMods FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new MergedMods();
            }
            if (File.Exists(path))
            {
                if (path.EndsWith(".toml"))
                {
                    // This can throw an error, but it's fine
                    return FromTomlFile(path);
                }
                return new MergedMods(new FileInfo(path).DirectoryName);
            }
            else if (Directory.Exists(path))
            {
                return new MergedMods(new DirectoryInfo(path).FullName);
            }
            else
            {
                // Don't fail here, wait until the main randomizer loop
                return new MergedMods(path);
            }
        }

        public static void AddExternalDlls(string inPath, string outPath, List<string> extraDlls)
        {
            TomlTable table;
            using (StreamReader reader = File.OpenText(inPath))
            {
                // May fail parse
                table = TOML.Parse(reader);
            }
            string tomlDir = new FileInfo(inPath).DirectoryName;
            TomlNode tomlList = table["modengine"]["external_dlls"];
            // These must be fully qualified paths
            List<string> dlls = new(extraDlls);
            if (tomlList.IsArray)
            {
                foreach (TomlNode node in tomlList)
                {
                    if (node is TomlString path)
                    {
                        dlls.Add(GetFullFileName(tomlDir, path.Value));
                    }
                }
            }
            TomlArray newDlls = new TomlArray();
            newDlls.AddRange(dlls.Distinct().Select(s => new TomlString { Value = s }));
            table["modengine"]["external_dlls"] = newDlls;
            // The library can't roundtrip, fucks up multiple top-level sections
            using (StreamWriter writer = File.CreateText(outPath))
            {
                foreach ((string key, TomlNode node) in table.RawTable)
                {
                    // Lol
                    writer.WriteLine($"{key} = {node.ToInlineToml()}");
                }
            }
        }

        public static MergedMods FromTomlFile(string tomlPath)
        {
            // Parses out mods, excluding the current directory
            string currentDir = Directory.GetCurrentDirectory();
            string tomlDir = new FileInfo(tomlPath).DirectoryName;
            TomlTable table;
            using (StreamReader reader = File.OpenText(tomlPath))
            {
                table = TOML.Parse(reader);
            }
            List<string> dirs = new List<string>();
            foreach (TomlNode node in table["extension"]["mod_loader"]["mods"])
            {
                if (node["enabled"] is TomlBoolean enabled)
                {
                    if (!enabled.Value) continue;
                }
                string modName = null;
                if (node["name"] is TomlString name)
                {
                    modName = name.Value;
                }
                if (node["path"] is TomlString path)
                {
                    // Assume that modengine bat is in the same directory if it's not an absolute path.
                    string dir = GetFullDirectoryName(tomlDir, path.Value);
                    if (dir == currentDir) continue;
                    // A custom hack where fog mod is always excluded, since neither randomizer nor fog can merge it in.
                    // This allows the same toml file to be used by all mods.
                    // TODO: make this explicit in the API.
                    if (dir.Contains("fog") && modName == "fog") continue;
                    dirs.Add(dir);
                }
            }
            List<string> dlls = new List<string>();
            foreach (TomlNode node in table["modengine"]["external_dlls"])
            {
                if (node is TomlString path)
                {
                    dlls.Add(GetFullFileName(tomlDir, path.Value));
                }
            }
            return new MergedMods(dirs, dlls);
        }

        private static string GetFullDirectoryName(string dir, string path)
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(Path.Combine(dir, path));
                return info.FullName;
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static string GetFullFileName(string dir, string path)
        {
            try
            {
                FileInfo info = new FileInfo(Path.Combine(dir, path));
                return info.FullName;
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static void PrintTable(TomlNode node, string indent = "", string parent = "")
        {
            Console.WriteLine($"{indent}{parent}{node}");
            if (node is TomlTable subtable)
            {
                foreach ((string key, TomlNode child) in subtable.RawTable)
                {
                    PrintTable(child, indent + "- ", $"{key}: ");
                }
            }
            else
            {
                foreach (TomlNode child in node)
                {
                    PrintTable(child, indent + "- ");
                }
            }
        }
    }
}
