using KuroakiGimmick.Core;

namespace KuroakiGimmick.UI;

/// <summary>
/// 原始 VSM / WindowMovement 标识符的编辑器侧说明目录。时间轴上的标签与源文件逐字一致，
/// 这里只负责在悬停时解释它们；本目录的任何文本都不会写回谱面，也不会替换事件的原始 identifier。
/// </summary>
internal static class EditorTrackHelp
{
    /// <summary>Short 为一行摘要，Detail 为逐条要点；两者都只供帮助卡片显示。</summary>
    internal sealed record Info(string Short, string[] Detail);

    /// <summary>
    /// 查询顺序：WindowMovement 操作 → VSM 文档条目 → 内置的 legacy 说明。永远有返回值，
    /// 查不到时退化为"保留原始 identifier、不编造语义"的通用说明，而不是留空。
    /// </summary>
    internal static Info Get(string name, bool window, int target, Session session)
    {
        if (window) return Window(name, target);
        if (EditorReferenceHelp.TryGet(name, out var documented)) return documented;
        var legacy = Legacy(name, target, session);
        return legacy;
    }

    /// <summary>文档条目未覆盖时的内置说明表。默认值取自 ModCatalog，会随工程 ScrollSpeed 变化，因此每次查询都重新求值。</summary>
    static Info Legacy(string name, int target, Session session)
    {
        double def = ModCatalog.Default(name, target, session.Project.ScrollSpeed);
        string defaultText = double.IsFinite(def) && Math.Abs(def) < 1e12 ? def.ToString("0.####") : "source-defined";

        if (CustomImages.TryMod(name, out string imageKind, out string imageId))
            return Image(imageKind, imageId, defaultText);
        if (CustomText.TryMod(name, out string textKind, out string textId))
            return Text(textKind, textId, defaultText);

        return name switch
        {
            "scrollspeed" => new("音符基础滚速 / SV 倍率。", [
                "作用：控制音符纵向滚动的基础倍率。",
                $"默认：{defaultText}；最终纵向倍率还会乘 velocity 与逐轨 scrollindN。",
                "关系：scrollspeed × velocity × scrollindN 决定主要纵向移动速度。",
                "用途：整段变速、SV 变化；负值可造成反向滚动，0 会使主要滚动停住。",
                "这是 GLOBAL mod；编辑器即使源 VSM 没有事件也会保留空轨供新增。"
            ]),
            "velocity" => new("额外的全局 SV / 速度倍率。", [
                "作用：在 scrollspeed 之外再乘一次全局速度倍率。",
                $"默认：{defaultText}；1 = 不额外改变速度。",
                "关系：scrollspeed × velocity × scrollindN。",
                "用途：更适合做临时加速、减速或瞬时 SV，而不改工程基础 ScrollSpeed。",
                "它改变音符运动速度，不改变音乐播放速度。"
            ]),
            "noterot" => new("普通 Note / Bumper 贴图旋转角度。", [
                "作用：传给普通 Note 与 Bumper 的贴图旋转，单位为度。",
                $"默认：{defaultText}°；90 / 180 / 360 都可直接作为目标值。",
                "注意：Hold 使用独立长条几何绘制，不按普通 Note 的同一路径旋转。",
                "如果想旋转整个轨道 / 判定区，应使用 proxy 的 prrz，而不是 noterot。",
                "这是 GLOBAL mod，对当前画面中的普通 Note 生效。"
            ]),
            "wave" => new("音符纵向波浪位移幅度。", [
                "作用：按 Note 到判定线的距离加入正弦纵向偏移。",
                $"默认：{defaultText}；0 = 关闭这一项波形。",
                "当前预览公式按 distance 计算，因此拖时间轴与正常播放共用同一求值。",
                "它不是音乐波形，也不是时间轴上的 AUDIO / PEAKS。",
                "与 scrollspeed / velocity 可以同时叠加。"
            ]),
            "notealp" => new("全局 Note 透明度。", [
                "作用：控制普通 Note / Hold / Bumper 的基础透明度。",
                $"默认：{defaultText}；通常 0 = 隐藏，1 = 原透明度。",
                "Custom Gimmicks 还可再乘 notealpindN 形成逐轨透明度。",
                "它不等于 uialpha；后者控制玩法 HUD。",
                "适合做 Note 淡入、淡出或短暂隐藏。"
            ]),
            "xoffset" => new("所有 Note 的额外横向位移。", [
                "作用：给 NoteMotion.X 增加全局 X 偏移。",
                $"默认：{defaultText}；单位使用游戏逻辑坐标。",
                "逐轨版本是 xoffsetindN，可与 beat 横摆同时叠加。",
                "只移动 Note 运动结果；并不等价于 proxy prx 的整轨移动。",
                "需要整个轨道、判定区一起移动时优先用 proxy。"
            ]),
            "yoffset" => new("所有 Note 的纵向距离偏移。", [
                "作用：修改 Note 到判定线的纵向距离项。",
                $"默认：{defaultText}；逐轨版本为 yoffsetindN。",
                "它会参与 scrollspeed / velocity 后的纵向位置计算。",
                "因此相同 yoffset 在不同 SV 下的画面位移可能不同。",
                "适合做 Note 位置偏移，不等价于整个 proxy 的 pry。"
            ]),
            "beat" => new("按节拍产生横向摆动。", [
                "作用：根据当前 beat 和 Note 距离产生左右摆动。",
                $"默认：{defaultText}；0 = 无这一项横摆。",
                "这是 NoteMotion 的一部分，不会移动固定 HUD。",
                "其结果还能与 xoffset / xoffsetindN 叠加。",
                "适合做随节拍左右扭动的 Note gimmick。"
            ]),
            "boost_distance" => new("Note boost 的纵向位移强度。", [
                "作用：与 boost_time 组合，改变接近判定线时的纵向运动曲线。",
                $"默认：{defaultText}；0 时全局 boost 分支关闭。",
                "Custom Gimmicks 可再叠加 boost_distanceindN。",
                "这不是简单恒定速度倍率，而是距离相关的三次曲线偏移。",
                "需要纯 SV 时优先使用 scrollspeed / velocity。"
            ]),
            "boost_time" => new("Note boost 曲线的作用距离 / 时间参数。", [
                "作用：与 boost_distance 共同决定 boost 曲线。",
                $"默认：{defaultText}；当前实现按 Note 的 distance 值参与计算。",
                "逐轨版本为 boost_timeindN。",
                "boost_distance 为 0 时这一组 boost 不产生全局位移。",
                "不要把它当作事件 Duration；它是 gimmick 自身参数。"
            ]),
            "uialpha" => new("玩法 HUD 的透明度。", [
                "作用：控制固定玩法 UI / 底部区域的可见度。",
                $"默认：{defaultText}；0 通常隐藏，1 通常显示。",
                "它不是 notealp，也不是图片 imgalp。",
                "特殊原生对象自己绘制的 UI 不一定受这一项自动控制。",
                "用于隐藏 / 淡入玩法 HUD，而不是改变 Note。"
            ]),
            "prx" => Proxy("轨道 Proxy 横向位置。", "X 平移", defaultText, "与 prxb / prxc / prxd 相加后成为最终横向位移。"),
            "pry" => Proxy("轨道 Proxy 纵向位置。", "Y 平移", defaultText, "与 pryb / pryc / pryd 相加后成为最终纵向位移。"),
            "prrz" => Proxy("轨道 Proxy 的 2D 旋转角。", "Z / 画面平面旋转", defaultText, "与 prrzb 相加并乘 rotdir；单位为度。"),
            "przm" => Proxy("轨道 Proxy 的统一缩放倍率。", "整体缩放", defaultText, "会再乘 przmb / przmc / przx / przy 等缩放项。"),
            "pra" => Proxy("轨道 Proxy 的透明度。", "Proxy alpha", defaultText, "0 时该 proxy 不绘制；1 为正常不额外衰减。"),
            _ when name.StartsWith("scrollind", StringComparison.Ordinal) => new("逐轨 Note 滚速倍率。", [
                "作用：只对对应 lane 的 Note 纵向滚动再乘一个倍率。",
                $"默认：{defaultText}；最终关系仍是 scrollspeed × velocity × scrollindN。",
                "N 是源 mod 名中的轨道索引；原始名字会在左侧保持不变。",
                "适合单轨加速 / 减速 / 反向，而不是整个 proxy 变换。",
                "具体 lane 绑定遵循目标 Custom Gimmicks 协议。"
            ]),
            _ when name.StartsWith("notealpind", StringComparison.Ordinal) => new("逐轨 Note 透明度倍率。", [
                "作用：与 notealp 相乘，只影响对应 lane。",
                $"默认：{defaultText}；1 = 不额外改变。",
                "可用于单轨淡入、淡出或隐藏。",
                "原始 N 索引按目标 Custom Gimmicks 协议解释。",
                "轨道左侧保持 source identifier，不改成人造别名。"
            ]),
            _ => Generic(name, defaultText)
        };
    }

