using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SoulsFormats;
using static SoulsIds.GameSpec;

namespace SoulsIds
{
    public class GameEditor
    {
        public readonly GameSpec Spec;

        public GameEditor(FromGame game)
        {
            this.Spec = ForGame(game);
        }
        public GameEditor(GameSpec spec)
        {
            this.Spec = spec;
        }

        public Dictionary<string, PARAM> LoadParams(Dictionary<string, PARAMDEF> defs)
        {
            if (Spec.ParamFile == null) throw new Exception("Param path unknown");
            return LoadParams($@"{Spec.GameDir}\{Spec.ParamFile}", defs);
        }

        public Dictionary<string, PARAM> LoadParams(string path, Dictionary<string, PARAMDEF> defs)
        {
            return LoadBnd(path, (data, paramPath) =>
            {
                PARAM param;
                try
                {
                    param = PARAM.Read(data);
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to load param {paramPath}: " + e);
                }
                if (defs != null && defs.Count > 0)
                {
                    if (!param.ApplyParamdefCarefully(defs.Values))
                    {
                        // Console.WriteLine($"No applicable paramdef found for {paramPath} ({param.DetectedSize} size)");
                    }
                }
                return param;
            });
        }

        // Load params from a combination of game dir and param file in spec.
        public Dictionary<string, PARAM> LoadParams(Dictionary<string, PARAM.Layout> layouts = null, bool allowError = false)
        {
            if (Spec.ParamFile == null) throw new Exception("Param path unknown");
            return LoadParams($@"{Spec.GameDir}\{Spec.ParamFile}", layouts, allowError);
        }

        // Load params from given path, relative to current dir
        public Dictionary<string, PARAM> LoadParams(string path, Dictionary<string, PARAM.Layout> layouts = null, bool allowError = false)
        {
            layouts = layouts ?? LoadLayouts();
            return LoadBnd(path, (data, paramPath) =>
            {
                PARAM param;
                try
                {
                    param = PARAM.Read(data);
                }
                catch (Exception e)
                {
                    if (!allowError) throw new Exception($"Failed to load param {paramPath}: " + e);
                    // For DS3 this also includes draw params, so just silently fail
                    // TODO: Find a better way to load all params reliably
                    return null;
                }
                if (layouts == null)
                {
                    return param;
                }
                else if (layouts.ContainsKey(param.ParamType))
                {
                    PARAM.Layout layout = layouts[param.ParamType];
                    if (layout.Size == param.DetectedSize)
                    {
                        param.ApplyParamdef(layout.ToParamdef(param.ParamType, out var _));
                        return param;
                    }
                    else
                    {
                        // Console.WriteLine($"Mismatched size for {path} - {layout.Size} vs {param.DetectedSize} actual");
                    }
                }
                return null;
            });
        }

