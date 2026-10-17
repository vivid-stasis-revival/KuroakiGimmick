using System.Globalization;
using System.Text;

namespace KuroakiGimmick.Core;

/// <summary>
/// Custom Gimmicks 1.14 .vsmpf author-language subset. The parser is intentionally
/// closed: no C# scripting, reflection, file access, or arbitrary user functions.
/// </summary>
internal sealed class MpfScriptLanguage
{
    abstract record Expr
    {
        internal abstract object? Eval(Context context);
    }
    sealed record Literal(object? Value) : Expr
    {
        internal override object? Eval(Context context) => Value;
    }
    sealed record Variable(string Name) : Expr
    {
        internal override object? Eval(Context context) => context.Variables.TryGetValue(Name, out var value) ? value : 0d;
    }
    sealed record ArrayLiteral(List<Expr> Values) : Expr
    {
        internal override object? Eval(Context context) => Values.Select(e => e.Eval(context)).ToList();
    }
    sealed record Index(Expr Source, Expr Position) : Expr
    {
        internal override object? Eval(Context context)
        {
            if (Source.Eval(context) is not List<object?> array) throw new InvalidDataException("IndexError: value is not an array.");
            int i = ArrayIndex(Position.Eval(context), array.Count);
            return array[i];
        }
    }
    sealed record Unary(string Op, Expr Inner) : Expr
    {
        internal override object? Eval(Context c) => Op == "!" ? (Truthy(Inner.Eval(c)) ? 0d : 1d) : -Number(Inner.Eval(c));
    }
    sealed record Binary(string Op, Expr Left, Expr Right) : Expr
    {
        internal override object? Eval(Context c)
        {
            object? a = Left.Eval(c);
            if (Op == "&&" && !Truthy(a)) return 0d;
            if (Op == "||" && Truthy(a)) return 1d;
            object? b = Right.Eval(c);
            if (Op is "==" or "!=")
            {
                bool eq = a is double x && b is double y ? x == y : Equals(a, b);
                return (Op == "==" ? eq : !eq) ? 1d : 0d;
            }
            if (Op is "&&" or "||") return Truthy(b) ? 1d : 0d;
            double l = Number(a), r = Number(b);
            return Op switch
            {
                "+" => l + r, "-" => l - r, "*" => l * r,
                "/" => r == 0 ? throw new DivideByZeroException("ZeroDivisionError") : l / r,
                @"\" => r == 0 ? throw new DivideByZeroException("ZeroDivisionError") : Math.Floor(l / r),
                "%" => r == 0 ? throw new DivideByZeroException("ZeroDivisionError") : l % r,
                "<" => l < r ? 1d : 0d, "<=" => l <= r ? 1d : 0d,
                ">" => l > r ? 1d : 0d, ">=" => l >= r ? 1d : 0d,
                _ => throw new InvalidDataException("Unknown operator: " + Op)
            };
        }
    }
    sealed record Call(string Name, List<Expr> Arguments) : Expr
    {
        internal override object? Eval(Context context) =>
            context.Function(Name, Arguments.Select(x => x.Eval(context)).ToArray());
    }

    abstract record Statement
    {
        internal abstract void Run(Context context);
    }
    sealed record Block(List<Statement> Statements) : Statement
    {
        internal override void Run(Context context)
        {
            foreach (var statement in Statements)
            {
                context.Budget();
                statement.Run(context);
            }
        }
    }
    sealed record Declare(List<(string Name, Expr Init)> Entries) : Statement
    {
        internal override void Run(Context context)
        {
            foreach (var (name, expression) in Entries) context.Variables[name] = expression.Eval(context);
        }
    }
    sealed record Assign(Expr Target, string Operation, Expr Value) : Statement
    {
        internal override void Run(Context context)
        {
            object? value = Value.Eval(context);
            object? old = null;
            if (Operation != "=") old = Target.Eval(context);
            if (Operation != "=") value = new Binary(Operation[..^1], new Literal(old), new Literal(value)).Eval(context);
            if (Target is Variable variable) context.Variables[variable.Name] = value;
            else if (Target is Index indexed)
            {
                if (indexed.Source.Eval(context) is not List<object?> array)
                    throw new InvalidDataException("IndexError: assignment target is not an array.");
                array[ArrayIndex(indexed.Position.Eval(context), array.Count)] = value;
            }
            else throw new InvalidDataException("Invalid assignment target.");
        }
    }
    sealed record ExpressionStatement(Expr Expression) : Statement
    {
        internal override void Run(Context context) => _ = Expression.Eval(context);
    }
    sealed record Conditional(Expr Test, Statement Then, Statement? Otherwise) : Statement
    {
        internal override void Run(Context context)
        {
            if (Truthy(Test.Eval(context))) Then.Run(context);
            else Otherwise?.Run(context);
        }
    }
    sealed class FlowException(bool continueLoop) : Exception
    {
        internal bool ContinueLoop { get; } = continueLoop;
    }
    sealed record LoopJump(bool Continue) : Statement
    {
        internal override void Run(Context context) => throw new FlowException(Continue);
    }
    sealed record WhileLoop(Expr Test, Statement Body) : Statement
    {
        internal override void Run(Context context)
        {
            int count = 0;
            while (Truthy(Test.Eval(context)))
            {
                if (++count > context.MaxLoop) throw new InvalidDataException("MaxLoopReached");
                context.Budget();
                try { Body.Run(context); }
                catch (FlowException flow) { if (!flow.ContinueLoop) break; }
            }
        }
    }
    sealed record ForLoop(Statement? Start, Expr? Test, Statement? Step, Statement Body) : Statement
    {
        internal override void Run(Context context)
        {
            Start?.Run(context);
            int count = 0;
            while (Test == null || Truthy(Test.Eval(context)))
            {
                if (++count > context.MaxLoop) throw new InvalidDataException("MaxLoopReached");
                context.Budget();
                try { Body.Run(context); }
                catch (FlowException flow) { if (!flow.ContinueLoop) break; }
                Step?.Run(context);
            }
        }
    }

