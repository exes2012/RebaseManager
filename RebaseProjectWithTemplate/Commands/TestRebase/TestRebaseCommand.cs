using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.TestRebase.UI.Views;

namespace RebaseProjectWithTemplate.Commands.TestRebase
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TestRebaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var doc = uiApp.ActiveUIDocument.Document;

                if (doc.IsLinked)
                {
                    TaskDialog.Show("Error", "Cannot run test rebase on a linked document.");
                    return Result.Cancelled;
                }

                var window = new TestRebaseView(doc);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
