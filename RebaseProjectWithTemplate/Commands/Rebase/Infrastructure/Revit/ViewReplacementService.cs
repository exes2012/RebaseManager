using System.Threading;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

public class ViewReplacementService
{
    public ViewReplacementResult ReplaceLegends(
        Document documentToUpdate,
        Document standardSourceDocument,
        IProgress<string> progress = null)
    {
        return ReplaceViews(documentToUpdate, standardSourceDocument, ViewType.Legend, progress);
    }

    public ViewReplacementResult ReplaceDraftingViews(
        Document documentToUpdate,
        Document standardSourceDocument,
        IProgress<string> progress = null)
    {
        return ReplaceViews(documentToUpdate, standardSourceDocument, ViewType.DraftingView, progress);
    }

    private ViewReplacementResult ReplaceViews(
        Document documentToUpdate,
        Document standardSourceDocument,
        ViewType viewType,
        IProgress<string> progress = null)
    {
        var result = new ViewReplacementResult();
        var viewTypeName = viewType.ToString().ToLower().Replace("view", " views");

        using (var transaction = new Transaction(documentToUpdate, $"Replace {viewTypeName}"))
        {
            transaction.Start();

            try
            {
                var getTemplateMethod = viewType == ViewType.Legend
                    ? (Func<Document, View>)ViewQueryService.GetFirstLegendTemplate
                    : ViewQueryService.GetFirstDraftingTemplate;
                var getViewsMethod = viewType == ViewType.Legend
                    ? (Func<Document, List<View>>)ViewQueryService.GetLegendViews
                    : ViewQueryService.GetDraftingViews;

                var duplicationSourceView = getTemplateMethod(documentToUpdate);
                if (duplicationSourceView == null)
                    throw new Exception($"No {viewTypeName} found in document to update to use as duplication source.");

                // Temporarily rename the duplication source view to avoid name conflicts during duplication
                var originalDuplicationSourceName = duplicationSourceView.Name;
                duplicationSourceView.Name = $"TEMP_{Guid.NewGuid():N}_{originalDuplicationSourceName}";

                var standardViews = getViewsMethod(standardSourceDocument);
                if (standardViews.Count == 0)
                {
                    progress?.Report($"No {viewTypeName} found in standard source document");
                    result.Success = true;
                    transaction.Commit();
                    return result;
                }

                var viewsToDelete = getViewsMethod(documentToUpdate)
                    .Where(v => v.Id != duplicationSourceView.Id)
                    .ToList();

                ViewManipulationService.DeleteViews(documentToUpdate, viewsToDelete);

                foreach (var standardView in standardViews)
                {
                    var newViewId = duplicationSourceView.Duplicate(ViewDuplicateOption.Duplicate);
                    var newView = documentToUpdate.GetElement(newViewId) as View;

                    if (newView != null)
                    {
                        ViewManipulationService.SetViewProperties(newView, standardView, standardView.Name);
                        ViewCopyService.CopyViewContent(standardView, newView, CancellationToken.None);
                    }
                }

                // Delete the temporary duplication source view
                documentToUpdate.Delete(duplicationSourceView.Id);

                transaction.Commit();
                result.Success = true;
                result.ViewsProcessed = standardViews.Count;
                progress?.Report($"Replaced {standardViews.Count} {viewTypeName}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to replace {viewTypeName}: {ex.Message}", ex);
            }
        }

        return result;
    }
}

public class ViewReplacementResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public int ViewsProcessed { get; set; }
}