package com.ssdc.kiosk;

import android.os.Environment;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileWriter;
import java.util.ArrayList;
import java.util.List;

/**
 * 配置模型：/sdcard/kiosk/config.json（与 Windows 版同款运维模型，文件可直改）。
 * 首次启动自动写入默认配置。
 */
public class KioskConfig {
    public static final String DIR = Environment.getExternalStorageDirectory() + "/kiosk";
    public static final String PATH = DIR + "/config.json";

    public String homeUrl = "about:blank";
    public boolean kioskMode = true;       // 霸屏（沉浸+LockTask）；false=普通窗口调试
    public boolean devMode = false;        // 放行 WebView 调试
    public List<String> navigationWhitelist = new ArrayList<>();

    // 左上角连击设置入口
    public boolean zoneTapEnabled = true;
    public int zoneSize = 80;              // px
    public int tapCount = 8;
    public int timeWindowMs = 5000;
    public int maxGapMs = 800;

    // 软键盘 / 输入法
    public boolean keyboardEnabled = true;
    public String keyboardMode = "ime";    // ime=系统输入法(推荐) | inject=内置键盘JS注入(降级)
    public int keyboardHeightPercent = 35;
    public boolean enableChinese = false;
    public List<String> vocabularies = new ArrayList<>(java.util.Arrays.asList("general"));
    public String customVocabulary = "";

    // 模拟登录（小厂软件兜底；与 Windows 版同模型）
    public boolean autoLoginEnabled = false;
    public String usernameSelector = "#username";
    public String passwordSelector = "#password";
    public String submitSelector = "#login-btn";
    public int maxWaitSeconds = 10;
    public int pollIntervalMs = 500;
    public String autoLoginUser = "";      // TODO 二期: 凭据加密（对齐 Windows AES-256-GCM）
    public String autoLoginPass = "";

    public static KioskConfig load() {
        KioskConfig cfg = new KioskConfig();
        try {
            File f = new File(PATH);
            if (!f.exists()) {
                cfg.save();
                Log.i(KioskApp.TAG, "default config written: " + PATH);
                return cfg;
            }
            byte[] buf = new byte[(int) f.length()];
            FileInputStream in = new FileInputStream(f);
            int n = in.read(buf);
            in.close();
            JSONObject j = new JSONObject(new String(buf, 0, Math.max(n, 0)));

            JSONObject browser = j.optJSONObject("browser");
            if (browser != null) {
                cfg.homeUrl = browser.optString("homeUrl", cfg.homeUrl);
                cfg.kioskMode = browser.optBoolean("kioskMode", cfg.kioskMode);
                JSONArray wl = browser.optJSONArray("navigationWhitelist");
                if (wl != null)
                    for (int i = 0; i < wl.length(); i++)
                        cfg.navigationWhitelist.add(wl.optString(i));
            }
            cfg.devMode = j.optBoolean("devMode", false);
            JSONObject kb = j.optJSONObject("keyboard");
            if (kb != null) {
                cfg.keyboardEnabled = kb.optBoolean("enabled", cfg.keyboardEnabled);
                cfg.keyboardMode = kb.optString("mode", cfg.keyboardMode);
                cfg.keyboardHeightPercent = kb.optInt("heightPercent", cfg.keyboardHeightPercent);
                cfg.enableChinese = kb.optBoolean("enableChinese", cfg.enableChinese);
                cfg.customVocabulary = kb.optString("customVocabulary", cfg.customVocabulary);
                JSONArray vs = kb.optJSONArray("vocabularies");
                if (vs != null) {
                    cfg.vocabularies.clear();
                    for (int i = 0; i < vs.length(); i++)
                        cfg.vocabularies.add(vs.optString(i));
                }
            }
            JSONObject zone = j.optJSONObject("settingsEntry");
            if (zone != null) {
                cfg.zoneTapEnabled = zone.optBoolean("enabled", cfg.zoneTapEnabled);
                cfg.zoneSize = zone.optInt("zoneSize", cfg.zoneSize);
                cfg.tapCount = zone.optInt("tapCount", cfg.tapCount);
                cfg.timeWindowMs = zone.optInt("timeWindowMs", cfg.timeWindowMs);
                cfg.maxGapMs = zone.optInt("maxGapMs", cfg.maxGapMs);
            }
            JSONObject al = j.optJSONObject("autoLogin");
            if (al != null) {
                cfg.autoLoginEnabled = al.optBoolean("enabled", cfg.autoLoginEnabled);
                cfg.usernameSelector = al.optString("usernameSelector", cfg.usernameSelector);
                cfg.passwordSelector = al.optString("passwordSelector", cfg.passwordSelector);
                cfg.submitSelector = al.optString("submitSelector", cfg.submitSelector);
                cfg.maxWaitSeconds = al.optInt("maxWaitSeconds", cfg.maxWaitSeconds);
                cfg.pollIntervalMs = al.optInt("pollIntervalMs", cfg.pollIntervalMs);
                JSONObject cred = al.optJSONObject("credentials");
                if (cred != null) {
                    cfg.autoLoginUser = cred.optString("username", cfg.autoLoginUser);
                    cfg.autoLoginPass = cred.optString("password", cfg.autoLoginPass);
                }
            }
        } catch (Exception e) {
            Log.e(KioskApp.TAG, "config load failed, using defaults: " + e.getMessage());
        }
        Log.i(KioskApp.TAG, "config loaded, home=" + cfg.homeUrl);
        return cfg;
    }

    public void save() {
        try {
            new File(DIR).mkdirs();
            JSONObject browser = new JSONObject();
            browser.put("homeUrl", homeUrl);
            browser.put("kioskMode", kioskMode);
            browser.put("navigationWhitelist", new JSONArray(navigationWhitelist));
            JSONObject zone = new JSONObject();
            zone.put("enabled", zoneTapEnabled);
            zone.put("zoneSize", zoneSize);
            zone.put("tapCount", tapCount);
            zone.put("timeWindowMs", timeWindowMs);
            zone.put("maxGapMs", maxGapMs);
            JSONObject kb = new JSONObject();
            kb.put("enabled", keyboardEnabled);
            kb.put("mode", keyboardMode);
            kb.put("heightPercent", keyboardHeightPercent);
            kb.put("enableChinese", enableChinese);
            kb.put("vocabularies", new JSONArray(vocabularies));
            kb.put("customVocabulary", customVocabulary);
            JSONObject al = new JSONObject();
            al.put("enabled", autoLoginEnabled);
            al.put("usernameSelector", usernameSelector);
            al.put("passwordSelector", passwordSelector);
            al.put("submitSelector", submitSelector);
            al.put("maxWaitSeconds", maxWaitSeconds);
            al.put("pollIntervalMs", pollIntervalMs);
            JSONObject cred = new JSONObject();
            cred.put("username", autoLoginUser);
            cred.put("password", autoLoginPass);
            al.put("credentials", cred);
            JSONObject j = new JSONObject();
            j.put("version", "1.0");
            j.put("browser", browser);
            j.put("settingsEntry", zone);
            j.put("keyboard", kb);
            j.put("autoLogin", al);
            j.put("devMode", devMode);
            FileWriter w = new FileWriter(PATH);
            w.write(j.toString(2));
            w.close();
        } catch (Exception e) {
            Log.e(KioskApp.TAG, "config save failed: " + e.getMessage());
        }
    }
}
