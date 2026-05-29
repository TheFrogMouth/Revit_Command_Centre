using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    public class AddWorksetEventHandler : IExternalEventHandler
    {
        public string? WorksetName { get; set; }

        public void Execute(UIApplication app)
        {
            if (string.IsNullOrWhiteSpace(WorksetName)) return;

            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("BIM Command Centre", "No active document.");
                    return;
                }
                if (!doc.IsWorkshared)
                {
                    TaskDialog.Show("BIM Command Centre", "Worksharing is not enabled for this project.");
                    return;
                }

                bool exists = new FilteredElementCollector(doc)
                    .OfClass(typeof(Workset))
                    .Cast<Workset>()
                    .Any(ws => ws.Kind == WorksetKind.UserCreated &&
                               ws.Name.Equals(WorksetName, StringComparison.OrdinalIgnoreCase));

                if (exists)
                {
                    TaskDialog.Show("BIM Command Centre", $"Workset \"{WorksetName}\" already exists.");
                    return;
                }

                using var tx = new Transaction(doc, $"Add workset: {WorksetName}");
                tx.Start();
                Workset.Create(doc, WorksetName);
                tx.Commit();

                TaskDialog.Show("BIM Command Centre", $"Workset \"{WorksetName}\" created successfully.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre", $"Failed to create workset:\n\n{ex.Message}");
            }
        }

        public string GetName() => "BIM Command Centre — Add Workset";
    }
}
