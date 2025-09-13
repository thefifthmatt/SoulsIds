using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using SoulsFormats;
using YamlDotNet.Serialization;
using static SoulsIds.NightreignData;

namespace SoulsIds
{
    public class NightreignData
    {
        public List<SmallBase> SmallBaseData { get; set; }
        public List<AttachPoint> AttachPointData { get; set; }
        public List<StartingPoint> StartingPointData { get; set; }

        // Maps based on above config entries. List format in yaml is still more readable for the actual config
        [YamlIgnore]
        public Dictionary<int, AttachPoint> AttachPoints { get; private set; }
        [YamlIgnore]
        public Dictionary<int, StartingPoint> StartingPoints { get; private set; }
        [YamlIgnore]
        public Dictionary<SmallBaseKey, SmallBase> SmallBases { get; private set; }

        [YamlIgnore]
        public Dictionary<int, PlayArea> PlayAreas { get; private set; }
        [YamlIgnore]
        public SortedDictionary<int, MapPattern> Patterns { get; private set; }

        // Initialize using path to dist/NR/Names/PatternPoint.txt, then call Process with game params
        public static NightreignData Read(string path)
        {
            return new DeserializerBuilder().Build().Deserialize<NightreignData>(File.ReadAllText(path));
        }

        public void Process(ParamDictionary param, int reqPatternId = -1)
        {
            // First make some maps for config data (will fail if any duplicate keys)
            AttachPoints = AttachPointData.ToDictionary(a => a.ID, a => a);
            StartingPoints = StartingPointData.ToDictionary(a => a.ID, a => a);
            SmallBases = SmallBaseData.ToDictionary(a => new SmallBaseKey(a.ID, a.Variation), a => a);
            foreach (AttachPoint attach in AttachPointData)
            {
                if (attach.ParentID > 0)
                {
                    if (!AttachPoints.TryGetValue(attach.ParentID, out AttachPoint parent))
                    {
                        throw new Exception($"Invalid parent {attach.ParentID} in attach data {attach.ID}");
                    }
                    attach.Parent = parent;
                    parent.Child = attach;
                }
            }
            // Read patterns
            Patterns = MapPattern.FromParam(param["LotResultMapPatternFlag"]);
            // Note this includes ones not used by the game, with missing attach entries (519 in Noklateo)
            PlayAreas = GameEditor.ParamToDictionary(param["PlayAreaCreateParam"], PlayArea.FromRow);
            foreach (PARAM.Row row in param["SortieStartingPoint"].Rows)
            {
                // Only applies to starting points with both names and params.
                // Overall this should be smarter to handle mods which add new starting and attach points.
                if (StartingPoints.TryGetValue(row.ID, out StartingPoint start))
                {
                    int req = (int)row["requireModifier"].Value;
                    int ex1 = (int)row["excludeModifier1"].Value;
                    int ex2 = (int)row["excludeModifier2"].Value;
                    start.RequireModifier = req;
                    start.ExcludeModifiers = new[] { ex1, ex2 }.Where(c => c > 0).ToList();
                }
            }
            foreach (PlayArea playArea in PlayAreas.Values)
            {
                AttachPoints.TryGetValue(playArea.BossAttachID, out AttachPoint bossAttach);
                AttachPoints.TryGetValue(playArea.ExtraBossAttachID, out AttachPoint extraBossAttach);
                playArea.BossAttach = bossAttach;
                playArea.ExtraBossAttach = extraBossAttach;
            }
            // Process patterns
            foreach (PARAM.Row row in param["LotResultPlayAreaParam"].Rows)
            {
                int patternId = (int)row["patternId"].Value;
                if (reqPatternId >= 0 && reqPatternId != patternId)
                {
                    continue;
                }
                if (!Patterns.TryGetValue(patternId, out MapPattern pattern))
                {
                    throw new Exception($"Invalid pattern {patternId} for pattern play area {row.ID}");
                }
                // Process night bosses
                for (int dayNum = 1; dayNum <= 2; dayNum++)
                {
                    int playAreaId = (int)row[$"playArea{dayNum}"].Value;
                    if (!PlayAreas.TryGetValue(playAreaId, out PlayArea playArea))
                    {
                        throw new Exception($"{patternId} has unknown play area {playAreaId}");
                    }
                    // Slightly circular reference between night boss name and attaches
                    NightBoss nightBoss = new NightBoss
                    {
                        Day = dayNum,
                        PlayArea = playArea,
                    };
                    List<string> names = new();
                    for (byte j = 0; j <= 1; j++)
                    {
                        string type = j == 0 ? "boss" : "extraBoss";
                        short baseId = (short)row[$"{type}Id{dayNum}"].Value;
                        if (baseId == -1) continue;
                        int cond = (int)row[$"{type}Modifier{dayNum}"].Value;
                        int attachId = j == 0 ? playArea.BossAttachID : playArea.ExtraBossAttachID;
                        SmallBaseKey baseKey = new SmallBaseKey(baseId, j);
                        string name = "Unknown";
                        // TODO: Validate this
                        if (SmallBases.TryGetValue(baseKey, out SmallBase smallBase))
                        {
                            name = smallBase.FullName;
                            string bossOnlyName = smallBase.Name;
                            if (j == 1 && SmallBases.TryGetValue(new SmallBaseKey(baseId, 0), out SmallBase regularBase))
                            {
                                // Maybe the "Only" should be cosmetic, but this works
                                bossOnlyName = regularBase.Name;
                            }
                            names.Add(bossOnlyName);
                        }
                        AttachPoints.TryGetValue(attachId, out AttachPoint attach);
                        SmallBaseAttach baseVal = new SmallBaseAttach
                        {
                            PatternID = patternId,
                            BaseID = baseKey,
                            AttachID = attachId,
                            Base = smallBase,
                            Attach = attach,
                            Name = name + $" (Day {dayNum})",
                            Modifier = cond,
                            NightBoss = nightBoss,
                        };
                        pattern.Attach.Add(baseVal);
                    }
                    nightBoss.Name = string.Join(" & ", names);
                    pattern.Nights.Add(nightBoss);
                }
            }
            // Camps
            foreach (PARAM.Row row in param["LotResultSmallBaseAndSpot"].Rows)
            {
                int patternId = (int)row["patternId"].Value;
                if (reqPatternId >= 0 && patternId != reqPatternId)
                {
                    continue;
                }
                if (!Patterns.TryGetValue(patternId, out MapPattern pattern))
                {
                    throw new Exception($"Invalid pattern {patternId} for small base attach {row.ID}");
                }
                int attachId = (int)row["attachId"].Value;
                short baseId = (short)(int)row["smallBaseMapId"].Value;
                byte variationId = (byte)row["variationId"].Value;
                byte mapIndex = (byte)row["mapIndex"].Value;
                int mod = (int)row["modifier"].Value;
                SmallBaseKey baseKey = new SmallBaseKey(baseId, variationId);
                string name = "Unknown";
                if (SmallBases.TryGetValue(baseKey, out SmallBase smallBase))
                {
                    name = smallBase.FullName;
                }
                AttachPoints.TryGetValue(attachId, out AttachPoint attach);
                SmallBaseAttach baseVal = new SmallBaseAttach
                {
                    PatternID = patternId,
                    BaseID = baseKey,
                    AttachID = attachId,
                    MapIndex = mapIndex,
                    Base = smallBase,
                    Attach = attach,
                    Name = name,
                    Modifier = mod,
                };
                pattern.Attach.Add(baseVal);
            }
        }

