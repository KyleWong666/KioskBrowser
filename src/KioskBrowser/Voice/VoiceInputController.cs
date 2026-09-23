using KioskBrowser.Shared;
using NAudio.Wave;
using SherpaOnnx;

namespace KioskBrowser.Voice;

/// <summary>
/// 本地语音输入控制器（sherpa-onnx 离线引擎）：
/// 按住说话 → 16kHz PCM 采集 → 流式模型实时出中间结果 / 非流式模型松开一次性解码。
/// 支持热词文件（领域词库 + 自定义词库）。
/// </summary>
public sealed class VoiceInputController : IDisposable
{
    private readonly bool _streaming;
    private readonly OnlineRecognizer? _online;
    private readonly OfflineRecognizer? _offline;

    /// <summary>中间状态/中间识别结果（显示在候选栏）。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>最终识别文本（上屏）。</summary>
    public event Action<string>? FinalResult;

    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private OnlineStream? _stream;
    private List<float>? _offlineBuf;
    private string _lastText = "";
    private DateTime _lastInterim = DateTime.MinValue;

    public bool IsActive
    {
        get { lock (_sync) return _waveIn != null; }
    }

    /// <summary>构造即加载模型（耗时数秒，请在后台线程创建）。模型缺失时抛异常。</summary>
    public VoiceInputController(SpeechConfig cfg)
    {
        var model = ModelManager.GetModel(cfg.ModelSize);
        var dir = ModelManager.ModelDir(cfg.ModelSize);
        _streaming = model.Streaming;
        // paraformer 不支持热词（sherpa-onnx 限制），仅流式模型启用
        var hotwords = model.SupportsHotwords ? HotwordManager.HotwordsPath : null;

        if (_streaming)
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
            c.ModelConfig.Debug = 0;
            c.EnableEndpoint = 1;
            c.Rule1MinTrailingSilence = 1.2f;
            c.Rule2MinTrailingSilence = 2.4f;
            c.Rule3MinUtteranceLength = 20f;
            if (hotwords != null)
            {
                c.HotwordsFile = hotwords;
                c.HotwordsScore = cfg.HotwordsScore;
                c.DecodingMethod = "modified_beam_search"; // 热词需要（1.13.x 校验要求）
            }
            _online = new OnlineRecognizer(c);
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
            c.ModelConfig.Debug = 0;
            if (hotwords != null)
            {
                c.HotwordsFile = hotwords;
                c.HotwordsScore = cfg.HotwordsScore;
                // sherpa-onnx 硬性要求：离线模型用热词必须 modified_beam_search
                c.DecodingMethod = "modified_beam_search";
            }
            _offline = new OfflineRecognizer(c);
        }
        Logger.Info($"voice model loaded: {cfg.ModelSize} (streaming={_streaming}, hotwords={(hotwords != null)})");
    }

    /// <summary>按下：开始录音。</summary>
    public void Begin()
    {
        lock (_sync)
        {
            if (_waveIn != null) return;
            _lastText = "";
            _lastInterim = DateTime.MinValue;
            if (_streaming)
                _stream = _online!.CreateStream();
            else
                _offlineBuf = new List<float>();

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += OnAudio;
            _waveIn.StartRecording();
        }
        StatusChanged?.Invoke("正在聆听，请说话…");
    }

    /// <summary>松开：结束录音，出最终结果。</summary>
    public async void End()
    {
        StatusChanged?.Invoke("识别中…");
        var text = await Task.Run(() =>
        {
            lock (_sync)
            {
                try { _waveIn?.StopRecording(); _waveIn?.Dispose(); } catch { }
                _waveIn = null;

                if (_streaming)
                {
                    if (_stream == null) return "";
                    try
                    {
                        _stream.InputFinished();
                        while (_online!.IsReady(_stream)) _online.Decode(_stream);
                        // 空音频等场景底层可能返回空结果，退回最后的中间结果
                        string t;
                        try { t = _online.GetResult(_stream).Text ?? ""; }
                        catch { t = _lastText; }
                        return t.Length > 0 ? t : _lastText;
                    }
                    finally
                    {
                        try { _stream.Dispose(); } catch { }
                        _stream = null;
                    }
                }
                else
                {
                    if (_offlineBuf == null || _offlineBuf.Count < 1600) return ""; // 不足 0.1s
                    var st = _offline!.CreateStream();
                    try
                    {
                        st.AcceptWaveform(16000, _offlineBuf.ToArray());
                        _offline.Decode(st);
                        // 极短/无声音频底层可能返回空结果
                        try { return st.Result.Text ?? ""; }
                        catch { return ""; }
                    }
                    finally
                    {
                        st.Dispose();
                        _offlineBuf = null;
                    }
                }
            }
        });
        FinalResult?.Invoke(text.Trim());
    }

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        ApplyAgc(samples);

        lock (_sync)
        {
            if (_streaming)
            {
                if (_stream == null) return;
                try
                {
                    _stream.AcceptWaveform(16000, samples);
                    while (_online!.IsReady(_stream)) _online.Decode(_stream);

                    // 中间结果节流 300ms 上报一次
                    if ((DateTime.Now - _lastInterim).TotalMilliseconds >= 300)
                    {
                        _lastInterim = DateTime.Now;
                        // 开头静音阶段底层结果句柄可能为空，NRE 会杀死采集线程，必须兜住
                        string text;
                        try { text = _online.GetResult(_stream).Text ?? ""; }
                        catch { return; }
                        if (text.Length > 0 && text != _lastText)
                        {
                            _lastText = text;
                            StatusChanged?.Invoke(text);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"streaming feed failed: {ex.Message}");
                }
            }
            else
            {
                _offlineBuf?.AddRange(samples);
            }
        }
    }

    // ---------------- AGC：自适应增益，补偿静音麦克风 ----------------

    private float _gain = 1f;

    private void ApplyAgc(float[] samples)
    {
        float peak = 0;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        if (peak > 0.002f)
        {
            // 目标峰值 0.25，增益上限 15x，平滑收敛防爆音
            var desired = Math.Clamp(0.25f / peak, 1f, 15f);
            _gain = _gain * 0.7f + desired * 0.3f;
        }
        if (_gain <= 1.01f) return;
        for (var i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * _gain, -1f, 1f);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _waveIn?.StopRecording(); _waveIn?.Dispose(); } catch { }
            _waveIn = null;
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }
        try { _online?.Dispose(); } catch { }
        try { _offline?.Dispose(); } catch { }
    }
}
