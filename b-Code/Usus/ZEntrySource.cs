using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using HistoryVulcan.Core.Commands;
using Usus.Knowledge;

namespace Usus;

/// <summary>
/// 经 <c>diana.z.*</c> 读 z-经验库（0.3.0）。z 级文件夹只在源码区机器上、从不迁移，Usus 迁到哪台机器都经指令切面读它，
/// 不认本机路径。正文按指纹（路径、大小、修改时间）缓存，没变的文件不重读。
/// </summary>
internal sealed class ZEntrySource(ICommandBus bus, string address) : IEntrySource
{
    private const int ChunkBytes = 1024 * 1024;
    private readonly ConcurrentDictionary<string, string> _texts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Location => address;

    /// <inheritdoc />
    public IReadOnlyList<SourceFile> List()
    {
        var data = Invoke($"diana.z.tree path={CommandParser.QuoteArg(address)} depth=10 limit=5000");
        if (data.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"经验库超过 5000 项，diana.z.tree 已截断：{address}");
        var root = data.GetProperty("path").GetString()!;
        return data.GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("kind").GetString() == "file")
            .Select(entry => (Entry: entry, Relative: entry.GetProperty("path").GetString()![(root.Length + 1)..]))
            .Where(item => DirectoryEntrySource.IsCandidate(item.Relative))
            .OrderBy(item => item.Relative, StringComparer.Ordinal)
            .Select(item => new SourceFile(item.Relative, item.Entry.GetProperty("bytes").GetInt64(), item.Entry.GetProperty("modified").GetString() ?? ""))
            .ToList();
    }

    /// <inheritdoc />
    public string Read(SourceFile file)
    {
        var key = $"{file.RelativePath}|{file.Length}|{file.Modified}";
        if (_texts.TryGetValue(key, out var cached))
            return cached;

        var text = new StringBuilder();
        var offset = 0;
        for (var guard = 0; guard < 64; guard++)
        {
            var path = CommandParser.QuoteArg($"{address}/{file.RelativePath}");
            var data = Invoke($"diana.z.read path={path} offset={offset} length={ChunkBytes} encoding=text");
            text.Append(data.GetProperty("content").GetString());
            offset = data.GetProperty("nextOffset").GetInt32();
            if (data.GetProperty("eof").GetBoolean())
                break;
        }

        var result = text.ToString();
        // 旧版本的同一路径不再需要。
        foreach (var stale in _texts.Keys.Where(k => k.StartsWith(file.RelativePath + "|", StringComparison.Ordinal) && k != key))
            _texts.TryRemove(stale, out _);
        _texts[key] = result;
        return result;
    }

    private JsonElement Invoke(string command)
    {
        var result = bus.InvokeAsync(command, "").GetAwaiter().GetResult();
        if (!result.Success)
            throw new InvalidOperationException($"读经验库失败（{command.Split(' ')[0]}，需要源码区的 HistoryDiana 3.0+）：{result.Message}");
        if (result.Data is null)
            throw new InvalidOperationException($"{command.Split(' ')[0]} 没有返回数据");
        return result.Data is JsonElement element ? element : JsonSerializer.SerializeToElement(result.Data);
    }
}
