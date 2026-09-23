package com.ssdc.kiosk.ime;

import android.inputmethodservice.InputMethodService;
import android.util.Log;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.widget.FrameLayout;

import com.ssdc.kiosk.KioskApp;
import com.ssdc.kiosk.KioskConfig;

/**
 * Kiosk 系统输入法：键盘视图与拼音引擎原样复用，
 * 上屏走 InputConnection 直写（零 JS IPC，对比注入式键盘的核心提速点）。
 *
 * 装机：adb shell ime enable com.ssdc.kiosk/.ime.KioskImeService
 *       adb shell ime set    com.ssdc.kiosk/.ime.KioskImeService
 */
public class KioskImeService extends InputMethodService {

    private KeyboardView keyboard;

    @Override
    public View onCreateInputView() {
        KioskConfig cfg = KioskConfig.load();
        Log.i(KioskApp.TAG, "ime input view created, chinese=" + cfg.enableChinese);

        keyboard = new KeyboardView(this, new KeyboardView.KeyListener() {
            @Override public void onCommit(String text) { commit(text); }
            @Override public void onBackspace() { backspace(); }
            @Override public void onEnter() { enter(); }
            @Override public void onFocusNext() { sendDownUpKeyEvents(KeyEvent.KEYCODE_TAB); }
            @Override public void onHide() { requestHideSelf(0); }
        });
        keyboard.setChineseAllowed(cfg.enableChinese);

        // IME 窗口按配置高度（weight 布局需要确定高度）
        int h = (int) (getResources().getDisplayMetrics().heightPixels
                * (cfg.keyboardHeightPercent / 100.0f));
        keyboard.setLayoutParams(new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, h));

        // 拼音引擎后台加载（18.5 万词，主线程禁阻塞）
        KioskConfig c = cfg;
        new Thread(() -> {
            PinyinEngine eng = PinyinEngine.get(this);
            eng.configure(this, c.vocabularies, c.customVocabulary);
            keyboard.post(() -> keyboard.setEngine(eng));
        }).start();
        return keyboard;
    }

    @Override
    public void onFinishInputView(boolean finishingInput) {
        super.onFinishInputView(finishingInput);
        if (keyboard != null) keyboard.reset();
    }

    // ---------------- 上屏 ----------------

    private void commit(String text) {
        InputConnection ic = getCurrentInputConnection();
        if (ic != null) ic.commitText(text, 1);
    }

    private void backspace() {
        InputConnection ic = getCurrentInputConnection();
        if (ic == null) return;
        // deleteSurroundingText 自动处理：有选区删选区，无选区删光标前一字符
        if (!ic.deleteSurroundingText(1, 0)) {
            sendDownUpKeyEvents(KeyEvent.KEYCODE_DEL);
        }
    }

    private void enter() {
        InputConnection ic = getCurrentInputConnection();
        EditorInfo ei = getCurrentInputEditorInfo();
        if (ic != null && ei != null) {
            int action = ei.imeOptions & EditorInfo.IME_MASK_ACTION;
            boolean noAction = (ei.imeOptions & EditorInfo.IME_FLAG_NO_ENTER_ACTION) != 0;
            if (action != EditorInfo.IME_ACTION_NONE && !noAction) {
                ic.performEditorAction(action); // 正规提交（登录/搜索按钮动作）
                return;
            }
        }
        sendDownUpKeyEvents(KeyEvent.KEYCODE_ENTER);
    }
}
