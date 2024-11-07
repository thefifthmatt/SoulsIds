using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SoulsFormats;

namespace SoulsIds
{
    // Altered version of AST.cs from ESDLang
    public class AST
    {
        public enum FalseCond
        {
            NONE, CONTINUE, ABORT
        }

        public abstract class Expr
        {
            // Any special handling if the expression is false. Seems to be no-op for game ESDs in DS3 and on, used for optimization.
            public FalseCond IfFalse { get; set; }

            public int AsInt()
            {
                if (TryAsInt(out int i)) return i;
                throw new Exception($"{this} cannot be used as an int");
            }

            public bool IsInt(int check)
            {
                return TryAsInt(out int val) && val == check;
            }

            public virtual bool TryAsInt(out int i)
            {
                i = 0;
                return false;
            }

            public sealed override string ToString() => ToDisplayString(null);
            public string ToDisplayString(string parent = null, bool nonCommute = false)
            {
                Expr expr = this;
                string s;
                if (expr is ConstExpr ce)
                {
                    if (ce.Value is float f) s = f.ToString("R");
                    else if (ce.Value is double d) s = d.ToString("R");
                    else if (ce.Value is string s2) s = $"\"{s2}\"";
                    else s = $"{ce.Value}";
                }
                else if (expr is UnaryExpr ue)
                {
                    s = $"{ue.Op}{ue.Arg.ToDisplayString($"u{ue.Op}")}";
                }
                else if (expr is BinaryExpr be)
                {
                    // Do some adhoc simplification here
                    string op = be.Op;
                    s = $"{be.Lhs.ToDisplayString(op)} {op} {be.Rhs.ToDisplayString(op, true)}";
                    if (parent != null)
                    {
                        if (eqs.Contains(op) && (parent == "&&" || parent == "||"))
                        {
                            // No paren required
                        }
                        else if (nonCommute || !commutes.ContainsKey(op) || !commutes.ContainsKey(parent) || commutes[op] != commutes[parent])
                        {
                            s = $"({s})";
                        }
                    }
                }
                else if (expr is FunctionCall func)
                {
                    s = func.Name == "StateGroupArg" ? $"{func.Name}[{func.Args[0]}]" : $"{func.Name}({string.Join(", ", func.Args)})";
                }
                else if (expr is CallResult)
                {
                    // No special language for this in zeditor yet
                    s = "call.Get()";
                }
                else if (expr is CallOngoing)
                {
                    s = "call.Ongoing()";
                }
                else if (expr is Unknown huh)
                {
                    s = $"#{huh.Opcode:X2}";
                }
                else throw new Exception($"Unknown expression subclass {expr.GetType()}");
                // if (IfFalse == FalseCond.ABORT) return $"AbortIfFalse({s})";
                return s;
            }

            private static readonly HashSet<string> eqs = new HashSet<string> { "==", "!=", "<=", ">=", ">", "<" };
            private static readonly Dictionary<string, int> commutes = new Dictionary<string, int>
            {
                ["+"] = 0,
                ["-"] = 0,
                ["*"] = 1,
                ["/"] = 1,
                ["&&"] = 2,
                ["||"] = 3,
            };

            public Expr Copy()
            {
                return Visit(AstVisitor.Pre(e =>
                {
                    e = (Expr)e.MemberwiseClone();
                    if (e is FunctionCall f) f.Args = f.Args.ToList();
                    return e;
                }));
            }

            public Expr Visit(AstVisitor visitor, Expr parent = null)
            {
                Expr expr = this;
                Expr newExpr = visitor.Previsit?.Invoke(expr);
                if (newExpr != null) expr = newExpr;
                newExpr = visitor.PrevisitParent?.Invoke(expr, parent);
                if (newExpr != null) expr = newExpr;
                if (expr is FunctionCall f)
                {
                    for (int i = 0; i < f.Args.Count; i++)
                    {
                        f.Args[i] = f.Args[i].Visit(visitor, expr);
                    }
                }
                else if (expr is BinaryExpr b)
                {
                    b.Lhs = b.Lhs.Visit(visitor, expr);
                    b.Rhs = b.Rhs.Visit(visitor, expr);
                }
                else if (expr is UnaryExpr u)
                {
                    u.Arg = u.Arg.Visit(visitor, expr);
                }
                newExpr = visitor.Postvisit?.Invoke(expr);
                if (newExpr != null) expr = newExpr;
                newExpr = visitor.PostvisitParent?.Invoke(expr, parent);
                if (newExpr != null) expr = newExpr;
                return expr;
            }
        }

