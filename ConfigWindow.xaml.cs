using Microsoft.UI.Xaml.Controls;
using System.Linq;
using VMGenerator.Models;

namespace VMGenerator
{
    public sealed partial class ConfigWindow : ContentDialog
    {
        public AppConfig Config { get; private set; }

        public ConfigWindow(AppConfig config)
        {
            InitializeComponent();
            Config = config;
            LoadConfig();

            PrimaryButtonText = "OK";
            SecondaryButtonText = "Cancel";
            PrimaryButtonClick += BtnOK_Click;
            SecondaryButtonClick += BtnCancel_Click;
        }

        private void LoadConfig()
        {
            ProxmoxUrlBox.Text = Config.Proxmox.Url;
            ProxmoxUserBox.Text = Config.Proxmox.Username;
            ProxmoxPassBox.Text = Config.Proxmox.Password;

            TinyUrlBox.Text = Config.TinyFM.Url;
            TinyUserBox.Text = Config.TinyFM.Username;
            TinyPassBox.Text = Config.TinyFM.Password;

            StorageOptionsBox.Text = string.Join(", ", Config.Storage.Options);
            StorageDefaultBox.Text = Config.Storage.Default;

            FormatOptionsBox.Text = string.Join(", ", Config.Format.Options);
            FormatDefaultBox.Text = Config.Format.Default;

            NoMachinePathBox.Text = Config.NoMachine.ConfigPath;

            DebugModeCheckBox.IsChecked = Config.DebugMode;
        }

        private void BtnOK_Click(ContentDialog sender, ContentDialogButtonClickEventArgs e)
        {
            Config.Proxmox.Url = ProxmoxUrlBox.Text.Trim();
            Config.Proxmox.Username = ProxmoxUserBox.Text.Trim();
            Config.Proxmox.Password = ProxmoxPassBox.Text;

            Config.TinyFM.Url = TinyUrlBox.Text.Trim();
            Config.TinyFM.Username = TinyUserBox.Text.Trim();
            Config.TinyFM.Password = TinyPassBox.Text;

            Config.Storage.Options = StorageOptionsBox.Text.Split(',')
                .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            Config.Storage.Default = StorageDefaultBox.Text.Trim();

            Config.Format.Options = FormatOptionsBox.Text.Split(',')
                .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            Config.Format.Default = FormatDefaultBox.Text.Trim();

            Config.NoMachine.ConfigPath = NoMachinePathBox.Text.Trim();

            Config.DebugMode = DebugModeCheckBox.IsChecked == true;
        }

        private void BtnCancel_Click(ContentDialog sender, ContentDialogButtonClickEventArgs e)
        {
        }
    }
}