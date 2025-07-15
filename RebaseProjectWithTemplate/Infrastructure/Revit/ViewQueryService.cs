using Autodesk.Revit.DB;

namespace RebaseProjectWithTemplate.Infrastructure.Revit;

public static class ViewQueryService
{
    public static List<View> GetLegendViews(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.ViewType == ViewType.Legend)
            .ToList();
    }

    public static List<View> GetDraftingViews(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.ViewType == ViewType.DraftingView)
            .ToList();
    }

    public static View GetFirstLegendTemplate(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.ViewType == ViewType.Legend);
    }

    public static View GetFirstDraftingTemplate(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.ViewType == ViewType.DraftingView);
    }
}