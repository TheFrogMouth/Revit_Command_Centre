using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre
{
    /// <summary>
    /// Entry point for the add-in. Registers the ribbon tab and button when Revit starts.
    /// Implements IExternalApplication so Revit loads it via the .addin manifest.
    /// </summary>
    [Regeneration(RegenerationOption.Manual)]
    public class App : IExternalApplication
    {
        private const string TabName = "BIM Command Centre";
        private const string PanelName = "Tools";
        private const string ButtonName = "LaunchBIMCommandCentre";
        private const string ButtonText = "BIM Command\nCentre";
        private const string ButtonTooltip = "Launch the BIM Command Centre panel";
        private const string CommandClass = "Revit_Command_Centre.LaunchCommand";

        /// <summary>
        /// Called by Revit on startup. Creates the ribbon tab, panel, and launch button.
        /// Uses UIControlledApplication ribbon API (not UIApplication — no document yet).
        /// </summary>
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                app.CreateRibbonTab(TabName);

                RibbonPanel panel = app.CreateRibbonPanel(TabName, PanelName);

                string assemblyPath = typeof(App).Assembly.Location;

                var buttonData = new PushButtonData(
                    ButtonName,
                    ButtonText,
                    assemblyPath,
                    CommandClass)
                {
                    ToolTip = ButtonTooltip
                };

                panel.AddItem(buttonData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre — Startup Error", ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Called by Revit on shutdown. No cleanup required.
        /// </summary>
        public Result OnShutdown(UIControlledApplication app)
        {
            return Result.Succeeded;
        }
    }
}