        public class ConstExpr : Expr
        {
            // Can be sbyte, float, double, int, string
            public object Value { get; set; }
            public override bool TryAsInt(out int o)
            {
                if (Value is sbyte sb)
                {
                    o = sb;
                    return true;
                }
                if (Value is int i)
                {
                    o = i;
                    return true;
                }
                o = 0;
                return false;
            }
            public override int GetHashCode() => Value.GetType().GetHashCode() ^ Value.ToString().GetHashCode();
            public override bool Equals(object obj) => obj is ConstExpr o && Equals(o);
            public bool Equals(ConstExpr o) => Value.Equals(o.Value);
        }

        public class FunctionCall : Expr
        {
            // Used to represent actual functions as well as built-in ops which can be written as functions
            // For real functions, expr is either the function name if known or f<number> if unknown.
            // Built-in functions:
            // SetReg[0-7](<expr>)
            // GetReg[0-7]()
            // StateGroupArg[<index>] (with brackets instead of parens for fun)
            public string Name { get; set; }
            public List<Expr> Args { get; set; }
        }

        public class BinaryExpr : Expr
        {
            // Supported ops: + - * / <= >= < > == != && ||
            public string Op { get; set; }
            public Expr Lhs { get; set; }
            public Expr Rhs { get; set; }
        }

        public class UnaryExpr : Expr
        {
            // Supported ops: N
            public string Op { get; set; }
            public Expr Arg { get; set; }
        }

        public class CallResult : Expr { }

        public class CallOngoing : Expr { }

        public class Unknown : Expr
        {
            public byte Opcode { get; set; }
        }

        public class AstVisitor
        {
            public static AstVisitor Pre(Func<Expr, Expr> func) => new AstVisitor { Previsit = func };
            public static AstVisitor Pre(Func<Expr, Expr, Expr> func) => new AstVisitor { PrevisitParent = func };
            public static AstVisitor PreAct(Action<Expr> func) => new AstVisitor { Previsit = e => { func(e); return null; } };
            public static AstVisitor Post(Func<Expr, Expr> func) => new AstVisitor { Postvisit = func };
            public static AstVisitor Post(Func<Expr, Expr, Expr> func) => new AstVisitor { PostvisitParent = func };
            public static AstVisitor PostAct(Action<Expr> func) => new AstVisitor { Postvisit = e => { func(e); return null; } };

            // AST visitor functions.
            // If they return a non-null value, that node will be replaced in the tree, or returned at the top level.
            // This is a bit complex but probably better than setting every node every time.
            public Func<Expr, Expr> Previsit { get; set; }
            public Func<Expr, Expr, Expr> PrevisitParent { get; set; }
            public Func<Expr, Expr> Postvisit { get; set; }
            public Func<Expr, Expr, Expr> PostvisitParent { get; set; }
        }

