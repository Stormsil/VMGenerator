using System.IO;
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

        public static AppConfig Load(string filePath = "config.json")
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    var defaultConfig = new AppConfig();
                    defaultConfig.Save(filePath);
                    return defaultConfig;
                }

                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(string filePath = "config.json")
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
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