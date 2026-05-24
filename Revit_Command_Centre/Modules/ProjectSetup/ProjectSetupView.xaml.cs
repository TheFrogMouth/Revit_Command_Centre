using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;
using Revit_Command_Centre.UI;

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

        private readonly Picker _language  = new(new[] { "English", "Nederlands", "Français" });
        private readonly Picker _titleBlock = new(new[] { "Standard A1", "Standard A3", "Custom" });

        private static readonly string[] TierLabels = { "", "Tier 1 — Standard", "Tier 2 — BIM Compliant", "Tier 3 — ISO 19650 Full" };

        public ProjectSetupView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PickerHelper.Refresh(CmbLanguage,   _language,   UpdatePreview);
                PickerHelper.Refresh(CmbTitleBlock, _titleBlock, UpdatePreview);
                UpdatePreview();
            };
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

        private void Preview_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            PreviewClient.Text   = string.IsNullOrWhiteSpace(TxtClientName.Text)    ? "—" : TxtClientName.Text;
            PreviewProject.Text  = string.IsNullOrWhiteSpace(TxtProjectName.Text)   ? "—" : TxtProjectName.Text;
            PreviewNumber.Text   = string.IsNullOrWhiteSpace(TxtProjectNumber.Text) ? "—" : TxtProjectNumber.Text;
            PreviewLanguage.Text = _language.Value;
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

            SetPickerByValue(_language,   CmbLanguage,   config.Language,    "English");
            SetPickerByValue(_titleBlock, CmbTitleBlock, config.TitleBlock,   "Standard A1");

            _selectedTier = Math.Clamp(config.ComplianceTier, 1, 3);
            ApplyTierStyles();
            UpdatePreview();
        }

        public ProjectConfig BuildConfig() => new ProjectConfig
        {
            ClientName      = TxtClientName.Text.Trim(),
            ProjectName     = TxtProjectName.Text.Trim(),
            ProjectNumber   = TxtProjectNumber.Text.Trim(),
            Language        = _language.Value,
            TitleBlock      = _titleBlock.Value,
            ComplianceTier  = _selectedTier,
            IfcSchema       = _selectedTier >= 2 ? "IFC4" : string.Empty,
            CobieEnabled    = _selectedTier >= 3,
            LastModified    = DateTime.UtcNow
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
