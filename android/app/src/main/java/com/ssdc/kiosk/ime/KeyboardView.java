package com.ssdc.kiosk.ime;

import android.content.Context;
import android.graphics.Color;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.List;

/**
 * 自绘软键盘（按钮式，布局对齐 Windows 版）：EN 直输 / 中文拼音（组合串→候选栏）/ 符号页。
 * 触摸键盘不收 WebView 焦点（本 View 不获取焦点），字符经 JS 注入页面。
 */
public class KeyboardView extends LinearLayout {

    public interface KeyListener {
        void onCommit(String text);
        void onBackspace();
        void onEnter();
        void onFocusNext();
        void onHide();
    }

    private static final int BG = Color.rgb(30, 32, 40);
    private static final int KEY_BG = Color.rgb(58, 62, 76);
    private static final int KEY_FG = Color.rgb(230, 232, 240);
    private static final int FN_BG = Color.rgb(44, 47, 58);

    private final KeyListener listener;
    private PinyinEngine engine;         // 引擎 18.5 万词加载较慢，后台就绪后 setEngine
    private boolean chinese = true;      // 中文模式（受配置开关约束）
    private boolean chineseAllowed = true;
    private boolean shift = false;
    private boolean symbols = false;

    // 中文组合态
    private String composition = "";
    private List<String> candidates = new java.util.ArrayList<>();
    private int candPage = 0;
    private static final int CAND_PAGE_SIZE = 8;

    private LinearLayout candBar;
    private TextView compositionView;
    private LinearLayout candButtons;
    private LinearLayout keysHost;

    public KeyboardView(Context ctx, KeyListener l) {
        super(ctx);
        listener = l;
        setOrientation(VERTICAL);
        setBackgroundColor(BG);
        setFocusable(false);
        build();
    }

    /** 后台线程加载完成后注入引擎（主线程禁阻塞加载 18.5 万词）。 */
    public void setEngine(PinyinEngine e) { engine = e; }

    public void setChineseAllowed(boolean allowed) {
        chineseAllowed = allowed;
        if (!allowed) chinese = false;
        rebuildKeys();
    }

    public boolean isChinese() { return chinese; }

    // ---------------- UI 构建 ----------------

    private void build() {
        candBar = new LinearLayout(getContext());
        candBar.setOrientation(HORIZONTAL);
        candBar.setVisibility(GONE);
        compositionView = new TextView(getContext());
        compositionView.setTextColor(Color.rgb(140, 200, 255));
        compositionView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        compositionView.setPadding(dp(8), 0, dp(8), 0);
        compositionView.setGravity(Gravity.CENTER_VERTICAL);
        candBar.addView(compositionView);
        candButtons = new LinearLayout(getContext());
        candButtons.setOrientation(HORIZONTAL);
        candButtons.setLayoutParams(new LayoutParams(0, LayoutParams.MATCH_PARENT, 1));
        candBar.addView(candButtons);
        addView(candBar, new LayoutParams(LayoutParams.MATCH_PARENT, dp(44)));
        keysHost = new LinearLayout(getContext());
        keysHost.setOrientation(VERTICAL);
        addView(keysHost, new LayoutParams(LayoutParams.MATCH_PARENT, 0, 1));
        rebuildKeys();
    }

    private void rebuildKeys() {
        keysHost.removeAllViews();
        if (symbols) buildSymbolRows();
        else buildAlphaRows();
    }

    private void buildAlphaRows() {
        boolean up = shift && !chinese;
        addKeyRow(keysHost, row("1","2","3","4","5","6","7","8","9","0"));
        addKeyRow(keysHost, row(letter("q",up),letter("w",up),letter("e",up),letter("r",up),letter("t",up),
                letter("y",up),letter("u",up),letter("i",up),letter("o",up),letter("p",up)));
        addKeyRow(keysHost, row(letter("a",up),letter("s",up),letter("d",up),letter("f",up),letter("g",up),
                letter("h",up),letter("j",up),letter("k",up),letter("l",up)));
        addKeyRow(keysHost,
                fnKey("⇧", 1.5f, v -> { shift = !shift; rebuildKeys(); }),
                keys(letter("z",up),letter("x",up),letter("c",up),letter("v",up),letter("b",up),
                        letter("n",up),letter("m",up)),
                fnKey("⌫", 1.5f, v -> backspace()));
        addKeyRow(keysHost,
                fnKey("123", 1.5f, v -> { symbols = true; rebuildKeys(); }),
                fnKey(chineseAllowed ? (chinese ? "中文" : "EN") : "EN", 1.5f, v -> {
                    if (!chineseAllowed) return;
                    chinese = !chinese;
                    clearComposition();
                    rebuildKeys();
                }),
                textKey("空格", 5f),
                textKey("，", 1f), textKey("。", 1f),
                fnKey("⇥", 1.5f, v -> listener.onFocusNext()),
                fnKey("↵", 1.5f, v -> enter()),
                fnKey("▼", 1f, v -> listener.onHide()));
    }

