namespace Usus.Knowledge;

/// <summary>
/// 经验库的当前快照：每次取用前比对文件指纹，变了才重新读库、重建索引。
/// 索引只在内存里，正文永远以经验库目录为准。
/// </summary>
public sealed class KnowledgeBase(Func<string> root)
{
    private readonly object _gate = new();
    private string? _fingerprint;
    private string? _root;
    private Library? _library;
    private KeywordIndex? _index;

    public (Library Library, KeywordIndex Index) Current()
    {
        var path = root();
        var fingerprint = Library.Fingerprint(path);
        lock (_gate)
        {
            if (_library is null || _index is null || _root != path || _fingerprint != fingerprint)
            {
                _library = Library.Load(path);
                _index = new KeywordIndex(_library.Entries);
                _root = path;
                _fingerprint = fingerprint;
            }

            return (_library, _index);
        }
    }

    public IReadOnlyList<Hit> Search(string query, int top) => Current().Index.Search(query, top);

    public Entry? Find(string id)
        => Current().Library.Entries.FirstOrDefault(entry => entry.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
}