    public sealed class Context
    {
        internal readonly Dictionary<string, object?> Variables = new(StringComparer.Ordinal);
        internal Func<string, object?[], object?> Function { get; set; } = (_, _) => 0d;
        internal int MaxLoop = 65536;
        internal int Steps;
        internal long TotalSteps;
        internal void Budget()
        {
            if (++Steps > 100_000 || ++TotalSteps > 5_000_000)
                throw new InvalidDataException("MaxLoopReached: MPF preview execution budget exceeded.");
        }
        internal void Reset()
        {
            Variables.Clear();
            Variables["beat"] = 0d; Variables["beatStart"] = 0d;
            Variables["beatEnd"] = 0d; Variables["pi"] = Math.PI;
        }
    }

    static double Number(object? value) =>
        value is double d && double.IsFinite(d) ? d :
        throw new InvalidDataException("TypeError: numeric value required.");
    static bool Truthy(object? value) => Number(value) > 0;
    static int ArrayIndex(object? value, int count)
    {
        double d = Number(value);
        if (d < 0 || d >= count || Math.Truncate(d) != d) throw new InvalidDataException("IndexError: array index outside range.");
        return (int)d;
    }

    readonly Statement? init, finish;
    readonly Statement main;
    public string RiskLevel { get; }
    public bool RefreshOnEachRun { get; }
    public int MaxLoop { get; }
    MpfScriptLanguage(Statement? init, Statement main, Statement? finish, string risk, bool refresh, int maxLoop)
    {
        this.init = init; this.main = main; this.finish = finish;
        RiskLevel = risk; RefreshOnEachRun = refresh; MaxLoop = maxLoop;
    }
    public void Run(Context context, string phase)
    {
        context.MaxLoop = MaxLoop;
        context.Steps = 0;
        (phase == "init" ? init : phase == "finish" ? finish : main)?.Run(context);
    }

    readonly record struct Token(string Value, string Kind, int Line);

