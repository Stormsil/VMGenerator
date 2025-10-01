using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VMGenerator.Models;
using VMGenerator.Services;

namespace VMGenerator
{
    public partial class MainWindow : Window
    {
        private readonly UiLogger _log;
        private readonly ProxmoxAutomation _proxmox;
        private readonly TinyFMAutomation _tinyFM;
        private readonly Patcher _patcher;
        private AppConfig _config;
        private CancellationTokenSource? _cts;

        public ObservableCollection<CloneItem> Queue { get; } = new();
        public string[] StorageOptions { get; private set; } = Array.Empty<string>();
        public string[] FormatOptions { get; private set; } = Array.Empty<string>();

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

        public MainWindow()
        {
            InitializeComponent();

            _log = new UiLogger(LogBox);
            _proxmox = new ProxmoxAutomation(_log);
            _tinyFM = new TinyFMAutomation(_log);
            _patcher = new Patcher();

            LoadConfig();
            InitializeQueue();

            QueueList.ItemsSource = Queue;
        }

        private void LoadConfig()
        {
            _config = AppConfig.Load();
            StorageOptions = _config.Storage.Options;
            FormatOptions = _config.Format.Options;
        }

        private void InitializeQueue()
        {
            Queue.Add(new CloneItem
            {
                Name = "WoW",
                Storage = _config.Storage.Default,
                Format = _config.Format.Default
            });
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Status("Инициализация WebView2…", UiState.Working);

                var env = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: null,
                    browserExecutableFolder: null,
                    options: new CoreWebView2EnvironmentOptions("--ignore-certificate-errors"));

