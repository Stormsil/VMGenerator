using System.IO;
using System.Reflection;
using System.Text.Json;

namespace VMGenerator.Models
{
    public class AppConfig
    {
        public ProxmoxConfig Proxmox { get; set; } = new();
        public TinyFMConfig TinyFM { get; set; } = new();
        public StorageConfig Storage { get; set; } = new();
        public FormatConfig Format { get; set; } = new();
        public TemplateConfig Template { get; set; } = new();
        public bool DebugMode { get; set; } = false;

        private static string GetConfigPath()
        {
            // Путь к .vmgenerator рядом с exe
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            return Path.Combine(exeDir, ".vmgenerator");
        }

        private static string LoadEmbeddedDefaultConfig()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "VMGenerator.config.json";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
            catch { }

            // Fallback: создаем дефолтный конфиг программно
            var defaultConfig = new AppConfig();
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(defaultConfig, options);
        }

        public static AppConfig Load()
        {
            string configPath = GetConfigPath();

            try
            {
                // Если конфиг существует - загружаем его
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
                }

                // Если конфига нет - загружаем дефолтный из ресурсов и сохраняем
                string defaultJson = LoadEmbeddedDefaultConfig();
                File.WriteAllText(configPath, defaultJson);

                var defaultOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<AppConfig>(defaultJson, defaultOptions) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                string configPath = GetConfigPath();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(configPath, json);
            }
            catch { }
        }
    }

    public class ProxmoxConfig
    {
        public string Url { get; set; } = "https://192.168.0.43:8006/";
        public string Username { get; set; } = "root";
        public string Password { get; set; } = "!HexWare";
    }

    public class TinyFMConfig
    {
        public string Url { get; set; } = "http://192.168.0.43:8080/index.php?";
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin@123";
    }

    public class StorageConfig
    {
        public string[] Options { get; set; } = { "data", "nvme0n1" };
        public string Default { get; set; } = "nvme0n1";
    }

    public class FormatConfig
    {
        public string[] Options { get; set; } = { "Raw disk image (raw)", "QEMU image format (qcow2)" };
        public string Default { get; set; } = "Raw disk image (raw)";
    }

    public class TemplateConfig
    {
        public int VmId { get; set; } = 100;
        public string Name { get; set; } = "VM 100";
    }
}