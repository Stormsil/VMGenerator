using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VMGenerator.Services
{
    public class LocalConfigUpdater
    {
        private readonly UiLogger _log;

        public LocalConfigUpdater(UiLogger log)
        {
            _log = log;
        }

        public async Task UpdateNoMachineConfigAsync(string configPath, string vmName, string targetIp)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configPath) || !Directory.Exists(configPath))
                {
                    _log.Warn($"[NoMachine] Папка не найдена или путь пуст: {configPath}");
                    return;
                }

                // Ищем файл .nxs, имя которого начинается с vmName
                // Точное совпадение: WoW8.nxs
                string fileName = $"{vmName}.nxs";
                string fullPath = Path.Combine(configPath, fileName);

                if (!File.Exists(fullPath))
                {
                    // Попробуем найти регистронезависимо
                    var files = Directory.GetFiles(configPath, "*.nxs");
                    fullPath = Array.Find(files, f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    
                    if (fullPath == null)
                    {
                        _log.Warn($"[NoMachine] Файл конфигурации не найден: {fileName}");
                        return;
                    }
                }

                _log.Info($"[NoMachine] Обновление конфига: {Path.GetFileName(fullPath)} -> {targetIp}");

                string content = await File.ReadAllTextAsync(fullPath);
                string originalContent = content;

                // Замена Server host
                // <option key="Server host" value="192.168.1.1" />
                content = Regex.Replace(content, 
                    @"(<option\s+key=""Server host""\s+value="")[^""]*("")", 
                    $"${{1}}{targetIp}${{2}}");



                if (content != originalContent)
                {
                    await File.WriteAllTextAsync(fullPath, content);
                    _log.Info($"[NoMachine] ✓ Конфиг успешно обновлен.");
                }
                else
                {
                    _log.Info($"[NoMachine] Изменений не требуется.");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[NoMachine] Ошибка обновления конфига: {ex.Message}");
            }
        }
    }
}