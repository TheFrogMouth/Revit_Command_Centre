using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.UI;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Revit_Command_Centre.Modules.SheetsAndDisciplines
{
    /// <summary>
    /// Code-behind for the Sheets &amp; Disciplines module.
    /// Builds discipline toggle cards dynamically and keeps the sheet preview in sync.
    /// </summary>
    public partial class SheetsView : UserControl
    {
        private readonly UIApplication _uiApp;

        private readonly Picker _format    = new(new[] { "[Disc]-[Floor]-[Number]", "[Disc][Number]", "Custom" }, defaultIndex: 0);
        private readonly Picker _padding   = new(new[] { "2 digits", "3 digits" }, defaultIndex: 1);
        private readonly Picker _separator = new(new[] { "Dash", "Dot", "None" }, defaultIndex: 0);

        private static readonly (string Code, string Name)[] Disciplines =
        {
            ("A", "Architectural"),
            ("S", "Structural"),
            ("M", "Mechanical"),
            ("E", "Electrical"),
            ("P", "Plumbing"),
            ("C", "Civil"),
            ("F", "Fire Safety"),
        };

        // Tracks which disciplines are toggled on
        private readonly HashSet<string> _activeCodes = new(StringComparer.Ordinal) { "A", "S" };

        public SheetsView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PickerHelper.Refresh(CmbFormat,    _format,    RefreshPreview);
                PickerHelper.Refresh(CmbPadding,   _padding,   RefreshPreview);
                PickerHelper.Refresh(CmbSeparator, _separator, RefreshPreview);
                BuildDisciplineCards();
                RefreshPreview();
            };
        }

        // ──────────────────────────────────────  discipline cards  ────────────────────────────────

        /// <summary>Generates a toggle card for each discipline plus a custom (+) button.</summary>
        private void BuildDisciplineCards()
        {
            DisciplinesPanel.Children.Clear();

            foreach (var (code, name) in Disciplines)
            {
                bool active = _activeCodes.Contains(code);
                DisciplinesPanel.Children.Add(CreateDisciplineCard(code, name, active));
            }

            // Add custom discipline button
            var addCard = new Border
            {
                Width         = 110,
                Height        = 68,
                Margin        = new Thickness(0, 0, 8, 8),
                CornerRadius  = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush   = new SolidColorBrush(Color.FromRgb(0x1E, 0x00, 0x00)) { Opacity = 0.12 },
                Background    = new SolidColorBrush(Colors.White),
                Cursor        = Cursors.Hand,
            };
            var addContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            addContent.Children.Add(new TextBlock { Text = "+", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0x9B)) });
            addContent.Children.Add(new TextBlock { Text = "Add custom", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0x9B)) });
            addCard.Child = addContent;
            addCard.MouseLeftButtonUp += AddCustomDiscipline_Click;
            DisciplinesPanel.Children.Add(addCard);
        }

        private Border CreateDisciplineCard(string code, string name, bool active)
        {
            var activeBrush  = new SolidColorBrush(Color.FromRgb(0x18, 0x5F, 0xA5));
            var activeBgBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF1, 0xFB));
            var borderBrush  = new SolidColorBrush(Color.FromArgb(0x1E, 0x00, 0x00, 0x00));

            var card = new Border
            {
                Width           = 110,
                Height          = 68,
                Margin          = new Thickness(0, 0, 8, 8),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = active ? new Thickness(2) : new Thickness(1),
                BorderBrush     = active ? activeBrush : borderBrush,
                Background      = active ? activeBgBrush : new SolidColorBrush(Colors.White),
                Cursor          = Cursors.Hand,
                Tag             = code,
            };

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text       = code,
                FontSize   = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = active ? activeBrush : new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0x9B))
            });
            panel.Children.Add(new TextBlock
            {
                Text       = name,
                FontSize   = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth   = 90,
            });
            card.Child = panel;
            card.MouseLeftButtonUp += DisciplineCard_Toggle;
            return card;
        }

        private void DisciplineCard_Toggle(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border card || card.Tag is not string code) return;

            if (_activeCodes.Contains(code))
                _activeCodes.Remove(code);
            else
                _activeCodes.Add(code);

            BuildDisciplineCards();
            RefreshPreview();
        }

        private void AddCustomDiscipline_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new CustomDisciplineDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Code))
            {
                _activeCodes.Add(dlg.Code.ToUpperInvariant());
                BuildDisciplineCards();
                RefreshPreview();
            }
        }

        // ──────────────────────────────────────  public API  ─────────────────────────────────────

        /// <summary>Queues sheet generation in Revit's API event loop.</summary>
        public void GenerateSheets()
        {
            if (App.GenerateSheetsHandler == null || App.GenerateSheetsEvent == null) return;

            App.GenerateSheetsHandler.Disciplines = Disciplines
                .Where(d => _activeCodes.Contains(d.Code))
                .Select(d => (d.Code, d.Name))
                .ToList();
            App.GenerateSheetsHandler.Format    = _format.Value;
            App.GenerateSheetsHandler.Padding   = _padding.Value;
            App.GenerateSheetsHandler.Separator = _separator.Value;
            App.GenerateSheetsHandler.Floors    = ParseFloors(TxtFloors?.Text ?? string.Empty);
            App.GenerateSheetsEvent.Raise();
        }

        private static string[] ParseFloors(string input) =>
            string.IsNullOrWhiteSpace(input)
                ? Array.Empty<string>()
                : input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // ──────────────────────────────────────  sheet preview  ───────────────────────────────────

        private void Floors_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshPreview();
            if (TxtFloorsHint == null) return;
            string[] floors = ParseFloors(TxtFloors?.Text ?? string.Empty);
            TxtFloorsHint.Text = floors.Length > 0
                ? $"{_activeCodes.Count} discipline(s) × {floors.Length} floor(s) = {_activeCodes.Count * floors.Length} sheet(s)"
                : "Leave blank — one sheet set per discipline";
        }

        /// <summary>Regenerates the live preview table based on active disciplines and naming options.</summary>
        private void RefreshPreview()
        {
            if (PreviewList == null) return;

            string format    = _format.Value;
            string padding   = _padding.Value;
            string separator = _separator.Value;

            char sep = separator switch { "Dot" => '.', "None" => '\0', _ => '-' };
            int padLen = padding.StartsWith("2") ? 2 : 3;

            var rows = new List<SheetPreviewRow>();

            string[] floors = ParseFloors(TxtFloors?.Text ?? string.Empty);
            if (floors.Length == 0) floors = new[] { string.Empty };

            foreach (var (code, name) in Disciplines)
            {
                if (!_activeCodes.Contains(code)) continue;

                int num = 1;
                foreach (string floor in floors)
                {
                    string numStr    = num.ToString().PadLeft(padLen, '0');
                    string sheetCode = BuildCode(format, code, floor, numStr, sep);
                    string sheetName = string.IsNullOrEmpty(floor)
                        ? $"{name} — General Arrangement"
                        : $"{name} — {floor} General Arrangement";
                    rows.Add(new SheetPreviewRow { Code = sheetCode, Name = sheetName });
                    num++;
                }
            }

            PreviewList.ItemsSource = rows;

            bool hasRows = rows.Count > 0;
            TxtNoPreview.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string BuildCode(string format, string disc, string floor, string num, char sep)
        {
            string s = sep == '\0' ? string.Empty : sep.ToString();
            return format switch
            {
                "[Disc][Number]"            => $"{disc}{s}{num}",
                "[Disc]-[Floor]-[Number]"   => $"{disc}{s}{floor}{s}{num}",
                _                           => $"{disc}{s}{floor}{s}{num}"
            };
        }
    }

    // ──────────────────────────────────────  helper types  ────────────────────────────────────────

    internal class SheetPreviewRow
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    internal class CustomDisciplineDialog : Window
    {
        public string? Code { get; private set; }

        public CustomDisciplineDialog()
        {
            Title  = "Add Custom Discipline";
            Width  = 280;
            Height = 160;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Discipline code (e.g. L):", FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            var txtCode = new WpfTextBox { MaxLength = 3, FontSize = 14, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(txtCode);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button { Content = "Cancel", Width = 70, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += (_, _) => DialogResult = false;
            var btnOk = new Button { Content = "Add", Width = 70, Height = 28 };
            btnOk.Click += (_, _) => { Code = txtCode.Text.Trim(); DialogResult = true; };
            row.Children.Add(btnCancel);
            row.Children.Add(btnOk);
            panel.Children.Add(row);

            Content = panel;
        }
    }
}
