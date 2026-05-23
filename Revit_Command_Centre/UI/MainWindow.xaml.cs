using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    /// Main floating panel. Hosts sidebar navigation and swaps module UserControls
    /// into ContentArea based on the selected nav button.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly UIApplication _uiApp;
        private Button? _activeNavButton;

        // Revit API is only safe inside IExternalCommand.Execute. These fields are populated
        // in the constructor (which runs within Execute) so WPF event handlers never touch Revit.
        private readonly string _cachedDocTitle   = "No document open";
        private readonly string _cachedProjNumber = "—";
        private readonly string _cachedDocPath    = string.Empty;
        private readonly bool   _cachedHasConfig  = false;

        private static readonly Dictionary<string, (string Title, string Subtitle)> PageMeta = new()
        {
            ["ProjectSetup"]    = ("Project Setup",          "Configure project information and compliance tier"),
            ["Sheets"]          = ("Sheets & Disciplines",   "Define active disciplines and sheet naming convention"),
            ["UpdateFamilies"]  = ("Update Families",        "Add shared parameters to existing family files"),
            ["CreateFamilies"]  = ("Create Families",        "Generate new families from templates"),
        };

        public MainWindow(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            // No Revit API calls here at all — defer to a safe point after the window is shown.
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Apply cached doc info — zero Revit API calls here.
            TxtDocumentName.Text  = _cachedDocTitle;
            TxtProjectNumber.Text = _cachedProjNumber;
            DocStatusDot.Fill     = _cachedHasConfig
                ? new SolidColorBrush(Color.FromRgb(0x1D, 0x9E, 0x75))
                : new SolidColorBrush(Color.FromRgb(0xBA, 0x75, 0x17));

            ActivateView("ProjectSetup", BtnProjectSetup);
        }

        // ──────────────────────────────────────  navigation  ──────────────────────────────────────

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                ActivateView(tag, btn);
        }

        private void ActivateView(string tag, Button button)
        {
            if (_activeNavButton != null)
                _activeNavButton.Style = (Style)FindResource("NavButtonStyle");

            button.Style     = (Style)FindResource("NavButtonActiveStyle");
            _activeNavButton = button;

            if (PageMeta.TryGetValue(tag, out var meta))
            {
                TxtPageTitle.Text    = meta.Title;
                TxtPageSubtitle.Text = meta.Subtitle;
            }

            TopbarButtons.Children.Clear();
            ContentArea.Content = tag switch
            {
                "ProjectSetup"   => CreateProjectSetupView(),
                "Sheets"         => CreateSheetsView(),
                "UpdateFamilies" => CreateUpdateFamiliesView(),
                "CreateFamilies" => CreateCreateFamiliesView(),
                _                => null
            };
        }

        // ──────────────────────────────────────  view factories  ──────────────────────────────────

        private UIElement CreateProjectSetupView()
        {
            AddTopbarButton("Load from file", isSecondary: true,  onClick: ProjectSetup_LoadFromFile);
            AddTopbarButton("Save & apply",   isSecondary: false, onClick: ProjectSetup_SaveAndApply);
            var view = new ProjectSetupView(_uiApp);
            view.Tag = "ProjectSetupInstance";
            return view;
        }

        private UIElement CreateSheetsView() => new SheetsView(_uiApp);

        private UIElement CreateUpdateFamiliesView()
        {
            AddTopbarButton("Validate only", isSecondary: true,  onClick: UpdateFamilies_Validate);
            AddTopbarButton("Run",           isSecondary: false, onClick: UpdateFamilies_Run);
            return new UpdateFamiliesView(_uiApp);
        }

        private UIElement CreateCreateFamiliesView()
        {
            AddTopbarButton("Generate", isSecondary: false, onClick: CreateFamilies_Generate);
            return new CreateFamiliesView(_uiApp);
        }

        // ──────────────────────────────────────  topbar helpers  ──────────────────────────────────

        private void AddTopbarButton(string label, bool isSecondary, RoutedEventHandler onClick)
        {
            var btn = new Button
            {
                Content = label,
                Style   = (Style)FindResource(isSecondary ? "SecondaryButtonStyle" : "PrimaryButtonStyle"),
                Margin  = new Thickness(isSecondary ? 0 : 8, 0, 0, 0)
            };
            btn.Click += onClick;
            TopbarButtons.Children.Add(btn);
        }

        // ──────────────────────────────────────  topbar event handlers  ───────────────────────────

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
                    MessageBox.Show("No config found for this file.", "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
                else if (ContentArea.Content is ProjectSetupView psv)
                    psv.LoadConfig(config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load config:\n{ex.Message}", "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProjectSetup_SaveAndApply(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is not ProjectSetupView psv) return;

            try
            {
                ProjectConfig config = psv.BuildConfig();

                // _cachedDocPath was captured inside Execute — safe to use here.
                if (!string.IsNullOrEmpty(_cachedDocPath))
                    ConfigService.SaveConfig(config, _cachedDocPath);

                MessageBox.Show("Config saved successfully.", "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save config:\n{ex.Message}", "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateFamilies_Validate(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is UpdateFamiliesView ufv)
                ufv.RunValidation();
        }

        private void UpdateFamilies_Run(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is UpdateFamiliesView ufv)
                ufv.RunBatchProcess();
        }

        private void CreateFamilies_Generate(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is CreateFamiliesView cfv)
                cfv.GenerateFamily();
        }

        // ──────────────────────────────────────  project chip  ────────────────────────────────────

        private void ProjectChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Just show the cached document name in a message — no live Revit API call.
            MessageBox.Show(
                $"Active document: {_cachedDocTitle}\nPath: {(string.IsNullOrEmpty(_cachedDocPath) ? "(unsaved)" : _cachedDocPath)}",
                "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ──────────────────────────────────────  inline document picker  ──────────────────────────────

    internal class DocumentPickerDialog : Window
    {
        public string? SelectedDocumentTitle { get; private set; }

        public DocumentPickerDialog(List<string> documentTitles)
        {
            Title  = "Select Document";
            Width  = 340;
            Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text       = "Open Revit documents:",
                FontSize   = 12,
                FontWeight = FontWeights.Medium,
                Margin     = new Thickness(0, 0, 0, 8)
            });

            var listBox = new ListBox { Height = 150, Margin = new Thickness(0, 0, 0, 12), ItemsSource = documentTitles };
            panel.Children.Add(listBox);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            btnCancel.Click += (_, _) => DialogResult = false;
            var btnOk = new Button { Content = "Select", Width = 80, Height = 30 };
            btnOk.Click += (_, _) => { if (listBox.SelectedItem is string s) { SelectedDocumentTitle = s; DialogResult = true; } };
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnOk);
            panel.Children.Add(btnRow);

            Content = panel;
        }
    }
}