        // Loads names from the given txt file name in NameDir, relative to the current dir
        public Dictionary<T, string> LoadNames<T>(string name, Func<string, T> key, bool allowMissing = false)
        {
            if (Spec.NameDir == null) throw new Exception("Name file dir not provided");
            Dictionary<T, string> ret = new Dictionary<T, string>();
            string path = $@"{Spec.NameDir}\{name}.txt";
            if (allowMissing && !File.Exists(path)) return new Dictionary<T, string>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("#")) continue;
                int spot = line.IndexOf(' ');
                if (spot == -1)
                {
                    throw new Exception($"Bad line {line} in {path}");
                }
                string idstr = line.Substring(0, spot);
                string text = line.Substring(spot + 1);
                ret[key(idstr)] = text;
            }
            return ret;
        }

        // Loads layouts from layout dir, relative to the current dir
        public Dictionary<string, PARAM.Layout> LoadLayouts()
        {
            if (Spec.LayoutDir == null) throw new Exception("Layout dir not provided");
            Dictionary<string, PARAM.Layout> layouts = new Dictionary<string, PARAM.Layout>();
            foreach (string path in Directory.GetFiles(Spec.LayoutDir, "*.xml"))
            {
                string paramID = Path.GetFileNameWithoutExtension(path);
                PARAM.Layout layout = PARAM.Layout.ReadXMLFile(path);
                layouts[paramID] = layout;
            }
            return layouts;
        }

        public Dictionary<string, PARAMDEF> LoadDefs()
        {
            if (Spec.DefDir == null) throw new Exception("Def dir not provided");
            Dictionary<string, PARAMDEF> defs = new Dictionary<string, PARAMDEF>();
            foreach (string path in Directory.GetFiles(Spec.DefDir, "*.xml"))
            {
                string paramID = Path.GetFileNameWithoutExtension(path);
                PARAMDEF def = PARAMDEF.XmlDeserialize(path);
                defs[paramID] = def;
            }
            return defs;
        }

        // Loads multiple files from relDir, relative to the game directory
        public Dictionary<string, T> Load<T>(string relDir, Func<string, T> reader, string ext = "*.dcx")
        {
            if (Spec.GameDir == null) throw new Exception("Base game dir not provided");
            Dictionary<string, T> ret = new Dictionary<string, T>();
            foreach (string path in Directory.GetFiles($@"{Spec.GameDir}\{relDir}", ext))
            {
                string name = BaseName(path);
                try
                {
                    ret[name] = reader(path);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load {path}: {ex}");
                }
            }
            return ret;
        }

        // Loads a bnd file from file path, relative to the current dir
        public Dictionary<string, T> LoadBnd<T>(string path, Func<byte[], string, T> parser, string fileExt = null)
        {
            string name = BaseName(path);
            Dictionary<string, T> bnds = new Dictionary<string, T>();
            IBinder bnd;
            try
            {
                bnd = ReadBnd(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load {path}: {ex}");
            }
            foreach (BinderFile file in bnd.Files)
            {
                if (fileExt != null && !file.Name.EndsWith(fileExt)) continue;
                string bndName = BaseName(file.Name);
                try
                {
                    T res = parser(file.Bytes, bndName);
                    if (res != null) bnds[bndName] = res;
                }
                catch (Exception ex)
                {
                    // TODO: Check this is okay, rather than printing
                    throw new Exception($"Failed to load {path}: {bndName}: {ex}");
                }
            }
            return bnds;
        }

        // Loads multiple bnd files from file path, relative to game dir
        public Dictionary<string, Dictionary<string, T>> LoadBnds<T>(string relDir, Func<byte[], string, T> parser, string ext = "*bnd.dcx", string fileExt = null)
        {
            if (Spec.GameDir == null) throw new Exception("Base game dir not provided");
            Dictionary<string, Dictionary<string, T>> ret = new Dictionary<string, Dictionary<string, T>>();
            foreach (string path in Directory.GetFiles($@"{Spec.GameDir}\{relDir}", ext))
            {
                string name = BaseName(path);
                Dictionary<string, T> bnds = LoadBnd(path, parser, fileExt);
                if (bnds.Count > 0)
                {
                    ret[name] = bnds;
                }
            }
            return ret;
        }

        // Multi-reader based on path
        // This seems to inconsistently strip compression metadata, so TODO check all usages.
        public IBinder ReadBnd(string path)
        {
            try
            {
                string detectPath = path;
                if (detectPath.EndsWith("bak")) detectPath = detectPath.Substring(0, detectPath.LastIndexOf('.'));
                if (Spec.Game == FromGame.DS3 && detectPath.EndsWith("Data0.bdt"))
                {
                    return SFUtil.DecryptDS3Regulation(path);
                }
                if (Spec.Game == FromGame.ER && detectPath.EndsWith("regulation.bin"))
                {
                    return SFUtil.DecryptERRegulation(path);
                }
                byte[] data = File.ReadAllBytes(path);
                if (DCX.Is(data))
                {
                    data = DCX.Decompress(data);
                }
                if (BND4.Is(data))
                {
                    return BND4.Read(data);
                }
                else if (BND3.Is(data))
                {
                    return BND3.Read(data);
                }
                else throw new Exception($"Unrecognized bnd format for game {Spec.Game}: {path}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load {path}: {ex}");
            }
        }

        // Writes using similar selection logic as ReadBnd
        public void WriteBnd(string outPath, IBinder bnd, DCX.Type dcx)
        {
            if (bnd is BND4 bnd4)
            {
                if (Spec.Game == FromGame.DS3 && outPath.EndsWith("Data0.bdt"))
                {
                    bnd4.Compression = dcx;
                    SFUtil.EncryptDS3Regulation(outPath, bnd4);
                    if (new FileInfo(outPath).Length > 0x10000C)
                    {
                        File.Delete(outPath);
                        throw new Exception($"You must set loadLooseParams=1 in modengine.ini. Otherwise Data0.bdt is too large and will permanently corrupt your save file.");
                    }
                }
                else if (Spec.Game == FromGame.ER && outPath.EndsWith("regulation.bin"))
                {
                    bnd4.Compression = dcx;
                    SFUtil.EncryptERRegulation(outPath, bnd4);
                }
                else
                {
                    bnd4.Write(outPath, dcx);
                }
            }
            if (bnd is BND3 bnd3) bnd3.Write(outPath, dcx);
        }

        // Overrides bnd, relative to current dir, and outputs to directory, relative to game dir if not absolute path
        // This is the second-worst method. Is anything using this?
        public void OverrideBnd<T>(string path, string toDir, Dictionary<string, T> diffData, Func<T, byte[]> writer, string fileExt = null)
        {
            string fname = Path.GetFileName(path);
            string outPath = AbsolutePath(Spec.GameDir, $@"{toDir}\{fname}");
            OverrideBndRel(path, outPath, diffData, writer, fileExt);
        }

        // Overrides bnd, relative to current dir, and outputs to other file
        public void OverrideBndRel<T>(string path, string outPath, Dictionary<string, T> diffData, Func<T, byte[]> writer, string fileExt = null, DCX.Type dcx = DCX.Type.Unknown)
        {
            if (dcx == DCX.Type.Unknown)
            {
                if (Spec.Dcx == DCX.Type.Unknown) throw new Exception("DCX compression type not provided");
                dcx = Spec.Dcx;
            }
            IBinder bnd = GetOverrideBndRel(path, diffData, writer, fileExt);
            WriteBnd(outPath, bnd, dcx);
        }

        // A get method version, mainly for adding extra files before writing
        public IBinder GetOverrideBndRel<T>(string path, Dictionary<string, T> diffData, Func<T, byte[]> writer, string fileExt = null)
        {
            IBinder bnd = ReadBnd(path);
            foreach (BinderFile file in bnd.Files)
            {
                if (fileExt != null && !file.Name.EndsWith(fileExt)) continue;
                string bndName = BaseName(file.Name);
                if (!diffData.ContainsKey(bndName) || diffData[bndName] == null) continue;
                try
                {
                    byte[] data = writer(diffData[bndName]);
                    if (data != null)
                    {
                        file.Bytes = data;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load {path}: {bndName}: {ex}");
                }
            }
            return bnd;
        }

        // Overrides bnds, relative to game dir, and outputs to directory, relative to game dir if relative
        // This is basically the worst method. Is anything using it anymore?
        public void OverrideBnds<T>(string fromDir, string toDir, Dictionary<string, Dictionary<string, T>> diffBnds, Func<T, byte[]> writer, string ext = "*bnd.dcx", string fileExt = null)
        {
            if (Spec.GameDir == null) throw new Exception("Base game dir not provided");
            foreach (string path in Directory.GetFiles($@"{Spec.GameDir}\{fromDir}", ext))
            {
                string name = BaseName(path);
                if (!diffBnds.ContainsKey(name)) continue;
                OverrideBnd(path, toDir, diffBnds[name], writer, fileExt);
            }
        }

        // Return a path name without, for easy access within code.
        // This should be avoided when there are multiple files with the same base name.
        public static string BaseName(string path)
        {
            path = Path.GetFileName(path);
            if (path.IndexOf('.') == -1) return path;
            return path.Substring(0, path.IndexOf('.'));
        }

        // Given an default absolute path and relative or absolute path, resolve the latter path if necessary.
        public static string AbsolutePath(string basePath, string maybeRelPath)
        {
            if (basePath == null) return Path.GetFullPath(maybeRelPath);
            return Path.GetFullPath(Path.Combine(basePath, maybeRelPath));
        }

        // Param util: copy all values from one row to another
        public static void CopyRow(PARAM.Row from, PARAM.Row to)
        {
            for (int i = 0; i < from.Cells.Count; i++)
            {
                to.Cells[i].Value = from.Cells[i].Value;
            }
        }

        public static PARAM.Row AddRow(PARAM param, int id, int oldId = -1)
        {
            PARAM.Row oldRow = null;
            if (oldId >= 0)
            {
                oldRow = param[oldId];
                if (oldRow == null)
                {
                    throw new ArgumentException($"Row id {oldId} in {param.AppliedParamdef?.ParamType} does not exist. Cannot copy to row {id}");
                }
            }
            return AddRow(param, id, oldRow);
        }

        public static PARAM.Row AddRow(PARAM param, int id, PARAM.Row oldRow)
        {
            if (param[id] != null)
            {
                // This can get quadratic, but good to check
                throw new ArgumentException($"Trying to add row id {id} in {param.AppliedParamdef?.ParamType} but it already exists");
            }
            PARAM.Row row = new PARAM.Row(id, "", param.AppliedParamdef);
            param.Rows.Add(row);
            if (oldRow != null)
            {
                CopyRow(oldRow, row);
            }
            return row;
        }
    }
}
