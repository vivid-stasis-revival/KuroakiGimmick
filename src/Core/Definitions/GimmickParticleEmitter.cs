using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>区域粒子发射器；所有速度仍按原版 60 Hz 逻辑 tick 定义，而不是显示器帧率。</summary>
public sealed class GimmickParticleEmitter
{
    public string Sprite { get; set; } = "";
    public string IntervalMod { get; set; } = "";
    public string? SaturationMod { get; set; }
    /// <summary>发射区域尺寸，320×180 逻辑空间；Margin 是同一空间内的越界容差，不是像素。</summary>
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 180;
    public int ParticlesPerEmission { get; set; } = 2;
    public double Speed { get; set; } = 1.5;
    public double Lifetime { get; set; } = 2;
    public double Margin { get; set; } = 50;
    public uint Seed { get; set; } = 271;
    public string? FidelityNote { get; set; }
}

