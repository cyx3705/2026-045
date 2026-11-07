using System.Text.Json;
using System.Text.RegularExpressions;
using HistoryVulcan.Core.Commands;

/// <summary>
/// 假的源码区：只答 diana.z.tree / diana.z.read，数据来自内存字典。root 为 null 时模拟 Diana 没装载。
/// </summary>
sealed partial class FakeZBus(string? root, Dictionary<string, string> files) : ICommandBus
{
    public int Reads { get; private set; }

    public Task<CommandResult> ExecuteAsync(string text, string source, CancellationToken cancellation = default)
        => InvokeAsync(text, source, cancellation);

    public Task<CommandResult> InvokeAsync(string text, string source, CancellationToken cancellation = default)
    {
        var name = text.Split(' ')[0];
        if (root is null || !name.StartsWith("diana.z.", StringComparison.Ordinal))
            return Task.FromResult(CommandResult.Fail($"未知指令 {name}"));
        var match = PathArgument().Match(text);
        var path = match.Groups[1].Success ? Regex.Unescape(match.Groups[1].Value) : match.Groups[2].Value;

        if (name == "diana.z.tree")
        {
            if (path != "2026-045/z-经验库")
                return Task.FromResult(CommandResult.Fail("地址不对：" + path));
            var entries = files.Keys.OrderBy(key => key, StringComparer.Ordinal).Select(key => new
            {
                path = $"{root}/{key}",
                kind = "file",
                bytes = (long)files[key].Length,
                modified = files[key].GetHashCode().ToString(),
            }).ToList();
            return Task.FromResult(CommandResult.Ok("tree", JsonSerializer.SerializeToElement(new { path = root, truncated = false, entries })));
        }

        Reads++;
        var relative = path["2026-045/z-经验库/".Length..];
        var content = files[relative];
        return Task.FromResult(CommandResult.Ok("read", JsonSerializer.SerializeToElement(new
        {
            content,
            nextOffset = System.Text.Encoding.UTF8.GetByteCount(content),
            eof = true,
        })));
    }

    public bool RequestConfirmation(string prompt) => false;

    [GeneratedRegex("path=(?:\"((?:[^\"\\\\]|\\\\.)*)\"|(\\S+))")]
    private static partial Regex PathArgument();
}
