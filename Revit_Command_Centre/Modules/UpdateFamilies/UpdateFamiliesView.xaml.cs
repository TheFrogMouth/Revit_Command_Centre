using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre.Modules.UpdateFamilies
{
    public partial class UpdateFamiliesView : UserControl
    {
        private readonly UIApplication _uiApp;
        private readonly List<string> _selectedFiles = new();
        private string _outputFolder = string.Empty;

        private int _total;
        private double _progressBarTotalWidth;

        private readonly Picker _categoryFilter  = new(new[] { "All", "Architectural", "Structural", "MEP" });
        private readonly Picker _parameterFilter = new(new[] { "From project config", "Tier 1 only", "All tiers" });

        private enum ViewMode { UpdateParams, Rename }
        private ViewMode _currentMode = ViewMode.UpdateParams;
        private readonly Picker _modePicker = new(new[] { "Update Parameters", "Rename to Convention" });
        private List<RenameCandidate> _renameCandidates = new();
        private readonly HashSet<RenameRow> _selectedRenameRows = new();

        private static readonly SolidColorBrush BrushInfo       = new(Color.FromRgb(0x18, 0x5F, 0xA5));
        private static readonly SolidColorBrush BrushSuccess    = new(Color.FromRgb(0x1D, 0x9E, 0x75));
        private static readonly SolidColorBrush BrushWarn       = new(Color.FromRgb(0xBA, 0x75, 0x17));
        private static readonly SolidColorBrush BrushError      = new(Color.FromRgb(0xE2, 0x4B, 0x4A));
        private static readonly SolidColorBrush BrushBorder     = new(Color.FromArgb(0x1E, 0, 0, 0));
        private static readonly SolidColorBrush BrushTxtPri     = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly SolidColorBrush BrushTxtSec     = new(Color.FromRgb(0x6B, 0x6B, 0x6B));
        private static readonly SolidColorBrush BrushSelectedBg = new(Color.FromRgb(0xE6, 0xF1, 0xFB));
        private static readonly SolidColorBrush BrushHelperBg   = new(Color.FromRgb(0xF5, 0xF8, 0xFF));
        private static readonly FontFamily      ConsolasFont    = new("Consolas");
        private static readonly FontFamily      AppFont         = new("Segoe UI");

        static UpdateFamiliesView()
        {
            BrushInfo.Freeze();      BrushSuccess.Freeze();
            BrushWarn.Freeze();      BrushError.Freeze();
            BrushBorder.Freeze();    BrushTxtPri.Freeze();
            BrushTxtSec.Freeze();    BrushSelectedBg.Freeze();
            BrushHelperBg.Freeze();
        }

        public UpdateFamiliesView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();

            var saved = AppSettingsService.Load();
            if (!string.IsNullOrEmpty(saved.DefaultFamilyOutputFolder))
            {
                _outputFolder = saved.DefaultFamilyOutputFolder;
                TxtOutputFolder.Text = _outputFolder;
            }

            TxtNamePrefix.TextChanged += (_, __) => RefreshFileList();
            Loaded += OnViewLoaded;
        }

        private void OnViewLoaded(object sender, RoutedEventArgs e)
        {
            PickerHelper.Refresh(CmbCategoryFilter,  _categoryFilter);
            PickerHelper.Refresh(CmbParameterFilter, _parameterFilter);

            BrowseOutputFolderContainer.Children.Add(
                PickerHelper.MakeButton("Browse", OutputFolder_Click));

            ClearFilesContainer.Children.Add(
                PickerHelper.MakeButton("Clear", ClearFiles_Click, height: 24, margin: new Thickness(0)));

            _progressBarTotalWidth = ProgressFill.ActualWidth > 0 ? ProgressFill.ActualWidth : 300;
            ProgressFill.SizeChanged += (_, ev) =>
                _progressBarTotalWidth = ev.NewSize.Width > 0 ? ev.NewSize.Width : _progressBarTotalWidth;

            PickerHelper.Refresh(ModeSwitcherContainer, _modePicker, OnModeChanged);

            BrowseRenameFolderContainer.Children.Add(
                PickerHelper.MakeButton("Browse…", BrowseRenameFolder_Click));
            BuildRenameActionBar();
        }

        // ── mode toggle ────────────────────────────────────────────────────────────────────

        private void OnModeChanged()
        {
            _currentMode = _modePicker.Value == "Rename to Convention"
                ? ViewMode.Rename : ViewMode.UpdateParams;
            UpdateParamsPanel.Visibility = _currentMode == ViewMode.UpdateParams
                ? Visibility.Visible : Visibility.Collapsed;
            RenamePanel.Visibility = _currentMode == ViewMode.Rename
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── drop zone ─────────────────────────────────────────────────────────────────────

        private void DropZone_Click(object sender, MouseButtonEventArgs e) => BrowseForFiles();

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

            var added = new List<string>();
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                    added.AddRange(Directory.GetFiles(path, "*.rfa", SearchOption.AllDirectories));
                else if (File.Exists(path) && path.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                    added.Add(path);
            }
            AddFiles(added);
        }

        private void BrowseForFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Select .rfa family files",
                Filter = "Revit Family Files (*.rfa)|*.rfa",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
                AddFiles(dlg.FileNames);
        }

        // ── output folder ──────────────────────────────────────────────────────────────────

        private void OutputFolder_Click(object sender, MouseButtonEventArgs e) => OpenOutputFolderDialog();
        private void OutputFolder_Click(object sender, RoutedEventArgs e)     => OpenOutputFolderDialog();

        private void OpenOutputFolderDialog()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select output folder for processed families",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;
            _outputFolder = dlg.FolderName;
            TxtOutputFolder.Text = _outputFolder;
            var settings = AppSettingsService.Load();
            settings.DefaultFamilyOutputFolder = _outputFolder;
            AppSettingsService.Save(settings);
        }

        // ── file list ──────────────────────────────────────────────────────────────────────

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
                if (!_selectedFiles.Contains(path))
                    _selectedFiles.Add(path);
            RefreshFileList();
        }

        private void ClearFiles_Click(object sender, MouseButtonEventArgs e)
        {
            _selectedFiles.Clear();
            RefreshFileList();
            ResetStats();
        }

        private void RefreshFileList()
        {
            FileListPanel.Children.Clear();
            string prefix = TxtNamePrefix.Text.Trim();

            if (_selectedFiles.Count == 0)
            {
                TxtNoFiles.Visibility = Visibility.Visible;
                FileListPanel.Children.Add(TxtNoFiles);
                TxtNamePreview.Text = string.Empty;
                UpdateStats(0, 0, 0, 0);
                return;
            }

            TxtNoFiles.Visibility = Visibility.Collapsed;
            foreach (string path in _selectedFiles)
            {
                string original = Path.GetFileName(path);
                string renamed  = BuildOutputName(prefix, original);
                string label    = string.IsNullOrEmpty(prefix) ? original : $"{original}  →  {renamed}";

                FileListPanel.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11,
                    Foreground = string.IsNullOrEmpty(prefix) ? (Brush)FindResource("TextSecondaryBrush") : BrushInfo,
                    Padding = new Thickness(0, 2, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            TxtNamePreview.Text = _selectedFiles.Count > 0 && !string.IsNullOrEmpty(prefix)
                ? $"e.g. {BuildOutputName(prefix, Path.GetFileName(_selectedFiles[0]))}"
                : string.Empty;

            UpdateStats(0, 0, 0, _selectedFiles.Count);
        }

        private static string BuildOutputName(string prefix, string originalFileName) =>
            string.IsNullOrEmpty(prefix) ? originalFileName : $"{prefix}_{originalFileName}";

        // ── public API (called by MainView) ───────────────────────────────────────────────

        public void RunValidation()
        {
            if (!EnsureFilesSelected()) return;
            if (App.UpdateFamiliesHandler == null || App.UpdateFamiliesEvent == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("BIM Command Centre", "Handler not ready. Please try again.");
                return;
            }

            ClearLog();
            ResetStats();
            AppendLog("→ Queuing validation…", BrushInfo);

            App.UpdateFamiliesHandler.Mode       = UpdateFamiliesEventHandler.OperationMode.Validate;
            App.UpdateFamiliesHandler.FilePaths  = new List<string>(_selectedFiles);
            App.UpdateFamiliesHandler.Parameters = GetRequiredParameters();
            App.UpdateFamiliesHandler.OnLog      = (msg, level) => AppendLog(msg, LevelToColor(level));
            App.UpdateFamiliesHandler.OnProgress = (file, cur, total) =>
            {
                TxtCurrentFile.Text = file;
                TxtProgress.Text    = $"{cur}/{total}";
                if (total > 0) ProgressFill.Width = _progressBarTotalWidth * cur / (double)total;
            };
            App.UpdateFamiliesHandler.OnComplete = (updated, skipped, errors) =>
                UpdateStats(updated, skipped, errors, _selectedFiles.Count);

            var req = App.UpdateFamiliesEvent.Raise();
            if (req != ExternalEventRequest.Accepted)
                AppendLog($"Could not queue operation ({req}). Revit may be busy — try again.", BrushError);
        }

        public void RunBatchProcess()
        {
            if (!EnsureFilesSelected()) return;
            if (App.UpdateFamiliesHandler == null || App.UpdateFamiliesEvent == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("BIM Command Centre", "Handler not ready. Please try again.");
                return;
            }

            ClearLog();
            ResetStats();
            _total = _selectedFiles.Count;
            UpdateStats(0, 0, 0, _total);
            AppendLog("→ Queuing batch process…", BrushInfo);

            App.UpdateFamiliesHandler.Mode         = UpdateFamiliesEventHandler.OperationMode.BatchProcess;
            App.UpdateFamiliesHandler.FilePaths    = new List<string>(_selectedFiles);
            App.UpdateFamiliesHandler.Parameters   = GetRequiredParameters();
            App.UpdateFamiliesHandler.OutputFolder = _outputFolder;
            App.UpdateFamiliesHandler.NamePrefix   = TxtNamePrefix.Text.Trim();
            App.UpdateFamiliesHandler.OnLog        = (msg, level) => AppendLog(msg, LevelToColor(level));
            App.UpdateFamiliesHandler.OnProgress   = (file, cur, total) =>
            {
                TxtCurrentFile.Text = file;
                TxtProgress.Text    = $"{cur}/{total}";
                if (total > 0) ProgressFill.Width = _progressBarTotalWidth * cur / (double)total;
            };
            App.UpdateFamiliesHandler.OnComplete = (updated, skipped, errors) =>
                UpdateStats(updated, skipped, errors, _total);

            var req = App.UpdateFamiliesEvent.Raise();
            if (req != ExternalEventRequest.Accepted)
                AppendLog($"Could not queue operation ({req}). Revit may be busy — try again.", BrushError);
        }

        // ── rename to convention ──────────────────────────────────────────────────────────

        private void BrowseRenameFolder_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder containing .rfa files to rename"
            };
            if (dlg.ShowDialog() != true) return;
            TxtRenameFolderPath.Text = dlg.FolderName;
            ScanAndBuildRenameTable(dlg.FolderName);
        }

        private void ScanAndBuildRenameTable(string folderPath)
        {
            _renameCandidates = FamilyRenameService.ScanFolder(folderPath);
            PopulateRenameList();
            int total      = _renameCandidates.Count;
            int compliant  = _renameCandidates.Count(c => c.IsCompliant);
            int needsInput = _renameCandidates.Count(c => c.NeedsManualInput);
            int autoRename = total - compliant - needsInput;
            TxtRenameStatus.Text =
                $"{total} files — {compliant} already compliant, {autoRename} auto-rename, {needsInput} need input.";
        }

        private void PopulateRenameList()
        {
            _selectedRenameRows.Clear();
            RenameListPanel.Children.Clear();

            if (_renameCandidates.Count == 0)
            {
                RenameListPanel.Children.Add(new TextBlock
                {
                    Text = "No .rfa files found.", FontSize = 11,
                    Foreground = BrushTxtSec, Margin = new Thickness(0, 4, 0, 4)
                });
                return;
            }

            RenameListPanel.Children.Add(MakeRenameHeaderRow());

            foreach (var candidate in _renameCandidates)
            {
                var row = new RenameRow(candidate);
                if (row.ApplyRename) _selectedRenameRows.Add(row);
                RenameListPanel.Children.Add(MakeRenameRowBorder(row, row.ApplyRename));
            }
        }

        private static Border MakeRenameHeaderRow()
        {
            var grid = MakeRenameRowGrid();

            void AddH(int col, string text)
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = BrushTxtSec, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }
            AddH(0, "CURRENT NAME");
            AddH(2, "PROPOSED NAME");
            AddH(4, "STATUS");

            return new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = BrushBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Child = grid
            };
        }

        private Border MakeRenameRowBorder(RenameRow row, bool selected)
        {
            var outerStack = new StackPanel();
            var grid = MakeRenameRowGrid();

            // Current name
            var currentTb = new TextBlock
            {
                Text = row.CurrentName, FontSize = 11, Foreground = BrushTxtPri,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(currentTb, 0);
            grid.Children.Add(currentTb);

            // Proposed name — editable for non-compliant rows
            if (row.CanEdit)
            {
                var proposedTb = new System.Windows.Controls.TextBox
                {
                    Text = row.ProposedName, FontSize = 11, FontFamily = AppFont,
                    Background = Brushes.White, Padding = new Thickness(4, 2, 4, 2),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                proposedTb.TextChanged += (_, _) => row.ProposedName = proposedTb.Text;
                // Keep TextBox in sync when the naming helper updates the model
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(RenameRow.ProposedName) && proposedTb.Text != row.ProposedName)
                        proposedTb.Text = row.ProposedName;
                };
                Grid.SetColumn(proposedTb, 2);
                grid.Children.Add(proposedTb);
            }
            else
            {
                var proposedTb = new TextBlock
                {
                    Text = row.ProposedName, FontSize = 11, Foreground = BrushTxtSec,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(proposedTb, 2);
                grid.Children.Add(proposedTb);
            }

            outerStack.Children.Add(grid);

            // Build the outer border first so we can close over it in the helper
            var border = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                Background = selected ? BrushSelectedBg : Brushes.White,
                BorderBrush = BrushBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = row.IsCompliant ? Cursors.Arrow : Cursors.Hand,
                Child = outerStack
            };

            bool needsHelp = row.Status.StartsWith("⚠");

            if (needsHelp)
            {
                // Status cell with "▸ fill in" expand link
                var statusStack = new StackPanel { Orientation = Orientation.Horizontal };
                statusStack.Children.Add(new TextBlock
                {
                    Text = row.Status, FontSize = 11, Foreground = BrushWarn,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var expandLink = new TextBlock
                {
                    Text = "  ▸ fill in", FontSize = 10, Foreground = BrushInfo,
                    VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand
                };
                statusStack.Children.Add(expandLink);
                Grid.SetColumn(statusStack, 4);
                grid.Children.Add(statusStack);

                // Inline naming helper panel (collapsed until expand link is clicked)
                var helperPanel = MakeNamingHelperPanel(row, border, _selectedRenameRows);
                helperPanel.Visibility = Visibility.Collapsed;
                outerStack.Children.Add(helperPanel);

                expandLink.MouseLeftButtonUp += (_, _) =>
                {
                    if (helperPanel.Visibility == Visibility.Visible)
                    {
                        helperPanel.Visibility = Visibility.Collapsed;
                        expandLink.Text = "  ▸ fill in";
                    }
                    else
                    {
                        helperPanel.Visibility = Visibility.Visible;
                        expandLink.Text = "  ▾ close";
                    }
                };
            }
            else
            {
                // Plain status label
                var statusBrush = row.Status.StartsWith("✓") ? BrushTxtSec : BrushInfo;
                var statusTb = new TextBlock
                {
                    Text = row.Status, FontSize = 11, Foreground = statusBrush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(statusTb, 4);
                grid.Children.Add(statusTb);

                // Auto-rename rows: click toggles selection
                if (!row.IsCompliant)
                {
                    border.MouseLeftButtonUp += (_, _) =>
                    {
                        if (_selectedRenameRows.Contains(row))
                        {
                            _selectedRenameRows.Remove(row);
                            border.Background = Brushes.White;
                        }
                        else
                        {
                            _selectedRenameRows.Add(row);
                            border.Background = BrushSelectedBg;
                        }
                    };
                }
            }

            return border;
        }

        /// <summary>
        /// Builds the inline form that guides the user through the naming convention:
        /// CATCODE-Type_Name_Size.rfa   e.g.  ELEC-Transformer_Trihal_Schneider_100kVA.rfa
        /// </summary>
        private static Border MakeNamingHelperPanel(
            RenameRow row, Border outerBorder, HashSet<RenameRow> selectedRows)
        {
            // Parse partial proposed name to pre-fill fields.
            // Format from ProposeCompliantName: "{CAT}-Type_Name_Size" or "ELEC-Type_Name_Size"
            string nameNoExt = row.ProposedName.Replace(".rfa", "", StringComparison.OrdinalIgnoreCase);
            int dashIdx = nameNoExt.IndexOf('-');

            string preCat  = dashIdx > 0 ? nameNoExt[..dashIdx] : "";
            string body    = dashIdx > 0 ? nameNoExt[(dashIdx + 1)..] : nameNoExt;

            // If category is the placeholder, clear it so the field shows empty
            if (preCat == "{CAT}") preCat = "";

            // Split body by underscores: first token = Type, last = Size (if looks like a rating),
            // middle = Name tokens
            string[] bodyParts = body.Split('_', StringSplitOptions.RemoveEmptyEntries);
            string preType = bodyParts.Length > 0 ? bodyParts[0] : "";
            string preSize = "";
            var    nameTokens = new List<string>();

            if (bodyParts.Length > 1)
            {
                // Last token is size if it starts with a digit or looks like DN\d+
                string last = bodyParts[^1];
                bool looksLikeSize = char.IsDigit(last[0])
                    || last.StartsWith("DN", StringComparison.OrdinalIgnoreCase);

                int nameEnd = looksLikeSize ? bodyParts.Length - 1 : bodyParts.Length;
                nameTokens.AddRange(bodyParts[1..nameEnd]);
                if (looksLikeSize) preSize = last;
            }

            string preName = string.Join("_", nameTokens);

            var txCat  = MakeHelperTextBox(preCat,  "ELEC · LIGHT · MECH · PLUMB · ARCH · FIRE");
            var txType = MakeHelperTextBox(preType,  "e.g. Transformer, Panel, AHU");
            var txName = MakeHelperTextBox(preName,  "e.g. Trihal_Schneider_DryType  (use _ to separate)");
            var txSize = MakeHelperTextBox(preSize,  "e.g. 100kVA, 400A, 150mm  (optional)");

            var previewTb = new TextBlock { FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };

            void Rebuild()
            {
                string cat  = txCat.Text.Trim().ToUpperInvariant();
                string type = txType.Text.Trim();
                string name = txName.Text.Trim().Replace(' ', '_');
                string size = txSize.Text.Trim();

                bool valid = !string.IsNullOrEmpty(cat) && cat != "{CAT}"
                          && !string.IsNullOrEmpty(type);

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(type)) parts.Add(Capitalise(type));
                if (!string.IsNullOrEmpty(name)) parts.AddRange(
                    name.Split('_', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => char.IsDigit(t[0]) ? t : Capitalise(t)));
                if (!string.IsNullOrEmpty(size)) parts.Add(size);

                string catDisplay = string.IsNullOrEmpty(cat) ? "{CAT}" : cat;
                string bodyStr    = parts.Count > 0 ? string.Join("_", parts) : "Family";

                previewTb.Text       = $"→  {catDisplay}-{bodyStr}.rfa";
                previewTb.Foreground = valid ? BrushSuccess : BrushWarn;
            }

            txCat.TextChanged  += (_, _) => Rebuild();
            txType.TextChanged += (_, _) => Rebuild();
            txName.TextChanged += (_, _) => Rebuild();
            txSize.TextChanged += (_, _) => Rebuild();
            Rebuild();

            var applyBtn = PickerHelper.MakeButton("Apply", (object _, MouseButtonEventArgs _) =>
            {
                string cat  = txCat.Text.Trim().ToUpperInvariant();
                string type = txType.Text.Trim();
                string name = txName.Text.Trim().Replace(' ', '_');
                string size = txSize.Text.Trim();

                if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(type))
                {
                    previewTb.Text       = "⚠  Category Code and Type are required.";
                    previewTb.Foreground = BrushError;
                    return;
                }

                var parts = new List<string>();
                parts.Add(Capitalise(type));
                if (!string.IsNullOrEmpty(name)) parts.AddRange(
                    name.Split('_', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => char.IsDigit(t[0]) ? t : Capitalise(t)));
                if (!string.IsNullOrEmpty(size)) parts.Add(size);

                row.ProposedName = $"{cat}-{string.Join("_", parts)}.rfa";

                if (!selectedRows.Contains(row)) selectedRows.Add(row);
                outerBorder.Background = BrushSelectedBg;
            }, height: 28);

            // Input grid — CAT | gap | TYPE | gap | NAME (wide) | gap | SIZE
            var inputGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var w in new GridLength[]
            {
                new(70), new(8), new(120), new(8),
                new(1, GridUnitType.Star), new(8), new(120)
            })
                inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

            void AddField(int col, System.Windows.Controls.TextBox tb, string label)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 9, Foreground = BrushTxtSec, Margin = new Thickness(0, 0, 0, 2)
                });
                sp.Children.Add(tb);
                Grid.SetColumn(sp, col);
                inputGrid.Children.Add(sp);
            }

            AddField(0, txCat,  "CATEGORY");
            AddField(2, txType, "TYPE");
            AddField(4, txName, "NAME (brand / model)");
            AddField(6, txSize, "SIZE (rating)");

            // Preview row with Apply button
            var previewRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(previewTb, 0);
            previewRow.Children.Add(previewTb);

            var applyWrapper = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            applyWrapper.Children.Add(applyBtn);
            Grid.SetColumn(applyWrapper, 1);
            previewRow.Children.Add(applyWrapper);

            var content = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            content.Children.Add(new TextBlock
            {
                Text = "Convention:  CATCODE-Type_Name_Size.rfa  " +
                       "e.g.  ELEC-Transformer_Trihal_Schneider_100kVA.rfa",
                FontSize = 10, Foreground = BrushTxtSec, Margin = new Thickness(0, 0, 0, 6)
            });
            content.Children.Add(inputGrid);
            content.Children.Add(previewRow);

            return new Border
            {
                Padding = new Thickness(8, 6, 8, 8),
                Background = BrushHelperBg,
                BorderBrush = BrushInfo, BorderThickness = new Thickness(0, 1, 0, 0),
                Child = content
            };
        }

        private static System.Windows.Controls.TextBox MakeHelperTextBox(string text, string toolTip) =>
            new System.Windows.Controls.TextBox
            {
                Text = text, FontSize = 11, FontFamily = AppFont,
                Background = Brushes.White, Padding = new Thickness(4, 3, 4, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = toolTip
            };

        private static string Capitalise(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        private static Grid MakeRenameRowGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            return grid;
        }

        private void BuildRenameActionBar()
        {
            RenameScanContainer.Children.Clear();
            RenameScanContainer.Children.Add(
                PickerHelper.MakeButton("↺ Re-scan", (object s, MouseButtonEventArgs e) =>
                {
                    string folder = TxtRenameFolderPath.Text;
                    if (!string.IsNullOrEmpty(folder)) ScanAndBuildRenameTable(folder);
                }));

            RenameActionContainer.Children.Clear();
            RenameActionContainer.Children.Add(
                PickerHelper.MakeButton("Rename selected", RenameSelected_Click));
        }

        private void RenameSelected_Click(object sender, MouseButtonEventArgs e)
        {
            var ops = _selectedRenameRows
                .Where(r => !string.IsNullOrWhiteSpace(r.ProposedName) && !r.IsCompliant
                            && !r.ProposedName.Contains("{CAT}"))
                .Select(r => new RenameOperation(r.CurrentPath, r.ProposedName))
                .ToList();

            if (ops.Count == 0)
            {
                TxtRenameStatus.Text =
                    "No rows ready to rename. For \"→ rename\" rows: click them to select (blue = selected). " +
                    "For \"⚠ needs input\" rows: click \"▸ fill in\", fill the form, then click Apply.";
                return;
            }

            var result = FamilyRenameService.BatchRename(ops);
            string errMsg = result.Errors.Count > 0
                ? $"\nErrors:\n{string.Join("\n", result.Errors.Take(10))}"
                : "";
            TxtRenameStatus.Text = $"Renamed: {result.Renamed}  Skipped: {result.Skipped}{errMsg}";

            string folder = TxtRenameFolderPath.Text;
            if (!string.IsNullOrEmpty(folder)) ScanAndBuildRenameTable(folder);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────

        private bool EnsureFilesSelected()
        {
            if (_selectedFiles.Count > 0) return true;
            Autodesk.Revit.UI.TaskDialog.Show("BIM Command Centre", "Please add .rfa files first.");
            return false;
        }

        private List<FamilyParameter> GetRequiredParameters()
        {
            int tier = _parameterFilter.Value switch
            {
                "Tier 1 only" => 1,
                "All tiers"   => 3,
                _             => 2
            };
            return ConfigService.GetDefaultParameters(tier);
        }

        private static SolidColorBrush LevelToColor(UpdateFamiliesEventHandler.LogLevel level) => level switch
        {
            UpdateFamiliesEventHandler.LogLevel.Success => BrushSuccess,
            UpdateFamiliesEventHandler.LogLevel.Warning => BrushWarn,
            UpdateFamiliesEventHandler.LogLevel.Error   => BrushError,
            _                                           => BrushInfo
        };

        // These run on the UI thread (called from BeginInvoke callbacks or directly from UI thread handlers)
        private void AppendLog(string message, SolidColorBrush colour)
        {
            LogPanel.Children.Add(new TextBlock
            {
                Text = message, FontFamily = ConsolasFont, FontSize = 11,
                Foreground = colour, Padding = new Thickness(0, 1, 0, 1)
            });
            LogScrollViewer.ScrollToBottom();
        }

        private void ClearLog()    => LogPanel.Children.Clear();
        private void ResetStats()  { _total = 0; UpdateStats(0, 0, 0, 0); }

        private void UpdateStats(int updated, int skipped, int errors, int total)
        {
            StatTotal.Text   = total.ToString();
            StatUpdated.Text = updated.ToString();
            StatSkipped.Text = skipped.ToString();
            StatErrors.Text  = errors.ToString();
        }
    }

    // ── rename row model ──────────────────────────────────────────────────────────────────

    public class RenameRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string CurrentPath { get; }
        public string CurrentName { get; }
        public string Status      { get; }
        public bool   CanEdit     { get; }
        public bool   IsCompliant { get; }

        private string _proposedName;
        public string ProposedName
        {
            get => _proposedName;
            set { _proposedName = value; OnPropertyChanged(nameof(ProposedName)); }
        }

        private bool _applyRename;
        public bool ApplyRename
        {
            get => _applyRename;
            set { _applyRename = value; OnPropertyChanged(nameof(ApplyRename)); }
        }

        public RenameRow(RenameCandidate candidate)
        {
            CurrentPath   = candidate.CurrentPath;
            CurrentName   = System.IO.Path.GetFileName(candidate.CurrentPath);
            _proposedName = candidate.ProposedName;
            IsCompliant   = candidate.IsCompliant;
            CanEdit       = !candidate.IsCompliant;
            _applyRename  = !candidate.IsCompliant && !candidate.NeedsManualInput;
            Status = candidate.IsCompliant      ? "✓ compliant"
                   : candidate.NeedsManualInput ? "⚠ needs input"
                   : "→ rename";
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
