
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.ReplaceSpecialityEquipment.UI.Views;
using RebaseProjectWithTemplate.Commands.ReplaceSpecialityEquipment.UI.ViewModels;

namespace RebaseProjectWithTemplate.Commands.ReplaceSpecialityEquipment
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReplaceSpecialityEquipmentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            var viewModel = new ReplaceSpecialityEquipmentViewModel(uiApp);
            var view = new ReplaceSpecialityEquipmentView
            {
                DataContext = viewModel
            };

            view.ShowDialog();

            return Result.Succeeded;
        }
    }
}