    static Info Proxy(string shortText, string role, string defaultText, string relation) => new(shortText, [
        $"作用：{role}，目标是当前 Pn proxy，而不是单个 Note。",
        $"默认：{defaultText}。",
        relation,
        "Proxy 变换会连同该轨道副本中的判定区 / Note 一起合成。",
        "左侧的 Pn 小标只表示目标；轨道主名称仍保持原始 mod identifier。"
    ]);

    static Info Image(string kind, string id, string defaultText)
    {
        (string shortText, string detail) = kind switch
        {
            "imgx" => ("图片对象 X 坐标。", "控制图片中心的游戏逻辑 X 坐标。"),
            "imgy" => ("图片对象 Y 坐标。", "控制图片中心的游戏逻辑 Y 坐标。"),
            "imgrot" => ("图片对象旋转角。", "控制图片旋转，单位为度。"),
            "imgscalex" => ("图片对象横向缩放。", "原图宽度会乘这个倍率。"),
            "imgscaley" => ("图片对象纵向缩放。", "原图高度会乘这个倍率。"),
            "imgalp" => ("图片对象透明度。", "0 隐藏，1 为正常透明度；最终仍受该图层绘制规则影响。"),
            "imgidx" => ("图片对象动画帧索引。", "值会四舍五入并按该图片的帧数取模。"),
            "imgcolrgb" => ("图片对象 RGB 乘色。", "使用 0xRRGGBB 对图片颜色进行乘色。"),
            "imgskewx" => ("图片对象 X skew。", "写入图片变换矩阵的横向斜切项。"),
            "imgskewy" => ("图片对象 Y skew。", "写入图片变换矩阵的纵向斜切项。"),
            "imgxtime" => ("按 NoteMotion 计算图片 X 时间偏移。", "把该值作为 distance 送入 NoteMotion.X，再叠加到图片 X。"),
            "imgytime" => ("按 NoteMotion 计算图片 Y 时间偏移。", "把该值作为 distance 送入 NoteMotion.Y，再叠加到图片 Y。"),
            "imgscaleytime" => ("按 NoteMotion 计算图片纵向时间缩放。", "遵循目标 v1.12.7 的原始公式计算纵向尺度。"),
            _ => ("图片对象参数。", "该参数属于 VSP 图片实例的普通 VSM 控制轨。")
        };
        return new(shortText, [
            $"对象：{id}；原始轨道名保持 {kind}_{id}。",
            $"作用：{detail}",
            $"默认：{defaultText}。",
            "这条轨道控制已声明的 VSP 图片对象，不会创建新的图片资源。",
            "需要更换素材 / 图层时应修改资源或对象声明，而不是改这个数值轨。"
        ]);
    }

