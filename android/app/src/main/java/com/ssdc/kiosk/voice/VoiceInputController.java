package com.ssdc.kiosk.voice;

import android.annotation.SuppressLint;
import android.media.AudioFormat;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.util.Log;

import com.k2fsa.sherpa.onnx.EndpointConfig;
import com.k2fsa.sherpa.onnx.FeatureConfig;
import com.k2fsa.sherpa.onnx.HomophoneReplacerConfig;
import com.k2fsa.sherpa.onnx.OnlineCtcFstDecoderConfig;
import com.k2fsa.sherpa.onnx.OnlineLMConfig;
import com.k2fsa.sherpa.onnx.OnlineModelConfig;
import com.k2fsa.sherpa.onnx.OnlineRecognizer;
import com.k2fsa.sherpa.onnx.OnlineRecognizerConfig;
import com.k2fsa.sherpa.onnx.OnlineStream;
import com.k2fsa.sherpa.onnx.OnlineTransducerModelConfig;
import com.ssdc.kiosk.KioskApp;

import java.io.File;

/**
 * 语音输入控制器（sherpa-onnx 流式识别 + AudioRecord 16kHz 单声道）：
 * 按住说话（startListening/stopListening），松开出最终文本。
 * 热词走 hotwordsFile + modified_beam_search（与 Windows 版一致）。
 */
public class VoiceInputController {

    public interface Listener {
        void onStatus(String status);   // "听写中: xxx" / "初始化中…" / 错误提示
        void onResult(String text);     // 最终识别文本（空串=未识别到）
    }

    private final String modelDir;
    private final String hotwordsFile;
    private final float hotwordsScore;
    private final Listener listener;

    private OnlineRecognizer recognizer;
    private OnlineStream stream;
    private AudioRecord recorder;
    private Thread recordThread;
    private volatile boolean recording = false;
    private volatile boolean ready = false;

    public VoiceInputController(String modelDir, String hotwordsFile, float hotwordsScore, Listener l) {
        this.modelDir = modelDir;
        this.hotwordsFile = hotwordsFile;
        this.hotwordsScore = hotwordsScore;
        this.listener = l;
    }

    /** 后台线程加载模型（~160MB，秒级）。 */
    public void initAsync() {
        new Thread(() -> {
            try {
                listener.onStatus("语音模型加载中…");
                OnlineTransducerModelConfig transducer = new OnlineTransducerModelConfig();
                transducer.setEncoder(modelDir + "/encoder.int8.onnx");
                transducer.setDecoder(modelDir + "/decoder.onnx");
                transducer.setJoiner(modelDir + "/joiner.int8.onnx");

                OnlineModelConfig model = new OnlineModelConfig();
                model.setTransducer(transducer);
                model.setTokens(modelDir + "/tokens.txt");
                model.setNumThreads(2);
                model.setDebug(false);
                model.setProvider("cpu");

                boolean hasHotwords = new File(hotwordsFile).exists()
                        && new File(hotwordsFile).length() > 0;
                recognizer = new OnlineRecognizer(null, new OnlineRecognizerConfig(
                        new FeatureConfig(), model,
                        new OnlineLMConfig(), new OnlineCtcFstDecoderConfig(),
                        new HomophoneReplacerConfig(), new EndpointConfig(),
                        true,
                        hasHotwords ? "modified_beam_search" : "greedy_search",
                        4,
                        hasHotwords ? hotwordsFile : "",
                        hotwordsScore,
                        "", "", 0.0f));
                stream = recognizer.createStream("");
                ready = true;
                listener.onStatus("语音就绪（按住 🎤 说话）");
                Log.i(KioskApp.TAG, "voice recognizer ready, hotwords=" + hasHotwords);
            } catch (Throwable e) {
                Log.e(KioskApp.TAG, "voice init failed: " + e.getMessage());
                listener.onStatus("语音初始化失败");
            }
        }).start();
    }

    @SuppressLint("MissingPermission")
    public void startListening() {
        if (!ready || recording) return;
        int sampleRate = 16000;
        int bufSize = AudioRecord.getMinBufferSize(sampleRate,
                AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        try {
            recorder = new AudioRecord(MediaRecorder.AudioSource.MIC, sampleRate,
                    AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, bufSize * 2);
            if (recorder.getState() != AudioRecord.STATE_INITIALIZED) {
                listener.onStatus("麦克风不可用（检查 RECORD_AUDIO 权限）");
                return;
            }
        } catch (Throwable e) {
            listener.onStatus("麦克风打开失败");
            return;
        }
        recognizer.reset(stream);
        recording = true;
        recorder.startRecording();
        listener.onStatus("听写中…");
        recordThread = new Thread(() -> {
            short[] buf = new short[1600]; // 100ms
            float[] fbuf = new float[buf.length];
            while (recording) {
                int n;
                try {
                    n = recorder.read(buf, 0, buf.length);
                } catch (Throwable e) {
                    Log.e(KioskApp.TAG, "audio read failed: " + e.getMessage());
                    break;
                }
                if (n <= 0) continue;
                for (int i = 0; i < n; i++) fbuf[i] = buf[i] / 32768.0f;
                try {
                    stream.acceptWaveform(fbuf, 16000);
                    while (recognizer.isReady(stream)) recognizer.decode(stream);
                    String partial = recognizer.getResult(stream).getText();
                    if (partial != null && !partial.isEmpty())
                        listener.onStatus("听写中: " + partial);
                } catch (Throwable e) {
                    // getResult 空音频 NRE 防护（Windows 版踩过：会杀采集线程）
                    Log.w(KioskApp.TAG, "decode skipped: " + e.getMessage());
                }
            }
        });
        recordThread.start();
    }

    public void stopListening() {
        if (!recording) return;
        recording = false;
        try {
            recorder.stop();
            recorder.release();
        } catch (Throwable ignored) {}
        try { recordThread.join(500); } catch (Throwable ignored) {}
        String text = "";
        try {
            stream.inputFinished();
            while (recognizer.isReady(stream)) recognizer.decode(stream);
            text = recognizer.getResult(stream).getText();
        } catch (Throwable e) {
            Log.w(KioskApp.TAG, "final decode failed: " + e.getMessage());
        }
        if (text == null) text = "";
        listener.onResult(text.trim());
    }

    public boolean isReady() { return ready; }
}