        public static Expr DisassembleExpression(byte[] Bytes)
        {
            bool IsBigEndian = false;
            byte[] bigEndianReverseBytes = IsBigEndian ? Bytes.Reverse().ToArray() : null;

            Stack<Expr> exprs = new Stack<Expr>();
            List<Expr> popArgs(int amount)
            {
                List<Expr> args = new List<Expr>();
                for (int i = 0; i < amount; i++)
                {
                    args.Add(exprs.Pop());
                }
                args.Reverse();
                return args;
            }
            string GetFunctionInfo(int id)
            {
                return $"f{id}";
            }

            for (int i = 0; i < Bytes.Length; i++)
            {
                byte b = Bytes[i];
                if (b >= 0 && b <= 0x7F)
                {
                    exprs.Push(new ConstExpr { Value = (sbyte)(b - 64) });
                }
                else if (b == 0xA5)
                {
                    int j = 0;
                    while (Bytes[i + j + 1] != 0 || Bytes[i + j + 2] != 0)
                        j += 2;
                    string text = IsBigEndian
                        ? Encoding.BigEndianUnicode.GetString(Bytes, i + 1, j)
                        : Encoding.Unicode.GetString(Bytes, i + 1, j);

                    if (text.Contains('"') || text.Contains('\r') || text.Contains('\n'))
                        throw new Exception("Illegal character in string literal");
                    exprs.Push(new ConstExpr { Value = text });
                    i += j + 2;
                }
                else if (b == 0x80)
                {
                    float val;
                    if (!IsBigEndian)
                    {
                        val = BitConverter.ToSingle(Bytes, i + 1);
                    }
                    else
                    {
                        val = BitConverter.ToSingle(bigEndianReverseBytes, (bigEndianReverseBytes.Length - 1) - (i + 1) - 4);
                    }
                    exprs.Push(new ConstExpr { Value = val });

                    i += 4;
                }
                else if (b == 0x81)
                {
                    double val;
                    if (!IsBigEndian)
                    {
                        val = BitConverter.ToDouble(Bytes, i + 1);
                    }
                    else
                    {
                        val = BitConverter.ToDouble(bigEndianReverseBytes, (bigEndianReverseBytes.Length - 1) - (i + 1) - 8);
                    }
                    exprs.Push(new ConstExpr { Value = val });

                    i += 8;
                }
                else if (b == 0x82)
                {
                    int val;
                    if (!IsBigEndian)
                    {
                        val = BitConverter.ToInt32(Bytes, i + 1);
                    }
                    else
                    {
                        val = BitConverter.ToInt32(bigEndianReverseBytes, (bigEndianReverseBytes.Length - 1) - (i + 1) - 4);
                    }
                    exprs.Push(new ConstExpr { Value = val });

                    i += 4;
                }
                else if (b == 0x84)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(0), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x85)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(1), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x86)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(2), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x87)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(3), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x88)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(4), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x89)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(5), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (b == 0x8A)
                {
                    exprs.Push(new FunctionCall { Args = popArgs(6), Name = GetFunctionInfo(exprs.Pop().AsInt()) });
                }
                else if (OperatorsByByte.ContainsKey(b))
                {
                    if (UnaryOperators.Contains(b))
                    {
                        exprs.Push(new UnaryExpr { Op = OperatorsByByte[b], Arg = exprs.Pop() });
                    }
                    else
                    {
                        exprs.Push(new BinaryExpr { Op = OperatorsByByte[b], Rhs = exprs.Pop(), Lhs = exprs.Pop() });
                    }
                }
                else if (b == 0xA6)
                {
                    Expr top = exprs.Peek();
                    top.IfFalse = FalseCond.CONTINUE;
                }
                else if (b >= 0xA7 && b <= 0xAE)
                {
                    byte regIndex = (byte)(b - 0xA7);
                    exprs.Push(new FunctionCall { Args = popArgs(1), Name = $"SetREG{regIndex}" });
                }
                else if (b >= 0xAF && b <= 0xB6)
                {
                    byte regIndex = (byte)(b - 0xAF);
                    exprs.Push(new FunctionCall { Args = popArgs(0), Name = $"GetREG{regIndex}" });
                }
                else if (b == 0xB7)
                {
                    Expr top = exprs.Peek();
                    top.IfFalse = FalseCond.ABORT;
                }
                else if (b == 0xB8)
                {
                    // exprs.Push(new FunctionCall { Args = popArgs(1), Name = "StateGroupArg" });
                    FunctionCall func = new FunctionCall { Args = popArgs(1), Name = "StateGroupArg" };
                    ConstExpr ce = func.Args[0] as ConstExpr;
                    // Console.WriteLine($"{ce} {ce.Value.GetType()}");
                    exprs.Push(func);
                }
                else if (b == 0xB9)
                {
                    exprs.Push(new CallResult());
                }
                else if (b == 0xBA)
                {
                    // This opcode just returns a constant value 0x7FFFFFFF
                    // But use higher-level representation of it
                    exprs.Push(new CallOngoing());
                }
                else if (b == 0xA1)
                {
                    // Terminator, should be redundant.
                    // break;
                }
                else
                {
                    exprs.Push(new Unknown { Opcode = b });
                }
            }
            if (exprs.Count != 1) throw new Exception("Could not parse expr. Remaining stack: " + string.Join("; ", exprs) + $"; = {string.Join(" ", Bytes.Select(x => x.ToString("X2")))}");
            return exprs.Pop();
        }

        public static byte[] AssembleExpression(Expr topExpr)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms, Encoding.Unicode))
            {
                void writeExpr(Expr expr)
                {
                    // Number
                    if (expr is ConstExpr ce)
                    {
                        if (ce.Value is int i)
                        {
                            bw.Write((byte)0x82);
                            bw.Write(i);
                        }
                        else if (ce.Value is sbyte b)
                        {
                            bw.Write((byte)(b + 64));
                        }
                        else if (ce.Value is float f)
                        {
                            bw.Write((byte)0x80);
                            bw.Write(f);
                        }
                        else if (ce.Value is double d)
                        {
                            bw.Write((byte)0x81);
                            bw.Write(d);
                        }
                        else if (ce.Value is string s)
                        {
                            if (s.Contains('\r') || s.Contains('\n'))
                                throw new Exception("String literals may not contain newlines");
                            bw.Write((byte)0xA5);
                            bw.Write(Encoding.Unicode.GetBytes(s + "\0"));
                        }
                        else throw new Exception($"Invalid type {ce.Value.GetType()} for ConstExpr {ce} in {topExpr}");
                    }
                    else if (expr is UnaryExpr ue)
                    {
                        writeExpr(ue.Arg);
                        bw.Write(BytesByOperator[ue.Op]);
                    }
                    else if (expr is BinaryExpr be)
                    {
                        writeExpr(be.Lhs);
                        writeExpr(be.Rhs);
                        bw.Write(BytesByOperator[be.Op]);
                    }
                    else if (expr is FunctionCall func)
                    {
                        string name = func.Name;
                        if (name.StartsWith("SetREG"))
                        {
                            int regIndex = byte.Parse(name.Substring(name.Length - 1));
                            writeExpr(func.Args[0]);
                            bw.Write((byte)(0xA7 + regIndex));
                        }
                        else if (name.StartsWith("GetREG"))
                        {
                            int regIndex = byte.Parse(name.Substring(name.Length - 1));
                            bw.Write((byte)(0xAF + regIndex));
                        }
                        else if (name.StartsWith("StateGroupArg"))
                        {
                            int index = func.Args[0].AsInt();
                            bw.Write((byte)(index + 64));
                            bw.Write((byte)0xB8);
                        }
                        else
                        {
                            // In ESDLang, this is context.GetFunctionID(name)
                            int id = int.Parse(name.Substring(1));
                            if (id >= -64 && id <= 63)
                            {
                                bw.Write((byte)(id + 64));
                            }
                            else
                            {
                                bw.Write((byte)0x82);
                                bw.Write(id);
                            }
                            // for (int i = func.Args.Count - 1; i >= 0; i--)
                            for (int i = 0; i < func.Args.Count; i++)
                            {
                                writeExpr(func.Args[i]);
                            }
                            bw.Write((byte)(0x84 + func.Args.Count));
                        }
                    }
                    else if (expr is CallResult)
                    {
                        bw.Write((byte)0xB9);
                    }
                    else if (expr is CallOngoing)
                    {
                        bw.Write((byte)0xBA);
                    }
                    else if (expr is Unknown huh)
                    {
                        bw.Write(huh.Opcode);
                    }
                    else throw new Exception($"Unknown expression subclass {expr.GetType()}: {expr} in {topExpr}");
                    if (expr.IfFalse == FalseCond.CONTINUE)
                    {
                        bw.Write(BytesByTerminator['~']);
                    }
                    else if (expr.IfFalse == FalseCond.ABORT)
                    {
                        bw.Write(BytesByTerminator['.']);
                    }
                }
                writeExpr(topExpr);
                bw.Write((byte)0xA1);
                byte[] arr = ms.ToArray();
                if (arr.Length == 1) throw new Exception($"{topExpr}");
                return arr;
            }
        }

        public static Dictionary<byte, string> OperatorsByByte = new Dictionary<byte, string>
        {
            [0x8C] = "+",
            // Negate
            [0x8D] = "N",
            [0x8E] = "-",
            [0x8F] = "*",
            [0x90] = "/",
            [0x91] = "<=",
            [0x92] = ">=",
            [0x93] = "<",
            [0x94] = ">",
            [0x95] = "==",
            [0x96] = "!=",
            [0x98] = "&&",
            [0x99] = "||",
            [0x9A] = "!",
        };
        public static Dictionary<string, byte> BytesByOperator = OperatorsByByte.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        public static byte[] UnaryOperators = new byte[] { 0x8D, 0x9A };
        public static Dictionary<byte, string> TerminatorsByByte = new Dictionary<byte, string>
        {
            [0xA6] = "~",
            [0xB7] = ".",
        };
        public static Dictionary<char, byte> BytesByTerminator = TerminatorsByByte.ToDictionary(kvp => kvp.Value[0], kvp => kvp.Key);

        // Misc utility functions too, why not
        public static string FormatMachine(int id)
        {
            string asHex = $"{id:X}";
            if (asHex.StartsWith("7FFF"))
            {
                id = 0x7FFFFFFF - id;
                return "x" + id;
            }
            return id.ToString();
        }
        public static string FormatMachine(long id) => FormatMachine((int)id);

        public static int MachineForIndex(int diffpart) => 0x7FFFFFFF - diffpart;

        public static int ParseMachine(string mIdStr)
        {
            if (!ParseMachine(mIdStr, out int id)) throw new Exception($"Internal error: invalid machine id {id}");
            return id;
        }

        public static bool ParseMachine(string mIdStr, out int mId)
        {
            if (!int.TryParse(mIdStr, out mId))
            {
                if (mIdStr.StartsWith("x") && int.TryParse(mIdStr.Substring(1), out int diffpart))
                {
                    mId = 0x7FFFFFFF - diffpart;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        // Easier programmatic creation of things
        public static Expr MakeVal(object val)
        {
            return new ConstExpr { Value = val };
        }

        public static Expr MakeArg(int arg)
        {
            return new FunctionCall
            {
                Name = "StateGroupArg",
                Args = new List<Expr> { MakeVal(arg) },
            };
        }

        // Could also do fancy int casting here to support enums
        private static Expr MakeExpr(object a) => a is Expr e ? e : MakeVal(a);

        public static Expr MakeFunction(string name, params object[] args)
        {
            return new FunctionCall
            {
                Name = name,
                Args = args.Select(MakeExpr).ToList(),
            };
        }

        public static ESD.CommandCall MakeCommand(int bank, int id, params object[] args)
        {
            ESD.CommandCall call = new ESD.CommandCall(bank, id);
            foreach (object a in args)
            {
                call.Arguments.Add(AssembleExpression(MakeExpr(a)));
            }
            return call;
        }

        public static readonly Expr Pass = MakeVal(1);
        public static Expr NegateCond(Expr expr) => new BinaryExpr { Op = "==", Lhs = expr, Rhs = MakeVal(0) };
        public static Expr Binop(object lhs, string op, object rhs) => new BinaryExpr { Op = op, Lhs = MakeExpr(lhs), Rhs = MakeExpr(rhs) };

        public static Expr ChainExprs(string op, IEnumerable<Expr> parts)
        {
            Expr ret = null;
            foreach (Expr part in parts)
            {
                if (part == null)
                {
                    continue;
                }
                if (ret == null)
                {
                    ret = part;
                }
                else
                {
                    ret = new BinaryExpr { Op = op, Lhs = ret, Rhs = part };
                }
            }
            return ret;
        }

        public static (long, ESD.State) AllocateState(Dictionary<long, ESD.State> states, ref long baseId)
        {
            baseId = Math.Max(0, baseId);
            while (states.ContainsKey(baseId))
            {
                baseId++;
            }
            ESD.State state = new ESD.State();
            states[baseId] = state;
            return (baseId, state);
        }

        public static List<ESD.State> AllocateBranch(Dictionary<long, ESD.State> states, ESD.State main, List<Expr> condExprs, ref long baseId)
        {
            List<ESD.State> alts = new List<ESD.State>();
            foreach (Expr condExpr in condExprs)
            {
                (long next, ESD.State alt) = AllocateState(states, ref baseId);
                ESD.Condition cond = new ESD.Condition(next, AssembleExpression(condExpr));
                main.Conditions.Add(cond);
                alts.Add(alt);
            }
            return alts;
        }

        public static (ESD.State, ESD.State) SimpleBranch(Dictionary<long, ESD.State> states, ESD.State main, Expr cond, ref long baseId)
        {
            List<ESD.State> branches = AllocateBranch(states, main, new List<Expr> { cond, Pass }, ref baseId);
            return (branches[0], branches[1]);
        }

        public static void CallMachine(ESD.State state, long nextState, int machineIndex, params object[] args)
        {
            ESD.CommandCall call = MakeCommand(6, MachineForIndex(machineIndex), args);
            state.EntryCommands.Add(call);
            Expr pendingCall = new BinaryExpr { Op = "!=", Lhs = new CallResult(), Rhs = new CallOngoing() };
            ESD.Condition cond = new ESD.Condition(nextState, AssembleExpression(pendingCall));
            state.Conditions.Add(cond);
        }

        public static void CallState(ESD.State state, long nextState)
        {
            ESD.Condition cond = new ESD.Condition(nextState, AssembleExpression(Pass));
            state.Conditions.Add(cond);
        }

        public static void CallReturn(ESD.State state, int val)
        {
            ESD.Condition cond = new ESD.Condition();
            cond.Evaluator = AssembleExpression(Pass);
            ESD.CommandCall call = MakeCommand(7, -1, val);
            cond.PassCommands.Add(call);
            state.Conditions.Add(cond);
        }

        public static long GetFollowState(Dictionary<long, ESD.State> states, ESD.State state)
        {
            if (state.Conditions.Count != 1
                || !DisassembleExpression(state.Conditions[0].Evaluator).IsInt(1)
                || state.Conditions[0].TargetState is not long toState
                || !states.ContainsKey(toState))
            {
                throw new Exception("Grace state machine has unexpected structure, can't edit it");
            }
            return toState;
        }
    }
}
