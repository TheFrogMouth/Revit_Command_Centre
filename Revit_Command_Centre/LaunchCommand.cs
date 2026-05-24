using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre
{
    /// <summary>
    /// Keeps the ribbon button enabled at all times — no project required.
    /// The panel can open and process family files without an active document.
    /// </summary>
    public class LaunchCommandAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories) => true;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = commandData.Application.GetDockablePane(App.PaneId);
                pane.Show();

                // Activate is called AFTER pane.Show() — this is the first safe moment to
                // touch the Revit API and instantiate module views. The pane constructor and
                // Loaded handler deliberately do nothing with Revit to avoid crashing during
                // document loading, when Revit constructs the pane on its worker thread.
                App.PaneProvider?.View?.Activate(commandData.Application);

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
