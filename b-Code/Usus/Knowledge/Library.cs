using System.Text.RegularExpressions;

namespace Usus.Knowledge;

/// <summary>
/// 读经验库：从 <see cref="IEntrySource"/> 取 <c>*.md</c> 候选（已跳过 README 与 <c>_</c> 开头的文件），解析文件头。
/// 不引用宿主；格式不对的文件不进库，记进 <see cref="Problems"/>。
/// </summary>
public sealed partial class Library
{
    private Library(string root, IReadOnlyList<Entry> entries, IReadOnlyList<Problem> problems)
    {
        Root = root;
        Entries = entries;
        Problems = problems;
    }

    /// <summary>经验库位置（z 地址或本机目录），只用于展示。</summary>
    public string Root { get; }
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<Problem> Problems { get; }

    /// <summary>候选文件的指纹（路径、大小、修改时间）；变了才需要重建索引。</summary>
    public static string Fingerprint(IReadOnlyList<SourceFile> files)
        => string.Join('\n', files.Select(file => $"{file.RelativePath}|{file.Length}|{file.Modified}"));

    /// <summary>本机目录作经验库（测试与排障）。</summary>
    public static Library Load(string root) => Load(new DirectoryEntrySource(root));

    public static Library Load(IEntrySource source) => Load(source, source.List());

    public static Library Load(IEntrySource source, IReadOnlyList<SourceFile> files)
    {
        var entries = new List<Entry>();
        var problems = new List<Problem>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relative = file.RelativePath;
            var entry = Parse(source.Read(file), relative, out var reason);
            if (entry is null)
            {
                problems.Add(new Problem(relative, reason!));
                continue;
            }

            if (seen.TryGetValue(entry.Id, out var other))
            {
                problems.Add(new Problem(relative, $"编号 {entry.Id} 与 {other} 重复"));
                continue;
            }

            seen[entry.Id] = relative;
            entries.Add(entry);
        }

        return new Library(source.Location, entries, problems);
    }

    /// <summary>解析一个条目文件；不合格式时返回 null 并给出原因。</summary>
    public static Entry? Parse(string text, string relativePath, out string? reason)
    {
        reason = null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            reason = "缺少文件头（第一行应为 ---）";
            return null;
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
        {
            reason = "文件头没有结束的 ---";
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < end; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                fields[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        foreach (var required in new[] { "id", "title", "tags", "date" })
        {
            if (!fields.TryGetValue(required, out var value) || value.Length == 0)
            {
                reason = $"文件头缺少 {required}";
                return null;
            }
        }

        var id = fields["id"];
        if (!IdPattern().IsMatch(id))
        {
            reason = $"编号 {id} 不是 U + 四位数字";
            return null;
        }

        if (!Path.GetFileName(relativePath).StartsWith(id + "-", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"文件名应以 {id}- 开头";
            return null;
        }

        var tags = fields["tags"].Trim('[', ']')
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var body = string.Join('\n', lines.Skip(end + 1)).Trim();
        return new Entry(
            id,
            fields["title"],
            tags,
            fields.GetValueOrDefault("scope"),
            fields.GetValueOrDefault("source"),
            fields["date"],
            body,
            relativePath);
    }

    [GeneratedRegex("^U[0-9]{4}$")]
    private static partial Regex IdPattern();
}
