using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Usus;
using Usus.Knowledge;
using HistoryVulcan.Core.Commands;
using System.Diagnostics;
using System.Reflection;

Console.OutputEncoding = Encoding.UTF8;

// 全部离线。最后一组把打好的包交给已发布宿主的命令行 `--probe` 装一遍（宿主 5.9.0 起的统一契约）——
// 版本漂移、manifest 写错、身份类缺失在宿主那边都是「静默跳过整个模块」，只有这样才能在出包前拦住。
// 测试只看宿主的装载结果与指令结果，不引用宿主的实现程序集，宿主内部怎么改都不影响这里。
var tests = new (string Name, Func<Task> Run)[]
{
    ("command registration", Sync(TestCommandRegistration)),
    ("entry file format", Sync(TestEntryFormat)),
    ("tokenizer keeps model numbers and splits han", Sync(TestTokenizer)),
    ("library search, show, check", TestLibraryCommands),
    ("z address reads through diana.z", TestZSource),
    ("page owner follows domain", Sync(TestPageOwner)),
    ("buttons, actions and commands line up", Sync(TestPageWiring)),
    ("versions agree", Sync(TestVersionsAgree)),
    ("host loads the package", TestHostLoadsPackage),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Func<Task> Sync(Action test) => () =>
{
    test();
    return Task.CompletedTask;
};

static Registrar Registry()
{
    var registry = new Registrar();
    UsusModule.Register(registry, new UsusState(null, null, Path.Combine(Path.GetTempPath(), "Usus.Tests.empty")));
    return registry;
}

static void TestCommandRegistration()
{
    var registry = Registry();
    var domain = UsusIdentity.Domain;
    string[] expected =
    [
        domain + ".kb.search",
        domain + ".kb.show",
        domain + ".kb.list",
        domain + ".kb.check",
        domain + ".kb.refresh",
        domain + ".kb.root",
        domain + ".ui.describe",
        domain + ".ui.actions",
        domain + ".ui.data",
    ];
    foreach (var name in expected)
    {
        True(registry.TryGet(name, out var descriptor), $"未注册 {name}");
        Equal(domain, descriptor!.Domain!);
    }

    Equal(expected.Length, registry.All.Count(d => d.Name.StartsWith(domain + ".", StringComparison.Ordinal)));

    foreach (var method in new[] { "describe", "actions", "data" })
    {
        True(registry.TryGet(domain + ".ui." + method, out var ui), $"缺少 {domain}.ui.{method}");
        True(ui!.Readonly, $"{domain}.ui.{method} 必须只读");
        True(ui.HiddenReason is not null, $"{domain}.ui.{method} 是界面内部协议，必须 HiddenReason");
    }
}

static void TestEntryFormat()
{
    var entry = Library.Parse(EntryText("U0007", "振动场合别靠弹簧垫圈防松", "[螺栓, 防松， GB/T 93]", "结论第一句。\n\n后面的解释。"),
        "连接/U0007-防松.md", out var reason);
    True(entry is not null, reason ?? "");
    Equal("U0007", entry!.Id);
    Equal(3, entry.Tags.Count);
    Equal("GB/T 93", entry.Tags[2]);
    Equal("结论第一句。", entry.Snippet());

    Library.Parse("没有文件头", "U0001-x.md", out reason);
    True(reason!.Contains("文件头", StringComparison.Ordinal), reason);
    Library.Parse(EntryText("U0001", "t", "[a]", "b"), "U0002-x.md", out reason);
    True(reason!.Contains("文件名", StringComparison.Ordinal), reason);
    Library.Parse(EntryText("A1", "t", "[a]", "b"), "A1-x.md", out reason);
    True(reason!.Contains("编号", StringComparison.Ordinal), reason);
    Library.Parse("---\nid: U0001\ntitle: t\ndate: 2026-11-03\n---\nb", "U0001-x.md", out reason);
    True(reason!.Contains("tags", StringComparison.Ordinal), reason);
}

static void TestTokenizer()
{
    var tokens = KeywordIndex.Tokenize("HGH20CA导轨，GB/T 5783 螺栓").ToList();
    True(tokens.Contains("hgh20ca"), string.Join(",", tokens));
    True(tokens.Contains("5783"), string.Join(",", tokens));
    True(tokens.Contains("导轨"), string.Join(",", tokens));
    True(tokens.Contains("螺栓"), string.Join(",", tokens));
    Equal(1, KeywordIndex.Tokenize("轴").Count());
}

static async Task TestLibraryCommands()
{
    var root = Path.Combine(Path.GetTempPath(), UsusIdentity.Name + ".Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "连接"));
    Directory.CreateDirectory(Path.Combine(root, "传动"));
    try
    {
        File.WriteAllText(Path.Combine(root, "README.md"), "不是条目");
        File.WriteAllText(Path.Combine(root, "连接", "U0001-防松.md"),
            EntryText("U0001", "振动场合别靠弹簧垫圈防松", "[螺栓, 防松, 振动]", "有横向振动时弹簧垫圈基本不防松。"));
        File.WriteAllText(Path.Combine(root, "传动", "U0003-导轨.md"),
            EntryText("U0003", "HGH20CA 导轨安装面要磨", "[导轨, HGH20CA]", "导轨安装面平面度决定运行精度。"));
        File.WriteAllText(Path.Combine(root, "传动", "U0004-坏.md"), "没有文件头");

        var state = new UsusState(null, null, root);
        var registry = new Registrar();
        UsusModule.Register(registry, state);

        var search = await Run(registry, "kb.search", ("q", "螺栓振动松了怎么办"));
        True(search.Success, search.Message);
        True(search.Message.Contains("U0001", StringComparison.Ordinal) && !search.Message.Contains("U0003", StringComparison.Ordinal), search.Message);

        var model = await Run(registry, "kb.search", ("q", "hgh20ca"));
        True(model.Message.Contains("U0003", StringComparison.Ordinal), model.Message);

        var none = await Run(registry, "kb.search", ("q", "齿轮"));
        True(none.Success && none.Message.Contains("没有找到", StringComparison.Ordinal), none.Message);

        var show = await Run(registry, "kb.show", ("id", "u0003"));
        True(show.Success && show.Message.Contains("平面度", StringComparison.Ordinal), show.Message);
        True(!(await Run(registry, "kb.show", ("id", "U0009"))).Success, "不存在的编号应失败");

        var list = await Run(registry, "kb.list", ("tag", "导轨"));
        True(list.Message.StartsWith("共 1 条", StringComparison.Ordinal), list.Message);

        var check = await Run(registry, "kb.check");
        True(check.Message.Contains("2 条", StringComparison.Ordinal) && check.Message.Contains("格式问题 1 个", StringComparison.Ordinal)
            && check.Message.Contains("U0005", StringComparison.Ordinal), check.Message);

        // 改文件后不用手动刷新：下一次检索按指纹自动重读。
        File.WriteAllText(Path.Combine(root, "传动", "U0004-坏.md"),
            EntryText("U0004", "同步带张紧轮放松边", "[同步带, 张紧]", "张紧轮装在松边。"));
        var after = await Run(registry, "kb.search", ("q", "同步带张紧"));
        True(after.Message.Contains("U0004", StringComparison.Ordinal), after.Message);
        Equal(3, state.Rows().Count);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

// 0.3.0：缺省位置是 z 地址，经 diana.z.tree / diana.z.read 读，不碰本机路径；没变的文件不重读。
static async Task TestZSource()
{
    var files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["连接/U0001-防松.md"] = EntryText("U0001", "振动场合别靠弹簧垫圈防松", "[螺栓, 防松]", "弹簧垫圈基本不防松。"),
        ["README.md"] = "不是条目",
    };
    var bus = new FakeZBus("2026-045-Usus/z-经验库", files);
    True(UsusState.DefaultRoot == "2026-045/z-经验库", "缺省位置必须是 z 地址");
    True(!UsusState.IsLocalPath(UsusState.DefaultRoot) && UsusState.IsLocalPath(@"C:\x"), "本机路径判定");

    var state = new UsusState(bus, null);
    var registry = new Registrar();
    UsusModule.Register(registry, state);
    var search = await Run(registry, "kb.search", ("q", "螺栓防松"));
    True(search.Message.Contains("U0001", StringComparison.Ordinal), search.Message);
    var reads = bus.Reads;
    await Run(registry, "kb.search", ("q", "螺栓"));
    Equal(reads, bus.Reads);

    files["连接/U0002-张紧.md"] = EntryText("U0002", "同步带张紧轮放松边", "[同步带]", "张紧轮装在松边。");
    var after = await Run(registry, "kb.search", ("q", "同步带"));
    True(after.Message.Contains("U0002", StringComparison.Ordinal), after.Message);
    Equal(reads + 1, bus.Reads);

    var missing = new UsusState(new FakeZBus(null, files), null);
    var missingRegistry = new Registrar();
    UsusModule.Register(missingRegistry, missing);
    var check = await Run(missingRegistry, "kb.check");
    True(!check.Success && check.Message.Contains("diana", StringComparison.Ordinal), check.Message);
}

static string EntryText(string id, string title, string tags, string body)
    => $"---\nid: {id}\ntitle: {title}\ntags: {tags}\ndate: 2026-11-03\n---\n\n{body}\n";

static async Task<CommandResult> Run(Registrar registry, string command, params (string Name, string Value)[] args)
{
    True(registry.TryGet(UsusIdentity.Domain + "." + command, out var descriptor), $"未注册 {command}");
    var arguments = args.ToDictionary(arg => arg.Name, arg => arg.Value);
    return await descriptor!.Handler(new CommandContext(descriptor, arguments, "test", null, CancellationToken.None));
}

static void TestPageOwner()
{
    // Aurora 的判据：owner = "History" + 首字母大写的指令域；对不上整页被静默拒收。
    var expected = "History" + char.ToUpperInvariant(UsusIdentity.Domain[0]) + UsusIdentity.Domain[1..];
    Equal(expected, UsusIdentity.PageOwner);

    using var description = JsonDocument.Parse(UsusPage.Describe());
    var root = description.RootElement;
    Equal(expected, root.GetProperty("owner").GetString()!);
    Equal(1, root.GetProperty("schemaVersion").GetInt32());
    foreach (var page in root.GetProperty("pages").EnumerateArray())
        Equal(expected, page.GetProperty("scene").GetString()!);

    using var actions = JsonDocument.Parse(UsusPage.Actions());
    Equal(expected, actions.RootElement.GetProperty("owner").GetString()!);
}

static void TestPageWiring()
{
    var registry = Registry();
    using var actions = JsonDocument.Parse(UsusPage.Actions());
    var declared = new HashSet<string>(StringComparer.Ordinal);
    foreach (var action in actions.RootElement.GetProperty("actions").EnumerateArray())
    {
        var id = action.GetProperty("id").GetString()!;
        var command = action.GetProperty("command").GetString()!;
        True(declared.Add(id), $"动作 id 重复：{id}");
        True(registry.TryGet(command.Split(' ')[0], out _), $"动作 {id} 指向未注册的指令 {command}");
    }

    using var description = JsonDocument.Parse(UsusPage.Describe());
    var nodes = Descendants(description.RootElement).ToList();
    foreach (var button in nodes.Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() == "button"))
    {
        var action = button.GetProperty("action").GetString()!;
        True(declared.Contains(action), $"按钮指向未声明的动作 {action}");
    }

    foreach (var source in nodes.Where(node => node.TryGetProperty("dataSource", out _)))
    {
        var command = source.GetProperty("dataSource").GetProperty("command").GetString()!;
        True(registry.TryGet(command, out _), $"表格取数指令未注册：{command}");
    }
}

static void TestVersionsAgree()
{
    var manifest = ReadManifest(Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
    Equal(UsusIdentity.Name, manifest.GetProperty("name").GetString()!);
    Equal(UsusIdentity.Name + ".dll", manifest.GetProperty("artifact").GetString()!);
    Equal(new ModuleInfo().Version, manifest.GetProperty("version").GetString()!);
    Equal(UsusIdentity.Name, new ModuleInfo().ModuleName);
    Equal(UsusIdentity.Description, manifest.GetProperty("description").GetString()!);
}

static async Task TestHostLoadsPackage()
{
    var root = Path.Combine(Path.GetTempPath(), UsusIdentity.Name + ".Tests", Guid.NewGuid().ToString("N"));
    var package = Path.Combine(root, UsusIdentity.Name);
    Directory.CreateDirectory(package);
    try
    {
        var manifest = ReadManifest(Path.Combine(AppContext.BaseDirectory, "module.manifest.json"));
        foreach (var file in new[]
                 {
                     "module.manifest.json",
                     manifest.GetProperty("artifact").GetString()!,
                     manifest.GetProperty("docs").GetString()!,
                 })
        {
            var from = Path.Combine(AppContext.BaseDirectory, file);
            True(File.Exists(from), $"构建产物缺少 {file}");
            File.Copy(from, Path.Combine(package, file));
        }

        // 宿主 7.0 起只接受 nupkg；只在临时目录构造测试候选，不安装或修改运行区。
        var archive = Path.Combine(root, UsusIdentity.Name + ".nupkg");
        WriteNugetPackage(package, archive, manifest.GetProperty("version").GetString()!);

        var echo = await Probe(archive, UsusIdentity.Domain + ".kb.search q=螺栓防松");
        True(echo.GetProperty("data").ValueKind == JsonValueKind.Object, "宿主探针未返回装载数据：" + echo.GetRawText());
        var module = echo.GetProperty("data").GetProperty("module");
        True(module.GetProperty("attached").GetBoolean(),
            "宿主没有接上模块：" + string.Join("；", module.GetProperty("attachFailures").EnumerateArray().Select(item => item.GetString()))
            + "；诊断：" + string.Join("；", echo.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetString())));
        Equal(UsusIdentity.Name, module.GetProperty("name").GetString());
        Equal(9, module.GetProperty("commandCount").GetInt32());
        // 0.3.0：经验库经 diana.z 读；探针宿主只装本模块，没有 Diana，必须是说清原因的失败，不是执行异常。
        var result = echo.GetProperty("data").GetProperty("result");
        var message = result.GetProperty("message").GetString() ?? "";
        True(!result.GetProperty("success").GetBoolean() && message.Contains("diana", StringComparison.Ordinal), message);
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

// 调已发布宿主的 `HistoryVulcan.Cli.exe --probe`：宿主根目录在构建时写进本程序集（见 csproj 的 HistoryVulcanHostRoot）。
static async Task<JsonElement> Probe(string package, string command)
{
    var hostRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(item => item.Key == "HistoryVulcanHostRoot")?.Value;
    if (string.IsNullOrEmpty(hostRoot))
        throw new InvalidOperationException("构建时没有写入 HistoryVulcanHostRoot。");
    var cli = Path.Combine(hostRoot, "HistoryVulcan.Cli.exe");
    True(File.Exists(cli), $"找不到宿主命令行：{cli}（宿主 5.9.0 起提供 --probe）");

    var start = new ProcessStartInfo(cli)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (var argument in new[] { "--probe", package, "--format", "json", "--cli" }.Concat(command.Split(' ')))
        start.ArgumentList.Add(argument);

    using var process = Process.Start(start)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    var error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    True(output.TrimStart().StartsWith('{'), $"--probe 没有输出 JSON（退出码 {process.ExitCode}）：{output}{error}");
    using var document = JsonDocument.Parse(output);
    return document.RootElement.Clone();
}

static JsonElement ReadManifest(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.Clone();
}

// 与 eng/Build-*Package.ps1 同一格式：大写十六进制、两个空格、正斜杠路径、无 BOM。
// 与宿主实际 nupkg 相同：nuspec 与模块有效载荷平铺，不带运行区状态或数据。
static void WriteNugetPackage(string package, string archive, string version)
{
    File.WriteAllText(Path.Combine(package, UsusIdentity.Name + ".nuspec"), $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata><id>{UsusIdentity.Name}</id><version>{version}</version>
          <authors>HistoryVulcan</authors><description>{UsusIdentity.Name}</description></metadata>
        </package>
        """, new UTF8Encoding(false));
    ZipFile.CreateFromDirectory(package, archive);
}

static IEnumerable<JsonElement> Descendants(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        yield return element;
        foreach (var property in element.EnumerateObject())
            foreach (var child in Descendants(property.Value))
                yield return child;
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in element.EnumerateArray())
            foreach (var child in Descendants(item))
                yield return child;
    }
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
}

/// <summary>
/// 模块登记口的测试替身：只记下登记了哪些指令。宿主 6.0.0 起注册表是宿主内部类，
/// 登记口 ICommandRegistrar 就是模块能看到的全部；来源由宿主盖章，这里不再核对。
/// </summary>
sealed class Registrar : ICommandRegistrar
{
    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<CommandDescriptor> All => _commands.Values;

    public void Register(CommandDescriptor descriptor)
    {
        if (!_commands.TryAdd(descriptor.Name, descriptor))
            throw new InvalidOperationException($"重复登记 {descriptor.Name}");
    }

    public bool TryGet(string name, out CommandDescriptor? descriptor) => _commands.TryGetValue(name, out descriptor);
}
