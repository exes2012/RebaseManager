using Autodesk.Revit.DB;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

public static class ViewCopyService
{
    public static void CopyViewContent(View sourceView, View targetView, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var elementsToCopy = GetAnnotations(sourceView);

        if (elementsToCopy.Count == 0)
            return;

        var copyPasteOptions = new CopyPasteOptions();
        copyPasteOptions.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

        ElementTransformUtils.CopyElements(sourceView, elementsToCopy, targetView, null, copyPasteOptions);
    }

    private static List<ElementId> GetAnnotations(View view)
    {
        var categoryList = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Dimensions,
            BuiltInCategory.OST_Tags,
            BuiltInCategory.OST_Lines,
            BuiltInCategory.OST_DetailComponents,
            BuiltInCategory.OST_DetailComponentTags,
            BuiltInCategory.OST_RasterImages,
            BuiltInCategory.OST_TextNotes,
            BuiltInCategory.OST_ModelText,
            BuiltInCategory.OST_FilledRegion
        };

        var output = new FilteredElementCollector(view.Document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementOwnerViewFilter(view.Id))
            .WherePasses(new ElementMulticategoryFilter(categoryList, false))
            .Where(e => e.Category != null)
            .Select(e => e.Id)
            .ToList();

        var dwgElements = new FilteredElementCollector(view.Document)
            .WhereElementIsNotElementType()
            .WherePasses(new ElementOwnerViewFilter(view.Id))
            .OfClass(typeof(ImportInstance))
            .Select(e => e.Id)
            .ToList();

        output.AddRange(dwgElements);

        return output;
    }
}

public class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
{
    public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
    {
        return DuplicateTypeAction.UseDestinationTypes;
    }
}