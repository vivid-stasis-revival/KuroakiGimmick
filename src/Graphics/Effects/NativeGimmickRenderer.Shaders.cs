using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    /// <summary>
    /// 全屏后处理。选择器要求是有限、整数且落在 int 范围内的值，并且能对应到已定义的 post mode；任何一步不满足都写诊断。
    /// 失败时复制输入，绝不能把上一帧残留的离屏纹理当成当前输出。
    /// </summary>
    public void RenderPost(Target output, Texture input, Session session, double time)
    {
        Prepare(session);
        if (current?.Data is not { } definition || definition.PostModes.Count == 0)
        {
            canvas.Pass(output, input, canvas.Basic);
            return;
        }
        double selected = session.Timeline.Get(definition.PostModeMod, time);
        if (!double.IsFinite(selected) || selected != Math.Truncate(selected) || selected < int.MinValue || selected > int.MaxValue
            || !definition.PostModes.TryGetValue((int) selected, out var mode))
        {
            current.Fail(definition.PostModeMod, "No post shader mode is defined for the evaluated selector.");
            canvas.Pass(output, input, canvas.Basic);
            return;
        }
        if (!ApplyShader(output, input, session, time, mode))
        {
            canvas.Pass(output, input, canvas.Basic);
        }
    }

    /// <summary>
    /// 提交 pass 之前先把全部输入验证完：uniform 必须有限且在 float 范围内，sampler 用到的图片必须全部就位，
    /// 任何一项不合格都返回 false 由调用方走复制路径，不留下画到一半的输出。背景滤镜与全屏后处理共用此入口。
    /// sampler 绑定从单元 1 开始（0 留给 gm_BaseTexture）。
    /// </summary>
    private bool ApplyShader(Target output, Texture input, Session session, double time, GimmickShaderMode mode)
    {
        if (current == null || !current.IsModeAvailable(mode) || failedModes.Contains(mode) || !shaders.TryGetValue(mode.Shader, out var shader))
        {
            return false;
        }
        try
        {
            var uniforms = new Dictionary<string, double[]>();
            foreach (var (name, scalars) in mode.Uniforms)
            {
                var values = scalars.Select(scalar => scalar.Value(session.Timeline, time)).ToArray();
                if (values.Any(value => !double.IsFinite(value) || Math.Abs(value) > float.MaxValue))
                {
                    throw new InvalidDataException("Shader uniform is not a finite float: " + name);
                }
                uniforms.Add(name, values);
            }
            var samplers = new List<(string Name, Texture Image)>();
            foreach (var (name, textureName) in mode.Samplers)
            {
                var image = Image(textureName, repeat: true, linear: mode.LinearSamplers.Contains(name, StringComparer.Ordinal));
                if (image == null)
                {
                    return false;
                }
                samplers.Add((name, image));
            }
            canvas.Flush();
            for (int index = 0; index < samplers.Count; index++)
            {
                canvas.Gpu.BindTexture(index + 1, samplers[index].Image);
            }
            canvas.Pass(output, input, shader, program =>
            {
                foreach (var (name, values) in uniforms)
                {
                    switch (values.Length)
                    {
                        case 1:
                            program.Float(name, values[0]);
                            break;
                        case 2:
                            program.Vec2(name, values[0], values[1]);
                            break;
                        case 3:
                            program.Vec3(name, values[0], values[1], values[2]);
                            break;
                        case 4:
                            program.Vec4(name, new((float) values[0], (float) values[1], (float) values[2], (float) values[3]));
                            break;
                    }
                }
                for (int index = 0; index < samplers.Count; index++)
                {
                    var sampler = samplers[index];
                    program.Int(sampler.Name, index + 1);
                    // GameMaker 生成的滤镜都依赖这个约定俗成的 <name>TexelSize 配套 uniform。
                    program.Vec2(sampler.Name + "TexelSize", 1.0 / sampler.Image.Width, 1.0 / sampler.Image.Height);
                }
            });
            return true;
        }
        catch (Exception exception) when (ResourceFiles.IsResourceError(exception))
        {
            failedModes.Add(mode);
            current.Fail("shader/" + mode.Shader, exception.Message);
            return false;
        }
    }
}

