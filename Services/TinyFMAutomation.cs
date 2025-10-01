using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VMGenerator.Services
{
    public sealed class TinyFMAutomation
    {
        private readonly UiLogger _log;
        public TinyFMAutomation(UiLogger log) => _log = log;

        private static string JsJson(string s) => JsonSerializer.Serialize(s);

        private static string FromJs(string raw)
        {
            try { return JsonSerializer.Deserialize<string>(raw) ?? string.Empty; }
            catch
            {
                if (!string.IsNullOrEmpty(raw) && raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                    raw = raw.Substring(1, raw.Length - 2);
                return raw
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }
        }

        private static async Task EnsureCoreAsync(WebView2 web)
        {
            if (web.CoreWebView2 == null) await web.EnsureCoreWebView2Async();
        }

        private static async Task WaitForAsync(WebView2 web, string jsCondition, int timeoutSec, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                string ok = FromJs(await web.ExecuteScriptAsync(
                    $"(()=>{{try{{return ({jsCondition})? '1':'0';}}catch(e){{return '0';}}}})();"));
                if (ok == "1") return;
                await Task.Delay(150, ct);
            }
            throw new TimeoutException("Таймаут ожидания DOM.");
        }

        public async Task EnsureQemuAsync(WebView2 web, string url, string user, string pass,
                                          CancellationToken ct, bool quickIfAlready = false)
        {
            await EnsureCoreAsync(web);

            if (quickIfAlready &&
                web.Source != null &&
                web.Source.ToString().Contains("?p=qemu", StringComparison.OrdinalIgnoreCase))
                return;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async void OnDomLoaded(object? _, CoreWebView2DOMContentLoadedEventArgs __)
            {
                try
                {
                    string script = $@"
(()=>{{
  try {{
    const usr = document.getElementById('fm_usr');
    const pwd = document.getElementById('fm_pwd');
    if (usr && pwd) {{
      usr.value = {JsJson(user)};
      pwd.value = {JsJson(pass)};
      const btn = document.querySelector('button[type=""submit""]') || document.querySelector('button.btn-success');
      if (btn) btn.click(); else if (pwd.form) pwd.form.submit();
      return 'login';
    }}
    const q = document.querySelector('a[href*=""?p=qemu""]');
    if (q) {{ q.click(); return 'qemu_click'; }}
    if (location.search.includes('?p=qemu')) return 'at_qemu';
    return 'noop';
  }} catch (e) {{ return 'err:' + e; }}
}})();";
                    string res = FromJs(await web.ExecuteScriptAsync(script));
                    if (res == "at_qemu" || res == "qemu_click")
                        tcs.TrySetResult(true);
                }
                catch (Exception ex) { _log.Warn("JS: " + ex.Message); }
            }

            web.CoreWebView2.DOMContentLoaded += OnDomLoaded;
            web.CoreWebView2.Navigate(url);
            _log.Info("Открываю TinyFM…");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            try { await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cts.Token)); } catch { }
            web.CoreWebView2.DOMContentLoaded -= OnDomLoaded;

            if (!tcs.Task.IsCompleted)
                _log.Warn("Не найден логин/ссылка qemu. Если уже авторизован — зайди вручную в /qemu и жми Start ещё раз.");
            else
                _log.Info("TinyFM: /qemu открыт.");
        }

        public async Task<string> OpenAndReadConfigAsync(WebView2 web, int vmId, CancellationToken ct)
        {
            await EnsureCoreAsync(web);

            string q = $"?p=qemu&edit={vmId}.conf";
            await web.ExecuteScriptAsync($"location.search={JsJson(q)}");
            _log.Step($"Открываю редактор {vmId}.conf");

            await WaitForAsync(web, "document.getElementById('normal-editor')!==null", 20, ct);

            string text = FromJs(await web.ExecuteScriptAsync(
                "document.getElementById('normal-editor')?.value ?? ''"));
            _log.Info($"Прочитал {text.Length} символов, строк: {text.Split('\n').Length}.");
            return text;
        }

        public async Task WriteAndSaveConfigAsync(WebView2 web, int vmId, string content, CancellationToken ct)
        {
            await EnsureCoreAsync(web);

            string setJs = $@"
(()=>{{
  const ta = document.getElementById('normal-editor');
  if (!ta) return 'no_editor';
  ta.value = {JsJson(content)};
  ta.dispatchEvent(new Event('input', {{ bubbles:true }}));
  const btn = document.querySelector('button[name=""Save""]');
  if (btn) {{ btn.click(); return 'saved_click'; }}
  return 'no_button';
}})();";

            string res = FromJs(await web.ExecuteScriptAsync(setJs));
            if (res == "saved_click") _log.Info("Нажал Save.");
            else if (res == "no_button") _log.Warn("Не нашёл кнопку Save.");
            else if (res == "no_editor") _log.Error("Не найден textarea редактора.");

            await Task.Delay(1200, ct);
            await web.ExecuteScriptAsync("location.search='?p=qemu'");
            await WaitForAsync(web, "location.search.includes('?p=qemu')", 10, ct);
            _log.Info("Вернулся в /qemu.");
        }
    }
}