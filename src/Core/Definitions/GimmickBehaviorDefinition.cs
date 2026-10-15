using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>A reusable runtime component. Object names are asset lookup identities, not executable behavior selectors.</summary>
public sealed class GimmickBehaviorDefinition
{
    public string Type { get; set; } = "";
    public Dictionary<string,string> Mods { get; set; } = [];
    public Dictionary<string,string> Sprites { get; set; } = [];
    public Dictionary<string,string[]> SpriteLists { get; set; } = [];
    public Dictionary<string,JsonElement> Parameters { get; set; } = [];
    public string Mod(string role) => Mods.TryGetValue(role,out var name) ? name : role;
    public string Sprite(string role) => Sprites.TryGetValue(role,out var name) ? name : role;
    public double Number(string name,double fallback) => Parameters.TryGetValue(name,out var value) ? value.GetDouble() : fallback;
    public bool Flag(string name,bool fallback=false) => Parameters.TryGetValue(name,out var value) ? value.GetBoolean() : fallback;
    public string Text(string name,string fallback) => Parameters.TryGetValue(name,out var value) ? value.GetString()! : fallback;
}

/// <summary>Callback routing is declarative; source duration/easing/proxy arguments are ignored for signal operations.</summary>
public sealed class GimmickSignalDefinition
{
    public string Signal { get; set; } = "";
    public string Operation { get; set; } = "signal";
    public string? Target { get; set; }
    public double Lifetime { get; set; }
    public double? AbsoluteEnd { get; set; }
    public double? MinimumExclusive { get; set; }
    public double RandomScale { get; set; } = 256;
    public uint SeedMultiplier { get; set; } = 17;
    public uint SeedOffset { get; set; } = 9127;
}

public sealed class GimmickLifecycleDefinition
{
    public bool Initial { get; set; } = true;
    public string[] Create { get; set; } = [];
    public string[] Destroy { get; set; } = [];
}

public sealed class GimmickSkinRule
{
    public string Skin { get; set; } = "normal";
    public string? Mod { get; set; }
    public double Threshold { get; set; } = .5;
    public double? Start { get; set; }
    public double? End { get; set; }
    public bool HideTimingBumpers { get; set; }
}

public sealed class GimmickFunctionProgram
{
    public string Kernel { get; set; } = "";
    public string[] Inputs { get; set; } = [];
    public Dictionary<string,double> Parameters { get; set; } = [];
    public List<GimmickFunctionWindow> Windows { get; set; } = [];
}

public sealed class GimmickFunctionWindow
{
    public double Start { get; set; }
    public double End { get; set; }
    public string[] Outputs { get; set; } = [];
    public bool Toggle { get; set; }
}

public sealed class GimmickContextCondition
{
    public string Key { get; set; } = "";
    public JsonElement Value { get; set; }
    public bool Not { get; set; }
}

public sealed class GimmickContextRule
{
    public List<GimmickContextCondition> All { get; set; } = [];
    public JsonElement Patch { get; set; }
}
