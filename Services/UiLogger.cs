using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VMGenerator.Services
{
    public sealed class UiLogger
    {
        private readonly StackPanel _box;
        private readonly StringBuilder _fullLog = new StringBuilder();

        public UiLogger(StackPanel box)
        {
            _box = box;
        }

        public string GetFullLog() => _fullLog.ToString();

        private void Line(string prefix, string text, Windows.UI.Color prefixColor, Windows.UI.Color textColor)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] {prefix}{text}";
            _fullLog.AppendLine(logLine);

            _box.DispatcherQueue.TryEnqueue(() =>
            {
                var textBlock = new TextBlock
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12
                };

                textBlock.Inlines.Add(new Run
                {
                    Text = prefix,
                    Foreground = new SolidColorBrush(prefixColor)
                });

                textBlock.Inlines.Add(new Run
                {
                    Text = text,
                    Foreground = new SolidColorBrush(textColor)
                });

                _box.Children.Add(textBlock);
            });
        }

        public void Info(string msg) => Line("[INFO] ", msg, Windows.UI.Color.FromArgb(255, 255, 208, 70), Windows.UI.Color.FromArgb(255, 220, 220, 220));
        public void Warn(string msg) => Line("[WARN] ", msg, Windows.UI.Color.FromArgb(255, 255, 208, 70), Windows.UI.Color.FromArgb(255, 255, 208, 70));
        public void Error(string msg) => Line("[ERROR] ", msg, Windows.UI.Color.FromArgb(255, 255, 208, 70), Windows.UI.Color.FromArgb(255, 192, 57, 43));
        public void Step(string msg) => Line("> ", msg, Windows.UI.Color.FromArgb(255, 255, 208, 70), Windows.UI.Color.FromArgb(255, 78, 161, 255));
        public void Debug(string msg) => Line("[DEBUG] ", msg, Windows.UI.Color.FromArgb(255, 255, 208, 70), Windows.UI.Color.FromArgb(255, 0, 255, 255));

        public void Table(string title, IEnumerable<(string Field, string Value)> rows)
        {
            var list = rows?.ToList() ?? new();
            if (list.Count == 0) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _fullLog.AppendLine($"[{timestamp}] • {title}");
            foreach (var row in list)
            {
                _fullLog.AppendLine($"  {row.Field}: {row.Value}");
            }

            _box.DispatcherQueue.TryEnqueue(() =>
            {
                var titleBlock = new TextBlock
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                titleBlock.Inlines.Add(new Run { Text = "• ", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 208, 70)) });
                titleBlock.Inlines.Add(new Run { Text = title, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)) });
                _box.Children.Add(titleBlock);

                foreach (var row in list)
                {
                    var rowBlock = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
                    rowBlock.Inlines.Add(new Run { Text = $"  {row.Field}: ", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)), FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    rowBlock.Inlines.Add(new Run { Text = row.Value, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 174, 96)) });
                    _box.Children.Add(rowBlock);
                }
            });
        }

        public void DiffTable(string title, IEnumerable<Change> changes)
        {
            var items = (changes ?? Array.Empty<Change>()).Where(c => c != null).ToList();
            if (items.Count == 0) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _fullLog.AppendLine($"[{timestamp}] • {title}");
            foreach (var item in items)
            {
                _fullLog.AppendLine($"  {item.Field}: {item.Old} → {item.New}");
            }

            _box.DispatcherQueue.TryEnqueue(() =>
            {
                var titleBlock = new TextBlock
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                titleBlock.Inlines.Add(new Run { Text = "• ", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 208, 70)) });
                titleBlock.Inlines.Add(new Run { Text = title, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)) });
                _box.Children.Add(titleBlock);

                foreach (var c in items)
                {
                    var rowBlock = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
                    rowBlock.Inlines.Add(new Run { Text = $"  {c.Field ?? ""}: ", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)), FontWeight = Microsoft.UI.Text.FontWeights.Bold });
                    rowBlock.Inlines.Add(new Run { Text = string.IsNullOrEmpty(c.Old) ? "—" : c.Old, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 57, 43)) });
                    rowBlock.Inlines.Add(new Run { Text = " → ", Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 220, 220, 220)) });
                    rowBlock.Inlines.Add(new Run { Text = string.IsNullOrEmpty(c.New) ? "—" : c.New, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 39, 174, 96)) });
                    _box.Children.Add(rowBlock);
                }
            });
        }
    }

    public sealed class Change
    {
        public string Field { get; set; } = "";
        public string Old { get; set; } = "";
        public string New { get; set; } = "";

        public Change() { }
        public Change(string field, string oldV, string newV)
        { Field = field; Old = oldV; New = newV; }
    }
}