    sealed class Parser
    {
        readonly List<Token> tokens;
        readonly HashSet<string> declared;
        int pos, loopDepth;
        Token Current => tokens[Math.Min(pos, tokens.Count - 1)];
        bool Is(string s) => Current.Value == s;
        bool Match(string s) { if (!Is(s)) return false; pos++; return true; }
        Token Take() { var item = Current; pos++; return item; }
        InvalidDataException Error(string reason) => new("SyntaxError at line " + Current.Line + ": " + reason);
        void Require(string s) { if (!Match(s)) throw Error("expected '" + s + "'."); }
        string Identifier()
        {
            if (Current.Kind != "id") throw Error("expected identifier.");
            return Take().Value;
        }
        internal Parser(string source, int lineOffset, HashSet<string> declared)
        {
            this.declared = declared; tokens = Tokenize(source, lineOffset);
        }
        internal Statement Parse()
        {
            var result = new List<Statement>();
            while (Current.Kind != "eof") result.Add(ParseStatement());
            return new Block(result);
        }
        Statement ParseStatement()
        {
            if (Match(";")) return new Block([]);
            if (Match("{"))
            {
                var list = new List<Statement>();
                while (!Match("}"))
                {
                    if (Current.Kind == "eof") throw Error("expected '}'.");
                    list.Add(ParseStatement());
                }
                return new Block(list);
            }
            if (Match("if"))
            {
                Require("("); var test = Expression(); Require(")");
                var yes = ParseStatement();
                var no = Match("else") ? ParseStatement() : null;
                return new Conditional(test, yes, no);
            }
            if (Match("while"))
            {
                Require("("); var test = Expression(); Require(")");
                loopDepth++;
                try { return new WhileLoop(test, ParseStatement()); }
                finally { loopDepth--; }
            }
            if (Match("for"))
            {
                Require("(");
                var before = Is(";") ? null : Simple(false);
                Require(";");
                var test = Is(";") ? null : Expression();
                Require(";");
                var after = Is(")") ? null : Simple(false);
                Require(")");
                loopDepth++;
                try { return new ForLoop(before, test, after, ParseStatement()); }
                finally { loopDepth--; }
            }
            if (Match("break"))
            {
                if (loopDepth == 0) throw Error("break outside loop.");
                Require(";"); return new LoopJump(false);
            }
            if (Match("continue"))
            {
                if (loopDepth == 0) throw Error("continue outside loop.");
                Require(";"); return new LoopJump(true);
            }
            return Simple(true);
        }
        Statement Simple(bool semi)
        {
            if (Match("var"))
            {
                var declarations = new List<(string, Expr)>();
                do
                {
                    string name = Identifier();
                    if (!declared.Add(name)) throw Error("duplicate variable '" + name + "'.");
                    declarations.Add((name, Match("=") ? Expression() : new Literal(0d)));
                }
                while (Match(","));
                if (semi) Require(";");
                return new Declare(declarations);
            }
            var left = Expression();
            if (Current.Value is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or @"\=")
            {
                if (left is not Variable and not Index) throw Error("invalid assignment target.");
                string op = Take().Value;
                var statement = new Assign(left, op, Expression());
                if (semi) Require(";");
                return statement;
            }
            if (semi) Require(";");
            return new ExpressionStatement(left);
        }
        static int Priority(string op) => op switch
        {
            "||" => 1, "&&" => 2, "==" or "!=" => 3,
            "<" or ">" or "<=" or ">=" => 4,
            "+" or "-" => 5, "*" or "/" or "%" or @"\" => 6, _ => 0
        };
        Expr Expression(int minPriority = 1)
        {
            Expr left = Prefix();
            while (Priority(Current.Value) >= minPriority)
            {
                string op = Take().Value;
                int rank = Priority(op);
                left = new Binary(op, left, Expression(rank + 1));
            }
            return left;
        }
        Expr Prefix()
        {
            if (Match("-")) return new Unary("-", Prefix());
            if (Match("!")) return new Unary("!", Prefix());
            Expr result;
            if (Match("(")) { result = Expression(); Require(")"); }
            else if (Match("["))
            {
                var list = new List<Expr>();
                if (!Is("]")) { do { list.Add(Expression()); } while (Match(",")); }
                Require("]"); result = new ArrayLiteral(list);
            }
            else if (Current.Kind == "number")
            {
                string number = Take().Value;
                result = new Literal(double.Parse(number, CultureInfo.InvariantCulture));
            }
            else if (Current.Kind == "string") result = new Literal(Take().Value);
            else if (Match("true")) result = new Literal(1d);
            else if (Match("false")) result = new Literal(0d);
            else if (Current.Kind == "id")
            {
                string name = Identifier();
                if (Match("("))
                {
                    var args = new List<Expr>();
                    if (!Is(")")) { do { args.Add(Expression()); } while (Match(",")); }
                    Require(")"); result = new Call(name, args);
                }
                else
                {
                    if (!declared.Contains(name)) throw Error("NameError: undeclared variable '" + name + "'.");
                    result = new Variable(name);
                }
            }
            else throw Error("unexpected token '" + Current.Value + "'.");
            while (Match("[")) { var index = Expression(); Require("]"); result = new Index(result, index); }
            return result;
        }
    }