    static Info Text(string kind, string id, string defaultText)
    {
        string subject = string.IsNullOrEmpty(id) ? "legacy text" : id;
        string shortText = kind switch
        {
            "textX" => "文字对象 X 坐标。",
            "textY" => "文字对象 Y 坐标。",
            "textrot" => "文字对象旋转角。",
            "textalp" => "文字对象透明度。",
            "textscale" => "文字对象缩放倍率。",
            "textsep" => "文字字符间距倍率。",
            "textmaxwidth" => "文字最大宽度参数。",
            "textalignh" or "textalignv" => "文字对齐参数。",
            "textcolrgb" or "textcolhex" => "文字颜色参数。",
            _ => "文字对象参数。"
        };
        return new(shortText, [
            $"对象：{subject}；轨道名直接来自源 VSM。",
            $"参数：{kind}；默认值：{defaultText}。",
            "文字内容本身来自对应 text 文件；这条轨道只控制其显示属性。",
            "删除数值事件不会删除文字文件或文字 cue。",
            "未知 / 未声明目标会继续由兼容报告说明，而不是偷偷改绑其他文字。"
        ]);
    }

    /// <summary>未知 mod 的兜底说明：只陈述原始名与默认值，明确不替 UI 编造未记录的语义。</summary>
    static Info Generic(string name, string defaultText)
    {
        bool supported = ModCatalog.Supported.Contains(name);
        return new(supported ? "已识别的 VSM mod。" : "源 VSM mod；语义未内置说明。", [
            $"原始 identifier：{name}；默认值（当前目录规则）：{defaultText}。",
            supported ? "预览器已把它列为支持项；具体视觉效果仍取决于当前对象 / 资源 / 房间。" : "编辑器会保留并允许编辑结构化事件，但不会凭空编造未知语义。",
            "片段字段仍是 Beat / Duration / From / To / Ease / Proxy。",
            "左侧永远显示原始名字；这个帮助层不会改写保存到 VSM 的 identifier。",
            "若它来自特殊原生 gimmick，兼容报告可能给出比这里更具体的限制。"
        ]);
    }

