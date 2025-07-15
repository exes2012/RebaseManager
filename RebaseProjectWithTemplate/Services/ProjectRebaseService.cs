using Autodesk.Revit.DB;
using System;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Services
{
    public class ProjectRebaseService : IDisposable
    {
        private readonly ViewTemplateRebaseService _viewTemplateService;
        private readonly ViewReplacementService _viewReplacementService;

        public ProjectRebaseService()
        {
            _viewTemplateService = new ViewTemplateRebaseService();
            _viewReplacementService = new ViewReplacementService();
        }

        public async Task<ProjectRebaseResult> ExecuteFullRebase(
            Document sourceDocument,
            Document templateDocument,
            IProgress<string> progress = null)
        {
            var result = new ProjectRebaseResult();

            try
            {
                // Step 1: Rebase View Templates
                progress?.Report("Step 1: Rebasing view templates...");
                var viewTemplateResult = await _viewTemplateService.RebaseViewTemplatesAsync(sourceDocument, templateDocument, progress);
                if (!viewTemplateResult.Success)
                {
                    result.ErrorMessage = viewTemplateResult.ErrorMessage;
                    return result;
                }

                // Step 2: Replace Legends
                progress?.Report("Step 2: Replacing legends...");
                var legendResult = _viewReplacementService.ReplaceLegends(sourceDocument, templateDocument, progress);
                if (!legendResult.Success)
                {
                    result.ErrorMessage = legendResult.ErrorMessage;
                    return result;
                }

                // Step 3: Replace Drafting Views
                progress?.Report("Step 3: Replacing drafting views...");
                var draftingResult = _viewReplacementService.ReplaceDraftingViews(sourceDocument, templateDocument, progress);
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

        public void Dispose()
        {
            _viewTemplateService?.Dispose();
            _viewReplacementService?.Dispose();
        }
    }

    public class ProjectRebaseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}
