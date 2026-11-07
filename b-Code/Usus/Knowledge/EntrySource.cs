namespace Usus.Knowledge;

/// <summary>经验库里的一个候选文件：相对经验库根的路径、大小、修改时间。三项合起来就是指纹。</summary>
public sealed record SourceFile(string RelativePath, long Length, string Modified);

/// <summary>
/// 经验条目从哪里读。检索核心只认这个接口，不关心文件在本机目录还是在源码区机器上（0.3.0）。
/// </summary>
/// <remarks>
/// 生产用 <c>ZEntrySource</c>：经 <c>diana.z.*</c> 按逻辑地址读 z-经验库，模块迁到哪台机器都能用；
/// <see cref="DirectoryEntrySource"/> 只给测试和排障用。
/// </remarks>
public interface IEntrySource
{
    /// <summary>给人看的位置：z 地址或本机目录。</summary>
    string Location { get; }

    /// <summary>全部 <c>*.md</c> 候选，已跳过 README 与 <c>_</c> 开头的文件，按路径排序。位置不存在时抛异常。</summary>
    IReadOnlyList<SourceFile> List();

    /// <summary>读一个候选文件的全文（UTF-8）。</summary>
    string Read(SourceFile file);
}

/// <summary>本机目录作经验库。只给测试和排障用；生产走 z 地址。</summary>
public sealed class DirectoryEntrySource(string root) : IEntrySource
{
    /// <inheritdoc />
    public string Location => root;

    /// <inheritdoc />
    public IReadOnlyList<SourceFile> List()
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"经验库目录不存在：{root}");
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: System.IO.Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(item => IsCandidate(item.Relative))
            .OrderBy(item => item.Relative, StringComparer.Ordinal)
            .Select(item =>
            {
                var info = new FileInfo(item.Path);
                return new SourceFile(item.Relative, info.Length, info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            })
            .ToList();
    }

    /// <inheritdoc />
    public string Read(SourceFile file) => File.ReadAllText(System.IO.Path.Combine(root, file.RelativePath));

    /// <summary>条目候选：<c>*.md</c>，跳过 README 与 <c>_</c> 开头的文件。</summary>
    public static bool IsCandidate(string relativePath)
    {
        var name = System.IO.Path.GetFileName(relativePath);
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
               && !name.StartsWith('_')
               && !name.Equals("README.md", StringComparison.OrdinalIgnoreCase);
    }
}
