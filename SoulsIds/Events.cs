using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using SoulsFormats;
using static SoulsFormats.EMEVD.Instruction;

namespace SoulsIds
{
    public class Events
    {
        private static Dictionary<long, int> ArgLengths = new Dictionary<long, int>
        {
            [0] = 1,
            [1] = 2,
            [2] = 4,
            [3] = 1,
            [4] = 2,
            [5] = 4,
            [6] = 4,
            [8] = 4,
        };

        // Normally 'out object' would be preferable, but this is only for internal config values, so skipping the boilerplate should be fine
        private object ParseArgWithEnum(string arg, ArgType type)
        {
            if (enumByName.TryGetValue(arg, out int value))
            {
                arg = value.ToString();
            }
            return ParseArg(arg, type);
        }

        private static object ParseArg(string arg, ArgType type)
        {
            try
            {
                switch (type)
                {
                    case ArgType.Byte:
                        return byte.Parse(arg);
                    case ArgType.UInt16:
                        return ushort.Parse(arg);
                    case ArgType.UInt32:
                        return uint.Parse(arg);
                    case ArgType.SByte:
                        return sbyte.Parse(arg);
                    case ArgType.Int16:
                        return short.Parse(arg);
                    case ArgType.Int32:
                        return int.Parse(arg);
                    case ArgType.Single:
                        return float.Parse(arg, CultureInfo.InvariantCulture);
                    default:
                        throw new Exception($"Unrecognized arg type {type}");
                }
            }
            catch (FormatException)
            {
                throw new Exception($"Internal error: Failed to parse \"{arg}\" as {type}");
            }
        }

        private static readonly Dictionary<long, object> defaultArgValues = new Dictionary<long, object>
        {
            // Instead of parsing the EMEDF default value, just use the same defaults for all instructions
            [0] = (byte)0,
            [1] = (ushort)0,
            [2] = (uint)0,
            [3] = (sbyte)0,
            [4] = (short)0,
            [5] = (int)0,
            [6] = (float)0,
            [8] = (uint)0,
        };

        public static string TypeString(long type)
        {
            if (type == 0) return "byte";
            if (type == 1) return "ushort";
            if (type == 2) return "uint";
            if (type == 3) return "sbyte";
            if (type == 4) return "short";
            if (type == 5) return "int";
            if (type == 6) return "float";
            if (type == 8) return "uint";
            throw new Exception("Invalid type in argument definition.");
        }

        private EMEDF doc;
        private Dictionary<string, (int, int)> docByName = new Dictionary<string, (int, int)>();
        private Dictionary<string, int> enumByName = new Dictionary<string, int>();
        private Dictionary<EMEDF.InstrDoc, List<int>> funcBytePositions = new Dictionary<EMEDF.InstrDoc, List<int>>();
        private Dictionary<EMEDF.InstrDoc, Dictionary<int, EventValueType>> funcValueTypes = null;
        private readonly bool darkScriptMode;
        private readonly bool paramAwareMode;
        private readonly bool liteMode;

        public Events(
            string emedfPath,
            bool darkScriptMode = false,
            bool paramAwareMode = false,
            List<InstructionValueSpec> valueSpecs = null)
        {
            if (emedfPath == null)
            {
                doc = MakeLite(valueSpecs);
                enumByName["TargetEventFlagType.EventFlag"] = 0;
                enumByName["ON"] = 1;
                enumByName["OFF"] = 0;
                enumByName["PASS"] = 1;
                enumByName["FAIL"] = 0;
                enumByName["MAIN"] = 0;
                enumByName["SoundType.SFX"] = 5;
                enumByName["Enabled"] = 1;
                enumByName["Disabled"] = 0;
                enumByName["ComparisonType.Equal"] = 0;
                enumByName["ComparisonType.NotEqual"] = 1;
                enumByName["AIStateType.Combat"] = 3;
                enumByName["DeathState.Dead"] = 1;
                for (int label = 0; label <= 20; label++)
                {
                    enumByName[$"Label.Label{label}"] = label;
                }
                for (int i = 1; i <= 15; i++)
                {
                    enumByName[$"AND_{i:d2}"] = i;
                    enumByName[$"OR_{i:d2}"] = -i;
                }
                liteMode = true;
            }
            else
            {
                doc = EMEDF.ReadFile(emedfPath);
                liteMode = false;
            }
            this.darkScriptMode = darkScriptMode;
            this.paramAwareMode = paramAwareMode;

            docByName = doc.Classes.SelectMany(c => c.Instructions.Select(i => (i, (int)c.Index))).ToDictionary(i => i.Item1.Name, i => (i.Item2, (int)i.Item1.Index));
            funcBytePositions = new Dictionary<EMEDF.InstrDoc, List<int>>();
            Dictionary<string, EMEDF.EnumDoc> enums = new Dictionary<string, EMEDF.EnumDoc>();
            // For darkScriptMode: accept either standard or darkscript names as full-command input, but output based on the flag.
            // However, the edit strings do require enums in darkscript mode because we do coarse string matching.
            foreach (EMEDF.EnumDoc enm in doc.Enums)
            {
                bool global = EnumNamesForGlobalization.Contains(enm.Name);
                enums[enm.Name] = enm;
                string enumName = Regex.Replace(enm.Name, @"[^\w]", "");
                string prefix = global ? "" : $"{enumName}.";
                enm.DisplayName = enumName;
                enm.DisplayValues = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> pair in enm.Values)
                {
                    // DarkScript3 input/output
                    string name = prefix + Regex.Replace(pair.Value, @"[^\w]", "");
                    name = EnumReplacements.TryGetValue(name, out string displayName) ? displayName : name;
                    enumByName[name] = int.Parse(pair.Key);
                    if (darkScriptMode)
                    {
                        enm.DisplayValues[pair.Key] = name;
                    }
                }
            }
            foreach (EMEDF.ClassDoc bank in doc.Classes)
            {
                foreach (EMEDF.InstrDoc instr in bank.Instructions)
                {
                    (int, int) spec = ((int)bank.Index, (int)instr.Index);
                    string darkName = TitleCaseName(instr.Name);
                    docByName[instr.Name] = docByName[darkName] = spec;
                    if (darkScriptMode && !liteMode)
                    {
                        // Name is used for output
                        instr.Name = darkName;
                    }
                    int bytePos = 0;
                    foreach (EMEDF.ArgDoc arg in instr.Arguments)
                    {
                        int len = ArgLengths[arg.Type];
                        if (bytePos % len > 0) bytePos += len - (bytePos % len);
                        AddMulti(funcBytePositions, instr, bytePos);
                        arg.Offset = bytePos;
                        bytePos += len;
                        if (arg.EnumName != null && enums.TryGetValue(arg.EnumName, out EMEDF.EnumDoc enm))
                        {
                            arg.EnumDoc = enm;
                        }
                        if (darkScriptMode && !liteMode)
                        {
                            arg.Name = CamelCaseName(arg.Name);
                        }
                    }
                    // Final int padding. Add a final one for overall length
                    if (bytePos % 4 > 0) bytePos += 4 - (bytePos % 4);
                    AddMulti(funcBytePositions, instr, bytePos);
                }
            }

            if (valueSpecs != null)
            {
                funcValueTypes = new Dictionary<EMEDF.InstrDoc, Dictionary<int, EventValueType>>();
                foreach (InstructionValueSpec spec in valueSpecs)
                {
                    if (spec.Args == null) continue;
                    EMEDF.InstrDoc instrDoc = doc[spec.Bank][spec.ID];
                    funcValueTypes[instrDoc] = spec.Args;
                }
            }
        }

        public static EMEDF MakeLite(List<InstructionValueSpec> specs)
        {
            SortedDictionary<int, EMEDF.ClassDoc> classes = new SortedDictionary<int, EMEDF.ClassDoc>();
            Dictionary<string, ArgType> argTypes =
                ((ArgType[])Enum.GetValues(typeof(ArgType))).ToDictionary(e => e.ToString(), e => e);
            foreach (InstructionValueSpec spec in specs)
            {
                if (!classes.TryGetValue(spec.Bank, out EMEDF.ClassDoc classDoc))
                {
                    classes[spec.Bank] = classDoc = new EMEDF.ClassDoc
                    {
                        Index = spec.Bank,
                        Instructions = new List<EMEDF.InstrDoc>()
                    };
                }
                EMEDF.InstrDoc doc = new EMEDF.InstrDoc { Index = spec.ID };
                classDoc.Instructions.Add(doc);
                if (spec.Alias == null)
                {
                    doc.Name = $"c{spec.Bank}_{spec.ID}";
                    doc.Arguments = Enumerable.Range(0, spec.Length / 4).Select(i => new EMEDF.ArgDoc
                    {
                        Name = $"unknown{i}",
                        Type = (long)ArgType.Int32,
                    }).ToArray();
                }
                else
                {
                    string[] parts = spec.Alias.Split(' ');
                    doc.Name = parts[0];
                    doc.Arguments = parts.Skip(1).Select((part, i) => new EMEDF.ArgDoc
                    {
                        Name = $"unknown{i}",
                        Type = (long)argTypes[part],
                    }).ToArray();
                }
            }
            return new EMEDF
            {
                Classes = classes.Values.ToList(),
                Enums = new EMEDF.EnumDoc[0],
            };
        }

