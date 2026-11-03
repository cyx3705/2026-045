using System.Text;

namespace Usus.Knowledge;

/// <summary>一条检索命中。</summary>
public sealed record Hit(Entry Entry, double Score);

/// <summary>
/// 关键词检索（BM25）。中文没有分词器，按连续汉字的二元组切；字母数字整段保留，
/// 所以 <c>HGH20CA</c>、<c>M8</c>、<c>5783</c> 都能原样命中。标题与标签按重复次数加权。
/// </summary>
/// <remarks>语义检索接进来后与它各出一批再合并，见现行约定「混合检索」。</remarks>
public sealed class KeywordIndex
{
    private const double K1 = 1.2;
    private const double B = 0.75;
    private const int TitleWeight = 3;
    private const int TagWeight = 3;

    private readonly List<(Entry Entry, Dictionary<string, int> Terms, int Length)> _documents = [];
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    public KeywordIndex(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            var tokens = new List<string>();
            for (var i = 0; i < TitleWeight; i++)
                tokens.AddRange(Tokenize(entry.Title));
            for (var i = 0; i < TagWeight; i++)
                tokens.AddRange(entry.Tags.SelectMany(Tokenize));
            tokens.AddRange(Tokenize(entry.Scope ?? ""));
            tokens.AddRange(Tokenize(entry.Body));

            var terms = tokens.GroupBy(token => token, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            foreach (var term in terms.Keys)
                _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
            _documents.Add((entry, terms, tokens.Count));
        }

        _averageLength = _documents.Count == 0 ? 1 : _documents.Average(document => (double)document.Length);
    }

    public int Count => _documents.Count;

    public IReadOnlyList<Hit> Search(string query, int top)
    {
        var terms = Tokenize(query).Distinct(StringComparer.Ordinal).ToList();
        if (terms.Count == 0)
            return [];

        var hits = new List<Hit>();
        foreach (var (entry, documentTerms, length) in _documents)
        {
            var score = 0.0;
            foreach (var term in terms)
            {
                if (!documentTerms.TryGetValue(term, out var frequency))
                    continue;
                var n = _documentFrequency[term];
                var idf = Math.Log(1 + (_documents.Count - n + 0.5) / (n + 0.5));
                score += idf * frequency * (K1 + 1) / (frequency + K1 * (1 - B + B * length / _averageLength));
            }

            if (score > 0)
                hits.Add(new Hit(entry, score));
        }

        return hits.OrderByDescending(hit => hit.Score).ThenBy(hit => hit.Entry.Id, StringComparer.Ordinal).Take(top).ToList();
    }

    /// <summary>字母数字整段（小写）；连续汉字切二元组，单个汉字保留为一元。</summary>
    public static IEnumerable<string> Tokenize(string text)
    {
        var word = new StringBuilder();
        var han = new StringBuilder();
        foreach (var c in text + " ")
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                foreach (var token in FlushHan(han))
                    yield return token;
                word.Append(char.ToLowerInvariant(c));
            }
            else if (IsHan(c))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                han.Append(c);
            }
            else
            {
                if (word.Length > 0)
                {
                    yield return word.ToString();
                    word.Clear();
                }

                foreach (var token in FlushHan(han))
                    yield return token;
            }
        }
    }

    private static IEnumerable<string> FlushHan(StringBuilder han)
    {
        if (han.Length == 0)
            yield break;
        var run = han.ToString();
        han.Clear();
        if (run.Length == 1)
        {
            yield return run;
            yield break;
        }

        for (var i = 0; i + 1 < run.Length; i++)
            yield return run.Substring(i, 2);
    }

    private static bool IsHan(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿';
}
