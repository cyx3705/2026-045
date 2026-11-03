using System.Globalization;
using System.Text;
using HistoryVulcan.Core.Commands;
using HistoryVulcan.Core.Modules;
using Usus.Knowledge;

namespace Usus;

/// <summary>
/// 模块装配入口：经验库指令 <c>usus.kb.*</c> + Aurora 页面协议三条指令（describe / actions / data）。
/// </summary>
/// <remarks>
/// 经验是数据不是指令：总线上只有检索与查看，加经验就是在经验库目录里写文件。
/// 只读查询标 <c>Readonly</c>（mcpExposure=readonly 时只有它们投影到 MCP）；界面内部协议一律带 <c>HiddenReason</c>。
/// </remarks>
public sealed class UsusModule : IModuleContextAware
{
    private const string Hidden = "Aurora 页面内部协议，不对远程消费面暴露";
    private const string Kb = "kb";

    /// <summary>宿主在装载时注入权威指令总线与命令注册器。</summary>
    public void Attach(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = new UsusState(context.Bus, context.Environment.DataDirectory);
        context.RegisterCommands(registry => Register(registry, state));
    }

    internal static void Register(ICommandRegistrar registry, UsusState state)
    {
        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.search",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "按问题检索机械设计经验，返回最相关的几条及出处",
            Example = UsusIdentity.Domain + ".kb.search q=振动场合螺栓怎么防松 k=5",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "q",
                    Description = "问题或关键词，中文自然语言即可；型号、标准号照原样写，例如 M8 螺栓防松 GB/T 93",
                    Type = ParamType.String,
                    Required = true,
                },
                new ParameterSpec
                {
                    Name = "k",
                    Description = "最多返回几条，1–20 的整数",
                    Type = ParamType.Int,
                    Default = "5",
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var k = context.GetInt("k", 5);
                if (k is < 1 or > 20)
                    return CommandResult.Fail("k 须为 1–20 的整数");
                var hits = state.Knowledge.Search(context.RequireString("q"), k);
                if (hits.Count == 0)
                    return CommandResult.Ok("没有找到相关经验", Array.Empty<object>());
                var text = new StringBuilder($"找到 {hits.Count} 条：");
                foreach (var hit in hits)
                    text.Append('\n').Append($"{hit.Entry.Id} {hit.Entry.Title}——{hit.Entry.Snippet()}（{hit.Entry.RelativePath}）");
                return CommandResult.Ok(text.ToString(), hits.Select(HitData).ToList());
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.show",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "按编号取一条经验的全文与出处",
            Example = UsusIdentity.Domain + ".kb.show id=U0001",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "id",
                    Description = "经验编号，U 加四位数字，例如 U0001；编号来自 kb.search 或 kb.list",
                    Type = ParamType.String,
                    Required = true,
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var id = context.RequireString("id");
                var entry = state.Knowledge.Find(id);
                return entry is null
                    ? CommandResult.Fail($"经验库里没有 {id}")
                    : CommandResult.Ok($"{entry.Id} {entry.Title}\n\n{entry.Body}", EntryData(entry, includeBody: true));
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.list",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "列出经验条目的编号、标题与标签，可按标签筛选",
            Example = UsusIdentity.Domain + ".kb.list tag=螺栓",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "tag",
                    Description = "只列带这个标签的条目，完整匹配、不分大小写，例如 螺栓；省略时列全部",
                    Type = ParamType.String,
                },
            ],
            Handler = CommandDescriptor.Sync(context =>
            {
                var tag = context.GetString("tag")?.Trim();
                var entries = state.Knowledge.Current().Library.Entries
                    .Where(entry => string.IsNullOrEmpty(tag) || entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                    .ToList();
                var text = new StringBuilder($"共 {entries.Count} 条");
                foreach (var entry in entries)
                    text.Append('\n').Append($"{entry.Id} {entry.Title}");
                return CommandResult.Ok(text.ToString(), entries.Select(entry => EntryData(entry, includeBody: false)).ToList());
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.check",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "检查经验库：位置、条目数、格式不合的文件，并给出下一个可用编号",
            Example = UsusIdentity.Domain + ".kb.check",
            Readonly = true,
            Handler = CommandDescriptor.Sync(_ =>
            {
                var library = state.Knowledge.Current().Library;
                if (!Directory.Exists(library.Root))
                    return CommandResult.Fail($"经验库目录不存在：{library.Root}");
                var next = NextId(library);
                var text = new StringBuilder($"经验库 {library.Root}：{library.Entries.Count} 条，格式问题 {library.Problems.Count} 个，下一个编号 {next}");
                foreach (var problem in library.Problems)
                    text.Append('\n').Append($"{problem.RelativePath}：{problem.Reason}");
                return CommandResult.Ok(text.ToString(), new Dictionary<string, object>
                {
                    ["root"] = library.Root,
                    ["count"] = library.Entries.Count,
                    ["nextId"] = next,
                    ["problems"] = library.Problems
                        .Select(problem => new Dictionary<string, string> { ["path"] = problem.RelativePath, ["reason"] = problem.Reason })
                        .ToList(),
                });
            }),
        });

        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.refresh",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "重新读经验库并刷新页面表格（检索前本来就会按文件变化自动重读）",
            Example = UsusIdentity.Domain + ".kb.refresh",
            Level = CommandLevel.Run,
            Handler = async _ =>
            {
                var library = state.Knowledge.Current().Library;
                await state.RefreshPageAsync().ConfigureAwait(false);
                return CommandResult.Ok($"经验库 {library.Entries.Count} 条，格式问题 {library.Problems.Count} 个");
            },
        });

        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".kb.root",
            Domain = UsusIdentity.Domain,
            CommandClass = Kb,
            Summary = "改经验库目录位置，存进模块设置",
            Example = UsusIdentity.Domain + @".kb.root path=D:\临时\经验库",
            Level = CommandLevel.Run,
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "path",
                    Description = @"经验库目录的绝对路径；传空串回到缺省位置 C:\OneHistory\HistoryClio\2026-045-Usus\z-经验库",
                    Type = ParamType.String,
                    Required = true,
                },
            ],
            Handler = async context =>
            {
                var root = await state.SetRootAsync(context.GetString("path") ?? "").ConfigureAwait(false);
                return Directory.Exists(root)
                    ? CommandResult.Ok($"经验库位置改为 {root}")
                    : CommandResult.Ok($"经验库位置改为 {root}（目录还不存在）");
            },
        });

        registry.Register(Internal("describe", "返回页面描述", _ => Json(UsusPage.Describe())));
        registry.Register(Internal("actions", "返回页面动作声明", _ => Json(UsusPage.Actions())));
        registry.Register(new CommandDescriptor
        {
            Name = UsusIdentity.Domain + ".ui.data",
            Domain = UsusIdentity.Domain,
            CommandClass = "ui",
            Summary = "返回页面表格的行",
            Readonly = true,
            HiddenReason = Hidden,
            AllowUnspecifiedParameters = true,
            Handler = CommandDescriptor.Sync(context =>
                context.GetString("view")?.Trim().ToLowerInvariant() is null or UsusPage.EntriesView
                    ? CommandResult.Ok("经验条目", state.Rows())
                    : CommandResult.Fail($"未知 view；支持 {UsusPage.EntriesView}")),
        });
    }

    internal static string NextId(Library library)
    {
        // 格式不合的文件也占着编号：按文件名取，编号只增不复用。
        var max = library.Entries.Select(entry => entry.RelativePath)
            .Concat(library.Problems.Select(problem => problem.RelativePath))
            .Select(path => Path.GetFileName(path))
            .Where(name => name.Length >= 5 && name[0] == 'U' && name.AsSpan(1, 4).IndexOfAnyExceptInRange('0', '9') < 0)
            .Select(name => int.Parse(name.AsSpan(1, 4), CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return "U" + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object> HitData(Hit hit)
    {
        var data = EntryData(hit.Entry, includeBody: false);
        data["score"] = Math.Round(hit.Score, 3);
        data["snippet"] = hit.Entry.Snippet();
        return data;
    }

    private static Dictionary<string, object> EntryData(Entry entry, bool includeBody)
    {
        var data = new Dictionary<string, object>
        {
            ["id"] = entry.Id,
            ["title"] = entry.Title,
            ["tags"] = entry.Tags,
            ["path"] = entry.RelativePath,
        };
        if (entry.Scope is not null)
            data["scope"] = entry.Scope;
        if (entry.Source is not null)
            data["source"] = entry.Source;
        if (entry.Date is not null)
            data["date"] = entry.Date;
        if (includeBody)
            data["body"] = entry.Body;
        return data;
    }

    private static CommandResult Json(string json) => CommandResult.Ok(json, json);

    private static CommandDescriptor Internal(string method, string summary, Func<CommandContext, CommandResult> handler)
        => new()
        {
            Name = UsusIdentity.Domain + ".ui." + method,
            Domain = UsusIdentity.Domain,
            CommandClass = "ui",
            Summary = summary,
            Readonly = true,
            HiddenReason = Hidden,
            Handler = CommandDescriptor.Sync(handler),
        };
}
