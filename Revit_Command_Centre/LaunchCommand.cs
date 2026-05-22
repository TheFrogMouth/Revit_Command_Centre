using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre
{
    /// <summary>
    /// IExternalCommand that opens the BIM Tools main window.
    /// Called when the user clicks "Launch BIM Tools" in the ribbon.
    /// TransactionMode.Manual — the window manages its own transactions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LaunchCommand : IExternalCommand
    {
        private static MainWindow? _openWindow;

        /// <summary>
        /// Opens (or brings to front) the main BIM Tools modeless WPF window.
        /// Passes the UIApplication so child panels can make Revit API calls.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (_openWindow != null && _openWindow.IsLoaded)
                {
                    _openWindow.Activate();
                    return Result.Succeeded;
                }

                _openWindow = new MainWindow(commandData.Application);
                _openWindow.Closed += (_, _) => _openWindow = null;
                _openWindow.Show();

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
