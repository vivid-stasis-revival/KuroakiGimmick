using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 对象演出的声明式定义。对象名只用于查找 manifest，不参与时间轴或绘制分支。
/// 动画时间以秒计，位置以 320×180 逻辑画布计；谱面 mod 以拍计，粒子速度/间隔另按逻辑 tick 定义。
/// </summary>
public sealed class GimmickDefinition
{
    public int Version { get; set; } = 1;
    public string ObjectName { get; set; } = "";
    public string DisplayName { get; set; } = "OBJECT PROFILE";
    public string[] ProfileAliases { get; set; } = [];
    public string[] PreviewMods { get; set; } = ["scrollspeed", "velocity", "wave", "notealp", "uialpha"];
    public string? RoomPreset { get; set; }
    /// <summary>对象是否覆盖完整原生演出；同时是 UseNativeColorControls 缺省时的取值。</summary>
    public bool CompleteObject { get; set; } = true;
    public NativeSequenceDefinition? Sequence { get; set; }
    public Dictionary<string, string> SourceNoOps { get; set; } = [];
    public double? InitialBpm { get; set; }
    public double? FixedBpm { get; set; }
    public string[] ExtraMods { get; set; } = [];
    public Dictionary<string, double> Defaults { get; set; } = [];
    public Dictionary<string, string> ModAliases { get; set; } = [];
    public Dictionary<string, Dictionary<string, GimmickScalar>> PerFrameBindings { get; set; } = [];
    public Dictionary<string, GimmickCallbackFade> CallbackFades { get; set; } = [];
    public Dictionary<string, GimmickCallbackConstraint> CallbackConstraints { get; set; } = [];
    // 资源包路径可指向歌曲根目录内的不同子目录；旧包名只留在数据中，不留在加载器中。
    public string? ResourcePack { get; set; }
    public string ParameterObjectPath { get; set; } = "background.parameters";
    public Dictionary<string, GimmickSpriteRequirement> RequiredSprites { get; set; } = [];
    public Dictionary<string, GimmickSprite> Sprites { get; set; } = [];
    public Dictionary<string, GimmickTextureSource> Textures { get; set; } = [];
    public Dictionary<string, string> Shaders { get; set; } = [];
    /// <summary>回调占用的时间尾部（秒）；未声明时取绘制项的最长寿命。</summary>
    public Dictionary<string, double> CallbackLifetimes { get; set; } = [];
    public Dictionary<string, List<GimmickDrawing>> Callbacks { get; set; } = [];
    public List<GimmickDrawing> Overlays { get; set; } = [];
    public GimmickBackground? Background { get; set; }
    public GimmickParticleEmitter? ParticleEmitter { get; set; }
    public string PostModeMod { get; set; } = "shadermode";
    public Dictionary<int, GimmickShaderMode> PostModes { get; set; } = [];
    // 这些是可组合的渲染能力，不能把它们重新合并成某首歌的布尔开关。
    public bool AmbientParticles { get; set; }
    public bool BurstAfterJacket { get; set; }
    public bool Checkerboard { get; set; }
    public int CheckerMode { get; set; } = 1;
    public bool DisableJacketMultiply { get; set; }
    public bool ReplaceJudgmentOverlay { get; set; }
    public bool ClearProxyFooter { get; set; } = true;
    public bool UseCommonPostProcessing { get; set; } = true;
    /// <summary>
    /// null 保留 r15 原生对象的颜色控制语义；显式 false 才表示禁用。
    /// 不能把缺失字段反序列化成 false，否则旧 v1 定义会静默丢失 FX_red。
    /// </summary>
    public bool? UseNativeColorControls { get; set; }
    public bool UseRecolorTint { get; set; }
}
