using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Services;
using Revit_Command_Centre.UI;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Revit_Command_Centre.Modules.SheetsAndDisciplines
{
    public partial class SheetsView : UserControl
    {
        private readonly UIApplication _uiApp;

        private readonly Picker _format    = new(new[] { "[Disc]-[Floor]-[Number]", "[Disc][Number]", "Custom" }, defaultIndex: 0);
        private readonly Picker _padding   = new(new[] { "2 digits", "3 digits" }, defaultIndex: 1);
        private readonly Picker _separator = new(new[] { "Dash", "Dot", "None" }, defaultIndex: 0);

        private int _aboveGround = 1;
        private int _belowGround = 0;

        // Floor label config
        private string _groundLabel = "GF";
        private string _abovePrefix = "L";
        private string _belowPrefix = "B";

        // Per-discipline number ranges: Code → (StartNumber, EndNumber?)
        private readonly Dictionary<string, (int Start, int? End)> _ranges = new();

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

        // Current model sheets
        private List<SheetRow> _modelSheets       = new();
        private List<string>   _pendingDeleteNumbers = new();
        private Border?        _confirmBtn;

        // Frozen brushes
        private static readonly SolidColorBrush BrushBlue     = Freeze(0xFF, 0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush BrushBlueBg   = Freeze(0xFF, 0xE6, 0xF1, 0xFB);
        private static readonly SolidColorBrush BrushBorder   = Freeze(0x1E, 0x00, 0x00, 0x00);
        private static readonly SolidColorBrush BrushGrey     = Freeze(0xFF, 0x9B, 0x9B, 0x9B);
        private static readonly SolidColorBrush BrushMid      = Freeze(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush BrushWhite    = Freeze(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly SolidColorBrush BrushDanger   = Freeze(0xFF, 0xDC, 0x35, 0x45);
        private static readonly SolidColorBrush BrushDisabled = Freeze(0xFF, 0xCC, 0xCC, 0xCC);
        private static readonly FontFamily      AppFont       = new("Segoe UI");

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
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PickerHelper.Refresh(CmbFormat,    _format,    RefreshPreview);
            PickerHelper.Refresh(CmbPadding,   _padding,   RefreshPreview);
            PickerHelper.Refresh(CmbSeparator, _separator, RefreshPreview);

            BuildFloorCounters();
            BuildDisciplineCards();
            BuildNumberRangesPanel();
            BuildModelSheetsGrid();
            RefreshPreview();

            RefreshSheetsContainer.Children.Add(
                PickerHelper.MakeButton("Refresh", RefreshSheets_Click));
            RemoveSheetsContainer.Children.Add(
                PickerHelper.MakeButton("Remove selected", RemoveSheets_Click));
            CancelDeleteContainer.Children.Add(
                PickerHelper.MakeButton("Cancel", CancelDelete_Click));

            _confirmBtn = PickerHelper.MakeButton("Delete", ConfirmDelete_Click);
            SetConfirmEnabled(false);
            ConfirmDeleteContainer.Children.Add(_confirmBtn);

            FetchModelSheets();
        }

        // ──────────────────────────────────────  floor counters  ─────────────────────────

        private void BuildFloorCounters()
        {
            BuildCounter(AboveGroundCounter, isAbove: true);
            BuildCounter(BelowGroundCounter, isAbove: false);
            UpdateFloorCodesLabel();
        }

        private void BuildCounter(StackPanel panel, bool isAbove)
        {
            panel.Children.Clear();
            panel.Orientation = Orientation.Horizontal;

            var lbl = new TextBlock
            {
                Text              = isAbove ? _aboveGround.ToString() : _belowGround.ToString(),
                Width             = 36,
                FontSize          = 14,
                FontFamily        = AppFont,
                FontWeight        = FontWeights.Medium,
                TextAlignment     = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = BrushBlue
            };

            var btnMinus = CounterButton("−");
            var btnPlus  = CounterButton("+");

            btnMinus.MouseLeftButtonUp += (_, _) =>
            {
                if (isAbove  && _aboveGround > 1)  { _aboveGround--; lbl.Text = _aboveGround.ToString(); }
                if (!isAbove && _belowGround > 0)  { _belowGround--; lbl.Text = _belowGround.ToString(); }
                UpdateFloorCodesLabel();
                RefreshPreview();
            };
            btnPlus.MouseLeftButtonUp += (_, _) =>
            {
                if (isAbove  && _aboveGround < 50) { _aboveGround++; lbl.Text = _aboveGround.ToString(); }
                if (!isAbove && _belowGround < 10) { _belowGround++; lbl.Text = _belowGround.ToString(); }
                UpdateFloorCodesLabel();
                RefreshPreview();
            };

            panel.Children.Add(btnMinus);
            panel.Children.Add(lbl);
            panel.Children.Add(btnPlus);
        }

        private static Border CounterButton(string label) => new()
        {
            Width = 28, Height = 28, Cursor = Cursors.Hand,
            CornerRadius    = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush     = BrushBorder,
            Background      = BrushWhite,
            Child = new TextBlock
            {
                Text = label, FontSize = 16, FontFamily = new FontFamily("Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Foreground          = BrushMid
            }
        };

        private string[] GetFloors()
        {
            var floors = new List<string>();
            for (int i = _belowGround; i >= 1; i--)
                floors.Add($"{_belowPrefix}{i:D2}");
            floors.Add(_groundLabel);
            for (int i = 1; i < _aboveGround; i++)
                floors.Add($"{_abovePrefix}{i:D2}");
            return floors.ToArray();
        }

        private void UpdateFloorCodesLabel()
        {
            if (TxtFloorCodes == null) return;
            TxtFloorCodes.Text = string.Join("  ", GetFloors());
        }

        private void FloorLabel_Changed(object sender, TextChangedEventArgs e)
        {
            _groundLabel = string.IsNullOrWhiteSpace(TxtGroundLabel?.Text) ? "GF" : TxtGroundLabel.Text.Trim();
            _abovePrefix = string.IsNullOrWhiteSpace(TxtAbovePrefix?.Text) ? "L"  : TxtAbovePrefix.Text.Trim();
            _belowPrefix = string.IsNullOrWhiteSpace(TxtBelowPrefix?.Text) ? "B"  : TxtBelowPrefix.Text.Trim();
            UpdateFloorCodesLabel();
            RefreshPreview();
        }

        // ──────────────────────────────────────  discipline cards  ───────────────────────

        private void BuildDisciplineCards()
        {
            DisciplinesPanel.Children.Clear();

            foreach (var (code, name) in Disciplines)
            {
                bool   active = _activeCodes.Contains(code);
                Border card   = MakeDisciplineCard(code, name, active);
                string cap    = code;
                card.MouseLeftButtonUp += (_, _) =>
                {
                    if (_activeCodes.Contains(cap)) _activeCodes.Remove(cap);
                    else _activeCodes.Add(cap);
                    BuildDisciplineCards();
                    BuildNumberRangesPanel();
                    RefreshPreview();
                };
                DisciplinesPanel.Children.Add(card);
            }

            var addCard = new Border
            {
                Width = 110, Height = 68, Margin = new Thickness(0, 0, 8, 8),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush     = BrushBorder,
                Background      = BrushWhite,
                Cursor          = Cursors.Hand,
                Child = new StackPanel
                {
                    VerticalAlignment   = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "+", FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushGrey },
                        new TextBlock { Text = "Add custom", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushGrey }
                    }
                }
            };
            addCard.MouseLeftButtonUp += AddCustomDiscipline_Click;
            DisciplinesPanel.Children.Add(addCard);
        }

        private static Border MakeDisciplineCard(string code, string name, bool active) => new()
        {
            Width = 110, Height = 68, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius    = new CornerRadius(6),
            BorderThickness = active ? new Thickness(2) : new Thickness(1),
            BorderBrush     = active ? BrushBlue : BrushBorder,
            Background      = active ? BrushBlueBg : BrushWhite,
            Cursor          = Cursors.Hand,
            Tag             = code,
            Child = new StackPanel
            {
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = code, FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = active ? BrushBlue : BrushGrey },
                    new TextBlock { Text = name, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = BrushMid, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 90 }
                }
            }
        };

        private void AddCustomDiscipline_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new CustomDisciplineDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Code))
            {
                _activeCodes.Add(dlg.Code.ToUpperInvariant());
                BuildDisciplineCards();
                BuildNumberRangesPanel();
                RefreshPreview();
            }
        }

        // ──────────────────────────────────────  number ranges  ─────────────────────────

        private void BuildNumberRangesPanel()
        {
            NumberRangesPanel.Children.Clear();

            var active = Disciplines.Where(d => _activeCodes.Contains(d.Code)).ToList();
            NumberRangesCard.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (active.Count == 0) return;

            // Header row
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            SetColumns(header, 140, -1, 12, -1);
            SetCol(header, SmallLabel("DISCIPLINE"), 0);
            SetCol(header, SmallLabel("START NUMBER"), 1);
            SetCol(header, SmallLabel("END NUMBER  (blank = unlimited)"), 3);
            NumberRangesPanel.Children.Add(header);

            foreach (var (code, name) in active)
            {
                if (!_ranges.ContainsKey(code)) _ranges[code] = (1, null);
                var (start, end) = _ranges[code];
                string cap = code;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                SetColumns(row, 140, -1, 12, -1);

                var txtStart = new WpfTextBox
                {
                    Text = start.ToString(), FontSize = 11, Padding = new Thickness(6, 4, 6, 4),
                    BorderBrush = BrushBorder, BorderThickness = new Thickness(1)
                };
                txtStart.TextChanged += (_, _) =>
                {
                    if (int.TryParse(txtStart.Text, out int v) && v >= 1)
                        _ranges[cap] = (v, _ranges.GetValueOrDefault(cap).End);
                    RefreshPreview();
                };

                var txtEnd = new WpfTextBox
                {
                    Text = end?.ToString() ?? "", FontSize = 11, Padding = new Thickness(6, 4, 6, 4),
                    BorderBrush = BrushBorder, BorderThickness = new Thickness(1)
                };
                txtEnd.TextChanged += (_, _) =>
                {
                    int? endVal = int.TryParse(txtEnd.Text, out int v) ? v : (int?)null;
                    _ranges[cap] = (_ranges.GetValueOrDefault(cap).Start, endVal);
                    RefreshPreview();
                };

                SetCol(row, new TextBlock { Text = $"{code}  {name}", FontSize = 11, Foreground = BrushBlue, VerticalAlignment = VerticalAlignment.Center }, 0);
                SetCol(row, txtStart, 1);
                SetCol(row, txtEnd,   3);
                NumberRangesPanel.Children.Add(row);
            }
        }

        private static void SetColumns(Grid g, params int[] widths)
        {
            foreach (int w in widths)
                g.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w)
                });
        }

        private static void SetCol(Grid g, UIElement el, int col)
        {
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        private static TextBlock SmallLabel(string text) => new()
        {
            Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B)),
            Margin = new Thickness(0, 0, 0, 2)
        };

        // ──────────────────────────────────────  public API  ─────────────────────────────

        public void GenerateSheets()
        {
            if (App.GenerateSheetsHandler == null || App.GenerateSheetsEvent == null) return;

            App.GenerateSheetsHandler.Mode = GenerateSheetsEventHandler.OperationMode.Generate;
            App.GenerateSheetsHandler.Disciplines = Disciplines
                .Where(d => _activeCodes.Contains(d.Code))
                .Select(d => (d.Code, d.Name))
                .ToList();
            App.GenerateSheetsHandler.Format       = _format.Value;
            App.GenerateSheetsHandler.Padding      = _padding.Value;
            App.GenerateSheetsHandler.Separator    = _separator.Value;
            App.GenerateSheetsHandler.Floors       = GetFloors();
            App.GenerateSheetsHandler.NumberRanges = new Dictionary<string, (int Start, int? End)>(_ranges);
            App.GenerateSheetsEvent.Raise();
        }

        // ──────────────────────────────────────  sheet preview  ──────────────────────────

        private void RefreshPreview()
        {
            if (PreviewList == null) return;

            char     sep    = _separator.Value switch { "Dot" => '.', "None" => '\0', _ => '-' };
            int      padLen = _padding.Value.StartsWith("2") ? 2 : 3;
            string[] floors = GetFloors();
            var      rows   = new List<SheetPreviewRow>();

            foreach (var (code, name) in Disciplines)
            {
                if (!_activeCodes.Contains(code)) continue;
                var (startNum, endNum) = _ranges.GetValueOrDefault(code, (1, null));
                int num = startNum;

                foreach (string floor in floors)
                {
                    if (endNum.HasValue && num > endNum.Value) break;
                    string numStr = num.ToString().PadLeft(padLen, '0');
                    string code_  = BuildCode(_format.Value, code, floor, numStr, sep);
                    rows.Add(new SheetPreviewRow { Code = code_, Name = $"{name} — {floor} General Arrangement" });
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

        // ──────────────────────────────────────  model sheets grid  ──────────────────────

        private void BuildModelSheetsGrid()
        {
            ModelSheetsGrid.Columns.Clear();
            ModelSheetsGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Sheet Number",
                Binding = new Binding("Number") { Mode = BindingMode.OneWay },
                Width   = new DataGridLength(130)
            });
            ModelSheetsGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Sheet Name",
                Binding = new Binding("Name") { Mode = BindingMode.OneWay },
                Width   = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        private void FetchModelSheets()
        {
            if (App.GenerateSheetsHandler == null || App.GenerateSheetsEvent == null) return;
            App.GenerateSheetsHandler.Mode           = GenerateSheetsEventHandler.OperationMode.FetchSheets;
            App.GenerateSheetsHandler.OnSheetsLoaded = OnSheetsLoaded;
            App.GenerateSheetsEvent.Raise();
            TxtSheetsStatus.Text = "Loading…";
        }

        private void OnSheetsLoaded(List<(string Number, string Name)> sheets)
        {
            _modelSheets = sheets
                .Select(s => new SheetRow { Number = s.Number, Name = s.Name })
                .ToList();
            ModelSheetsGrid.ItemsSource = null;
            ModelSheetsGrid.ItemsSource = _modelSheets;
            TxtSheetsStatus.Text = $"{_modelSheets.Count} sheet{(_modelSheets.Count == 1 ? "" : "s")} in model.";
            TxtSelectionCount.Text = "";
        }

        private void RefreshSheets_Click(object sender, MouseButtonEventArgs e) => FetchModelSheets();

        private void ModelSheetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int n = ModelSheetsGrid.SelectedItems.Count;
            TxtSelectionCount.Text = n > 0 ? $"{n} row{(n == 1 ? "" : "s")} selected" : "";
        }

        // ──────────────────────────────────────  delete flow  ────────────────────────────

        private void RemoveSheets_Click(object sender, MouseButtonEventArgs e)
        {
            var selected = ModelSheetsGrid.SelectedItems.Cast<SheetRow>().ToList();
            if (selected.Count == 0)
            {
                TxtSheetsStatus.Text = "Select one or more rows first.";
                return;
            }

            _pendingDeleteNumbers = selected.Select(r => r.Number).ToList();
            TxtDeletePrompt.Text =
                $"You are about to permanently delete {selected.Count} " +
                $"sheet{(selected.Count == 1 ? "" : "s")} from the model. " +
                "This cannot be undone.\n\nType DELETE SHEETS to confirm:";
            TxtDeleteConfirm.Text = "";
            SetConfirmEnabled(false);
            DeleteConfirmPanel.Visibility = Visibility.Visible;
        }

        private void TxtDeleteConfirm_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetConfirmEnabled(TxtDeleteConfirm.Text == "DELETE SHEETS");
        }

        private void SetConfirmEnabled(bool enabled)
        {
            if (_confirmBtn == null) return;
            _confirmBtn.Background = enabled ? BrushDanger   : BrushDisabled;
            _confirmBtn.Cursor     = enabled ? Cursors.Hand   : Cursors.Arrow;
        }

        private void CancelDelete_Click(object sender, MouseButtonEventArgs e)
        {
            DeleteConfirmPanel.Visibility = Visibility.Collapsed;
            TxtDeleteConfirm.Text = "";
            _pendingDeleteNumbers.Clear();
        }

        private void ConfirmDelete_Click(object sender, MouseButtonEventArgs e)
        {
            if (TxtDeleteConfirm.Text != "DELETE SHEETS") return;
            DeleteConfirmPanel.Visibility = Visibility.Collapsed;

            if (App.GenerateSheetsHandler == null || App.GenerateSheetsEvent == null) return;
            App.GenerateSheetsHandler.Mode             = GenerateSheetsEventHandler.OperationMode.DeleteSheets;
            App.GenerateSheetsHandler.SheetsToDelete   = new List<string>(_pendingDeleteNumbers);
            App.GenerateSheetsHandler.OnDeleteComplete = OnDeleteComplete;
            _pendingDeleteNumbers.Clear();

            if (App.GenerateSheetsEvent.Raise() != ExternalEventRequest.Accepted)
                TxtSheetsStatus.Text = "Could not queue delete — another operation is in progress.";
            else
                TxtSheetsStatus.Text = "Deleting…";
        }

        private void OnDeleteComplete(int deleted)
        {
            TxtSheetsStatus.Text = $"Deleted {deleted} sheet{(deleted == 1 ? "" : "s")}.";
            FetchModelSheets();
        }
    }

    // ──────────────────────────────────────  helper types  ───────────────────────────────

    internal class SheetRow
    {
        public string Number { get; set; } = string.Empty;
        public string Name   { get; set; } = string.Empty;
    }

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
            ResizeMode            = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = "Discipline code (e.g. L):", FontSize = 12, Margin = new Thickness(0, 0, 0, 6)
            });

            var txtCode = new WpfTextBox
            {
                MaxLength = 3, FontSize = 14, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txtCode);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnCancel = new Border
            {
                Width = 70, Height = 28, Margin = new Thickness(0, 0, 8, 0),
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x1E, 0, 0, 0)),
                Background      = Brushes.White,
                Cursor          = Cursors.Hand,
                Child = new TextBlock { Text = "Cancel", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            btnCancel.MouseLeftButtonUp += (_, _) => DialogResult = false;

            var btnOk = new Border
            {
                Width = 70, Height = 28, CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x5F, 0xA5)),
                Cursor     = Cursors.Hand,
                Child = new TextBlock { Text = "Add", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            btnOk.MouseLeftButtonUp += (_, _) => { Code = txtCode.Text.Trim(); DialogResult = true; };

            row.Children.Add(btnCancel);
            row.Children.Add(btnOk);
            panel.Children.Add(row);
            Content = panel;
        }
    }
}
