// 汇总对象清单中供加载器、时间轴及渲染器共同使用的声明式配置。

using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 对象演出的声明式定义。对象名只用于查找 manifest，不参与时间轴或绘制分支。
/// 动画时间以秒计，位置以 320×180 逻辑画布计；谱面 mod 以拍计，粒子速度/间隔另按逻辑 tick 定义。
/// </summary>
public sealed class GimmickDefinition
{
    /// <summary>定义文件的格式版本，默认 1。</summary>
    public int Version { get; set; } = 1;
    /// <summary>用于定位此定义的原版对象名。</summary>
    public string ObjectName { get; set; } = "";
    /// <summary>该对象组合使用的运行时组件。</summary>
    public List<GimmickBehaviorDefinition> Behaviors { get; set; } = [];
    /// <summary>从来源回调名称到信号操作的映射。</summary>
    public Dictionary<string, GimmickSignalDefinition> Signals { get; set; } = [];
    /// <summary>按函数名注册的逐帧计算程序。</summary>
    public Dictionary<string, GimmickFunctionProgram> FunctionPrograms { get; set; } = [];
    /// <summary>决定音符皮肤及其生效区间的规则。</summary>
    public List<GimmickSkinRule> SkinRules { get; set; } = [];
    /// <summary>控制合成效果创建与销毁的可选条件。</summary>
    public GimmickLifecycleDefinition? CompositeLifecycle { get; set; }
    /// <summary>根据工程上下文向定义应用的补丁列表。</summary>
    public List<GimmickContextRule> ContextRules { get; set; } = [];
    /// <summary>在房间层中插入游戏区域时采用的深度。</summary>
    public double? PlayfieldLayerDepth { get; set; }
    /// <summary>返回第一个 Type 匹配的组件，未找到时为 null。</summary>
    /// <param name="type">组件类型键。</param>
    /// <returns>首个类型匹配的组件，缺失时为 null。</returns>
    public GimmickBehaviorDefinition? Behavior(string type) => Behaviors.FirstOrDefault(component => component.Type == type);
    /// <summary>检查是否声明了指定类型的组件。</summary>
    /// <param name="type">组件类型键。</param>
    /// <returns>存在该类型的组件时为 true。</returns>
    public bool HasBehavior(string type) => Behavior(type) != null;
    /// <summary>编辑器展示的对象配置名称。</summary>
    public string DisplayName { get; set; } = "OBJECT PROFILE";
    /// <summary>可用于标识该配置的别名。</summary>
    public string[] ProfileAliases { get; set; } = [];
    /// <summary>配置建议在预览中显示的 mod 列表。</summary>
    public string[] PreviewMods { get; set; } = ["scrollspeed", "velocity", "wave", "notealp", "uialpha"];
    /// <summary>可选的房间效果预设键。</summary>
    public string? RoomPreset { get; set; }
    /// <summary>来源游戏房间标识。</summary>
    public string? SourceRoom { get; set; }
    /// <summary>对象是否覆盖完整原生演出；同时是 UseNativeColorControls 缺省时的取值。</summary>
    public bool CompleteObject { get; set; } = true;
    /// <summary>继承的基础 Draw GUI 在房间效果完成后采样整张 application surface。</summary>
    public bool ApplicationProxy { get; set; }
    /// <summary>使用尚未应用滤镜的源画面作为 application proxy 输入。</summary>
    public bool ApplicationProxyUsesUnfilteredSource { get; set; }
    /// <summary>直接调用 event_user(11) 的对象即使没有 proxy 也会执行 Other_21；继承的 Draw_64 在此情况下提前退出。</summary>
    public bool AlwaysApplicationProxy { get; set; }
    /// <summary>原版 main_shader 作用于已完成 proxy 变换的 application surface。</summary>
    public bool PostAfterApplicationProxy { get; set; }
    /// <summary>源对象覆盖基础 Draw GUI 并跳过其 proxy 绘制逻辑。</summary>
    public bool DisableProxy { get; set; }
    /// <summary>原版演出的分层场景、剧情与音频序列。</summary>
    public NativeSequenceDefinition? Sequence { get; set; }
    /// <summary>从原版空操作名称到诊断说明的映射。</summary>
    public Dictionary<string, string> SourceNoOps { get; set; } = [];
    /// <summary>记录源上下文依赖缺失的回调，实际调用这些回调会产生错误。</summary>
    public Dictionary<string, string> UnavailableCallbacks { get; set; } = [];
    /// <summary>对工程初始 BPM 的可选覆盖。</summary>
    public double? InitialBpm { get; set; }
    /// <summary>显式固定的每分钟拍数，提供时忽略谱内变速。</summary>
    public double? FixedBpm { get; set; }
    /// <summary>覆盖工程时间偏移的毫秒数。</summary>
    public double? OffsetOverrideMs { get; set; }
    /// <summary>选择场景合成方式的键。</summary>
    public string? SceneComposite { get; set; }
    /// <summary>模拟桌面及窗口合成使用的资源配置。</summary>
    public NativeDesktopDefinition? Desktop { get; set; }
    /// <summary>是否在 GUI 绘制之后执行合成。</summary>
    public bool CompositeAfterGui { get; set; }
    /// <summary>合成所采样的源画面键，默认 final。</summary>
    public string CompositeSource { get; set; } = "final";
    /// <summary>合成源画面时顺序执行的 shader pass。</summary>
    public List<GimmickShaderMode> CompositePasses { get; set; } = [];
    /// <summary>控制台演出使用的背景、命令及配色表。</summary>
    public OperationConsoleDefinition? Console { get; set; }
    /// <summary>以来源索引访问的对话消息。</summary>
    public List<NativeDialogueLine> DialogueTable { get; set; } = [];
    /// <summary>插入歌曲呈现时间轴的剧情定义。</summary>
    public List<NativeInterludeDefinition> Interludes { get; set; } = [];
    /// <summary>按拍指定的原版 BPM 分段。</summary>
    public List<GimmickBpmPoint> BpmOverride { get; set; } = [];
    /// <summary>由该对象额外注册的 mod 名称。</summary>
    public string[] ExtraMods { get; set; } = [];
    /// <summary>全局轨道尚无事件时使用的默认数值。</summary>
    public Dictionary<string, double> Defaults { get; set; } = [];
    /// <summary>将来源 mod 名称解析到对应轨道的别名表。</summary>
    public Dictionary<string, string> ModAliases { get; set; } = [];
    /// <summary>以标量表达式求值的具名轨道。</summary>
    public Dictionary<string, GimmickScalar> ValueBindings { get; set; } = [];
    /// <summary>按逐帧函数和输出轨道组织的标量表达式。</summary>
    public Dictionary<string, Dictionary<string, GimmickScalar>> PerFrameBindings { get; set; } = [];
    /// <summary>由回调触发的渐变配置。</summary>
    public Dictionary<string, GimmickCallbackFade> CallbackFades { get; set; } = [];
    /// <summary>对各回调输入值施加的数值约束。</summary>
    public Dictionary<string, GimmickCallbackConstraint> CallbackConstraints { get; set; } = [];
    // 资源包路径可指向歌曲根目录内的不同子目录；旧包名只留在数据中，不留在加载器中。
    /// <summary>从歌曲目录解析的可选资源包位置。</summary>
    public string? ResourcePack { get; set; }
    /// <summary>在资源配置中逐级查找数值参数的点分路径。</summary>
    public string ParameterObjectPath { get; set; } = "background.parameters";
    /// <summary>对资源包内指定精灵的尺寸和帧数要求。</summary>
    public Dictionary<string, GimmickSpriteRequirement> RequiredSprites { get; set; } = [];
    /// <summary>按资源键索引的精灵及其源帧。</summary>
    public Dictionary<string, GimmickSprite> Sprites { get; set; } = [];
    /// <summary>按纹理键索引的文件解析规则。</summary>
    public Dictionary<string, GimmickTextureSource> Textures { get; set; } = [];
    /// <summary>对象 shader 键到资源名的映射。</summary>
    public Dictionary<string, string> Shaders { get; set; } = [];
    /// <summary>回调占用的时间尾部（秒）；未声明时取绘制项的最长寿命。</summary>
    public Dictionary<string, double> CallbackLifetimes { get; set; } = [];
    /// <summary>按回调名分组的绘制项。</summary>
    public Dictionary<string, List<GimmickDrawing>> Callbacks { get; set; } = [];
    /// <summary>回调之间取消已有绘制实例的关系表。</summary>
    public Dictionary<string, string[]> CallbackCancels { get; set; } = [];
    /// <summary>将回调名映射为原版侧边发射器行为，可选 unravel、astellion 或 apocalypse。</summary>
    public Dictionary<string, string> SideCallbacks { get; set; } = [];
    /// <summary>仅当缓动终值 arg3 大于 0 时创建侧边实例的回调名。</summary>
    public string[] PositiveSideCallbacks { get; set; } = [];
    /// <summary>独立于回调触发的常驻绘制项。</summary>
    public List<GimmickDrawing> Overlays { get; set; } = [];
    /// <summary>使用原版文字表和字体图集的绘制项。</summary>
    public List<GimmickTextOverlay> TextOverlays { get; set; } = [];
    /// <summary>原版辅助渲染器按索引使用的文本表。</summary>
    public Dictionary<string, string[]> SourceTextTables { get; set; } = [];
    /// <summary>构成房间背景的图层定义。</summary>
    public List<GimmickRoomLayer> RoomLayers { get; set; } = [];
    /// <summary>生成遮罩图像使用的绘制项。</summary>
    public List<GimmickDrawing> MaskLayers { get; set; } = [];
    /// <summary>对象背景的绘制项和可选滤镜。</summary>
    public GimmickBackground? Background { get; set; }
    /// <summary>区域粒子发射器的可选配置。</summary>
    public GimmickParticleEmitter? ParticleEmitter { get; set; }
    /// <summary>选择后处理模式的轨道名，默认 shadermode。</summary>
    public string PostModeMod { get; set; } = "shadermode";
    /// <summary>按模式编号索引的后处理 shader。</summary>
    public Dictionary<int, GimmickShaderMode> PostModes { get; set; } = [];
    /// <summary>选定模式后依次执行的 pass；未配置模式时构成完整后处理链。</summary>
    public List<GimmickShaderMode> PostPasses { get; set; } = [];
    // 这些是可组合的渲染能力，不能把它们重新合并成某首歌的布尔开关。
    /// <summary>是否启用环境粒子的绘制能力。</summary>
    public bool AmbientParticles { get; set; }
    /// <summary>是否将爆发效果放在曲绘之后绘制。</summary>
    public bool BurstAfterJacket { get; set; }
    /// <summary>是否启用棋盘格背景。</summary>
    public bool Checkerboard { get; set; }
    /// <summary>启用原版棋盘格时的固定模式号。</summary>
    public int CheckerMode { get; set; } = 1;
    /// <summary>是否禁用曲绘的乘法混合。</summary>
    public bool DisableJacketMultiply { get; set; }
    /// <summary>是否由对象的绘制替换默认判定覆盖层。</summary>
    public bool ReplaceJudgmentOverlay { get; set; }
    /// <summary>是否清理 proxy 画面底部区域。</summary>
    public bool ClearProxyFooter { get; set; } = true;
    /// <summary>是否使用公共后处理流程，默认开启。</summary>
    public bool UseCommonPostProcessing { get; set; } = true;
    /// <summary>
    /// null 保留 r15 原生对象的颜色控制语义；显式 false 才表示禁用。
    /// 不能把缺失字段反序列化成 false，否则旧 v1 定义会静默丢失 FX_red。
    /// </summary>
    public bool? UseNativeColorControls { get; set; }
    /// <summary>是否应用 recolor 调色。</summary>
    public bool UseRecolorTint { get; set; }
    /// <summary>原版将 combo、judgement、early-late 的 drawInGui 关闭时，这些 HUD 在后处理前绘制。</summary>
    public bool HudBeforePost { get; set; }
    /// <summary>指定轨道为正时，将普通 Draw 的 HUD 延后到 GUI 阶段。</summary>
    public string? HudAfterPostWhenPositive { get; set; }
    /// <summary>是否启用原版 mode00 的 proxy 几何规则。</summary>
    public bool ProxyMode00 { get; set; }
    /// <summary>是否绘制公共白色闪光层。</summary>
    public bool UseCommonWhiteFlash { get; set; } = true;
}
