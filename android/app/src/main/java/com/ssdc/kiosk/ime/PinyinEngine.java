package com.ssdc.kiosk.ime;

import android.content.Context;
import android.util.Log;

import com.ssdc.kiosk.KioskApp;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;

/**
 * 拼音引擎（Windows 版 PinyinEngine.cs 的 1:1 移植）：
 * 单字表(音节→汉字,高频在前) + 词组表(连拼key→词组)。
 * 候选优先级：领域/自定义置顶 → 精确词组 → 精确单字 → 词组前缀 → 首字母 → 音节组合 → 单字前缀。
 */
public class PinyinEngine {

    private static PinyinEngine instance;
    public static synchronized PinyinEngine get(Context ctx) {
        if (instance == null) instance = load(ctx.getApplicationContext());
        return instance;
    }

    private final Map<String, String> chars = new HashMap<>();
    private final Map<String, String[]> phrases = new HashMap<>();
    private final Map<String, String[]> initials = new HashMap<>();
    private final Map<Character, String> charPrimary = new HashMap<>();
    private final Map<String, String> wordToKey = new HashMap<>();
    private final List<String> syllablesByLenDesc = new ArrayList<>();
    private String[] phraseKeys = new String[0];

    private boolean generalEnabled = true;
    private List<String[]> boost = new ArrayList<>(); // {word, key, initials}

    private static final Map<String, String> PACK_FILES = new HashMap<>();
    static {
        PACK_FILES.put("gov", "vocab_gov.txt");
        PACK_FILES.put("medical", "vocab_medical.txt");
        PACK_FILES.put("retail", "vocab_retail.txt");
        PACK_FILES.put("industrial", "vocab_industrial.txt");
    }

    public List<String> boostWords() {
        List<String> r = new ArrayList<>();
        for (String[] b : boost) r.add(b[0]);
        return r;
    }

    private static PinyinEngine load(Context ctx) {
        PinyinEngine e = new PinyinEngine();
        try {
            BufferedReader r;
            r = open(ctx, "pinyin_dict.txt");
            for (String line; (line = r.readLine()) != null; ) {
                String[] p = line.split("\t");
                if (p.length == 2 && p[0].length() > 0) e.chars.put(p[0], p[1]);
            }
            r.close();
            r = open(ctx, "pinyin_phrases.txt");
            for (String line; (line = r.readLine()) != null; ) {
                String[] p = line.split("\t");
                if (p.length == 3 && p[0].length() > 0) {
                    String[] words = p[2].trim().split(" +");
                    e.phrases.put(p[0], words);
                    for (String w : words) e.wordToKey.putIfAbsent(w, p[0]);
                }
            }
            r.close();
            r = open(ctx, "pinyin_initials.txt");
            for (String line; (line = r.readLine()) != null; ) {
                String[] p = line.split("\t");
                if (p.length == 2 && p[0].length() > 0)
                    e.initials.put(p[0], p[1].trim().split(" +"));
            }
            r.close();
            r = open(ctx, "pinyin_primary.txt");
            for (String line; (line = r.readLine()) != null; ) {
                String[] p = line.split("\t");
                if (p.length == 2 && p[0].length() == 1) e.charPrimary.put(p[0].charAt(0), p[1]);
            }
            r.close();
        } catch (Exception ex) {
            Log.e(KioskApp.TAG, "pinyin engine load failed: " + ex.getMessage());
        }
        e.syllablesByLenDesc.addAll(e.chars.keySet());
        e.syllablesByLenDesc.sort((a, b) -> b.length() - a.length());
        e.phraseKeys = e.phrases.keySet().toArray(new String[0]);
        Arrays.sort(e.phraseKeys, Comparator.comparingInt(String::length));
        Log.i(KioskApp.TAG, "pinyin engine loaded: " + e.chars.size() + " syllables, "
                + e.phraseKeys.length + " phrase keys, " + e.initials.size() + " initials keys");
        return e;
    }

    private static BufferedReader open(Context ctx, String file) throws Exception {
        InputStream in = ctx.getAssets().open("pinyin/" + file);
        return new BufferedReader(new InputStreamReader(in));
    }

    // ---------------- 候选 ----------------

    public List<String> getCandidates(String input, int max) {
        LinkedHashSet<String> result = new LinkedHashSet<>();
        if (input == null || input.isEmpty()) return new ArrayList<>();

        // 0) 领域/自定义词库置顶：精确连拼 > 前缀 > 首字母
        for (String[] b : boost) {
            if (b[1].equals(input)) result.add(b[0]);
            if (result.size() >= max) break;
        }
        if (result.size() < max)
            for (String[] b : boost) {
                if ((b[1].startsWith(input) || b[2].startsWith(input)) && !b[1].equals(input))
                    result.add(b[0]);
                if (result.size() >= max) break;
            }
        if (result.size() < max)
            for (String[] b : boost) {
                if (b[2].equals(input)) result.add(b[0]);
                if (result.size() >= max) break;
            }

        // 1) 精确词组
        String[] words;
        if (generalEnabled && (words = phrases.get(input)) != null)
            for (String w : words) {
                result.add(w);
                if (result.size() >= max) break;
            }

        // 2) 精确单字音节
        String cs;
        if ((cs = chars.get(input)) != null)
            for (int i = 0; i < cs.length() && result.size() < max; i++)
                result.add(String.valueOf(cs.charAt(i)));

        // 3) 词组前缀
        if (generalEnabled && result.size() < max && input.length() >= 2)
            for (String key : phraseKeys) {
                if (result.size() >= max) break;
                if (key.length() <= input.length() || !key.startsWith(input)) continue;
                String[] ws = phrases.get(key);
                for (int i = 0; i < Math.min(2, ws.length); i++) result.add(ws[i]);
            }

        // 4) 首字母输入（2~5 字母）
        if (generalEnabled && result.size() < max && input.length() >= 2 && input.length() <= 5
                && (words = initials.get(input)) != null)
            for (String w : words) {
                result.add(w);
                if (result.size() >= max) break;
            }

        // 5) 音节切分组合兜底
        if (result.size() < max && input.length() > 1) {
            List<String> segs = segment(input);
            if (segs != null && segs.size() >= 2)
                for (String combo : buildCombos(segs, max)) result.add(combo);
        }

        // 6) 单字音节前缀
        if (result.size() < max) {
            List<String> prefixes = new ArrayList<>();
            for (String k : chars.keySet())
                if (k.startsWith(input) && !k.equals(input)) prefixes.add(k);
            prefixes.sort(String::compareTo);
            for (int rank = 0; rank < 3 && result.size() < max; rank++)
                for (String py : prefixes) {
                    if (result.size() >= max) break;
                    String c = chars.get(py);
                    if (rank < c.length()) result.add(String.valueOf(c.charAt(rank)));
                }
        }
        return new ArrayList<>(result).subList(0, Math.min(max, result.size()));
    }

