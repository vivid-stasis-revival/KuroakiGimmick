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
    public List<GimmickBehaviorDefinition> Behaviors { get; set; } = [];
    public Dictionary<string,GimmickSignalDefinition> Signals { get; set; } = [];
    public Dictionary<string,GimmickFunctionProgram> FunctionPrograms { get; set; } = [];
    public List<GimmickSkinRule> SkinRules { get; set; } = [];
    public GimmickLifecycleDefinition? CompositeLifecycle { get; set; }
    public List<GimmickContextRule> ContextRules { get; set; } = [];
    public double? PlayfieldLayerDepth { get; set; }
    public GimmickBehaviorDefinition? Behavior(string type) => Behaviors.FirstOrDefault(component=>component.Type==type);
    public bool HasBehavior(string type) => Behavior(type)!=null;
    public string DisplayName { get; set; } = "OBJECT PROFILE";
    public string[] ProfileAliases { get; set; } = [];
    public string[] PreviewMods { get; set; } = ["scrollspeed", "velocity", "wave", "notealp", "uialpha"];
    public string? RoomPreset { get; set; }
    public string? SourceRoom { get; set; }
    /// <summary>对象是否覆盖完整原生演出；同时是 UseNativeColorControls 缺省时的取值。</summary>
    public bool CompleteObject { get; set; } = true;
    /// <summary>Inherited base Draw GUI samples the complete application surface after room effects.</summary>
    public bool ApplicationProxy { get; set; }
    public bool ApplicationProxyUsesUnfilteredSource { get; set; }
    /// <summary>Direct event_user(11) calls Other_21 even with zero proxies; inherited Draw_64 exits in that case.</summary>
    public bool AlwaysApplicationProxy { get; set; }
    /// <summary>The source main_shader runs on the already transformed application proxy surface.</summary>
    public bool PostAfterApplicationProxy { get; set; }
    /// <summary>The source overrides base Draw GUI without invoking its proxy routine.</summary>
    public bool DisableProxy { get; set; }
    public NativeSequenceDefinition? Sequence { get; set; }
    public Dictionary<string, string> SourceNoOps { get; set; } = [];
    /// <summary>Resolved source-context dependency failures; unlike undefined/no-op callbacks, invoking these is an error.</summary>
    public Dictionary<string,string> UnavailableCallbacks { get; set; } = [];
    public double? InitialBpm { get; set; }
    public double? FixedBpm { get; set; }
    public double? OffsetOverrideMs { get; set; }
    public string? SceneComposite { get; set; }
    public NativeDesktopDefinition? Desktop { get; set; }
    public bool CompositeAfterGui { get; set; }
    public string CompositeSource { get; set; } = "final";
    public List<GimmickShaderMode> CompositePasses { get; set; } = [];
    public OperationConsoleDefinition? Console { get; set; }
    public List<NativeDialogueLine> DialogueTable { get; set; } = [];
    public List<NativeInterludeDefinition> Interludes { get; set; } = [];
    public List<GimmickBpmPoint> BpmOverride { get; set; } = [];
    public string[] ExtraMods { get; set; } = [];
    public Dictionary<string, double> Defaults { get; set; } = [];
    public Dictionary<string, string> ModAliases { get; set; } = [];
    public Dictionary<string,GimmickScalar> ValueBindings { get; set; } = [];
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
    public Dictionary<string, string[]> CallbackCancels { get; set; } = [];
    /// <summary>Callback names mapped to the source side-emitter behavior: unravel, astellion, or apocalypse.</summary>
    public Dictionary<string, string> SideCallbacks { get; set; } = [];
    /// <summary>Source side callbacks that test their final tween value (arg3 > 0) before creating instances.</summary>
    public string[] PositiveSideCallbacks { get; set; } = [];
    public List<GimmickDrawing> Overlays { get; set; } = [];
    public List<GimmickTextOverlay> TextOverlays { get; set; } = [];
    /// <summary>Literal source text tables consumed by native helper renderers.</summary>
    public Dictionary<string, string[]> SourceTextTables { get; set; } = [];
    public List<GimmickRoomLayer> RoomLayers { get; set; } = [];
    public List<GimmickDrawing> MaskLayers { get; set; } = [];
    public GimmickBackground? Background { get; set; }
    public GimmickParticleEmitter? ParticleEmitter { get; set; }
    public string PostModeMod { get; set; } = "shadermode";
    public Dictionary<int, GimmickShaderMode> PostModes { get; set; } = [];
    /// <summary>Ordered passes after the selected mode, or the complete post chain when no modes are declared.</summary>
    public List<GimmickShaderMode> PostPasses { get; set; } = [];
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
    /// <summary>Source objects that set combo/judgement/early-late drawInGui=false place that HUD before post-processing.</summary>
    public bool HudBeforePost { get; set; }
    /// <summary>A positive source value moves the normal-draw HUD into the GUI event instead.</summary>
    public string? HudAfterPostWhenPositive { get; set; }
    public bool ProxyMode00 { get; set; }
    public bool UseCommonWhiteFlash { get; set; } = true;
}
