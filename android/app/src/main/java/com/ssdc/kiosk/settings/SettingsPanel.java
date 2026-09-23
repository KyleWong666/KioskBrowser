package com.ssdc.kiosk.settings;

import android.content.Context;
import android.graphics.Color;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;

import com.ssdc.kiosk.KioskConfig;

import java.util.ArrayList;
import java.util.List;

/**
 * 设置面板（全屏遮罩覆盖层，左上角连击打开）：
 * URL / 霸屏 / 键盘高度 / 中文输入 / 领域词库 / 自定义词库 + 保存(重启) / 退出 Kiosk。
 */
public class SettingsPanel extends FrameLayout {

    public interface Listener {
        void onSave(KioskConfig cfg, boolean restart);
        void onExitKiosk();
        void onClose();
    }

    private final KioskConfig cfg;
    private final Listener listener;

    private EditText editUrl;
    private CheckBox chkKiosk, chkChinese;
    private SeekBar seekHeight;
    private final List<CheckBox> vocabChecks = new ArrayList<>();
    private static final String[][] VOCABS = {
            {"general", "通用"}, {"gov", "政务"}, {"medical", "医疗"},
            {"retail", "商场"}, {"industrial", "工业"}};
    private EditText editCustom;
    private TextView msgView;
    private Button btnExit;
    private long exitArmAt = 0;

    public SettingsPanel(Context ctx, KioskConfig c, Listener l) {
        super(ctx);
        cfg = c;
        listener = l;
        setBackgroundColor(Color.argb(0xE0, 10, 10, 14));
        build();
    }

    private void build() {
        ScrollView scroll = new ScrollView(getContext());
        LinearLayout card = new LinearLayout(getContext());
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(28), dp(20), dp(28), dp(20));

        TextView title = new TextView(getContext());
        title.setText("Kiosk 设置");
        title.setTextColor(Color.WHITE);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        card.addView(title);
        card.addView(space(12));

        editUrl = addEdit(card, "目标 URL", cfg.homeUrl);
        chkKiosk = addCheck(card, "霸屏模式（沉浸 + LockTask）", cfg.kioskMode);
        chkChinese = addCheck(card, "启用中文拼音输入", cfg.enableChinese);

