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

        private int _aboveGround = 1;
        private int _belowGround = 0;

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

        private readonly HashSet<string> _activeCodes = new(StringComparer.Ordinal) { "A", "S" };

        // Frozen brushes
        private static readonly SolidColorBrush BrushBlue    = Freeze(0xFF, 0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush BrushBlueBg  = Freeze(0xFF, 0xE6, 0xF1, 0xFB);
        private static readonly SolidColorBrush BrushBorder  = Freeze(0x1E, 0x00, 0x00, 0x00);
        private static readonly SolidColorBrush BrushGrey    = Freeze(0xFF, 0x9B, 0x9B, 0x9B);
        private static readonly SolidColorBrush BrushMid     = Freeze(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush BrushWhite   = Freeze(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly FontFamily      AppFont      = new("Segoe UI");
        private static readonly FontFamily      MonoFont     = new("Consolas");

        private static SolidColorBrush Freeze(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        public SheetsView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PickerHelper.Refresh(CmbFormat,    _format,    RefreshPreview);
                PickerHelper.Refresh(CmbPadding,   _padding,   RefreshPreview);
                PickerHelper.Refresh(CmbSeparator, _separator, RefreshPreview);
                BuildFloorCounters();
                BuildDisciplineCards();
                RefreshPreview();
            };
        }

        // ──────────────────────────────────────  floor counters  ──────────────────────────────────

        private void BuildFloorCounters()
        {
            BuildCounter(AboveGroundCounter, ref _aboveGround, min: 1, max: 50);
            BuildCounter(BelowGroundCounter, ref _belowGround, min: 0, max: 10);
            UpdateFloorCodesLabel();
        }

        private void BuildCounter(StackPanel panel, ref int value, int min, int max)
        {
            int captured = value; // closure capture

            panel.Children.Clear();
            panel.Orientation = Orientation.Horizontal;

            var btnMinus = CounterButton("−");
            var lblValue = new TextBlock
            {
                Text              = value.ToString(),
                Width             = 36,
                FontSize          = 14,
                FontFamily        = AppFont,
                FontWeight        = FontWeights.Medium,
                TextAlignment     = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = BrushBlue
            };
            var btnPlus = CounterButton("+");

            btnMinus.MouseLeftButtonUp += (_, _) =>
            {
                if (panel == AboveGroundCounter && _aboveGround > min) { _aboveGround--; lblValue.Text = _aboveGround.ToString(); }
                if (panel == BelowGroundCounter && _belowGround > min) { _belowGround--; lblValue.Text = _belowGround.ToString(); }
                UpdateFloorCodesLabel();
                RefreshPreview();
            };
            btnPlus.MouseLeftButtonUp += (_, _) =>
            {
                if (panel == AboveGroundCounter && _aboveGround < max) { _aboveGround++; lblValue.Text = _aboveGround.ToString(); }
                if (panel == BelowGroundCounter && _belowGround < max) { _belowGround++; lblValue.Text = _belowGround.ToString(); }
                UpdateFloorCodesLabel();
                RefreshPreview();
            };

            panel.Children.Add(btnMinus);
            panel.Children.Add(lblValue);
            panel.Children.Add(btnPlus);
        }

        private static Border CounterButton(string label) => new()
        {
            Width           = 28,
            Height          = 28,
            Cursor          = Cursors.Hand,
            CornerRadius    = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = BrushBorder,
            Background      = BrushWhite,
            Child = new TextBlock
            {
                Text              = label,
                FontSize          = 16,
                FontFamily        = AppFont,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = BrushMid
            }
        };

        private string[] GetFloors()
        {
            var floors = new List<string>();
            for (int i = _belowGround; i >= 1; i--)
                floors.Add($"B{i:D2}");
            floors.Add("GF");
            for (int i = 1; i < _aboveGround; i++)
                floors.Add($"L{i:D2}");
            return floors.ToArray();
        }

        private void UpdateFloorCodesLabel()
        {
            if (TxtFloorCodes == null) return;
            string[] floors = GetFloors();
            TxtFloorCodes.Text = string.Join(", ", floors);
        }

        // ──────────────────────────────────────  discipline cards  ────────────────────────────────

        private void BuildDisciplineCards()
        {
            DisciplinesPanel.Children.Clear();

            foreach (var (code, name) in Disciplines)
            {
                bool active = _activeCodes.Contains(code);
                DisciplinesPanel.Children.Add(CreateDisciplineCard(code, name, active));
            }

            var addCard = new Border
            {
                Width           = 110, Height = 68,
                Margin          = new Thickness(0, 0, 8, 8),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush     = BrushBorder,
                Background      = BrushWhite,
                Cursor          = Cursors.Hand,
            };
            var addContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            addContent.Children.Add(new TextBlock { Text = "+", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushGrey });
            addContent.Children.Add(new TextBlock { Text = "Add custom", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushGrey });
            addCard.Child = addContent;
            addCard.MouseLeftButtonUp += AddCustomDiscipline_Click;
            DisciplinesPanel.Children.Add(addCard);
        }

        private Border CreateDisciplineCard(string code, string name, bool active)
        {
            var card = new Border
            {
                Width           = 110, Height = 68,
                Margin          = new Thickness(0, 0, 8, 8),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = active ? new Thickness(2) : new Thickness(1),
                BorderBrush     = active ? BrushBlue : BrushBorder,
                Background      = active ? BrushBlueBg : BrushWhite,
                Cursor          = Cursors.Hand,
                Tag             = code,
            };

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = code, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = active ? BrushBlue : BrushGrey });
            panel.Children.Add(new TextBlock { Text = name, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushMid, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 90 });
            card.Child = panel;
            card.MouseLeftButtonUp += DisciplineCard_Toggle;
            return card;
        }

        private void DisciplineCard_Toggle(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border card || card.Tag is not string code) return;
            if (_activeCodes.Contains(code)) _activeCodes.Remove(code);
            else _activeCodes.Add(code);
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
            App.GenerateSheetsHandler.Floors    = GetFloors();
            App.GenerateSheetsEvent.Raise();
        }

        // ──────────────────────────────────────  sheet preview  ───────────────────────────────────

        private void RefreshPreview()
        {
            if (PreviewList == null) return;

            string format    = _format.Value;
            string padding   = _padding.Value;
            string separator = _separator.Value;

            char sep   = separator switch { "Dot" => '.', "None" => '\0', _ => '-' };
            int padLen = padding.StartsWith("2") ? 2 : 3;

            string[] floors = GetFloors();
            var rows = new List<SheetPreviewRow>();

            foreach (var (code, name) in Disciplines)
            {
                if (!_activeCodes.Contains(code)) continue;

                int num = 1;
                foreach (string floor in floors)
                {
                    string numStr    = num.ToString().PadLeft(padLen, '0');
                    string sheetCode = BuildCode(format, code, floor, numStr, sep);
                    string sheetName = floor == "GF" || floor.StartsWith("L") || floor.StartsWith("B")
                        ? $"{name} — {floor} General Arrangement"
                        : $"{name} — General Arrangement";
                    rows.Add(new SheetPreviewRow { Code = sheetCode, Name = sheetName });
                    num++;
                }
            }

            PreviewList.ItemsSource = rows;
            TxtNoPreview.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string BuildCode(string format, string disc, string floor, string num, char sep)
        {
            string s = sep == '\0' ? string.Empty : sep.ToString();
            return format switch
            {
                "[Disc][Number]" => $"{disc}{s}{num}",
                _                => $"{disc}{s}{floor}{s}{num}"
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
            Width  = 280; Height = 160;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Discipline code (e.g. L):", FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

            var txtCode = new WpfTextBox { MaxLength = 3, FontSize = 14, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(txtCode);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Border { Width = 70, Height = 28, Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1E, 0, 0, 0)), Background = Brushes.White, Cursor = Cursors.Hand, Child = new TextBlock { Text = "Cancel", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            btnCancel.MouseLeftButtonUp += (_, _) => DialogResult = false;
            var btnOk = new Border { Width = 70, Height = 28, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x5F, 0xA5)), Cursor = Cursors.Hand, Child = new TextBlock { Text = "Add", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            btnOk.MouseLeftButtonUp += (_, _) => { Code = txtCode.Text.Trim(); DialogResult = true; };
            row.Children.Add(btnCancel);
            row.Children.Add(btnOk);
            panel.Children.Add(row);
            Content = panel;
        }
    }
}
