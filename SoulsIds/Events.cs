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
        private static Dictionary<int, int> ArgLength = new Dictionary<int, int>
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

        private EMEDF doc;
        private Dictionary<string, (int, int)> docByName = new Dictionary<string, (int, int)>();
        private Dictionary<string, int> enumByName = new Dictionary<string, int>();
        private Dictionary<EMEDF.InstrDoc, List<int>> funcBytePositions = new Dictionary<EMEDF.InstrDoc, List<int>>();
        private readonly bool darkScriptMode;

        public Events(string emedfPath, bool darkScriptMode = false)
        {
            doc = EMEDF.ReadFile(emedfPath);
            docByName = doc.Classes.SelectMany(c => c.Instructions.Select(i => (i, (int)c.Index))).ToDictionary(i => i.Item1.Name, i => (i.Item2, (int)i.Item1.Index));
            funcBytePositions = new Dictionary<EMEDF.InstrDoc, List<int>>();
            Dictionary<string, EMEDF.EnumDoc> enums = new Dictionary<string, EMEDF.EnumDoc>();
            // For darkScriptMode: accept either standard or darkscript names as full-command input, but output based on the flag.
            // However, the edit strings do require enums in darkscript mode because we do coarse string matching.
            this.darkScriptMode = darkScriptMode;
            foreach (EMEDF.EnumDoc enm in doc.Enums)
            {
                bool global = EnumNamesForGlobalization.Contains(enm.Name);
                enums[enm.Name] = enm;
                string enumName = Regex.Replace(enm.Name, @"[^\w]", "");
                string prefix = global ? "" : $"{enumName}.";
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
                    if (darkScriptMode)
                    {
                        // Name is used for output
                        instr.Name = darkName;
                    }
                    int bytePos = 0;
                    foreach (EMEDF.ArgDoc arg in instr.Arguments)
                    {
                        int len = ArgLength[(int)arg.Type];
                        if (bytePos % len > 0) bytePos += len - (bytePos % len);
                        AddMulti(funcBytePositions, instr, bytePos);
                        bytePos += len;
                        if (arg.EnumName != null && enums.TryGetValue(arg.EnumName, out EMEDF.EnumDoc enm))
                        {
                            arg.EnumDoc = enm;
                        }
                    }
                }
            }
        }

        // Instruction metadata
        public Instr Parse(EMEVD.Instruction instr, bool onlyCmd = false, bool onlyInit = false)
        {
            bool isInit = instr.Bank == 2000 && (instr.ID == 0 || instr.ID == 6);
            if (onlyCmd && isInit) return null;
            if (onlyInit && !isInit) return null;
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
                Args = args,
                Init = isInit
            };
            if (isInit)
            {
                ret.Offset = instr.ID == 0 ? 2 : 1;
                ret.Callee = (int)args[instr.ID == 0 ? 1 : 0];
            }
            return ret;
        }

        public class Instr
        {
            // Actual instruction
            public EMEVD.Instruction Val { get; set; }
            public EMEDF.InstrDoc Doc { get; set; }
            public string Name => Doc?.Name;
            public List<ArgType> Types { get; set; }
            // TODO: Hide these, as they override Modified management
            public List<object> Args { get; set; }
            // Whether an event initialization or not
            public bool Init { get; set; }
            // If an event initialization, which event is being initialized
            public int Callee { get; set; }
            // If an event initialization, the index start of actual event arguments
            public int Offset { get; set; }
            // Dirty bit
            public bool Modified { get; set; }

            public void Save()
            {
                if (Modified)
                {
                    Val.PackArgs(Args);
                    Modified = false;
                }
            }

            public object this[int i]
            {
                get => Args[i];
                set
                {
                    if (value is string s)
                    {
                        if (s.StartsWith("X"))
                        {
                            // Allow this in the case of psuedo-variables representing event args. This instruction cannot be repacked in this case.
                            Args[i] = value;
                        }
                        else
                        {
                            Args[i] = ParseArg(s, Types[i]);
                        }
                    }
                    else
                    {
                        Args[i] = value;
                    }
                    Modified = true;
                }
            }

            public string FormatArg(object arg, int i)
            {
                return FormatValue(arg, i < Doc.Arguments.Length ? Doc.Arguments[i] : null);
            }

            public override string ToString() => $"{Name} ({string.Join(", ", Args.Select((a, i) => FormatArg(a, i)))})";
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
            }
            if (arg is float f)
            {
                // One possibility: adding a .0 for integer floats to show it's a float. This is a bit noisy though.
                arg = f.ToString(CultureInfo.InvariantCulture);
            }
            return arg.ToString();
        }

        private int IndexFromByteOffset(Instr instr, int offset)
        {
            int paramIndex = funcBytePositions[instr.Doc].IndexOf(offset);
            if (paramIndex == -1) throw new Exception($"Finding {instr.Name}, target {offset}, available {string.Join(",", funcBytePositions[instr.Doc])}");
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

        public Instr CopyInit(Instr instr, EMEVD.Event newEvent)
        {
            // Assume this is copying common id/map id to fresh map id
            Instr newInstr = Parse(CopyInstruction(instr.Val));
            if (newInstr.Val.Bank == 2000)
            {
                if (newEvent == null) throw new Exception($"Internal error: Event not provided for copying {string.Join(",", instr.Args)}");
                if (newInstr.Val.ID == 0)
                {
                    newInstr[0] = 0;
                    newInstr[1] = (uint)newEvent.ID;
                }
                else if (newInstr.Val.ID == 6)
                {
                    // Just create a brand new instruction
                    List<object> args = newInstr.Args.ToList();
                    args.Insert(0, 0);
                    args[1] = (uint)newEvent.ID;
                    return Parse(new EMEVD.Instruction(2000, 0, args));
                }
            }
            return newInstr;
        }

        // Preserving parameters after adding/removing instructions
        public class OldParams
        {
            public EMEVD.Event Event { get; set; }
            public List<EMEVD.Instruction> Original { get; set; }
            public Dictionary<EMEVD.Instruction, List<EMEVD.Parameter>> NewInstructions = new Dictionary<EMEVD.Instruction, List<EMEVD.Parameter>>();

            // Creates a record of parameters with original instructions.
            // The parameters will be preserved if the parameterized instructions are still present by reference.
            // If using this system, parameters should not be modified manually.
            public static OldParams Preprocess(EMEVD.Event e)
            {
                if (e.Parameters.Count == 0) return new OldParams();
                return new OldParams
                {
                    Event = e,
                    Original = e.Instructions.ToList(),
                };
            }

            // Adds a never-before-seen paramterized instruction, and parameters to add later for it.
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
                int oldIndex = Original == null ? -1 : Original.IndexOf(ins);
                if (oldIndex == -1) return new List<EMEVD.Parameter>();
                return Event.Parameters.Where(p => p.InstructionIndex == oldIndex).ToList();
            }

            // Updates old indices and adds new indices
            public void Postprocess()
            {
                if (Event == null || (Event.Parameters.Count == 0 && NewInstructions.Count == 0)) return;
                Dictionary<EMEVD.Instruction, List<int>> currentIndices = Event.Instructions
                    .Select((a, i) => (a, i))
                    .GroupBy(p => p.Item1)
                    .ToDictionary(ps => ps.Key, ps => ps.Select(p => p.Item2).ToList());
                List<EMEVD.Parameter> empty = new List<EMEVD.Parameter>();
                // Update old indices
                Event.Parameters = Event.Parameters.SelectMany(p =>
                {
                    if (currentIndices.TryGetValue(Original[(int)p.InstructionIndex], out List<int> indices))
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
            }
        }

        public enum SegmentState
        {
            // After starting instruction reached, if one is defined, but before match
            Before,
            // After starting intstruction reached, not expecting match
            BeforeMatchless,
            // After starting instruction, before first match. By default, segments require a match to add other commands into its spot.
            Prematch,
            // In the segment
            During,
            // After ending instruction reached, if one is defined
            After
        }

        // Editing macros
        public class EventEdits
        {
            // All edits matched against instruction names
            public Dictionary<string, List<InstrEdit>> NameEdits { get; set; }
            // All edits matched against integer values
            public Dictionary<string, List<InstrEdit>> ArgEdits { get; set; }
            // All edits matched by instruction name + full arguments, with format depending on darkScriptMode
            public Dictionary<(string, string), List<InstrEdit>> NameArgEdits { get; set; }
            // All edits matched against segment start (SegmentAdd edits)
            public Dictionary<string, List<InstrEdit>> SegmentEdits { get; set; }

            // Set of all edits, for the purpose of making sure all are applied
            public HashSet<InstrEdit> PendingEdits = new HashSet<InstrEdit>();
            // Set of all edits, to avoid repetition in some cases
            public HashSet<InstrEdit> EncounteredEdits = new HashSet<InstrEdit>();
            // InstrEdits to apply by line index. This is saved until the end and done in reverse order because it's index-based.
            public Dictionary<int, List<InstrEdit>> PendingAdds = new Dictionary<int, List<InstrEdit>>();
            // Segment tracking state, for edits which only apply during specific segments
            public Dictionary<string, SegmentState> SegmentStates = new Dictionary<string, SegmentState>();

            // Returns all applicable edits
            public List<InstrEdit> GetMatches(Instr instr)
            {
                List<InstrEdit> nameEdit = null;
                if (instr.Name == null) return null;  // Can happen with removed instructions, nothing left to edit
                if (NameEdits != null && !NameEdits.TryGetValue(instr.Name, out nameEdit) && ArgEdits == null && NameArgEdits == null) return null;
                List<string> strArgs = instr.Args.Select((a, i) => instr.FormatArg(a, i)).ToList();
                List<InstrEdit> edits = new List<InstrEdit>();
                if (ArgEdits != null)
                {
                    edits.AddRange(strArgs.SelectMany(s => ArgEdits.TryGetValue(s, out List<InstrEdit> edit) ? edit : new List<InstrEdit>()));
                }
                if (nameEdit != null)
                {
                    edits.AddRange(nameEdit);
                }
                if (NameArgEdits != null && NameArgEdits.TryGetValue((instr.Name, string.Join(",", strArgs)), out List<InstrEdit> nameArgEdit))
                {
                    edits.AddRange(nameArgEdit);
                }
                return edits;
            }

            // Applies all edits that can be applied in place, and adds others later
            public void ApplyEdits(Instr instr, int index)
            {
                List<InstrEdit> edits = GetMatches(instr);
                if (edits == null) return;

                // Either apply edits or return them back
                void beginSegment(string segment, int addIndex)
                {
                    SegmentStates[segment] = SegmentState.During;
                    // This may prompt some additions
                    if (SegmentEdits != null && SegmentEdits.TryGetValue(segment, out List<InstrEdit> segEdits))
                    {
                        foreach (InstrEdit segEdit in segEdits)
                        {
                            if (segEdit.Type != EditType.SegmentAdd) throw new Exception($"Internal error: segment edits not supported for {segEdit}");
                            AddMulti(PendingAdds, addIndex, segEdit);
                        }
                    }
                }
                bool removed = false;
                foreach (InstrEdit edit in edits.OrderBy(e => e.Type))
                {
                    // "Apply once" edits apply uniquely to their own command
                    if (edit.ApplyOnce && (EncounteredEdits.Contains(edit) || removed))
                    {
                        continue;
                    }
                    EncounteredEdits.Add(edit);
                    if (edit.Type == EditType.StartSegment)
                    {
                        if (SegmentStates[edit.Segment] == SegmentState.Before)
                        {
                            SegmentStates[edit.Segment] = SegmentState.Prematch;
                        }
                        else if (SegmentStates[edit.Segment] == SegmentState.BeforeMatchless)
                        {
                            beginSegment(edit.Segment, index + 1);
                        }
                    }
                    else if (edit.Type == EditType.EndSegment)
                    {
                        if (SegmentStates[edit.Segment] == SegmentState.During || SegmentStates[edit.Segment] == SegmentState.Prematch)
                        {
                            SegmentStates[edit.Segment] = SegmentState.After;
                        }
                    }
                    else if (edit.Type == EditType.Remove)
                    {
                        // TODO Don't start a segment if it's not in the right state
                        // if (edit.Segment != null)
                        // For now, use inplace remove, a bit less messy
                        instr.Val = new EMEVD.Instruction(1014, 69);
                        instr.Init = false;
                        instr.Doc = null;
                        instr.Args.Clear();
                        instr.Types.Clear();
                        removed = true;
                        // Start any relevant segments
                        if (edit.Segment != null && SegmentStates[edit.Segment] == SegmentState.Prematch)
                        {
                            SegmentStates[edit.Segment] = SegmentState.During;
                            beginSegment(edit.Segment, index);
                        }
                    }
                    if (edit.Add != null)
                    {
                        // Do it later to keep indices consistent
                        AddMulti(PendingAdds, index, edit);
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
                            for (int i = 0; i < instr.Args.Count; i++)
                            {
                                if (edit.ValEdit.TryGetValue(instr.FormatArg(instr[i], i), out string replace))
                                {
                                    instr[i] = replace;
                                }
                            }
                        }
                    }
                    // This edit is accounted for, unless it's an Add, in which case it will be done later
                    if (edit.Add == null)
                    {
                        PendingEdits.Remove(edit);
                    }
                }
            }

            public void AddEdit(string toFind, Predicate<string> docName, InstrEdit edit)
            {
                if (edit.Type == EditType.None)
                {
                    throw new Exception($"Invalid InstrEdit {edit}");
                }
                if (edit.Segment != null && !SegmentStates.ContainsKey(edit.Segment))
                {
                    throw new Exception($"Internal error: Segment {edit.Segment} not found in [{string.Join(", ", SegmentStates.Keys)}]");
                }
                if (int.TryParse(toFind, out var _))
                {
                    if (ArgEdits == null) ArgEdits = new Dictionary<string, List<InstrEdit>>();
                    AddMulti(ArgEdits, toFind, edit);
                }
                else if (docName(toFind))
                {
                    // If this isn't a name, it will come up later as an unused pending edit
                    if (NameEdits == null) NameEdits = new Dictionary<string, List<InstrEdit>>();
                    AddMulti(NameEdits, toFind, edit);
                }
                // Perhaps have a more coherent way of doing this, but use this naming convention for now
                else if (toFind.Contains("segment"))
                {
                    if (SegmentEdits == null) SegmentEdits = new Dictionary<string, List<InstrEdit>>();
                    AddMulti(SegmentEdits, toFind, edit);
                }
                else
                {
                    (string cmd, List<string> addArgs) = ParseCommandString(toFind);
                    if (NameArgEdits == null) NameArgEdits = new Dictionary<(string, string), List<InstrEdit>>();
                    AddMulti(NameArgEdits, (cmd, string.Join(",", addArgs)), edit);
                }
                if (!edit.Optional)
                {
                    PendingEdits.Add(edit);
                }
            }

            public void AddReplace(string toFind, string toVal = null)
            {
                if (toVal != null || Regex.IsMatch(toFind, @"^[\d.]+\s*->\s*[\d.]+$"))
                {
                    // Replace any value with any other value
                    string[] parts = toVal == null ? Regex.Split(toFind, @"\s*->\s*") : new[] { toFind, toVal };
                    InstrEdit edit = new InstrEdit
                    {
                        SearchInfo = toFind,
                        Type = EditType.Replace,
                        ValEdit = new Dictionary<string, string> { { parts[0], parts[1] } },
                    };
                    if (ArgEdits == null) ArgEdits = new Dictionary<string, List<InstrEdit>>();
                    AddMulti(ArgEdits, parts[0], edit);
                    PendingEdits.Add(edit);  // Currently cannot be optional
                }
                else
                {
                    if (toVal != null) throw new Exception();
                    (string cmd, List<string> addArgs) = ParseCommandString(toFind);
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
                    if (NameArgEdits == null) NameArgEdits = new Dictionary<(string, string), List<InstrEdit>>();
                    AddMulti(NameArgEdits, (cmd, string.Join(",", addArgs)), edit);
                    PendingEdits.Add(edit);  // Currently cannot be optional
                }
            }
        }

        public enum EditType
        {
            // Various edit types. These are ordered by application order.
            None, EndSegment, Remove, AddAfter, AddBefore, SegmentAdd, Replace, StartSegment,
        }

        // Edits to apply based on a matching line
        public class InstrEdit
        {
            // Matching info, for debug purposes only. (TODO could also make this authoritative, and not a string)
            public string SearchInfo { get; set; }
            // Edit type
            public EditType Type { get; set; }
            // The segment name, for StartSegment/EndSegment
            public string Segment { get; set; }
            // The instruction to add, for AddAfter/AddBefore/SegmentAdd. TODO could also do Replace I guess?
            public EMEVD.Instruction Add { get; set; }
            // Parameters for the new instruction. InstructionIndex is filled in by OldParams during postprocessing
            public List<EMEVD.Parameter> AddParams { get; set; }
            // If a new instruction, whether to add it after the matching instruction or displace it
            // public bool AddAfter { get; set; }
            // If a remove instruction
            // public bool Remove { get; set; }
            // TODO: maybe we want a Replace mode?
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
                + (Add == null ? " " : $" [Cmd:{Add.Bank}.{Add.ID}]")
                + (AddParams == null ? "" : $"[Param:{AddParams.Count}]")
                + (PosEdit == null ? "" : $"[Set:{string.Join(",", PosEdit)}]")
                + (ValEdit == null ? "" : $"[Replace:{string.Join(", ", ValEdit)}]");
        }

        public void ApplyAllEdits(EMEVD.Event ev, EventEdits edits)
        {
            OldParams pre = OldParams.Preprocess(ev);
            for (int j = 0; j < ev.Instructions.Count; j++)
            {
                Instr instr = Parse(ev.Instructions[j]);
                // if (instr.Init) continue;
                edits.ApplyEdits(instr, j);
                instr.Save();
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

        public void RegisterSegment(EventEdits edits, string name, string startCmd, string endCmd, bool ignoreMatch)
        {
            if (edits.SegmentStates.ContainsKey(name)) throw new Exception($"Segment {name} already defined");
            edits.SegmentStates[name] = startCmd == null
                ? SegmentState.Prematch
                : (ignoreMatch ? SegmentState.BeforeMatchless : SegmentState.Before);
            if (startCmd != null)
            {
                InstrEdit start = new InstrEdit
                {
                    SearchInfo = startCmd,
                    Type = EditType.StartSegment,
                    Segment = name,
                };
                edits.AddEdit(startCmd, n => docByName.ContainsKey(n), start);
            }
            if (endCmd != null)
            {
                InstrEdit end = new InstrEdit
                {
                    SearchInfo = endCmd,
                    Type = EditType.EndSegment,
                    Segment = name,
                };
                edits.AddEdit(endCmd, n => docByName.ContainsKey(n), end);
            }
        }

        public void AddMacro(EventEdits edits, List<EventAddCommand> adds)
        {
            foreach (EventAddCommand add in adds)
            {
                if (add.Before == null)
                {
                    AddMacro(edits, EditType.AddAfter, add.Cmd, add.After);
                }
                else
                {
                    AddMacro(edits, EditType.AddBefore, add.Cmd, add.Before == "start" ? null : add.Before);
                }
            }
        }

        [Obsolete]
        public void AddMacro(EventEdits edits, string toFind, bool addAfter, string add)
        {
            AddMacro(edits, addAfter ? EditType.AddAfter : EditType.AddBefore, add, toFind);
        }

        public void AddMacro(EventEdits edits, EditType editType, string add, string toFind = null, bool applyOnce = false)
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
                edits.AddEdit(toFind, n => docByName.ContainsKey(n), edit);
            }
        }

        public void RemoveMacro(EventEdits edits, string toFind, bool applyOnce = false)
        {
            edits.AddEdit(toFind, n => docByName.ContainsKey(n), new InstrEdit
            {
                SearchInfo = toFind,
                Type = EditType.Remove,
                ApplyOnce = applyOnce,
            });
        }

        public void RemoveSegmentMacro(EventEdits edits, string segment, string toFind)
        {
            edits.AddEdit(toFind, n => docByName.ContainsKey(n), new InstrEdit
            {
                SearchInfo = toFind,
                Type = EditType.Remove,
                Segment = segment,
                Optional = true,
            });
        }

        public void ReplaceMacro(EventEdits edits, string toFind, string toVal = null)
        {
            // This case includes an add/remove, so it cannot be done in the edit itself. TODO simplify this split
            if ((toVal != null && toVal.Contains("(")) || Regex.IsMatch(toFind, @"->.*\("))
            {
                // Replace a full command with another full command
                string[] parts = toVal == null ? Regex.Split(toFind, @"\s*->\s*") : new[] { toFind, toVal };
                RemoveMacro(edits, parts[0]);
                AddMacro(edits, EditType.AddAfter, parts[1], parts[0]);
            }
            else
            {
                edits.AddReplace(toFind, toVal);
            }
        }

        // Simpler mass rewrite: just int replacements, for entity ids which are generally unambiguous
        public void RewriteInts(Instr instr, Dictionary<int, int> changes)
        {
            for (int i = 0; i < instr.Args.Count; i++)
            {
                if (instr.Args[i] is int ik && changes.TryGetValue(ik, out int val))
                {
                    instr[i] = val;
                }
                else if (instr.Args[i] is uint uk && changes.TryGetValue((int)uk, out val))
                {
                    instr[i] = (uint)val;
                }
            }
        }

        public string RewriteInts(string add, Dictionary<int, int> changes)
        {
            (string cmd, List<string> addArgs) = ParseCommandString(add);
            for (int i = 0; i < addArgs.Count; i++)
            {
                if (int.TryParse(addArgs[i], out int ik) && changes.TryGetValue(ik, out int val))
                {
                    addArgs[i] = val.ToString();
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

        // Set params in the Instr.
        // This makes them unsuitable to be written back currently (TODO, use a ParamArg class for this if needed)
        public void SetInstrParamArgs(Instr instr, OldParams pre)
        {
            List<EMEVD.Parameter> ps = pre.GetInstructionParams(instr.Val);
            if (ps.Count == 0) return;
            EMEDF.InstrDoc insDoc = doc[instr.Val.Bank][instr.Val.ID];
            List<int> paramOffsets = insDoc != null && funcBytePositions.TryGetValue(insDoc, out var val) ? val : null;
            if (paramOffsets == null) throw new Exception($"Unknown parameterized instruction {instr}");
            foreach (EMEVD.Parameter p in ps)
            {
                int pos = paramOffsets.IndexOf((int)p.TargetStartByte);
                if (pos == -1) throw new Exception($"Misaligned parameter {p.SourceStartByte}->{p.TargetStartByte} in {instr}");
                instr[pos] = $"X{p.SourceStartByte}_{p.ByteCount}";
            }
        }

        // Condition rewriting
        public List<int> FindCond(EMEVD.Event e, string req)
        {
            List<int> cond = new List<int>();
            bool isGroup = int.TryParse(req, out int _);
            for (int i = 0; i < e.Instructions.Count; i++)
            {
                Instr instr = Parse(e.Instructions[i]);
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

        public List<EMEVD.Instruction> RewriteCondGroup(List<EMEVD.Instruction> after, Dictionary<int, int> reloc, int target)
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

        public bool ParseArgSpec(string arg, out int pos)
        {
            return TryArgSpec(arg, out pos);
        }

        public static bool TryArgSpec(string arg, out int pos)
        {
            // For event initializations with int args specified as X0, X4, X8, etc., return the arg position, e.g. 0, 1, 2
            pos = 0;
            if (arg == null) return false;
            if (arg.StartsWith("X") && int.TryParse(arg.Substring(1), out pos) && pos >= 0)
            {
                pos /= 4;
                return true;
            }
            return false;
        }

        public abstract class AbstractEventSpec
        {
            public int ID { get; set; }
            public string Comment { get; set; }
            public List<string> DebugInfo { get; set; }
            public List<string> DebugInit { get; set; }
            public List<string> DebugCommands { get; set; }
        }

        public class EventAddCommand
        {
            public string Cmd { get; set; }
            public string Before { get; set; }
            public string After { get; set; }
        }

        public class EventDebug
        {
            public int Event { get; set; }
            public bool Highlight { get; set; }
            public bool HighlightInstr { get; set; }
            public bool UsesParameters { get; set; }
            public List<int> IDs = new List<int>();
            public List<InstructionDebug> Callers = new List<InstructionDebug>();
            public List<InstructionDebug> Instructions = new List<InstructionDebug>();
        }

        public class InstructionDebug
        {
            public int Event { get; set; }
            public string Name { get; set; }
            public List<string> Args = new List<string>();
            public HashSet<int> HighlightArgs = new HashSet<int>();
            public HashSet<int> IDs = new HashSet<int>();
            public string Space = "";
            // Deprecated
            public InstructionDebug Caller { get; set; }
            public InstructionDebug Copy()
            {
                return (InstructionDebug)MemberwiseClone();
            }
            public string CallString() => $"{Name}{Space}({string.Join(", ", Args.Select((a, i) => HighlightArgs.Contains(i) ? $"{a}*" : a))})";
            public override string ToString() => Caller == null ? $"[Event {Event}] {CallString()}" : $"{Caller.CallString()} - {CallString()}";
        }

        // Code for producing event configs
        public SortedDictionary<int, EventDebug> GetHighlightedEvents(Dictionary<string, EMEVD> emevds, HashSet<int> ids, Predicate<Instr> alwaysHighlight = null)
        {
            SortedDictionary<int, EventDebug> eventInfos = new SortedDictionary<int, EventDebug>();
            // Map from (event ID, X# parameter offset) -> instructions that use that parameter. For annotating the instruction based on callers.
            Dictionary<(int, int), List<InstructionDebug>> argCommands = new Dictionary<(int, int), List<InstructionDebug>>();
            // Similar map for ArgDoc, for better rendering
            Dictionary<(int, int), EMEDF.ArgDoc> argDocs = new Dictionary<(int, int), EMEDF.ArgDoc>();
            foreach (KeyValuePair<string, EMEVD> entry in emevds)
            {
                foreach (EMEVD.Event e in entry.Value.Events)
                {
                    EventDebug eventInfo = new EventDebug { Event = (int)e.ID, UsesParameters = e.Parameters.Count > 0 };
                    eventInfos[eventInfo.Event] = eventInfo;
                    for (int i = 0; i < e.Instructions.Count; i++)
                    {
                        Instr instr = Parse(e.Instructions[i]);
                        // Save event initialization for next pass
                        List<(int, int)> usedParams = new List<(int, int)>();
                        foreach (EMEVD.Parameter param in e.Parameters)
                        {
                            if (param.InstructionIndex == i)
                            {
                                int paramIndex = IndexFromByteOffset(instr, (int)param.TargetStartByte);
                                instr[paramIndex] = $"X{param.SourceStartByte}_{param.ByteCount}";
                                (int, int) paramId = ((int)e.ID, (int)param.SourceStartByte);
                                usedParams.Add(paramId);
                                if (paramIndex < instr.Doc.Arguments.Length)
                                {
                                    argDocs[paramId] = instr.Doc.Arguments[paramIndex];
                                }
                            }
                        }
                        InstructionDebug info = new InstructionDebug
                        {
                            Event = (int)e.ID,
                            Name = instr.Name,
                            Args = darkScriptMode
                                ? instr.Doc.Arguments.Select((arg, j) => instr.FormatArg(instr[j], j)).ToList()
                                : instr.Doc.Arguments.Select((arg, j) => $"{arg.Name} = {instr[j]}").ToList(),
                            Space = darkScriptMode ? "" : " ",
                        };
                        for (int j = 0; j < instr.Args.Count; j++)
                        {
                            object arg = instr[j];
                            if (arg is int argint && ids.Contains(argint))
                            {
                                info.HighlightArgs.Add(j);
                                info.IDs.Add(argint);
                                eventInfo.Highlight = true;
                                eventInfo.IDs.Add(argint);
                            }
                        }
                        if (alwaysHighlight != null && alwaysHighlight(instr))
                        {
                            eventInfo.Highlight = true;
                            eventInfo.HighlightInstr = true;
                        }
                        if (instr.Args.Count > instr.Doc.Arguments.Length) info.Args.AddRange(instr.Args.Skip(instr.Doc.Arguments.Length).Select(arg => arg.ToString()));
                        foreach ((int, int) id in usedParams)
                        {
                            AddMulti(argCommands, id, info);
                        }
                        eventInfo.Instructions.Add(info);
                    }
                }
            }
            foreach (KeyValuePair<string, EMEVD> entry in emevds)
            {
                foreach (EMEVD.Event e in entry.Value.Events)
                {
                    EventDebug eventInfo = eventInfos[(int)e.ID];
                    for (int i = 0; i < e.Instructions.Count; i++)
                    {
                        Instr instr = Parse(e.Instructions[i]);
                        // Find event initialization
                        if (!instr.Init) continue;
                        // New system - detect if event is interesting due to params. Or if it's highlighted, record it regardless.
                        EventDebug calleeInfo = eventInfos[instr.Callee];
                        List<int> entityParams = Enumerable.Range(0, instr.Args.Count - instr.Offset).Where(j => instr[instr.Offset + j] is int argint && ids.Contains(argint)).ToList();
                        if (entityParams.Count > 0 || calleeInfo.Highlight || (alwaysHighlight?.Invoke(instr) ?? false))
                        {
                            calleeInfo.Highlight = true;
                            string renderCallArg(object arg, int pos)
                            {
                                if (pos >= instr.Offset)
                                {
                                    int offset = (pos - instr.Offset) * 4;
                                    argDocs.TryGetValue((instr.Callee, offset), out EMEDF.ArgDoc doc);
                                    return $"X{offset}_4 = {FormatValue(arg, doc)}";
                                }
                                return $"{arg}";
                            }
                            InstructionDebug caller = new InstructionDebug
                            {
                                Event = (int)e.ID,
                                Name = instr.Name,
                                Args = instr.Args.Select(renderCallArg).ToList(),
                                Space = darkScriptMode ? "" : " ",
                            };
                            calleeInfo.Callers.Add(caller);
                            calleeInfo.IDs.AddRange(entityParams.Select(j => (int)instr[instr.Offset + j])); // is int argint ? argint : 0
                            Dictionary<string, int> paramStrings = entityParams.ToDictionary(j => $"X{j * 4}_4", j => (int)instr[instr.Offset + j]);
                            foreach (int j in entityParams)
                            {
                                caller.HighlightArgs.Add(instr.Offset + j);
                                argCommands.TryGetValue((instr.Callee, j * 4), out List<InstructionDebug> usages);
                                if (usages != null)
                                {
                                    foreach (InstructionDebug usage in usages)
                                    {
                                        for (int k = 0; k < usage.Args.Count; k++)
                                        {
                                            // Hackily extract last part of e.g. "Target Entity ID = X0_4"
                                            string argString = usage.Args[k].Split(' ').Last();
                                            if (paramStrings.TryGetValue(argString, out int argint))
                                            {
                                                usage.HighlightArgs.Add(k);
                                                usage.IDs.Add(argint);
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

        public List<T> CreateEventConfig<T>(
            SortedDictionary<int, EventDebug> eventInfos,
            Predicate<int> eligibleFilter,
            Func<T> createSpec,
            Func<int, string> quickId,
            HashSet<int> eventsOverride = null,
            HashSet<int> idsOverride = null)
            where T : AbstractEventSpec
        {
            List<T> toWrite = new List<T>();
            foreach (KeyValuePair<int, EventDebug> entry in eventInfos.OrderBy(e => e.Key))
            {
                EventDebug info = entry.Value;
                // At least for now, don't rewrite constructors in configs
                if (entry.Key == 0 || entry.Key == 50) continue;
                bool process = info.Highlight;
                process = process && (info.HighlightInstr || info.IDs.Any(id => eligibleFilter(id)));
                if (eventsOverride?.Count > 0) process = eventsOverride.Contains(entry.Key);
                else if (idsOverride?.Count > 0) process = idsOverride.Intersect(info.IDs).Count() > 0;
                if (!process) continue;

                // Awarding "my thanks" when Tsorig dies, depending on which one the player is closest to and death flags
                T spec = createSpec();
                spec.ID = entry.Key;
                spec.Comment = "none";
                spec.DebugInfo = info.IDs.Select(id => quickId(id)).Distinct().ToList();
                if (info.Callers.Count == 0)
                {
                    spec.DebugInit = new List<string> { "No initializations" };
                }
                else if (info.UsesParameters)
                {
                    spec.DebugInit = info.Callers.Select(c => c.CallString()).ToList();
                }
                spec.DebugCommands = info.Instructions.Select(c => $"{(c.HighlightArgs.Count > 0 ? "+ " : "")}{c.CallString()}").ToList();
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

        private static readonly List<string> EnumNamesForGlobalization = new List<string>
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
