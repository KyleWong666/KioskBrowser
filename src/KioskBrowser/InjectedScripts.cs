using System.Text.Json;
using KioskBrowser.Shared;

namespace KioskBrowser;

/// <summary>
/// 注入页面的 JS 脚本：焦点监听（弹软键盘）、左上角连击检测、模拟登录、字符注入。
/// </summary>
public static class InjectedScripts
{
    private static string Js(string s) => JsonSerializer.Serialize(s);

    /// <summary>
    /// 文档创建时注入：焦点事件 + 左上角区域点击上报。
    /// </summary>
    public static string FocusAndZone(int zoneSize) => @"
(function(){
  if (window.__kioskInjected) return; window.__kioskInjected = true;
  var ZONE = __ZONE__;
  function post(o){ try{ window.chrome.webview.postMessage(o); }catch(e){} }
  window.addEventListener('pointerdown', function(e){
    if (e.clientX >= 0 && e.clientX <= ZONE && e.clientY >= 0 && e.clientY <= ZONE)
      post({type:'zonetap', x:e.clientX, y:e.clientY});
  }, true);
  function isEditable(t){
    if(!t) return false;
    if(t.isContentEditable) return true;
    var tag = t.tagName;
    if(tag === 'TEXTAREA') return !t.readOnly && !t.disabled;
    if(tag === 'INPUT'){
      var ty = (t.type||'text').toLowerCase();
      return ['text','password','search','email','url','tel','number'].indexOf(ty) >= 0
             && !t.readOnly && !t.disabled;
    }
    return false;
  }
  window.addEventListener('focusin', function(e){ if(isEditable(e.target)) post({type:'focus'}); }, true);
  window.addEventListener('focusout', function(e){ if(isEditable(e.target)) post({type:'blur'}); }, true);
})();
".Replace("__ZONE__", zoneSize.ToString());

    /// <summary>
    /// 模拟登录：轮询等待表单出现 → 填充凭据（触发 input/change，兼容 React/Vue）→ 提交 → 验证。
    /// 结果通过 postMessage {type:'loginResult', result:'success'|'failed'|'notfound'} 回报。
    /// </summary>
    public static string AutoLogin(AutoLoginConfig cfg, string username, string password) => @"
(function(){
  var cfg = {
    userSel: __USERSEL__,
    passSel: __PASSEL__,
    submitSel: __SUBSEL__,
    user: __USER__,
    pass: __PASS__,
    maxWaitMs: __MAXWAIT__,
    pollMs: __POLL__
  };
  function post(o){ try{ window.chrome.webview.postMessage(o); }catch(e){} }
  function find(sel){ try{ return sel ? document.querySelector(sel) : null; }catch(e){ return null; } }
  function setVal(el, v){
    el.focus();
    var proto = el.tagName === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype
                                          : window.HTMLInputElement.prototype;
    var desc = Object.getOwnPropertyDescriptor(proto, 'value');
    if (desc && desc.set) desc.set.call(el, v); else el.value = v;
    el.dispatchEvent(new Event('input', {bubbles:true}));
    el.dispatchEvent(new Event('change', {bubbles:true}));
  }
  var waited = 0;
  var timer = setInterval(function(){
    var u = find(cfg.userSel), p = find(cfg.passSel);
    if (u && p) {
      clearInterval(timer);
      setVal(u, cfg.user);
      setVal(p, cfg.pass);
      setTimeout(function(){
        var btn = find(cfg.submitSel);
        if (btn) btn.click();
        else if (p.form) { if (p.form.requestSubmit) p.form.requestSubmit(); else p.form.submit(); }
        // 验证：用户名输入框消失视为登录成功
        var vwaited = 0;
        var vt = setInterval(function(){
          vwaited += 500;
          if (!find(cfg.userSel)) { clearInterval(vt); post({type:'loginResult', result:'success'}); }
          else if (vwaited >= 8000) { clearInterval(vt); post({type:'loginResult', result:'failed'}); }
        }, 500);
      }, 300);
      return;
    }
    waited += cfg.pollMs;
    if (waited >= cfg.maxWaitMs) { clearInterval(timer); post({type:'loginResult', result:'notfound'}); }
  }, cfg.pollMs);
})();
".Replace("__USERSEL__", Js(cfg.UsernameSelector))
 .Replace("__PASSEL__", Js(cfg.PasswordSelector))
 .Replace("__SUBSEL__", Js(cfg.SubmitSelector))
 .Replace("__USER__", Js(username))
 .Replace("__PASS__", Js(password))
 .Replace("__MAXWAIT__", (cfg.MaxWaitSeconds * 1000).ToString())
 .Replace("__POLL__", cfg.PollIntervalMs.ToString());

