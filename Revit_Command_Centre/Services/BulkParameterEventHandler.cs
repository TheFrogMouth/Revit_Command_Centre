using System;
using System.Collections.Generic;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Three-mode handler for the Bulk Parameter editor.
    ///   FetchCategories — returns category list to UI via callback.
    ///   FetchGrid       — builds the parameter grid for a selected category.
    ///   Commit          — writes all pending edits in a single Transaction.
    /// BeginInvoke used for all callbacks to avoid blocking Revit's API thread.
    /// </summary>
    public class BulkParameterEventHandler : IExternalEventHandler
    {
        public enum OperationMode { FetchCategories, FetchGrid, Commit }

        public OperationMode Mode { get; set; }
        public ElementId?    SelectedCategoryId  { get; set; }
        public List<string>? SelectedParameters  { get; set; }
        public List<CellEdit>? PendingEdits      { get; set; }

        public Action<List<(ElementId Id, string Name, int Count)>>? OnCategoriesLoaded { get; set; }
        public Action<List<ParameterInfo>, List<ParameterRow>>?      OnGridLoaded       { get; set; }
        public Action<CommitResult>?                                  OnCommitComplete   { get; set; }

        public void Execute(UIApplication app)
        {
            try { ExecuteCore(app); }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre",
                    $"Bulk parameter operation failed:\n\n{ex.Message}");
            }
        }

        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show("BIM Command Centre", "No project is open.");
                return;
            }

            switch (Mode)
            {
                case OperationMode.FetchCategories:
                {
                    var cats = BulkParameterService.GetCategories(doc);
                    Application.Current.Dispatcher.BeginInvoke(
                        () => OnCategoriesLoaded?.Invoke(cats));
                    break;
                }

                case OperationMode.FetchGrid:
                {
                    if (SelectedCategoryId == null) return;
                    var parms = BulkParameterService.GetParameters(doc, SelectedCategoryId);
                    var rows  = BulkParameterService.BuildGrid(
                        doc, SelectedCategoryId, SelectedParameters ?? new List<string>());
                    Application.Current.Dispatcher.BeginInvoke(
                        () => OnGridLoaded?.Invoke(parms, rows));
                    break;
                }

                case OperationMode.Commit:
                {
                    if (PendingEdits == null || PendingEdits.Count == 0) return;
                    var result = BulkParameterService.CommitChanges(doc, PendingEdits);
                    Application.Current.Dispatcher.BeginInvoke(
                        () => OnCommitComplete?.Invoke(result));
                    break;
                }
            }
        }

        public string GetName() => "BIM Command Centre — Bulk Parameters";
    }
}
