using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VMGenerator.Models
{
    public class CloneItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _storage = "data";
        private string _format = "raw";
        private bool _isCompleted = false;
        private bool _isConfigured = false;
        private int? _vmId;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Storage
        {
            get => _storage;
            set { _storage = value; OnPropertyChanged(); }
        }

        public string Format
        {
            get => _format;
            set { _format = value; OnPropertyChanged(); }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                _isCompleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public bool IsConfigured
        {
            get => _isConfigured;
            set
            {
                _isConfigured = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        public string StatusText
        {
            get
            {
                if (IsConfigured) return "✓✓";
                if (IsCompleted) return "✓";
                return "—";
            }
        }

        public Brush StatusColor
        {
            get
            {
                if (IsConfigured || IsCompleted)
                    return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80));
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 139, 139));
            }
        }

        public int? VmId
        {
            get => _vmId;
            set { _vmId = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}