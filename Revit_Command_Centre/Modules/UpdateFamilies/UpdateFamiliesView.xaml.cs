using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre.Modules.UpdateFamilies
{
    public partial class UpdateFamiliesView : UserControl
    {
        private readonly UIApplication _uiApp;
        private readonly List<string> _selectedFiles = new();
        private string _outputFolder = string.Empty;

        private int _total, _updated, _skipped, _errors;

        private static readonly SolidColorBrush BrushInfo    = new(Color.FromRgb(0x18, 0x5F, 0xA5));
        private static readonly SolidColorBrush BrushSuccess = new(Color.FromRgb(0x1D, 0x9E, 0x75));
        private static readonly SolidColorBrush BrushWarn    = new(Color.FromRgb(0xBA, 0x75, 0x17));
        private static readonly SolidColorBrush BrushError   = new(Color.FromRgb(0xE2, 0x4B, 0x4A));
        private static readonly FontFamily      ConsolasFont = new("Consolas");

        static UpdateFamiliesView()
        {
            BrushInfo.Freeze();
            BrushSuccess.Freeze();
            BrushWarn.Freeze();
            BrushError.Freeze();
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
        }

        // ── drop zone ─────────────────────────────────────────────────────────────────────────────

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

        // ── output folder ──────────────────────────────────────────────────────────────────────────

        private void OutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title      = "Select output folder for processed families",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            _outputFolder = dlg.FolderName;
            TxtOutputFolder.Text = _outputFolder;

            var settings = AppSettingsService.Load();
            settings.DefaultFamilyOutputFolder = _outputFolder;
            AppSettingsService.Save(settings);
        }

        private void OutputFolder_Click(object sender, MouseButtonEventArgs e)
        {
            OutputFolder_Click(sender, (RoutedEventArgs)e);
        }

        // ── file list ──────────────────────────────────────────────────────────────────────────────

        private void AddFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!_selectedFiles.Contains(path))
                    _selectedFiles.Add(path);
            }
            RefreshFileList();
        }

        private void ClearFiles_Click(object sender, RoutedEventArgs e)
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
                string original  = Path.GetFileName(path);
                string renamed   = BuildOutputName(prefix, original);
                string label     = string.IsNullOrEmpty(prefix) ? original : $"{original}  →  {renamed}";

                var row = new TextBlock
                {
                    Text       = label,
                    FontSize   = 11,
                    Foreground = string.IsNullOrEmpty(prefix)
                                     ? (Brush)FindResource("TextSecondaryBrush")
                                     : BrushInfo,
                    Padding    = new Thickness(0, 2, 0, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                FileListPanel.Children.Add(row);
            }

            TxtNamePreview.Text = _selectedFiles.Count > 0 && !string.IsNullOrEmpty(prefix)
                ? $"e.g. {BuildOutputName(prefix, Path.GetFileName(_selectedFiles[0]))}"
                : string.Empty;

            UpdateStats(0, 0, 0, _selectedFiles.Count);
        }

        private static string BuildOutputName(string prefix, string originalFileName)
        {
            if (string.IsNullOrEmpty(prefix)) return originalFileName;
            return $"{prefix}_{originalFileName}";
        }

        // ── public API (called by MainView) ────────────────────────────────────────────────────────

        public void RunValidation()
        {
            if (!EnsureFilesSelected()) return;

            ClearLog();
            AppendLog("→ Validation started…", BrushInfo);

            List<FamilyParameter> required = GetRequiredParameters();
            int warnings = 0;

            foreach (string file in _selectedFiles)
            {
                try
                {
                    List<string> missing = FamilyParameterService.ValidateFamily(_uiApp, file, required);
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

            List<FamilyParameter> parameters = GetRequiredParameters();
            string prefix = TxtNamePrefix.Text.Trim();
            _total = _selectedFiles.Count;
            UpdateStats(0, 0, 0, _total);

            int processed = 0;
            foreach (string file in _selectedFiles)
            {
                processed++;
                double pct      = (double)processed / _total * 100.0;
                string shortName = Path.GetFileName(file);

                Dispatcher.Invoke(() =>
                {
                    TxtCurrentFile.Text = shortName;
                    TxtProgress.Text    = $"{processed}/{_total}";
                    ProgressBar.Value   = pct;
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
                ProgressBar.Value   = 100;
            });

            AppendLog($"→ Batch complete. Updated: {_updated}  Skipped: {_skipped}  Errors: {_errors}", BrushInfo);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the full output path for a family file.
        /// If no output folder and no prefix are set, returns null (save in-place).
        /// </summary>
        private string? ResolveOutputPath(string sourceFile, string prefix)
        {
            string outDir  = !string.IsNullOrEmpty(_outputFolder) ? _outputFolder : Path.GetDirectoryName(sourceFile)!;
            string outName = BuildOutputName(prefix, Path.GetFileName(sourceFile));
            string outPath = Path.Combine(outDir, outName);

            // if nothing changed, treat as in-place save
            return outPath == sourceFile ? null : outPath;
        }

        private bool EnsureFilesSelected()
        {
            if (_selectedFiles.Count == 0)
            {
                MessageBox.Show("Please add .rfa files first.", "BIM Command Centre",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            return true;
        }

        private List<FamilyParameter> GetRequiredParameters()
        {
            string filter = (CmbParameterFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "From project config";
            int tier = filter switch
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
                var tb = new TextBlock
                {
                    Text       = message,
                    FontFamily = ConsolasFont,
                    FontSize   = 11,
                    Foreground = colour,
                    Padding    = new Thickness(0, 1, 0, 1)
                };
                LogPanel.Children.Add(tb);
                LogScrollViewer.ScrollToBottom();
            });
        }

        private void ClearLog() => Dispatcher.Invoke(() => LogPanel.Children.Clear());

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
}
