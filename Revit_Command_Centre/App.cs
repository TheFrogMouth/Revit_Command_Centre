using System;
using System.IO;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre
{
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

        public bool IsWindowOpen    => _handler.IsWindowOpen;
        public void ActivateWindow() => _handler.ActivateExisting();
        public void RaiseShowWindow() => _showEvent?.Raise();

        // ── ExternalEvent handler — runs on Revit's main thread when Revit is idle ────────────

        private sealed class ShowWindowHandler : IExternalEventHandler
        {
            // _window lives on the WPF thread; access only via _window.Dispatcher or _isOpen flag.
            private MainWindow? _window;
            private volatile bool _isOpen = false;

            public bool IsWindowOpen => _isOpen;

            public void ActivateExisting()
            {
                if (_isOpen && _window != null)
                    _window.Dispatcher.BeginInvoke(() => _window?.Activate());
            }

            public void Execute(UIApplication app)
            {
                try
                {
                    if (_isOpen)
                    {
                        ActivateExisting();
                        return;
                    }

                    // Create the window on a dedicated STA thread with its own Dispatcher.
                    // This isolates our WPF rendering entirely from Revit's main-thread
                    // rendering state (process-level SoftwareOnly mode, WPF channel, etc.)
                    // which has been causing 0xc0000005 crashes when we attempt window
                    // creation directly on Revit's main thread.
                    var uiApp = app;
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            _window = new MainWindow(uiApp);
                            _window.Closed += (_, _) =>
                            {
                                _isOpen = false;
                                _window = null;
                                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                            };
                            _isOpen = true;
                            _window.Show();
                            System.Windows.Threading.Dispatcher.Run();
                        }
                        catch (Exception ex)
                        {
                            _isOpen = false;
                            _window = null;
                            LogError(ex);
                        }
                    });

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    TaskDialog.Show("BIM Command Centre", $"Failed to open window:\n{ex.GetType().Name}: {ex.Message}");
                }
            }

            public string GetName() => "Show BIM Command Centre";

            private static void LogError(Exception ex)
            {
                try
                {
                    string path = Path.Combine(Path.GetTempPath(), "BIMCommandCentre_error.txt");
                    File.WriteAllText(path,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n" +
                        $"{ex.GetType().FullName}: {ex.Message}\n" +
                        $"Inner: {ex.InnerException?.Message}\n\n" +
                        $"{ex.StackTrace}");
                }
                catch { }
            }
        }
    }
}
