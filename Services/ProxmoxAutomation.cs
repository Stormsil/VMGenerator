using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VMGenerator.Services
{
    public sealed class ProxmoxAutomation
    {
        private readonly UiLogger _log;
        public bool DebugMode { get; set; } = false;

        public ProxmoxAutomation(UiLogger log) => _log = log;

        public int SlowMoMs { get; set; } = 800;
        private Task Sleep(CancellationToken ct, int? ms = null) =>
            Task.Delay(ms ?? SlowMoMs, ct);

        private static string Js(string s) => JsonSerializer.Serialize(s);

        private void DebugLog(WebView2 web, string message, object? data = null)
        {
            if (!DebugMode) return;

            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string dataStr = "";

                if (data != null)
                {
                    try
                    {
                        dataStr = $" | Data: {JsonSerializer.Serialize(data)}";
                    }
                    catch
                    {
                        dataStr = $" | Data: {data}";
                    }
                }

                // Логируем в UI (главное!)
                _log.Debug($"{message}{dataStr}");

                // Также логируем в консоль браузера (необязательно)
                try
                {
                    var escapedMsg = Js(message);
                    var escapedData = data != null ? Js(data.ToString() ?? "") : "null";

                    web.ExecuteScriptAsync($@"
console.log('%c[PROXMOX DEBUG {timestamp}]%c {escapedMsg}',
    'color: #00ff00; font-weight: bold;',
    'color: #ffffff;',
    {escapedData});");
                }
                catch { }
            }
            catch (Exception ex)
            {
                _log.Warn($"DebugLog error: {ex.Message}");
            }
        }

        private static string FromJs(string raw)
        {
            try { return JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
            catch
            {
                return raw?.Trim('"')
                          .Replace("\\n", "\n").Replace("\\r", "\r")
                          .Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\") ?? "";
            }
        }

        private static async Task EnsureCoreAsync(WebView2 web)
        {
            if (web.CoreWebView2 == null) await web.EnsureCoreWebView2Async();
        }

        private static async Task WaitForAsync(WebView2 web, string jsCond, int timeoutSec, CancellationToken ct)
        {
            var end = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < end)
            {
                ct.ThrowIfCancellationRequested();
                string ok = FromJs(await web.ExecuteScriptAsync(
                    $"(()=>{{try{{return ({jsCond})?'1':'0';}}catch(e){{return '0';}}}})();"));
                if (ok == "1") return;
                await Task.Delay(150, ct);
            }
            throw new TimeoutException("Таймаут ожидания DOM");
        }

        public async Task ConnectAndPrepareAsync(WebView2 web, string url, string user, string pass, CancellationToken ct)
        {
            await EnsureCoreAsync(web);

            // FORCEFULLY kill "Leave site?" dialogs by overriding addEventListener for 'beforeunload'
            string antiDialogScript = @"
                window.onbeforeunload = null;
                const originalAddEventListener = window.addEventListener;
                window.addEventListener = function(type, listener, options) {
                    if (type === 'beforeunload') {
                        console.log('Blocked beforeunload listener attempt');
                        return;
                    }
                    return originalAddEventListener.call(window, type, listener, options);
                };
            ";
            await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(antiDialogScript);

            var host = new Uri(url).Host;
            web.CoreWebView2.ServerCertificateErrorDetected += (s, e) =>
            {
                try
                {
                    var reqHost = new Uri(e.RequestUri).Host;
                    e.Action = string.Equals(reqHost, host, StringComparison.OrdinalIgnoreCase)
                        ? CoreWebView2ServerCertificateErrorAction.AlwaysAllow
                        : CoreWebView2ServerCertificateErrorAction.Cancel;
                }
                catch { e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; }
            };

            // Проверим, не открыт ли уже Proxmox интерфейс
            try
            {
                string isAlreadyOpen = FromJs(await web.ExecuteScriptAsync(@"
(() => {
  try {
    // Проверяем что это страница Proxmox (есть дерево)
    if (document.querySelector('.x-tree-panel')) {
      // Проверяем что мы на правильном URL (содержит proxmox или имя хоста)
      const currentUrl = window.location.href;
      if (currentUrl.includes('proxmox') || currentUrl.includes('8006') ||
          currentUrl.match(/\d+\.\d+\.\d+\.\d+/)) {
        return 'proxmox_interface';
      }
    }

    // Проверяем наличие формы логина
    if (document.querySelector('input[name=""username""]') &&
        document.querySelector('input[name=""password""]')) {
      return 'login_form';
    }

    return 'unknown_page';
  } catch(e) { return 'error'; }
})();"));

                if (isAlreadyOpen == "proxmox_interface")
                {
                    // Убедимся что дерево развернуто и VM 100 видна
                    await WaitForAsync(web, "document.querySelector('.x-tree-panel')", 10, ct);
                    await Task.Delay(400, ct);

                    // Попытаемся раскрыть дерево если нужно
                    await web.ExecuteScriptAsync(@"
(() => {
  try {
    if (window.Ext && Ext.ComponentQuery) {
      const tree = Ext.ComponentQuery.query('treepanel')[0];
      if (tree && tree.expandAll) {
        tree.expandAll();
      }
    }
  } catch(e) {}
})();");

                    await Task.Delay(1000, ct);

                    // Проверим что VM 100 видна
                    string vm100Visible = FromJs(await web.ExecuteScriptAsync(@"
(() => {
  try {
    return Array.from(document.querySelectorAll('.x-tree-node-text'))
      .some(e => (e.textContent||'').includes('100 (VM 100)')) ? 'visible' : 'hidden';
  } catch(e) { return 'error'; }
})();"));

                    if (vm100Visible == "visible")
                    {
                        return; // УЖЕ ГОТОВО! Выходим
                    }
                    else
                    {
                        // Продолжаем с раскрытием узла h1 ниже
                        goto ExpandH1Node;
                    }
                }
                else if (isAlreadyOpen == "login_form")
                {
                    // Пропускаем навигацию, сразу к логину
                    goto DoLogin;
                }
                else
                {
                    // Продолжаем с навигацией
                }
            }
            catch (Exception ex)
            {
                DebugLog(web, $"Ошибка проверки состояния: {ex.Message}");
            }

            // Навигация к Proxmox если нужно (только если это не форма логина и не интерфейс)
            string currentState = FromJs(await web.ExecuteScriptAsync(@"
(() => {
  try {
    if (document.querySelector('input[name=""username""]') && document.querySelector('input[name=""password""]')) {
      return 'login_form';
    }
    if (document.querySelector('.x-tree-panel')) {
      return 'proxmox_interface';
    }
    return 'other_page';
  } catch(e) { return 'error'; }
})();"));

            if (currentState == "other_page" || currentState == "error")
            {
                web.CoreWebView2.Navigate(url);
                await Task.Delay(600, ct);
            }

            web.CoreWebView2.DOMContentLoaded += async (_, __) =>
            {
                try
                {
                    string res = FromJs(await web.ExecuteScriptAsync(@"
(() => {
  try{
    let clicked = false;
    const tap = ()=>{
      const d = document.getElementById('details-button');
      if (d && !d.hasAttribute('data-clicked')) { d.setAttribute('data-clicked','1'); d.click(); clicked = true; }
      const p = document.getElementById('proceed-link');
      if (p) { p.click(); clicked = true; }
    };
    tap();
    let tries = 0;
    const id = setInterval(()=>{ tries++; if (tries>10) { clearInterval(id); return; } tap(); }, 300);
    return clicked ? 'clicked' : 'noop';
  }catch(e){ return 'err'; }
})();"));
                }
                catch { }
            };

        DoLogin:
            await WaitForAsync(web,
                "document.querySelector('input[name=\"username\"]') && document.querySelector('input[name=\"password\"]')",
                30, ct);

            _log.Step("Ввожу учётку и жму Login…");
            string loginJs = $@"
(()=>{{
  const setVal=(el,val)=>{{ if(!el) return; el.value=val;
    el.dispatchEvent(new Event('input',{{bubbles:true}}));
    el.dispatchEvent(new Event('change',{{bubbles:true}}));
  }};
  const u=document.querySelector('input[name=""username""]');
  const p=document.querySelector('input[name=""password""]');
  setVal(u,{Js(user)}); setVal(p,{Js(pass)});

  setTimeout(()=>{{
    try {{
      if (window.Ext && Ext.ComponentQuery) {{
        var btn = Ext.ComponentQuery.query('button[text=Login]')[0]
               || Ext.ComponentQuery.query('button[ariaLabel=Login]')[0]
               || Ext.ComponentQuery.query('button')[0];
        if (btn) {{
          if (btn.el && btn.el.dom) btn.el.dom.click();
          else if (btn.handler) btn.handler.call(btn);
          return;
        }}
      }}
      var inner = Array.from(document.querySelectorAll('span.x-btn-inner,button,span'))
        .find(e => /^\\s*Login\\s*$/i.test(e.textContent||''));
      if (inner) {{ (inner.closest('span.x-btn-button') || inner.closest('button') || inner).click(); return; }}
      if (p) {{
        p.form && p.form.dispatchEvent(new Event('submit',{{bubbles:true,cancelable:true}}));
        p.dispatchEvent(new KeyboardEvent('keydown', {{key:'Enter', keyCode:13, which:13, bubbles:true}}));
      }}
    }} catch(e) {{}}
  }}, 150);
  return 'login_action_scheduled';
}})();";
            await web.ExecuteScriptAsync(loginJs);
            await Task.Delay(600, ct);

            await WaitForAsync(web, "document.querySelector('.x-tree-panel')", 40, ct);
            await Task.Delay(400, ct);

        ExpandH1Node:
            _log.Step("Раскрываю узел 'h1'…");
            string jsExpand = @"
(() => {
  const TARGET='h1';
  window.__PX_H1='start';
  const hasVM100 = () => Array.from(document.querySelectorAll('.x-tree-node-text'))
    .some(e => (e.textContent||'').includes('100 (VM 100)'));

  try {
    if (window.Ext && Ext.ComponentQuery) {
      const tree  = Ext.ComponentQuery.query('treepanel')[0];
      const store = tree?.getStore?.() || tree?.store;
      if (tree && store) {
        if (tree.expandAll) tree.expandAll();
        const node = store.findNode ? store.findNode('text', TARGET, true) : null;
        if (node) { node.expand(true); tree.getSelectionModel()?.select(node); }
      }
    }
  } catch(e) {}

  const findRow = () => {
    const label = Array.from(document.querySelectorAll('.x-tree-node-text'))
      .find(e => (e.textContent||'').trim() === TARGET);
    return label ? label.closest('tr') : null;
  };

  let tries = 0;
  const id = setInterval(() => {
    tries++;
    if (hasVM100()) { clearInterval(id); window.__PX_H1='expanded'; return; }
    const row = findRow();
    const exp = row?.querySelector('.x-tree-expander');
    if (exp) exp.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));
    if (tries > 40) { clearInterval(id); window.__PX_H1='timeout'; }
  }, 250);

  return 'expand_scheduled';
})();";
            await web.ExecuteScriptAsync(jsExpand);

            await WaitForAsync(web, "window.__PX_H1==='expanded'", 20, ct);

            await WaitForAsync(web,
                "Array.from(document.querySelectorAll('.x-tree-node-text')).some(e => (e.textContent||'').includes('100 (VM 100)'))",
                30, ct);

            await Task.Delay(400, ct);
        }

        public async Task<int?> CloneFromTemplate100Async(WebView2 web, string vmName, string storage, string _ignoreFormat, CancellationToken ct)
        {
            DebugLog(web, "=== НАЧАЛО КЛОНИРОВАНИЯ ===", new { vmName, storage });

            await WaitForAsync(web, "document.querySelector('.x-tree-panel')", 20, ct);

            _log.Step("Открываю контекст-меню на '100 (VM 100)'…");
            DebugLog(web, "Поиск VM 100 в дереве для контекстного меню");

            string openMenu = FromJs(await web.ExecuteScriptAsync(@"
(()=>{
  const label = Array.from(document.querySelectorAll('.x-tree-node-text'))
    .find(e => (e.textContent||'').includes('100 (VM 100)'));
  if(!label) return 'not_found';
  const cell = label.closest('.x-grid-cell');
  const r = cell.getBoundingClientRect();
  cell.dispatchEvent(new MouseEvent('contextmenu', {bubbles:true,cancelable:true,clientX:r.left+16,clientY:r.top+10}));
  return 'ok';
})();"));
            DebugLog(web, "Результат открытия контекстного меню", openMenu);
            await Sleep(ct);

            _log.Step("Жму пункт Clone…");
            string clickClone = FromJs(await web.ExecuteScriptAsync(@"
(()=>{
  const tryClick = () => {
    const txt = Array.from(document.querySelectorAll('.x-menu-item .x-menu-item-text'))
      .find(el => /^(Clone|Клонировать)\s*$/i.test(el.textContent||''));
    if (!txt) return false;
    (txt.closest('.x-menu-item') || txt).click();
    return true;
  };
  if (tryClick()) return 'clicked';
  let tries=0; const id=setInterval(()=>{ if(tryClick()||++tries>10) clearInterval(id); },150);
  return 'waiting';
})();"));
            DebugLog(web, "Результат клика по Clone в меню", clickClone);
            await Sleep(ct, 900);

            await WaitForAsync(web, "document.querySelector('input[name=\"name\"], input[data-componentid^=\"textfield-\"]')", 20, ct);
            await Sleep(ct);

            _log.Step("Заполняю Name / Storage / Format(raw)…");
            DebugLog(web, "Заполнение формы клонирования", new { vmName, storage });

            string fillRes = FromJs(await web.ExecuteScriptAsync($@"
(()=>{{
  const fireAll = (el) => {{ ['input','change','keyup','blur'].forEach(t => el.dispatchEvent(new Event(t,{{bubbles:true}}))); }};
  const setDom = (q, val) => {{ const el=document.querySelector(q); if(!el) return false; el.focus(); el.value=val; fireAll(el); return true; }};
  const setExt = (name, val) => {{ try{{ if(window.Ext&&Ext.ComponentQuery){{const c=Ext.ComponentQuery.query('field[name='+name+']')[0]; if(c&&c.setValue){{c.setValue(val); return true;}} }} }}catch(e){{}} return false; }};
  const lower=s=>(s||'').toLowerCase();

  let okName = setExt('name', {Js(vmName)}) || setDom('input[name=""name""], input[data-componentid^=""textfield-""]', {Js(vmName)});

  const wantStorage = {Js(storage)};
  let okStorage = setExt('hdstorage', wantStorage);
  if (!okStorage) {{
    const st = document.querySelector('input[name=""hdstorage""]');
    if (st) {{
      st.click();
      const pick = () => {{
        const item = Array.from(document.querySelectorAll('.x-boundlist .x-boundlist-item'))
          .find(el => lower(el.textContent).startsWith(lower(wantStorage)));
        if (!item) return false; item.click(); return true;
      }};
      if (pick()) okStorage = true;
      else {{ let t=0; const id=setInterval(()=>{{ if(pick()||++t>12) clearInterval(id); }},120); }}
    }}
  }}

  let okFormat = setExt('diskformat', 'raw') || setDom('input[name=""diskformat""]', 'raw');

  const read = sel => (document.querySelector(sel)?.value)||'';
  return JSON.stringify({{
    nameSet: okName?1:0,
    storageSet: okStorage?1:0,
    formatSet: okFormat?1:0,
    nameNow: read('input[name=""name""]') || read('input[data-componentid^=""textfield-""]'),
    storageNow: read('input[name=""hdstorage""]'),
    formatNow: read('input[name=""diskformat""]')
  }});
}})();"));
            await Sleep(ct);

            try
            {
                var obj = JsonDocument.Parse(fillRes).RootElement;
                _log.Table("Поля после ввода", new[]
                {
            ("Name set",    obj.GetProperty("nameSet").GetInt32().ToString()),
            ("Storage set", obj.GetProperty("storageSet").GetInt32().ToString()),
            ("Format set",  obj.GetProperty("formatSet").GetInt32().ToString()),
            ("Name now",    obj.GetProperty("nameNow").GetString() ?? ""),
            ("Storage now", obj.GetProperty("storageNow").GetString() ?? ""),
            ("Format now",  obj.GetProperty("formatNow").GetString() ?? ""),
        });
            }
            catch { DebugLog(web, "Filled fallback", fillRes); }

            DebugLog(web, "Результат заполнения формы", fillRes);

            await Sleep(ct, 800);

            _log.Step("Нажимаю кнопку Clone…");
            string pressClone = FromJs(await web.ExecuteScriptAsync(@"
(()=>{
  const tap = () => {
    const win = Array.from(document.querySelectorAll('.x-window'))
      .find(w => w.offsetWidth>0 && w.offsetHeight>0 && w.querySelector('input[name=""name""]'));
    const inner = win && Array.from(win.querySelectorAll('span.x-btn-inner,button'))
      .find(e => /^(Clone|Клонировать)\s*$/i.test(e.textContent||''));
    if (!inner) return false;
    const btn = inner.closest('span.x-btn-button') || inner.closest('button') || inner;
    btn.click(); return true;
  };
  if (tap()) return 'clicked';
  let tries=0; const id=setInterval(()=>{ if(tap()||++tries>10) clearInterval(id); },200);
  return 'waiting';
})();"));
            DebugLog(web, "Результат нажатия кнопки Clone", pressClone);

            // Попытка закрыть диалог нажатием Escape
            DebugLog(web, "Попытка закрыть диалог через Escape");
            await web.ExecuteScriptAsync(@"
(() => {
  try {
    document.dispatchEvent(new KeyboardEvent('keydown', {key: 'Escape', keyCode: 27, which: 27, bubbles: true}));
  } catch(e) {}
})();");

            await Sleep(ct, 1500);

            // Проверяем закрылся ли диалог
            string dialogStatus = FromJs(await web.ExecuteScriptAsync(@"
(() => {
  try {
    const nameInput = document.querySelector('input[name=""name""], input[data-componentid^=""textfield-""]');
    const cloneWindow = Array.from(document.querySelectorAll('.x-window'))
      .find(w => w.offsetWidth>0 && w.offsetHeight>0 && w.querySelector('input[name=""name""]'));

    if (!nameInput && !cloneWindow) return 'closed';
    if (nameInput && nameInput.offsetParent === null) return 'closed';
    if (cloneWindow && cloneWindow.offsetParent === null) return 'closed';

    return 'open';
  } catch(e) { return 'closed'; }
})();"));

            DebugLog(web, "Статус диалога после Escape", dialogStatus);

            if (dialogStatus == "open")
            {
                DebugLog(web, "Диалог не закрылся, но это нормально - продолжаем");
            }
            else
            {
                DebugLog(web, "Диалог закрыт");
            }

            await Sleep(ct);

            // Ждем появления новой VM в дереве
            DebugLog(web, "Начало поиска новой VM в дереве", vmName);

            web.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            var timeoutEnd = DateTime.UtcNow.AddSeconds(60); // Увеличим таймаут
            int? foundVmId = null;

            while (DateTime.UtcNow < timeoutEnd && !ct.IsCancellationRequested)
            {
                DebugLog(web, "Попытка обновления дерева Proxmox");

                // Легкое обновление без полного перезагрузки дерева
                await web.ExecuteScriptAsync(@"
(() => {
  try {
    if (window.PVE && PVE.data && PVE.data.resourceTree && PVE.data.resourceTree.reload) {
        console.log('%c[DEBUG] Вызов PVE.data.resourceTree.reload()', 'color: #ffff00;');
        PVE.data.resourceTree.reload();
    } else {
        console.warn('[DEBUG] PVE.data.resourceTree недоступен:', {
            hasPVE: !!window.PVE,
            hasData: !!(window.PVE && window.PVE.data),
            hasResourceTree: !!(window.PVE && window.PVE.data && window.PVE.data.resourceTree),
            hasReload: !!(window.PVE && window.PVE.data && window.PVE.data.resourceTree && window.PVE.data.resourceTree.reload)
        });
    }
  } catch(e) {
    console.error('Failed to reload Proxmox resource tree:', e);
  }
})();");

                await Task.Delay(1000, ct); // Подождем обновления

                string vmIdJs = FromJs(await web.ExecuteScriptAsync($@"
(() => {{
  try {{
    const nameToFind = {Js(vmName)};
    const nodes = Array.from(document.querySelectorAll('.x-tree-node-text'));
    console.log('%c[DEBUG] Поиск VM в дереве. Всего узлов:', 'color: #00ffff;', nodes.length);

    const allTexts = nodes.map(n => n.textContent).filter(t => t && t.trim());
    console.log('%c[DEBUG] Все тексты узлов:', 'color: #00ffff;', allTexts);

    for (const node of nodes) {{
      const text = node.textContent || '';
      console.log('%c[DEBUG] Проверяю узел:', 'color: #00ffff;', text);

      // Ищем VM по имени в скобках, например: ""112 (WoW12)""
      if (text.includes(nameToFind)) {{
        console.log('%c[DEBUG] Найдено совпадение по имени!', 'color: #ffff00;', text);
        const match = text.match(/(\d+)\s*\(/);
        if (match) {{
          console.log('%c[DEBUG] НАЙДЕНА VM!', 'color: #00ff00; font-size: 16px;', {{ text, vmId: match[1] }});
          return match[1];
        }} else {{
          console.log('%c[DEBUG] Имя найдено, но не удалось извлечь ID', 'color: #ff9900;', text);
        }}
      }}
    }}
    console.log('%c[DEBUG] VM не найдена. Искали:', 'color: #ff9900;', nameToFind);
    return '';
  }} catch(e) {{
    console.error('[DEBUG] Ошибка поиска VM:', e);
    return '';
  }}
}})();"));

                DebugLog(web, "Результат поиска VM", vmIdJs);

                if (int.TryParse(vmIdJs, out int vmId))
                {
                    foundVmId = vmId;
                    DebugLog(web, "✓ VM НАЙДЕНА", vmId);
                    break;
                }

                DebugLog(web, "VM пока не найдена, продолжаем ждать");
                await Task.Delay(2000, ct); // Подождем еще 2 секунды
            }

            if (foundVmId.HasValue)
            {
                DebugLog(web, "Активация новой VM контекстным меню", foundVmId.Value);

                // Дополнительная проверка - кликнем правой кнопкой на новую VM чтобы "активировать" ее
                await web.ExecuteScriptAsync($@"
(() => {{
  try {{
    const nameToFind = {Js(vmName)};
    const label = Array.from(document.querySelectorAll('.x-tree-node-text'))
      .find(e => (e.textContent||'').includes(nameToFind) && (e.textContent||'').includes('VM'));
    if (label) {{
      const cell = label.closest('.x-grid-cell');
      const r = cell.getBoundingClientRect();
      cell.dispatchEvent(new MouseEvent('contextmenu', {{bubbles:true,cancelable:true,clientX:r.left+16,clientY:r.top+10}}));
      setTimeout(() => {{
        // Закрываем контекстное меню
        document.addEventListener('click', function() {{ }}, {{ once: true }});
        document.dispatchEvent(new MouseEvent('click', {{bubbles:true,cancelable:true,clientX:0,clientY:0}}));
      }}, 100);
    }}
  }} catch(e) {{
    console.error('[DEBUG] Ошибка при активации VM:', e);
  }}
}})();");

                await Task.Delay(500, ct); // Небольшая пауза
                DebugLog(web, "=== КЛОНИРОВАНИЕ ЗАВЕРШЕНО УСПЕШНО ===", foundVmId.Value);
                return foundVmId.Value;
            }

            _log.Warn($"VM '{vmName}' не найдена в дереве после клонирования.");
            DebugLog(web, "ОШИБКА: VM не найдена после таймаута", vmName);
            return null;
        }
    }
}