        public enum RareMap
        {
            // This value is for unset config fields only
            Unspecified = 0,
            Default = 10,
            Mountaintop = 11,
            Crater = 12,
            Rotted_Woods = 13,
            Noklateo = 15,
        }
        public static readonly Dictionary<RareMap, string> RareMapNames = EnumNames<RareMap>();

        // May not be unspecified in places this appears, currently
        public enum TargetBoss
        {
            Gladius = 0,
            Adel = 1,
            Gnoster = 2,
            Maris = 3,
            Libra = 4,
            Fulghor = 5,
            Caligo = 6,
            Heolstor = 7,
        }
        public static readonly Dictionary<TargetBoss, string> TargetBossNames = EnumNames<TargetBoss>();

        // Categories have arbitrary ids based on roughly the first map in that category
        public enum BaseCategory
        {
            Default = 0,
            Map_Event = 20_00,
            Fort = 30_00,
            Camp = 32_00,
            Ruins = 34_00,
            Township = 37_90,
            Great_Church = 38_00,
            Sorcerers_Rise = 40_00,
            Church = 41_00,
            Small_Camp = 43_00,
            Event = 45_53,
            Night_Horde = 46_00,
            Evergaol = 46_50,
            Field_Boss = 46_51,
            Strong_Field_Boss = 46_52,
            Arena_Boss = 46_81,
            Night_Boss = 47_70,
            Castle = 49_41,
            None = 10000,
            TODO = 10001,
        }
        public static readonly Dictionary<BaseCategory, string> BaseCategoryNames = EnumNames<BaseCategory>(new()
        {
            [BaseCategory.Sorcerers_Rise] = "Sorcerer's Rise",
        });

