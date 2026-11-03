namespace Usus.Knowledge;

/// <summary>一条经验：<c>z-经验库/</c> 下的一个 Markdown 文件。字段含义见经验库 README。</summary>
public sealed record Entry(
    string Id,
    string Title,
    IReadOnlyList<string> Tags,
    string? Scope,
    string? Source,
    string? Date,
    string Body,
    string RelativePath)
{
    /// <summary>检索片段：正文第一段（结论放第一句的约定就是为了这里）。</summary>
    public string Snippet(int maxLength = 120)
    {
        var first = Body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
        return first.Length <= maxLength ? first : first[..maxLength] + "…";
    }
}

/// <summary>经验库里不合格式的文件，及原因。</summary>
public sealed record Problem(string RelativePath, string Reason);