    /// <summary>WindowMovement 操作说明。注意时间轴按拍显示，而源 JSON 的 t / dur / easeDur 一律是秒。</summary>
    static Info Window(string op, int target) => op switch
    {
        "NewWindowDance" => new("WindowMovement 的窗口运动事件。", [
            $"目标：WINDOW {target}；通过 preset 与运动参数驱动窗口位置 / 角度。",
            "时间轴显示 Beat，但配置文件中的 t / easeDur 仍以秒保存。",
            "Move / Sway / Wrap / Ellipse / ShakePer 等 preset 使用同一事件类型。",
            "DESKTOP 是虚拟桌面预览；LIVE 才会创建 / 移动真实 SDL 窗口。",
            "拖片段主体改开始时间，拖右端手柄改 easeDur。"
        ]),
        "WindowResize" => new("WindowMovement 的窗口缩放 / 尺寸事件。", [
            $"目标：WINDOW {target}；控制 sx / sy、pivot 与 anchor。",
            "时间轴显示 Beat；源 JSON 的 t / dur 使用秒。",
            "拖右侧手柄会修改 dur，而不是改音乐速度。",
            "窗口内容来源与窗口几何是两套独立状态。",
            "真实窗口效果只在 LIVE 模式下作用到桌面。"
        ]),
        "HideWindow" => new("显示或隐藏指定窗口。", [
            $"目标：WINDOW {target}；show 决定事件之后的可见状态。",
            "这是瞬时状态事件，没有可拉伸的持续区间。",
            "隐藏窗口不会自动删除它的内容绑定或后续事件。",
            "虚拟桌面和 LIVE 后端都使用同一份窗口状态求值。",
            "时间轴上的位置由源 JSON 秒值换算为 Beat 显示。"
        ]),
        "ReorderWindows" => new("修改窗口 Z 顺序。", [
            "作用：重排窗口的前后层叠关系。",
            "order 数组是源配置内容；并不是编辑器轨道的视觉上下顺序。",
            "移动这条时间轴轨道本身不会改变 Z order。",
            "事件为瞬时状态，不应通过拉长轨道伪造持续时间。",
            "窗口列表仍按 ExtCustomGimmick / WindowMovement 配置解释。"
        ]),
        "SetWindowContent" => new("切换窗口内容来源。", [
            $"目标：WINDOW {target}；room / source 指定窗口显示什么内容。",
            "它与窗口位置、大小和 Z 顺序独立。",
            "无法识别的来源会显示不可用，而不是偷偷复制主窗口画面。",
            "Proxy bindings 也属于内容绑定层，不属于窗口运动参数。",
            "事件发生时间仍从源 JSON 秒值映射到编辑器 Beat。"
        ]),
        _ => new("WindowMovement 原始操作。", [
            $"目标：WINDOW {target}；原始 operation：{op}。",
            "编辑器保留操作名，不把它改成 UI 自造别名。",
            "字段单位和语义以 ExtCustomGimmick / WindowMovement 配置为准。",
            "未知字段继续保存在 JSON；不会因为 UI 不认识就删除。",
            "兼容状态请结合右侧 inspector 与 report 检查。"
        ])
    };
}