        public enum AttachCategory
        {
            Default = 0,
            Major_Base = 100,
            Starter_Major_Base = 121,
            Castle = 190,
            Minor_Base = 300,
            Rotted_Woods = 309,
            Starter_Minor_Base = 350,
            Night_Boss = 500,
            Event = 520,
            Evergaol = 601,
            Night_Horde = 615,
            Field_Boss = 750,
            Arena_Boss = 757,
            Extra_Night_Boss = 800,
            None = 10000,
            TODO = 10001,
        }
        public static readonly Dictionary<AttachCategory, string> AttachCategoryNames = EnumNames<AttachCategory>();

        public record SmallBaseKey(short ID, int Variation)
        {
            public string MapID => NightCoordinator.FormatBaseMap(ID);

            public override string ToString() => $"{ID} variation {Variation}";
        }
        public class SmallBase
        {
            public short ID { get; set; }
            public int Variation { get; set; }
            public BaseCategory Category { get; set; }
            public string Name { get; set; }

            [YamlIgnore]
            public SmallBaseKey Key => new SmallBaseKey(ID, Variation);
            [YamlIgnore]
            public string FullName => $"{BaseCategoryNames[Category]} - {Name}";
            [YamlIgnore]
            public string MapID => NightCoordinator.FormatBaseMap(ID);
        }

        public class AttachPoint
        {
            // From static config
            public int ID { get; set; }
            public AttachCategory Category { get; set; }
            public string Name { get; set; }
            // Set for starter camps and extra night bosses
            public int ParentID { get; set; }
            // Where the attach point is defined. Use via GetAllowed() below.
            public RareMap Require { get; set; }
            public List<RareMap> Exclude { get; set; }
            public bool Invalid { get; set; }
            // <x> <z> global coordinates (see NightCoordinator below, 0 0 is the center of the map image)
            public string GlobalPos { get; set; }

            [YamlIgnore]
            public string FullName => $"{AttachCategoryNames[Category]} - {Name}";
            [YamlIgnore]
            public AttachPoint Parent { get; set; }
            [YamlIgnore]
            public AttachPoint Child { get; set; }

            // Combined from above
            public IEnumerable<RareMap> GetAllowed()
            {
                if (Invalid)
                {
                    return Array.Empty<RareMap>();
                }
                else if (Require != RareMap.Unspecified)
                {
                    return new[] { Require };
                }
                else if (Exclude != null)
                {
                    return RareMaps.All.Except(Exclude);
                }
                return RareMaps.All;
            }
        }

        public class StartingPoint
        {
            // From static config
            public int ID { get; set; }
            public string Name { get; set; }
            public List<RareMap> Exclude { get; set; }

            // From params
            [YamlIgnore]
            public int RequireModifier { get; set; }
            [YamlIgnore]
            public List<int> ExcludeModifiers { get; set; }
        }

        // No config info, read from params, use attach info for everything else
        public class PlayArea
        {
            public int ID { get; set; }
            public string Map { get; set; }
            // X and Z only
            public Vector3 GlobalPos { get; set; }
            public string Quadrant { get; set; }
            public int BossAttachID { get; set; }
            public int ExtraBossAttachID { get; set; }
            // May not be present if attach is unused (unnamed)
            public AttachPoint BossAttach;
            public AttachPoint ExtraBossAttach;