    /// <summary>向当前焦点元素插入文本（原生 setter + input/change 事件，框架兼容）。</summary>
    public static string InsertText(string text) => @"
(function(){
  var el = document.activeElement;
  if(!el) return;
  var text = __TEXT__;
  if(el.isContentEditable){ document.execCommand('insertText', false, text); return; }
  var tag = el.tagName;
  if(tag !== 'INPUT' && tag !== 'TEXTAREA') return;
  var proto = tag === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype
                                 : window.HTMLInputElement.prototype;
  var desc = Object.getOwnPropertyDescriptor(proto, 'value');
  var s = el.selectionStart || 0, e = el.selectionEnd || 0;
  var v = el.value || '';
  var nv = v.slice(0, s) + text + v.slice(e);
  if (desc && desc.set) desc.set.call(el, nv); else el.value = nv;
  var pos = s + text.length;
  try{ el.setSelectionRange(pos, pos); }catch(ex){}
  el.dispatchEvent(new Event('input', {bubbles:true}));
  el.dispatchEvent(new Event('change', {bubbles:true}));
})();
".Replace("__TEXT__", Js(text));

    public const string Backspace = @"
(function(){
  var el = document.activeElement;
  if(!el) return;
  if(el.isContentEditable){ document.execCommand('delete'); return; }
  var tag = el.tagName;
  if(tag !== 'INPUT' && tag !== 'TEXTAREA') return;
  var proto = tag === 'TEXTAREA' ? window.HTMLTextAreaElement.prototype
                                 : window.HTMLInputElement.prototype;
  var desc = Object.getOwnPropertyDescriptor(proto, 'value');
  var s = el.selectionStart, e = el.selectionEnd, v = el.value || '';
  if(s == null) return;
  if(s === e && s > 0) s--;
  var nv = v.slice(0, s) + v.slice(e);
  if (desc && desc.set) desc.set.call(el, nv); else el.value = nv;
  try{ el.setSelectionRange(s, s); }catch(ex){}
  el.dispatchEvent(new Event('input', {bubbles:true}));
  el.dispatchEvent(new Event('change', {bubbles:true}));
})();
";

    /// <summary>Enter：textarea 插入换行；input 尝试提交所属表单。</summary>
    public const string Enter = @"
(function(){
  var el = document.activeElement;
  if(!el) return;
  el.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', bubbles:true}));
  var tag = el.tagName;
  if(tag === 'TEXTAREA' || el.isContentEditable){
    document.execCommand('insertText', false, '\n');
  } else if(tag === 'INPUT'){
    var f = el.form;
    if(f){ if(f.requestSubmit) f.requestSubmit(); else f.submit(); }
  }
})();
";

    /// <summary>Tab：焦点移到下一个可输入元素。</summary>
    public const string FocusNext = @"
(function(){
  var els = Array.prototype.slice.call(
    document.querySelectorAll('input,textarea,select,[contenteditable]'))
    .filter(function(x){ return !x.disabled && x.offsetParent !== null; });
  var i = els.indexOf(document.activeElement);
  if(els.length === 0) return;
  var next = els[(i + 1) % els.length];
  next.focus();
  if(next.select) next.select();
})();
";
}
