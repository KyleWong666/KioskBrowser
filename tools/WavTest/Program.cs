// 用仓库自带测试音频验证流式模型解码（排除麦克风因素）
using KioskBrowser.Voice;
using NAudio.Wave;
using SherpaOnnx;

var wavPath = args.Length > 0 ? args[0] : @"..\..\..\test0.wav";
var size = args.Length > 1 ? args[1] : "medium";

// 读取 wav 为 float[]
var samples = new List<float>();
using (var reader = new WaveFileReader(wavPath))
{
    var provider = reader.ToSampleProvider();
    var buf = new float[reader.WaveFormat.SampleRate];
    int n;
    // 重采样到 16k mono（test wav 已是 16k mono）
    while ((n = provider.Read(buf, 0, buf.Length)) > 0)
        for (var i = 0; i < n; i++) samples.Add(buf[i]);
}
Console.WriteLine($"wav: {samples.Count / 16000.0:F1}s");
// AGC: 自适应增益到峰值 0.25
var peak = samples.Count > 0 ? samples.Max(Math.Abs) : 0f;
if (peak > 0.001f)
{
    var gain = Math.Min(0.25f / peak, 15f);
    for (var i = 0; i < samples.Count; i++)
        samples[i] = Math.Clamp(samples[i] * gain, -1f, 1f);
    Console.WriteLine($"AGC gain: {gain:F1}x");
}

var dir = ModelManager.ModelDir(size);
var model = ModelManager.GetModel(size);
Console.WriteLine($"model: {size}, streaming={model.Streaming}");

if (model.Streaming)
{
    var c = new OnlineRecognizerConfig();
    c.FeatConfig.SampleRate = 16000;
    c.FeatConfig.FeatureDim = 80;
    c.ModelConfig.Transducer.Encoder = Path.Combine(dir, model.EncoderFile);
    c.ModelConfig.Transducer.Decoder = Path.Combine(dir, model.DecoderFile);
    c.ModelConfig.Transducer.Joiner = Path.Combine(dir, model.JoinerFile);
    c.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
    c.ModelConfig.NumThreads = 2;
    c.ModelConfig.Provider = "cpu";
    var hwMode = args.Length > 2 ? args[2] : "none";
    if (hwMode != "none")
    {
        var words = new List<string> { "社保卡", "身份证", "挂失", "星创大厅" };
        var f = Path.Combine(Path.GetTempPath(), "hw_" + hwMode + ".txt");
        if (hwMode == "spaced")
            File.WriteAllLines(f, words.Select(w => string.Join(" ", w.ToCharArray())));
        else
            File.WriteAllLines(f, words);
        c.HotwordsFile = f;
        c.HotwordsScore = 1.5f;
        c.DecodingMethod = "modified_beam_search";
        Console.WriteLine("hotwords: " + hwMode);
    }
    using var rec = new OnlineRecognizer(c);
    using var stream = rec.CreateStream();
    stream.AcceptWaveform(16000, samples.ToArray());
    stream.InputFinished();
    while (rec.IsReady(stream)) rec.Decode(stream);
    Console.WriteLine("结果: [" + rec.GetResult(stream).Text + "]");
}
else
{
    var c = new OfflineRecognizerConfig();
    c.FeatConfig.SampleRate = 16000;
    c.FeatConfig.FeatureDim = 80;
    c.ModelConfig.Paraformer.Model = Path.Combine(dir, "model.int8.onnx");
    c.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
    c.ModelConfig.NumThreads = 2;
    c.ModelConfig.Provider = "cpu";
    using var rec = new OfflineRecognizer(c);
    using var stream = rec.CreateStream();
    stream.AcceptWaveform(16000, samples.ToArray());
    rec.Decode(stream);
    Console.WriteLine("结果: [" + stream.Result.Text + "]");
}
