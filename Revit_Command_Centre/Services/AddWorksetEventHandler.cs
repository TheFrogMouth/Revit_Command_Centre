using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Creates a named user workset in the active workshared document.
    /// Raised by the Project Setup worksets panel via ExternalEvent.
    /// </summary>
    public class AddWorksetEventHandler : IExternalEventHandler
    {
        public string? WorksetName { get; set; }

        public void Execute(UIApplication app)
        {
            if (string.IsNullOrWhiteSpace(WorksetName)) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || !doc.IsWorkshared)
            {
                TaskDialog.Show("BIM Command Centre", "Worksharing is not enabled on this project.");
                return;
            }

            try
            {
                bool exists = new FilteredElementCollector(doc)
                    .OfClass(typeof(Workset))
                    .Cast<Workset>()
                    .Any(ws => ws.Name.Equals(WorksetName, StringComparison.OrdinalIgnoreCase));

                if (exists)
                {
                    TaskDialog.Show("BIM Command Centre", $"A workset named \"{WorksetName}\" already exists.");
                    return;
                }

                using var tx = new Transaction(doc, $"Add workset: {WorksetName}");
                tx.Start();
                Workset.Create(doc, WorksetName);
                tx.Commit();

                TaskDialog.Show("BIM Command Centre",
                    $"Workset \"{WorksetName}\" created.\nNavigate back to Project Setup to refresh the list.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre", $"Failed to create workset:\n{ex.Message}");
            }
        }

        public string GetName() => "BIM Command Centre — Add Workset";
    }
}
