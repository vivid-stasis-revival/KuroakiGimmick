using System.Numerics;

namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 编辑时用的一份姿态取值，坐标与 CustomImages 同为 320×180 逻辑空间，角度是度、Alpha 是 0..1。
/// 它只是 UI 与生成事件之间的中转值，不是另一份会被保存的变换——真正的状态始终在 VSM 事件里。
/// </summary>
/// <param name="X">逻辑横坐标。</param>
/// <param name="Y">逻辑纵坐标。</param>
/// <param name="ScaleX">水平缩放倍率。</param>
/// <param name="ScaleY">垂直缩放倍率。</param>
/// <param name="Rotation">旋转角，单位为度。</param>
/// <param name="Alpha">不透明度，范围为 0 至 1。</param>
public readonly record struct ImageEditPose(double X, double Y, double ScaleX, double ScaleY, double Rotation, double Alpha)
{
    /// <summary>用属性名取值的回调装配一份姿态，调用方通常传 <see cref="ImageValueSampler"/> 在某一拍的求值。</summary>
    /// <param name="get">读取指定图片属性值的函数。</param>
    /// <returns>根据属性值组装的姿态。</returns>
    public static ImageEditPose Read(Func<string, double> get) => new(get("imgx"), get("imgy"), get("imgscalex"),
        get("imgscaley"), get("imgrot"), get("imgalp"));
    /// <summary>展开成 VSM 属性名。位置与缩放一律成对出现，写了一轴就必须写另一轴，否则会留下半边未定义的变换。</summary>
    /// <param name="channels">要处理的图片姿态通道位掩码。</param>
    /// <returns>这些通道对应的 VSM 属性名。</returns>
    public static string[] Properties(ImageChannels channels)
    {
        var result = new List<string>();
        if (channels.HasFlag(ImageChannels.Position)) result.AddRange(["imgx", "imgy"]);
        if (channels.HasFlag(ImageChannels.Scale)) result.AddRange(["imgscalex", "imgscaley"]);
        if (channels.HasFlag(ImageChannels.Rotation)) result.Add("imgrot");
        if (channels.HasFlag(ImageChannels.Alpha)) result.Add("imgalp");
        return result.ToArray();
    }
    /// <summary>属性名到通道的映射。返回 None 表示这不是本编辑器能直接操作的基本属性（例如 imgskewx、imgxtime）。</summary>
    /// <param name="property">图片属性名。</param>
    /// <returns>对应通道，不能直接编辑时为 None。</returns>
    public static ImageChannels Channel(string property) => property switch
    {
        "imgx" or "imgy" => ImageChannels.Position,
        "imgscalex" or "imgscaley" => ImageChannels.Scale,
        "imgrot" => ImageChannels.Rotation,
        "imgalp" => ImageChannels.Alpha,
        _ => ImageChannels.None
    };
    /// <summary>按 VSM 图片属性键读取当前姿态的对应分量。</summary>
    /// <param name="property">图片属性名。</param>
    /// <returns>该姿态对应的属性分量。</returns>
    /// <exception cref="ArgumentException">属性名不属于可直接编辑的基础图片属性。</exception>
    public double Get(string property) => property switch
    {
        "imgx" => X,
        "imgy" => Y,
        "imgscalex" => ScaleX,
        "imgscaley" => ScaleY,
        "imgrot" => Rotation,
        "imgalp" => Alpha,
        _ => throw new ArgumentException("Not a basic image property: " + property)
    };
    /// <summary>写进源文件前的取值校验：必须有限且在 ±1e7 内；Alpha 额外限定 0..1，因为源格式里它就是 0..1 而非 0..255。</summary>
    /// <param name="channels">要处理的图片姿态通道位掩码。</param>
    /// <exception cref="FormatException">选中分量非有限、超过允许范围或不透明度超出 0 至 1。</exception>
    public void Validate(ImageChannels channels = ImageChannels.All)
    {
        foreach (string property in Properties(channels))
            if (!double.IsFinite(Get(property)) || Math.Abs(Get(property)) > 1e7) throw new FormatException("Image values must be finite and within +/-10000000.");
        if (channels.HasFlag(ImageChannels.Alpha) && (Alpha is < 0 or > 1)) throw new FormatException("Opacity must be 0..1.");
    }
    /// <summary>
    /// 组装出画布用的变换矩阵，顺序固定为 缩放 → 旋转 → 平移，与渲染端一致；换顺序会让旋转绕错中心。
    /// 传入的 width/height 是图片的逻辑尺寸，矩阵作用在以中心为原点的单位方块上。
    /// </summary>
    /// <param name="width">图片单帧逻辑宽度。</param>
    /// <param name="height">图片单帧逻辑高度。</param>
    /// <returns>按缩放、旋转和平移组合的画布变换。</returns>
    public Matrix3x2 Matrix(double width, double height) => Matrix3x2.CreateScale((float)(width * ScaleX), (float)(height * ScaleY)) *
        Matrix3x2.CreateRotation((float)(Rotation * Math.PI / 180)) * Matrix3x2.CreateTranslation((float)X, (float)Y);
}

