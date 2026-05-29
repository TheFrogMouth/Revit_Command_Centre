using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre.Modules.ProjectSetup
{
    public partial class ProjectSetupView : UserControl
    {
        private readonly UIApplication _uiApp;
        private int  _selectedTier   = 2;
        private bool _isProjectSaved;

        private readonly Picker _language   = new(new[] { "English", "Nederlands", "Français" });
        private readonly Picker _titleBlock = new(new[] { "Standard A1", "Standard A3", "Custom" });

        private static readonly string[] TierLabels =
            { "", "Tier 1 — Standard", "Tier 2 — BIM Compliant", "Tier 3 — ISO 19650 Full" };

        // Brushes kept local to avoid importing Autodesk.Revit.DB (which conflicts with WPF Grid)
        private static readonly SolidColorBrush LockedBg    = Frozen(0xFF, 0xF5, 0xF5, 0xF5);
        private static readonly SolidColorBrush GreenFill   = Frozen(0xFF, 0x2E, 0xA4, 0x3E);
        private static readonly SolidColorBrush GreyFill    = Frozen(0xFF, 0xC0, 0xC0, 0xC0);
        private static readonly SolidColorBrush TxtPrimary  = Frozen(0xFF, 0x1A, 0x1A, 0x1A);
        private static readonly SolidColorBrush TxtSecond   = Frozen(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush BorderC     = Frozen(0x1E, 0x00, 0x00, 0x00);
        private static readonly FontFamily      AppFont     = new("Segoe UI");

        private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public ProjectSetupView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            var doc = uiApp.ActiveUIDocument?.Document;
            _isProjectSaved = doc != null && !string.IsNullOrEmpty(doc.PathName);
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsService.Load();
            if (!string.IsNullOrEmpty(settings.TitleBlockFolder))
                TxtTitleBlockFolder.Text = settings.TitleBlockFolder;

            BrowseTitleBlockFolderContainer.Children.Add(
                PickerHelper.MakeButton("Browse…", BrowseTitleBlockFolder_Click));

            PickerHelper.Refresh(CmbLanguage,   _language,   UpdatePreview);
            PickerHelper.Refresh(CmbTitleBlock, _titleBlock, UpdatePreview);
            ApplyTierStyles();

            if (_isProjectSaved)
            {
                LockField(TxtProjectName,   LblProjectName);
                LockField(TxtProjectNumber, LblProjectNumber);

                var doc = _uiApp.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    TxtProjectName.Text   = doc.ProjectInformation.Name;
                    TxtProjectNumber.Text = doc.ProjectInformation.Number;
                    TxtClientName.Text    = doc.ProjectInformation.ClientName;

                    // Restore picker state from saved config
                    ProjectConfig? config = null;
                    try { config = ExtensibleStorageService.ReadConfig(doc); } catch { }
                    if (config == null && !string.IsNullOrEmpty(doc.PathName))
                        config = ConfigService.LoadConfig(doc.PathName);
                    if (config != null)
                    {
                        SetPickerByValue(_language,   CmbLanguage,   config.Language,   "English");
                        SetPickerByValue(_titleBlock, CmbTitleBlock, config.TitleBlock,  "Standard A1");
                        _selectedTier = Math.Clamp(config.ComplianceTier, 1, 3);
                        ApplyTierStyles();
                    }
                }
            }

            PopulateWorksets();
            UpdatePreview();
        }

        private static void LockField(TextBox box, TextBlock label)
        {
            box.IsReadOnly        = true;
            box.IsHitTestVisible  = false;
            box.Background        = LockedBg;
            box.Cursor            = Cursors.Arrow;
            box.ToolTip           = "Locked — project identity cannot change after the project is saved to disk.";
            label.Text            = label.Text + " (LOCKED)";
        }

        // ───────────────────────────────────  worksets  ───────────────────────────────────

        private void PopulateWorksets()
        {
            WorksetActionContainer.Children.Clear();
            WorksetsList.Children.Clear();

            var doc = _uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TxtWorksetsStatus.Text = "No project open.";
                return;
            }

            if (!doc.IsWorkshared)
            {
                TxtWorksetsStatus.Text = "Worksharing is not enabled on this project.";
                return;
            }

            TxtWorksetsStatus.Text = string.Empty;
            WorksetActionContainer.Children.Add(
                PickerHelper.MakeButton("+ Add workset", ShowAddWorksetPanel_Click));

            var worksets = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.Workset))
                .Cast<Autodesk.Revit.DB.Workset>()
                .Where(ws => ws.Kind == Autodesk.Revit.DB.WorksetKind.UserCreated)
                .OrderBy(ws => ws.Name)
                .ToList();

            if (worksets.Count == 0)
            {
                TxtWorksetsStatus.Text = "No user worksets defined yet.";
                return;
            }

            foreach (var ws in worksets)
                WorksetsList.Children.Add(BuildWorksetRow(ws.Name, ws.Owner, ws.IsOpen));
        }

        private UIElement BuildWorksetRow(string name, string owner, bool isOpen)
        {
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameTb = new TextBlock
            {
                Text = name, FontFamily = AppFont, FontSize = 12,
                Foreground = TxtPrimary, VerticalAlignment = VerticalAlignment.Center
            };
            var ownerTb = new TextBlock
            {
                Text = string.IsNullOrEmpty(owner) ? "—" : owner,
                FontFamily = AppFont, FontSize = 11, Foreground = TxtSecond,
                VerticalAlignment = VerticalAlignment.Center
            };
            var dot = new Ellipse
            {
                Width = 8, Height = 8, Fill = isOpen ? GreenFill : GreyFill,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = isOpen ? "Open" : "Closed"
            };

            Grid.SetColumn(nameTb,  0);
            Grid.SetColumn(ownerTb, 1);
            Grid.SetColumn(dot,     2);
            grid.Children.Add(nameTb);
            grid.Children.Add(ownerTb);
            grid.Children.Add(dot);

            return new Border
            {
                BorderBrush = BorderC, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }

        private void ShowAddWorksetPanel_Click(object sender, MouseButtonEventArgs e)
        {
            WorksetActionContainer.Children.Clear();

            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            // Template chips (exclude worksets already in the project)
            var settings = AppSettingsService.Load();
            var doc      = _uiApp.ActiveUIDocument?.Document;
            var existing = doc != null && doc.IsWorkshared
                ? new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Workset))
                    .Cast<Autodesk.Revit.DB.Workset>()
                    .Select(ws => ws.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var available = settings.WorksetTemplates?.Where(t => !existing.Contains(t)).ToList()
                            ?? new List<string>();

            if (available.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "SUGGESTED", FontFamily = AppFont, FontSize = 10,
                    FontWeight = FontWeights.SemiBold, Foreground = TxtSecond,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var t in available)
                {
                    var capture = t;
                    var chip = new Border
                    {
                        Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4),
                        CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                        BorderBrush = BorderC, BorderThickness = new Thickness(1),
                        Background = Brushes.White,
                        Child = new TextBlock { Text = capture, FontFamily = AppFont, FontSize = 11, Foreground = TxtPrimary }
                    };
                    chip.MouseLeftButtonUp += (s, ev) => RaiseAddWorkset(capture, panel);
                    chips.Children.Add(chip);
                }
                panel.Children.Add(chips);
            }

            panel.Children.Add(new TextBlock
            {
                Text = available.Count > 0 ? "OR ENTER CUSTOM NAME" : "WORKSET NAME",
                FontFamily = AppFont, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = TxtSecond, Margin = new Thickness(0, 0, 0, 6)
            });

            var input = new TextBox
            {
                Height = 30, Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = AppFont, FontSize = 12, Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(input);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(PickerHelper.MakeButton("Add", (object s, MouseButtonEventArgs ev) =>
            {
                var name = input.Text.Trim();
                if (!string.IsNullOrEmpty(name)) RaiseAddWorkset(name, panel);
            }));
            btnRow.Children.Add(PickerHelper.MakeButton("Cancel", (object s, MouseButtonEventArgs ev) =>
            {
                WorksetsList.Children.Remove(panel);
                WorksetActionContainer.Children.Add(
                    PickerHelper.MakeButton("+ Add workset", ShowAddWorksetPanel_Click));
            }));
            panel.Children.Add(btnRow);

            WorksetsList.Children.Add(panel);
        }

        private void RaiseAddWorkset(string name, StackPanel addPanel)
        {
            if (App.AddWorksetHandler == null || App.AddWorksetEvent == null) return;
            App.AddWorksetHandler.WorksetName = name;
            App.AddWorksetEvent.Raise();
            WorksetsList.Children.Remove(addPanel);
            WorksetActionContainer.Children.Clear();
            WorksetActionContainer.Children.Add(
                PickerHelper.MakeButton("+ Add workset", ShowAddWorksetPanel_Click));
        }

        // ────────────────────────────────  existing methods  ────────────────────────────────

        private void BrowseTitleBlockFolder_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder containing title block .rfa files"
            };
            if (dlg.ShowDialog() != true) return;
            TxtTitleBlockFolder.Text = dlg.FolderName;
            var settings = AppSettingsService.Load();
            settings.TitleBlockFolder = dlg.FolderName;
            AppSettingsService.Save(settings);
        }

        private void TierCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && int.TryParse(border.Tag?.ToString(), out int tier))
            {
                _selectedTier = tier;
                ApplyTierStyles();
                UpdatePreview();
            }
        }

        private void ApplyTierStyles()
        {
            var normal   = (Style)FindResource("CardStyle");
            var selected = (Style)FindResource("CardSelectedStyle");
            CardTier1.Style = _selectedTier == 1 ? selected : normal;
            CardTier2.Style = _selectedTier == 2 ? selected : normal;
            CardTier3.Style = _selectedTier == 3 ? selected : normal;
        }

        private void Preview_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            PreviewClient.Text   = string.IsNullOrWhiteSpace(TxtClientName.Text)    ? "—" : TxtClientName.Text;
            PreviewProject.Text  = string.IsNullOrWhiteSpace(TxtProjectName.Text)   ? "—" : TxtProjectName.Text;
            PreviewNumber.Text   = string.IsNullOrWhiteSpace(TxtProjectNumber.Text) ? "—" : TxtProjectNumber.Text;
            PreviewLanguage.Text = _language.Value;
            PreviewTier.Text     = TierLabels[_selectedTier];
        }

        public void LoadConfig(ProjectConfig config)
        {
            TxtClientName.Text = config.ClientName;
            if (!_isProjectSaved)
            {
                TxtProjectName.Text   = config.ProjectName;
                TxtProjectNumber.Text = config.ProjectNumber;
            }
            SetPickerByValue(_language,   CmbLanguage,   config.Language,   "English");
            SetPickerByValue(_titleBlock, CmbTitleBlock, config.TitleBlock,  "Standard A1");
            _selectedTier = Math.Clamp(config.ComplianceTier, 1, 3);
            ApplyTierStyles();
            UpdatePreview();
        }

        public ProjectConfig BuildConfig() => new()
        {
            ClientName     = TxtClientName.Text.Trim(),
            ProjectName    = TxtProjectName.Text.Trim(),
            ProjectNumber  = TxtProjectNumber.Text.Trim(),
            Language       = _language.Value,
            TitleBlock     = _titleBlock.Value,
            ComplianceTier = _selectedTier,
            IfcSchema      = _selectedTier >= 2 ? "IFC4" : string.Empty,
            CobieEnabled   = _selectedTier >= 3,
            LastModified   = DateTime.UtcNow
        };

        private void SetPickerByValue(Picker picker, StackPanel panel, string value, string fallback)
        {
            for (int i = 0; i < picker.Options.Length; i++)
            {
                if (picker.Options[i].Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    picker.Index = i;
                    PickerHelper.Refresh(panel, picker, UpdatePreview);
                    return;
                }
            }
            picker.Index = 0;
            PickerHelper.Refresh(panel, picker, UpdatePreview);
        }
    }
}
