using Autodesk.Revit.DB;

namespace RebaseProjectWithTemplate.Infrastructure.Revit;

public static class ViewManipulationService
{
    public static void SetViewProperties(View targetView, View sourceView, string newName)
    {
        // Set name
        if (!string.IsNullOrEmpty(newName)) targetView.Name = newName;

        // Set scale
        try
        {
            targetView.Scale = sourceView.Scale;
        }
        catch (Exception)
        {
            // Scale setting failed, continue anyway
        }

        // For drafting views, set additional parameters
        if (targetView.ViewType == ViewType.DraftingView && sourceView.ViewType == ViewType.DraftingView)
            SetDraftingViewParameters(targetView, sourceView);
    }

    private static void SetDraftingViewParameters(View targetView, View sourceView)
    {
        try
        {
            // Set View Size parameter
            var viewSizeParam = sourceView.LookupParameter("View Size");
            var targetViewSizeParam = targetView.LookupParameter("View Size");
            if (viewSizeParam != null && targetViewSizeParam != null && !viewSizeParam.IsReadOnly)
                if (viewSizeParam.StorageType == StorageType.String)
                    targetViewSizeParam.Set(viewSizeParam.AsString());

            // Set View Category parameter
            var viewCategoryParam = sourceView.LookupParameter("View Category");
            var targetViewCategoryParam = targetView.LookupParameter("View Category");
            if (viewCategoryParam != null && targetViewCategoryParam != null && !viewCategoryParam.IsReadOnly)
                if (viewCategoryParam.StorageType == StorageType.String)
                    targetViewCategoryParam.Set(viewCategoryParam.AsString());

            // Set Category parameter
            var categoryParam = sourceView.LookupParameter("Category");
            var targetCategoryParam = targetView.LookupParameter("Category");
            if (categoryParam != null && targetCategoryParam != null && !categoryParam.IsReadOnly)
                if (categoryParam.StorageType == StorageType.String)
                    targetCategoryParam.Set(categoryParam.AsString());
        }
        catch (Exception)
        {
            // Parameter setting failed, continue anyway
        }
    }

    public static void DeleteViews(Document document, List<View> viewsToDelete)
    {
        foreach (var view in viewsToDelete)
            try
            {
                document.Delete(view.Id);
            }
            catch (Exception)
            {
                // Continue if deletion fails
            }
    }
}