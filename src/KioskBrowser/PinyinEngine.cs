using System.Reflection;

namespace KioskBrowser;

/// <summary>
/// 拼音引擎：单字表（音节→汉字, 高频在前）+ 词组表（连拼 key→真实词组）。
/// 候选优先级：精确词组 → 精确单字 → 词组前缀 → 音节组合兜底 → 单字前缀。
/// </summary>
public sealed class PinyinEngine
{
    private readonly Dictionary<string, string> _chars = new();
    private readonly Dictionary<string, string[]> _phrases = new();
    private readonly Dictionary<string, string[]> _initials = new();
    private readonly Dictionary<char, string> _charPrimary = new();
    private readonly Dictionary<string, string> _wordToKey = new(); // 词→连拼键（含多音字正确读音）
    private readonly List<string> _syllablesByLenDesc = new();
    private string[] _phraseKeys = Array.Empty<string>();

    // 词库配置（Configure 后生效）
    private bool _generalEnabled = true;
    private List<(string word, string key, string initials)> _boost = new();

    /// <summary>领域词库文件映射（key = 配置里的词库名）。</summary>
    private static readonly Dictionary<string, string> PackFiles = new()
    {
        ["gov"] = "vocab_gov.txt",
        ["medical"] = "vocab_medical.txt",
        ["retail"] = "vocab_retail.txt",
        ["industrial"] = "vocab_industrial.txt",
    };

    private static PinyinEngine? _instance;
    public static PinyinEngine Instance => _instance ??= Load();

    /// <summary>当前置顶词库的词列表（领域词库 + 自定义词库，热词代管用）。</summary>
    public IReadOnlyList<string> BoostWords => _boost.Select(b => b.word).ToList();

