using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre.Modules.CreateFamilies
{
    /// <summary>
    /// Code-behind for the Create Families module.
    /// Builds template picker cards, auto-generates the family name,
    /// and delegates family creation to FamilyGeneratorService.
    /// </summary>
    public partial class CreateFamiliesView : UserControl
    {
        private readonly UIApplication _uiApp;
        private string _selectedTemplate = "Generic";

        private static readonly string[] TemplateNames =
        {
            "Door", "Window", "Column", "Beam",
            "Furniture", "MEP fixture", "Electrical", "Generic"
        };

        // Unicode glyphs for each template card
        private static readonly Dictionary<string, string> TemplateGlyphs = new()
        {
            ["Door"]        = "&#x1F6AA;",
            ["Window"]      = "&#x25A1;",
            ["Column"]      = "&#x2503;",
            ["Beam"]        = "&#x2501;",
            ["Furniture"]   = "&#x1FA91;",
            ["MEP fixture"] = "&#x1F6B0;",
            ["Electrical"]  = "&#x26A1;",
            ["Generic"]     = "&#x25FB;",
        };

        // Plain-text alternatives where Unicode chars may not render
        private static readonly Dictionary<string, string> TemplateIcons = new()
        {
            ["Door"]        = "D",
            ["Window"]      = "W",
            ["Column"]      = "Co",
            ["Beam"]        = "Bm",
            ["Furniture"]   = "Fu",
            ["MEP fixture"] = "MP",
            ["Electrical"]  = "El",
            ["Generic"]     = "Gn",
        };

        public CreateFamiliesView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += (_, _) =>
            {
                BuildTemplateCards();
                UpdateHint();
            };
        }

        // ──────────────────────────────────────  template cards  ──────────────────────────────────

        /// <summary>Populates the 4×2 template grid with clickable cards.</summary>
        private void BuildTemplateCards()
        {
            TemplateGrid.Children.Clear();

            foreach (string name in TemplateNames)
            {
                bool active = name == _selectedTemplate;
                TemplateGrid.Children.Add(CreateTemplateCard(name, active));
            }
        }

        private Border CreateTemplateCard(string name, bool active)
        {
            var activeBrush  = new SolidColorBrush(Color.FromRgb(0x18, 0x5F, 0xA5));
            var activeBgBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF1, 0xFB));
            var borderBrush  = new SolidColorBrush(Color.FromArgb(0x1E, 0x00, 0x00, 0x00));

            var card = new Border
            {
                Margin          = new Thickness(3),
                Height          = 72,
                CornerRadius    = new CornerRadius(6),
                BorderThickness = active ? new Thickness(2) : new Thickness(1),
                BorderBrush     = active ? activeBrush : borderBrush,
                Background      = active ? activeBgBrush : new SolidColorBrush(Colors.White),
                Cursor          = Cursors.Hand,
                Tag             = name,
            };

            string icon = TemplateIcons.TryGetValue(name, out string? ic) ? ic : name[..1];

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text       = icon,
                FontSize   = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = active ? activeBrush : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B))
            });
            panel.Children.Add(new TextBlock
            {
                Text       = name,
                FontSize   = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth   = 80,
            });
            card.Child = panel;
            card.MouseLeftButtonUp += TemplateCard_Click;
            return card;
        }

        private void TemplateCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border card && card.Tag is string name)
            {
                _selectedTemplate = name;
                BuildTemplateCards();
                UpdateAutoName();
                UpdateHint();
            }
        }

        // ──────────────────────────────────────  naming  ──────────────────────────────────────────

        private void Naming_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => UpdateAutoName();

        private void UpdateAutoName()
        {
            if (TxtWidth == null || TxtHeight == null || TxtFamilyName == null) return;

            string w = TxtWidth.Text.Trim();
            string h = TxtHeight.Text.Trim();
            TxtFamilyName.Text = $"BIM_{_selectedTemplate.Replace(" ", "_")}_{w}x{h}";
        }

        private void UpdateHint()
        {
            if (TxtHint == null) return;
            int tier = GetTierFromFilter();
            TxtHint.Text = $"Family will be named per project convention and saved with {ConfigService.GetDefaultParameters(tier).Count} shared parameters pre-loaded from your Tier {tier} config.";
        }

        // ──────────────────────────────────────  folder picker  ───────────────────────────────────

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select save folder" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtSaveFolder.Text = dlg.SelectedPath;
        }

        // ──────────────────────────────────────  generate (called by MainWindow)  ─────────────────

        /// <summary>
        /// Validates inputs and calls FamilyGeneratorService to create the new .rfa file.
        /// </summary>
        public void GenerateFamily()
        {
            if (!double.TryParse(TxtWidth.Text, out double width) || width <= 0)
            {
                MessageBox.Show("Please enter a valid width in mm.", "BIM Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(TxtHeight.Text, out double height) || height <= 0)
            {
                MessageBox.Show("Please enter a valid height in mm.", "BIM Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(TxtSaveFolder.Text))
            {
                MessageBox.Show("Please select a save folder.", "BIM Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string name = string.IsNullOrWhiteSpace(TxtFamilyName.Text)
                ? $"BIM_{_selectedTemplate}_{width}x{height}"
                : TxtFamilyName.Text.Trim();

            List<FamilyParameter> parameters = ConfigService.GetDefaultParameters(GetTierFromFilter());

            try
            {
                string outputPath = FamilyGeneratorService.GenerateFamily(
                    _uiApp,
                    _selectedTemplate,
                    width,
                    height,
                    name,
                    TxtSaveFolder.Text,
                    parameters);

                MessageBox.Show($"Family created successfully:\n{outputPath}", "BIM Tools", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate family:\n{ex.Message}", "BIM Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ──────────────────────────────────────  helpers  ─────────────────────────────────────────

        private int GetTierFromFilter()
        {
            string filter = (CmbParameterTier?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "From project config";
            return filter switch
            {
                "Tier 1 only" => 1,
                "All tiers"   => 3,
                _             => 2
            };
        }
    }
}
