
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.Rebase.UI.ViewModels;
using RebaseProjectWithTemplate.Commands.Rebase.UI.Views;

namespace RebaseProjectWithTemplate.Commands.Rebase
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RebaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            var viewModel = new MainViewModel(uiApp);
            var view = new MainView
            {
                DataContext = viewModel
            };

            view.ShowDialog();

            return Result.Succeeded;
        }
    }
}
