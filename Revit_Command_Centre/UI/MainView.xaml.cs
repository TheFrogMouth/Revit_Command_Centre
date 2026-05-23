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
    /// Main BIM Command Centre panel hosted inside Revit's dockable pane framework.
    /// Using a dockable pane (UserControl) rather than a top-level Window avoids
    /// WPF render-target creation crashing in Revit's process-level rendering context.
    /// </summary>
    public partial class MainView : UserControl
    {
        private Button? _activeNavButton;
        private string _cachedDocTitle   = "No document open";
        private string _cachedProjNumber = "—";
        private string _cachedDocPath    = string.Empty;

        private static readonly Dictionary<string, (string Title, string Subtitle)> PageMeta = new()
        {
            ["ProjectSetup"]   = ("Project Setup",        "Configure project information and compliance tier"),
            ["Sheets"]         = ("Sheets & Disciplines", "Define active disciplines and sheet naming convention"),
            ["UpdateFamilies"] = ("Update Families",      "Add shared parameters to existing family files"),
            ["CreateFamilies"] = ("Create Families",      "Generate new families from templates"),
        };

        public MainView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private UIApplication? UiApp => App.CurrentUIApp;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Populate doc info now that Revit has stored UIApplication in App.CurrentUIApp.
            try
            {
                var doc = UiApp?.ActiveUIDocument?.Document;
                if (doc != null)
                {
                    _cachedDocTitle  = doc.Title;
                    _cachedDocPath   = doc.PathName;
                }
            }
            catch { }

            TxtDocumentName.Text  = _cachedDocTitle;
            TxtProjectNumber.Text = _cachedProjNumber;
            DocStatusDot.Fill     = new SolidColorBrush(Color.FromRgb(0xBA, 0x75, 0x17));

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
            return new ProjectSetupView(UiApp!);
        }

        private UIElement CreateSheetsView() => new SheetsView(UiApp!);

        private UIElement CreateUpdateFamiliesView()
        {
            AddTopbarButton("Validate only", isSecondary: true,  onClick: UpdateFamilies_Validate);
            AddTopbarButton("Run",           isSecondary: false, onClick: UpdateFamilies_Run);
            return new UpdateFamiliesView(UiApp!);
        }

        private UIElement CreateCreateFamiliesView()
        {
            AddTopbarButton("Generate", isSecondary: false, onClick: CreateFamilies_Generate);
            return new CreateFamiliesView(UiApp!);
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
                    MessageBox.Show("No config found for this file.", "BIM Command Centre",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else if (ContentArea.Content is ProjectSetupView psv)
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
            if (ContentArea.Content is not ProjectSetupView psv) return;

            try
            {
                ProjectConfig config = psv.BuildConfig();
                if (!string.IsNullOrEmpty(_cachedDocPath))
                    ConfigService.SaveConfig(config, _cachedDocPath);
                MessageBox.Show("Config saved successfully.", "BIM Command Centre",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save config:\n{ex.Message}", "BIM Command Centre",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateFamilies_Validate(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is UpdateFamiliesView ufv) ufv.RunValidation();
        }

        private void UpdateFamilies_Run(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is UpdateFamiliesView ufv) ufv.RunBatchProcess();
        }

        private void CreateFamilies_Generate(object sender, RoutedEventArgs e)
        {
            if (ContentArea.Content is CreateFamiliesView cfv) cfv.GenerateFamily();
        }

        // ──────────────────────────────────────  project chip  ────────────────────────────────────

        private void ProjectChip_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MessageBox.Show(
                $"Active document: {_cachedDocTitle}\nPath: {(string.IsNullOrEmpty(_cachedDocPath) ? "(unsaved)" : _cachedDocPath)}",
                "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
