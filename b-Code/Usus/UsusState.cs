using System.Text.Json;
using HistoryVulcan.Core.Commands;
using Usus.Knowledge;

namespace Usus;

/// <summary>
/// 模块的运行状态：经验库位置（存在运行区数据目录的 settings.json）与经验库快照。
/// 运行区只放这个设置和可重建的索引，经验正文永远在经验库目录里。
/// </summary>
internal sealed class UsusState
{
    /// <summary>缺省的经验库位置：项目库里本仓的 z-经验库。</summary>
    public const string DefaultRoot = @"C:\OneHistory\HistoryClio\2026-045-Usus\z-经验库";

    private readonly ICommandBus? _bus;
    private readonly string? _settingsPath;
    private readonly object _gate = new();
    private string _root;

    public UsusState(ICommandBus? bus, string? dataDirectory, string? root = null)
    {
        _bus = bus;
        _settingsPath = dataDirectory is null ? null : Path.Combine(dataDirectory, "settings.json");
        _root = root ?? ReadRoot() ?? DefaultRoot;
        Knowledge = new KnowledgeBase(() => Root);
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

    /// <summary>改经验库位置并存进设置；空串表示回到缺省位置。</summary>
    public async Task<string> SetRootAsync(string path)
    {
        var root = string.IsNullOrWhiteSpace(path) ? DefaultRoot : Path.GetFullPath(path.Trim());
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
            return document.RootElement.TryGetProperty("root", out var root) ? root.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