    static List<Token> Tokenize(string src, int offset)
    {
        var tokens = new List<Token>();
        int pos = 0, line = offset + 1;
        while (pos < src.Length)
        {
            char c = src[pos];
            if (c == '\n') { line++; pos++; continue; }
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < src.Length && src[pos + 1] == '/')
            {
                while (pos < src.Length && src[pos] != '\n') pos++;
                continue;
            }
            if (char.IsAsciiDigit(c) || c == '.' && pos + 1 < src.Length && char.IsAsciiDigit(src[pos + 1]))
            {
                int start = pos++;
                while (pos < src.Length && (char.IsAsciiDigit(src[pos]) || src[pos] == '.')) pos++;
                tokens.Add(new(src[start..pos], "number", line)); continue;
            }
            if (c == '"')
            {
                pos++; var value = new StringBuilder(); bool ended = false;
                while (pos < src.Length && src[pos] != '\n')
                {
                    char ch = src[pos++];
                    if (ch == '"') { ended = true; break; }
                    if (ch == '\\' && pos < src.Length)
                    {
                        char escaped = src[pos++];
                        ch = escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', _ => escaped };
                    }
                    value.Append(ch);
                }
                if (!ended) throw new InvalidDataException("SyntaxError at line " + line + ": unterminated string.");
                tokens.Add(new(value.ToString(), "string", line)); continue;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = pos++;
                while (pos < src.Length && (char.IsAsciiLetterOrDigit(src[pos]) || src[pos] == '_')) pos++;
                tokens.Add(new(src[start..pos], "id", line)); continue;
            }
            string two = pos + 1 < src.Length ? src.Substring(pos, 2) : "";
            if (new[] { "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=", "%=", @"\=" }.Contains(two))
            {
                tokens.Add(new(two, "operator", line)); pos += 2; continue;
            }
            if ("+-*/%\\=<>!(){}[],;".Contains(c))
            {
                tokens.Add(new(c.ToString(), "operator", line)); pos++; continue;
            }
            throw new InvalidDataException("SyntaxError at line " + line + ": unexpected character '" + c + "'.");
        }
        tokens.Add(new("", "eof", line));
        return tokens;
    }

    public static MpfScriptLanguage Parse(string source)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = source.Split('\n');
        var section = new Dictionary<string, (StringBuilder Text, int Start)>(StringComparer.Ordinal);
        string current = "", risk = "SAFE";
        bool refresh = true;
        int maxLoop = 65536, lastRank = -1;
        string[] markers = ["::MetaData::", "::Init::", "::Main::", "::Finish::"];
        for (int i = 0; i < lines.Length; i++)
        {
            string row = lines[i].Trim();
            int rank = Array.IndexOf(markers, row);
            if (rank >= 0)
            {
                if (rank <= lastRank) throw new InvalidDataException("SyntaxError at line " + (i + 1) + ": repeated or out-of-order section.");
                lastRank = rank; current = row;
                section[current] = (new StringBuilder(), i + 1); continue;
            }
            if (row.StartsWith("::", StringComparison.Ordinal))
                throw new InvalidDataException("SyntaxError at line " + (i + 1) + ": unknown section.");
            if (current == "" || current == "::MetaData::")
            {
                if (row.Length == 0 || row.StartsWith("//", StringComparison.Ordinal)) continue;
                if (!row.StartsWith('#')) throw new InvalidDataException("SyntaxError at line " + (i + 1) + ": expected an attribute.");
                string[] pair = row[1..].Split('=', 2);
                if (pair.Length != 2) throw new InvalidDataException("SyntaxError at line " + (i + 1) + ": invalid attribute.");
                string key = pair[0].Trim(), value = pair[1].Trim();
                if (key == "RISK_LEVEL")
                {
                    if (value is not ("SAFE" or "RISK" or "HIGH_RISK" or "CRITICAL_RISK"))
                        throw new InvalidDataException("ValueError: invalid RISK_LEVEL.");
                    risk = value;
                }
                else if (key == "MAX_LOOP_TIMES")
                {
                    if (!int.TryParse(value, out maxLoop) || maxLoop < 1 || maxLoop > 65536)
                        throw new InvalidDataException("ValueError: MAX_LOOP_TIMES must be 1..65536.");
                }
                else if (key == "REFRESH_ON_EACH_RUN")
                {
                    if (value is not ("true" or "false")) throw new InvalidDataException("ValueError: invalid REFRESH_ON_EACH_RUN.");
                    refresh = value == "true";
                }
                continue;
            }
            section[current].Text.AppendLine(lines[i]);
        }
        if (!section.ContainsKey("::Main::")) throw new InvalidDataException("SyntaxError: the ::Main:: block is missing.");
        var vars = new HashSet<string>(StringComparer.Ordinal) { "beat", "beatStart", "beatEnd", "pi" };
        Statement? Compile(string key) => section.TryGetValue(key, out var block)
            ? new Parser(block.Text.ToString(), block.Start, vars).Parse() : null;
        return new(Compile("::Init::"), Compile("::Main::")!, Compile("::Finish::"), risk, refresh, maxLoop);
    }
}