        card.addView(label("键盘高度：" + cfg.keyboardHeightPercent + "%"));
        seekHeight = new SeekBar(getContext());
        seekHeight.setMax(25);
        seekHeight.setProgress(cfg.keyboardHeightPercent - 25);
        seekHeight.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            public void onProgressChanged(SeekBar sb, int p, boolean fromUser) {
                cfg.keyboardHeightPercent = 25 + p;
                // 直接改标签文本（简单起见重建文本）
                ((TextView) ((LinearLayout) sb.getParent()).getChildAt(
                        ((LinearLayout) sb.getParent()).indexOfChild(sb) - 1))
                        .setText("键盘高度：" + cfg.keyboardHeightPercent + "%");
            }
            public void onStartTrackingTouch(SeekBar sb) {}
            public void onStopTrackingTouch(SeekBar sb) {}
        });
        card.addView(seekHeight);
        card.addView(space(8));

        card.addView(label("领域词库（多选，候选置顶）："));
        LinearLayout vocabRow = new LinearLayout(getContext());
        vocabRow.setOrientation(LinearLayout.HORIZONTAL);
        for (String[] v : VOCABS) {
            CheckBox cb = new CheckBox(getContext());
            cb.setText(v[1]);
            cb.setTextColor(Color.WHITE);
            cb.setChecked(cfg.vocabularies.contains(v[0]));
            cb.setTag(v[0]);
            vocabChecks.add(cb);
            vocabRow.addView(cb);
        }
        card.addView(vocabRow);

        card.addView(label("自定义词库（一行一词）："));
        editCustom = new EditText(getContext());
        editCustom.setText(cfg.customVocabulary);
        editCustom.setMinLines(4);
        editCustom.setGravity(Gravity.TOP);
        editCustom.setTextColor(Color.WHITE);
        editCustom.setBackgroundColor(Color.rgb(30, 32, 40));
        editCustom.setPadding(dp(10), dp(8), dp(10), dp(8));
        card.addView(editCustom);
        card.addView(space(12));

        msgView = new TextView(getContext());
        msgView.setTextColor(Color.rgb(240, 173, 78));
        card.addView(msgView);
        card.addView(space(8));

        LinearLayout btnRow = new LinearLayout(getContext());
        btnRow.setOrientation(LinearLayout.HORIZONTAL);
        btnRow.addView(button("保存并重启", v -> save(true)));
        btnRow.addView(hspace(10));
        btnRow.addView(button("保存", v -> save(false)));
        btnRow.addView(hspace(10));
        btnExit = button("退出 Kiosk", v -> exitArm());
        btnExit.setBackgroundColor(Color.rgb(140, 60, 60));
        btnRow.addView(btnExit);
        btnRow.addView(hspace(10));
        btnRow.addView(button("关闭", v -> listener.onClose()));
        card.addView(btnRow);

        scroll.addView(card);
        LayoutParams lp = new LayoutParams(dp(560), LayoutParams.WRAP_CONTENT);
        lp.gravity = Gravity.CENTER;
        addView(scroll, lp);
        setOnClickListener(v -> { /* 吃掉点击 */ });
    }

    private void save(boolean restart) {
        cfg.homeUrl = editUrl.getText().toString().trim();
        cfg.kioskMode = chkKiosk.isChecked();
        cfg.enableChinese = chkChinese.isChecked();
        cfg.customVocabulary = editCustom.getText().toString();
        cfg.vocabularies.clear();
        for (CheckBox cb : vocabChecks)
            if (cb.isChecked()) cfg.vocabularies.add((String) cb.getTag());
        if (cfg.vocabularies.isEmpty() && cfg.customVocabulary.trim().isEmpty()) {
            msgView.setText("词库不能为空：至少勾选一个领域词库或填写自定义词库");
            return;
        }
        cfg.save();
        msgView.setText(restart ? "已保存，正在重启…" : "已保存（部分配置需重启后生效）");
        listener.onSave(cfg, restart);
    }

    /** 退出按钮二次确认（3 秒内再点一次生效）。 */
    private void exitArm() {
        long now = System.currentTimeMillis();
        if (now - exitArmAt < 3000) {
            listener.onExitKiosk();
            return;
        }
        exitArmAt = now;
        btnExit.setText("再点一次确认退出");
        postDelayed(() -> btnExit.setText("退出 Kiosk"), 3000);
    }

    // ---------------- 布局工具 ----------------

    private TextView label(String t) {
        TextView tv = new TextView(getContext());
        tv.setText(t);
        tv.setTextColor(Color.rgb(180, 184, 198));
        tv.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        tv.setPadding(0, dp(8), 0, dp(4));
        return tv;
    }

    private EditText addEdit(LinearLayout host, String lbl, String val) {
        host.addView(label(lbl));
        EditText e = new EditText(getContext());
        e.setText(val);
        e.setTextColor(Color.WHITE);
        e.setBackgroundColor(Color.rgb(30, 32, 40));
        e.setPadding(dp(10), dp(8), dp(10), dp(8));
        host.addView(e);
        return e;
    }

    private CheckBox addCheck(LinearLayout host, String lbl, boolean val) {
        CheckBox cb = new CheckBox(getContext());
        cb.setText(lbl);
        cb.setTextColor(Color.WHITE);
        cb.setChecked(val);
        cb.setPadding(0, dp(6), 0, dp(6));
        host.addView(cb);
        return cb;
    }

    private Button button(String t, OnClickListener l) {
        Button b = new Button(getContext());
        b.setText(t);
        b.setTextColor(Color.WHITE);
        b.setBackgroundColor(Color.rgb(50, 110, 200));
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private View space(int h) {
        View v = new View(getContext());
        v.setLayoutParams(new LinearLayout.LayoutParams(1, dp(h)));
        return v;
    }

    private View hspace(int w) {
        View v = new View(getContext());
        v.setLayoutParams(new LinearLayout.LayoutParams(dp(w), 1));
        return v;
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
                getResources().getDisplayMetrics());
    }
}
