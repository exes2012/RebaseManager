using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.UI.Views;
using RebaseProjectWithTemplate.UI.ViewModels;

namespace RebaseProjectWithTemplate.UI.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
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
