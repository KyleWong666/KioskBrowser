package com.ssdc.kiosk.web;

import org.json.JSONObject;

/** 注入页面的 JS（Windows 版 InjectedScripts 移植，postMessage → KioskBridge.post）。 */
public class JsInject {

    private static String js(String s) {
        return JSONObject.quote(s);
    }

    /** 页面加载完成后注入一次：焦点事件上报 + inputmode=none 压制系统输入法。 */
    public static String focusNotify() {
        return "(function(){"
                + "if(window.__kioskInjected)return;window.__kioskInjected=true;"
                + "function post(o){try{KioskBridge.post(JSON.stringify(o));}catch(e){}}"
                + "function isEditable(t){if(!t)return false;"
                + "if(t.isContentEditable)return true;var tag=t.tagName;"
                + "if(tag==='TEXTAREA')return !t.readOnly&&!t.disabled;"
                + "if(tag==='INPUT'){var ty=(t.type||'text').toLowerCase();"
                + "return ['text','password','search','email','url','tel','number'].indexOf(ty)>=0"
                + "&&!t.readOnly&&!t.disabled;}return false;}"
                // 压制系统 IME：聚焦前把 inputmode 置 none（WebView Chrome81+ 生效）
                + "function suppressIme(t){try{t.inputMode='none';t.setAttribute('inputmode','none');}catch(e){}}"
                + "window.addEventListener('pointerdown',function(e){if(isEditable(e.target))suppressIme(e.target);},true);"
                + "Array.prototype.forEach.call(document.querySelectorAll('input,textarea'),suppressIme);"
                + "window.addEventListener('focusin',function(e){if(isEditable(e.target)){suppressIme(e.target);post({type:'focus'});}},true);"
                + "window.addEventListener('focusout',function(e){if(isEditable(e.target))post({type:'blur'});},true);"
                + "})();";
    }

    /** 向当前焦点元素插入文本（原生 setter + input/change，框架兼容）。 */
    public static String insertText(String text) {
        return "(function(){var el=document.activeElement;if(!el)return;var text=" + js(text) + ";"
                + "if(el.isContentEditable){document.execCommand('insertText',false,text);return;}"
                + "var tag=el.tagName;if(tag!=='INPUT'&&tag!=='TEXTAREA')return;"
                + "var proto=tag==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;"
                + "var desc=Object.getOwnPropertyDescriptor(proto,'value');"
                + "var s=el.selectionStart||0,e=el.selectionEnd||0,v=el.value||'';"
                + "var nv=v.slice(0,s)+text+v.slice(e);"
                + "if(desc&&desc.set)desc.set.call(el,nv);else el.value=nv;"
                + "var pos=s+text.length;try{el.setSelectionRange(pos,pos);}catch(ex){}"
                + "el.dispatchEvent(new Event('input',{bubbles:true}));"
                + "el.dispatchEvent(new Event('change',{bubbles:true}));})();";
    }

    public static final String BACKSPACE =
            "(function(){var el=document.activeElement;if(!el)return;"
                    + "if(el.isContentEditable){document.execCommand('delete');return;}"
                    + "var tag=el.tagName;if(tag!=='INPUT'&&tag!=='TEXTAREA')return;"
                    + "var proto=tag==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;"
                    + "var desc=Object.getOwnPropertyDescriptor(proto,'value');"
                    + "var s=el.selectionStart,e=el.selectionEnd,v=el.value||'';if(s==null)return;"
                    + "if(s===e&&s>0)s--;var nv=v.slice(0,s)+v.slice(e);"
                    + "if(desc&&desc.set)desc.set.call(el,nv);else el.value=nv;"
                    + "try{el.setSelectionRange(s,s);}catch(ex){}"
                    + "el.dispatchEvent(new Event('input',{bubbles:true}));"
                    + "el.dispatchEvent(new Event('change',{bubbles:true}));})();";

    public static final String ENTER =
            "(function(){var el=document.activeElement;if(!el)return;"
                    + "el.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}));"
                    + "var tag=el.tagName;"
                    + "if(tag==='TEXTAREA'||el.isContentEditable){document.execCommand('insertText',false,'\\n');}"
                    + "else if(tag==='INPUT'){var f=el.form;if(f){if(f.requestSubmit)f.requestSubmit();else f.submit();}}})();";

    public static final String FOCUS_NEXT =
            "(function(){var els=Array.prototype.slice.call("
                    + "document.querySelectorAll('input,textarea,select,[contenteditable]'))"
                    + ".filter(function(x){return !x.disabled&&x.offsetParent!==null;});"
                    + "var i=els.indexOf(document.activeElement);if(els.length===0)return;"
                    + "var next=els[(i+1)%els.length];next.focus();if(next.select)next.select();})();";
}
