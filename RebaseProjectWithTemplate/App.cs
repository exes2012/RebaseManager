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



        var testCategoryButtonData = new PushButtonData(
            "TestCategoryRebase",
            "Test Category Rebase",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.Rebase.TestCategoryRebaseCommand");

        panel.AddItem(testCategoryButtonData);

        var replaceSpecialityEquipmentButtonData = new PushButtonData(
            "ReplaceSpecialityEquipment",
            "Replace Speciality Equipment",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.ReplaceSpecialityEquipment.ReplaceSpecialityEquipmentCommand");

        panel.AddItem(replaceSpecialityEquipmentButtonData);

        var testRebaseButtonData = new PushButtonData(
            "TestRebase",
            "Test Rebase",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.TestRebase.TestRebaseCommand");

        panel.AddItem(testRebaseButtonData);

        var deleteSpecialtyEquipmentButtonData = new PushButtonData(
            "DeleteSpecialtyEquipment",
            "Delete Specialty Equipment",
            assemblyName,
            "RebaseProjectWithTemplate.Commands.DeleteSpecialtyEquipmentCommand");

        panel.AddItem(deleteSpecialtyEquipmentButtonData);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}