package com.ssdc.kiosk;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.admin.DevicePolicyManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.PowerManager;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import com.ssdc.kiosk.admin.KioskAdminReceiver;

/**
 * Kiosk 主界面：沉浸全屏 WebView + Device Owner LockTask + 左上角连击设置入口。
 */
public class MainActivity extends AppCompatActivity {

    private WebView web;
    private KioskConfig cfg;

    // 左上角连击状态机
    private int tapCounter = 0;
    private long firstTapAt = 0, lastTapAt = 0;

    @SuppressLint({"SetJavaScriptEnabled", "ClickableViewAccessibility"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setContentView(R.layout.activity_main);

        cfg = KioskConfig.load();
        ensureStoragePermission();
        applyImmersive(cfg.kioskMode);
        applyDeviceOwnerLock(cfg.kioskMode);

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

        web.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, String url) {
                if (isAllowed(url)) return false; // 继续在当前 WebView 加载
                Log.w(KioskApp.TAG, "navigation blocked by whitelist: " + url);
                return true;
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                Log.i(KioskApp.TAG, "page finished: " + url);
                // TODO Phase3: AutoLogin 注入（移植 ids WebAutoLogin 通用模板）
            }
        });
        web.setWebChromeClient(new WebChromeClient());

        // 触摸事件先过连击检测，再交给 WebView
        web.setOnTouchListener((v, ev) -> {
            if (cfg.zoneTapEnabled) trackZoneTap(ev);
            return false;
        });

        if (!"about:blank".equals(cfg.homeUrl)) web.loadUrl(cfg.homeUrl);
        Log.i(KioskApp.TAG, "initialized, kioskMode=" + cfg.kioskMode);
    }

    // ---------------- 霸屏 ----------------

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
        // 霸屏禁用返回
    }

    @Override
    protected void onResume() {
        super.onResume();
        applyImmersive(cfg != null && cfg.kioskMode);
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        if (pm != null && pm.isIgnoringBatteryOptimizations(getPackageName()) == false) {
            Log.w(KioskApp.TAG, "建议加白电池优化以保证长期运行");
        }
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

    private void openSettings() {
        // TODO Phase2: 设置面板（改 URL / 键盘 / 词库 / 退出 Kiosk）
        Toast.makeText(this, "设置面板（Phase2 待实现）", Toast.LENGTH_SHORT).show();
        Log.i(KioskApp.TAG, "settings entry triggered");
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
