using System.Text.Json;
using System.Text.Json.Nodes;

namespace KuroakiGimmick.Core;

/// <summary>
/// 针对 r15 → r17 的兼容断点：真实旧定义、缺失的新式共享定义、v1 资源包字段大小写。
/// 只写临时目录；不重命名安装目录，不修改曲包，也不把 CPU 检查冒充画面验证。
/// </summary>
public static class LegacyCompatibilitySelfTest
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Legacy compatibility test failed: " + message);
            }
            checks++;
            Console.WriteLine("PASS " + message);
        }
        string directory = Path.Combine(Path.GetTempPath(), "kuroaki-legacy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 模拟"新程序 + 旧 Assets 目录"：新增的内建对象定义必须能从内嵌资源回退加载，
            // Origin == "embedded" 就是在确认它走的是回退而不是碰巧读到了散装文件。
            string emptyAssets = Path.Combine(directory, "old-assets");
            Directory.CreateDirectory(emptyAssets);
            var fallback = GimmickCatalog.Resolve("obj_astellion_gimmick", assetsRoot: emptyAssets);
            Check(fallback?.Origin == "embedded", "new built-in object definition survives an old Assets directory");
            Check(GimmickCatalog.Resolve("ignored", profile: "astellion", assetsRoot: emptyAssets)?.Origin == "embedded",
                "legacy profile alias works without the new loose definition");
            var fallbackData = GimmickDefinitionReader.Read(fallback!.Json);
            // 这 24 个 ID 的顺序就是 r15 二进制的扩展 mod 表：旧谱面按下标引用它们，
            // 插入、删除或重排任何一项都会让旧谱面的事件指到别的效果上，所以整串逐个比对。
            string[] ids = "gray barrel barrel2 hdistort fish vig abx aby aberamp uialpha cover1 cover2 cover3 wflash rainbow sides notealp video noteoverlayalp astbars bgalph fxdist1 fxdist2 parttimer".Split(' ');
            Check(GimmickCatalog.ReadExtraMods("obj_astellion_gimmick", assetsRoot: emptyAssets).SequenceEqual(ids),
                "all 24 r15 extension IDs preserve exact positions");
            // 174 是该曲固定 BPM；资源包路径沿用 r15 的 "<profile>/manifest.json" 发现方式，不能改成新式布局。
            Check(fallbackData.FixedBpm == 174 && fallbackData.ResourcePack == "astellion/manifest.json",
                "embedded metadata preserves BPM and legacy resource-pack discovery");
            Check(fallbackData.Callbacks.ContainsKey("sides") && fallbackData.Callbacks.ContainsKey("astbars")
                && fallbackData.Background != null && fallbackData.ParticleEmitter != null && fallbackData.PostModes.Count > 0,
                "background, callbacks, particles and original post pipeline remain declared");
            // 没登记过的对象名必须返回 null：回退机制不能顺手给它派一个"最像的"内建渲染器。
            Check(GimmickCatalog.Resolve("obj_unregistered_fixture", assetsRoot: emptyAssets) == null,
                "unknown object is not falsely assigned a built-in renderer");

            // 这份 fixture 是真实的 r15 定义原文（随测试内嵌），不是照着新格式手写的近似品 ——
            // 只有原文才能暴露真正的兼容断点。
            using Stream legacyStream = typeof(LegacyCompatibilitySelfTest).Assembly.GetManifestResourceStream("KuroakiGimmick.Tests.LegacyScarletDefinition.json") ?? throw new InvalidDataException("Missing r15 test fixture.");
            using var legacyReader = new StreamReader(legacyStream);
            string legacyJson = legacyReader.ReadToEnd();
            var migrations = new List<string>();
            var legacyDefinition = GimmickDefinitionReader.Read(legacyJson, migrations);
            GimmickValidation.Validate(legacyDefinition);
            // 三态字段：null（字段缺失）不等于 false。旧的完整原生对象默认沿用颜色控制，
            // 只有显式写 false 才禁用 —— 混为一谈会让旧 Scarlet 资源绕开真正的 FX_red / colour-balance 链路，
            // 退化成往画面上叠一个红色矩形。
            Check(legacyDefinition.UseNativeColorControls == null, "unmodified r15 definition does not falsely declare native colors disabled");
            // r15 的命令式 perFrameFunctions 要迁移成声明式 PerFrameBindings，并且迁移动作必须被记一笔（Count==1），
            // 不能静默改写作者的定义。
            Check(legacyDefinition.PerFrameBindings.ContainsKey("aberControl") && migrations.Count == 1,
                "real r15 perFrameFunctions migrates to declarative expressions");
            string legacyPath = Path.Combine(directory, "legacy-object.json");
            File.WriteAllText(legacyPath, legacyJson);
            // 原封不动地复制安装目录下的资源子树，让 r15 定义里的相对路径仍然指得到东西。
            // manifest.json 不复制：这一组要验的是 r15 定义自身的加载路径，不是新式资源包清单。
            string installed = Path.Combine(Paths.Assets, "Gimmicks", "obj_scarletdeath_gimmick");
            foreach (string file in Directory.EnumerateFiles(installed, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "manifest.json")
                {
                    continue;
                }
                string target = Path.Combine(directory, Path.GetRelativePath(installed, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var chart = new Chart { ObjectName = "obj_scarletdeath_gimmick" };
            chart.Mods.Add(new(0, 0, "linear", 0, 1, "fx_red", -1, 0));
            chart.Mods.Add(new(0, 0, "linear", 0, 2, "aberamp", -1, 1));
            chart.PerFrame.Add(new(0, 4, "aberControl"));
            var project = new ViewerProject { Gimmick = Path.Combine(directory, "legacy.vsm"), GimmickDefinition = legacyPath, Bpm = 120 };
            var loaded = NativeGimmickProfile.Load(project, chart);
            Check(loaded.Data != null && loaded.Issues.Count == 0 && loaded.UseNativeColorControls,
                "r15 manifest loads with resources and effective native colors enabled");
            var fx = GameFxProfile.Load(project, chart, loaded);
            var red = fx.Find("FX_red") ?? throw new InvalidOperationException("Missing red room layer.");
            var timeline = new Timeline(chart, project, 2, fx, loaded);
            // 房间里的红色层初始是隐藏的，且必须挂在原版的 colour-balance shader 上，不是另起一个新滤镜。
            Check(!red.Visible && red.Filter == "_filter_colour_balance",
                "room red layer retains original hidden initial state and colour-balance shader");
            // 这里调的是渲染器真正用的那个判定函数，而不是测试自己再实现一遍规则 ——
            // 否则判定逻辑改了，测试还是绿的。
            Check(RoomFxState.IsForegroundActive(red, false, false, loaded.UseNativeColorControls, name => timeline.Get(name, .5)),
                "the actual renderer predicate enables FX_red after its chart event");
            Check(!RoomFxState.IsForegroundActive(red, false, false, loaded.UseNativeColorControls, _ => 0),
                "red stays off when its event value is zero");
            // 走 Custom 对象分支时（第 2 个参数 true），ENABLE_NON_BASE_FX 关掉就一律不亮，
            // 哪怕谱面明确写了值：配置优先于事件。
            Check(!RoomFxState.IsForegroundActive(red, true, false, loaded.UseNativeColorControls, _ => 1),
                "custom protocol still respects ENABLE_NON_BASE_FX");
            // 迁移后的逐帧绑定必须算出和 r15 原公式一样的结果：abx 用 sin×0.4，aby 用 cos×-0.8。
            // 幅度和正负号都要对得上 —— 符号反了色差就往反方向偏，肉眼不一定看得出来但和原版不同。
            Check(Math.Abs(timeline.Get("abx", .5) - Math.Sin(Math.PI / 4) * .4) < 1e-9,
                "legacy per-frame chromatic control evaluates at source beat timing");
            Check(Math.Abs(timeline.Get("aby", .5) - Math.Cos(Math.PI / 4) * -.8) < 1e-9,
                "legacy per-frame Y chromatic control preserves sign and amplitude");
            // 逐帧窗口是开区间：起点 0 和终点 2 秒（120 BPM 下的 4 拍）上都取 0，
            // 改成闭区间会在窗口两端各多出一帧跳变。
            Check(timeline.Get("abx", 0) == 0 && timeline.Get("abx", 2) == 0, "legacy per-frame function keeps open-interval boundaries");
            // 同一份 r15 JSON，只把两个字段改成显式值：显式 false 必须压过"字段缺失"的默认行为，
            // 显式声明的绑定也必须压过迁移出来的模板。
            var explicitFalse = JsonNode.Parse(legacyJson)!.AsObject();
            explicitFalse["useNativeColorControls"] = false;
            explicitFalse["perFrameBindings"] = JsonNode.Parse("""{"aberControl":{"abx":{"constant":3}}}""");
            File.WriteAllText(legacyPath, explicitFalse.ToJsonString());
            var disabled = NativeGimmickProfile.Load(project, new Chart { ObjectName = chart.ObjectName });
            Check(!disabled.UseNativeColorControls, "explicit false remains authoritative, unlike an absent legacy field");
            Check(disabled.Data!.PerFrameBindings["aberControl"]["abx"].Constant == 3, "explicit declarative binding overrides the legacy template");

            // 结构性的 JSON 字段名（Version/Room/Sprites/Background/Parameters）大小写不敏感：
            // 流通在外的 v1 资源包两种写法都有。但 shader 参数 ID（g_TestValue）属于内容，绝不能跟着规范化，
            // 改了名字 shader 就找不到这个 uniform 了。variant 0 用首字母小写，variant 1 用首字母大写，两遍断言相同。
            string song = Path.Combine(directory, "case-pack");
            string pack = Path.Combine(song, "resources");
            Directory.CreateDirectory(pack);
            string chartPath = Path.Combine(song, "test.vsc");
            File.WriteAllText(chartPath, "1000,0,0\n");
            File.Copy(Path.Combine(Paths.Assets, "GameCommon", "pt_diamonddust_0.png"), Path.Combine(pack, "shape.png"));
            var packDefinition = new GimmickDefinition { ObjectName = "obj_case_fixture", ResourcePack = "resources/manifest.json",
                RoomPreset = "none" };
            File.WriteAllText(Path.Combine(song, "gimmick-object.json"), JsonSerializer.Serialize(packDefinition, ViewerProject.Json));
            for (int variant = 0; variant < 2; variant++)
            {
                string Field(string name) => variant == 0 ? char.ToLowerInvariant(name[0]) + name[1..] : name;
                var packData = new Dictionary<string, object>
                {
                    [Field("Version")] = 1,
                    [Field("Room")] = "fixture_room",
                    [Field("Sprites")] = new Dictionary<string, GimmickSprite> { ["shape"] = new() { Width = 9, Height = 9,
                        Frames = ["shape.png"] } },
                    [Field("Background")] = new Dictionary<string, object> { [Field("Parameters")] = new Dictionary<string,
                        object> { ["g_TestValue"] = 7 } }
                };
                File.WriteAllText(Path.Combine(pack, "manifest.json"), JsonSerializer.Serialize(packData, ViewerProject.Json));
                var caseProfile = NativeGimmickProfile.Load(new ViewerProject { Chart = chartPath },
                    new Chart { ObjectName = packDefinition.ObjectName });
                Check(caseProfile.ResourcePackLoaded && caseProfile.Issues.Count == 0 && caseProfile.ResourceRoom == "fixture_room"
                    && caseProfile.Sprites.Count == 1 && caseProfile.ParameterNumber("g_TestValue") == 7,
                    "legacy resource pack field casing loads: " + variant);
            }
            // 大小写不敏感的代价：同一份 JSON 里同时出现 version 和 Version 就无法判断作者要哪一个。
            // 这种情况必须报错，随便挑一个会让同一个文件在不同版本里解析出不同结果。
            using var duplicate = JsonDocument.Parse("""{"version":1,"Version":2}""");
            bool rejected = false;
            try
            {
                JsonSchemaMembers.Get(duplicate.RootElement, "version");
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, "ambiguous duplicate schema fields fail instead of choosing an arbitrary value");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
        return checks;
    }
}
