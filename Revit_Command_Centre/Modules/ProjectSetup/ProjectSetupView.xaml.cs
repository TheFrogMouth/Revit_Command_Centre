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
        private int _selectedTier = 2;

        private readonly Picker _language   = new(new[] { "English", "Nederlands", "Français" });
        private readonly Picker _titleBlock = new(new[] { "Standard A1", "Standard A3", "Custom" });

        private static readonly string[] TierLabels =
            { "", "Tier 1 — Standard", "Tier 2 — BIM Compliant", "Tier 3 — ISO 19650 Full" };

        // Frozen brush for locked input fields
        private static readonly SolidColorBrush LockedBg = MakeFrozenBrush(0xF0, 0xF0, 0xF0);
        private static readonly SolidColorBrush Blue     = MakeFrozenBrush(0x18, 0x5F, 0xA5);
        private static readonly SolidColorBrush BlueBg   = MakeFrozenBrush(0xE6, 0xF1, 0xFB);
        private static readonly SolidColorBrush Grey     = MakeFrozenBrush(0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush Dark     = MakeFrozenBrush(0x1A, 0x1A, 0x1A);
        private static readonly SolidColorBrush Green    = MakeFrozenBrush(0x22, 0xC5, 0x5E);

        private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private HashSet<string> _existingWorksetNames = new(StringComparer.OrdinalIgnoreCase);

        public ProjectSetupView(UIApplication uiApp)
        {
            _uiApp = uiApp;
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

            Autodesk.Revit.DB.Document? doc = null;
            try { doc = _uiApp.ActiveUIDocument?.Document; } catch { }

            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
            {
                // Populate identity fields from live document
                TxtClientName.Text    = doc.ProjectInformation.ClientName;
                TxtProjectName.Text   = doc.ProjectInformation.Name;
                TxtProjectNumber.Text = doc.ProjectInformation.Number;

                // Lock project identity — name and number uniquely identify the project
                LockField(TxtProjectName,   LblProjectName);
                LockField(TxtProjectNumber, LblProjectNumber);

                // Restore picker state from extensible storage or sidecar JSON
                try
                {
                    var saved = ExtensibleStorageService.ReadConfig(doc);
                    if (saved == null)
                        saved = ConfigService.LoadConfig(doc.PathName);
                    if (saved != null)
                    {
                        SetPickerByValue(_language,   CmbLanguage,   saved.Language,   "English");
                        SetPickerByValue(_titleBlock, CmbTitleBlock, saved.TitleBlock,  "Standard A1");
                        _selectedTier = Math.Clamp(saved.ComplianceTier, 1, 3);
                        ApplyTierStyles();
                    }
                }
                catch { }
            }

            PopulateWorksets(doc);
            UpdatePreview();
        }

        private static void LockField(TextBox box, TextBlock label)
        {
            box.IsReadOnly       = true;
            box.IsHitTestVisible = false;
            box.Background       = LockedBg;
            box.Cursor           = Cursors.Arrow;
            box.ToolTip          = "Locked — project identity cannot change after saving to disk.";
            label.Text          += " (LOCKED)";
        }

        // ──────────────────────────────────────  worksets  ──────────────────────────────────────────

        private void PopulateWorksets(Autodesk.Revit.DB.Document? doc)
        {
            WorksetsList.Children.Clear();
            WorksetActionContainer.Children.Clear();

            if (doc == null)
            {
                TxtWorksetsStatus.Text = "No project open. Worksets will appear once a project is active.";
                return;
            }

            if (!doc.IsWorkshared)
            {
                TxtWorksetsStatus.Text = "Worksharing is not enabled for this project.";
                return;
            }

            try
            {
                var worksets = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Workset))
                    .Cast<Autodesk.Revit.DB.Workset>()
                    .Where(ws => ws.Kind == Autodesk.Revit.DB.WorksetKind.UserCreated)
                    .OrderBy(ws => ws.Name)
                    .ToList();

                _existingWorksetNames = new HashSet<string>(
                    worksets.Select(ws => ws.Name), StringComparer.OrdinalIgnoreCase);

                TxtWorksetsStatus.Text = worksets.Count == 0
                    ? "No user worksets yet."
                    : $"{worksets.Count} user workset{(worksets.Count == 1 ? "" : "s")}";

                foreach (var ws in worksets)
                    WorksetsList.Children.Add(BuildWorksetRow(ws.Name, ws.Owner, ws.IsOpen));

                BuildAddWorksetButton();
            }
            catch (Exception ex)
            {
                TxtWorksetsStatus.Text = $"Could not read worksets: {ex.Message}";
            }
        }

        private static UIElement BuildWorksetRow(string name, string owner, bool isOpen)
        {
            var dot = new Ellipse
            {
                Width             = 8,
                Height            = 8,
                Fill              = isOpen ? Green : new SolidColorBrush(Color.FromRgb(0x9B, 0x9B, 0x9B)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
                ToolTip           = isOpen ? "Open in this session" : "Closed"
            };

            var lblName = new TextBlock
            {
                Text              = name,
                FontSize          = 12,
                FontWeight        = FontWeights.Medium,
                Foreground        = Dark,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var lblOwner = new TextBlock
            {
                Text              = string.IsNullOrEmpty(owner) ? "—" : owner,
                FontSize          = 10,
                Foreground        = Grey,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(dot);
            row.Children.Add(lblName);
            row.Children.Add(lblOwner);
            return row;
        }

        private void BuildAddWorksetButton()
        {
            WorksetActionContainer.Children.Clear();

            var btn = new Border
            {
                Cursor              = Cursors.Hand,
                Padding             = new Thickness(10, 5, 10, 5),
                CornerRadius        = new CornerRadius(4),
                BorderThickness     = new Thickness(1),
                BorderBrush         = new SolidColorBrush(Color.FromArgb(0x3F, 0, 0, 0)),
                Background          = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child               = new TextBlock { Text = "+ Add workset", FontSize = 11, Foreground = Blue }
            };
            btn.MouseLeftButtonUp += (_, _) =>
            {
                var available = AppSettingsService.Load().WorksetTemplates
                    .Where(t => !_existingWorksetNames.Contains(t))
                    .ToList();
                ShowAddWorksetForm(available);
            };
            WorksetActionContainer.Children.Add(btn);
        }

        private void ShowAddWorksetForm(List<string> templates)
        {
            WorksetActionContainer.Children.Clear();

            var panel = new StackPanel();

            if (templates.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text       = "Quick add from template:",
                    FontSize   = 10,
                    Foreground = Grey,
                    Margin     = new Thickness(0, 0, 0, 6)
                });

                var chipWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                foreach (var t in templates)
                    chipWrap.Children.Add(BuildTemplateChip(t));
                panel.Children.Add(chipWrap);
            }

            panel.Children.Add(new TextBlock
            {
                Text       = "Custom name:",
                FontSize   = 10,
                Foreground = Grey,
                Margin     = new Thickness(0, 0, 0, 4)
            });

            var txtInput = new TextBox
            {
                FontSize                 = 12,
                Height                   = 28,
                Padding                  = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderBrush              = new SolidColorBrush(Color.FromArgb(0x3F, 0, 0, 0)),
                BorderThickness          = new Thickness(1),
                Background               = Brushes.White,
                Width                    = 200,
                HorizontalAlignment      = HorizontalAlignment.Left,
            };

            var btnAdd = new Border
            {
                Cursor          = Cursors.Hand,
                Padding         = new Thickness(12, 0, 12, 0),
                Height          = 28,
                CornerRadius    = new CornerRadius(4),
                Background      = Blue,
                Margin          = new Thickness(6, 0, 0, 0),
                Child           = new TextBlock { Text = "Add", FontSize = 11, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
            };

            var btnCancel = new Border
            {
                Cursor          = Cursors.Hand,
                Padding         = new Thickness(10, 0, 10, 0),
                Height          = 28,
                CornerRadius    = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x3F, 0, 0, 0)),
                Background      = Brushes.White,
                Margin          = new Thickness(4, 0, 0, 0),
                Child           = new TextBlock { Text = "Cancel", FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
            };

            var inputRow = new StackPanel { Orientation = Orientation.Horizontal };
            inputRow.Children.Add(txtInput);
            inputRow.Children.Add(btnAdd);
            inputRow.Children.Add(btnCancel);
            panel.Children.Add(inputRow);

            btnAdd.MouseLeftButtonUp    += (_, _) => RaiseAddWorkset(txtInput.Text.Trim());
            btnCancel.MouseLeftButtonUp += (_, _) => BuildAddWorksetButton();

            WorksetActionContainer.Children.Add(panel);
        }

        private Border BuildTemplateChip(string name)
        {
            var chip = new Border
            {
                Cursor          = Cursors.Hand,
                Padding         = new Thickness(8, 4, 8, 4),
                Margin          = new Thickness(0, 0, 6, 6),
                CornerRadius    = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush     = Blue,
                Background      = BlueBg,
                Child           = new TextBlock { Text = name, FontSize = 10, Foreground = Blue }
            };
            chip.MouseLeftButtonUp += (_, _) => RaiseAddWorkset(name);
            return chip;
        }

        private void RaiseAddWorkset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (App.AddWorksetHandler == null || App.AddWorksetEvent == null) return;

            App.AddWorksetHandler.WorksetName = name;
            // If a previous event is still pending the handler's WorksetName must not be
            // overwritten — bail out silently so the user retries after Revit processes it.
            if (App.AddWorksetEvent.Raise() != Autodesk.Revit.UI.ExternalEventRequest.Accepted)
                return;

            // Optimistic row only — intentionally do NOT add to _existingWorksetNames so
            // the template chip stays available if Revit rejects the creation (duplicate
            // name, worksharing not enabled, read-only doc, etc.).
            WorksetsList.Children.Add(BuildWorksetRow(name, string.Empty, true));
            BuildAddWorksetButton();
        }

        // ──────────────────────────────────────  browse  ────────────────────────────────────────────

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

        // ──────────────────────────────────────  tier card selection  ───────────────────────────────

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

        // ──────────────────────────────────────  live preview  ──────────────────────────────────────

        private void Preview_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            PreviewClient.Text   = string.IsNullOrWhiteSpace(TxtClientName.Text)    ? "—" : TxtClientName.Text;
            PreviewProject.Text  = string.IsNullOrWhiteSpace(TxtProjectName.Text)   ? "—" : TxtProjectName.Text;
            PreviewNumber.Text   = string.IsNullOrWhiteSpace(TxtProjectNumber.Text) ? "—" : TxtProjectNumber.Text;
            PreviewLanguage.Text = _language.Value;
            PreviewTier.Text     = TierLabels[_selectedTier];
        }

        // ──────────────────────────────────────  public API  ────────────────────────────────────────

        public void LoadConfig(ProjectConfig config)
        {
            TxtClientName.Text = config.ClientName;
            // Respect the lock: project identity must not be overwritten once saved to disk
            if (!TxtProjectName.IsReadOnly)
                TxtProjectName.Text   = config.ProjectName;
            if (!TxtProjectNumber.IsReadOnly)
                TxtProjectNumber.Text = config.ProjectNumber;

            SetPickerByValue(_language,   CmbLanguage,   config.Language,   "English");
            SetPickerByValue(_titleBlock, CmbTitleBlock, config.TitleBlock,  "Standard A1");

            _selectedTier = Math.Clamp(config.ComplianceTier, 1, 3);
            ApplyTierStyles();
            UpdatePreview();
        }

        public ProjectConfig BuildConfig() => new ProjectConfig
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
