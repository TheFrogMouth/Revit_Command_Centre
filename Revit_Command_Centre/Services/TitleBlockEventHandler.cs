using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Two-mode handler:
    ///   FetchParameters — reads available title block parameter names, calls back the UI thread.
    ///   Apply           — shows a preview TaskDialog then commits the update in one transaction.
    /// </summary>
    public class TitleBlockEventHandler : IExternalEventHandler
    {
        public enum OperationMode { FetchParameters, Apply }

        public OperationMode Mode { get; set; } = OperationMode.FetchParameters;
        public ProjectConfig? PendingConfig { get; set; }
        public Dictionary<string, string>? FieldMapping { get; set; }
        public Action<List<string>>? OnParametersFetched { get; set; }

        public void Execute(UIApplication app)
        {
            try { ExecuteCore(app); }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre", $"Title block error:\n\n{ex.Message}");
            }
        }

        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                if (Mode == OperationMode.Apply)
                    TaskDialog.Show("BIM Command Centre", "No project is open.");
                return;
            }

            if (Mode == OperationMode.FetchParameters)
            {
                var parameters = TitleBlockService.GetTitleBlockParameters(doc);
                // BeginInvoke so we don't block the Revit API thread waiting on the UI thread
                Application.Current.Dispatcher.BeginInvoke(() => OnParametersFetched?.Invoke(parameters));
                return;
            }

            // Apply mode ───────────────────────────────────────────────────────────
            if (PendingConfig == null || FieldMapping == null) return;

            var preview = TitleBlockService.PreviewPopulation(doc, PendingConfig, FieldMapping);
            if (preview.Count == 0)
            {
                TaskDialog.Show("BIM Command Centre",
                    "No field mappings configured. Please set up the title block mapping first.");
                return;
            }

            string previewText = string.Join("\n",
                preview.Select(p => $"  {p.FieldName}: \"{p.ConfigValue}\"  →  [{p.ParameterName}]"));

            var td = new TaskDialog("Apply to Title Blocks")
            {
                MainInstruction = "This will update all sheets:",
                MainContent     = previewText,
                CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
            };

            if (td.Show() != TaskDialogResult.Yes) return;

            var result = TitleBlockService.PopulateTitleBlocks(doc, PendingConfig, FieldMapping);
            string errText = result.Errors.Count > 0 ? $"\n  Errors: {string.Join(", ", result.Errors)}" : "";
            TaskDialog.Show("BIM Command Centre",
                $"Title blocks updated.\n  Sheets updated: {result.Updated}\n  Sheets skipped: {result.Skipped}{errText}");
        }

        public string GetName() => "BIM Command Centre — Title Blocks";
    }
}
