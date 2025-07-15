using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

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
            // Step 1: Rebase View Templates
            progress?.Report("Step 1: Rebasing view templates...");
            var viewTemplateResult =
                await _viewTemplateService.RebaseViewTemplatesAsync(documentToUpdate, standardSourceDocument, progress);
            if (!viewTemplateResult.Success)
            {
                result.ErrorMessage = viewTemplateResult.ErrorMessage;
                return result;
            }

            // Step 2: Replace Legends
            progress?.Report("Step 2: Replacing legends...");
            var legendResult = _viewReplacementService.ReplaceLegends(documentToUpdate, standardSourceDocument, progress);
            if (!legendResult.Success)
            {
                result.ErrorMessage = legendResult.ErrorMessage;
                return result;
            }

            // Step 3: Replace Drafting Views
            progress?.Report("Step 3: Replacing drafting views...");
            var draftingResult = _viewReplacementService.ReplaceDraftingViews(documentToUpdate, standardSourceDocument, progress);
            if (!draftingResult.Success)
            {
                result.ErrorMessage = draftingResult.ErrorMessage;
                return result;
            }

            result.Success = true;
            progress?.Report("Project rebase completed successfully!");
            return result;
        }
        catch (Exception ex)
        {
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