using KioskBrowser;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var asm = typeof(PinyinEngine).Assembly;
Console.WriteLine("resources: " + string.Join(", ", asm.GetManifestResourceNames().Where(n => n.Contains("vocab"))));

var engine = PinyinEngine.Instance;
engine.Configure(new List<string> { "industrial" }, "");
Console.WriteLine("boost count: " + engine.BoostWords.Count);
Console.WriteLine("has bianpin word: " + engine.BoostWords.Any(w => w.Contains("变频器")));
foreach (var input in new[] { "bianpinqi", "bpq", "cifu", "sifu", "chechuang", "zhoucheng" })
    Console.WriteLine($"{input} => " + string.Join(" ", engine.GetCandidates(input, 5)));
