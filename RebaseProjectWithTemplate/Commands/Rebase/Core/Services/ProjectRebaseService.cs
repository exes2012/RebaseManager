using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services;

public class ProjectRebaseService
{
    private readonly ViewReplacementService _viewReplacementService;
    private readonly ViewTemplateRebaseService _viewTemplateService;

    public ProjectRebaseService(ViewTemplateRebaseService viewTemplateService,
        ViewReplacementService viewReplacementService)
    {
        _viewTemplateService = viewTemplateService;
        _viewReplacementService = viewReplacementService;
    }

    public async Task<ProjectRebaseResult> ExecuteFullRebase(
        Document documentToUpdate,
        Document standardSourceDocument,
        IProgress<string> progress = null)
    {
        var result = new ProjectRebaseResult();

        try
        {
            LogHelper.Information("Starting ProjectRebaseService.ExecuteFullRebase");

            // Step 1: Rebase View Templates
            progress?.Report("Step 1: Rebasing view templates...");
            LogHelper.Information("Step 1: Starting view template rebase");
            var viewTemplateResult =
                await _viewTemplateService.RebaseViewTemplatesAsync(documentToUpdate, standardSourceDocument, progress);
            if (!viewTemplateResult.Success)
            {
                LogHelper.Error($"Step 1 failed: {viewTemplateResult.ErrorMessage}");
                result.ErrorMessage = viewTemplateResult.ErrorMessage;
                return result;
            }
            LogHelper.Information($"Step 1 completed: {viewTemplateResult.ViewsMapped} views mapped, {viewTemplateResult.TemplatesCopied} templates copied");

            // Step 2: Replace Legends
            progress?.Report("Step 2: Replacing legends...");
            LogHelper.Information("Step 2: Starting legend replacement");
            var legendResult = _viewReplacementService.ReplaceLegends(documentToUpdate, standardSourceDocument, progress);
            if (!legendResult.Success)
            {
                LogHelper.Error($"Step 2 failed: {legendResult.ErrorMessage}");
                result.ErrorMessage = legendResult.ErrorMessage;
                return result;
            }
            LogHelper.Information($"Step 2 completed: {legendResult.ViewsProcessed} legends processed");

            // Step 3: Replace Drafting Views
            progress?.Report("Step 3: Replacing drafting views...");
            LogHelper.Information("Step 3: Starting drafting view replacement");
            var draftingResult = _viewReplacementService.ReplaceDraftingViews(documentToUpdate, standardSourceDocument, progress);
            if (!draftingResult.Success)
            {
                LogHelper.Error($"Step 3 failed: {draftingResult.ErrorMessage}");
                result.ErrorMessage = draftingResult.ErrorMessage;
                return result;
            }
            LogHelper.Information($"Step 3 completed: {draftingResult.ViewsProcessed} drafting views processed");

            result.Success = true;
            progress?.Report("Project rebase completed successfully!");
            LogHelper.Information("ProjectRebaseService.ExecuteFullRebase completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"ProjectRebaseService.ExecuteFullRebase failed: {ex.Message}");
            LogHelper.Error($"Stack trace: {ex.StackTrace}");
            result.ErrorMessage = ex.Message;
            return result;
        }
    }
}

public class ProjectRebaseResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
}