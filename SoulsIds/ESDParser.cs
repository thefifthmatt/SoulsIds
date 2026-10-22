using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SoulsIds
{
    /// <summary>
    /// A class that parses text into <see cref="AST.Expr"/>s and <see cref="AST.Command"/>s.
    /// </summary>
    /// <remarks>This follows C#'s operator precedence rules.</remarks>
    public class ESDParser
    {
        /// <summary>The reader we use to consume text from the source.</summary>
        private TextReader Reader;

        /// <summary>Documentation of the ESD names available to this parser.</summary>
        private ESDDocumentation Doc;

        /// <summary>
        /// Consumes and returns an ESD expression from <paramref name="reader"/>.
        /// </summary>
        public static AST.Expr ParseExpression(TextReader reader, ESDDocumentation doc) =>
            new ESDParser(reader, doc).Expression();

        /// <summary>Parses <paramref name="expression"/> as an ESD expression.</summary>
        public static AST.Expr ParseExpression(string expression, ESDDocumentation doc)
        {
            var parser = new ESDParser(new StringReader(expression), doc);
            var expr = parser.Expression();
            parser.ExpectDone();
            return expr;
        }

        /// <summary>
        /// Consumes and returns an ESD command from <paramref name="reader"/>.
        /// </summary>
        public static AST.Command ParseCommand(TextReader reader, ESDDocumentation doc) =>
            new ESDParser(reader, doc).Command();

        /// <summary>Parses <paramref name="command"/> as an ESD command.</summary>
        public static AST.Command ParseCommand(string command, ESDDocumentation doc)
        {
            var parser = new ESDParser(new StringReader(command), doc);
            var expr = parser.Command();
            parser.ExpectDone();
            return expr;
        }

        private ESDParser(TextReader reader, ESDDocumentation doc)
        {
            Reader = reader;
            Doc = doc;
        }

        /// <summary>Consumes and returns an ESD command invocation.</summary>
        private AST.Command Command()
        {
            var name = Identifier();
            if (!Doc.CommandsByName.TryGetValue(name, out var command))
            {
                throw new Exception($"Invalid ESD: No command named {name}");
            }

            return new AST.Command()
            {
                Name = name,
                Arguments = Arguments(),
            };
        }

        /// <summary>Consumes and returns an expression tree.</summary>
        private AST.Expr Expression()
        {
            var lhs = AndExpression();
            Whitespace();
            if (Reader.Peek() != '|') return lhs;

            Expect("||");
            Whitespace();
            return new AST.BinaryExpr() { Op = "||", Lhs = lhs, Rhs = AndExpression() };
        }

        /// <summary>
        /// Consumes an returns a binary and expression or an expression with higher operator
        /// precedence.
        /// </summary>
        private AST.Expr AndExpression()
        {
            var lhs = EqualityExpression();
            Whitespace();
            if (Reader.Peek() != '&') return lhs;

            Expect("&&");
            Whitespace();
            return new AST.BinaryExpr() { Op = "&&", Lhs = lhs, Rhs = EqualityExpression() };
        }

        /// <summary>
        /// Consumes an returns a binary equality expression or an expression with higher operator
        /// precedence.
        /// </summary>
        private AST.Expr EqualityExpression()
        {
            var lhs = RelationalExpression();
            Whitespace();
            switch (Reader.Peek())
            {
                case '=':
                    Expect("==");
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "==",
                        Lhs = lhs,
                        Rhs = RelationalExpression()
                    };

                case '!':
                    Expect("!=");
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "!=",
                        Lhs = lhs,
                        Rhs = RelationalExpression()
                    };

                default:
                    return lhs;
            }
        }

        /// <summary>
        /// Consumes an returns a binary relational expression or an expression with higher
        /// operator precedence.
        /// </summary>
        private AST.Expr RelationalExpression()
        {
            var lhs = AdditiveExpression();
            Whitespace();
            switch (Reader.Peek())
            {
                case '<':
                    Read();
                    var ltOp = Scan('=') ? "<=" : "<";
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = ltOp,
                        Lhs = lhs,
                        Rhs = AdditiveExpression()
                    };

                case '>':
                    Read();
                    var gtOp = Scan('=') ? ">=" : ">";
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = gtOp,
                        Lhs = lhs,
                        Rhs = AdditiveExpression()
                    };

                default:
                    return lhs;
            }
        }

        /// <summary>
        /// Consumes an returns a binary additive expression or an expression with higher
        /// operator precedence.
        /// </summary>
        private AST.Expr AdditiveExpression()
        {
            var lhs = MultiplicativeExpression();
            Whitespace();
            switch (Reader.Peek())
            {
                case '+':
                    Read();
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "+",
                        Lhs = lhs,
                        Rhs = MultiplicativeExpression()
                    };

                case '-':
                    Read();
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "-",
                        Lhs = lhs,
                        Rhs = MultiplicativeExpression()
                    };

                default:
                    return lhs;
            }
        }

        /// <summary>
        /// Consumes an returns a binary multiplicative expression or a single expression.
        /// </summary>
        private AST.Expr MultiplicativeExpression()
        {
            var lhs = SingleExpression();
            Whitespace();
            switch (Reader.Peek())
            {
                case '*':
                    Read();
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "*",
                        Lhs = lhs,
                        Rhs = SingleExpression()
                    };

                case '/':
                    Read();
                    Whitespace();
                    return new AST.BinaryExpr()
                    {
                        Op = "/",
                        Lhs = lhs,
                        Rhs = SingleExpression()
                    };

                default:
                    return lhs;
            }
        }

        /// <summary>
        /// Consumes and returns a single expression without any postfix or infix oeprations.
        /// </summary>
        private AST.Expr SingleExpression() {
            switch (Peek())
            {
                case '.' or ((>= '0') and (<= '9')): return Number();
                case '"' or '\'': return String();

                case '!':
                    Read();
                    Whitespace();
                    return new AST.UnaryExpr() { Op = "N", Arg = Expression() };

                case '(':
                    Read();
                    Whitespace();
                    var expr = Expression();
                    Whitespace();
                    Expect(')');
                    return expr;

                case ((>= 'a') and (<= 'z')) or ((>= 'A') and (<= 'Z')) or '_':
                    var identifier = Identifier();

                    switch (Peek())
                    {
                        case '.':
                            Read();
                            if (!Doc.Enums.TryGetValue(identifier, out var enumDoc))
                            {
                                throw new Exception(
                                    $"Failed to parse ESD: No enum named {identifier}"
                                );
                            }

                            var entry = Identifier();
                            if (!enumDoc.Values.TryGetValue(entry, out var value))
                            {
                                throw new Exception(
                                    $"Failed to parse ESD: Enum member {identifier}.{entry} does " +
                                        "not exist"
                                );
                            }

                            return new AST.ConstExpr() { Value = value };

                        case '(':
                            return new AST.FunctionCall() { Name = identifier, Args = Arguments() };

                        default:
                            Expected("\".\" or \"(\"");
                            return null;
                    }

                default:
                    Expected("expression");
                    return null;
            }
        }

        /// <summary>Consumes and returns an argument invocation list.</summary>
        private List<AST.Expr> Arguments()
        {
            Expect('(');
            Read();
            Whitespace();
            if (Scan(')')) return new();

            var args = new List<AST.Expr>();
            do
            {
                Whitespace();
                args.Append(Expression());
                Whitespace();
            } while (Scan(','));
            Expect(')');
            return args;
        }

        /// <summary>
        /// Consumes and returns a numeric value (byte, int, or float). Throws an exception if
        /// there's no number available at the current location.
        /// </summary>
        private AST.ConstExpr Number()
        {
            var beforePoint = Digits();

            if (Scan('.'))
            {
                var afterPoint = Digits();
                if (afterPoint == "") Expected("digits");

                return new AST.ConstExpr() { Value = float.Parse(beforePoint + '.' + afterPoint) };
            }
            else
            {
                if (beforePoint == "") Expected("number");
                var number = int.Parse(beforePoint);
                return new AST.ConstExpr() {
                    Value = Math.Abs(number) < 0x80 ? (object)(sbyte) number : (object)number
                };
            }
        }

        /// <summary>Consumes and returns a sequence of zero or more digits.</summary>
        private string Digits()
        {
            var sb = new StringBuilder();
            while (true)
            {
                switch (Reader.Peek())
                {
                    case ((>= '0') and (<= '9')):
                        sb.Append(Read());
                        break;
                    default:
                        return sb.ToString();
                }
            }
        }

        /// <summary>
        /// Consumes and returns a string value. Throws an exception if there's no string available
        /// at the current location.
        /// </summary>
        private AST.ConstExpr String()
        {
            char quote;
            switch (Peek())
            {
                case '"' or '\'':
                    quote = Read();
                    break;
                default:
                    Expected("string");
                    return null;
            }

            var sb = new StringBuilder();
            while (true)
            {
                switch(Peek())
                {
                    case '\\':
                        Read();
                        switch (Peek())
                        {
                            case ((>= 'a') and (<= 'z')) or ((>= 'A') and (<= 'Z')):
                                var escape = new string(Read(), 1);
                                throw new Exception($"Invalid ESD: Unsupported escape code \\{escape}");

                            case char c:
                                sb.Append(c);
                                break;
                        }
                        break;

                    case char c:
                        if (c == quote)
                        {
                            Read();
                            return new AST.ConstExpr { Value = sb.ToString() };
                        }
                        else
                        {
                            sb.Append(Read());
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// Consumes and returns an alphanumeric identifier. Throws an exception if there's no
        /// identifier available at the current location.
        /// </summary>
        private string Identifier()
        {
            var sb = new StringBuilder();
            switch (Reader.Peek())
            {
                case ((>= 'a') and (<= 'z')) or ((>= 'A') and (<= 'Z')) or '_':
                    sb.Append(Read());
                    break;
                default:
                    Expected("identifier");
                    break;
            }

            while (true)
            {
                switch (Reader.Peek())
                {
                    case ((>= 'a') and (<= 'z'))
                            or ((>= 'A') and (<= 'Z'))
                            or ((>= '0') and (<= '9'))
                            or '_':
                        sb.Append(Read());
                        break;
                    default: return sb.ToString();
                }
            }
        }

        /// <summary>Consumes zero or more whitespace characters.</summary>
        private void Whitespace()
        {
            while (true)
            {
                switch (Reader.Peek())
                {
                    case ' ' or '\t' or '\n' or '\r':
                        Read();
                        break;
                    default: return;
                }
            }
        }

        /// <summary>
        /// Consumes <paramref name="c"/> if possible and throws an error otherwise.
        /// </summary>
        private void Expect(char c)
        {
            if (!Scan(c)) Expected(new string(c, 1));
        }

        /// <summary>
        /// Consumes <paramref name="str"/> if possible and throws an error otherwise.
        /// </summary>
        private void Expect(string str)
        {
            foreach (var c in str)
            {
                if (!Scan(c)) Expected($"{new string(c, 1)} as part of {str}");
            }
        }

        /// <summary>
        /// If <paramref name="c"/> is the next character in <see cref="Reader"/>, consumes it.
        /// </summary>
        /// <returns>Whether <paramref name="c"/> was consumed.</returns>
        private bool Scan(char c)
        {
            if (Reader.Peek() != (int) c) return false;
            Read();
            return true;
        }

        /// <summary>
        /// Consumes and returns the next character in the stream as a <see cref="char"/>, or
        /// throws an exception if the stream is done.
        /// </summary>
        private char Read()
        {
            return Reader.Read() switch
            {
                -1 => throw new Exception("Invalid ESD: unexpected end of input"),
                var c => (char) c,
            };
        }

        /// <summary>
        /// Returns the next character in the stream as a <see cref="char"/> without consuming it,
        /// or throws an exception if the stream is done.
        /// </summary>
        private char Peek()
        {
            return Reader.Peek() switch
            {
                -1 => throw new Exception("Invalid ESD: unexpected end of input"),
                var c => (char) c,
            };
        }

        /// <summary>
        /// Throws an exception indicating that <paramref name="name"/> was expected but not found.
        /// </summary>
        private void Expected(string name)
        {
            throw new Exception($"Invalid ESD: expected {name}, was \"{Reader.ReadToEnd()}\"");
        }

        /// <summary>
        /// Throws an exception if <see cref="Reader"/> isn't at the end of its input.
        /// </summary>
        private void ExpectDone()
        {
            if (Reader.Peek() == -1) return;
            Expected("end of input");
        }
    }
}
