namespace KuroakiGimmick.Core;

/// <summary>Parser and bounded interpreter regressions independent of game assets.</summary>
internal static class MpfLanguageChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidDataException("MPF language regression failed: " + name);
            checks++;
        }
        void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, name);
        }
        var received = new List<string>();
        var context = new MpfScriptLanguage.Context();
        context.Reset();
        context.Function = (_, args) => { received.Add((string)args[0]!); return 0d; };
        // These strings must remain literals even when they spell grammar tokens.
        string[] literals = ["(", ")", "[", "]", "-", "!", "true", "false", ";", "if", "=", "||"];
        foreach (string literal in literals)
        {
            var script = MpfScriptLanguage.Parse("::Main::\nwrite_log(\"" + literal + "\");");
            script.Run(context, "main");
        }
        Check(received.SequenceEqual(literals), "operator/keyword string literals");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nvar a = " + new string('(', 500) + "1" + new string(')', 500) + ";"),
            "deep parentheses fail safely");
        Reject(() => MpfScriptLanguage.Parse("::Main::\n" + new string('{', 500) + new string('}', 500)),
            "deep blocks fail safely");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nvar a = " + new string('!', 500) + "1;"),
            "deep unary expressions fail safely");
        var deepTree = MpfScriptLanguage.Parse("::Main::\nvar a = " + string.Join("+", Enumerable.Repeat("1", 1000)) + ";");
        Reject(() => deepTree.Run(new(), "main"), "left associative AST evaluation bounded");
        var allocation = new MpfScriptLanguage.Context();
        allocation.Allocate(1_000_000);
        allocation.Reset();
        Reject(() => allocation.Allocate(1), "refresh does not reset allocation budget");
        var arrays = MpfScriptLanguage.Parse("::Main::\nvar a = [1,2,3];");
        var constrained = new MpfScriptLanguage.Context();
        constrained.Allocate(999_998);
        Reject(() => arrays.Run(constrained, "main"), "array literals charge before allocation");
        var logical = MpfScriptLanguage.Parse("::Main::\nvar a = 2 || 3; var b = -2 && 9; var c = 2 && 7; var d = 0 || 8;");
        var values = new MpfScriptLanguage.Context();
        logical.Run(values, "main");
        Check(Equals(values.Variables["a"], 2d) && Equals(values.Variables["b"], -2d)
            && Equals(values.Variables["c"], 7d) && Equals(values.Variables["d"], 8d), "logical operators preserve source operands");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nvar a = 1 == \"1\";").Run(new(), "main"), "mixed equality rejects");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nvar a = [] == [];").Run(new(), "main"), "array equality rejects");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nif (false) missing();"), "unknown function in dead code rejects");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nif (false) register_mod(\"a\",0);"), "init scope checked at parse time");
        Reject(() => MpfScriptLanguage.Parse("::Main::\nif (false) add_judge_cnt(0,1);"), "risk checked at parse time");
        var metadata = MpfScriptLanguage.Parse("#risk_level=high_risk\n#refresh_on_each_run=FALSE\n::Main::\nadd_judge_cnt(0,1);");
        Check(metadata.RiskLevel == "HIGH_RISK" && !metadata.RefreshOnEachRun, "metadata casing matches source");
        Console.WriteLine("PASS MPF language: " + checks + " checks");
        return 0;
    }
}
