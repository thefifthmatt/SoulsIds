using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SoulsIds
{
    public class Universe
    {
        public static Dictionary<uint, int> LotTypes = new Dictionary<uint, int>
        {
            { 0x00000000, 0 },
            { 0x10000000, 1 },
            { 0x20000000, 2 },
            { 0x40000000, 3 }
        };

        public enum Namespace
        {
            // Meta namespaces
            Global,
            Item,
            // More-or-less unique namespaces
            Event,
            EventFlag,
            ESD,
            Lot,
            Shop,
            NPC,
            Material,
            Skill,
            // Item types in order
            Weapon, // 0
            Protector, // 1
            Accessory, // 2
            Goods, // 3
            Gem, // 4 in Elden Ring
            Arts, // 5 in Elden Ring, but not supported by scripting
            Booster, // 4 in AC6 shop - 6 here
            Fcs, // 5 in AC6 shop - 7 here
            Generator, // 6 in AC6 shop - 5 here
            // End of items
            Talk,
            Dialogue,
            Entity,
            Part,
            ObjModel,
            ChrModel,
            Treasure,
            Map,
            Bonfire,
            Human,
            Action,
            ActionButton,
            ActionButtonText,
            Gesture,
            // AC6
            Account,
            Mission,
            Arena, // TODO
            Tutorial,
            // In future
            Cutscene,
            Achievement,
            Animation,
            SpEffect,
            SFX,
        }
        private static HashSet<Namespace> Quotes = new HashSet<Namespace> { Namespace.Action, Namespace.Dialogue, Namespace.Talk, Namespace.ActionButton };
        public class Obj : IComparable<Obj>
        {
            public string ID { get; set; }
            public Namespace Type { get; set; }
            public int RangeEnd { get; set; }

            public override string ToString() => $"{Type.ToString().ToLower()}:{ID}" + (RangeEnd == -1 ? "" : $":{RangeEnd}");
            public override bool Equals(object obj) => obj is Obj o && Equals(o);
            public bool Equals(Obj o) => Type == o.Type && ID.Equals(o.ID) && RangeEnd == o.RangeEnd;
            public override int GetHashCode() => ((int)Type) << 24 ^ ID.GetHashCode() ^ RangeEnd;
            public int CompareTo(Obj o)
            {
                int tcomp = Type.CompareTo(o.Type);
                if (tcomp != 0) return tcomp;
                if (int.TryParse(ID, out int id1) && int.TryParse(o.ID, out int id2))
                {
                    return Nest(id1.CompareTo(id2), RangeEnd.CompareTo(o.RangeEnd));
                }
                return Nest(ID.CompareTo(o.ID), RangeEnd.CompareTo(o.RangeEnd));
            }
            public bool HasType(Namespace n)
            {
                if (n == Namespace.Global) return true;
                if (n == Namespace.Item)
                {
                    return (int)Type >= (int)Namespace.Weapon && (int)Type <= (int)Namespace.Goods;
                }
                return n == Type;
            }

            private Obj(object ID, Namespace Type, int RangeEnd=-1)
            {
                this.ID = ID.ToString();
                this.Type = Type;
                this.RangeEnd = RangeEnd;
            }

            // Helpers
            public static Obj Lot(int id) => new Obj(id, Namespace.Lot);
            public static Obj Shop(int id, int end=-1) => new Obj(id, Namespace.Shop, end);
            public static Obj EventFlag(int id, int end = -1) => new Obj(id, Namespace.EventFlag, end);
            public static Obj EventFlag(uint id, int end = -1) => new Obj(id, Namespace.EventFlag, end);
            public static Obj Talk(int id) => new Obj(id, Namespace.Talk);
            public static Obj Action(int id) => new Obj(id, Namespace.Action);
            public static Obj Esd(int id) => new Obj(id, Namespace.ESD);
            public static Obj Npc(int id) => new Obj(id, Namespace.NPC);
            public static Obj Material(int id) => new Obj(id, Namespace.Material);
            public static Obj Skill(int id) => new Obj(id, Namespace.Skill);
            public static Obj Map(string id) => new Obj(id, Namespace.Map);
            public static Obj Entity(int id) => new Obj(id, Namespace.Entity);
            public static Obj Part(string map, string id) => new Obj($"{map}_{id}", Namespace.Part);
            public static Obj ObjModel(string id) => new Obj(id, Namespace.ObjModel);
            public static Obj ChrModel(string id) => new Obj(id, Namespace.ChrModel);
            public static Obj Treasure(string map, int index) => new Obj($"{map}_{index}", Namespace.Treasure);
            public static Obj Dialogue(int id) => new Obj(id, Namespace.Dialogue);
            public static Obj Bonfire(int id) => new Obj(id, Namespace.Bonfire);
            public static Obj Human(int id) => new Obj(id, Namespace.Human);

            // For names
            public static Obj Of(Namespace type, object id) => new Obj(id, type);

            public static Obj Item(uint type, int id)
            {
                if (!LotTypes.TryGetValue(type, out int itemType))
                {
                    if (type <= 5)
                    {
                        itemType = (int)type;
                    }
                    else return UnknownItem(type, id);
                }
                return Of(Namespace.Weapon + itemType, id);
            }

            public static Obj AC6Item(int type, int id)
            {
                Namespace n;
                if (type >= 0 && type < 4)
                {
                    n = Namespace.Weapon + type;
                }
                // idk. It's 4 5 6 in EquipmentLineupParam?
                else if (type == 6) n = Namespace.Booster;
                else if (type == 7) n = Namespace.Fcs;
                else if (type == 5) n = Namespace.Generator;
                else return UnknownItem(type, id);
                return Of(n, id);
            }

            private static Obj UnknownItem(object type, object id) => new Obj($"{type}:{id}", Namespace.Item);
        }
        public enum Verb
        {
            // Checks the status of (event flag, entity health, item ownership)
            READS,
            // Changes the status of (event flag, entity enabling, other state)
            WRITES,
            // Produces items or item lots
            PRODUCES,
            // Consumes items
            CONSUMES,
            // Misc 1:n or n:1 relationships: map contains entity id, which contains parts. Parts contain npc/obj/chr. NPC contains ESD.
            CONTAINS,
            // If has a msg id.
            HAS_TEXT,
            // ESD state transitions and event intializations. Not used for now... control flow probably shouldn't be here
            // STARTS,
        }
        public static readonly List<string> ActiveVerb = new List<string> { "reads", "writes", "produces", "consumes", "contains", "has text" };
        public static readonly List<string> PassiveVerb = new List<string> { "read by", "written by", "produced by", "consumed by", "contained in", "used in" };
        public class Relation
        {
            public Obj From { get; set; }
            public Obj To { get; set; }
            public Verb Verb { get; set; }
        }
        public class Node
        {
            public Obj Obj { get; set; }
            public List<Relation> From { get; set; }
            public List<Relation> To { get; set; }
        }
        public readonly Dictionary<Obj, Node> Nodes = new Dictionary<Obj, Node>();
        public readonly Dictionary<Obj, string> Names = new Dictionary<Obj, string>();
        public Universe()
        {
        }
        public string Name(Obj obj)
        {
            if (Names.TryGetValue(obj, out string name))
            {
                return $"{obj}:{(Quotes.Contains(obj.Type) ? $"\"{name}\"" : name)}";
            }
            return $"{obj}";
        }
        public List<Obj> Next(Obj obj, Verb v, Namespace type=Namespace.Global)
        {
            if (!Nodes.ContainsKey(obj)) return new List<Obj>();
            return Nodes[obj].To.Where(r => r.Verb == v && r.To.HasType(type)).Select(r => r.To).ToList();
        }
        public List<Obj> Prev(Obj obj, Verb v, Namespace type=Namespace.Global)
        {
            if (!Nodes.ContainsKey(obj)) return new List<Obj>();
            return Nodes[obj].From.Where(r => r.Verb == v && r.From.HasType(type)).Select(r => r.From).ToList();
        }
        public Node Add(Obj obj)
        {
            if (!Nodes.ContainsKey(obj))
            {
                Nodes[obj] = new Node
                {
                    Obj = obj,
                    From = new List<Relation>(),
                    To = new List<Relation>(),
                };
            }
            return Nodes[obj];
        }
        // Makes a link between two objects. Does not check if the link already exists.
        public void Add(Verb verb, Obj from, Obj to)
        {
            Node fromNode = Add(from);
            Node toNode = Add(to);
            Relation rel = new Relation { From = from, To = to, Verb = verb };
            fromNode.To.Add(rel);
            toNode.From.Add(rel);
        }

        public class PartialRelation
        {
            public Obj Obj { get; set; }
            public Verb Verb { get; set; }
            public bool Subject { get; set; }
            // <unknown object> verbs given object
            public static PartialRelation Verbs(Verb verb, Obj obj)
            {
                return new PartialRelation { Obj = obj, Verb = verb, Subject = false };
            }
            // given object verbs <unknown object>
            // Different arg order is because English. And this is a helper method
            public static PartialRelation VerbedBy(Obj obj, Verb verb)
            {
                return new PartialRelation { Obj = obj, Verb = verb, Subject = true };
            }
            public Relation Complete(Obj other)
            {
                return new Relation { From = Subject ? Obj : other, To = Subject ? other : Obj, Verb = Verb };
            }

            public override bool Equals(object obj) => obj is PartialRelation o && Equals(o);
            public bool Equals(PartialRelation o) => Obj.Equals(o.Obj) && Verb == o.Verb && Subject == o.Subject;
            public override int GetHashCode() => ((int)Verb) << 24 ^ Obj.GetHashCode() ^ (Subject ? 0 : 0xFFFF);
        }
        public class RelationSpec
        {
            public Verb Verb { get; set; }
            public bool Subject { get; set; }
            // Optional
            public Namespace Type { get; set; }
            // Opposite subject treatment from PartialRelation, hm. Should give PartialRelation a better name
            public static RelationSpec Verbs(Verb verb, Namespace type=Namespace.Global)
            {
                return new RelationSpec { Verb = verb, Subject = true, Type = type };
            }
            public static RelationSpec VerbedBy(Verb verb, Namespace type = Namespace.Global)
            {
                return new RelationSpec { Verb = verb, Subject = false, Type = type };
            }
        }
        public List<Obj> Connected(Obj obj, RelationSpec rel)
        {
            return rel.Subject ? Next(obj, rel.Verb, rel.Type) : Prev(obj, rel.Verb, rel.Type);
        }

        private static int Nest(int first, int second)
        {
            return first != 0 ? first : second;
        }
    }
}