                await Browser.EnsureCoreWebView2Async(env);
                Status("Готово", UiState.Ready);
            }
            catch (Exception ex)
            {
                Status("WebView2 error: " + ex.Message, UiState.Error);
            }
        }

        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Можно добавить логику изменения интерфейса в зависимости от режима
        }

        private void BtnConfig_Click(object sender, RoutedEventArgs e)
        {
            var configWindow = new ConfigWindow(_config);
            if (configWindow.ShowDialog() == true)
            {
                _config = configWindow.Config;
                _config.Save();
                StorageOptions = _config.Storage.Options;
                FormatOptions = _config.Format.Options;
                _log.Info("Конфигурация обновлена.");
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null) return;

            var selectedMode = ((ComboBoxItem)ModeCombo.SelectedItem)?.Tag?.ToString() ?? "both";
            var emptyItems = Queue.Where(x => string.IsNullOrWhiteSpace(x.Name)).ToList();

            if (emptyItems.Any())
            {
                _log.Warn("Есть пустые имена VM. Заполните все поля.");
                return;
            }

            if (Queue.Count == 0)
            {
                _log.Warn("Очередь пуста. Добавьте VM для обработки.");
                return;
            }

            _cts = new CancellationTokenSource();
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            BtnAdd.IsEnabled = false;

            try
            {
                switch (selectedMode)
                {
                    case "clone":
                        await ProcessCloneOnlyAsync();
                        break;
                    case "configure":
                        await ProcessConfigureOnlyAsync();
                        break;
                    case "both":
                        await ProcessBothAsync();
                        break;
                }

                Status("Все задачи выполнены", UiState.Success);
            }
            catch (OperationCanceledException)
            {
                Status("Остановлено", UiState.Error);
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message);
                Status("Ошибка: " + ex.Message, UiState.Error);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                BtnAdd.IsEnabled = true;
                CurrentOpText.Text = "";
            }
        }

        private async Task ProcessCloneOnlyAsync()
        {
            CurrentOpText.Text = "Подключение к Proxmox...";
            Status("Подключение к Proxmox…", UiState.Working);

            await _proxmox.ConnectAndPrepareAsync(Browser, _config.Proxmox.Url,
                _config.Proxmox.Username, _config.Proxmox.Password, _cts.Token);

            Status("Клонирование VM", UiState.Working);

            foreach (var item in Queue.ToList())
            {
                if (item.IsCompleted) continue;

                CurrentOpText.Text = $"Клонирование: {item.Name}";
                _cts.Token.ThrowIfCancellationRequested();

                try
                {
                    _log.Table("Запускаю clone", new[]
                    {
                        ("Name", item.Name),
                        ("Storage", item.Storage),
                        ("Format", item.Format),
                    });

                    await Task.Delay(700, _cts.Token);

                    var vmId = await _proxmox.CloneFromTemplate100Async(Browser, item.Name,
                        item.Storage, item.Format, _cts.Token);

                    item.IsCompleted = true;
                    item.VmId = vmId;
                    _log.Info($"Clone '{item.Name}' завершен. VM ID: {vmId}");
                }
                catch (Exception ex)
                {
                    _log.Error($"Clone '{item.Name}': {ex.Message}");
                }

                await Task.Delay(800, _cts.Token);
            }
        }

        private async Task ProcessConfigureOnlyAsync()
        {
            // Ищем VM которые помечены как завершенные клонированием, но еще не сконфигурированы
            var toConfig = Queue.Where(x => x.IsCompleted && !x.IsConfigured).ToList();

            // Если нет явных VmId, попытаемся найти их в Proxmox
            var withoutIds = toConfig.Where(x => !x.VmId.HasValue).ToList();
            if (withoutIds.Any())
            {
                _log.Info($"Попытка найти ID для {withoutIds.Count} VM в Proxmox...");

                foreach (var item in withoutIds)
                {
                    try
                    {
                        string vmIdJs = FromJs(await Browser.ExecuteScriptAsync($@"
(() => {{
  try {{
    const nameToFind = {JsonSerializer.Serialize(item.Name)};
    const nodes = Array.from(document.querySelectorAll('.x-tree-node-text'));
    for (const node of nodes) {{
      const text = node.textContent || '';
      if (text.includes(nameToFind) && text.includes('VM')) {{
        const match = text.match(/(\d+)\s*\(/);
        if (match) return match[1];
      }}
    }}
    return '';
  }} catch(e) {{ return ''; }}
}})();"));

                        if (int.TryParse(vmIdJs, out int vmId))
                        {
                            item.VmId = vmId;
                            _log.Info($"Найден ID {vmId} для VM '{item.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"Ошибка поиска ID для '{item.Name}': {ex.Message}");
                    }
                }
            }

            // Обновляем список для конфигурации
            toConfig = Queue.Where(x => x.IsCompleted && !x.IsConfigured && x.VmId.HasValue).ToList();

            if (!toConfig.Any())
            {
                _log.Warn("Нет VM для конфигурирования. Убедитесь что клонирование завершено и VM видны в Proxmox.");
                return;
            }

            CurrentOpText.Text = "Подключение к TinyFM...";
            Status("Подключение к TinyFM…", UiState.Working);

            await _tinyFM.EnsureQemuAsync(Browser, _config.TinyFM.Url,
                _config.TinyFM.Username, _config.TinyFM.Password, _cts.Token);

            Status("Настройка VM", UiState.Working);

            foreach (var item in toConfig)
            {
                CurrentOpText.Text = $"Настройка: {item.Name}";
                _cts.Token.ThrowIfCancellationRequested();

                try
                {
                    var vmId = item.VmId.Value;
                    int vmbr = Math.Abs(vmId) % 100;

                    Status($"VM {vmId}: ожидание готовности конфига…", UiState.Working);

                    // Ждем до 30 секунд пока конфиг станет доступен
                    string cfg = null;
                    var configTimeout = DateTime.UtcNow.AddSeconds(30);
                    int attempt = 0;

                    while (DateTime.UtcNow < configTimeout && !_cts.Token.IsCancellationRequested)
                    {
                        attempt++;
                        try
                        {
                            _log.Info($"VM {vmId}: попытка {attempt} чтения конфига...");
                            cfg = await _tinyFM.OpenAndReadConfigAsync(Browser, vmId, _cts.Token);

                            // Проверим что конфиг не пустой и содержит разумные данные
                            if (!string.IsNullOrWhiteSpace(cfg) && cfg.Length > 50)
                            {
                                _log.Info($"VM {vmId}: конфиг успешно прочитан ({cfg.Length} символов)");
                                break;
                            }
                            else
                            {
                                _log.Warn($"VM {vmId}: конфиг пустой или слишком короткий, повторяю попытку...");
                                cfg = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Warn($"VM {vmId}: ошибка чтения конфига (попытка {attempt}): {ex.Message}");
                            cfg = null;
                        }

                        if (cfg == null)
                        {
                            await Task.Delay(2000, _cts.Token); // Ждем 2 секунды перед следующей попыткой

                            // Переподключимся к TinyFM чтобы обновить состояние
                            await _tinyFM.EnsureQemuAsync(Browser, _config.TinyFM.Url,
                                _config.TinyFM.Username, _config.TinyFM.Password, _cts.Token, quickIfAlready: true);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(cfg))
                    {
                        throw new Exception($"Не удалось прочитать конфиг VM {vmId} после {attempt} попыток");
                    }

                    Status($"VM {vmId}: патчу (vmbr={vmbr})…", UiState.Working);
                    var pr = await _patcher.BuildPatchedAsync(cfg, vmbr);

                    _log.DiffTable($"VM {vmId}", pr.Changes);

                    await _tinyFM.WriteAndSaveConfigAsync(Browser, vmId, pr.Patched, _cts.Token);

                    item.IsConfigured = true;
                    Status($"VM {vmId}: сохранено", UiState.Success);

                    await _tinyFM.EnsureQemuAsync(Browser, _config.TinyFM.Url,
                        _config.TinyFM.Username, _config.TinyFM.Password, _cts.Token, quickIfAlready: true);
                }
                catch (Exception ex)
                {
                    _log.Error($"Configure '{item.Name}': {ex.Message}");
                }

                await Task.Delay(500, _cts.Token);
            }
        }

        private async Task ProcessBothAsync()
        {
            await ProcessCloneOnlyAsync();

            if (_cts.Token.IsCancellationRequested) return;

            _log.Info("Ожидание готовности VM для конфигурирования...");
            await Task.Delay(5000, _cts.Token); // Увеличенная пауза между этапами
            await ProcessConfigureOnlyAsync();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private enum UiState { Ready, Working, Success, Error }

        private void Status(string text, UiState state)
        {
            StatusText.Text = text;
            string bg = state switch
            {
                UiState.Working => "#2F5C9B",
                UiState.Success => "#2E7D32",
                UiState.Error => "#8B2F2F",
                _ => "#444444"
            };
            StatusBadge.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(bg)!;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            string newName = GenerateNextVmName();
            Queue.Add(new CloneItem
            {
                Name = newName,
                Storage = _config.Storage.Default,
                Format = _config.Format.Default
            });
        }

        private string GenerateNextVmName()
        {
            if (!Queue.Any())
                return "WoW";

            // Ищем все имена с паттерном "prefix" + число
            var existingNames = Queue.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            string basePattern = "";
            int maxNumber = 0;

            foreach (var name in existingNames)
            {
                // Пытаемся найти паттерн: текст + число в конце
                var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)(\d+)$");
                if (match.Success)
                {
                    string prefix = match.Groups[1].Value;
                    int number = int.Parse(match.Groups[2].Value);

                    if (number > maxNumber)
                    {
                        basePattern = prefix;
                        maxNumber = number;
                    }
                }
                else
                {
                    // Имя без числа в конце - используем как базу
                    if (string.IsNullOrEmpty(basePattern))
                        basePattern = name;
                }
            }

            // Если не нашли числовой паттерн, создаем на основе последнего имени
            if (string.IsNullOrEmpty(basePattern))
            {
                var lastName = existingNames.LastOrDefault();
                if (!string.IsNullOrEmpty(lastName))
                {
                    basePattern = lastName;
                    maxNumber = 0;
                }
                else
                {
                    basePattern = "WoW";
                    maxNumber = 0;
                }
            }

            return basePattern + (maxNumber + 1);
        }

        private void BtnRemoveRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CloneItem item)
                Queue.Remove(item);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            Queue.Clear();
            _log.Info("Очередь очищена.");
        }

        private async void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _log.Info("Принудительный сброс браузера...");
                Status("Сброс браузера...", UiState.Working);

                // Останавливаем текущие операции
                _cts?.Cancel();

                // Очищаем браузер
                if (Browser.CoreWebView2 != null)
                {
                    await Browser.CoreWebView2.ExecuteScriptAsync("window.location.href = 'about:blank';");
                    await Task.Delay(1000);
                }

                _log.Info("Браузер сброшен. Готов к новой работе.");
                Status("Готово", UiState.Ready);
            }
            catch (Exception ex)
            {
                _log.Error($"Ошибка сброса: {ex.Message}");
                Status("Ошибка сброса", UiState.Error);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
                BtnStart_Click(this, new RoutedEventArgs());
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
                BtnStop_Click(this, new RoutedEventArgs());
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
                BtnAdd_Click(this, new RoutedEventArgs());
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
                BtnReset_Click(this, new RoutedEventArgs());
        }
    }
}