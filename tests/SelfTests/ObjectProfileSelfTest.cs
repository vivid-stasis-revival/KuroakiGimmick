using System.Text.Json;

namespace KuroakiGimmick.Core;

/// <summary>
/// 不创建窗口的对象引擎回归测试。所有资源写入临时目录，既不修改用户谱面，也不向共享 Assets 写文件。
/// 换对象名、mod 名、回调名和资源目录后重复相同断言，防止再次引入按曲名分支。
/// </summary>
public static class ObjectProfileSelfTest
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Object profile test failed: " + message);
            }
            checks++;
            Console.WriteLine("PASS " + message);
        }
        bool Rejects(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }
        string directory = Path.Combine(Path.GetTempPath(), "kuroaki-object-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 随程序分发的粒子图只是借来当 sprite 素材，不代表这个对象的身份 ——
            // 别因为看到 pt_diamonddust 就以为测试和某首曲子绑定了。
            string fixtureImage = Path.Combine(Paths.Assets, "GameCommon", "pt_diamonddust_0.png");
            File.Copy(fixtureImage, Path.Combine(directory, "shape.png"));
            string chartPath = Path.Combine(directory, "arbitrary.vsc");
            File.WriteAllText(chartPath, "10000,0,0\n");
            // 两轮循环把对象名、回调名、控制名全部换掉再跑一遍完全相同的断言：
            // 只要有任何一条实现偷偷按名字做特判，第二轮就会挂。
            for (int variant = 0; variant < 2; variant++)
            {
                string objectName = "obj_unrelated_fixture_" + variant;
                string callbackName = "pulse_" + variant;
                string control = "opacity_" + variant;
                var definition = new GimmickDefinition
                {
                    ObjectName = objectName,
                    DisplayName = "Fixture " + variant,
                    RoomPreset = "none",
                    FixedBpm = 150,
                    ExtraMods = [callbackName, control],
                    Defaults = new()
                    {
                        ["rate"] = 2,
                        ["amplitude"] = 2
                    },
                    ModAliases = new()
                    {
                        ["linked"] = control
                    },
                    Sprites = new()
                    {
                        ["shape"] = new()
                        {
                            Width = 9,
                            Height = 9,
                            OriginX = 4,
                            OriginY = 4,
                            Frames = ["shape.png"]
                        }
                    },
                    Callbacks = new()
                    {
                        [callbackName] = [new()
                        {
                            Sprite = "shape",
                            Stage = GimmickStages.BeforePlayfield,
                            Lifetime = 1,
                            EventValue = 1,
                            LatestOnly = true,
                            X = 10,
                            VelocityX = 20,
                            Tweens = [new()
                            {
                                Property = "alpha",
                                Duration = 1,
                                From = 1,
                                To = 0
                            }]
                        }]
                    },
                    CallbackConstraints = new()
                    {
                        [callbackName] = new()
                        {
                            Integer = true,
                            Minimum = 1,
                            Maximum = 2
                        }
                    },
                    CallbackFades = new()
                    {
                        [control] = new()
                        {
                            Callback = callbackName,
                            Duration = 2,
                            From = 0,
                            To = 1
                        }
                    },
                    PerFrameBindings = new()
                    {
                        ["oscillator"] = new()
                        {
                            ["motion"] = new()
                            {
                                BeatScale = Math.PI,
                                Wave = "sin",
                                MultiplyMod = "amplitude"
                            }
                        }
                    },
                    ParticleEmitter = new()
                    {
                        Sprite = "shape",
                        IntervalMod = "rate",
                        Lifetime = 2
                    }
                };
                string manifest = Path.Combine(directory, "gimmick-object.json");
                File.WriteAllText(manifest, AppJson.Serialize(definition, ViewerProject.Json));
                var chart = new Chart
                {
                    ObjectName = objectName
                };
                // 拍 2.5 上这条值为 1，在约束范围内。
                chart.Mods.Add(new(2.5, 0, "linear", 0, 1, callbackName, -1, 0));
                // 固定 BPM 150 下 1 拍 = 0.4 秒，上面这条正好落在 t=1。
                chart.Mods.Add(new(3, 0, "linear", 0, 1, callbackName, -1, 1));
                chart.Mods.Add(new(4, 0, "linear", 0, 3, callbackName, -1, 2));
                // 上面这条值为 3，超出 CallbackConstraints 声明的 1..2，必须被挡在回调之外。
                chart.Mods.Add(new(8, 0, "linear", 0, .25, control, -1, 3));
                chart.PerFrame.Add(new(0, 100, "oscillator"));
                var project = new ViewerProject
                {
                    Chart = chartPath,
                    Bpm = 60,
                    RoomPreset = "none"
                };
                var profile = NativeGimmickProfile.Load(project, chart);
                Check(profile.Data?.ObjectName == objectName && profile.Issues.Count == 0, "arbitrary object definition loads: " + variant);
                Check(profile.Sprites["shape"].Frames.Single() == Path.Combine(directory, "shape.png"),
                    "declared sprite resolves inside definition directory");
                Check(GimmickCatalog.ReadExtraMods(objectName, directory).SequenceEqual(definition.ExtraMods), "binary IDs use declaration order");
                // 扩展 mod 的二进制 ID 从 129 开始按声明顺序递增，这是原版的编号约定：
                // ExtraMods 数组的下标即 ID-129，因此往中间插一项会让旧谱面整体错位。
                Check(ModCatalog.Decode(129, objectName, definition.ExtraMods) == callbackName, "first extension ID is 129");
                Check(ModCatalog.Decode(130, objectName, definition.ExtraMods) == control, "second extension ID is 130");
                var timeline = new Timeline(chart, project, 5, native: profile);
                // FixedBpm 由对象定义提供（150），压过 project 里写的 60：拍 2.5 → 1 秒。
                Check(timeline.Bpm.IsFixed && timeline.Bpm.Time(2.5) == 1, "fixed BPM comes from the object definition");
                // 三条回调事件只有两条合法，越界那条要变成一条 Error 诊断而不是静默丢弃。
                Check(timeline.Callbacks.Count == 2 && chart.Diagnostics.Any(item => item.Error && item.Source.EndsWith(callbackName)),
                    "invalid callback values are rejected before scheduling");
                // CallbackFades 从该回调"首次"触发（t=1）起算，Duration=2 是秒而不是拍：t=.5 还没触发取 From=0，t=2 走到一半为 .5。
                Check(timeline.Get(control, .5) == 0 && Math.Abs(timeline.Get(control, 2) - .5) < 1e-9, "callback fade uses its configured duration");
                // 别名和本名指向同一条求值结果，不能各算各的。
                Check(timeline.Get("linked", 2) == timeline.Get(control, 2), "aliases share the same evaluated control");
                // t=3 时淡入已满（1）；t=3.5 之后拍 8（=3.2 秒）那条显式 .25 接管 —— 淡入不能一直压着后续写入。
                Check(timeline.Get(control, 3) == 1 && timeline.Get(control, 3.5) == .25, "fade completion respects subsequent explicit writes");
                // 逐帧绑定按拍求值：sin(π×beat) 再乘 amplitude 默认值 2。t=.2 秒 = 0.5 拍 → sin(π/2)×2 = 2。
                Check(Math.Abs(timeline.Get("motion", .2) - 2) < 1e-9, "arbitrary per-frame output evaluates a beat-domain expression");
                // 窗口起点是开区间：正好在 t=0 上取 0，否则每个逐帧窗口的第一帧都会跳变一下。
                Check(timeline.Get("motion", 0) == 0, "per-frame windows retain strict start boundaries");
                var schedule = new GimmickDrawScheduler(profile.Data!);
                // LatestOnly 只压制同一个 drawing 的旧实例：t=1.3 时拍 2.5 和拍 3 两条都还活着（Lifetime=1），
                // 但只保留较晚的那条（EventIndex 1）。
                var commands = schedule.At(timeline, 1.3, GimmickStages.BeforePlayfield, true);
                Check(commands.Count == 1 && commands[0].EventIndex == 1, "latest-only affects only the matching drawing");
                // 位移按"回调自身年龄"算，不是按绝对时间：Age = 1.3-1.2 = .1 秒，x = 10 + 20×.1 = 12。
                Check(Math.Abs(commands[0].Drawing.Value("x", commands[0].Age) - 12) < 1e-9, "linear sprite motion uses callback age");
                Check(schedule.At(timeline, 2.3, GimmickStages.BeforePlayfield, true).Count == 0, "expired drawings are removed");
                Check(schedule.At(timeline, 1.3, GimmickStages.Gui, true).Count == 0, "render stages do not leak into each other");
                Check(timeline.ObjectParticles.Count > 0 && timeline.ObjectParticles.All(particle => particle.Frame == 0),
                    "particle emitter uses the declared frame count");
                // 用同样的输入重建一次时间轴，粒子必须逐个相同：发射器不许依赖全局随机状态或构造顺序。
                var rebuilt = new Timeline(chart, project, 5, native: profile);
                Check(rebuilt.ObjectParticles.SequenceEqual(timeline.ObjectParticles), "particle rebuilding is deterministic");
                // 先跳到 4 秒（此时都已过期），再跳回 1.3 秒，结果要和第一次完全一致。
                Check(schedule.At(timeline, 4, GimmickStages.BeforePlayfield, true).Count == 0 && schedule.At(timeline, 1.3,
                    GimmickStages.BeforePlayfield, true).SequenceEqual(commands), "backward seeking reproduces callback commands");
                // 以下几条都在 Validate 阶段拦：递归求值、自引用、非有限 BPM、未知阶段名。
                // 必须在构造 Session 或分配 GPU 资源之前就失败，否则等到渲染时才炸，资源已经建了一半。
                definition.ModAliases["loopA"] = "loopB";
                definition.ModAliases["loopB"] = "loopA";
                Check(Rejects(() => GimmickValidation.Validate(definition)), "cyclic aliases are rejected");
                definition.ModAliases.Remove("loopA");
                definition.ModAliases.Remove("loopB");
                definition.PerFrameBindings["oscillator"]["motion"].MultiplyMod = "motion";
                Check(Rejects(() => GimmickValidation.Validate(definition)), "self-referential per-frame output is rejected");
                definition.PerFrameBindings["oscillator"]["motion"].MultiplyMod = "amplitude";
                definition.FixedBpm = double.NaN;
                Check(Rejects(() => GimmickValidation.Validate(definition)), "non-finite configured BPM is rejected");
                definition.FixedBpm = 150;
                definition.Callbacks[callbackName][0].Stage = "not-a-render-stage";
                Check(Rejects(() => GimmickValidation.Validate(definition)), "unknown composition stages are rejected");
                definition.Callbacks[callbackName][0].Stage = GimmickStages.BeforePlayfield;
                // 资源路径出问题时按组件粒度报告：Data 仍然加载成功，只有 "shape" 这一项进 Issues，
                // 其余合法资源照常可用，不能因为一张图坏了整个对象都不预览。
                definition.Sprites["shape"].Frames = ["../outside.png"];
                File.WriteAllText(manifest, AppJson.Serialize(definition, ViewerProject.Json));
                var invalidResource = NativeGimmickProfile.Load(project, new Chart
                {
                    ObjectName = objectName
                });
                Check(invalidResource.Data != null && invalidResource.Issues.ContainsKey("shape"),
                    "invalid resource paths fail at component granularity");
                // 路径包含检查要同时挡住 ../ 上溯和别的平台的绝对路径写法（在 macOS/Linux 上 "C:\" 只是个普通相对名）。
                Check(Rejects(() => ResourceFiles.ContainedFile(directory, "../outside.png")), "resource parent traversal is rejected");
                Check(Rejects(() => ResourceFiles.ContainedFile(directory, "C:\\outside.png")), "foreign-platform absolute paths are rejected");
                File.Delete(manifest);
            }
            // 允许不同的二进制 ID 解码到同一个旧参数名：原版里确实有这种重复登记，两个位置都得保留。
            var repeatedNames = new GimmickDefinition
            {
                ObjectName = "obj_repeated_ids_fixture",
                ExtraMods = ["shared_parameter", "shared_parameter"]
            };
            GimmickValidation.Validate(repeatedNames);
            Check(ModCatalog.Decode(129, repeatedNames.ObjectName, repeatedNames.ExtraMods) == "shared_parameter" && ModCatalog.Decode(130,
                repeatedNames.ObjectName, repeatedNames.ExtraMods) == "shared_parameter", "repeated extension names retain both positional IDs");
            // 谱面在第 2 个音符处内嵌了 180 BPM 的变速（字典键 1 是 BPM 字段）。
            // 普通 BpmMap 要产生两段；带 fixedBpm=150 的 BpmMap 必须只有一段并明确忽略内嵌变速 ——
            // 这是对象定义里 FixedBpm 的语义：原版这类对象不跟随谱面变速。
            var tempoChart = new Chart();
            tempoChart.Notes.Add(new(2, 3, 0, 2, new Dictionary<int, object>
            {
                [1] = 180.0
            }));
            var variableTempo = new BpmMap(tempoChart, 120, 50);
            var fixedTempo = new BpmMap(tempoChart, 120, 50, 150);
            Check(variableTempo.Segments.Count == 2 && !variableTempo.IsFixed, "ordinary BPM maps preserve embedded tempo changes");
            Check(fixedTempo.Segments.Count == 1 && fixedTempo.IsFixed, "fixed-tempo mode explicitly ignores embedded changes");
            // 取样点覆盖负拍、原点、变速点前后（3、4）和远处，Beat(Time(x)) 必须回到 x：变速段的 offset 50 ms 不能算丢。
            foreach (double beat in new[]
            {
                - 1.0,
                0,
                3,
                4,
                10
            })
            {
                Check(Math.Abs(variableTempo.Beat(variableTempo.Time(beat)) - beat) < 1e-9, "tempo conversion round trip at beat " + beat);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
        return checks;
    }
}

