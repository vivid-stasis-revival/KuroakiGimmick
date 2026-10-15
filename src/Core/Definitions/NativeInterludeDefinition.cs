namespace KuroakiGimmick.Core;

/// <summary>A source confirm-to-advance sequence replayed with the user's authorized automatic reading timing.</summary>
public sealed class NativeInterludeDefinition
{
    public string Trigger { get; set; } = "";
    public double? StartSongTime { get; set; }
    public string? BackgroundSprite { get; set; }
    public double CharactersPerSecond { get; set; } = 24;
    public double MinimumCueSeconds { get; set; } = 2;
    public double PaddingSeconds { get; set; } = 1;
    public List<NativeInterludeStep> Steps { get; set; } = [];
}

public sealed class NativeInterludeStep
{
    public string Speaker { get; set; } = "";
    public string? Text { get; set; }
    public double? Duration { get; set; }
    public List<NativeInterludeAction> Actions { get; set; } = [];
}

public sealed class NativeInterludeAction
{
    public string Kind { get; set; } = "";
    public string? Id { get; set; }
    public string? Sprite { get; set; }
    public string? Path { get; set; }
    public int Frame { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double Scale { get; set; } = 1;
    public bool Mirror { get; set; }
    public double? From { get; set; }
    public double To { get; set; } = 1;
    public double Duration { get; set; }
    public uint ColorBgr { get; set; } = 0xffffff;
    public double Gain { get; set; } = 1;
    public bool Loop { get; set; }
    public bool Bounce { get; set; } = true;
}
