// 模型资源占用基准: 各档位加载内存 + 解码 RTF
using System.Diagnostics;
using KioskBrowser.Voice;
using NAudio.Wave;
using SherpaOnnx;

var wavPath = args.Length > 0 ? args[0] : "tools/tts_test.wav";

// 读音频
var samples = new List<float>();
using (var reader = new WaveFileReader(wavPath))
{
    var provider = reader.ToSampleProvider();
    var buf = new float[reader.WaveFormat.SampleRate];
    int n;
    while ((n = provider.Read(buf, 0, buf.Length)) > 0)
        for (var i = 0; i < n; i++) samples.Add(buf[i]);
}
var audio = samples.ToArray();
var audioSec = audio.Length / 16000.0;
Console.WriteLine($"音频: {audioSec:F1}s\n");

foreach (var size in new[] { "small", "medium", "large" })
{
    var model = ModelManager.GetModel(size);
    if (!ModelManager.IsReady(size))
    {
        Console.WriteLine($"[{size}] 未下载，跳过");
        continue;
    }
    GC.Collect();
    var memBefore = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0;

    var sw = Stopwatch.StartNew();
    if (model.Streaming)
    {
        var c = new OnlineRecognizerConfig();
        c.FeatConfig.SampleRate = 16000;
        c.FeatConfig.FeatureDim = 80;
        var dir = ModelManager.ModelDir(size);
        c.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder-epoch-99-avg-1.int8.onnx");
        c.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder-epoch-99-avg-1.int8.onnx");
        c.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner-epoch-99-avg-1.int8.onnx");
        c.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        c.ModelConfig.NumThreads = 2;
        c.ModelConfig.Provider = "cpu";
        using var rec = new OnlineRecognizer(c);
        var loadSec = sw.Elapsed.TotalSeconds;
        var memAfter = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0;

        // 解码 3 次取平均
        var times = new List<double>();
        string text = "";
        for (var i = 0; i < 3; i++)
        {
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(16000, audio);
            stream.InputFinished();
            var dw = Stopwatch.StartNew();
            while (rec.IsReady(stream)) rec.Decode(stream);
            dw.Stop();
            times.Add(dw.Elapsed.TotalSeconds);
            text = rec.GetResult(stream).Text;
        }
        var avg = times.Average();
        Console.WriteLine($"[{size}] 加载 {loadSec:F1}s, 内存 +{memAfter - memBefore:F0}MB, " +
                          $"解码 {avg:F2}s / 音频 {audioSec:F1}s → RTF {avg / audioSec:F2} " +
                          $"(解码速度 {audioSec / avg:F1}x 实时)");
    }
    else
    {
        var c = new OfflineRecognizerConfig();
        c.FeatConfig.SampleRate = 16000;
        c.FeatConfig.FeatureDim = 80;
        var dir = ModelManager.ModelDir(size);
        c.ModelConfig.Paraformer.Model = Path.Combine(dir, "model.int8.onnx");
        c.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        c.ModelConfig.NumThreads = 2;
        c.ModelConfig.Provider = "cpu";
        using var rec = new OfflineRecognizer(c);
        var loadSec = sw.Elapsed.TotalSeconds;
        var memAfter = Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0;

        var times = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(16000, audio);
            var dw = Stopwatch.StartNew();
            rec.Decode(stream);
            dw.Stop();
            times.Add(dw.Elapsed.TotalSeconds);
        }
        var avg = times.Average();
        Console.WriteLine($"[{size}] 加载 {loadSec:F1}s, 内存 +{memAfter - memBefore:F0}MB, " +
                          $"解码 {avg:F2}s / 音频 {audioSec:F1}s → RTF {avg / audioSec:F2} " +
                          $"(解码速度 {audioSec / avg:F1}x 实时)");
    }
}
