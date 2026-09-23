using KioskBrowser.Shared;

namespace KioskBrowser.Voice;

/// <summary>
/// 本地热词文件：把启用的领域词库（政务/医疗/商业）+ 自定义词库写成 sherpa-onnx 热词文件，
/// 识别时通过 HotwordsFile + HotwordsScore 提升领域词命中率。
/// </summary>
public static class HotwordManager
{
    private static string? _path;
    public static string? HotwordsPath => _path;

    /// <summary>写热词文件，返回路径；无词返回 null。</summary>
    public static string? WriteHotwordsFile(IReadOnlyList<string> words)
    {
        var distinct = words.Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().ToList();
        if (distinct.Count == 0)
        {
            _path = null;
            return null;
        }
        var path = Path.Combine(AppPaths.Root, "hotwords.txt");
        File.WriteAllLines(path, distinct);
        _path = path;
        Logger.Info($"hotwords file written: {distinct.Count} words");
        return path;
    }
}
