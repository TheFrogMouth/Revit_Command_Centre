using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
            private System.Windows.Window? _window;

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

                    // DIAGNOSTIC: plain code-only Window with zero XAML and zero Revit API calls.
                    // If this crashes → WPF window creation itself is broken in this Revit install.
                    // If this opens → the crash is inside MainWindow's XAML or constructor.
                    var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
                    panel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text     = "BIM Command Centre",
                        FontSize = 20,
                        Margin   = new System.Windows.Thickness(0, 0, 0, 12)
                    });
                    panel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text       = "Diagnostic mode — plain WPF window, no custom XAML.\nIf you can read this, the crash is in the custom XAML/styles.",
                        FontSize   = 13,
                        TextWrapping = System.Windows.TextWrapping.Wrap
                    });

                    _window = new System.Windows.Window
                    {
                        Title  = "BIM Command Centre",
                        Width  = 520,
                        Height = 320,
                        WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                        Content = panel
                    };
                    _window.Closed += (_, _) => _window = null;
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
