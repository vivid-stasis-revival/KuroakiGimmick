namespace KuroakiGimmick.Core.Editing;

/// <summary>
/// 一次拖拽要落到哪里：Initial 出场姿态、Key 任意一拍的关键帧、Start/End 某段动画的首尾、Path 整条路径平移。
/// 目标不同，写入的事件也不同，UI 必须先定好目标再开始拖，中途切换等于换了一种写入语义。
/// </summary>
public enum ImagePoseTarget
{
    /// <summary>写入图片出场时的姿态。</summary>
    Initial,
    /// <summary>写入所选拍的关键帧。</summary>
    Key,
    /// <summary>修改动画起点。</summary>
    Start,
    /// <summary>修改动画终点。</summary>
    End,
    /// <summary>平移整条运动路径。</summary>
    Path
}

