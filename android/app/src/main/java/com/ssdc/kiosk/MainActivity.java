package com.ssdc.kiosk;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.admin.DevicePolicyManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import com.ssdc.kiosk.admin.KioskAdminReceiver;
import com.ssdc.kiosk.ime.KeyboardView;
import com.ssdc.kiosk.ime.PinyinEngine;
import com.ssdc.kiosk.settings.SettingsPanel;
import com.ssdc.kiosk.web.JsInject;
import com.ssdc.kiosk.web.KioskBridge;

/**
 * Kiosk 主界面：沉浸全屏 WebView + Device Owner LockTask + 左上角连击设置
 * + 自绘软键盘（拼音输入法）+ 设置面板。
 */
public class MainActivity extends AppCompatActivity {

    private WebView web;
    private KioskConfig cfg;
    private FrameLayout root, keyboardHost;
    private KeyboardView keyboard;
    private SettingsPanel settingsPanel;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Runnable hideKbRunnable = () -> setKeyboardVisible(false);

    // 左上角连击状态机
    private int tapCounter = 0;
    private long firstTapAt = 0, lastTapAt = 0;

    @SuppressLint({"SetJavaScriptEnabled", "ClickableViewAccessibility", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setContentView(R.layout.activity_main);

        cfg = KioskConfig.load();
        ensureStoragePermission();
        applyImmersive(cfg.kioskMode);
        applyDeviceOwnerLock(cfg.kioskMode);

        root = findViewById(R.id.root);
        keyboardHost = findViewById(R.id.keyboardHost);
        web = findViewById(R.id.web);

        WebSettings st = web.getSettings();
        st.setJavaScriptEnabled(true);
        st.setDomStorageEnabled(true);
        st.setBuiltInZoomControls(false);
        st.setDisplayZoomControls(false);
        st.setUseWideViewPort(true);
        st.setLoadWithOverviewMode(true);
        st.setMediaPlaybackRequiresUserGesture(false);
        if (cfg.devMode) WebView.setWebContentsDebuggingEnabled(true);
        suppressSystemIme();

        web.addJavascriptInterface(new KioskBridge(new KioskBridge.Listener() {
            @Override public void onFocus() {
                ui.post(() -> { ui.removeCallbacks(hideKbRunnable); setKeyboardVisible(true); });
            }
            @Override public void onBlur() {
                ui.post(() -> { ui.removeCallbacks(hideKbRunnable); ui.postDelayed(hideKbRunnable, 500); });
            }
            @Override public void onZoneTap(float x, float y) { /* 原生检测已覆盖，备用 */ }
        }), "KioskBridge");

        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, String url) {
                if (isAllowed(url)) return false;
                Log.w(KioskApp.TAG, "navigation blocked by whitelist: " + url);
                return true;
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                Log.i(KioskApp.TAG, "page finished: " + url);
                view.evaluateJavascript(JsInject.focusNotify(), null);
                // TODO Phase3: AutoLogin 注入
            }
        });
        web.setWebChromeClient(new WebChromeClient());

        web.setOnTouchListener((v, ev) -> {
            if (ev.getAction() == MotionEvent.ACTION_DOWN)
                Log.d(KioskApp.TAG, "web touch: " + ev.getX() + "," + ev.getY()
                        + " zoneEnabled=" + cfg.zoneTapEnabled);
            if (cfg.zoneTapEnabled && settingsPanel == null) trackZoneTap(ev);
            return false;
        });

        initKeyboard();
        // 调试通道：devMode 下可通过广播打开设置（adb 连击太慢时用）
        if (cfg.devMode) {
            registerReceiver(new android.content.BroadcastReceiver() {
                @Override public void onReceive(Context c, android.content.Intent i) {
                    ui.post(() -> openSettings());
                }
            }, new android.content.IntentFilter("com.ssdc.kiosk.OPEN_SETTINGS"));
        }
        // 拼音引擎加载 + 词库配置（后台线程，18.5 万词约 2s，不卡首屏）
        new Thread(() -> {
            PinyinEngine eng = PinyinEngine.get(this);
            eng.configure(this, cfg.vocabularies, cfg.customVocabulary);
            ui.post(() -> keyboard.setEngine(eng));
        }).start();

        if (!"about:blank".equals(cfg.homeUrl)) web.loadUrl(cfg.homeUrl);
        Log.i(KioskApp.TAG, "initialized, kioskMode=" + cfg.kioskMode
                + ", keyboard=" + cfg.keyboardEnabled + ", chinese=" + cfg.enableChinese);
    }

    // ---------------- 软键盘 ----------------

    private void initKeyboard() {
        keyboard = new KeyboardView(this, new KeyboardView.KeyListener() {
            @Override public void onCommit(String text) { js(JsInject.insertText(text)); }
            @Override public void onBackspace() { js(JsInject.BACKSPACE); }
            @Override public void onEnter() { js(JsInject.ENTER); }
            @Override public void onFocusNext() { js(JsInject.FOCUS_NEXT); }
            @Override public void onHide() { setKeyboardVisible(false); }
        });
        keyboard.setChineseAllowed(cfg.enableChinese);
        keyboardHost.addView(keyboard,
                new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void setKeyboardVisible(boolean visible) {
        if (!cfg.keyboardEnabled) visible = false;
        int h = visible ? (int) (root.getHeight() * (cfg.keyboardHeightPercent / 100.0f)) : 0;
        ViewGroup.LayoutParams lp = keyboardHost.getLayoutParams();
        if (lp.height != h) {
            lp.height = h;
            keyboardHost.setLayoutParams(lp);
        }
        if (!visible) keyboard.reset();
        Log.d(KioskApp.TAG, "keyboard " + (visible ? "shown" : "hidden"));
    }

    private void js(String script) {
        web.evaluateJavascript(script, null);
    }

    // ---------------- 设置面板 ----------------

    private void openSettings() {
        if (settingsPanel != null) return;
        setKeyboardVisible(false);
        Log.i(KioskApp.TAG, "settings opened");
        settingsPanel = new SettingsPanel(this, cfg, new SettingsPanel.Listener() {
            @Override public void onSave(KioskConfig c, boolean restart) {
                cfg = c;
                if (restart) ui.postDelayed(() -> restartApp(), 600);
            }
            @Override public void onExitKiosk() { exitKiosk(); }
            @Override public void onClose() { closeSettings(); }
        });
        root.addView(settingsPanel, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void closeSettings() {
        if (settingsPanel != null) {
            root.removeView(settingsPanel);
            settingsPanel = null;
        }
    }

    private void restartApp() {
        try {
            Intent i = getPackageManager().getLaunchIntentForPackage(getPackageName());
            if (i != null) {
                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
                startActivity(i);
            }
        } catch (Exception e) {
            Log.e(KioskApp.TAG, "restart failed: " + e.getMessage());
        }
        Runtime.getRuntime().exit(0);
    }

    private void exitKiosk() {
        Log.i(KioskApp.TAG, "admin exit requested");
        try { stopLockTask(); } catch (Exception ignored) {}
        finish();
        Runtime.getRuntime().exit(0);
    }

    // ---------------- 霸屏 ----------------

    /**
     * 压制系统输入法（原生层）：kiosk 必须用自绘键盘。
     * JS 层另有 inputmode=none 注入（见 JsInject.focusNotify），双保险。
     */
    private void suppressSystemIme() {
        getWindow().setSoftInputMode(
                WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN
                        | WindowManager.LayoutParams.SOFT_INPUT_ADJUST_NOTHING);
    }

    private void applyImmersive(boolean on) {
        if (!on) return;
        getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                        | View.SYSTEM_UI_FLAG_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
    }

    /** Device Owner 已授权时进入 LockTask（禁状态栏/多任务/Home/通知）。 */
    private void applyDeviceOwnerLock(boolean on) {
        if (!on) return;
        try {
            DevicePolicyManager dpm = (DevicePolicyManager) getSystemService(Context.DEVICE_POLICY_SERVICE);
            ComponentName admin = new ComponentName(this, KioskAdminReceiver.class);
            if (dpm != null && dpm.isDeviceOwnerApp(getPackageName())) {
                dpm.setLockTaskPackages(admin, new String[]{getPackageName()});
                startLockTask();
                Log.i(KioskApp.TAG, "LockTask enabled (device owner)");
            } else {
                Log.w(KioskApp.TAG, "not device owner; kiosk runs in immersive-only mode. " +
                        "授权: adb shell dpm set-device-owner com.ssdc.kiosk/.admin.KioskAdminReceiver");
            }
        } catch (Exception e) {
            Log.e(KioskApp.TAG, "lock task failed: " + e.getMessage());
        }
    }

    @Override
    public void onBackPressed() {
        if (settingsPanel != null) { closeSettings(); return; }
        // 霸屏禁用返回
    }

    @Override
    protected void onResume() {
        super.onResume();
        applyImmersive(cfg != null && cfg.kioskMode);
    }

    // ---------------- 导航白名单 ----------------

    private boolean isAllowed(String url) {
        if (cfg.navigationWhitelist.isEmpty()) return true;
        try {
            String host = android.net.Uri.parse(url).getHost();
            if (host == null) return false;
            for (String p : cfg.navigationWhitelist) {
                if (host.equalsIgnoreCase(p) || host.toLowerCase().endsWith("." + p.toLowerCase()))
                    return true;
            }
            return false;
        } catch (Exception e) {
            return false;
        }
    }

    // ---------------- 左上角连击 ----------------

    private void trackZoneTap(MotionEvent ev) {
        if (ev.getAction() != MotionEvent.ACTION_DOWN) return;
        long now = System.currentTimeMillis();
        float density = getResources().getDisplayMetrics().density;
        float zone = cfg.zoneSize * density;
        if (ev.getX() > zone || ev.getY() > zone) { tapCounter = 0; return; }
        if (tapCounter == 0) firstTapAt = now;
        else if (now - lastTapAt > cfg.maxGapMs || now - firstTapAt > cfg.timeWindowMs) {
            tapCounter = 0;
            firstTapAt = now;
        }
        lastTapAt = now;
        tapCounter++;
        if (tapCounter >= cfg.tapCount) {
            tapCounter = 0;
            openSettings();
        }
    }

    // ---------------- 存储权限 ----------------

    private void ensureStoragePermission() {
        if (Build.VERSION.SDK_INT >= 23 &&
                ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE)
                        != PackageManager.PERMISSION_GRANTED) {
            ActivityCompat.requestPermissions(this,
                    new String[]{Manifest.permission.WRITE_EXTERNAL_STORAGE,
                            Manifest.permission.READ_EXTERNAL_STORAGE}, 42);
        }
    }
}
