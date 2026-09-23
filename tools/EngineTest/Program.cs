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

// 词库配置测试: 政务+医疗+自定义(停用通用词库)
engine.Configure(new List<string> { "gov", "medical" }, "星创大厅\n一窗通办窗口");
Console.WriteLine("--- boost: gov+industrial+custom, general OFF ---");
foreach (var input in new[] { "shenfenzheng", "sfz", "bianpinqi", "bpq", "sifu", "zhouchuang", "nihao", "ni" })
{
    var cands = engine.GetCandidates(input, 8);
    Console.WriteLine($"{input,-14} => {string.Join(" ", cands.Select(Esc))}");
}
