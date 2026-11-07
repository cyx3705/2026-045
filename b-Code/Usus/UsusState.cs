using System.Text.Json;
using HistoryVulcan.Core.Commands;
using Usus.Knowledge;

namespace Usus;

/// <summary>
/// 模块的运行状态：经验库位置（存在运行区数据目录的 settings.json）与经验库快照。
/// 运行区只放这个设置和可重建的索引，经验正文永远在 z-经验库里。
/// 位置缺省是 z 地址，经 <c>diana.z.*</c> 读（0.3.0）；只有写成本机绝对路径时才直接读目录（测试与排障）。
/// </summary>
internal sealed class UsusState
{
    /// <summary>缺省的经验库位置：本仓 z-经验库的逻辑地址，不依赖本机目录布局。</summary>
    public const string DefaultRoot = "2026-045/z-经验库";

    private readonly ICommandBus? _bus;
    private readonly string? _settingsPath;
    private readonly object _gate = new();
    private string _root;
    private IEntrySource? _source;
    private string? _sourceRoot;

    public UsusState(ICommandBus? bus, string? dataDirectory, string? root = null)
    {
        _bus = bus;
        _settingsPath = dataDirectory is null ? null : Path.Combine(dataDirectory, "settings.json");
        _root = root ?? ReadRoot() ?? DefaultRoot;
        Knowledge = new KnowledgeBase(Source);
    }

    /// <summary>本机绝对路径（盘符或 UNC）才直接读目录；其余都当 z 地址。</summary>
    public static bool IsLocalPath(string root) => Path.IsPathFullyQualified(root);

    /// <summary>当前位置对应的读取方式；位置不变时复用同一个（z 读取带正文缓存）。</summary>
    public IEntrySource Source()
    {
        var root = Root;
        lock (_gate)
        {
            if (_source is null || _sourceRoot != root)
            {
                _source = IsLocalPath(root)
                    ? new DirectoryEntrySource(root)
                    : new ZEntrySource(_bus ?? throw new InvalidOperationException("没有命令总线，无法经 diana.z 读经验库"), root);
                _sourceRoot = root;
            }

            return _source;
        }
    }

    public KnowledgeBase Knowledge { get; }

    public string Root
    {
        get
        {
            lock (_gate)
                return _root;
        }
    }

    /// <summary>改经验库位置并存进设置；空串表示回到缺省位置。z 地址原样存，本机路径取绝对形式。</summary>
    public async Task<string> SetRootAsync(string path)
    {
        var trimmed = (path ?? "").Trim();
        var root = trimmed.Length == 0 ? DefaultRoot
            : IsLocalPath(trimmed) ? Path.GetFullPath(trimmed)
            : trimmed.Replace('\\', '/').Trim('/');
        lock (_gate)
            _root = root;
        if (_settingsPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { root }));
        }

        await RefreshPageAsync().ConfigureAwait(false);
        return root;
    }

    /// <summary>表格的行：按编号排序的全部条目。</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows()
        => Knowledge.Current().Library.Entries
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["id"] = entry.Id,
                ["title"] = entry.Title,
                ["tags"] = string.Join("、", entry.Tags),
                ["path"] = entry.RelativePath,
            })
            .ToList();

    /// <summary>页面没开着或没装 Aurora 时刷不到，不影响指令结果。</summary>
    public async Task RefreshPageAsync()
    {
        if (_bus is null)
            return;
        try
        {
            await _bus.ExecuteAsync(
                $"aurora.ui.refreshdata node={UsusPage.EntriesTableId}",
                // 来源由宿主盖章（module:Usus），模块不自填。
                "").ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private string? ReadRoot()
    {
        if (_settingsPath is null || !File.Exists(_settingsPath))
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var root = document.RootElement.TryGetProperty("root", out var value) ? value.GetString() : null;
            // 0.2.0 把缺省位置写成本机绝对路径存进设置；那份旧缺省一律按新缺省（z 地址）读。
            return root is not null && root.Replace('\\', '/').TrimEnd('/').EndsWith("/2026-045-Usus/z-经验库", StringComparison.OrdinalIgnoreCase)
                ? null
                : root;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
