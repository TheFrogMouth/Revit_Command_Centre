using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre.Modules.UpdateFamilies
{
    /// <summary>
    /// Code-behind for the Update Families module.
    /// Manages drag-and-drop folder selection, batch processing with progress reporting,
    /// stats counters, and a colour-coded log panel.
    /// </summary>
    public partial class UpdateFamiliesView : UserControl
    {
        private readonly UIApplication _uiApp;
        private string _selectedFolder = string.Empty;

        // Running counters reset on each Run
        private int _total, _updated, _skipped, _errors;

        public UpdateFamiliesView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
        }

        // ──────────────────────────────────────  drop zone  ───────────────────────────────────────

        /// <summary>Called when user clicks the drop zone to browse for a folder.</summary>
        private void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectFolder();
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[]? paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            string dropped = paths[0];
            if (Directory.Exists(dropped))
                SetFolder(dropped);
            else if (File.Exists(dropped))
                SetFolder(Path.GetDirectoryName(dropped) ?? string.Empty);
        }

        private void SelectFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a folder containing .rfa files",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
                SetFolder(dlg.FolderName);
        }

        private void SetFolder(string path)
        {
            _selectedFolder = path;
            TxtSelectedFolder.Text = path;
            AppendLog($"→ Folder selected: {path}", "#185FA5");

            string[] files = Directory.GetFiles(path, "*.rfa", SearchOption.AllDirectories);
            _total = files.Length;
            UpdateStats(0, 0, 0, _total);
        }

        // ──────────────────────────────────────  public API (called by MainWindow)  ───────────────

        /// <summary>
        /// Validates all .rfa files in the selected folder without modifying them.
        /// Reports missing parameters to the log.
        /// </summary>
        public void RunValidation()
        {
            if (!EnsureFolderSelected()) return;

            ClearLog();
            AppendLog("→ Validation started…", "#185FA5");

            List<FamilyParameter> required = GetRequiredParameters();
            string[] files = Directory.GetFiles(_selectedFolder, "*.rfa", SearchOption.AllDirectories);

            int warnings = 0;
            foreach (string file in files)
            {
                try
                {
                    List<string> missing = FamilyParameterService.ValidateFamily(_uiApp, file, required);
                    if (missing.Count == 0)
                        AppendLog($"  ✓ {Path.GetFileName(file)}", "#1D9E75");
                    else
                    {
                        AppendLog($"  ⚠ {Path.GetFileName(file)} — missing: {string.Join(", ", missing)}", "#BA7517");
                        warnings++;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"  ✗ {Path.GetFileName(file)} — {ex.Message}", "#E24B4A");
                    _errors++;
                }
            }

            AppendLog($"→ Validation complete. {files.Length} files checked, {warnings} warnings.", "#185FA5");
        }

        /// <summary>
        /// Runs the full batch process — opens each .rfa, adds missing parameters, saves.
        /// Progress is reported on the UI thread via Dispatcher.
        /// </summary>
        public void RunBatchProcess()
        {
            if (!EnsureFolderSelected()) return;

            ClearLog();
            ResetStats();
            AppendLog("→ Batch process started…", "#185FA5");

            List<FamilyParameter> parameters = GetRequiredParameters();
            string[] files = Directory.GetFiles(_selectedFolder, "*.rfa", SearchOption.AllDirectories);

            _total = files.Length;
            UpdateStats(0, 0, 0, _total);

            int processed = 0;
            foreach (string file in files)
            {
                processed++;
                double pct = (double)processed / _total * 100.0;
                string shortName = Path.GetFileName(file);

                Dispatcher.Invoke(() =>
                {
                    TxtCurrentFile.Text = shortName;
                    TxtProgress.Text    = $"{processed}/{_total}";
                    ProgressBar.Value   = pct;
                });

                try
                {
                    string result = FamilyParameterService.AddParametersToFamily(_uiApp, file, parameters);
                    AppendLog($"  ✓ {shortName}", "#1D9E75");
                    _updated++;
                }
                catch (Exception ex)
                {
                    AppendLog($"  ✗ {shortName} — {ex.Message}", "#E24B4A");
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

            AppendLog($"→ Batch complete. Updated: {_updated}  Skipped: {_skipped}  Errors: {_errors}", "#185FA5");
        }

        // ──────────────────────────────────────  helpers  ─────────────────────────────────────────

        private bool EnsureFolderSelected()
        {
            if (string.IsNullOrEmpty(_selectedFolder))
            {
                MessageBox.Show("Please select a folder first.", "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void AppendLog(string message, string hexColour)
        {
            Dispatcher.Invoke(() =>
            {
                var tb = new TextBlock
                {
                    Text       = message,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColour)),
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
