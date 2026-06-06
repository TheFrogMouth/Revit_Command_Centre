using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Autodesk.Revit.UI;
using BimParameter = Revit_Command_Centre.Models.FamilyParameter;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Runs ValidateFamily or AddParametersToFamily on a list of .rfa files
    /// inside Revit's API context. Progress and results are posted back to the
    /// UI thread via Dispatcher.BeginInvoke so the view can update live.
    /// </summary>
    public class UpdateFamiliesEventHandler : IExternalEventHandler
    {
        public enum OperationMode { Validate, BatchProcess }
        public enum LogLevel      { Info, Success, Warning, Error }

        public OperationMode       Mode         { get; set; }
        public List<string>?       FilePaths    { get; set; }
        public List<BimParameter>? Parameters   { get; set; }
        public string?             OutputFolder { get; set; }
        public string?             NamePrefix   { get; set; }

        /// <summary>Called on UI thread with a log message and its severity.</summary>
        public Action<string, LogLevel>? OnLog      { get; set; }
        /// <summary>Called on UI thread: (currentFileName, currentIndex, totalCount).</summary>
        public Action<string, int, int>? OnProgress { get; set; }
        /// <summary>Called on UI thread at the end: (updated, skipped, errors).</summary>
        public Action<int, int, int>?    OnComplete  { get; set; }

        private static void PostToUi(Action action) =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try { action(); }
                catch { /* don't propagate to Revit's dispatcher */ }
            });

        public void Execute(UIApplication app)
        {
            try { ExecuteCore(app); }
            catch (Exception ex)
            {
                string msg = ex.Message;
                PostToUi(() =>
                {
                    OnLog?.Invoke($"Fatal error: {msg}", LogLevel.Error);
                    OnComplete?.Invoke(0, 0, 1);
                });
            }
        }

        private void ExecuteCore(UIApplication app)
        {
            var files      = FilePaths;
            var parameters = Parameters;
            if (files == null || files.Count == 0 || parameters == null) return;

            if (Mode == OperationMode.Validate)
            {
                PostToUi(() => OnLog?.Invoke("→ Validation started…", LogLevel.Info));
                int warnings = 0;

                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    string name = Path.GetFileName(file);
                    int    cur  = i + 1, tot = files.Count;
                    PostToUi(() => OnProgress?.Invoke(name, cur, tot));

                    try
                    {
                        var missing = FamilyParameterService.ValidateFamily(app, file, parameters);
                        if (missing.Count == 0)
                        {
                            PostToUi(() => OnLog?.Invoke($"  ✓ {name}", LogLevel.Success));
                        }
                        else
                        {
                            string missingStr = string.Join(", ", missing);
                            PostToUi(() => OnLog?.Invoke($"  ⚠ {name} — missing: {missingStr}", LogLevel.Warning));
                            warnings++;
                        }
                    }
                    catch (Exception ex)
                    {
                        string err = ex.Message;
                        PostToUi(() => OnLog?.Invoke($"  ✗ {name} — {err}", LogLevel.Error));
                        warnings++;
                    }
                }

                int total = files.Count, w = warnings;
                PostToUi(() =>
                {
                    OnLog?.Invoke($"→ Validation complete. {total} files, {w} with warnings.", LogLevel.Info);
                    OnProgress?.Invoke("Done", total, total);
                    OnComplete?.Invoke(0, 0, w);
                });
            }
            else
            {
                PostToUi(() => OnLog?.Invoke("→ Batch process started…", LogLevel.Info));
                int updated = 0, skipped = 0, errors = 0;

                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    string name = Path.GetFileName(file);
                    int    cur  = i + 1, tot = files.Count;
                    PostToUi(() => OnProgress?.Invoke(name, cur, tot));

                    string? outPath = ResolveOutputPath(file, OutputFolder, NamePrefix);
                    try
                    {
                        FamilyParameterService.AddParametersToFamily(app, file, parameters, outPath);
                        string suffix = outPath != null ? $" → {Path.GetFileName(outPath)}" : "";
                        PostToUi(() => OnLog?.Invoke($"  ✓ {name}{suffix}", LogLevel.Success));
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        string err = ex.Message;
                        PostToUi(() => OnLog?.Invoke($"  ✗ {name} — {err}", LogLevel.Error));
                        errors++;
                    }

                    int u = updated, s = skipped, e2 = errors;
                    PostToUi(() => OnComplete?.Invoke(u, s, e2));
                }

                int fu = updated, fs = skipped, fe = errors;
                PostToUi(() =>
                {
                    OnLog?.Invoke(
                        $"→ Batch complete. Updated: {fu}  Skipped: {fs}  Errors: {fe}",
                        fe > 0 ? LogLevel.Warning : LogLevel.Success);
                    OnProgress?.Invoke("Done", fu + fs + fe, files.Count);
                    OnComplete?.Invoke(fu, fs, fe);
                });
            }
        }

        private static string? ResolveOutputPath(string sourceFile, string? outputFolder, string? prefix)
        {
            string outDir  = !string.IsNullOrEmpty(outputFolder)
                ? outputFolder
                : Path.GetDirectoryName(sourceFile) ?? "";
            string outName = string.IsNullOrEmpty(prefix)
                ? Path.GetFileName(sourceFile)
                : $"{prefix}_{Path.GetFileName(sourceFile)}";
            string outPath = Path.Combine(outDir, outName);
            return outPath.Equals(sourceFile, StringComparison.OrdinalIgnoreCase) ? null : outPath;
        }

        public string GetName() => "BIM Command Centre — Update Families";
    }
}
