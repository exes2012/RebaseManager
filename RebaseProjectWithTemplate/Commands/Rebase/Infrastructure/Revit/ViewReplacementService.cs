using System.Threading;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;
using RebaseProjectWithTemplate.Infrastructure;
using System.Windows;
using System.Windows.Threading;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

public class ViewReplacementService
{
    private static void UpdateProgress(IProgress<string> progress, string message)
    {
        progress?.Report(message);
        LogHelper.Information($"ViewReplacementService: {message}");

        // Force UI update
        Application.Current?.Dispatcher?.Invoke(() => { }, DispatcherPriority.Background);
    }
    public ViewReplacementResult ReplaceLegends(
        Document documentToUpdate,
        Document standardSourceDocument,
        IProgress<string> progress = null)
    {
        LogHelper.Information("Starting ViewReplacementService.ReplaceLegends");
        var result = ReplaceViews(documentToUpdate, standardSourceDocument, ViewType.Legend, progress);
        LogHelper.Information($"ReplaceLegends completed - Success: {result.Success}, Views processed: {result.ViewsProcessed}");
        return result;
    }

    public ViewReplacementResult ReplaceDraftingViews(
        Document documentToUpdate,
        Document standardSourceDocument,
        IProgress<string> progress = null)
    {
        LogHelper.Information("Starting ViewReplacementService.ReplaceDraftingViews");
        var result = ReplaceViews(documentToUpdate, standardSourceDocument, ViewType.DraftingView, progress);
        LogHelper.Information($"ReplaceDraftingViews completed - Success: {result.Success}, Views processed: {result.ViewsProcessed}");
        return result;
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
            CommonFailuresPreprocessor.SetFailuresPreprocessor(transaction);
            transaction.Start();

            try
            {
                LogHelper.Information($"Starting ReplaceViews for {viewTypeName}");

                var getTemplateMethod = viewType == ViewType.Legend
                    ? (Func<Document, View>)ViewQueryService.GetFirstLegendTemplate
                    : ViewQueryService.GetFirstDraftingTemplate;
                var getViewsMethod = viewType == ViewType.Legend
                    ? (Func<Document, List<View>>)ViewQueryService.GetLegendViews
                    : ViewQueryService.GetDraftingViews;

                var duplicationSourceView = getTemplateMethod(documentToUpdate);
                if (duplicationSourceView == null)
                    throw new Exception($"No {viewTypeName} found in document to update to use as duplication source.");

                LogHelper.Information($"Using duplication source view: {duplicationSourceView.Name}");

                // Temporarily rename the duplication source view to avoid name conflicts during duplication
                var originalDuplicationSourceName = duplicationSourceView.Name;
                duplicationSourceView.Name = $"TEMP_{Guid.NewGuid():N}_{originalDuplicationSourceName}";

                var standardViews = getViewsMethod(standardSourceDocument);
                if (standardViews.Count == 0)
                {
                    UpdateProgress(progress, $"No {viewTypeName} found in standard source document");
                    LogHelper.Information($"No {viewTypeName} found in standard source document");
                    result.Success = true;
                    transaction.Commit();
                    return result;
                }

                LogHelper.Information($"Found {standardViews.Count} standard {viewTypeName} to copy");

                var viewsToDelete = getViewsMethod(documentToUpdate)
                    .Where(v => v.Id != duplicationSourceView.Id)
                    .ToList();

                LogHelper.Information($"Deleting {viewsToDelete.Count} existing {viewTypeName}");
                ViewManipulationService.DeleteViews(documentToUpdate, viewsToDelete);

                LogHelper.Information($"Creating {standardViews.Count} new {viewTypeName}");
                foreach (var standardView in standardViews)
                {
                    LogHelper.Debug($"Processing standard view: {standardView.Name}");
                    var newViewId = duplicationSourceView.Duplicate(ViewDuplicateOption.Duplicate);
                    var newView = documentToUpdate.GetElement(newViewId) as View;

                    if (newView != null)
                    {
                        ViewManipulationService.SetViewProperties(newView, standardView, standardView.Name);
                        ViewCopyService.CopyViewContent(standardView, newView, CancellationToken.None);
                        LogHelper.Debug($"Created and configured view: {newView.Name}");
                    }
                }

                // Delete the temporary duplication source view
                LogHelper.Information("Deleting temporary duplication source view");
                documentToUpdate.Delete(duplicationSourceView.Id);

                LogHelper.Information("Committing transaction");
                transaction.Commit();
                result.Success = true;
                result.ViewsProcessed = standardViews.Count;
                UpdateProgress(progress, $"Replaced {standardViews.Count} {viewTypeName}");
                LogHelper.Information($"Successfully replaced {standardViews.Count} {viewTypeName}");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to replace {viewTypeName}: {ex.Message}");
                LogHelper.Error($"Stack trace: {ex.StackTrace}");
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