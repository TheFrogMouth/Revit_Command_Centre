using System.IO;
using System.Linq;
using System.Windows;
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
        public string?        RvtFilePath     { get; set; }
        public string?        TitleBlockFolder { get; set; }
        /// <summary>If set, the document is saved (or renamed) to this full path after applying config.</summary>
        public string?        SaveAsPath      { get; set; }

        public void Execute(UIApplication app)
        {
            if (PendingConfig == null) return;

            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsReadOnly)
            {
                MessageBox.Show(
                    doc == null
                        ? "No project is open. Open a Revit project and try again."
                        : "The active document is read-only.",
                    "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? rfaPath = string.IsNullOrEmpty(TitleBlockFolder)
                ? null
                : FindTitleBlockRfa(TitleBlockFolder, PendingConfig.TitleBlock);

            using var tx = new Transaction(doc, "Apply BIM project config");
            tx.Start();

            // Project information
            doc.ProjectInformation.Name       = PendingConfig.ProjectName;
            doc.ProjectInformation.Number     = PendingConfig.ProjectNumber;
            doc.ProjectInformation.ClientName = PendingConfig.ClientName;

            // Load title block family if a matching RFA was found
            if (rfaPath != null)
                doc.LoadFamily(rfaPath, out _);

            ExtensibleStorageService.WriteConfig(doc, PendingConfig);
            tx.Commit();

            // SaveAs / rename the Revit file if a path was provided
            string saveMsg = string.Empty;
            if (!string.IsNullOrEmpty(SaveAsPath))
            {
                var opts = new SaveAsOptions { OverwriteExistingFile = true };
                doc.SaveAs(SaveAsPath, opts);
                saveMsg = $"\nProject saved as: {System.IO.Path.GetFileName(SaveAsPath)}";
                // Update the sidecar JSON next to the new file
                ConfigService.SaveConfig(PendingConfig, SaveAsPath);
            }
            else if (!string.IsNullOrEmpty(RvtFilePath))
            {
                ConfigService.SaveConfig(PendingConfig, RvtFilePath);
            }

            string tbMsg = rfaPath != null
                ? $"\nTitle block loaded: {Path.GetFileName(rfaPath)}"
                : (string.IsNullOrEmpty(TitleBlockFolder) ? "" : "\nNo matching title block RFA found in the specified folder.");

            MessageBox.Show(
                $"Project information updated in Revit and config saved.{tbMsg}{saveMsg}",
                "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public string GetName() => "BIM Command Centre — Apply Config";

        /// <summary>
        /// Looks for an RFA in <paramref name="folder"/> whose filename best matches
        /// the selected <paramref name="titleBlockName"/> (e.g. "Standard A1").
        /// </summary>
        private static string? FindTitleBlockRfa(string folder, string titleBlockName)
        {
            if (!Directory.Exists(folder)) return null;

            // 1. Exact name match: "Standard A1.rfa"
            string exact = Path.Combine(folder, $"{titleBlockName}.rfa");
            if (File.Exists(exact)) return exact;

            // 2. Fuzzy: any .rfa whose stem contains the key term ("A1", "A3", "custom")
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
