using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VMGenerator.Services
{
    public sealed class UiLogger
    {
        private readonly RichTextBox _box;

        private static readonly Brush FG = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        private static readonly Brush RED = new SolidColorBrush(Color.FromRgb(192, 57, 43));
        private static readonly Brush GREEN = new SolidColorBrush(Color.FromRgb(39, 174, 96));
        private static readonly Brush BLUE = new SolidColorBrush(Color.FromRgb(78, 161, 255));
        private static readonly Brush YELLOW = new SolidColorBrush(Color.FromRgb(255, 208, 70));
        private static readonly Brush HDR_BG = new SolidColorBrush(Color.FromRgb(46, 46, 46));
        private static readonly Brush GRID = new SolidColorBrush(Color.FromRgb(80, 80, 80));

        public UiLogger(RichTextBox box)
        {
            _box = box;
            _box.Document ??= new FlowDocument();
            _box.Document.PagePadding = new Thickness(0);
        }

        private void AddParagraph(IEnumerable<Inline> inlines)
        {
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var i in inlines) p.Inlines.Add(i);
            _box.Document.Blocks.Add(p);
            _box.ScrollToEnd();
        }

        private void Line(string prefix, string text, Brush color)
        {
            AddParagraph(new Inline[]
            {
                new Run(prefix){ Foreground = YELLOW },
                new Run(text){ Foreground = color }
            });
        }

        public void Info(string msg) => Line("[INFO] ", msg, FG);
        public void Warn(string msg) => Line("[WARN] ", msg, YELLOW);
        public void Error(string msg) => Line("[ERROR] ", msg, RED);
        public void Step(string msg) => Line("> ", msg, BLUE);

        public void Table(string title, IEnumerable<(string Field, string Value)> rows)
        {
            var list = rows?.ToList() ?? new();
            if (list.Count == 0) return;

            AddParagraph(new Inline[]{
                new Run("• "){Foreground = YELLOW},
                new Run(title){Foreground = FG, FontWeight = FontWeights.Bold}
            });

            double total = Math.Max(700, _box.ActualWidth - 32);
            double wField = 180, wVal = total - wField - 2;

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10) };
            table.Columns.Add(new TableColumn { Width = new GridLength(wField) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wVal) });
            var group = new TableRowGroup(); table.RowGroups.Add(group);

            group.Rows.Add(Row(
                Cell("Поле", FG, HDR_BG, bold: true, center: true, grid: true),
                Cell("Значение", FG, HDR_BG, bold: true, center: true, grid: true)
            ));
            foreach (var r in list)
                group.Rows.Add(Row(
                    Cell(r.Field, FG, null, bold: true, grid: true),
                    Cell(r.Value, GREEN, null, grid: true)
                ));
            _box.Document.Blocks.Add(table);
            _box.ScrollToEnd();

            static TableCell Cell(string text, Brush fg, Brush? bg, bool bold = false, bool center = false, bool grid = false)
            {
                var para = new Paragraph(new Run(text)) { Margin = new Thickness(6), TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, Foreground = fg };
                if (bold) para.FontWeight = FontWeights.Bold;
                var cell = new TableCell(para);
                if (bg != null) cell.Background = bg;
                if (grid) { cell.BorderBrush = GRID; cell.BorderThickness = new Thickness(0, 0, 1, 1); }
                return cell;
            }
            static TableRow Row(params TableCell[] cells) { var tr = new TableRow(); foreach (var c in cells) tr.Cells.Add(c); return tr; }
        }

        public void DiffTable(string title, IEnumerable<Change> changes)
        {
            var items = (changes ?? Array.Empty<Change>()).Where(c => c != null).ToList();
            if (items.Count == 0) return;

            AddParagraph(new Inline[]
            {
                new Run("• "){Foreground = YELLOW},
                new Run(title){Foreground = FG, FontWeight = FontWeights.Bold}
            });

            double total = Math.Max(600, _box.ActualWidth - 32);
            double wField = 140;
            double wValue = (total - wField - 2) / 2;

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10) };
            table.Columns.Add(new TableColumn { Width = new GridLength(wField) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wValue) });
            table.Columns.Add(new TableColumn { Width = new GridLength(wValue) });

            var group = new TableRowGroup();
            table.RowGroups.Add(group);

            group.Rows.Add(Row(
                Cell("Поле", FG, HDR_BG, bold: true, center: true, grid: true),
                Cell("Было", FG, HDR_BG, bold: true, center: true, grid: true),
                Cell("Стало", FG, HDR_BG, bold: true, center: true, grid: true)
            ));

            foreach (var c in items)
            {
                group.Rows.Add(Row(
                    Cell(c.Field ?? "", FG, null, bold: true, grid: true),
                    Cell(string.IsNullOrEmpty(c.Old) ? "—" : c.Old, RED, null, grid: true),
                    Cell(string.IsNullOrEmpty(c.New) ? "—" : c.New, GREEN, null, grid: true)
                ));
            }

            _box.Document.Blocks.Add(table);
            _box.ScrollToEnd();

            static TableCell Cell(string text, Brush fg, Brush? bg, bool bold = false, bool center = false, bool grid = false)
            {
                var para = new Paragraph(new Run(text))
                {
                    Margin = new Thickness(6),
                    TextAlignment = center ? TextAlignment.Center : TextAlignment.Left
                };
                para.Foreground = fg;
                if (bold) para.FontWeight = FontWeights.Bold;

                var cell = new TableCell(para);
                if (bg != null) cell.Background = bg;
                if (grid)
                {
                    cell.BorderBrush = GRID;
                    cell.BorderThickness = new Thickness(0, 0, 1, 1);
                }
                return cell;
            }

            static TableRow Row(params TableCell[] cells)
            {
                var r = new TableRow();
                foreach (var c in cells) r.Cells.Add(c);
                return r;
            }
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