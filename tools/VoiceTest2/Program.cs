// 流式模型诊断: 录 5 秒, 用 medium 模型测 3 种配置 (无热词 / 热词普通 / 热词逐字空格)
using KioskBrowser;
using KioskBrowser.Voice;
using NAudio.Wave;
using SherpaOnnx;

internal static class Program
{
    [STAThread]
    static async Task Main()
    {
        Console.WriteLine(">>> 请说话（录音 5 秒）...");
        var buf = new List<float>();
        using var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        waveIn.DataAvailable += (s, e) =>
        {
            for (var i = 0; i < e.BytesRecorded / 2; i++)
                buf.Add(BitConverter.ToInt16(e.Buffer, i * 2) / 32768f);
        };
        waveIn.StartRecording();
        await Task.Delay(5000);
        waveIn.StopRecording();
        var maxAmp = buf.Count > 0 ? buf.Max(Math.Abs) : 0f;
        Console.WriteLine($"录音 {buf.Count / 16000.0:F1}s 峰值 {maxAmp:F4}");
        if (buf.Count == 0) return;
        LastBuf = buf.ToArray();

        // 保存录音供 WavTest 复验
        var wavOut = Path.Combine(Path.GetTempPath(), "mic_test.wav");
        using (var writer = new WaveFileWriter(wavOut, new WaveFormat(16000, 16, 1)))
        {
            var shorts = LastBuf.Select(f => (short)Math.Clamp(f * 32767f, -32768, 32767)).ToArray();
            var bytes = new byte[shorts.Length * 2];
            Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
            writer.Write(bytes, 0, bytes.Length);
        }
        Console.WriteLine("录音已保存: " + wavOut);

        // 词库
        PinyinEngine.Instance.Configure(new List<string> { "general", "gov" }, "星创大厅\n一窗通办窗口");
        var words = PinyinEngine.Instance.BoostWords;
        var plainFile = Path.Combine(Path.GetTempPath(), "hw_plain.txt");
        var spacedFile = Path.Combine(Path.GetTempPath(), "hw_spaced.txt");
        File.WriteAllLines(plainFile, words);
        File.WriteAllLines(spacedFile, words.Select(w => string.Join(" ", w.ToCharArray())));

        TestStreaming("无热词", null);
        TestStreaming("无热词+无端点检测", null, noEndpoint: true);
        TestStreaming("热词(普通)", plainFile);
        TestStreaming("热词(逐字空格)", spacedFile);
    }

    static void TestStreaming(string label, string? hotwordsFile, bool noEndpoint = false)
    {
        Console.WriteLine($"\n=== {label} ===");
        try
        {
            var dir = ModelManager.ModelDir("medium");
            var c = new OnlineRecognizerConfig();
            c.FeatConfig.SampleRate = 16000;
            c.FeatConfig.FeatureDim = 80;
            c.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder-epoch-99-avg-1.int8.onnx");
            c.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder-epoch-99-avg-1.int8.onnx");
            c.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner-epoch-99-avg-1.int8.onnx");
            c.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
            c.ModelConfig.NumThreads = 2;
            c.ModelConfig.Provider = "cpu";
            if (!noEndpoint)
            {
                c.EnableEndpoint = 1;
                c.Rule1MinTrailingSilence = 1.2f;
                c.Rule2MinTrailingSilence = 2.4f;
                c.Rule3MinUtteranceLength = 20f;
            }
            if (hotwordsFile != null)
            {
                c.HotwordsFile = hotwordsFile;
                c.HotwordsScore = 1.5f;
                c.DecodingMethod = "modified_beam_search";
            }
            using var rec = new OnlineRecognizer(c);
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(16000, LastBuf!);
            stream.InputFinished();
            while (rec.IsReady(stream)) rec.Decode(stream);
            Console.WriteLine("结果: [" + rec.GetResult(stream).Text + "]");
        }
        catch (Exception ex)
        {
            Console.WriteLine("异常: " + ex.Message);
        }
    }

    static float[]? LastBuf;
}
