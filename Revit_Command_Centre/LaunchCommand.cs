using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre
{
    /// <summary>
    /// Ribbon command. Activates the existing window or raises the ExternalEvent so Revit
    /// opens the window during its next idle cycle — never during Execute itself.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (App.Instance?.IsWindowOpen == true)
                {
                    App.Instance.ActivateWindow();
                }
                else
                {
                    App.Instance?.RaiseShowWindow();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
