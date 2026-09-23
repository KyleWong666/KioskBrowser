// 语音链路诊断: 录音 5 秒 → 振幅统计 → sherpa-onnx paraformer 解码
using KioskBrowser;
using KioskBrowser.Voice;
using NAudio.Wave;
using SherpaOnnx;

internal static class Program
{
    [STAThread]
    static async Task Main()
    {
        Console.WriteLine("=== 语音链路诊断 ===");

        // 1) 录音 5 秒, 统计振幅
        Console.WriteLine(">>> 请对着麦克风说话（录音 5 秒）...");
        var buf = new List<float>();
        var maxAmp = 0f;
        using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        var done = new TaskCompletionSource();
        waveIn.DataAvailable += (s, e) =>
        {
            for (var i = 0; i < e.BytesRecorded / 2; i++)
            {
                var v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                buf.Add(v);
                if (Math.Abs(v) > maxAmp) maxAmp = Math.Abs(v);
            }
        };
        waveIn.StartRecording();
        await Task.Delay(5000);
        waveIn.StopRecording();
        var rms = Math.Sqrt(buf.Average(v => v * v));
        Console.WriteLine($"录音: {buf.Count / 16000.0:F1}s, 峰值振幅={maxAmp:F4}, RMS={rms:F4}");
        Console.WriteLine(maxAmp < 0.001f
            ? "!!! 振幅接近 0 → 麦克风没采到声音（检查默认录音设备/麦克风隐私权限）"
            : ">>> 麦克风采集正常");

        // 2) 模型解码
        var dir = ModelManager.ModelDir("large");
        Console.WriteLine(">>> 加载 paraformer 模型解码...");
        var cfg = new OfflineRecognizerConfig();
        cfg.FeatConfig.SampleRate = 16000;
        cfg.FeatConfig.FeatureDim = 80;
        cfg.ModelConfig.Paraformer.Model = Path.Combine(dir, "model.int8.onnx");
        cfg.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        cfg.ModelConfig.NumThreads = 2;
        cfg.ModelConfig.Provider = "cpu";
        // 热词（与主程序一致）
        PinyinEngine.Instance.Configure(new List<string> { "general", "gov" }, "星创大厅\n一窗通办窗口");
        var hw = HotwordManager.WriteHotwordsFile(PinyinEngine.Instance.BoostWords);
        // paraformer 不支持热词，这里不挂载（与主程序 large 档位一致）
        Console.WriteLine(">>> 热词文件已生成但 paraformer 不挂载: " + PinyinEngine.Instance.BoostWords.Count + " 词");

        using var rec = new OfflineRecognizer(cfg);
        var stream = rec.CreateStream();
        stream.AcceptWaveform(16000, buf.ToArray());
        rec.Decode(stream);
        string text;
        try { text = stream.Result.Text; } catch { text = "<null result>"; }
        Console.WriteLine("识别结果: [" + text + "]");
    }
}
