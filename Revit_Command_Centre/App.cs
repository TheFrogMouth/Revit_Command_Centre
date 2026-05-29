using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Services;

namespace Revit_Command_Centre
{
    [Regeneration(RegenerationOption.Manual)]
    public class App : IExternalApplication
    {
        public static App? Instance { get; private set; }

        public static MainViewPaneProvider?        PaneProvider           { get; private set; }
        public static ApplyConfigEventHandler?    ApplyConfigHandler     { get; private set; }
        public static ExternalEvent?              ApplyConfigEvent       { get; private set; }
        public static GenerateSheetsEventHandler? GenerateSheetsHandler  { get; private set; }
        public static ExternalEvent?              GenerateSheetsEvent    { get; private set; }
        public static AddWorksetEventHandler?     AddWorksetHandler      { get; private set; }
        public static ExternalEvent?              AddWorksetEvent        { get; private set; }

        public static readonly DockablePaneId PaneId =
            new DockablePaneId(new Guid("B7C8D9E0-F1A2-3B4C-5D6E-7F8A9B0C1D2E"));

        private const string TabName    = "BIM Command Centre";
        private const string PanelName  = "Tools";
        private const string ButtonName = "LaunchBIMCommandCentre";
        private const string ButtonText = "BIM Command\nCentre";
        private const string ButtonTooltip = "Launch the BIM Command Centre panel";
        private const string CommandClass  = "Revit_Command_Centre.LaunchCommand";

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                Instance = this;

                ApplyConfigHandler    = new ApplyConfigEventHandler();
                ApplyConfigEvent      = ExternalEvent.Create(ApplyConfigHandler);
                GenerateSheetsHandler = new GenerateSheetsEventHandler();
                GenerateSheetsEvent   = ExternalEvent.Create(GenerateSheetsHandler);
                AddWorksetHandler     = new AddWorksetEventHandler();
                AddWorksetEvent       = ExternalEvent.Create(AddWorksetHandler);

                PaneProvider = new MainViewPaneProvider();
                // Register dockable pane — Revit hosts our UserControl inside its own window.
                // This avoids creating a new top-level WPF Window (which crashed on this machine).
                app.RegisterDockablePane(PaneId, "BIM Command Centre", PaneProvider);

                app.CreateRibbonTab(TabName);
                RibbonPanel panel = app.CreateRibbonPanel(TabName, PanelName);
                string assemblyPath = typeof(App).Assembly.Location;
                panel.AddItem(new PushButtonData(ButtonName, ButtonText, assemblyPath, CommandClass)
                {
                    ToolTip = ButtonTooltip,
                    AvailabilityClassName = "Revit_Command_Centre.LaunchCommandAvailability"
                });

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BIM Command Centre — Startup Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            Instance           = null;
            PaneProvider       = null;
            ApplyConfigHandler    = null;
            ApplyConfigEvent      = null;
            GenerateSheetsHandler = null;
            GenerateSheetsEvent   = null;
            AddWorksetHandler     = null;
            AddWorksetEvent       = null;
            return Result.Succeeded;
        }
    }
}
