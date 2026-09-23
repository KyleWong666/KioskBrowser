using KioskBrowser.Shared;

namespace KioskBrowser.Voice;

/// <summary>
/// 本地语音识别模型管理：按档位下载/校验模型文件（hf-mirror 国内镜像）。
/// </summary>
public static class ModelManager
{
    public class ModelInfo
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Repo { get; set; } = "";
        public string[] Files { get; set; } = Array.Empty<string>();
        public bool Streaming { get; set; }

        /// <summary>是否支持热词（sherpa-onnx 仅流式 transducer 模型支持，paraformer 不支持）</summary>
        public bool SupportsHotwords { get; set; }

        // 流式模型的三个文件名（不同年代模型命名不同）
        public string EncoderFile { get; set; } = "encoder-epoch-99-avg-1.int8.onnx";
        public string DecoderFile { get; set; } = "decoder-epoch-99-avg-1.int8.onnx";
        public string JoinerFile { get; set; } = "joiner-epoch-99-avg-1.int8.onnx";
    }

    public static readonly ModelInfo[] Models =
    {
        new()
        {
            Key = "zh2025", DisplayName = "中文2025 (~165MB, 流式, 支持热词, 推荐)",
            Repo = "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30",
            Files = new[]
            {
                "encoder.int8.onnx",
                "decoder.onnx",
                "joiner.int8.onnx",
                "tokens.txt"
            },
            Streaming = true,
            SupportsHotwords = true,
            EncoderFile = "encoder.int8.onnx",
            DecoderFile = "decoder.onnx",
            JoinerFile = "joiner.int8.onnx"
        },
        new()
        {
            Key = "large", DisplayName = "大模型 paraformer (~230MB, 高精度, 松开出结果, 不支持热词)",
            Repo = "sherpa-onnx-paraformer-zh-2023-03-28",
            Files = new[] { "model.int8.onnx", "tokens.txt" },
            Streaming = false,
            SupportsHotwords = false
        },
    };

    public static ModelInfo GetModel(string key) =>
        Models.FirstOrDefault(m => m.Key == key) ?? Models[0]; // 默认中文2025

    public static string ModelDir(string key) =>
        Path.Combine(AppPaths.Root, "models", GetModel(key).Key);

    /// <summary>模型文件是否齐全。</summary>
    public static bool IsReady(string key)
    {
        var dir = ModelDir(key);
        var model = GetModel(key);
        return model.Files.All(f =>
        {
            var p = Path.Combine(dir, f);
            return File.Exists(p) && new FileInfo(p).Length > 0;
        });
    }

    /// <summary>下载缺失的模型文件。progress 回调 (文件名, 已下载MB, 总MB 或 -1 未知)。</summary>
    public static async Task<bool> EnsureAsync(string key, Action<string, double, double>? progress = null,
        CancellationToken ct = default)
    {
        var model = GetModel(key);
        var dir = ModelDir(key);
        Directory.CreateDirectory(dir);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        foreach (var file in model.Files)
        {
            var path = Path.Combine(dir, file);
            if (File.Exists(path) && new FileInfo(path).Length > 0) continue;

            var url = $"https://hf-mirror.com/csukuangfj/{model.Repo}/resolve/main/{file}";
            var tmp = path + ".download";
            Logger.Info($"downloading model file: {file}");
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1;
                long downloaded;
                await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                {
                    await using var dst = File.Create(tmp);
                    var buf = new byte[256 * 1024];
                    downloaded = 0;
                    int read;
                    while ((read = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, read), ct);
                        downloaded += read;
                        progress?.Invoke(file, downloaded / 1048576.0,
                            total > 0 ? total / 1048576.0 : -1);
                    }
                } // 先关闭流再移动
                File.Move(tmp, path, true);
                Logger.Info($"model file ready: {file} ({downloaded / 1048576}MB)");
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                Logger.Error($"model download failed: {file}", ex);
                return false;
            }
        }
        return true;
    }
}
