using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Models;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Runs inside Revit's API event loop so it can open a Transaction.
    /// Raised by MainView's Save &amp; Apply button via ExternalEvent.
    /// </summary>
    public class ApplyConfigEventHandler : IExternalEventHandler
    {
        public ProjectConfig? PendingConfig    { get; set; }
        public string?        RvtFilePath      { get; set; }
        public string?        TitleBlockFolder { get; set; }
        public string?        SaveAsPath       { get; set; }

        public void Execute(UIApplication app)
        {
            if (PendingConfig == null) return;
            try
            {
                ExecuteCore(app);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre",
                    $"Failed to apply project configuration:\n\n{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ExecuteCore(UIApplication app)
        {
            Document? doc = app.ActiveUIDocument?.Document;

            if (doc == null)
            {
                if (string.IsNullOrEmpty(SaveAsPath))
                {
                    TaskDialog.Show("BIM Command Centre",
                        "No project is open. Please choose a save location and try again.");
                    return;
                }

                try
                {
                    string? tmpl = app.Application.DefaultProjectTemplate;
                    doc = (!string.IsNullOrEmpty(tmpl) && File.Exists(tmpl))
                        ? app.Application.NewProjectDocument(tmpl)
                        : app.Application.NewProjectDocument(UnitSystem.Metric);
                }
                catch
                {
                    doc = app.Application.NewProjectDocument(UnitSystem.Metric);
                }

                if (doc == null)
                {
                    TaskDialog.Show("BIM Command Centre", "Failed to create a new Revit project.");
                    return;
                }
            }
            else if (doc.IsReadOnly)
            {
                TaskDialog.Show("BIM Command Centre", "The active document is read-only.");
                return;
            }

            string? rfaPath = string.IsNullOrEmpty(TitleBlockFolder)
                ? null
                : FindTitleBlockRfa(TitleBlockFolder, PendingConfig.TitleBlock);

            using (var tx = new Transaction(doc, "Apply BIM project config"))
            {
                tx.Start();
                doc.ProjectInformation.Name       = PendingConfig.ProjectName;
                doc.ProjectInformation.Number     = PendingConfig.ProjectNumber;
                doc.ProjectInformation.ClientName = PendingConfig.ClientName;
                if (rfaPath != null)
                    doc.LoadFamily(rfaPath, out _);
                try { ExtensibleStorageService.WriteConfig(doc, PendingConfig); } catch { }
                tx.Commit();
            }

            // SaveAs / rename the Revit file if a path was provided
            string saveMsg = string.Empty;
            if (!string.IsNullOrEmpty(SaveAsPath))
            {
                try
                {
                    doc.SaveAs(SaveAsPath, new SaveAsOptions { OverwriteExistingFile = true });
                    saveMsg = $"\nProject saved as: {Path.GetFileName(SaveAsPath)}";
                    try { ConfigService.SaveConfig(PendingConfig, SaveAsPath); } catch { }
                }
                catch (Exception saveEx)
                {
                    // Transaction was already committed — project info is applied in memory.
                    // Tell the user clearly so they can save manually rather than seeing a
                    // generic "Failed to apply config" message that implies nothing was done.
                    TaskDialog.Show("BIM Command Centre",
                        $"Project information was applied in Revit, but the file could not be saved to disk:\n\n{saveEx.Message}\n\nPlease save the project manually (File > Save As).");
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(RvtFilePath))
            {
                try { ConfigService.SaveConfig(PendingConfig, RvtFilePath); } catch { }
            }

            string tbMsg = rfaPath != null
                ? $"\nTitle block loaded: {Path.GetFileName(rfaPath)}"
                : (string.IsNullOrEmpty(TitleBlockFolder) ? ""
                    : "\nNo matching title block RFA found in the specified folder.");

            TaskDialog.Show("BIM Command Centre",
                $"Project information updated in Revit and config saved.{tbMsg}{saveMsg}");
        }

        public string GetName() => "BIM Command Centre — Apply Config";

        private static string? FindTitleBlockRfa(string folder, string titleBlockName)
        {
            if (!Directory.Exists(folder)) return null;
            string exact = Path.Combine(folder, $"{titleBlockName}.rfa");
            if (File.Exists(exact)) return exact;
            string key = titleBlockName
                .Replace("Standard ", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
            return Directory.GetFiles(folder, "*.rfa")
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                    .ToLowerInvariant().Contains(key));
        }
    }
}
