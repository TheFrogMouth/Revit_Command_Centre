using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre
{
    /// <summary>
    /// Entry point for the add-in. Creates the ribbon and an ExternalEvent so the modeless
    /// window can be opened safely when Revit is idle (not during command execution).
    /// </summary>
    [Regeneration(RegenerationOption.Manual)]
    public class App : IExternalApplication
    {
        public static App? Instance { get; private set; }

        private const string TabName    = "BIM Command Centre";
        private const string PanelName  = "Tools";
        private const string ButtonName = "LaunchBIMCommandCentre";
        private const string ButtonText = "BIM Command\nCentre";
        private const string ButtonTooltip = "Launch the BIM Command Centre panel";
        private const string CommandClass  = "Revit_Command_Centre.LaunchCommand";

        private readonly ShowWindowHandler _handler = new();
        private ExternalEvent? _showEvent;

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                Instance = this;

                // ExternalEvent defers window creation to Revit's idle cycle, avoiding crashes
                // that occur when a WPF window is shown during IExternalCommand.Execute.
                _showEvent = ExternalEvent.Create(_handler);

                app.CreateRibbonTab(TabName);
                RibbonPanel panel = app.CreateRibbonPanel(TabName, PanelName);

                string assemblyPath = typeof(App).Assembly.Location;
                panel.AddItem(new PushButtonData(ButtonName, ButtonText, assemblyPath, CommandClass)
                {
                    ToolTip = ButtonTooltip
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
            Instance = null;
            return Result.Succeeded;
        }

        public bool IsWindowOpen   => _handler.IsWindowOpen;
        public void ActivateWindow() => _handler.ActivateExisting();
        public void RaiseShowWindow() => _showEvent?.Raise();

        // ── ExternalEvent handler — runs on Revit's main thread when Revit is idle ────────────

        private sealed class ShowWindowHandler : IExternalEventHandler
        {
            private MainWindow? _window;

            public bool IsWindowOpen => _window?.IsLoaded == true;

            public void ActivateExisting() => _window?.Activate();

            public void Execute(UIApplication app)
            {
                try
                {
                    if (_window?.IsLoaded == true)
                    {
                        _window.Activate();
                        return;
                    }

                    _window = new MainWindow(app);
                    _window.Closed += (_, _) => _window = null;

                    // Parent to Revit's main HWND so the window stays in front of Revit
                    new WindowInteropHelper(_window).Owner = app.MainWindowHandle;

                    _window.Show();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("BIM Command Centre", $"Failed to open window:\n{ex.Message}");
                }
            }

            public string GetName() => "Show BIM Command Centre";
        }
    }
}
