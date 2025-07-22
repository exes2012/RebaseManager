using System.Reflection;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        LogHelper.Initialize();

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

        var exportButtonData = new PushButtonData(
            "ExportAnnotationSymbols",
            "Export Symbols",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.Export.ExportAnnotationSymbolsCommand");

        panel.AddItem(exportButtonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}