            // From params
            public int RequireModifier { get; set; }
            public List<int> ExcludeModifiers { get; set; }

            public static PlayArea FromRow(PARAM.Row row)
            {
                int id = row.ID;
                byte[] parts = new byte[] { (byte)row["areaNo"].Value, (byte)row["gridXNo"].Value, (byte)row["gridZNo"].Value, 0 };
                string mapId = NightCoordinator.FormatMap(parts);
                Vector3 pos = new Vector3((float)row["posX"].Value, 0, (float)row["posZ"].Value);
                int attachId = (int)row["bossAttachPoint"].Value;
                int extraAttachId = (int)row["extraBossAttachPoint"].Value;
                string quadZ = parts[2] >= 38 ? "North" : "South";
                string quadX = parts[1] >= 44 ? "east" : "west";
                int req = (int)row["requireModifier1"].Value;
                int ex1 = (int)row["excludeModifier1"].Value;
                int ex2 = (int)row["excludeModifier2"].Value;
                return new PlayArea()
                {
                    ID = id,
                    Map = mapId,
                    GlobalPos = NightCoordinator.ToGlobalPos(parts, pos),
                    Quadrant = quadZ + quadX,
                    BossAttachID = attachId,
                    ExtraBossAttachID = extraAttachId,
                    RequireModifier = req,
                    ExcludeModifiers = new[] { ex1, ex2 }.Where(c => c > 0).ToList(),
                };
            }
        }

        public class MapPattern
        {
            // Base fields
            public int ID { get; set; }
            public int SetID { get; set; }
            public TargetBoss TargetBoss { get; set; }
            public RareMap RareMap { get; set; }
            public List<PatternModifier> Modifiers { get; set; } = new();
            // From other params
            public List<SmallBaseAttach> Attach { get; set; } = new();
            public List<NightBoss> Nights { get; set; } = new();

            // Utility
            public string SetName => $"{TargetBoss.GetName()} in {RareMap.GetName()}";

            public PatternModifier GetModifier(int modifierSet, int modifier) =>
                Modifiers.Find(m => m.ModifierSet == modifierSet && m.Modifier == modifier);

            private HashSet<int> condIds;
            public bool IsEligible(int requireModifier, List<int> excludeModifiers)
            {
                if (excludeModifiers == null)
                {
                    // May happen if missing param row. Default to disallow here.
                    return false;
                }
                condIds ??= new(Modifiers.Where(c => c.Modifier > 0).Select(c => c.Modifier));
                if (requireModifier > 0 && !condIds.Contains(requireModifier))
                {
                    return false;
                }
                if (excludeModifiers.Count > 0 && condIds.Overlaps(excludeModifiers))
                {
                    return false;
                }
                return true;
            }

            public static SortedDictionary<int, MapPattern> FromParam(PARAM lotResultMapPatternFlagParam)
            {
                SortedDictionary<int, MapPattern> patterns = new();
                foreach (PARAM.Row row in lotResultMapPatternFlagParam.Rows)
                {
                    int patternId = (int)row["patternId"].Value;
                    int modifier = (int)row["modifier"].Value;
                    int modifierSet = (int)row["modifierSet"].Value;
                    uint eventFlag = (uint)row["eventFlag"].Value;
                    int patternSetId = (short)row["patternSetId"].Value;
                    int rareMap = (int)row["rareMap"].Value;
                    int targetBoss = (short)row["targetBoss"].Value;
                    if (!patterns.TryGetValue(patternId, out MapPattern pattern))
                    {
                        patterns[patternId] = pattern = new MapPattern
                        {
                            ID = patternId,
                            SetID = patternSetId,
                            TargetBoss = TargetBosses.From(targetBoss),
                            RareMap = RareMaps.From(rareMap),
                        };
                    }
                    pattern.Modifiers.Add(new PatternModifier(modifierSet, modifier, eventFlag));
                }
                return patterns;
            }
        }

        public record PatternModifier(int ModifierSet, int Modifier, uint EventFlag);

