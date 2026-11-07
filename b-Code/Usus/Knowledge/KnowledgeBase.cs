namespace Usus.Knowledge;

/// <summary>
/// 经验库的当前快照：每次取用前列一次候选文件比对指纹，变了才重新读库、重建索引。
/// 索引只在内存里，正文永远以经验库为准。
/// </summary>
public sealed class KnowledgeBase(Func<IEntrySource> source)
{
    private readonly object _gate = new();
    private string? _fingerprint;
    private string? _location;
    private Library? _library;
    private KeywordIndex? _index;

    public (Library Library, KeywordIndex Index) Current()
    {
        var current = source();
        var files = current.List();
        var fingerprint = Library.Fingerprint(files);
        lock (_gate)
        {
            if (_library is null || _index is null || _location != current.Location || _fingerprint != fingerprint)
            {
                _library = Library.Load(current, files);
                _index = new KeywordIndex(_library.Entries);
                _location = current.Location;
                _fingerprint = fingerprint;
            }

            return (_library, _index);
        }
    }

    public IReadOnlyList<Hit> Search(string query, int top) => Current().Index.Search(query, top);

    public Entry? Find(string id)
        => Current().Library.Entries.FirstOrDefault(entry => entry.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
}