    private static PinyinEngine Load()
    {
        var engine = new PinyinEngine();
        var asm = typeof(PinyinEngine).Assembly; // 用自身程序集，测试/宿主场景都正确
        var names = asm.GetManifestResourceNames();

        using (var reader = OpenResource(asm, names, "pinyin_dict.txt"))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && parts[0].Length > 0)
                    engine._chars[parts[0]] = parts[1];
            }
        }
        using (var reader = OpenResource(asm, names, "pinyin_phrases.txt"))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length == 3 && parts[0].Length > 0)
                {
                    var words = parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    engine._phrases[parts[0]] = words;
                    // 反向索引：词 → 连拼键（取首个，多音字得到词级正确读音）
                    foreach (var w in words)
                        engine._wordToKey.TryAdd(w, parts[0]);
                }
            }
        }
        using (var reader = OpenResource(asm, names, "pinyin_initials.txt"))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && parts[0].Length > 0)
                    engine._initials[parts[0]] = parts[1].Split(' ',
                        StringSplitOptions.RemoveEmptyEntries);
            }
        }
        using (var reader = OpenResource(asm, names, "pinyin_primary.txt"))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 && parts[0].Length == 1)
                    engine._charPrimary[parts[0][0]] = parts[1];
            }
        }
        engine._syllablesByLenDesc.AddRange(engine._chars.Keys.OrderByDescending(k => k.Length));
        engine._phraseKeys = engine._phrases.Keys.OrderBy(k => k.Length).ToArray();
        KioskBrowser.Shared.Logger.Info(
            $"pinyin engine loaded: {engine._chars.Count} syllables, " +
            $"{engine._phraseKeys.Length} phrase keys, {engine._initials.Count} initials keys");
        return engine;
    }

    private static StreamReader OpenResource(Assembly asm, string[] names, string file)
    {
        var name = names.FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{file} embedded resource not found");
        return new StreamReader(asm.GetManifestResourceStream(name)!);
    }

    public List<string> GetCandidates(string input, int max = 9)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(input)) return result;

        // 0) 领域/自定义词库置顶：精确连拼 > 前缀 > 首字母
        if (_boost.Count > 0)
        {
            foreach (var (word, key, _) in _boost)
            {
                if (key == input && !result.Contains(word)) result.Add(word);
                if (result.Count >= max) break;
            }
            foreach (var (word, key, inits) in _boost)
            {
                if ((key.StartsWith(input, StringComparison.Ordinal)
                        || inits.StartsWith(input, StringComparison.Ordinal))
                    && key != input && !result.Contains(word)) result.Add(word);
                if (result.Count >= max) break;
            }
            foreach (var (word, _, inits) in _boost)
            {
                if (inits == input && !result.Contains(word)) result.Add(word);
                if (result.Count >= max) break;
            }
        }

        // 1) 精确词组（nihao → 你好），通用词库关闭时跳过
        if (_generalEnabled && _phrases.TryGetValue(input, out var words))
            foreach (var w in words)
            {
                if (!result.Contains(w)) result.Add(w);
                if (result.Count >= max) break;
            }

        // 2) 精确单字音节（ni → 你尼呢…）
        if (_chars.TryGetValue(input, out var chars))
            foreach (var c in chars.Take(max))
            {
                if (result.Count >= max) break;
                if (!result.Contains(c.ToString())) result.Add(c.ToString());
            }

        // 3) 词组前缀（niha → 你好），通用词库关闭时跳过
        if (_generalEnabled && result.Count < max && input.Length >= 2)
            foreach (var key in _phraseKeys)
            {
                if (result.Count >= max) break;
                if (key.Length <= input.Length) continue;   // keys 按长度排序, 长的都在后面
                if (!key.StartsWith(input, StringComparison.Ordinal)) continue;
                foreach (var w in _phrases[key].Take(2))
                {
                    if (!result.Contains(w)) result.Add(w);
                    if (result.Count >= max) break;
                }
            }

        // 4) 首字母输入（nh → 你好, zg → 中国），限 2~5 个字母避免噪音；通用词库关闭时跳过
        if (_generalEnabled && result.Count < max && input.Length is >= 2 and <= 5
            && _initials.TryGetValue(input, out var initWords))
            foreach (var w in initWords)
            {
                if (!result.Contains(w)) result.Add(w);
                if (result.Count >= max) break;
            }

        // 4) 音节切分组合兜底（词库未覆盖的组合）
        if (result.Count < max && input.Length > 1)
        {
            var segs = Segment(input);
            if (segs is { Count: >= 2 })
                foreach (var combo in BuildCombos(segs, max))
                    if (!result.Contains(combo)) result.Add(combo);
        }

        // 5) 单字音节前缀（zho → 中/周…），按音节字母序
        if (result.Count < max)
        {
            var prefixes = _chars.Keys
                .Where(k => k.StartsWith(input) && k != input)
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            for (var rank = 0; rank < 3 && result.Count < max; rank++)
                foreach (var py in prefixes)
                {
                    if (result.Count >= max) break;
                    var cs = _chars[py];
                    if (rank < cs.Length && !result.Contains(cs[rank].ToString()))
                        result.Add(cs[rank].ToString());
                }
        }
        return result.Take(max).ToList();
    }

    /// <summary>
    /// 配置词库：vocabularies 为启用的词库名列表（general/gov/medical/retail），
    /// customVocabulary 为自定义词（一行一词）。领域词与自定义词在候选中置顶；
    /// 未启用 general 时通用词库不参与候选（达到限定输入范围的效果）。
    /// </summary>
    public void Configure(List<string> vocabularies, string customVocabulary)
    {
        _generalEnabled = vocabularies.Contains("general");

        var words = new List<string>();
        var asm = typeof(PinyinEngine).Assembly;
        var names = asm.GetManifestResourceNames();
        foreach (var pack in vocabularies)
        {
            if (!PackFiles.TryGetValue(pack, out var file)) continue;
            try
            {
                using var reader = OpenResource(asm, names, file);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length > 0) words.Add(line);
                }
            }
            catch (Exception ex)
            {
                KioskBrowser.Shared.Logger.Warning($"vocab pack {pack} load failed: {ex.Message}");
            }
        }
        // 分隔符兼容：空格/换行/逗号/顿号/分号（一条粘贴一串也能用）
        foreach (var w in System.Text.RegularExpressions.Regex.Split(
                     customVocabulary ?? "", @"[\s,，、;；]+"))
        {
            var word = w.Trim();
            if (word.Length > 0) words.Add(word);
        }

        var boost = new List<(string, string, string)>();
        foreach (var w in words.Distinct())
        {
            var key = KeyOf(w);
            if (key == null) continue; // 含无法注音的字则跳过
            var inits = InitialsOf(key);
            boost.Add((w, key, inits));
        }
        _boost = boost;
        KioskBrowser.Shared.Logger.Info(
            $"vocab configured: general={_generalEnabled}, boost words={_boost.Count}");
    }

    /// <summary>词 → 连拼拼音键：整词命中用词级读音（多音字正确）；
    /// 未命中时按最长子词拆分拼接（伺服电机 → 伺服+电机），兜底逐字主读音。</summary>
    private string? KeyOf(string word)
    {
        if (_wordToKey.TryGetValue(word, out var k)) return k;
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < word.Length)
        {
            var matched = false;
            for (var len = Math.Min(4, word.Length - i); len >= 2 && !matched; len--)
            {
                var sub = word.Substring(i, len);
                if (_wordToKey.TryGetValue(sub, out var subKey))
                {
                    sb.Append(subKey);
                    i += len;
                    matched = true;
                }
            }
            if (matched) continue;
            var c = word[i];
            if (c < 128) { sb.Append(char.ToLower(c)); i++; continue; }
            if (!_charPrimary.TryGetValue(c, out var py)) return null;
            sb.Append(py);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>连拼键 → 首字母串（按音节切分取首字母）。</summary>
    private string InitialsOf(string key)
    {
        var segs = Segment(key);
        if (segs != null) return string.Concat(segs.Select(s => s[0]));
        return key; // 切不出来就整串当首字母（极端情况）
    }
    /// <summary>贪心最长匹配切分；无法完整切分返回 null。</summary>
    private List<string>? Segment(string input)
    {
        var result = new List<string>();
        var i = 0;
        while (i < input.Length)
        {
            var matched = false;
            foreach (var syl in _syllablesByLenDesc)
            {
                if (syl.Length <= input.Length - i
                    && string.CompareOrdinal(input, i, syl, 0, syl.Length) == 0)
                {
                    result.Add(syl);
                    i += syl.Length;
                    matched = true;
                    break;
                }
            }
            if (!matched) return null;
        }
        return result;
    }

    /// <summary>各音节取前 3 字做笛卡尔组合，按字频序号和升序。</summary>
    private IEnumerable<string> BuildCombos(List<string> segs, int max)
    {
        var perSyl = segs.Select(s => _chars[s].Take(3).ToList()).ToList();
        var combos = new List<(string text, int rank)>();
        var indices = new int[perSyl.Count];
        while (true)
        {
            var sb = new System.Text.StringBuilder();
            var rank = 0;
            for (var i = 0; i < perSyl.Count; i++)
            {
                sb.Append(perSyl[i][indices[i]]);
                rank += indices[i];
            }
            combos.Add((sb.ToString(), rank));

            var pos = perSyl.Count - 1;
            while (pos >= 0 && ++indices[pos] >= perSyl[pos].Count)
            {
                indices[pos] = 0;
                pos--;
            }
            if (pos < 0) break;
        }
        return combos.OrderBy(c => c.rank).Take(max).Select(c => c.text);
    }
}
