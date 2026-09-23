# -*- coding: utf-8 -*-
"""
生成 KioskBrowser 拼音字典 v2:
  输入: pinyin.txt        (mozillazg/pinyin-data, 全量汉字→拼音)
        charfreq.txt      (清华 6763 字频序表, JS 字符串)
        phrase_pinyin.txt (mozillazg/phrase-pinyin-data, 词语→拼音)
  输出: pinyin_dict.txt    每行 "音节<TAB>汉字串"   (高频 6763 在前, 其余字按码位补全, 上限 128)
        pinyin_phrases.txt 每行 "连拼key<TAB>词1 词2 ..." (2~4字, 字全在频序表内, 按字频序和排序, 上限 16)
"""
import re
import unicodedata

# 1) 频序字表
raw = open("charfreq.txt", encoding="utf-8-sig").read()
m = re.search(r'var hanzipinlv = "(.+)"', raw, re.S)
freq_chars = list(m.group(1).strip())
freq_rank = {ch: i for i, ch in enumerate(freq_chars)}
print(f"freq chars: {len(freq_chars)}")

def strip_tone(s: str) -> str:
    s = s.replace("ǖ", "v").replace("ǘ", "v").replace("ǚ", "v").replace("ǜ", "v").replace("ü", "v")
    return "".join(c for c in unicodedata.normalize("NFKD", s) if not unicodedata.combining(c))

# 2) 单字拼音表: 字符 -> [音节]
char2py = {}
for line in open("pinyin.txt", encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    m = re.match(r"U\+([0-9A-F]+):\s*([^\s#]+)\s*#\s*(\S+)", line)
    if not m:
        continue
    ch = m.group(3)
    char2py.setdefault(ch, [strip_tone(p) for p in m.group(2).split(",")])
print(f"chars with pinyin: {len(char2py)}")

# 3) 单字字典: 高频字按频率序, 其余字按码位序补全
#    - 过滤 >U+FFFF 的代理对字符(扩展B及以后, 常见字体渲染不了会显示空白)
#    - 多音字: 第一读音(标准音)优先, 异读/古读排到该音节列表尾部
#      (pinyin-data 含异读, 如 内 nèi/nā、能 néng/nài, 不区分会污染候选)
table = {}
def add_char(ch, primary_only: bool):
    if ord(ch) > 0xFFFF:
        return
    pys = char2py.get(ch, [])
    if primary_only:
        pys = pys[:1]
    for py in pys:
        lst = table.setdefault(py, [])
        if len(lst) < 128 and ch not in lst:
            lst.append(ch)

for ch in freq_chars:          # 高频字: 第一读音
    add_char(ch, primary_only=True)
for ch in freq_chars:          # 高频字: 异读补到尾部
    add_char(ch, primary_only=False)
for ch in sorted(char2py):     # 全量补全: 第一读音
    add_char(ch, primary_only=True)
for ch in sorted(char2py):     # 全量补全: 异读
    add_char(ch, primary_only=False)
print(f"syllables: {len(table)}, total char entries: {sum(len(v) for v in table.values())}")

with open("pinyin_dict.txt", "w", encoding="utf-8") as f:
    for py in sorted(table):
        f.write(f"{py}\t{''.join(table[py])}\n")

# 3.5) 字->主读音映射(给词库词算拼音键用): 字<TAB>主读音
with open("pinyin_primary.txt", "w", encoding="utf-8") as f:
    for ch in sorted(char2py):
        if ord(ch) <= 0xFFFF and char2py[ch]:
            f.write(f"{ch}\t{char2py[ch][0]}\n")

# 3.5) jieba 词频表: 词 -> 频率
word_freq = {}
for line in open("jieba_dict.txt", encoding="utf-8"):
    parts = line.strip().split()
    if len(parts) >= 2 and parts[1].isdigit():
        word_freq[parts[0]] = int(parts[1])
print(f"jieba words: {len(word_freq)}")

# 4) 词组字典
phrases = {}
skipped = 0
for line in open("large_pinyin.txt", encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    m = re.match(r"(\S+):\s*(.+)$", line)
    if not m:
        continue
    word, pys = m.group(1), m.group(2).split()
    if not (2 <= len(word) <= 4) or len(word) != len(pys):
        skipped += 1
        continue
    if any(ch not in freq_rank for ch in word):   # 只收字都在频序表内的词, 保证质量
        skipped += 1
        continue
    key = "".join(strip_tone(p) for p in pys)
    inits = "".join(strip_tone(p)[0] for p in pys)
    # 排序键: 有词频的用词频(负值靠前), 没有的退化为字频序和(加大偏移排后)
    rank = -word_freq[word] if word in word_freq else 10_000_000 + sum(freq_rank[ch] for ch in word)
    lst = phrases.setdefault(key, [])
    lst.append((rank, word, inits))
print(f"phrase keys: {len(phrases)}, skipped: {skipped}")

# 输出: key<TAB>首字母<TAB>词1 词2 ...
with open("pinyin_phrases.txt", "w", encoding="utf-8") as f:
    for key in sorted(phrases):
        entries = sorted(phrases[key])[:16]
        words = [w for _, w, _ in entries]
        inits = entries[0][2]
        f.write(f"{key}\t{inits}\t{' '.join(words)}\n")

# 4.5) 首字母索引: 首字母串<TAB>词1 词2 ...(全局按字频序和排序, 上限 32)
initials = {}
for key, entries in phrases.items():
    for rank, word, inits in entries[:4]:   # 每个 key 只取前 4 个词参与首字母索引
        initials.setdefault(inits, []).append((rank, word))
with open("pinyin_initials.txt", "w", encoding="utf-8") as f:
    for inits in sorted(initials):
        words = []
        seen = set()
        for _, w in sorted(initials[inits]):
            if w not in seen:
                seen.add(w)
                words.append(w)
            if len(words) >= 32:
                break
        f.write(f"{inits}\t{' '.join(words)}\n")
print(f"initials keys: {len(initials)}")

# 抽样自检
pd = dict(l.split("\t") for l in open("pinyin_dict.txt", encoding="utf-8"))
pp = {}
for l in open("pinyin_phrases.txt", encoding="utf-8"):
    p = l.rstrip("\n").split("\t")
    pp[p[0]] = p[2]
for k in ["yue", "zi", "xuan"]:
    print(k, "->", pd.get(k, "")[:15].encode("unicode_escape").decode())
for k in ["nihao", "zhongguo", "beijing", "xiexie"]:
    print(k, "->", pp.get(k, "")[:40].encode("unicode_escape").decode())

# 首字母索引自检
init_idx = dict(l.rstrip("\n").split("\t") for l in open("pinyin_initials.txt", encoding="utf-8"))
for k in ["nh", "zg", "bj", "xx"]:
    print("initials", k, "->", init_idx.get(k, "<none>")[:30].encode("unicode_escape").decode())
