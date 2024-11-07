using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoulsIds
{
    public class EMEDF
    {
        public ClassDoc this[int classIndex] => Classes.Find(c => c.Index == classIndex);

        [JsonProperty(PropertyName = "unknown")]
        public long UNK;

        [JsonProperty(PropertyName = "main_classes")]
        public List<ClassDoc> Classes;

        [JsonProperty(PropertyName = "enums")]
        public EnumDoc[] Enums;

        [JsonProperty(PropertyName = "darkscript")]
        public DarkScriptDoc DarkScript { get; set; }

        public static EMEDF ReadText(string input)
        {
            return JsonConvert.DeserializeObject<EMEDF>(input);
        }

        public static EMEDF ReadFile(string path)
        {
            string input = File.ReadAllText(path);
            return ReadText(input);
        }

        public void WriteFile(string path)
        {
            string output = JsonConvert.SerializeObject(this, Formatting.Indented).Replace("\r\n", "\n");
            File.WriteAllText(path, output);
        }

        public class ClassDoc
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "index")]
            public long Index { get; set; }

            [JsonProperty(PropertyName = "instrs")]
            public List<InstrDoc> Instructions { get; set; }

            public InstrDoc this[int instructionIndex] => Instructions.Find(ins => ins.Index == instructionIndex);
        }

        public class InstrDoc
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "index")]
            public long Index { get; set; }

            [JsonProperty(PropertyName = "args")]
            public ArgDoc[] Arguments { get; set; }

            public ArgDoc this[uint i] => Arguments[i];
        }

        public class ArgDoc
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "type")]
            public long Type { get; set; }

            [JsonProperty(PropertyName = "enum_name")]
            public string EnumName { get; set; }

            [JsonProperty(PropertyName = "default")]
            public long Default { get; set; }

            [JsonProperty(PropertyName = "min")]
            public long Min { get; set; }

            [JsonProperty(PropertyName = "max")]
            public long Max { get; set; }

            [JsonProperty(PropertyName = "increment")]
            public long Increment { get; set; }

            [JsonProperty(PropertyName = "format_string")]
            public string FormatString { get; set; }

            [JsonProperty(PropertyName = "unk1")]
            public long UNK1 { get; set; }

            [JsonProperty(PropertyName = "unk2")]
            public long UNK2 { get; set; }

            [JsonProperty(PropertyName = "unk3")]
            public long UNK3 { get; set; }

            [JsonProperty(PropertyName = "unk4")]
            public long UNK4 { get; set; }

            // Calculated values

            // SoulsIds only
            [JsonIgnore]
            public int Offset { get; set; }

            [JsonIgnore]
            public string DisplayName { get; set; }

            [JsonIgnore]
            public EnumDoc EnumDoc { get; set; }

            [JsonIgnore]
            public DarkScriptType MetaType { get; set; }

            public object GetDisplayValue(object val) => EnumDoc == null ? val : EnumDoc.GetDisplayValue(val);
        }

        public class EnumDoc
        {
            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "values")]
            public Dictionary<string, string> Values { get; set; }

            // Calculated values
            [JsonIgnore]
            public string DisplayName { get; set; }

            [JsonIgnore]
            public Dictionary<string, string> DisplayValues { get; set; }

            public object GetDisplayValue(object val) => DisplayValues.TryGetValue(val.ToString(), out string reval) ? reval : val;
        }

        // Pared down version of DarkScript3 for metadata purposes
        public class DarkScriptDoc
        {
            [JsonProperty(PropertyName = "meta_aliases", Order = 5)]
            public Dictionary<string, List<string>> MetaAliases { get; set; }

            [JsonProperty(PropertyName = "meta_types", Order = 6)]
            public List<DarkScriptType> MetaTypes { get; set; }
        }

        public class DarkScriptType
        {
            // The main canonical arg name for the type
            [JsonProperty(PropertyName = "name", Order = 1)]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "multi_names", Order = 2)]
            public List<string> MultiNames { get; set; }

            [JsonProperty(PropertyName = "cmds", Order = 3)]
            public List<string> Cmds { get; set; }

            // Relevant types: entity, eventflag
            // For full EventValueType coverage: speffect, animation. (npcname covered by FMG)
            [JsonProperty(PropertyName = "data_type", Order = 4)]
            public string DataType { get; set; }

            [JsonProperty(PropertyName = "type", Order = 5)]
            public string Type { get; set; }

            [JsonProperty(PropertyName = "types", Order = 6)]
            public List<string> Types { get; set; }

            [JsonProperty(PropertyName = "override_enum", Order = 7)]
            public string OverrideEnum { get; set; }

            [JsonProperty(PropertyName = "override_types", Order = 8)]
            public Dictionary<string, DarkScriptTypeOverride> OverrideTypes { get; set; }

            public IEnumerable<string> AllTypes => (Type == null ? Array.Empty<string>() : new[] { Type }).Concat(Types ?? new List<string>());
        }

        public class DarkScriptTypeOverride
        {
            // The enum value to select a type.
            [JsonProperty(PropertyName = "value", Order = 1)]
            public int Value { get; set; }
        }
    }
}
