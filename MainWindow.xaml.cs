using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VMGenerator.Models;
using VMGenerator.Services;
using Windows.System;

namespace VMGenerator
{
    public sealed partial class MainWindow : Window
    {
        private readonly UiLogger _log;
        private readonly ProxmoxAutomation _proxmox;
        private readonly TinyFMAutomation _tinyFM;
        private readonly Patcher _patcher;
        private readonly LocalConfigUpdater _noMachineUpdater;
        private AppConfig _config;
        private CancellationTokenSource? _cts;
        private readonly DispatcherTimer _scanTimer = new DispatcherTimer();
        private HashSet<int> _usedIds = new();
        private HashSet<string> _usedNames = new();

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
            _noMachineUpdater = new LocalConfigUpdater(_log);

            LoadConfig();
            InitializeQueue();

            QueueList.ItemsSource = Queue;

            // Maximize window on startup
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.Maximize();
                }
            }
        }

        private void ComboBox_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                if (combo.Tag?.ToString() == "Storage")
                {
                    combo.ItemsSource = StorageOptions;
                }
                else if (combo.Tag?.ToString() == "Format")
                {
                    combo.ItemsSource = FormatOptions;
                }
            }
        }

        private void LoadConfig()
        {
            _config = AppConfig.Load();
            StorageOptions = _config.Storage.Options;
            FormatOptions = _config.Format.Options;

            // Активируем режим отладки если включен в конфиге
            _proxmox.DebugMode = _config.DebugMode;

            if (_config.DebugMode)
            {
                _log.Info("🐛 Режим отладки включен.");
            }
        }

        private void InitializeQueue()
        {
            // Queue is initially empty
        }

        private async void Window_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                Status("Инициализация WebView2…", UiState.Working);

                var env = await CoreWebView2Environment.CreateAsync();
                await Browser.EnsureCoreWebView2Async(env);

                // Игнорируем SSL ошибки через настройки WebView2
                Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

                Status("Готово", UiState.Ready);

                // Запускаем таймер сканирования
                _scanTimer.Interval = TimeSpan.FromSeconds(5);
                _scanTimer.Tick += async (s, args) => await RefreshVmDataAsync();
                _scanTimer.Start();

                // Автоматический логин в Proxmox при запуске
                await AutoConnectToProxmoxAsync();
            }
            catch (Exception ex)
            {
                Status("WebView2 error: " + ex.Message, UiState.Error);
            }
        }

        private async Task AutoConnectToProxmoxAsync(string? targetUrl = null)
        {
            try
            {
                Status("Подключение к Proxmox…", UiState.Working);

                var cts = new CancellationTokenSource();
                await _proxmox.ConnectAndPrepareAsync(Browser, _config.Proxmox.Url,
                    _config.Proxmox.Username, _config.Proxmox.Password, cts.Token);

                if (!string.IsNullOrEmpty(targetUrl))
                {
                    Status("Переход к целевой странице...", UiState.Working);
                    Browser.CoreWebView2.Navigate(targetUrl);
                }

                Status("Proxmox подключён", UiState.Success);
            }
            catch (Exception ex)
            {
                _log.Warn($"Не удалось подключиться к Proxmox: {ex.Message}");
                Status("Готово (без Proxmox)", UiState.Ready);
            }
        }

        private async void BtnConfig_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var configWindow = new ConfigWindow(_config);
            configWindow.XamlRoot = this.Content.XamlRoot;
            var result = await configWindow.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _config = configWindow.Config;
                _config.Save();
                StorageOptions = _config.Storage.Options;
                FormatOptions = _config.Format.Options;

                // Обновляем режим отладки
                _proxmox.DebugMode = _config.DebugMode;
                if (_config.DebugMode)
                {
                    _log.Info("🐛 Режим отладки включен.");
                }
            }
        }

        private async void BtnStart_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (_cts != null) return;

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
                // Всегда выполняем Clone + Configure
                await ProcessBothAsync();

                Status("✓ Все задачи выполнены успешно", UiState.Success);
                _log.Info("========== РАБОТА ЗАВЕРШЕНА ==========");
            }
            catch (OperationCanceledException)
            {
                Status("Остановлено", UiState.Error);
                _log.Warn("Работа остановлена пользователем");
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
                            cfg = await _tinyFM.OpenAndReadConfigAsync(Browser, vmId, _cts.Token);

                            // Проверим что конфиг не пустой и содержит разумные данные
                            if (!string.IsNullOrWhiteSpace(cfg) && cfg.Length > 50)
                            {
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

                    Status($"VM {vmId}: патчу (Name={item.Name})…", UiState.Working);
                    
                    // Обновленная логика патчера: передаем имя для генерации IP
                    var pr = await _patcher.BuildPatchedAsync(cfg, item.Name);

                    _log.Info($"Сгенерирован IP: {pr.GeneratedIp}");
                    _log.DiffTable($"VM {vmId}", pr.Changes);

                    await _tinyFM.WriteAndSaveConfigAsync(Browser, vmId, pr.Patched, _cts.Token);

                    // Обновляем локальный конфиг NoMachine
                    if (!string.IsNullOrEmpty(pr.GeneratedIp))
                    {
                         await _noMachineUpdater.UpdateNoMachineConfigAsync(_config.NoMachine.ConfigPath, item.Name, pr.GeneratedIp);
                    }

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

            await Task.Delay(5000, _cts.Token); // Увеличенная пауза между этапами
            await ProcessConfigureOnlyAsync();

            if (_cts.Token.IsCancellationRequested) return;

            // Генерируем команду start_and_key
            var configuredVms = Queue.Where(x => x.IsConfigured && x.VmId.HasValue).ToList();
            if (configuredVms.Any())
            {
                var vmIds = string.Join(" ", configuredVms.Select(x => x.VmId.Value));
                var command = $"./start_and_key {vmIds}";

                // Показываем команду в UI
                ShowCommand(command);

                try
                {
                    await Task.Delay(2000, _cts.Token); // Пауза перед открытием Shell

                    // Возвращаемся в Proxmox (после TinyFM мы остаемся в TinyFM интерфейсе)
                    Status("Открытие Shell...", UiState.Working);
                    CurrentOpText.Text = "Сброс сессии и открытие Shell...";

                    // Формируем URL к Shell в Proxmox (node h1)
                    var proxmoxUri = new Uri(_config.Proxmox.Url);
                    var shellUrl = $"{proxmoxUri.Scheme}://{proxmoxUri.Host}:{proxmoxUri.Port}/#v1:0:=node%2Fh1:4:=jsconsole:=contentIso:::8::";

                    _log.Step($"Ресет и открытие Shell: {shellUrl}");
                    
                    // Полный сброс сессии и переход к консоли
                    await ResetBrowserAsync(shellUrl);

                    Status("✓ Shell открыт", UiState.Success);
                    _log.Info($"✓ Shell открыт. Вставьте и выполните команду: {command}");
                }
                catch (Exception ex)
                {
                    _log.Warn($"Ошибка открытия Shell: {ex.Message}");
                    _log.Info($"Скопируйте команду вручную: {command}");
                }
            }
        }

        private void ShowCommand(string command)
        {
            CommandTextBox.DispatcherQueue.TryEnqueue(() =>
            {
                CommandTextBox.Text = command;
                CommandPanel.Visibility = Visibility.Visible;
            });
        }

        private void BtnStop_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private enum UiState { Ready, Working, Success, Error }

        private void Status(string text, UiState state)
        {
            StatusText.Text = text;
            var color = state switch
            {
                UiState.Working => Windows.UI.Color.FromArgb(255, 47, 92, 155),
                UiState.Success => Windows.UI.Color.FromArgb(255, 46, 125, 50),
                UiState.Error => Windows.UI.Color.FromArgb(255, 139, 47, 47),
                _ => Windows.UI.Color.FromArgb(255, 68, 68, 68)
            };
            StatusBadge.Background = new SolidColorBrush(color);
        }

        private async void BtnAdd_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            // Обновим данные перед добавлением для максимальной точности
            await RefreshVmDataAsync();
            
            string newName = GenerateNextVmName();
            Queue.Add(new CloneItem
            {
                Name = newName,
                Storage = _config.Storage.Default,
                Format = "raw"
            });
        }

        private string GenerateNextVmName()
        {
            // Учитываем также имена, которые уже в очереди
            var queueNames = Queue.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            string basePattern = "WoW";
            int i = 1;
            while (true)
            {
                int candidateId = 100 + i;
                string candidateName = $"{basePattern}{i}";

                bool idTaken = _usedIds.Contains(candidateId);
                bool nameTaken = _usedNames.Contains(candidateName) || queueNames.Contains(candidateName);

                if (_config.DebugMode)
                {
                    _log.Debug($"Check {candidateName} (ID {candidateId}): IdTaken={idTaken}, NameTaken={nameTaken}");
                }

                // Если ID свободен И Имя свободно - берем
                if (!idTaken && !nameTaken)
                {
                    return candidateName;
                }
                i++;
            }
        }

        private async Task RefreshVmDataAsync()
        {
            if (Browser?.CoreWebView2 == null) return;

            try
            {
                string json = await Browser.ExecuteScriptAsync(@"
(() => {
  try {
    const nodes = Array.from(document.querySelectorAll('.x-tree-node-text'));
    const items = [];
    const debug = [];
    
    for (const node of nodes) {
      const text = (node.innerText || node.textContent || '').trim();
      
      const match = text.match(/(\d+)[\s\u00A0]*\((.+?)\)/);

      if (match) {
        // Use PascalCase for C# compatibility
        items.push({ Id: parseInt(match[1]), Name: match[2].trim() });
        debug.push(`[MATCH] ${text} -> ID:${match[1]} Name:${match[2]}`);
      } else {
        debug.push(`[FAIL] ${text}`);
      }
    }
    
    return JSON.stringify({ items: items, debug: debug }); 
  } catch(e) { return JSON.stringify({ error: e.toString() }); }
})();");
                
                var root = JsonDocument.Parse(FromJs(json)).RootElement;
                
                if (root.TryGetProperty("error", out var err))
                {
                    _log.Error($"Scan JS Error: {err.GetString()}");
                    return;
                }

                if (root.TryGetProperty("debug", out var debugArr))
                {
                    if (_config.DebugMode)
                    {
                         var debugText = string.Join("\n", debugArr.EnumerateArray().Select(x => x.GetString()));
                         _log.Debug("Tree Scan Report:\n" + debugText);
                    }
                }

                if (root.TryGetProperty("items", out var itemsJson))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var items = JsonSerializer.Deserialize<List<VmInfo>>(itemsJson.GetRawText(), options);
                    
                    if (items != null)
                    {
                        _usedIds = new HashSet<int>(items.Select(x => x.Id));
                        _usedNames = new HashSet<string>(items.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                        
                        if (_config.DebugMode)
                             _log.Debug($"Scan updated: {_usedIds.Count} VMs found.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Scan Error: {ex.Message}");
            }
        }

        private class VmInfo { public int Id { get; set; } public string Name { get; set; } = ""; }

        private void BtnRemoveRow_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if ((sender as Microsoft.UI.Xaml.FrameworkElement)?.DataContext is CloneItem item)
                Queue.Remove(item);
        }

        private void BtnClear_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            Queue.Clear();
        }

        private async Task ResetBrowserAsync(string? targetUrl = null)
        {
            try
            {
                Status("Сброс браузера...", UiState.Working);

                // Останавливаем текущие операции
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;

                // Полный сброс WebView2
                if (Browser.CoreWebView2 != null)
                {
                    // Очищаем cookies и кеш
                    await Browser.CoreWebView2.ExecuteScriptAsync("window.location.href = 'about:blank';");
                    await Task.Delay(500);

                    // Очищаем профиль
                    try
                    {
                        await Browser.CoreWebView2.Profile.ClearBrowsingDataAsync();
                    }
                    catch { }

                    await Task.Delay(500);
                }

                // Переподключаемся к Proxmox
                await AutoConnectToProxmoxAsync(targetUrl);
            }
            catch (Exception ex)
            {
                _log.Error($"Ошибка сброса: {ex.Message}");
                Status("Ошибка сброса", UiState.Error);
            }
        }

        private async void BtnReset_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await ResetBrowserAsync();
        }

        private void BtnCopyLog_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var fullLog = _log.GetFullLog();
                if (string.IsNullOrEmpty(fullLog))
                {
                    _log.Warn("Лог пуст, нечего копировать.");
                    return;
                }

                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(fullLog);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                _log.Info($"✓ Лог скопирован в буфер обмена ({fullLog.Length} символов)");
            }
            catch (Exception ex)
            {
                _log.Error($"Ошибка копирования лога: {ex.Message}");
            }
        }

        private void BtnCopyCommand_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                var command = CommandTextBox.Text;
                if (string.IsNullOrEmpty(command))
                {
                    _log.Warn("Команда пуста, нечего копировать.");
                    return;
                }

                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(command);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                _log.Info($"✓ Команда скопирована в буфер обмена: {command}");
            }
            catch (Exception ex)
            {
                _log.Error($"Ошибка копирования команды: {ex.Message}");
            }
        }

        private void BtnTogglePanel_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (RightPanelColumn.Width.Value > 0)
            {
                // Скрываем
                RightPanelColumn.Width = new GridLength(0);
                RightPanelColumn.MinWidth = 0;
                BtnTogglePanel.Content = "◀";
            }
            else
            {
                // Показываем
                RightPanelColumn.Width = new GridLength(400);
                RightPanelColumn.MinWidth = 400;
                BtnTogglePanel.Content = "▶";
            }
        }

        private async void BtnExpandLog_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Full Log",
                CloseButtonText = "Close",
                XamlRoot = this.Content.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };

            // Создаем новый StackPanel для клонирования логов
            var newLogBox = new StackPanel 
            { 
                Padding = new Thickness(16), 
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 43, 43)) 
            };

            foreach (var child in LogBox.Children)
            {
                if (child is TextBlock tb)
                {
                    var newTb = new TextBlock
                    {
                        FontFamily = tb.FontFamily,
                        FontSize = tb.FontSize,
                        FontWeight = tb.FontWeight,
                        Margin = tb.Margin,
                        TextWrapping = TextWrapping.Wrap
                    };

                    foreach (var inline in tb.Inlines)
                    {
                        if (inline is Microsoft.UI.Xaml.Documents.Run run)
                        {
                            newTb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                            {
                                Text = run.Text,
                                Foreground = run.Foreground,
                                FontWeight = run.FontWeight,
                                FontStyle = run.FontStyle
                            });
                        }
                    }
                    newLogBox.Children.Add(newTb);
                }
            }

            var scroll = new ScrollViewer 
            { 
                Content = newLogBox,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = 700,
                Width = 1200,
                MinWidth = 800,
                MinHeight = 500
            };

            dialog.Content = scroll;
            dialog.Resources["ContentDialogMaxWidth"] = 2000;
            
            await dialog.ShowAsync();
        }

        private void BtnToggleLog_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var isExpanded = LogScrollViewer.Visibility == Visibility.Visible;

            if (isExpanded)
            {
                // Collapse the log panel
                LogScrollViewer.Visibility = Visibility.Collapsed;
                BtnToggleLog.Content = "\uE70D"; // ChevronUp icon - shows panel is collapsed, click to expand
            }
            else
            {
                // Expand the log panel
                LogScrollViewer.Visibility = Visibility.Visible;
                BtnToggleLog.Content = "\uE70E"; // ChevronDown icon - shows panel is expanded, click to collapse
            }
        }

        private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // F-key hotkeys
            if (e.Key == VirtualKey.F5)
            {
                BtnStart_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.F6)
            {
                BtnStop_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.F7)
            {
                BtnReset_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
                e.Handled = true;
            }

            // Ctrl+key hotkeys
            if (ctrl && e.Key == VirtualKey.S)
                BtnStart_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
            if (ctrl && e.Key == VirtualKey.E)
                BtnStop_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
            if (ctrl && e.Key == VirtualKey.N)
                BtnAdd_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
            if (ctrl && e.Key == VirtualKey.R)
                BtnReset_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
            if (ctrl && e.Key == VirtualKey.L)
                BtnCopyLog_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
        }
    }
}