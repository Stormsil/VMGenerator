using System.Linq;
using System.Windows;
using VMGenerator.Models;

namespace VMGenerator
{
    public partial class ConfigWindow : Window
    {
        public AppConfig Config { get; private set; }

        public ConfigWindow(AppConfig config)
        {
            InitializeComponent();
            Config = config;
            LoadConfig();
        }

        private void LoadConfig()
        {
            ProxmoxUrlBox.Text = Config.Proxmox.Url;
            ProxmoxUserBox.Text = Config.Proxmox.Username;
            ProxmoxPassBox.Password = Config.Proxmox.Password;

            TinyUrlBox.Text = Config.TinyFM.Url;
            TinyUserBox.Text = Config.TinyFM.Username;
            TinyPassBox.Password = Config.TinyFM.Password;

            StorageOptionsBox.Text = string.Join(", ", Config.Storage.Options);
            StorageDefaultBox.Text = Config.Storage.Default;

            FormatOptionsBox.Text = string.Join(", ", Config.Format.Options);
            FormatDefaultBox.Text = Config.Format.Default;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Config.Proxmox.Url = ProxmoxUrlBox.Text.Trim();
            Config.Proxmox.Username = ProxmoxUserBox.Text.Trim();
            Config.Proxmox.Password = ProxmoxPassBox.Password;

            Config.TinyFM.Url = TinyUrlBox.Text.Trim();
            Config.TinyFM.Username = TinyUserBox.Text.Trim();
            Config.TinyFM.Password = TinyPassBox.Password;

            Config.Storage.Options = StorageOptionsBox.Text.Split(',')
                .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            Config.Storage.Default = StorageDefaultBox.Text.Trim();

            Config.Format.Options = FormatOptionsBox.Text.Split(',')
                .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            Config.Format.Default = FormatDefaultBox.Text.Trim();

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}