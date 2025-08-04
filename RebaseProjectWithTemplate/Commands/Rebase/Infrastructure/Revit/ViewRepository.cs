using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

public class ViewRepository : IViewRepository
{
    private readonly Document _doc;
    private readonly Document _tpl;

    public ViewRepository(Document doc, Document tpl)
    {
        _doc = doc;
        _tpl = tpl;
    }

    public void ReplaceLegends()
    {
        ReplaceViews(ViewType.Legend);
    }

    public void ReplaceDraftingViews()
    {
        ReplaceViews(ViewType.DraftingView);
    }

    private void ReplaceViews(ViewType viewType)
    {
        using (var transaction = new Transaction(_doc, $"Replace {viewType}"))
        {
            transaction.Start();

            var getTemplateMethod = viewType == ViewType.Legend
                ? (Func<Document, View>)GetFirstLegendTemplate
                : GetFirstDraftingTemplate;
            var getViewsMethod = viewType == ViewType.Legend
                ? (Func<Document, List<View>>)GetLegendViews
                : GetDraftingViews;

            var duplicationSourceView = getTemplateMethod(_doc);
            if (duplicationSourceView == null)
                throw new Exception($"No {viewType} found in document to update to use as duplication source.");

            var originalDuplicationSourceName = duplicationSourceView.Name;
            duplicationSourceView.Name = $"TEMP_{Guid.NewGuid():N}_{originalDuplicationSourceName}";

            var standardViews = getViewsMethod(_tpl);
            if (standardViews.Count == 0)
            {
                transaction.Commit();
                return;
            }

            var viewsToDelete = getViewsMethod(_doc).Where(v => v.Id != duplicationSourceView.Id).ToList();
            DeleteViews(viewsToDelete);

            foreach (var standardView in standardViews)
            {
                var newViewId = duplicationSourceView.Duplicate(ViewDuplicateOption.Duplicate);
                var newView = _doc.GetElement(newViewId) as View;

                if (newView != null)
                {
                    SetViewProperties(newView, standardView, standardView.Name);
                    CopyViewContent(standardView, newView);
                }
            }

            _doc.Delete(duplicationSourceView.Id);

            transaction.Commit();
        }
    }

    private void DeleteViews(List<View> viewsToDelete)
    {
        foreach (var view in viewsToDelete)
            try
            {
                _doc.Delete(view.Id);
            }
            catch (Exception)
            {
            }
    }

    private void CopyViewContent(View sourceView, View targetView)
    {
        var elementsToCopy = GetAnnotations(sourceView);
        if (elementsToCopy.Count == 0) return;

        var copyPasteOptions = new CopyPasteOptions();
        copyPasteOptions.SetDuplicateTypeNamesHandler(new DestTypesHandler());

        ElementTransformUtils.CopyElements(sourceView, elementsToCopy, targetView, null, copyPasteOptions);
    }

    private List<ElementId> GetAnnotations(View view)
    {
        var categoryList = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Dimensions, BuiltInCategory.OST_Tags, BuiltInCategory.OST_Lines,
            BuiltInCategory.OST_DetailComponents, BuiltInCategory.OST_DetailComponentTags,
            BuiltInCategory.OST_RasterImages,
            BuiltInCategory.OST_TextNotes, BuiltInCategory.OST_ModelText, BuiltInCategory.OST_FilledRegion
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

    private void SetViewProperties(View targetView, View sourceView, string newName)
    {
        if (!string.IsNullOrEmpty(newName)) targetView.Name = newName;
        try
        {
            targetView.Scale = sourceView.Scale;
        }
        catch (Exception)
        {
        }

        if (targetView.ViewType == ViewType.DraftingView && sourceView.ViewType == ViewType.DraftingView)
            SetDraftingViewParameters(targetView, sourceView);
    }

    private void SetDraftingViewParameters(View targetView, View sourceView)
    {
        try
        {
            var viewSizeParam = sourceView.LookupParameter("View Size");
            var targetViewSizeParam = targetView.LookupParameter("View Size");
            if (viewSizeParam != null && targetViewSizeParam != null && !viewSizeParam.IsReadOnly)
                if (viewSizeParam.StorageType == StorageType.String)
                    targetViewSizeParam.Set(viewSizeParam.AsString());

            var viewCategoryParam = sourceView.LookupParameter("View Category");
            var targetViewCategoryParam = targetView.LookupParameter("View Category");
            if (viewCategoryParam != null && targetViewCategoryParam != null && !viewCategoryParam.IsReadOnly)
                if (viewCategoryParam.StorageType == StorageType.String)
                    targetViewCategoryParam.Set(viewCategoryParam.AsString());

            var categoryParam = sourceView.LookupParameter("Category");
            var targetCategoryParam = targetView.LookupParameter("Category");
            if (categoryParam != null && targetCategoryParam != null && !categoryParam.IsReadOnly)
                if (categoryParam.StorageType == StorageType.String)
                    targetCategoryParam.Set(categoryParam.AsString());
        }
        catch (Exception)
        {
        }
    }

    private List<View> GetLegendViews(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.ViewType == ViewType.Legend).ToList();
    }

    private List<View> GetDraftingViews(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.ViewType == ViewType.DraftingView).ToList();
    }

    private View GetFirstLegendTemplate(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.ViewType == ViewType.Legend);
    }

    private View GetFirstDraftingTemplate(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.ViewType == ViewType.DraftingView);
    }

    internal class DestTypesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}