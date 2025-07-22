using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Commands.Export.Services;
using System;

namespace RebaseProjectWithTemplate.Commands.Export
{
    [Transaction(TransactionMode.Manual)]
    public class ExportAnnotationSymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                var doc = uiDoc.Document;

                var exportService = new AnnotationSymbolsExportService(doc);
                exportService.ExportAnnotationSymbols();

                TaskDialog.Show("Success", "Annotation symbols exported successfully.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
