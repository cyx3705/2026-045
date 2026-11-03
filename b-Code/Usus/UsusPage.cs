using System.Text.Encodings.Web;
using System.Text.Json;

namespace Usus;

/// <summary>
/// Aurora 页面协议 V1：一页，上面一块控制面板放按钮，下面一张经验条目表。
/// </summary>
/// <remarks>
/// 三条规矩（离线测试守着）：owner 与 scene 用 <see cref="UsusIdentity.PageOwner"/>；
/// 每个按钮的 action 都在 <see cref="Actions()"/> 里声明；每个动作指向的指令都已注册。
/// </remarks>
internal static class UsusPage
{
    public const string PageId = UsusIdentity.Domain;
    public const string PanelId = "kb-actions";
    public const string EntriesTableId = "kb-entries";
    public const string EntriesView = "entries";
    public const string RefreshActionId = UsusIdentity.Domain + ".kb.refresh";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Describe() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = UsusIdentity.PageOwner,
        pages = new object[]
        {
            new
            {
                id = PageId,
                title = UsusIdentity.PageTitle,
                scene = UsusIdentity.PageOwner,
                placement = new { side = "center", visible = true, singleton = true },
                content = new
                {
                    type = "stack",
                    gap = "tight",
                    children = new object[]
                    {
                        new
                        {
                            type = "panel",
                            id = PanelId,
                            text = "经验库",
                            rows = new object[]
                            {
                                new
                                {
                                    mode = "even",
                                    widgets = new object[]
                                    {
                                        new { kind = "button", action = RefreshActionId, text = "重新读取" },
                                    },
                                },
                            },
                        },
                        new
                        {
                            type = "text",
                            style = "caption",
                            text = "加经验就是在 z-经验库 里新建 Markdown 文件；检索用 usus.kb.search。",
                        },
                        new
                        {
                            type = "table",
                            id = EntriesTableId,
                            dataSource = new
                            {
                                command = UsusIdentity.Domain + ".ui.data",
                                args = new { view = EntriesView },
                            },
                            columns = new object[]
                            {
                                new { key = "id", title = "编号", width = "80" },
                                new { key = "title", title = "标题", width = "*" },
                                new { key = "tags", title = "标签", width = "240" },
                                new { key = "path", title = "文件", width = "240" },
                            },
                        },
                    },
                },
            },
        },
    }, Options);

    public static string Actions() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = UsusIdentity.PageOwner,
        actions = new object[]
        {
            new
            {
                id = RefreshActionId,
                title = "重新读取",
                command = UsusIdentity.Domain + ".kb.refresh",
                summary = "重新读经验库并刷新表格",
            },
        },
    }, Options);
}
