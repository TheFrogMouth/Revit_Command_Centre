using System;
using System.Windows;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre.Modules.ProjectSetup
{
    /// <summary>
    /// Code-behind for the Project Setup module.
    /// Manages compliance tier card selection, live title block preview, and config load/save.
    /// </summary>
    public partial class ProjectSetupView : UserControl
    {
        private readonly UIApplication _uiApp;
        private int _selectedTier = 2;

        // Tier labels used in preview and config
        private static readonly string[] TierLabels = { "", "Tier 1 — Standard", "Tier 2 — BIM Compliant", "Tier 3 — ISO 19650 Full" };

        public ProjectSetupView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += (_, _) => UpdatePreview();
        }

        // ──────────────────────────────────────  tier card selection  ──────────────────────────────

        /// <summary>
        /// Handles clicks on the three compliance tier cards.
        /// Updates visual selection state and re-applies styles.
        /// </summary>
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

        // ──────────────────────────────────────  live preview  ────────────────────────────────────

        /// <summary>Updates the read-only title block preview panel as fields change.</summary>
        private void Preview_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            PreviewClient.Text   = string.IsNullOrWhiteSpace(TxtClientName.Text)   ? "—" : TxtClientName.Text;
            PreviewProject.Text  = string.IsNullOrWhiteSpace(TxtProjectName.Text)  ? "—" : TxtProjectName.Text;
            PreviewNumber.Text   = string.IsNullOrWhiteSpace(TxtProjectNumber.Text) ? "—" : TxtProjectNumber.Text;
            PreviewLanguage.Text = (CmbLanguage.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "English";
            PreviewTier.Text     = TierLabels[_selectedTier];
        }

        // ──────────────────────────────────────  public API  ──────────────────────────────────────

        /// <summary>
        /// Populates all form fields from an existing <see cref="ProjectConfig"/>.
        /// Called by the main window's "Load from file" action.
        /// </summary>
        public void LoadConfig(ProjectConfig config)
        {
            TxtClientName.Text    = config.ClientName;
            TxtProjectName.Text   = config.ProjectName;
            TxtProjectNumber.Text = config.ProjectNumber;

            SetComboByContent(CmbLanguage,    config.Language);
            SetComboByContent(CmbTitleBlock,  config.TitleBlock);

            _selectedTier = Math.Clamp(config.ComplianceTier, 1, 3);
            ApplyTierStyles();
            UpdatePreview();
        }

        /// <summary>
        /// Builds a <see cref="ProjectConfig"/> from the current form state.
        /// Called by the main window's "Save &amp; apply" action.
        /// </summary>
        public ProjectConfig BuildConfig() => new ProjectConfig
        {
            ClientName      = TxtClientName.Text.Trim(),
            ProjectName     = TxtProjectName.Text.Trim(),
            ProjectNumber   = TxtProjectNumber.Text.Trim(),
            Language        = (CmbLanguage.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "English",
            TitleBlock      = (CmbTitleBlock.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Standard A1",
            ComplianceTier  = _selectedTier,
            IfcSchema       = _selectedTier >= 2 ? "IFC4" : string.Empty,
            CobieEnabled    = _selectedTier >= 3,
            LastModified    = DateTime.UtcNow
        };

        // ──────────────────────────────────────  helpers  ─────────────────────────────────────────

        private static void SetComboByContent(WpfComboBox combo, string value)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }
    }
}
