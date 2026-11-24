// 描述运行时组件的角色绑定、回调路由、上下文规则和逐帧程序。

using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>可组合的运行时行为组件。对象名用于查找素材，具体行为由组件配置决定。</summary>
public sealed class GimmickBehaviorDefinition
{
    /// <summary>选择运行时组件实现的类型键。</summary>
    public string Type { get; set; } = "";
    /// <summary>组件角色到实际轨道名的映射。</summary>
    public Dictionary<string, string> Mods { get; set; } = [];
    /// <summary>组件角色到单个精灵键的映射。</summary>
    public Dictionary<string, string> Sprites { get; set; } = [];
    /// <summary>组件角色到有序精灵序列的映射。</summary>
    public Dictionary<string, string[]> SpriteLists { get; set; } = [];
    /// <summary>按名称提供给组件的 JSON 参数。</summary>
    public Dictionary<string, JsonElement> Parameters { get; set; } = [];
    /// <summary>查找角色的轨道映射，缺失时使用角色名。</summary>
    /// <param name="role">组件内部使用的角色键。</param>
    /// <returns>实际轨道名，无映射时返回角色名。</returns>
    public string Mod(string role) => Mods.TryGetValue(role, out var name) ? name : role;
    /// <summary>查找角色的精灵映射，缺失时使用角色名。</summary>
    /// <param name="role">组件内部使用的角色键。</param>
    /// <returns>实际精灵键，无映射时返回角色名。</returns>
    public string Sprite(string role) => Sprites.TryGetValue(role, out var name) ? name : role;
    /// <summary>按名称读取 JSON 数值，缺少键时使用调用方提供的值。</summary>
    /// <param name="name">JSON 数值键。</param>
    /// <param name="fallback">缺少该配置项时使用的回退值。</param>
    /// <returns>已配置的数值或回退值。</returns>
    public double Number(string name, double fallback) => Parameters.TryGetValue(name, out var value) ? value.GetDouble() : fallback;
    /// <summary>按名称读取布尔开关，缺少键时采用回退值。</summary>
    /// <param name="name">JSON 布尔键。</param>
    /// <param name="fallback">缺少该配置项时使用的回退值。</param>
    /// <returns>已配置的布尔值或回退值。</returns>
    public bool Flag(string name, bool fallback = false) => Parameters.TryGetValue(name, out var value) ? value.GetBoolean() : fallback;
    /// <summary>按名称读取字符串参数，缺少键时采用回退文本。</summary>
    /// <param name="name">JSON 文本键。</param>
    /// <param name="fallback">缺少该配置项时使用的回退值。</param>
    /// <returns>已配置的文本或回退文本。</returns>
    public string Text(string name, string fallback) => Parameters.TryGetValue(name, out var value) ? value.GetString()! : fallback;
}

/// <summary>声明式回调路由。信号操作忽略源事件的时长、缓动及 proxy 参数。</summary>
public sealed class GimmickSignalDefinition
{
    /// <summary>发出后供其他组件查询的信号名。</summary>
    public string Signal { get; set; } = "";
    /// <summary>选择信号路由处理方式的操作键。</summary>
    public string Operation { get; set; } = "signal";
    /// <summary>写入类操作使用的目标轨道。</summary>
    public string? Target { get; set; }
    /// <summary>用于延长时间轴尾部的相对秒数。</summary>
    public double Lifetime { get; set; }
    /// <summary>覆盖相对寿命的绝对结束秒数。</summary>
    public double? AbsoluteEnd { get; set; }
    /// <summary>仅在事件终值严格大于此值时路由。</summary>
    public double? MinimumExclusive { get; set; }
    /// <summary>将确定性散列映射为随机数值时的倍率。</summary>
    public double RandomScale { get; set; } = 256;
    /// <summary>来源事件序号转换为随机种子时的乘数。</summary>
    public uint SeedMultiplier { get; set; } = 17;
    /// <summary>生成种子时加在乘积上的偏移量。</summary>
    public uint SeedOffset { get; set; } = 9127;
}

/// <summary>以回调控制绘制项或合成效果的启用状态。</summary>
public sealed class GimmickLifecycleDefinition
{
    /// <summary>首次创建或销毁事件到来前的启用状态。</summary>
    public bool Initial { get; set; } = true;
    /// <summary>将生命周期切换为活动状态的回调名。</summary>
    public string[] Create { get; set; } = [];
    /// <summary>将生命周期切换为非活动状态的回调名。</summary>
    public string[] Destroy { get; set; } = [];
}

/// <summary>指定轨道、时段及阈值条件下选用的音符皮肤。</summary>
public sealed class GimmickSkinRule
{
    /// <summary>条件生效时采用的皮肤键。</summary>
    public string Skin { get; set; } = "normal";
    /// <summary>用于阈值判断的可选轨道名。</summary>
    public string? Mod { get; set; }
    /// <summary>选择皮肤时的轨道阈值。</summary>
    public double Threshold { get; set; } = .5;
    /// <summary>可选的生效起点。</summary>
    public double? Start { get; set; }
    /// <summary>可选的生效终点。</summary>
    public double? End { get; set; }
    /// <summary>规则生效时是否隐藏 timing bumper。</summary>
    public bool HideTimingBumpers { get; set; }
}

/// <summary>为已注册的逐帧计算内核配置输入和作用区间。</summary>
public sealed class GimmickFunctionProgram
{
    /// <summary>用于求值的已注册内核名。</summary>
    public string Kernel { get; set; } = "";
    /// <summary>内核使用的输入名称。</summary>
    public string[] Inputs { get; set; } = [];
    /// <summary>传给内核的数值参数表。</summary>
    public Dictionary<string, double> Parameters { get; set; } = [];
    /// <summary>定义内核写入或切换范围的窗口。</summary>
    public List<GimmickFunctionWindow> Windows { get; set; } = [];
}

/// <summary>限定逐帧函数可写入轨道的拍数区间。</summary>
public sealed class GimmickFunctionWindow
{
    /// <summary>窗口起始拍。</summary>
    public double Start { get; set; }
    /// <summary>窗口结束拍。</summary>
    public double End { get; set; }
    /// <summary>窗口内可受影响的输出轨道。</summary>
    public string[] Outputs { get; set; } = [];
    /// <summary>是否在该窗口内执行状态切换。</summary>
    public bool Toggle { get; set; }
}

/// <summary>对工程上下文中的指定 JSON 值进行匹配的条件。</summary>
public sealed class GimmickContextCondition
{
    /// <summary>要在上下文中查询的键。</summary>
    public string Key { get; set; } = "";
    /// <summary>用于比较的 JSON 值。</summary>
    public JsonElement Value { get; set; }
    /// <summary>是否反转匹配条件。</summary>
    public bool Not { get; set; }
}

/// <summary>满足全部条件时应用到对象定义的 JSON 补丁。</summary>
public sealed class GimmickContextRule
{
    /// <summary>需要同时满足的上下文条件。</summary>
    public List<GimmickContextCondition> All { get; set; } = [];
    /// <summary>条件匹配时向定义应用的 JSON 修改。</summary>
    public JsonElement Patch { get; set; }
}
