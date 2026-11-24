using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>区域粒子发射器；所有速度仍按原版 60 Hz 逻辑 tick 定义，而不是显示器帧率。</summary>
public sealed class GimmickParticleEmitter
{
    /// <summary>用于绘制粒子的精灵键。</summary>
    public string Sprite { get; set; } = "";
    /// <summary>决定发射间隔的轨道名。</summary>
    public string IntervalMod { get; set; } = "";
    /// <summary>可选的饱和度控制轨道。</summary>
    public string? SaturationMod { get; set; }
    /// <summary>发射区域尺寸，320×180 逻辑空间；Margin 是同一空间内的越界容差，不是像素。</summary>
    public double Width { get; set; } = 320;
    /// <summary>逻辑画布中的发射区域高度。</summary>
    public double Height { get; set; } = 180;
    /// <summary>每次触发时生成的粒子数。</summary>
    public int ParticlesPerEmission { get; set; } = 2;
    /// <summary>按 60 Hz 逻辑 tick 计算的粒子速度。</summary>
    public double Speed { get; set; } = 1.5;
    /// <summary>粒子存活时长，单位为秒。</summary>
    public double Lifetime { get; set; } = 2;
    /// <summary>发射区域外允许的逻辑距离。</summary>
    public double Margin { get; set; } = 50;
    /// <summary>用于重现粒子分布的初始种子。</summary>
    public uint Seed { get; set; } = 271;
    /// <summary>关于粒子表现与来源实现的说明。</summary>
    public string? FidelityNote { get; set; }
}

