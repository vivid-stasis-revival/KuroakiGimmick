namespace KuroakiGimmick.Core;

/// <summary>Source input-display context. Defaults follow define_options' S/D/J/K keyboard bindings.</summary>
public sealed record NativeInputContext(string Device = "keyboard", string[]? KeyboardLabels = null, int[]? GamepadIcons = null)
{
    public static readonly string[] DefaultKeys = ["S", "D", "J", "K"];
    public void Validate()
    {
        if (Device is not ("keyboard" or "xbox" or "playstation") ||
            KeyboardLabels is { } keys && (keys.Length != 4 || keys.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 64)) ||
            GamepadIcons is { } icons && (icons.Length != 4 || icons.Any(i => i is < 0 or > 14)))
            throw new InvalidDataException("Native input context requires four keyboard labels or four gamepad icon indices (0..14).");
    }
}
