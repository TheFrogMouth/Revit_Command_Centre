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
        public ProjectConfig? PendingConfig { get; set; }
        public string?        RvtFilePath  { get; set; }

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

            using var tx = new Transaction(doc, "Apply BIM project config");
            tx.Start();
            doc.ProjectInformation.Name       = PendingConfig.ProjectName;
            doc.ProjectInformation.Number     = PendingConfig.ProjectNumber;
            doc.ProjectInformation.ClientName = PendingConfig.ClientName;
            ExtensibleStorageService.WriteConfig(doc, PendingConfig);
            tx.Commit();

            if (!string.IsNullOrEmpty(RvtFilePath))
                ConfigService.SaveConfig(PendingConfig, RvtFilePath);

            MessageBox.Show("Project information updated in Revit and config saved.",
                "BIM Command Centre", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public string GetName() => "BIM Command Centre — Apply Config";
    }
}
