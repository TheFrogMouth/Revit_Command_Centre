using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Modules.CreateFamilies;
using Revit_Command_Centre.Modules.ProjectSetup;
using Revit_Command_Centre.Modules.SheetsAndDisciplines;
using Revit_Command_Centre.Modules.UpdateFamilies;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre.UI
{
    /// <summary>
    /// Main BIM Command Centre panel hosted inside Revit's dockable pane framework.
    /// UI is built entirely in code — no XAML resources, ControlTemplates, or Triggers —
    /// to avoid the 0xc0000005 crash that occurs when WPF's style engine compiles
    /// complex templates inside Revit's rendering context on this AMD GPU.
    /// </summary>
    public partial class MainView : UserControl
    {
        // ── frozen brushes (safe to share across threads) ─────────────────────
        private static readonly SolidColorBrush PrimaryBlue   = Freeze(0xFF, 0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush SidebarBg     = Freeze(0xFF, 0xF8, 0xF8, 0xF8);
        private static readonly SolidColorBrush BorderC       = Freeze(0x1E, 0x00, 0x00, 0x00);
        private static readonly SolidColorBrush TextPrimary   = Freeze(0xFF, 0x1A, 0x1A, 0x1A);
        private static readonly SolidColorBrush TextSecondary = Freeze(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush TextTertiary  = Freeze(0xFF, 0x9B, 0x9B, 0x9B);
        private static readonly SolidColorBrush WarningAmber  = Freeze(0xFF, 0xBA, 0x75, 0x17);
        private static readonly SolidColorBrush HoverBg       = Freeze(0xFF, 0xF0, 0xF0, 0xF0);
        private static readonly SolidColorBrush ContentBg     = Freeze(0xFF, 0xFA, 0xFA, 0xFA);

        private static SolidColorBrush Freeze(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static readonly FontFamily AppFont = new("Segoe UI");

        // ── live UI references ────────────────────────────────────────────────
        private Border?        _activeNavItem;
        private UIApplication? _uiApp;
        private string         _cachedDocTitle   = "No document open";
        private string         _cachedProjNumber = "—";
        private string         _cachedDocPath    = string.Empty;

        private TextBlock      _txtPageTitle    = null!;
        private TextBlock      _txtPageSubtitle = null!;
        private TextBlock      _txtDocumentName = null!;
        private TextBlock      _txtProjectNumber = null!;
        private Ellipse        _docStatusDot    = null!;
        private StackPanel     _topbarButtons   = null!;
        private ContentControl _contentArea     = null!;

        private Border _btnProjectSetup   = null!;
        private Border _btnSheets         = null!;
        private Border _btnUpdateFamilies = null!;
        private Border _btnCreateFamilies = null!;

        private static readonly Dictionary<string, (string Title, string Subtitle)> PageMeta = new()
        {
            ["ProjectSetup"]   = ("Project Setup",        "Configure project information and compliance tier"),
            ["Sheets"]         = ("Sheets & Disciplines", "Define active disciplines and sheet naming convention"),
            ["UpdateFamilies"] = ("Update Families",      "Add shared parameters to existing family files"),
            ["CreateFamilies"] = ("Create Families",      "Generate new families from templates"),
        };

        public MainView()
        {
            InitializeComponent(); // minimal — empty XAML root only
            BuildUI();
            Loaded += OnLoaded;
        }

        private UIApplication? UiApp => _uiApp;

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            Background = Brushes.White;

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            var divider = new Rectangle { Width = 1, Fill = BorderC };
            Grid.SetColumn(divider, 1);
            root.Children.Add(divider);

            var main = BuildMainContent();
            Grid.SetColumn(main, 2);
            root.Children.Add(main);

            Content = root;
        }

        private Grid BuildSidebar()
        {
            var grid = new Grid { Background = SidebarBg };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Logo
            var logo = new StackPanel();
            logo.Children.Add(new TextBlock { Text = "BIM",            FontFamily = AppFont, FontSize = 20, FontWeight = FontWeights.Bold,   Foreground = PrimaryBlue });
            logo.Children.Add(new TextBlock { Text = "Command Centre", FontFamily = AppFont, FontSize = 10, FontWeight = FontWeights.Normal, Foreground = TextTertiary });
            var logoBorder = new Border
            {
                Padding = new Thickness(16, 18, 16, 14),
                BorderBrush = BorderC, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = logo
            };
            Grid.SetRow(logoBorder, 0);
            grid.Children.Add(logoBorder);

            // Navigation
            var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            nav.Children.Add(SectionLabel("SETUP"));
            _btnProjectSetup   = NavItem("☰", "Project Setup",        "ProjectSetup",   active: true);
            _btnSheets         = NavItem("⊞", "Sheets & Disciplines", "Sheets",         active: false);
            nav.Children.Add(_btnProjectSetup);
            nav.Children.Add(_btnSheets);
            nav.Children.Add(SectionLabel("FAMILIES"));
            _btnUpdateFamilies = NavItem("↻", "Update Families",      "UpdateFamilies", active: false);
            _btnCreateFamilies = NavItem("✦", "Create Families",      "CreateFamilies", active: false);
            nav.Children.Add(_btnUpdateFamilies);
            nav.Children.Add(_btnCreateFamilies);
            Grid.SetRow(nav, 1);
            grid.Children.Add(nav);

            // Project chip
            _docStatusDot     = new Ellipse { Width = 8, Height = 8, Fill = WarningAmber, VerticalAlignment = VerticalAlignment.Center };
            _txtDocumentName  = new TextBlock { Text = "No document open", FontFamily = AppFont, FontSize = 11, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = TextPrimary };
            _txtProjectNumber = new TextBlock { Text = "—",                FontFamily = AppFont, FontSize = 10, FontWeight = FontWeights.Normal, Foreground = TextSecondary };

            var chipText = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            chipText.Children.Add(_txtDocumentName);
            chipText.Children.Add(_txtProjectNumber);

            var chipGrid = new Grid();
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_docStatusDot, 0);
            Grid.SetColumn(chipText,       1);
            chipGrid.Children.Add(_docStatusDot);
            chipGrid.Children.Add(chipText);

            var chip = new Border
            {
                Margin = new Thickness(10, 0, 10, 14), Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.White, CornerRadius = new CornerRadius(6),
                BorderBrush = BorderC, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Child = chipGrid
            };
            chip.MouseLeftButtonUp += ProjectChip_Click;
            Grid.SetRow(chip, 2);
            grid.Children.Add(chip);

            return grid;
        }

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text, FontFamily = AppFont, FontSize = 10,
            FontWeight = FontWeights.SemiBold, Foreground = TextTertiary,
            Margin = new Thickness(16, 14, 0, 6)
        };

        private Border NavItem(string icon, string label, string tag, bool active)
        {
            var iconTb  = new TextBlock { Text = icon,  FontSize = 13, Width = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var labelTb = new TextBlock { Text = label, FontFamily = AppFont, FontSize = 12, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(iconTb);
            row.Children.Add(labelTb);

            var border = new Border
            {
                Tag = tag,
                Height = 36,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 0, 12, 0),
                Child = row
            };
            ApplyNavStyle(border, active);

            border.MouseLeftButtonUp += NavItem_Click;
            border.MouseEnter += (s, _) => { if (s is Border b && b != _activeNavItem) b.Background = HoverBg; };
            border.MouseLeave += (s, _) => { if (s is Border b && b != _activeNavItem) b.Background = Brushes.Transparent; };

            return border;
        }

        private static void ApplyNavStyle(Border border, bool active)
        {
            border.BorderThickness = new Thickness(3, 0, 0, 0);
            border.BorderBrush     = active ? PrimaryBlue : Brushes.Transparent;
            border.Background      = active ? Brushes.White : Brushes.Transparent;

            if (border.Child is StackPanel sp)
                foreach (UIElement el in sp.Children)
                    if (el is TextBlock tb)
                    {
                        tb.Foreground = active ? TextPrimary : TextSecondary;
                        tb.FontWeight = active ? FontWeights.Medium : FontWeights.Normal;
                    }
        }

        private Grid BuildMainContent()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _txtPageTitle    = new TextBlock { FontFamily = AppFont, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = TextPrimary };
            _txtPageSubtitle = new TextBlock { FontFamily = AppFont, FontSize = 11, FontWeight = FontWeights.Normal, Foreground = TextSecondary, Margin = new Thickness(0, 2, 0, 0) };

            var titleStack = new StackPanel();
            titleStack.Children.Add(_txtPageTitle);
            titleStack.Children.Add(_txtPageSubtitle);

            _topbarButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var topbarGrid = new Grid();
            topbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(titleStack,     0);
            Grid.SetColumn(_topbarButtons, 1);
            topbarGrid.Children.Add(titleStack);
            topbarGrid.Children.Add(_topbarButtons);

            var topbar = new Border
            {
                Padding = new Thickness(20, 14, 20, 14),
                BorderBrush = BorderC, BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.White,
                Child = topbarGrid
            };
            Grid.SetRow(topbar, 0);
            grid.Children.Add(topbar);

            _contentArea = new ContentControl();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = ContentBg,
                Content    = _contentArea
            };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            return grid;
        }

        // ── Loaded ────────────────────────────────────────────────────────────

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Construction and Loaded are called by Revit during document loading — a time
            // when the Revit API is unsafe to call and documents are not in a stable state.
            // Do NOT touch UIApplication, Document, or instantiate module views here.
            // All of that is deferred to Activate(), called only from LaunchCommand.Execute.
            _txtDocumentName.Text  = _cachedDocTitle;
            _txtProjectNumber.Text = _cachedProjNumber;
            _docStatusDot.Fill     = WarningAmber;
        }

        // Called by LaunchCommand.Execute after pane.Show() — safe Revit API context.
        public void Activate(UIApplication uiApp)
        {
            _uiApp = uiApp;

            try
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    _cachedDocTitle = doc.Title;
                    _cachedDocPath  = doc.PathName;
                }
            }
            catch { }

            _txtDocumentName.Text  = _cachedDocTitle;
            _txtProjectNumber.Text = _cachedProjNumber;

            if (_activeNavItem == null)
                ActivateView("ProjectSetup", _btnProjectSetup);
        }

        // ── navigation ────────────────────────────────────────────────────────

        private void NavItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string tag)
                ActivateView(tag, b);
        }

        private void ActivateView(string tag, Border navItem)
        {
            if (_activeNavItem != null)
                ApplyNavStyle(_activeNavItem, false);

            ApplyNavStyle(navItem, true);
            _activeNavItem = navItem;

            if (PageMeta.TryGetValue(tag, out var meta))
            {
                _txtPageTitle.Text    = meta.Title;
                _txtPageSubtitle.Text = meta.Subtitle;
            }

            _topbarButtons.Children.Clear();
            _contentArea.Content = tag switch
            {
                "ProjectSetup"   => CreateProjectSetupView(),
                "Sheets"         => CreateSheetsView(),
                "UpdateFamilies" => CreateUpdateFamiliesView(),
                "CreateFamilies" => CreateCreateFamiliesView(),
                _                => null
            };
        }

        // ── view factories ────────────────────────────────────────────────────

        private UIElement CreateProjectSetupView()
        {
            if (_uiApp == null) return NotActivatedPlaceholder();
            AddTopbarButton("Load from file", isSecondary: true,  onClick: ProjectSetup_LoadFromFile);
            AddTopbarButton("Save & apply",   isSecondary: false, onClick: ProjectSetup_SaveAndApply);
            return new ProjectSetupView(_uiApp);
        }

        private UIElement CreateSheetsView()
        {
            if (_uiApp == null) return NotActivatedPlaceholder();
            AddTopbarButton("Generate sheets", isSecondary: false, onClick: Sheets_Generate);
            return new SheetsView(_uiApp);
        }

        private UIElement CreateUpdateFamiliesView()
        {
            if (_uiApp == null) return NotActivatedPlaceholder();
            AddTopbarButton("Validate only", isSecondary: true,  onClick: UpdateFamilies_Validate);
            AddTopbarButton("Run",           isSecondary: false, onClick: UpdateFamilies_Run);
            return new UpdateFamiliesView(_uiApp);
        }

        private UIElement CreateCreateFamiliesView()
        {
            if (_uiApp == null) return NotActivatedPlaceholder();
            AddTopbarButton("Generate", isSecondary: false, onClick: CreateFamilies_Generate);
            return new CreateFamiliesView(_uiApp);
        }

        private TextBlock NotActivatedPlaceholder() => new()
        {
            Text         = "Click the BIM Command Centre ribbon button to connect to Revit.",
            FontFamily   = AppFont,
            FontSize     = 12,
            Foreground   = TextSecondary,
            Margin       = new Thickness(20),
            TextWrapping = TextWrapping.Wrap
        };

        // ── topbar helpers ────────────────────────────────────────────────────

        private void AddTopbarButton(string label, bool isSecondary, RoutedEventHandler onClick)
        {
            // Use Border+TextBlock instead of Button — WPF Button's default ControlTemplate
            // uses D3D9 gradients that crash on AMD RX 9060 XT inside Revit.
            var btn = new Border
            {
                Height          = 32,
                Cursor          = Cursors.Hand,
                Padding         = new Thickness(14, 0, 14, 0),
                Margin          = new Thickness(isSecondary ? 0 : 8, 0, 0, 0),
                CornerRadius    = new CornerRadius(4),
                BorderThickness = isSecondary ? new Thickness(1) : new Thickness(0),
                BorderBrush     = isSecondary ? BorderC : null,
                Background      = isSecondary ? Brushes.White : PrimaryBlue,
                Child = new TextBlock
                {
                    Text              = label,
                    FontFamily        = AppFont,
                    FontSize          = 12,
                    FontWeight        = isSecondary ? FontWeights.Normal : FontWeights.Medium,
                    Foreground        = isSecondary ? TextPrimary : Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            btn.MouseLeftButtonUp += (s, e) => onClick(s, new RoutedEventArgs());
            _topbarButtons.Children.Add(btn);
        }

        // ── topbar event handlers ─────────────────────────────────────────────

        private void ProjectSetup_LoadFromFile(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Load BIM config",
                Filter = "BIM Config|*.bimconfig.json|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                ProjectConfig? config = ConfigService.LoadConfig(dlg.FileName.Replace(".bimconfig.json", ".rvt"));
                if (config == null)
                    MessageBox.Show("No config found for this file.", "BIM Command Centre",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else if (_contentArea.Content is ProjectSetupView psv)
                    psv.LoadConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load config:\n{ex.Message}", "BIM Command Centre",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProjectSetup_SaveAndApply(object sender, RoutedEventArgs e)
        {
            if (_contentArea.Content is not ProjectSetupView psv) return;
            if (App.ApplyConfigHandler == null || App.ApplyConfigEvent == null) return;

            var config = psv.BuildConfig();

            // Offer to save/rename if the project has never been saved to disk
            string? saveAsPath = null;
            if (string.IsNullOrEmpty(_cachedDocPath))
            {
                string suggested = SanitizeFileName(
                    $"{config.ProjectNumber} - {config.ProjectName}.rvt".Trim(' ', '-', ' '));
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title    = "Save Revit Project As",
                    Filter   = "Revit Project (*.rvt)|*.rvt",
                    FileName = string.IsNullOrWhiteSpace(suggested) ? "Project.rvt" : suggested
                };
                if (dlg.ShowDialog() != true)
                    return;
                saveAsPath = dlg.FileName;
                _cachedDocPath  = dlg.FileName;
                _cachedDocTitle = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                _txtDocumentName.Text = _cachedDocTitle;
            }

            App.ApplyConfigHandler.PendingConfig    = config;
            App.ApplyConfigHandler.RvtFilePath      = _cachedDocPath;
            App.ApplyConfigHandler.TitleBlockFolder = AppSettingsService.Load().TitleBlockFolder;
            App.ApplyConfigHandler.SaveAsPath       = saveAsPath;
            App.ApplyConfigEvent.Raise();
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void Sheets_Generate(object sender, RoutedEventArgs e)
        {
            if (_contentArea.Content is SheetsView sv) sv.GenerateSheets();
        }

        private void UpdateFamilies_Validate(object sender, RoutedEventArgs e)
        {
            if (_contentArea.Content is UpdateFamiliesView ufv) ufv.RunValidation();
        }

        private void UpdateFamilies_Run(object sender, RoutedEventArgs e)
        {
            if (_contentArea.Content is UpdateFamiliesView ufv) ufv.RunBatchProcess();
        }

        private void CreateFamilies_Generate(object sender, RoutedEventArgs e)
        {
            if (_contentArea.Content is CreateFamiliesView cfv) cfv.GenerateFamily();
        }

        // ── project chip ──────────────────────────────────────────────────────

        private void ProjectChip_Click(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show(
                $"Active document: {_cachedDocTitle}\nPath: {(string.IsNullOrEmpty(_cachedDocPath) ? "(unsaved)" : _cachedDocPath)}",
                "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
