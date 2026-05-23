using Autodesk.Revit.UI;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Provides the MainView UserControl to Revit's dockable pane framework.
    /// Revit calls SetupDockablePane on the main thread and hosts the element
    /// inside its own window — no top-level WPF Window is created by our code.
    /// </summary>
    public sealed class MainViewPaneProvider : IDockablePaneProvider
    {
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = new MainView();
        }
    }
}
