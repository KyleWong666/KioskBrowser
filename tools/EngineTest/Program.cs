// 拼音引擎输出验证工具: dotnet run --project tools/EngineTest
using KioskBrowser;

static string Esc(string s) => string.Concat(s.Select(c =>
    c > 127 ? $"\\u{(int)c:x4}" : c.ToString()));

var engine = PinyinEngine.Instance;
foreach (var input in new[] { "nihao", "zhongguo", "n", "lv", "beijing", "niha", "nh", "zg", "bj", "xx" })
{
    var cands = engine.GetCandidates(input, 12);
    Console.WriteLine($"{input,-10} => {string.Join(" ", cands.Select(Esc))}");
}

// 混合分隔符自定义词库导入场景（对齐设置面板导入逻辑）
var importPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "test", "vocab-sample.txt"));
var importText = File.ReadAllText(importPath);
Console.WriteLine($"import: {importPath} len={importText.Length}");

engine.Configure(new List<string> { "gov", "medical" }, importText);
Console.WriteLine($"--- boost({engine.BoostWords.Count}), sample words in boost: ---");
foreach (var w in engine.BoostWords.Where(w =>
    w.Contains("医保") || w.Contains("跨省") || w.Contains("互联网") || w.Contains("DRG") || w.Contains("发热")))
    Console.WriteLine($"  {Esc(w)}");
foreach (var input in new[] { "yibao", "ybdzpz", "kuasheng", "hlwyy", "drgfufei", "df", "menzhen", "nihao" })
{
    var cands = engine.GetCandidates(input, 8);
    Console.WriteLine($"{input,-14} => {string.Join(" ", cands.Select(Esc))}");
}