        public static void AddSimpleEvent(
            EMEVD emevd, int id,
            IEnumerable<EMEVD.Instruction> instrs,
            EMEVD.Event.RestBehaviorType rest = EMEVD.Event.RestBehaviorType.Default)
        {
            EMEVD.Event ev = new EMEVD.Event(id, rest);
            // ev.Instructions.AddRange(instrs.Select(t => events.ParseAdd(t)));
            ev.Instructions.AddRange(instrs);
            emevd.Events.Add(ev);
            emevd.Events[0].Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object> { 0, (uint)id, (uint)0 }));
        }

        // Instruction metadata
        public Instr Parse(EMEVD.Instruction instr, OldParams pre = null)
        {
            bool isInit = instr.Bank == 2000 && (instr.ID == 0 || instr.ID == 6);
            // if (onlyCmd && isInit) return null;
            // if (onlyInit && !isInit) return null;
            EMEDF.InstrDoc instrDoc = doc[instr.Bank][instr.ID];
            List<ArgType> argTypes = (isInit || instrDoc == null)
                ? Enumerable.Repeat(ArgType.Int32, instr.ArgData.Length / 4).ToList()
                : instrDoc.Arguments.Select(arg => arg.Type == 8 ? ArgType.UInt32 : (ArgType)arg.Type).ToList();
            List<object> args = instr.UnpackArgs(argTypes);
            Instr ret = new Instr
            {
                Val = instr,
                Doc = instrDoc,
                Types = argTypes,
                ArgList = args,
                Init = isInit,
                Writeable = pre != null || !paramAwareMode,
            };
            if (isInit)
            {
                ret.Offset = 2;
                // Non-Elden Ring case
                if (instr.ID == 6 && instrDoc.Arguments[0].Name == "Event ID")
                {
                    ret.Offset = 1;
                }
                // ret.Callee = (int)args[instr.ID == 0 ? 1 : 0];
                ret.Callee = (int)args[ret.Offset - 1];
            }
            if (paramAwareMode && pre != null)
            {
                SetInstrParamArgs(ret, pre);
            }
            return ret;
        }

        // Set params in the Instr. They will require the same OldParams to be written back.
        public void SetInstrParamArgs(Instr instr, OldParams pre)
        {
            List<EMEVD.Parameter> ps = pre.GetInstructionParams(instr.Val);
            if (ps.Count == 0) return;
            foreach (EMEVD.Parameter p in ps)
            {
                int index = IndexFromByteOffset(instr, (int)p.TargetStartByte);
                // Don't set instr[index] so Modified is not triggered
                instr.ArgList[index] = $"X{p.SourceStartByte}_{p.ByteCount}";
            }
        }

        public class Instr
        {
            // Actual instruction
            public EMEVD.Instruction Val { get; set; }
            public EMEDF.InstrDoc Doc { get; set; }
            public string Name => Doc?.Name;
            public List<ArgType> Types { get; set; }
            // Hidden to avoid circumventing Modified management
            internal List<object> ArgList { get; set; }
            // Read-only interface when args are really required
            public IEnumerable<object> Args => ArgList.Select(x => x);
            public int Count => ArgList.Count;
            // Whether an event initialization or not
            public bool Init { get; set; }
            // If an event initialization, which event is being initialized
            public int Callee { get; set; }
            // If an event initialization, the index start of actual event arguments
            public int Offset { get; set; }
            public int CalleeOffset => Offset - 1;
            // Dirty bit
            public bool Modified { get; private set; }
            public bool Writeable { get; internal set; }

            public void Save(OldParams pre = null)
            {
                if (!Writeable)
                {
                    throw new Exception($"Internal error: not allowed to repack {this}");
                }
                if (Modified)
                {
                    List<object> packArgs = ArgList;
                    if (ArgList.Any(a => a is string))
                    {
                        if (pre == null) throw new Exception($"Internal error: cannot repack {this} without provided param args");
                        packArgs = ArgList.ToList();
                        List<EMEVD.Parameter> ps = new List<EMEVD.Parameter>();
                        for (int i = 0; i < packArgs.Count; i++)
                        {
                            if (packArgs[i] is string a)
                            {
                                EMEDF.ArgDoc argDoc = Doc.Arguments[i];
                                int argOffset = argDoc.Offset;
                                int argLen = ArgLengths[argDoc.Type];
                                if (!TryFullArgSpec(a, out int paramOffset, out int paramLen) || paramLen > argLen)
                                {
                                    throw new Exception($"Internal error: invalid parameter at {i} repacking {this}");
                                }
                                ps.Add(new EMEVD.Parameter(0, argOffset, paramOffset, paramLen));
                                packArgs[i] = defaultArgValues[argDoc.Type];
                            }
                        }
                        pre.AddParameters(Val, ps);
                    }
                    Val.PackArgs(packArgs);
                    Modified = false;
                }
            }

            public void AddArgs(IEnumerable<object> args)
            {
                ArgList.AddRange(args);
                Modified = true;
            }

            public object this[int i]
            {
                get => ArgList[i];
                set
                {
                    if (value is string s)
                    {
                        if (s.StartsWith("X"))
                        {
                            // Allow this in the case of psuedo-variables representing event args. This instruction cannot be repacked in this case.
                            ArgList[i] = value;
                        }
                        else
                        {
                            ArgList[i] = ParseArg(s, Types[i]);
                        }
                    }
                    else
                    {
                        ArgList[i] = value;
                    }
                    Modified = true;
                }
            }

            public bool TryCalleeKey(ICollection<EventKey> check, string map, out EventKey key)
            {
                // TODO: Check if 1050402210 actually works or not
                key = new EventKey(Callee, map);
                if (check.Contains(key)) return true;
                key = new EventKey(Callee, "common_func");
                if (check.Contains(key)) return true;
                key = null;
                return false;
            }

            public bool TryCalleeValue<T>(IDictionary<EventKey, T> check, string map, out EventKey key, out T value)
            {
                value = default;
                if (!TryCalleeKey(check.Keys, map, out key))
                {
                    return false;
                }
                return check.TryGetValue(key, out value);
            }

            public string FormatArg(object arg, int i)
            {
                return FormatValue(arg, Doc?.Arguments != null && i < Doc.Arguments.Length ? Doc.Arguments[i] : null);
            }

            public override string ToString() => $"{Name ?? $"c{Val?.Bank}_{Val?.ID}"}({string.Join(", ", ArgList.Select((a, i) => FormatArg(a, i)))})";
        }

        private static string FormatValue(object arg, EMEDF.ArgDoc doc = null)
        {
            if (doc != null)
            {
                arg = doc.GetDisplayValue(arg);
                // Display float args as floats, especially in initializers
                if (doc.Type == 6 && arg is int g)
                {
                    arg = BitConverter.ToSingle(BitConverter.GetBytes(g), 0);
                }
                else if (doc.Type == 6 && arg is uint ug)
                {
                    arg = BitConverter.ToSingle(BitConverter.GetBytes(ug), 0);
                }
            }
            if (arg is float f)
            {
                // One possibility: adding a .0 for integer floats to show it's a float. This is a bit noisy though.
                arg = f.ToString(CultureInfo.InvariantCulture);
            }
            return arg.ToString();
        }

        public int ByteOffsetFromIndex(Instr instr, int index)
        {
            // This is ambiguous here - is it in the instruction or the args - so better to disallow it
            if (instr.Init) throw new Exception($"Byte index support not intended for initializations");
            if (instr.Doc == null || !funcBytePositions.TryGetValue(instr.Doc, out List<int> pos)) throw new Exception($"Unknown {instr}");
            if (index < 0 || index >= pos.Count - 1)
            {
                throw new Exception($"Invalid index {index} in {instr}\nOut of {string.Join(",", pos)}");
            }
            return pos[index];
        }

        public int GetInstructionLength(int bank, int id)
        {
            EMEDF.InstrDoc instrDoc = doc[bank][id];
            return funcBytePositions[instrDoc].Last();
        }

        public int IndexFromByteOffset(Instr instr, int offset)
        {
            if (instr.Doc == null || !funcBytePositions.TryGetValue(instr.Doc, out List<int> pos)) throw new Exception($"Unknown {instr}");
            int paramIndex = pos.IndexOf(offset);
            if (paramIndex == -1 || paramIndex >= pos.Count - 1)
            {
                throw new Exception($"Invalid offset {offset} in {instr}\nOut of {string.Join(",", pos)}");
            }
            return paramIndex;
        }

        public static bool IsTemp(int flag)
        {
            return (flag / 1000) % 10 == 5;
        }

        public EMEVD.Instruction CopyInstruction(EMEVD.Instruction i)
        {
            return i.Layer.HasValue ? new EMEVD.Instruction(i.Bank, i.ID, i.Layer.Value, i.ArgData) : new EMEVD.Instruction(i.Bank, i.ID, i.ArgData);
        }

        private static EMEVD.Parameter CopyParameter(EMEVD.Parameter p, int newIndex = -1)
        {
            return new EMEVD.Parameter(newIndex >= 0 ? newIndex : p.InstructionIndex, p.TargetStartByte, p.SourceStartByte, p.ByteCount);
        }

        public EMEVD.Event CopyEvent(EMEVD.Event src, int newId)
        {
            EMEVD.Event newEvent = new EMEVD.Event(newId, src.RestBehavior);
            if (src.Parameters.Count > 0)
            {
                newEvent.Parameters = src.Parameters.Select(p => CopyParameter(p)).ToList();
            }
            newEvent.Instructions = src.Instructions.Select(i => CopyInstruction(i)).ToList();
            return newEvent;
        }

        public Instr CopyInit(Instr instr, EMEVD.Event newEvent, OldParams pre = null)
        {
            return CopyInit(instr, newEvent == null ? -1 : (int)newEvent.ID, pre);
        }

        public Instr CopyInit(Instr instr, int newEventId, OldParams pre = null)
        {
            // Assume this is copying common id/map id to fresh map id
            Instr newInstr = Parse(CopyInstruction(instr.Val), pre);
            if (newInstr.Val.Bank == 2000)
            {
                if (newEventId < 0) throw new Exception($"Internal error: Event not provided for copying {string.Join(",", instr.ArgList)}");
                if (newInstr.Val.ID == 0)
                {
                    newInstr[0] = 0;
                    newInstr[1] = (uint)newEventId;
                }
                else if (newInstr.Val.ID == 6 && instr.Callee != newEventId)
                {
                    // Just create a brand new instruction if changing to a different id.
                    // Otherwise, the existing copied one should work.
                    List<object> args = newInstr.ArgList.ToList();
                    if (instr.Offset == 1)
                    {
                        args.Insert(0, 0);
                    }
                    args[1] = (uint)newEventId;
                    return Parse(new EMEVD.Instruction(2000, 0, args), pre);
                }
            }
            return newInstr;
        }

        // Preserving parameters after adding/removing instructions
        public class OldParams
        {
            private EMEVD.Event Event { get; set; }
            private List<EMEVD.Instruction> Original { get; set; }
            private Dictionary<EMEVD.Instruction, List<int>> OriginalIndices { get; set; }
            private Dictionary<EMEVD.Instruction, List<EMEVD.Parameter>> NewInstructions = new Dictionary<EMEVD.Instruction, List<EMEVD.Parameter>>();

            internal OldParams() { }

            // Creates a record of parameters with original instructions.
            // The parameters will be preserved if the parameterized instructions are still present by reference.
            // If using this system, parameters should not be modified manually.
            public static OldParams Preprocess(EMEVD.Event e)
            {
                // This is mainly suitable for existing events, not for creating new ones,
                // as we don't try to add params if none were there previously.
                if (e.Parameters.Count == 0) return new OldParams();
                return new OldParams
                {
                    Event = e,
                    Original = e.Instructions.ToList(),
                };
            }

            public static OldParams NewEvent(EMEVD.Event e)
            {
                return new OldParams
                {
                    Event = e,
                    Original = e.Instructions.ToList(),
                };
            }

            // Adds a never-before-seen paramterized instruction, and parameters to add later for it.
            // This should also work for existing instructions, in which case previous parmaeters will be deleted.
            public void AddParameters(EMEVD.Instruction instr, List<EMEVD.Parameter> ps)
            {
                if (ps != null && ps.Count > 0)
                {
                    NewInstructions[instr] = ps;
                }
            }

            // Get currently known args for the given instr (note, the InstructionIndex may be wrong)
            public List<EMEVD.Parameter> GetInstructionParams(EMEVD.Instruction ins)
            {
                if (NewInstructions.TryGetValue(ins, out List<EMEVD.Parameter> ps)) return ps;
                if (Original == null) return new List<EMEVD.Parameter>();
                if (OriginalIndices == null)
                {
                    OriginalIndices = ReverseIndices(Original);
                }
                if (!OriginalIndices.TryGetValue(ins, out List<int> oldIndices))
                {
                    return new List<EMEVD.Parameter>();
                }
                // Multiple instances is a weird case, but just use the first one, matching IndexOf behavior
                return Event.Parameters.Where(p => p.InstructionIndex == oldIndices[0]).ToList();
            }

            // Updates old indices and adds new indices
            public void Postprocess()
            {
                if (Event == null || (Event.Parameters.Count == 0 && NewInstructions.Count == 0)) return;
                Dictionary<EMEVD.Instruction, List<int>> currentIndices = ReverseIndices(Event.Instructions);
                List<EMEVD.Parameter> empty = new List<EMEVD.Parameter>();
                // Update old indices
                Event.Parameters = Event.Parameters.SelectMany(p =>
                {
                    EMEVD.Instruction originalInstr = Original[(int)p.InstructionIndex];
                    if (!NewInstructions.ContainsKey(originalInstr)
                        && currentIndices.TryGetValue(originalInstr, out List<int> indices))
                    {
                        return indices.Select(i => CopyParameter(p, i));
                    }
                    return empty;
                }).Where(p => p != null).ToList();
                // Add new indices not yet added
                foreach (KeyValuePair<EMEVD.Instruction, List<EMEVD.Parameter>> entry in NewInstructions)
                {
                    if (!currentIndices.TryGetValue(entry.Key, out List<int> indices)) continue;
                    foreach (EMEVD.Parameter p in entry.Value)
                    {
                        // This will be messy if the parameter is added independently, since it will probably error out above.
                        // As part of using this utility, parameters should not be modified manually.
                        // Either way, definitely update it here.
                        Event.Parameters.AddRange(indices.Select(i => CopyParameter(p, i)));
                    }
                }
                Original = Event.Instructions;
                OriginalIndices = null;
            }

            private static Dictionary<T, List<int>> ReverseIndices<T>(IList<T> list)
            {
                // Reverse mapping from instructions to indices, allowing for multiple entries
                // (otherwise, ToDictionary complains)
                return list
                    .Select((ins, i) => (ins, i))
                    .GroupBy(insIndex => insIndex.Item1)
                    .ToDictionary(e => e.Key, e => e.Select(insIndex => insIndex.Item2).ToList());
            }
        }

        public class EventValue
        {
            public EventValue(EventValueType Type, object ID)
            {
                this.Type = Type;
                this.ID = ID;
            }
            public EventValueType Type { get; set; }
            public object ID { get; set; }

            public int IntID
            {
                get
                {
                    if (ID is int id) return id;
                    if (ID is uint uid) return (int)uid;
                    throw new Exception($"Internal error: cannot extract int from {this}");
                }
            }

            public uint UIntID
            {
                get
                {
                    if (ID is int id) return (uint)id;
                    if (ID is uint uid) return uid;
                    throw new Exception($"Internal error: cannot extract uint from {this}");
                }
            }

            // Convenience functions for event-editing, which are currently mostly int-based
            public static EventValue Enemy(int id) => new EventValue(EventValueType.Enemy, id);
            public static EventValue Asset(int id) => new EventValue(EventValueType.Asset, id);
            public static EventValue Object(int id) => new EventValue(EventValueType.Object, id);
            public static EventValue Flag(int id) => new EventValue(EventValueType.Flag, id);
            public static EventValue Region(int id) => new EventValue(EventValueType.Region, id);
            public static EventValue Generator(int id) => new EventValue(EventValueType.Generator, id);
            public static EventValue Animation(int id) => new EventValue(EventValueType.Animation, id);
            public static EventValue NpcName(int id) => new EventValue(EventValueType.NpcName, id);
            // public static EventValue Unknown(int id) => new EventValue(EventValueType.Unknown, id);

            public override bool Equals(object obj) => obj is EventValue o && Equals(o);
            public bool Equals(EventValue o) => Type == o.Type && ID.Equals(o.ID);
            public override int GetHashCode() => Type.GetHashCode() ^ ID.GetHashCode();
            public override string ToString() => $"{Type.ToString().ToLowerInvariant()} {ID}";
        }

        public static readonly List<EventValueType> PartTypes = new List<EventValueType>
        {
            EventValueType.WarpPlayer, EventValueType.Asset, EventValueType.Object, EventValueType.Enemy, EventValueType.Collision,
        };
        // Ids which correspond to map objects
        public static readonly List<EventValueType> MapTypes =
            PartTypes.Concat(new[] { EventValueType.Region, EventValueType.Generator }).ToList();
        // Any addressable entity id, including meta ids
        public static readonly List<EventValueType> EntityTypes =
            MapTypes.Concat(new[] { EventValueType.Player, EventValueType.Entity, EventValueType.Boss }).ToList();
        public enum EventValueType
        {
            Unknown,
            Player,
            WarpPlayer,
            Asset,
            Object,
            Enemy,
            Collision,
            Region,
            Generator,
            // Custom values
            Flag,
            Speffect,
            Animation,
            NpcName,
            // Below are used for specific filtering logic, not as base ids
            // Short for any EntityTypes
            Entity,
            // Type of Enemy which is either a boss/helper
            Boss,
            // Anything at all, for blind edits
            All,
        }

        private static bool AreTypesCompatible(EventValueType a, EventValueType b)
        {
            if (a == EventValueType.All || b == EventValueType.All) return true;
            // While there are subcategories involved here, they all share the same entity id namespace
            if (EntityTypes.Contains(a) && EntityTypes.Contains(b)) return true;
            return a == b;
        }

        public bool IsArgCompatible(EMEDF.InstrDoc instrDoc, int index, EventValueType type)
        {
            if (funcValueTypes != null && type != EventValueType.All)
            {
                // Check for type compatibility, if that feature is enabled
                if (!funcValueTypes.TryGetValue(instrDoc, out Dictionary<int, EventValueType> types)) return false;
                List<int> offsets = funcBytePositions[instrDoc];
                if (index >= offsets.Count) return false;
                if (!types.TryGetValue(offsets[index], out EventValueType argType)) return false;
                if (!AreTypesCompatible(type, argType)) return false;
            }
            return true;
        }

        public enum SegmentState
        {
            // Initial state: waiting for starting instruction. Can transition to Prematch
            Before,
            // Initial state: waiting for starting instruction. Can transition to During
            BeforeMatchless,
            // Initial or intermediate state: waiting for applicable edit. Can transition to During
            Prematch,
            // In the segment
            During,
            // After ending instruction reached, if one is defined
            After
        }

        // Editing macros
        public class EventEdits
        {
            // Initial state before processing
            // All edits matched against instruction names
            public Dictionary<string, List<InstrEdit>> NameEdits { get; set; }
            // All edits matched against integer values
            public Dictionary<string, List<InstrEdit>> ArgEdits { get; set; }
            // All edits matched by instruction name + full arguments, with format depending on darkScriptMode
            public Dictionary<(string, string), List<InstrEdit>> NameArgEdits { get; set; }
            // All edits matched against segment start (SegmentAdd edits)
            public Dictionary<string, List<InstrEdit>> SegmentEdits { get; set; }

            // Filled in or updated during processing
            // Set of all edits, for the purpose of making sure all are applied
            public HashSet<InstrEdit> PendingEdits = new HashSet<InstrEdit>();
            // Set of all edits, to avoid repetition in some cases
            public HashSet<InstrEdit> EncounteredEdits = new HashSet<InstrEdit>();
            // InstrEdits to apply by line index. This is saved until the end and done in reverse order because it's index-based.
            public Dictionary<int, List<InstrEdit>> PendingAdds = new Dictionary<int, List<InstrEdit>>();
            // Starting indices of checked segments. TODO juse use PreSegment?
            // public Dictionary<string, int> SegmentChecks = new Dictionary<string, int>();
            // Segment tracking state, for edits which only apply during specific segments
            public Dictionary<string, SegmentState> SegmentStates = new Dictionary<string, SegmentState>();
        }

        // Returns all applicable edits
        public List<InstrEdit> GetMatches(EventEdits e, Instr instr)
        {
            if (!instr.Writeable) throw new Exception($"Internal error: instruction not searchable: {instr}");
            List<InstrEdit> nameEdit = null;
            if (instr.Name == null) return null;  // Can happen with removed instructions, nothing left to edit
            if (e.NameEdits != null
                && !e.NameEdits.TryGetValue(instr.Name, out nameEdit)
                && e.ArgEdits == null
                && e.NameArgEdits == null) return null;
            List<string> strArgs = instr.ArgList.Select((a, i) => instr.FormatArg(a, i)).ToList();
            List<InstrEdit> edits = new List<InstrEdit>();
            if (e.ArgEdits != null)
            {
                // ArgEdits may be dependent on type compatibility checks
                edits.AddRange(strArgs.SelectMany((s, i) =>
                {
                    if (e.ArgEdits.TryGetValue(s, out List<InstrEdit> argEdits))
                    {
                        return argEdits.Where(edit => IsArgCompatible(instr.Doc, i, edit.ValueType));
                    }
                    return new InstrEdit[] { };
                }));
            }
            if (nameEdit != null)
            {
                edits.AddRange(nameEdit);
            }
            if (e.NameArgEdits != null
                && e.NameArgEdits.TryGetValue((instr.Name, string.Join(",", strArgs)), out List<InstrEdit> nameArgEdit))
            {
                edits.AddRange(nameArgEdit);
            }
            return edits;
        }

        // Applies all edits that can be applied in place, and adds others later
        public void ApplyEdits(EventEdits e, Instr instr, int index)
        {
            List<InstrEdit> edits = GetMatches(e, instr);
            if (edits == null) return;

            // Either apply edits or return them back
            void beginSegment(string segment, int addIndex)
            {
                e.SegmentStates[segment] = SegmentState.During;
                // This may prompt some additions
                if (e.SegmentEdits != null && e.SegmentEdits.TryGetValue(segment, out List<InstrEdit> segEdits))
                {
                    foreach (InstrEdit segEdit in segEdits)
                    {
                        if (segEdit.Type == EditType.SegmentAdd)
                        {
                            AddMulti(e.PendingAdds, addIndex, segEdit);
                        }
                        else if (segEdit.Type == EditType.SegmentCheck)
                        {
                            // Nothing to do, the edit is marked as applied
                            e.PendingEdits.Remove(segEdit);
                        }
                        else
                        {
                            throw new Exception($"Internal error: segment edits not supported for {segEdit}");
                        }
                    }
                }
            }
            bool segmentAllowed(string segment)
            {
                if (segment == null) return true;
                SegmentState state = e.SegmentStates[segment];
                return state != SegmentState.Before && state != SegmentState.After;
            }
            bool removed = false;
            foreach (InstrEdit edit in edits.OrderBy(x => x.Type))
            {
                // "Apply once" edits apply uniquely to their own command
                if (edit.ApplyOnce && (e.EncounteredEdits.Contains(edit) || removed))
                {
                    continue;
                }
                if (edit.Type == EditType.StartSegment)
                {
                    // Console.WriteLine($"Start segment {edit.Segment} in state {e.SegmentStates[edit.Segment]}: {instr}");
                    // if (edit.PreSegment != null) Console.WriteLine($"  Presegment {edit.PreSegment} in state {e.SegmentStates[edit.PreSegment]}");
                    if (edit.PreSegment != null && e.SegmentStates[edit.PreSegment] < SegmentState.During)
                    {
                        // Console.WriteLine("  (skipped)");
                        continue;
                    }
                    if (e.SegmentStates[edit.Segment] == SegmentState.Before)
                    {
                        e.SegmentStates[edit.Segment] = SegmentState.Prematch;
                    }
                    else if (e.SegmentStates[edit.Segment] == SegmentState.BeforeMatchless)
                    {
                        beginSegment(edit.Segment, index + 1);
                    }
                }
                else if (edit.Type == EditType.EndSegment)
                {
                    if (e.SegmentStates[edit.Segment] == SegmentState.During
                        || e.SegmentStates[edit.Segment] == SegmentState.Prematch)
                    {
                        e.SegmentStates[edit.Segment] = SegmentState.After;
                    }
                }
                else if (edit.Type == EditType.Remove && segmentAllowed(edit.Segment))
                {
                    // For now, use inplace remove, a bit less messy
                    instr.Val = new EMEVD.Instruction(1014, 69);
                    instr.Init = false;
                    instr.Doc = null;
                    instr.ArgList.Clear();
                    instr.Types.Clear();
                    removed = true;
                }
                // Segments can be dynamically started from these two types, at the moment
                if (edit.Type == EditType.Remove || edit.Type == EditType.MatchSegment)
                {
                    if (edit.Segment != null && e.SegmentStates[edit.Segment] == SegmentState.Prematch)
                    {
                        beginSegment(edit.Segment, index);
                    }
                }
                if (edit.Add != null)
                {
                    // Do it later to keep indices consistent
                    AddMulti(e.PendingAdds, index, edit);
                }
                if (!removed)
                {
                    if (edit.PosEdit != null)
                    {
                        foreach (KeyValuePair<int, string> pos in edit.PosEdit)
                        {
                            instr[pos.Key] = pos.Value;
                        }
                    }
                    if (edit.ValEdit != null)
                    {
                        for (int i = 0; i < instr.ArgList.Count; i++)
                        {
                            if (edit.ValEdit.TryGetValue(instr.FormatArg(instr[i], i), out string replace))
                            {
                                instr[i] = replace;
                            }
                        }
                    }
                }
                // This edit is accounted for, unless it's an Add, in which case it will be done later
                e.EncounteredEdits.Add(edit);
                if (edit.Add == null)
                {
                    e.PendingEdits.Remove(edit);
                }
            }
        }

        public void AddEdit(EventEdits e, string toFind, InstrEdit edit)
        {
            if (edit.Type == EditType.None)
            {
                throw new Exception($"Invalid InstrEdit {edit}");
            }
            if (edit.Segment != null && !e.SegmentStates.ContainsKey(edit.Segment))
            {
                throw new Exception($"Internal error: Segment {edit.Segment} not found in [{string.Join(", ", e.SegmentStates.Keys)}]");
            }
            if (int.TryParse(toFind, out var _))
            {
                if (e.ArgEdits == null) e.ArgEdits = new Dictionary<string, List<InstrEdit>>();
                AddMulti(e.ArgEdits, toFind, edit);
            }
            else if (docByName.ContainsKey(toFind))
            {
                // If this isn't a name, it will come up later as an unused pending edit
                if (e.NameEdits == null) e.NameEdits = new Dictionary<string, List<InstrEdit>>();
                AddMulti(e.NameEdits, toFind, edit);
            }
            // Perhaps have a more coherent way of doing this, but use this naming convention for now
            else if (toFind.Contains("segment"))
            {
                if (e.SegmentEdits == null) e.SegmentEdits = new Dictionary<string, List<InstrEdit>>();
                AddMulti(e.SegmentEdits, toFind, edit);
            }
            else
            {
                (string cmd, List<string> addArgs) = ParseCommandString(toFind);
                SimplifyCommandArgs(addArgs);
                if (e.NameArgEdits == null) e.NameArgEdits = new Dictionary<(string, string), List<InstrEdit>>();
                AddMulti(e.NameArgEdits, (cmd, string.Join(",", addArgs)), edit);
            }
            if (!edit.Optional)
            {
                e.PendingEdits.Add(edit);
            }
        }

        public void AddReplace(EventEdits e, string toFind, string toVal = null, EventValueType type = EventValueType.All)
        {
            if (toVal != null || Regex.IsMatch(toFind, @"^[\d.]+\s*->\s*[\d.]+$"))
            {
                // Replace any value with any other value
                string[] parts = toVal == null ? Regex.Split(toFind, @"\s*->\s*") : new[] { toFind, toVal };
                InstrEdit edit = new InstrEdit
                {
                    SearchInfo = toFind,
                    Type = EditType.Replace,
                    ValueType = type,
                    ValEdit = new Dictionary<string, string> { { parts[0], parts[1] } },
                };
                if (e.ArgEdits == null) e.ArgEdits = new Dictionary<string, List<InstrEdit>>();
                AddMulti(e.ArgEdits, parts[0], edit);
                e.PendingEdits.Add(edit);  // Currently cannot be optional
            }
            else
            {
                if (toVal != null) throw new Exception();
                (string cmd, List<string> addArgs) = ParseCommandString(toFind);
                SimplifyCommandArgs(addArgs);
                InstrEdit edit = new InstrEdit
                {
                    SearchInfo = toFind,
                    Type = EditType.Replace,
                    PosEdit = new Dictionary<int, string>(),
                };
                for (int i = 0; i < addArgs.Count; i++)
                {
                    if (addArgs[i].Contains("->"))
                    {
                        string[] parts = Regex.Split(addArgs[i], @"\s*->\s*");
                        addArgs[i] = parts[0];
                        edit.PosEdit[i] = parts[1];
                    }
                }
                if (e.NameArgEdits == null) e.NameArgEdits = new Dictionary<(string, string), List<InstrEdit>>();
                AddMulti(e.NameArgEdits, (cmd, string.Join(",", addArgs)), edit);
                e.PendingEdits.Add(edit);  // Currently cannot be optional
            }
        }

        public enum EditType
        {
            // Various edit types. These are applied in enum order for a given instruction.
            // Should not be used
            None,
            // Ends matching for segment-based removals
            EndSegment,
            // Removes matching commands
            Remove,
            // Passively recognizes matching commands for segment starts
            MatchSegment,
            // Adds at the matching index, either before or after
            AddAfter, AddBefore,
            // Adds after the segment-matching index
            SegmentAdd,
            // Passively requires being in the segment to match (fails if the segment is never started)
            SegmentCheck,
            // Replaces values within the matching instruction
            Replace,
            // Marks the start index of a segment, if an explicit instruction is given for it
            StartSegment,
        }

        // Edits to apply based on a matching line
        public class InstrEdit
        {
            // Matching info, for debug purposes only. (TODO could also make this authoritative, and not a string)
            public string SearchInfo { get; set; }
            // Edit type
            public EditType Type { get; set; }
            // If an arg edit, the restriction on the matching value
            public EventValueType ValueType = EventValueType.All;
            // The segment name, for StartSegment/EndSegment
            public string Segment { get; set; }
            // A segment which must already be active for this one to activate,
            // because of course there are duplicate labels/gotos/main conditions etc.
            public string PreSegment { get; set; }
            // The instruction to add, for AddAfter/AddBefore/SegmentAdd. TODO could also do Replace I guess?
            // The main challenge here is that addition is done in a later pass, vs replace is in-place.
            public EMEVD.Instruction Add { get; set; }
            // Parameters for the new instruction. InstructionIndex is filled in by OldParams during postprocessing
            public List<EMEVD.Parameter> AddParams { get; set; }
            // If an instruction argument edit, the args to edit (index -> value)
            public Dictionary<int, string> PosEdit { get; set; }
            // If an instruction value edit, the values to replace (value -> value)
            public Dictionary<string, string> ValEdit { get; set; }
            // Whether this edit not being applied is an error
            public bool Optional { get; set; }
            // Whether this edit should only be applied the first time
            public bool ApplyOnce { get; set; }

            public override string ToString() => $"{SearchInfo} [{Type}]"
                + (Optional ? "[Optional]" : "")
                + (ValueType == EventValueType.All ? "" : $"[ValueType:{ValueType}]")
                + (Add == null ? "" : $"[Add:{Add.Bank}_{Add.ID}]")
                + (AddParams == null ? "" : $"[Params:{AddParams.Count}]")
                + (PosEdit == null ? "" : $"[Set:{string.Join(",", PosEdit)}]")
                + (ValEdit == null ? "" : $"[Replace:{string.Join(", ", ValEdit)}]");
        }

        public void ApplyAllEdits(EMEVD.Event ev, EventEdits edits)
        {
            OldParams pre = OldParams.Preprocess(ev);
            for (int j = 0; j < ev.Instructions.Count; j++)
            {
                Instr instr = Parse(ev.Instructions[j], pre);
                // if (instr.Init) continue;
                ApplyEdits(edits, instr, j);
                instr.Save(pre);
                ev.Instructions[j] = instr.Val;
            }
            ApplyAdds(edits, ev);
            pre.Postprocess();
        }

        public void ApplyAdds(EventEdits edits, EMEVD.Event e, OldParams oldParams = null)
        {
            // Add all commands in reverse order, to preserve indices
            foreach (KeyValuePair<int, List<InstrEdit>> lineEdit in edits.PendingAdds.OrderByDescending(item => item.Key))
            {
                if (lineEdit.Key == -1)
                {
                    // At the end. This is not being inserted repeatedly at an index, so re-reverse the order back to normal
                    foreach (InstrEdit addEdit in lineEdit.Value)
                    {
                        e.Instructions.Add(addEdit.Add);
                        if (addEdit.AddParams != null)
                        {
                            if (oldParams == null) throw new ArgumentException($"Can't add instruction with parameters if old params cannot be added in {edits}");
                            oldParams.AddParameters(addEdit.Add, addEdit.AddParams);
                        }
                        edits.PendingEdits.Remove(addEdit);
                    }
                    continue;
                }
                // Repeatedly inserting at the same index will produce a reverse order from the list
                foreach (InstrEdit addEdit in Enumerable.Reverse(lineEdit.Value))
                {
                    if (addEdit.Add != null && addEdit.Type == EditType.AddAfter)
                    {
                        e.Instructions.Insert(lineEdit.Key + 1, addEdit.Add);
                        if (addEdit.AddParams != null)
                        {
                            if (oldParams == null) throw new ArgumentException($"Can't add instruction with parameters if old params cannot be added in {edits}");
                            oldParams.AddParameters(addEdit.Add, addEdit.AddParams);
                        }
                        edits.PendingEdits.Remove(addEdit);
                    }
                }
                foreach (InstrEdit addEdit in Enumerable.Reverse(lineEdit.Value))
                {
                    if (addEdit.Add != null && addEdit.Type != EditType.AddAfter)
                    {
                        e.Instructions.Insert(lineEdit.Key, addEdit.Add);
                        if (addEdit.AddParams != null)
                        {
                            if (oldParams == null) throw new ArgumentException($"Can't add instruction with parameters if old params cannot be added in {edits}");
                            oldParams.AddParameters(addEdit.Add, addEdit.AddParams);
                        }
                        edits.PendingEdits.Remove(addEdit);
                    }
                }
            }
        }

        public void RegisterSegment(
            EventEdits edits, string name,
            string startCmd, string endCmd, bool ignoreMatch, string preSegment)
        {
            if (edits.SegmentStates.ContainsKey(name)) throw new Exception($"Segment {name} already defined");
            if (startCmd == null)
            {
                // Without an explicit start, rely solely on matching the first edit in the segment
                edits.SegmentStates[name] = SegmentState.Prematch;
            }
            else
            {
                // With an explicit start and ignoring edits, use BeforeMatchless (immediately starts segment)
                // Otherwise, if relying on edits, use Before which transitions to Prematch
                edits.SegmentStates[name] = ignoreMatch ? SegmentState.BeforeMatchless : SegmentState.Before;
                InstrEdit start = new InstrEdit
                {
                    SearchInfo = startCmd,
                    Type = EditType.StartSegment,
                    Segment = name,
                    PreSegment = preSegment,
                };
                AddEdit(edits, startCmd, start);
            }
            if (endCmd != null)
            {
                InstrEdit end = new InstrEdit
                {
                    SearchInfo = endCmd,
                    Type = EditType.EndSegment,
                    Segment = name,
                };
                AddEdit(edits, endCmd, end);
            }
        }

        public void CheckSegment(EventEdits edits, string name, string checkPrevSegment)
        {
            InstrEdit checkEdit = new InstrEdit
            {
                SearchInfo = name,
                Segment = name,
                Type = EditType.SegmentCheck,
                PreSegment = checkPrevSegment,
            };
            AddEdit(edits, name, checkEdit);
        }

        public void AddMacro(EventEdits edits, List<EventAddCommand> adds)
        {
            foreach (EventAddCommand add in adds)
            {
                if (add.Cmd != null)
                {
                    AddMacroCmd(edits, add, add.Cmd);
                }
                if (add.Cmds != null)
                {
                    foreach (string cmd in Decomment(add.Cmds))
                    {
                        AddMacroCmd(edits, add, cmd);
                    }
                }
            }
        }

        private void AddMacroCmd(EventEdits edits, EventAddCommand add, string cmd)
        {
            if (add.Before == null)
            {
                AddMacro(edits, EditType.AddAfter, cmd, add.After);
            }
            else
            {
                AddMacro(edits, EditType.AddBefore, cmd, add.Before == "start" ? null : add.Before);
            }
        }

        [Obsolete]
        public void AddMacro(EventEdits edits, string toFind, bool addAfter, string add)
        {
            AddMacro(edits, addAfter ? EditType.AddAfter : EditType.AddBefore, add, toFind);
        }

        public void AddMacro(
            EventEdits edits, EditType editType, string add,
            string toFind = null, bool applyOnce = false, EventValueType type = EventValueType.All)
        {
            EMEVD.Instruction instr;
            List<EMEVD.Parameter> ps = null;
            if (add.Contains("X"))
            {
                (instr, ps) = ParseAddArg(add, 0);
                if (ps.Count == 0) ps = null;
            }
            else
            {
                instr = ParseAdd(add);
            }
            InstrEdit edit = new InstrEdit
            {
                SearchInfo = toFind,
                Add = instr,
                AddParams = ps,
                Type = editType,
                ValueType = type,
                ApplyOnce = applyOnce,
            };
            if (editType == EditType.SegmentAdd)
            {
                edit.Segment = toFind;
            }
            if (toFind == null)
            {
                edits.PendingEdits.Add(edit);
                AddMulti(edits.PendingAdds, editType == EditType.AddAfter ? -1 : 0, edit);
            }
            else
            {
                AddEdit(edits, toFind, edit);
            }
        }

        public void RemoveMacro(EventEdits edits, string toFind, bool applyOnce = false, EventValueType type = EventValueType.All)
        {
            AddEdit(edits, toFind, new InstrEdit
            {
                SearchInfo = toFind,
                Type = EditType.Remove,
                ValueType = type,
                ApplyOnce = applyOnce,
            });
        }

        public void RemoveSegmentMacro(EventEdits edits, string segment, string toFind)
        {
            AddEdit(edits, toFind, new InstrEdit
            {
                SearchInfo = toFind,
                Type = EditType.Remove,
                Segment = segment,
                Optional = true,
            });
        }

        public void MatchSegmentMacro(EventEdits edits, string segment, string toFind)
        {
            AddEdit(edits, toFind, new InstrEdit
            {
                SearchInfo = toFind,
                Type = EditType.MatchSegment,
                Segment = segment,
                Optional = true,
            });
        }

        public void ReplaceMacro(EventEdits edits, List<EventReplaceCommand> replaces)
        {
            foreach (EventReplaceCommand replace in replaces)
            {
                EventValueType type = replace.Type;
                if (type == EventValueType.Unknown) type = EventValueType.All;
                ReplaceMacro(edits, replace.From, replace.To, type);
            }
        }

        public void ReplaceMacro(EventEdits edits, string toFind, string toVal = null, EventValueType type = EventValueType.All)
        {
            // This case includes an add/remove, so it cannot be done in the edit itself. TODO simplify this split
            if ((toVal != null && toVal.Contains("(")) || Regex.IsMatch(toFind, @"->.*\("))
            {
                // Replace a full command with another full command
                string[] parts = toVal == null ? Regex.Split(toFind, @"\s*->\s*") : new[] { toFind, toVal };
                RemoveMacro(edits, parts[0]);
                AddMacro(edits, EditType.AddAfter, parts[1], parts[0], type: type);
            }
            else
            {
                AddReplace(edits, toFind, toVal, type: type);
            }
        }

        // Simple mass rewrite: just int replacements, for entity ids which are generally unambiguous
        public void RewriteInts(Instr instr, Dictionary<int, int> changes)
        {
            for (int i = 0; i < instr.ArgList.Count; i++)
            {
                if (instr.ArgList[i] is int ik && changes.TryGetValue(ik, out int val))
                {
                    instr[i] = val;
                }
                else if (instr.ArgList[i] is uint uk && changes.TryGetValue((int)uk, out val))
                {
                    instr[i] = (uint)val;
                }
            }
        }

        // Type-aware rewrite
        private IEnumerable<EventValue> GetMatchingValues(Dictionary<EventValue, EventValue> changes, object arg)
        {
            // Assuming the args are compatible. Use string comparison for this, but it's intended for int types
            string argStr = arg.ToString();
            return changes.Where(e => e.Key.ID.ToString() == argStr).Select(e => e.Value);
        }

        public void RewriteInitInts(
            Instr instr,
            Dictionary<EventValue, EventValue> changes,
            Dictionary<int, EventValueType> allowedTypes = null)
        {
            if (!instr.Init) throw new Exception($"Internal error: can't call method for non-init {instr}");
            for (int i = instr.Offset; i < instr.ArgList.Count; i++)
            {
                object arg = instr.ArgList[i];
                EventValueType type = EventValueType.All;
                if (allowedTypes != null && allowedTypes.TryGetValue(i - instr.Offset, out EventValueType argType))
                {
                    type = argType;
                }
                foreach (EventValue value in GetMatchingValues(changes, arg))
                {
                    if (AreTypesCompatible(value.Type, type))
                    {
                        if (arg is int ik)
                        {
                            instr[i] = value.IntID;
                        }
                        else if (arg is uint uk)
                        {
                            instr[i] = value.UIntID;
                        }
                        else throw new Exception($"Error: Unknown type {arg.GetType()} in arg {i} of {instr}");
                        break;
                    }
                }
            }
        }

        public void RewriteInts(Instr instr, Dictionary<EventValue, EventValue> changes)
        {
            for (int i = 0; i < instr.ArgList.Count; i++)
            {
                object arg = instr.ArgList[i];
                foreach (EventValue value in GetMatchingValues(changes, arg))
                {
                    if (instr.Init || IsArgCompatible(instr.Doc, i, value.Type))
                    {
                        if (arg is int ik)
                        {
                            instr[i] = value.IntID;
                        }
                        else if (arg is uint uk)
                        {
                            instr[i] = value.UIntID;
                        }
                        else throw new Exception($"Error: Unknown type {arg.GetType()} in arg {i} of {instr}");
                        break;
                    }
                }
            }
        }

        public string RewriteInts(string add, Dictionary<EventValue, EventValue> changes)
        {
            (string cmd, List<string> addArgs) = ParseCommandString(add);
            if (!docByName.TryGetValue(cmd, out (int, int) docId)) throw new Exception($"Unrecognized command '{cmd}' in {add}");
            EMEDF.InstrDoc addDoc = doc[docId.Item1][docId.Item2];
            for (int i = 0; i < addArgs.Count; i++)
            {
                foreach (EventValue value in GetMatchingValues(changes, addArgs[i]))
                {
                    if (IsArgCompatible(addDoc, i, value.Type))
                    {
                        addArgs[i] = value.ID.ToString();
                        break;
                    }
                }
            }
            return $"{cmd}{(darkScriptMode ? "" : " ")}({string.Join(", ", addArgs)})";
        }

        public static (string, List<string>) ParseCommandString(string add)
        {
            int sparen = add.LastIndexOf('(');
            int eparen = add.LastIndexOf(')');
            if (sparen == -1 || eparen == -1) throw new Exception($"Bad command string {add}");
            string cmd = add.Substring(0, sparen).Trim();
            return (
                cmd,
                add.Substring(sparen + 1, eparen - sparen - 1).Split(',')
                    .Select(arg => arg.Trim())
                    .Where(arg => !string.IsNullOrWhiteSpace(arg))
                    .ToList());
        }

        private void SimplifyCommandArgs(List<string> args)
        {
            if (!liteMode) return;
            for (int i = 0; i < args.Count; i++)
            {
                if (enumByName.TryGetValue(args[i], out int val))
                {
                    args[i] = val.ToString();
                }
            }
        }

        private static readonly Regex CommentRe = new Regex(@"//.*");
        public List<string> Decomment(List<string> cmds)
        {
            if (cmds == null) return null;
            return cmds
                .Select(c => CommentRe.Replace(c, "").Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();
        }

        public bool TryGetInstructionId(string cmd, out (int, int) id)
        {
            // Hide this dictionary internally still for now
            return docByName.TryGetValue(cmd, out id);
        }

        // Parse a command so it can be added. Does not support parameters.
        public EMEVD.Instruction ParseAdd(string add)
        {
            (string cmd, List<string> addArgs) = ParseCommandString(add);
            if (!docByName.TryGetValue(cmd, out (int, int) docId)) throw new Exception($"Unrecognized command '{cmd}' in {add}");
            EMEDF.InstrDoc addDoc = doc[docId.Item1][docId.Item2];
            bool isInit = docId.Item1 == 2000 && (docId.Item2 == 0 || docId.Item2 == 6);
            List<ArgType> argTypes = isInit
                ? Enumerable.Repeat(ArgType.Int32, addArgs.Count).ToList()
                : addDoc.Arguments.Select(arg => arg.Type == 8 ? ArgType.UInt32 : (ArgType)arg.Type).ToList();
            if (addArgs.Count != argTypes.Count) throw new Exception($"Expected {argTypes.Count} arguments for {cmd}, given {addArgs.Count} in {add}");
            try
            {
                return new EMEVD.Instruction(docId.Item1, docId.Item2, addArgs.Select((a, j) => ParseArgWithEnum(a, argTypes[j])));
            }
            catch (FormatException)
            {
                throw new Exception($"Bad arguments in {add}");
            }
        }

        // Parse a command so it can be added, possibly with parameters. Does not support initializations.
        // addIndex should be filled in later by OldParams, if not given here.
        public (EMEVD.Instruction, List<EMEVD.Parameter>) ParseAddArg(string add, int addIndex = 0)
        {
            // This is duplicated from other command, but this one also does fancier parsing, so whatever, just keep them in sync.
            (string cmd, List<string> addArgs) = ParseCommandString(add);
            if (!docByName.TryGetValue(cmd, out (int, int) docId)) throw new Exception($"Unrecognized command '{cmd}' in {add}");
            EMEDF.InstrDoc addDoc = doc[docId.Item1][docId.Item2];
            List<ArgType> argTypes = addDoc.Arguments.Select(arg => arg.Type == 8 ? ArgType.UInt32 : (ArgType)arg.Type).ToList();
            if (addArgs.Count != argTypes.Count) throw new Exception($"Expected {argTypes.Count} arguments for {cmd}, given {addArgs.Count} in {add}");
            List<int> paramOffsets = funcBytePositions.TryGetValue(addDoc, out var val) ? val : null;
            List<object> args = new List<object>();
            List<EMEVD.Parameter> ps = new List<EMEVD.Parameter>();
            for (int i = 0; i < addArgs.Count; i++)
            {
                string arg = addArgs[i];
                if (arg.StartsWith("X"))
                {
                    List<int> pos = arg.Substring(1).Split('_').Select(pv => int.Parse(pv)).ToList();
                    if (paramOffsets == null || i >= paramOffsets.Count)
                    {
                        throw new Exception($"Can't substitute at pos {i} in {add} (found indices [{(paramOffsets == null ? "none" : string.Join(",", paramOffsets))})])");
                    }
                    if (pos.Count != 2) throw new Exception($"Invalid parameter format at pos {i} of {add}");
                    EMEVD.Parameter p = new EMEVD.Parameter(addIndex, paramOffsets[i], pos[0], pos[1]);
                    ps.Add(p);
                    args.Add(ParseArgWithEnum("0", argTypes[i]));
                }
                else
                {
                    args.Add(ParseArgWithEnum(arg, argTypes[i]));
                }
            }
            return (new EMEVD.Instruction(docId.Item1, docId.Item2, args), ps);
        }

        // Condition rewriting
        public List<int> FindCond(EMEVD.Event e, string req, OldParams pre = null)
        {
            List<int> cond = new List<int>();
            bool isGroup = int.TryParse(req, out int _);
            for (int i = 0; i < e.Instructions.Count; i++)
            {
                Instr instr = Parse(e.Instructions[i], pre);
                // IF (condition group) instruction, adding to the condition
                if (isGroup && instr.Val.Bank < 1000 && instr[0].ToString() == req)
                {
                    cond.Add(i);
                    continue;
                }
                // IfConditionGroup instruction, so the last instruction is the place where the group is used
                else if (isGroup && instr.Val.Bank == 0 && instr.Val.ID == 0 && instr[2].ToString() == req)
                {
                    cond.Add(i);
                    return cond;
                }
                // Just a single group-less instruction
                else if (!isGroup && instr.Name == req && instr[0].ToString() == "0")
                {
                    cond.Add(i);
                    return cond;
                }
            }
            throw new Exception($"Couldn't find ending condition '{req}', group {isGroup}, in event {e.ID}");
        }

        public List<EMEVD.Instruction> RewriteCondGroup(
            List<EMEVD.Instruction> after, Dictionary<int, int> reloc, int target, OldParams pre = null)
        {
            sbyte targetCond = (sbyte)target;
            sbyte sourceCond = 0;
            return after.Select(afterInstr =>
            {
                Instr instr = Parse(CopyInstruction(afterInstr));
                // IfConditionGroup
                if (instr.Val.ID == 0 && instr.Val.Bank == 0)
                {
                    if (sourceCond == 0) throw new Exception($"Internal error: can't infer condition group for {instr}");
                    instr[0] = targetCond;
                    instr[2] = (sbyte)(sourceCond > 0 ? 12 : -12);
                }
                else
                {
                    if (sourceCond == 0)
                    {
                        sourceCond = (sbyte)instr[0];
                    }
                    // This is way too hacky... can add more semantic info if it becomes fragile
                    instr[0] = after.Count == 1 ? targetCond : (sbyte)(sourceCond > 0 ? 12 : -12);
                }
                RewriteInts(instr, reloc);
                instr.Save();
                return instr.Val;
            }).ToList();
        }

        // Pretty sure this does not require object context. Consider adding [Obsolete]
        public bool ParseArgSpec(string arg, out int pos)
        {
            return TryArgSpec(arg, out pos);
        }

        public static bool TryArgSpec(string arg, out int pos)
        {
            // For event initializations with int args specified as X0, X4, X8, etc., return the arg position, e.g. 0, 1, 2
            pos = 0;
            if (arg == null) return false;
            // I guess can also handle cases like X0_4
            if (arg.Contains('_'))
            {
                arg = arg.Split('_')[0];
            }
            if (arg.StartsWith("X") && int.TryParse(arg.Substring(1), out pos) && pos >= 0)
            {
                if (pos % 4 != 0) return false;
                pos /= 4;
                return true;
            }
            return false;
        }

        public static bool TryFullArgSpec(string arg, out int offset, out int len)
        {
            // Only args formatted like X0_4
            offset = 0;
            len = 0;
            if (arg == null || !arg.StartsWith("X") || !arg.Contains('_')) return false;
            string[] parts = arg.Split('_');
            if (parts.Length != 2) return false;
            return int.TryParse(parts[0].Substring(1), out offset) && int.TryParse(parts[1], out len);
        }

        // Code for producing event configs
        public class EventKey : IComparable<EventKey>
        {
            public EventKey(int ID, string Map, int FileOrder = -1)
            {
                this.ID = ID;
                this.Map = Map;
                this.FileOrder = FileOrder;
            }
            public int ID { get; set; }
            // Nullable for legacy reaons
            public string Map { get; set; }
            // Optional field for sort only. Requires some care to not get lost in e.g. dictionary key copies
            public int FileOrder { get; set; }

            public override bool Equals(object obj) => obj is EventKey o && Equals(o);
            public bool Equals(EventKey o) => ID == o.ID && Map == o.Map;
            public override int GetHashCode() => ID.GetHashCode() ^ (Map == null ? 0 : Map.GetHashCode());
            public int CompareTo(EventKey o)
            {
                int cmp = Map == null ? (o.Map == null ? 0 : -1) : Map.CompareTo(o.Map);
                if (cmp != 0)
                {
                    // Make common_func always last
                    if (Map == "common_func") return 1;
                    if (o.Map == "common_func") return -1;
                    return cmp;
                }
                if (FileOrder >= 0 || o.FileOrder >= 0)
                {
                    return FileOrder.CompareTo(o.FileOrder);
                }
                return ID.CompareTo(o.ID);
            }
            public override string ToString() => ID + (Map == null ? "" : $" in {Map}");
            public static EventKey Parse(string str)
            {
                string[] parts = str.Split(' ');
                if (!int.TryParse(parts[0], out int id)) throw new ArgumentException($"Invalid event id in {str}");
                if (parts.Length == 1)
                {
                    return new EventKey(id, null);
                }
                if (parts.Length != 3 || parts[1] != "in") throw new ArgumentException($"Invalid map format in {str}");
                return new EventKey(id, parts[2]);
            }
        }

        public class InstructionValueSpec
        {
            public int Bank { get; set; }
            public int ID { get; set; }
            public int Length { get; set; }
            public string Alias { get; set; }
            public Dictionary<int, EventValueType> Args { get; set; }
        }

        public abstract class AbstractEventSpec
        {
            public int ID { get; set; }
            public string Map { get; set; }
            public string Comment { get; set; }
            public List<string> DebugInfo { get; set; }
            public List<string> DebugInit { get; set; }
            public List<string> DebugCommands { get; set; }
            public List<string> DebugOtherInits { get; set; }

            private EventKey key;
            public EventKey Key
            {
                get
                {
                    if (key == null) key = new EventKey(ID, Map);
                    return key;
                }
            }
        }

        public class EventAddCommand
        {
            public string Cmd { get; set; }
            public List<string> Cmds { get; set; }
            public string Before { get; set; }
            public string After { get; set; }

            public EventAddCommand DeepCopy()
            {
                EventAddCommand o = (EventAddCommand)MemberwiseClone();
                if (o.Cmds != null) o.Cmds = o.Cmds.ToList();
                return o;
            }
        }

        public class EventReplaceCommand
        {
            public string From { get; set; }
            public string To { get; set; }
            public EventValueType Type { get; set; }

            public EventReplaceCommand DeepCopy() => (EventReplaceCommand)MemberwiseClone();
        }

        public class EventAny<T, D> where D : InstructionAny<T>
        {
            public int Event { get; set; }
            public bool Highlight { get; set; }
            public bool HighlightInstr { get; set; }
            public bool UsesParameters { get; set; }
            public List<T> IDs = new List<T>();
            public List<T> CallerIDs = new List<T>();
            public IEnumerable<T> AllIDs => IDs.Concat(CallerIDs);
            public List<D> Callers = new List<D>();
            public List<D> Instructions = new List<D>();
        }

        public class InstructionAny<T>
        {
            public int Event { get; set; }
            public string Name { get; set; }
            public Instr Val { get; set; }
            public List<string> Args = new List<string>();
            public HashSet<int> HighlightArgs = new HashSet<int>();
            public List<T> IDs = new List<T>();
            public string Space = "";
            // Deprecated
            public InstructionDebug Caller { get; set; }
            public InstructionDebug Copy()
            {
                return (InstructionDebug)MemberwiseClone();
            }
            public string CallString(bool highlight = true) =>
                $"{Name}{Space}({string.Join(", ", Args.Select((a, i) => highlight && HighlightArgs.Contains(i) ? $"{a}*" : a))})";
            public override string ToString() => Caller == null ? $"[Event {Event}] {CallString()}" : $"{Caller.CallString()} - {CallString()}";
        }

        public class EventDebug : EventAny<int, InstructionDebug> { }
        public class InstructionDebug : InstructionAny<int> { }

        public SortedDictionary<EventKey, E> GetCommandHighlightedEvents<E, I, T>(
            Dictionary<string, EMEVD> emevds,
            // Given a non-init instruction, return instruction arg index + ids associated with it
            Func<Instr, List<(int, T)>> getInstrValues,
            // Given an init instruction and the callee, return param index + ids associated with it
            Func<Instr, E, List<(int, T)>> getInitValues,
            // Whether to highlight a returned value. At least one per event is needed to display it in the config output.
            Predicate<T> highlightValue,
            // Whether to always highlight a certain instruction, for the purpose of custom investigations
            Predicate<Instr> alwaysHighlight = null)
            where I : InstructionAny<T>, new()
            where E : EventAny<T, I>, new()
        {
            SortedDictionary<EventKey, E> eventInfos = new SortedDictionary<EventKey, E>();
            // Map from (event ID, X# parameter) -> instructions that use that parameter. For annotating instructions with highlight args and IDs
            Dictionary<(EventKey, int), List<I>> argCommands = new Dictionary<(EventKey, int), List<I>>();
            // Map from (event ID, X# parameter) -> any ArgDoc which uses that command, to render floats, enums, etc.
            Dictionary<(EventKey, int), EMEDF.ArgDoc> argDocs = new Dictionary<(EventKey, int), EMEDF.ArgDoc>();

            // Go through non-init instructions and identify all values
            // For blind number scan, this ignores parameter values
            // For typed scan, this returns parameter values with string arg ids
            foreach (KeyValuePair<string, EMEVD> entry in emevds)
            {
                foreach (EMEVD.Event e in entry.Value.Events)
                {
                    EventKey key = new EventKey((int)e.ID, entry.Key);
                    E eventInfo = new E { Event = (int)e.ID, UsesParameters = e.Parameters.Count > 0 };
                    eventInfos[key] = eventInfo;
                    for (int i = 0; i < e.Instructions.Count; i++)
                    {
                        // TODO: Can paramAwareMode be used to simplify this?
                        Instr instr = Parse(e.Instructions[i]);
                        if (instr.Init) continue;
                        // Process parameters first
                        List<(EventKey, int)> usedParams = new List<(EventKey, int)>();
                        foreach (EMEVD.Parameter param in e.Parameters)
                        {
                            if (param.InstructionIndex == i)
                            {
                                int paramIndex = IndexFromByteOffset(instr, (int)param.TargetStartByte);
                                instr[paramIndex] = $"X{param.SourceStartByte}_{param.ByteCount}";
                                (EventKey, int) paramId = (key, (int)param.SourceStartByte);
                                usedParams.Add(paramId);
                                if (paramIndex < instr.Doc.Arguments.Length)
                                {
                                    argDocs[paramId] = instr.Doc.Arguments[paramIndex];
                                }
                            }
                        }
                        // Basic display stuff
                        I info = new I
                        {
                            Event = (int)e.ID,
                            Name = instr.Name,
                            Val = instr,
                            Args = darkScriptMode
                                ? instr.Doc.Arguments.Select((arg, j) => instr.FormatArg(instr[j], j)).ToList()
                                : instr.Doc.Arguments.Select((arg, j) => $"{arg.Name} = {instr[j]}").ToList(),
                            Space = darkScriptMode ? "" : " ",
                        };
                        // Now add all IDs
                        foreach ((int j, T id) in getInstrValues(instr))
                        {
                            eventInfo.IDs.Add(id);
                            info.IDs.Add(id);
                            if (highlightValue(id))
                            {
                                eventInfo.Highlight = true;
                                info.HighlightArgs.Add(j);
                            }
                        }
                        if (alwaysHighlight != null && alwaysHighlight(instr))
                        {
                            eventInfo.Highlight = true;
                            eventInfo.HighlightInstr = true;
                        }
                        if (instr.ArgList.Count > instr.Doc.Arguments.Length)
                        {
                            info.Args.AddRange(instr.ArgList.Skip(instr.Doc.Arguments.Length).Select(arg => arg.ToString()));
                        }
                        foreach ((EventKey, int) id in usedParams)
                        {
                            AddMulti(argCommands, id, info);
                        }
                        eventInfo.Instructions.Add(info);
                    }
                }
            }
            bool debugCallees = false;
            foreach (KeyValuePair<string, EMEVD> entry in emevds)
            {
                foreach (EMEVD.Event e in entry.Value.Events)
                {
                    EventKey key = new EventKey((int)e.ID, entry.Key);
                    EventAny<T, I> eventInfo = eventInfos[key];
                    for (int i = 0; i < e.Instructions.Count; i++)
                    {
                        Instr instr = Parse(e.Instructions[i]);
                        if (!instr.Init) continue;
                        string calleeMap = instr.Val.ID == 6 ? "common_func" : key.Map;
                        EventKey callee = new EventKey(instr.Callee, calleeMap);
                        if (!eventInfos.TryGetValue(callee, out E calleeInfo))
                        {
                            // 1050402210 called as common_func event, among others? check for this I guess
                            calleeMap = instr.Val.ID == 6 ? key.Map : "common_func";
                            callee = new EventKey(instr.Callee, calleeMap);
                            if (!eventInfos.TryGetValue(callee, out calleeInfo))
                            {
                                if (debugCallees) Console.WriteLine($"Invalid callee in {entry.Key}: {instr}");
                                continue;
                            }
                            if (debugCallees) Console.WriteLine($"Mixed callee in {entry.Key}: {instr}");
                        }
                        // entityParams are the indices of this
                        List<(int, T)> initVals = getInitValues(instr, calleeInfo);
                        if (alwaysHighlight?.Invoke(instr) ?? false)
                        {
                            calleeInfo.Highlight = true;
                        }
                        if (initVals.Count > 0 || calleeInfo.Highlight)
                        {
                            // Add the metadata, but don't highlight it unless any of them are highlightable
                            string renderCallArg(object arg, int pos)
                            {
                                if (pos >= instr.Offset)
                                {
                                    int offset = (pos - instr.Offset) * 4;
                                    argDocs.TryGetValue((callee, offset), out EMEDF.ArgDoc doc);
                                    return $"X{offset}_4 = {FormatValue(arg, doc)}";
                                }
                                return $"{arg}";
                            }
                            I caller = new I
                            {
                                Event = (int)e.ID,
                                Name = instr.Name,
                                Val = instr,
                                Args = instr.ArgList.Select(renderCallArg).ToList(),
                                Space = darkScriptMode ? "" : " ",
                            };
                            calleeInfo.Callers.Add(caller);
                            List<T> callIds = initVals.Select(v => v.Item2).ToList();
                            calleeInfo.CallerIDs.AddRange(callIds);
                            caller.IDs.AddRange(callIds);
                            Dictionary<string, T> paramStrings = initVals.ToDictionary(v => $"X{v.Item1 * 4}_4", v => v.Item2);
                            foreach ((int j, T val) in initVals)
                            {
                                bool highlightArg = highlightValue(val);
                                if (highlightArg)
                                {
                                    calleeInfo.Highlight = true;
                                    // 1039442341
                                    caller.HighlightArgs.Add(instr.Offset + j);
                                }
                                argCommands.TryGetValue((callee, j * 4), out List<I> usages);
                                if (usages != null)
                                {
                                    foreach (I usage in usages)
                                    {
                                        for (int k = 0; k < usage.Args.Count; k++)
                                        {
                                            // Hackily extract last part of e.g. "Target Entity ID = X0_4"
                                            string argString = usage.Args[k].Split(' ').Last();
                                            if (paramStrings.TryGetValue(argString, out T arg))
                                            {
                                                // This might double-count some args which use multiple v
                                                if (highlightValue(arg))
                                                {
                                                    usage.HighlightArgs.Add(k);
                                                }
                                                // Also, this is confusing, only include ids directly present in the instruction
                                                // usage.IDs.Add(arg);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return eventInfos;
        }

        // TODO: We can migrate this to use the generic version, in theory
        public SortedDictionary<EventKey, EventDebug> GetHighlightedEvents(
            Dictionary<string, EMEVD> emevds,
            HashSet<int> ids,
            Predicate<Instr> alwaysHighlight = null)
        {
            List<(int, int)> getInstrValuesOffset(Instr instr, int offset)
            {
                List<(int, int)> ret = new List<(int, int)>();
                for (int j = offset; j < instr.ArgList.Count; j++)
                {
                    object arg = instr[j];
                    if (arg is int argint && ids.Contains(argint))
                    {
                        ret.Add((j - offset, argint));
                    }
                    else if (arg is uint arguint && ids.Contains((int)arguint))
                    {
                        argint = (int)arguint;
                        ret.Add((j - offset, argint));
                    }
                }
                return ret;
            }
            List<(int, int)> getInstrValues(Instr instr)
            {
                return getInstrValuesOffset(instr, 0);
            }
            List<(int, int)> getInitValues(Instr instr, EventDebug info)
            {
                return getInstrValuesOffset(instr, instr.Offset);
                // This solely based on which parameters are referenced in the event itself
            }
            bool highlightValue(int value)
            {
                // If it's returned from a values lookup, it's going to be eligible
                return true;
            }
            return GetCommandHighlightedEvents<EventDebug, InstructionDebug, int>(
                emevds, getInstrValues, getInitValues, highlightValue, alwaysHighlight);
        }

        public List<S> CreateEventConfig<S>(
            SortedDictionary<EventKey, EventDebug> eventInfos,
            Predicate<int> eligibleFilter,
            Func<S> createSpec,
            Func<int, string> quickId,
            HashSet<int> eventsOverride = null,
            HashSet<int> idsOverride = null)
            where S : AbstractEventSpec
        {
            return CreateEventConfigAny<S, EventDebug, InstructionDebug, int>(
                eventInfos, eligibleFilter, _ => createSpec(), quickId, eventsOverride, idsOverride);
        }

        public List<S> CreateEventConfigAny<S, E, I, T>(
            SortedDictionary<EventKey, E> eventInfos,
            Predicate<T> eligibleFilter,
            Func<EventKey, S> createSpec,
            Func<T, string> quickId,
            HashSet<int> eventsOverride = null,
            HashSet<T> idsOverride = null)
            where S : AbstractEventSpec
            where I : InstructionAny<T>, new()
            where E : EventAny<T, I>, new()
        {
            List<S> toWrite = new List<S>();
            bool mixCalls = true;
            HashSet<int> constructorIds = new HashSet<int> { 0, 50, 100, 150, 200, 250 };
            foreach (KeyValuePair<EventKey, E> entry in eventInfos.OrderBy(e => e.Key))
            {
                E info = entry.Value;
                // Include constructors, but don't include init commands, since they're covered by other entries
                // if (entry.Key.ID == 0 || entry.Key.ID == 50) continue;
                bool process = info.Highlight;
                HashSet<T> eligibleIDs = new HashSet<T>(info.AllIDs.Where(id => eligibleFilter(id)));
                bool isConstructor = constructorIds.Contains(entry.Key.ID);
                List<I> instrs = info.Instructions;
                if (isConstructor)
                {
                    // List the bare minimum for constructors: non-init instructions with eligible ids
                    instrs = instrs.Where(c => !c.Name.StartsWith("Initialize") && c.IDs.Intersect(eligibleIDs).Count() > 0).ToList();
                    eligibleIDs = new HashSet<T>(instrs.SelectMany(c => c.IDs.Intersect(eligibleIDs)));
                }
                process = process && (info.HighlightInstr || eligibleIDs.Count > 0);
                if (eventsOverride?.Count > 0) process = eventsOverride.Contains(entry.Key.ID);
                else if (idsOverride?.Count > 0) process = idsOverride.Intersect(info.AllIDs).Count() > 0;
                // TODO: Add a way to just specify event ids (again?)
                if (entry.Key.ID == 9005822) process = true;
                if (!process) continue;

                S spec = createSpec(entry.Key);
                spec.ID = entry.Key.ID;
                spec.Map = entry.Key.Map;
                spec.Comment = spec.Comment ?? "none";
                if (info.Callers.Count == 0)
                {
                    spec.DebugInit = new List<string> { "No initializations" };
                }
                if (mixCalls)
                {
                    if (isConstructor)
                    {
                        spec.DebugInfo = info.IDs
                            .Where(id => eligibleIDs.Contains(id))
                            .Select(id => quickId(id))
                            .ToList();
                    }
                    else
                    {
                        spec.DebugInfo = info.IDs.Distinct().Select(id => quickId(id)).ToList();
                    }
                    spec.DebugInfo.RemoveAll(text => text == null);
                    HashSet<T> usedIds = new HashSet<T>(info.IDs);
                    if (info.Callers.Count > 1 || info.UsesParameters)
                    {
                        List<string> initInfo = new List<string>();
                        bool first = true;
                        foreach (I caller in info.Callers)
                        {
                            initInfo.Add(caller.CallString());
                            foreach (T id in caller.IDs)
                            {
                                if (usedIds.Add(id))
                                {
                                    string text = quickId(id);
                                    if (text != null)
                                    {
                                        initInfo.Add(text);
                                    }
                                }
                            }
                            if (first)
                            {
                                spec.DebugInfo.AddRange(initInfo);
                                initInfo.Clear();
                                first = false;
                            }
                        }
                        if (initInfo.Count > 0)
                        {
                            if (spec.DebugInfo.Sum(i => i.Length) + initInfo.Sum(i => i.Length) < 1000)
                            {
                                spec.DebugInfo.AddRange(initInfo);
                            }
                            else
                            {
                                spec.DebugInfo.Add($"({info.Callers.Count} total initializations)");
                                spec.DebugOtherInits = initInfo;
                            }
                        }
                    }
                }
                else if (!isConstructor)
                {
                    if (info.UsesParameters)
                    {
                        spec.DebugInit = info.Callers.Select(c => c.CallString()).ToList();
                    }
                    spec.DebugInfo = info.AllIDs.Distinct().Select(id => quickId(id)).ToList();
                    spec.DebugInfo.RemoveAll(text => text == null);
                }
                spec.DebugCommands = instrs
                    .Select(c => $"{(c.HighlightArgs.Count > 0 ? "+ " : "")}{c.CallString()}")
                    .ToList();
                toWrite.Add(spec);
            }
            return toWrite;
        }

        private static void AddMulti<K, V>(IDictionary<K, List<V>> dict, K key, V value)
        {
            if (!dict.ContainsKey(key)) dict[key] = new List<V>();
            dict[key].Add(value);
        }

        // DarkScript3 name routines

        private List<string> EnumNamesForGlobalization = new List<string>
        {
            "ON/OFF",
            "ON/OFF/CHANGE",
            "Condition Group",
            "Condition State",
            "Disabled/Enabled",
        };

        private static readonly Dictionary<string, string> EnumReplacements = new Dictionary<string, string>
        {
            { "BOOL.TRUE", "true" },
            { "BOOL.FALSE", "false" },
        };

        private static readonly List<string> Acronyms = new List<string>()
        {
            "AI","HP","SE","SP","SFX","FFX","NPC"
        };

        private static string TitleCaseName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            string[] words = Regex.Replace(s, @"[^\w\s]", "").Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0)
                {
                    continue;
                }
                else if (Acronyms.Contains(words[i].ToUpper()))
                {
                    words[i] = words[i].ToUpper();
                    continue;
                }
                else if (words[i] == "SpEffect")
                {
                    continue;
                }

                char firstChar = char.ToUpper(words[i][0]);
                string rest = "";
                if (words[i].Length > 1)
                {
                    rest = words[i].Substring(1).ToLower();
                }
                words[i] = firstChar + rest;
            }
            string output = Regex.Replace(string.Join("", words), @"[^\w]", "");
            return output;
        }

        private static string CamelCaseName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string name = TitleCaseName(s);
            char firstChar = char.ToLower(name[0]);
            if (name.Length > 1)
                return firstChar + name.Substring(1);
            else
                return firstChar.ToString();
        }
    }
}
