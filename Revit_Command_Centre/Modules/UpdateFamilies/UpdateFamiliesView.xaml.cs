using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private int _total, _updated, _skipped, _errors;
        private double _progressBarTotalWidth;

        private readonly Picker _categoryFilter  = new(new[] { "All", "Architectural", "Structural", "MEP" });
        private readonly Picker _parameterFilter = new(new[] { "From project config", "Tier 1 only", "All tiers" });

        // Rename mode
        private enum ViewMode { UpdateParams, Rename }
        private ViewMode _currentMode = ViewMode.UpdateParams;
        private readonly Picker _modePicker = new(new[] { "Update Parameters", "Rename to Convention" });
        private List<RenameCandidate> _renameCandidates = new();

        private static readonly SolidColorBrush BrushInfo    = new(Color.FromRgb(0x18, 0x5F, 0xA5));
        private static readonly SolidColorBrush BrushSuccess = new(Color.FromRgb(0x1D, 0x9E, 0x75));
        private static readonly SolidColorBrush BrushWarn    = new(Color.FromRgb(0xBA, 0x75, 0x17));
        private static readonly SolidColorBrush BrushError   = new(Color.FromRgb(0xE2, 0x4B, 0x4A));
        private static readonly SolidColorBrush BrushBorder  = new(Color.FromArgb(0x1E, 0, 0, 0));
        private static readonly SolidColorBrush BrushTxtPri  = new(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly SolidColorBrush BrushTxtSec  = new(Color.FromRgb(0x6B, 0x6B, 0x6B));
        private static readonly FontFamily      ConsolasFont = new("Consolas");
        private static readonly FontFamily      AppFont      = new("Segoe UI");

        static UpdateFamiliesView()
        {
            BrushInfo.Freeze();    BrushSuccess.Freeze();
            BrushWarn.Freeze();    BrushError.Freeze();
            BrushBorder.Freeze();  BrushTxtPri.Freeze();
            BrushTxtSec.Freeze();
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

            // Mode switcher
            PickerHelper.Refresh(ModeSwitcherContainer, _modePicker, OnModeChanged);

            // Rename panel
            BrowseRenameFolderContainer.Children.Add(
                PickerHelper.MakeButton("Browse…", BrowseRenameFolder_Click));
            BuildRenameGrid();
            BuildRenameActionBar();
        }

        // ── mode toggle ───────────────────────────────────────────────────────────────────

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
            ClearLog();
            AppendLog("→ Validation started…", BrushInfo);
            var required = GetRequiredParameters();
            int warnings = 0;

            foreach (string file in _selectedFiles)
            {
                try
                {
                    var missing = FamilyParameterService.ValidateFamily(_uiApp, file, required);
                    if (missing.Count == 0)
                        AppendLog($"  ✓ {Path.GetFileName(file)}", BrushSuccess);
                    else
                    {
                        AppendLog($"  ⚠ {Path.GetFileName(file)} — missing: {string.Join(", ", missing)}", BrushWarn);
                        warnings++;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"  ✗ {Path.GetFileName(file)} — {ex.Message}", BrushError);
                    _errors++;
                }
            }
            AppendLog($"→ Validation complete. {_selectedFiles.Count} files checked, {warnings} warnings.", BrushInfo);
        }

        public void RunBatchProcess()
        {
            if (!EnsureFilesSelected()) return;
            ClearLog();
            ResetStats();
            AppendLog("→ Batch process started…", BrushInfo);

            var parameters = GetRequiredParameters();
            string prefix  = TxtNamePrefix.Text.Trim();
            _total = _selectedFiles.Count;
            UpdateStats(0, 0, 0, _total);

            int processed = 0;
            foreach (string file in _selectedFiles)
            {
                processed++;
                double pct       = (double)processed / _total * 100.0;
                string shortName = Path.GetFileName(file);

                Dispatcher.Invoke(() =>
                {
                    TxtCurrentFile.Text = shortName;
                    TxtProgress.Text    = $"{processed}/{_total}";
                    ProgressFill.Width  = _progressBarTotalWidth * pct / 100.0;
                });

                string? outputPath = ResolveOutputPath(file, prefix);
                try
                {
                    FamilyParameterService.AddParametersToFamily(_uiApp, file, parameters, outputPath);
                    AppendLog($"  ✓ {shortName}{(outputPath != null ? $" → {Path.GetFileName(outputPath)}" : "")}", BrushSuccess);
                    _updated++;
                }
                catch (Exception ex)
                {
                    AppendLog($"  ✗ {shortName} — {ex.Message}", BrushError);
                    _errors++;
                }
                UpdateStats(_updated, _skipped, _errors, _total);
            }

            Dispatcher.Invoke(() =>
            {
                TxtCurrentFile.Text = "Done";
                TxtProgress.Text    = $"{_total}/{_total}";
                ProgressFill.Width  = _progressBarTotalWidth;
            });
            AppendLog($"→ Batch complete. Updated: {_updated}  Skipped: {_skipped}  Errors: {_errors}", BrushInfo);
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
            PopulateRenameGrid();
            int total      = _renameCandidates.Count;
            int compliant  = _renameCandidates.Count(c => c.IsCompliant);
            int needsInput = _renameCandidates.Count(c => c.NeedsManualInput);
            int autoRename = total - compliant - needsInput;
            TxtRenameStatus.Text =
                $"{total} files found: {compliant} compliant, {autoRename} auto-rename, {needsInput} need manual input.";
        }

        private void BuildRenameGrid()
        {
            // Columns: Current Name | Proposed Name | Status | Apply
            // AutoGenerateColumns=False, no ControlTemplate usage
            RenameGrid.Columns.Clear();

            RenameGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Current Name",
                Binding = new System.Windows.Data.Binding("CurrentName") { Mode = System.Windows.Data.BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            RenameGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Proposed Name",
                Binding = new System.Windows.Data.Binding("ProposedName"),
                IsReadOnly = false,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            RenameGrid.Columns.Add(new DataGridTextColumn
            {
                Header  = "Status",
                Binding = new System.Windows.Data.Binding("Status") { Mode = System.Windows.Data.BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(160)
            });
            RenameGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header  = "Apply",
                Binding = new System.Windows.Data.Binding("ApplyRename"),
                Width   = new DataGridLength(60)
            });
        }

        private void PopulateRenameGrid()
        {
            RenameGrid.ItemsSource = _renameCandidates
                .Select(c => new RenameRow(c))
                .ToList();
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
            if (RenameGrid.ItemsSource is not List<RenameRow> rows) return;

            var ops = rows
                .Where(r => r.ApplyRename && !string.IsNullOrWhiteSpace(r.ProposedName))
                .Select(r => new RenameOperation(r.CurrentPath, r.ProposedName))
                .ToList();

            if (ops.Count == 0)
            {
                TxtRenameStatus.Text = "No rows checked. Tick the Apply checkbox for rows you want to rename.";
                return;
            }

            // Note: this renames .rfa files on disk only — does not affect already-loaded Revit families
            var result = FamilyRenameService.BatchRename(ops);
            string errMsg = result.Errors.Count > 0
                ? $"\nErrors:\n{string.Join("\n", result.Errors.Take(10))}"
                : "";
            TxtRenameStatus.Text = $"Renamed: {result.Renamed}  Skipped: {result.Skipped}{errMsg}";

            // Re-scan to refresh the table
            string folder = TxtRenameFolderPath.Text;
            if (!string.IsNullOrEmpty(folder)) ScanAndBuildRenameTable(folder);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────

        private string? ResolveOutputPath(string sourceFile, string prefix)
        {
            string outDir  = !string.IsNullOrEmpty(_outputFolder) ? _outputFolder : Path.GetDirectoryName(sourceFile)!;
            string outName = BuildOutputName(prefix, Path.GetFileName(sourceFile));
            string outPath = Path.Combine(outDir, outName);
            return outPath == sourceFile ? null : outPath;
        }

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

        private void AppendLog(string message, SolidColorBrush colour)
        {
            Dispatcher.Invoke(() =>
            {
                LogPanel.Children.Add(new TextBlock
                {
                    Text = message, FontFamily = ConsolasFont, FontSize = 11,
                    Foreground = colour, Padding = new Thickness(0, 1, 0, 1)
                });
                LogScrollViewer.ScrollToBottom();
            });
        }

        private void ClearLog()   => Dispatcher.Invoke(() => LogPanel.Children.Clear());
        private void ResetStats() { _updated = 0; _skipped = 0; _errors = 0; }

        private void UpdateStats(int updated, int skipped, int errors, int total)
        {
            Dispatcher.Invoke(() =>
            {
                StatTotal.Text   = total.ToString();
                StatUpdated.Text = updated.ToString();
                StatSkipped.Text = skipped.ToString();
                StatErrors.Text  = errors.ToString();
            });
        }
    }

    // ── rename grid row model ─────────────────────────────────────────────────────────────

    public class RenameRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string CurrentPath { get; }
        public string CurrentName { get; }
        public string Status      { get; }
        public bool   CanEdit     { get; }

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
            CanEdit       = !candidate.IsCompliant;
            _applyRename  = !candidate.IsCompliant && !candidate.NeedsManualInput;
            Status = candidate.IsCompliant    ? "✓ compliant"
                   : candidate.NeedsManualInput ? "⚠ needs input"
                   : "→ rename";
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
