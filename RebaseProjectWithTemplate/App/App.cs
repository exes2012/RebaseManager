using Autodesk.Revit.UI;
using System;
using System.Reflection;

namespace RebaseProjectWithTemplate.App
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            string assemblyName = Assembly.GetExecutingAssembly().Location;
            string tabName = "Revit Tools";
            application.CreateRibbonTab(tabName);

            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Rebase Tool");

            PushButtonData buttonData = new PushButtonData(
                "RebaseProject",
                "Rebase Project",
                assemblyName,
                "RebaseProjectWithTemplate.Commands.Command");

            panel.AddItem(buttonData);

            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