    // ---------------- 词库配置 ----------------

    public void configure(Context ctx, List<String> vocabularies, String customVocabulary) {
        generalEnabled = vocabularies.contains("general");
        LinkedHashSet<String> words = new LinkedHashSet<>();
        for (String pack : vocabularies) {
            String file = PACK_FILES.get(pack);
            if (file == null) continue;
            try {
                BufferedReader r = open(ctx, file);
                for (String line; (line = r.readLine()) != null; ) {
                    line = line.trim();
                    if (line.length() > 0) words.add(line);
                }
                r.close();
            } catch (Exception ex) {
                Log.w(KioskApp.TAG, "vocab pack " + pack + " load failed: " + ex.getMessage());
            }
        }
        for (String line : (customVocabulary == null ? "" : customVocabulary).split("\n")) {
            String w = line.trim();
            if (w.length() > 0) words.add(w);
        }
        List<String[]> boost = new ArrayList<>();
        for (String w : words) {
            String key = keyOf(w);
            if (key == null) continue;
            boost.add(new String[]{w, key, initialsOf(key)});
        }
        this.boost = boost;
        Log.i(KioskApp.TAG, "vocab configured: general=" + generalEnabled + ", boost=" + boost.size());
    }

    /** 词 → 连拼键：整词命中用词级读音；否则最长子词拼接；兜底逐字主读音。 */
    private String keyOf(String word) {
        String k = wordToKey.get(word);
        if (k != null) return k;
        StringBuilder sb = new StringBuilder();
        int i = 0;
        while (i < word.length()) {
            boolean matched = false;
            for (int len = Math.min(4, word.length() - i); len >= 2 && !matched; len--) {
                String subKey = wordToKey.get(word.substring(i, i + len));
                if (subKey != null) {
                    sb.append(subKey);
                    i += len;
                    matched = true;
                }
            }
            if (matched) continue;
            char c = word.charAt(i);
            if (c < 128) { sb.append(Character.toLowerCase(c)); i++; continue; }
            String py = charPrimary.get(c);
            if (py == null) return null;
            sb.append(py);
            i++;
        }
        return sb.toString();
    }

    private String initialsOf(String key) {
        List<String> segs = segment(key);
        if (segs == null) return key;
        StringBuilder sb = new StringBuilder();
        for (String s : segs) sb.append(s.charAt(0));
        return sb.toString();
    }

    /** 贪心最长匹配切分；无法完整切分返回 null。 */
    private List<String> segment(String input) {
        List<String> result = new ArrayList<>();
        int i = 0;
        while (i < input.length()) {
            boolean matched = false;
            for (String syl : syllablesByLenDesc) {
                if (syl.length() <= input.length() - i && input.startsWith(syl, i)) {
                    result.add(syl);
                    i += syl.length();
                    matched = true;
                    break;
                }
            }
            if (!matched) return null;
        }
        return result;
    }

    /** 各音节取前 3 字笛卡尔组合，按字频序号和升序。 */
    private List<String> buildCombos(List<String> segs, int max) {
        List<List<Character>> perSyl = new ArrayList<>();
        for (String s : segs) {
            List<Character> l = new ArrayList<>();
            String cs = chars.get(s);
            for (int i = 0; i < Math.min(3, cs.length()); i++) l.add(cs.charAt(i));
            perSyl.add(l);
        }
        List<int[]> combos = new ArrayList<>();
        List<String> texts = new ArrayList<>();
        int[] indices = new int[perSyl.size()];
        while (true) {
            StringBuilder sb = new StringBuilder();
            int rank = 0;
            for (int i = 0; i < perSyl.size(); i++) {
                sb.append(perSyl.get(i).get(indices[i]));
                rank += indices[i];
            }
            texts.add(sb.toString());
            combos.add(new int[]{rank, texts.size() - 1});
            int pos = perSyl.size() - 1;
            while (pos >= 0 && ++indices[pos] >= perSyl.get(pos).size()) {
                indices[pos] = 0;
                pos--;
            }
            if (pos < 0) break;
        }
        combos.sort((a, b) -> a[0] - b[0]);
        List<String> out = new ArrayList<>();
        for (int i = 0; i < Math.min(max, combos.size()); i++) out.add(texts.get(combos.get(i)[1]));
        return out;
    }
}
