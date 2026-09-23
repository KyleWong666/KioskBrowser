package com.ssdc.kiosk.web;

import android.webkit.JavascriptInterface;
import android.util.Log;

import com.ssdc.kiosk.KioskApp;

import org.json.JSONObject;

/**
 * 页面 → 宿主桥：window.KioskBridge.post(jsonString)。
 * JS 注入件见 JsInject（Windows 版 InjectedScripts.cs 的移植，postMessage 换成 KioskBridge）。
 */
public class KioskBridge {

    public interface Listener {
        void onFocus();          // 可输入元素获得焦点 → 弹键盘
        void onBlur();           // 失焦 → 收键盘
        void onZoneTap(float x, float y); // 页面内左上角区域点击（Android 侧已有原生检测，备用）
        void onLoginResult(String result); // 模拟登录结果：success|failed|notfound
    }

    private final Listener listener;

    public KioskBridge(Listener l) { listener = l; }

    @JavascriptInterface
    public void post(String json) {
        try {
            JSONObject o = new JSONObject(json);
            String type = o.optString("type");
            switch (type) {
                case "focus": listener.onFocus(); break;
                case "blur": listener.onBlur(); break;
                case "zonetap":
                    listener.onZoneTap((float) o.optDouble("x"), (float) o.optDouble("y"));
                    break;
                case "loginResult":
                    listener.onLoginResult(o.optString("result", "unknown"));
                    break;
            }
        } catch (Exception e) {
            Log.w(KioskApp.TAG, "bridge message ignored: " + json);
        }
    }
}
