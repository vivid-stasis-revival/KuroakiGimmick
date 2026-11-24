namespace KuroakiGimmick.Core;

/// <summary>预览输入提示所用的设备与四轨按键映射。</summary>
/// <param name="Device">设备类型，可取 keyboard、xbox 或 playstation。</param>
/// <param name="KeyboardLabels">可选的四轨键帽文字，每项最多 64 个字符。</param>
/// <param name="GamepadIcons">可选的四轨手柄图标索引，范围为 0 至 14。</param>
public sealed record NativeInputContext(string Device = "keyboard", string[]? KeyboardLabels = null, int[]? GamepadIcons = null)
{
    /// <summary>来自 define_options 的默认四轨键盘绑定。</summary>
    public static readonly string[] DefaultKeys = ["S", "D", "J", "K"];
    /// <summary>检查设备名称及已提供的四轨键帽和图标映射。</summary>
    /// <exception cref="InvalidDataException">设备名称无效、映射数量不等于四，或文字、图标索引超出允许范围。</exception>
    public void Validate()
    {
        if (Device is not ("keyboard" or "xbox" or "playstation") ||
            KeyboardLabels is { } keys && (keys.Length != 4 || keys.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 64)) ||
            GamepadIcons is { } icons && (icons.Length != 4 || icons.Any(i => i is < 0 or > 14)))
            throw new InvalidDataException("Native input context requires four keyboard labels or four gamepad icon indices (0..14).");
    }
}
