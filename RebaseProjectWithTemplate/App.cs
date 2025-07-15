using System.Reflection;
using Autodesk.Revit.UI;

namespace RebaseProjectWithTemplate;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        var assemblyName = Assembly.GetExecutingAssembly().Location;
        var tabName = "Revit Tools";
        application.CreateRibbonTab(tabName);

        var panel = application.CreateRibbonPanel(tabName, "Rebase Tool");

        var buttonData = new PushButtonData(
            "RebaseProject",
            "Rebase Project",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.Rebase.RebaseCommand");

        panel.AddItem(buttonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}