    private void buildSymbolRows() {
        addKeyRow(keysHost, row("!","@","#","$","%","^","&","*","(",")"));
        addKeyRow(keysHost, row("-","_","=","+","[","]","{","}",";"));
        addKeyRow(keysHost, row("'", "\"", "\\", "|","<",">","?","/","~"));
        addKeyRow(keysHost,
                keys("￥","…","、","「","」","『","』","【","】"),
                fnKey("⌫", 1.5f, v -> backspace()));
        addKeyRow(keysHost,
                fnKey("abc", 1.5f, v -> { symbols = false; rebuildKeys(); }),
                fnKey(chineseAllowed ? (chinese ? "中文" : "EN") : "EN", 1.5f, v -> {
                    if (!chineseAllowed) return;
                    chinese = !chinese;
                    clearComposition();
                    rebuildKeys();
                }),
                textKey("空格", 5f),
                textKey("，", 1f), textKey("。", 1f),
                fnKey("⇥", 1.5f, v -> listener.onFocusNext()),
                fnKey("↵", 1.5f, v -> enter()),
                fnKey("▼", 1f, v -> listener.onHide()));
    }

    // ---------------- 按键行为 ----------------

    private void onTextKey(String t) {
        if (chinese && !symbols && t.length() == 1 && Character.isLetter(t.charAt(0))) {
            composition += t.toLowerCase();
            candPage = 0;
            refreshCandidates();
        } else {
            listener.onCommit(t);
        }
    }

    private void backspace() {
        if (composition.length() > 0) {
            composition = composition.substring(0, composition.length() - 1);
            if (composition.isEmpty()) clearComposition();
            else refreshCandidates();
        } else {
            listener.onBackspace();
        }
    }

    private void enter() {
        if (composition.length() > 0 && !candidates.isEmpty()) {
            pickCandidate(candidates.get(0));
        } else {
            listener.onEnter();
        }
    }

    private void refreshCandidates() {
        candidates = engine != null
                ? engine.getCandidates(composition, 60)
                : new java.util.ArrayList<>();
        candPage = 0;
        showCandBar();
    }

    private void showCandBar() {
        if (composition.isEmpty()) { candBar.setVisibility(GONE); return; }
        candBar.setVisibility(VISIBLE);
        compositionView.setText(composition);
        candButtons.removeAllViews();
        int start = candPage * CAND_PAGE_SIZE;
        int end = Math.min(start + CAND_PAGE_SIZE, candidates.size());
        for (int i = start; i < end; i++) {
            final String w = candidates.get(i);
            Button b = makeButton(w, false);
            b.setOnClickListener(v -> pickCandidate(w));
            candButtons.addView(b, new LayoutParams(0, LayoutParams.MATCH_PARENT, 1));
        }
        if (candPage > 0) {
            Button prev = makeButton("‹", true);
            prev.setOnClickListener(v -> { candPage--; showCandBar(); });
            candButtons.addView(prev, new LayoutParams(dp(44), LayoutParams.MATCH_PARENT));
        }
        if (end < candidates.size()) {
            Button next = makeButton("›", true);
            next.setOnClickListener(v -> { candPage++; showCandBar(); });
            candButtons.addView(next, new LayoutParams(dp(44), LayoutParams.MATCH_PARENT));
        }
    }

    private void pickCandidate(String word) {
        listener.onCommit(word);
        clearComposition();
    }

    private void clearComposition() {
        composition = "";
        candidates = new java.util.ArrayList<>();
        candPage = 0;
        candBar.setVisibility(GONE);
    }

    /** 外部失焦/隐藏时清空组合态。 */
    public void reset() { clearComposition(); }

    // ---------------- 布局工具 ----------------

    private String letter(String l, boolean up) { return up ? l.toUpperCase() : l; }

    private String[] row(String... keys) { return keys; }

    private String[] keys(String... keys) { return keys; }

    private void addKeyRow(LinearLayout host, Object... items) {
        LinearLayout rowLayout = new LinearLayout(getContext());
        rowLayout.setOrientation(HORIZONTAL);
        for (Object item : items) {
            if (item instanceof String) {
                Button b = makeButton((String) item, false);
                final String label = (String) item;
                b.setOnClickListener(v -> onTextKey(label));
                rowLayout.addView(b, keyLp(1));
            } else if (item instanceof String[]) {
                for (String k : (String[]) item) {
                    Button b = makeButton(k, false);
                    final String label = k;
                    b.setOnClickListener(v -> onTextKey(label));
                    rowLayout.addView(b, keyLp(1));
                }
            } else if (item instanceof View) {
                View v = (View) item;
                rowLayout.addView(v, keyLp(((LayoutParams) v.getLayoutParams()).weight));
            }
        }
        host.addView(rowLayout, new LayoutParams(LayoutParams.MATCH_PARENT, 0, 1));
    }

    private Button textKey(String label, float weight) {
        Button b = makeButton(label, false);
        b.setOnClickListener(v -> onTextKey(label.equals("空格") ? " " : label));
        b.setLayoutParams(keyLp(weight));
        return b;
    }

    private Button fnKey(String label, float weight, OnClickListener l) {
        Button b = makeButton(label, true);
        b.setOnClickListener(l);
        b.setLayoutParams(keyLp(weight));
        return b;
    }

    private Button makeButton(String label, boolean fn) {
        Button b = new Button(getContext());
        b.setText(label);
        b.setTextColor(KEY_FG);
        b.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15);
        b.setBackgroundColor(fn ? FN_BG : KEY_BG);
        b.setFocusable(false);
        b.setAllCaps(false);
        b.setMinHeight(0);
        b.setMinWidth(0);
        b.setMinimumHeight(0);
        b.setMinimumWidth(0);
        return b;
    }

    private LayoutParams keyLp(float weight) {
        LayoutParams lp = new LayoutParams(0, LayoutParams.MATCH_PARENT, weight);
        lp.setMargins(dp(2), dp(2), dp(2), dp(2));
        return lp;
    }

    private int dp(int v) {
        return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v,
                getResources().getDisplayMetrics());
    }
}