        public class SmallBaseAttach
        {
            public int PatternID { get; set; }
            public int AttachID { get; set; }
            public SmallBaseKey BaseID { get; set; }
            public int MapIndex { get; set; }
            // May be absent if invalid ids
            public AttachPoint Attach { get; set; }
            public SmallBase Base { get; set; }
            // Base's name, also includes the day for Night Bosses
            public string Name { get; set; }
            // Boss modifiers, probably to prevent duplicate night boss/event placement
            public int Modifier { get; set; }
            // Only for night bosses, which are added here despite not being in LotResultSmallBaseAndSpot
            public NightBoss NightBoss { get; set; }
        }

        // PlayArea for a specific pattern
        public class NightBoss
        {
            public int Day { get; set; }
            // Combined name of night boss here
            public string Name { get; set; }
            public PlayArea PlayArea { get; set; }
            public int PlayAreaID => PlayArea.ID;
        }

        public static class NightCoordinator
        {
            // Utility
            public static string FormatMap(byte[] bytes) => "m" + string.Join("_", bytes.Select(b => b == 0xFF ? "XX" : $"{b:d2}"));
            public static byte[] SplitMap(string map) => map.TrimStart('m').Split('_').Select(p => byte.Parse(p)).ToArray();
            public static byte[] SplitMap(int id) => new byte[] { (byte)(id / 1_00_00_00 % 100), (byte)(id / 1_00_00 % 100), (byte)(id / 1_00 % 100), (byte)(id % 100) };
            // Nightreign-specific
            public static string FormatBaseMap(short baseId) => FormatMap(SplitBaseMap(baseId));
            public static byte[] SplitBaseMap(short baseId) => new byte[] { (byte)(baseId / 100), (byte)(baseId % 100), 0, 0 };

            public static Vector3 ToGlobalPos(byte[] tileMap, Vector3 pos)
            {
                // This coordinate system is centered around the center of the map (bottom-left corner of m60_44_38_00)
                // The bottom-left corner of the entire map is (-768, -768) and top-right corner is (768, 768)
                float globalX = 256 * (tileMap[1] - 44) + 128 + pos.X;
                float globalZ = 256 * (tileMap[2] - 38) + 128 + pos.Z;
                return new Vector3(globalX, 0, globalZ);
            }

            public static Vector3 OverworldGlobalPos(Vector3 pos)
            {
                // For objects in overworld map (m60_00_00_99)
                byte[] globalMap = new byte[] { 60, 42, 36, 0 };
                return ToGlobalPos(globalMap, pos + new Vector3(-128, 0, -640));
            }
        }

        // In lieu of more advanced localization. Requires unique values and category-type names.
        public static Dictionary<T, string> EnumNames<T>(Dictionary<T, string> overrides = null) where T : struct, Enum
        {
            Dictionary<T, string> dict = Enum.GetValues<T>().Cast<T>()
                .Where(v => (int)(object)v < 10000)
                .ToDictionary(v => v, v => v.ToString().Replace('_', ' '));
            if (overrides != null)
            {
                foreach (var e in overrides)
                {
                    dict[e.Key] = e.Value;
                }
            }
            return dict;
        }
    }

    public static class RareMaps
    {
        public static int GetID(this RareMap map)
        {
            if (map == RareMap.Unspecified)
            {
                throw new Exception($"Invalid value for RareMap.Unspecified");
            }
            return (int)map - 10;
        }
        public static int GetModifier(this RareMap map) => (int)map;
        public static string GetName(this RareMap map) => RareMapNames[map];
        // Ideally this would be defined on RareMap itself like in Java. Beware these do no validation
        public static RareMap From(int id) => (RareMap)(id + 10);
        public static RareMap FromModifier(int id) => (RareMap)id;

        public static readonly IReadOnlyCollection<RareMap> All = new List<RareMap>()
        {
            RareMap.Default, RareMap.Mountaintop, RareMap.Crater, RareMap.Rotted_Woods, RareMap.Noklateo
        }.AsReadOnly();
    }

    public static class TargetBosses
    {
        public static int GetID(this TargetBoss boss) => (int)boss;
        public static string GetName(this TargetBoss boss) => TargetBossNames[boss];
        public static TargetBoss From(int id) => (TargetBoss)id;

        public static readonly IReadOnlyCollection<TargetBoss> All = Enum.GetValues<TargetBoss>().ToList().AsReadOnly();
    }
}
