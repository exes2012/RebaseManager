using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Configuration;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Gemini;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Grok;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class ProjectRebaseService
    {
        private IAiService CreateAiService()
        {
            var provider = ConfigurationService.GetAiProvider();
            switch (provider?.ToLower())
            {
                case "gemini":
                    return new GeminiApiService();
                case "grok":
                    return new GrokApiService();
                default:
                    throw new Exception($"AI provider '{provider}' is not supported.");
            }
        }

        public async Task<ProjectRebaseResult> ExecuteFullRebase(
            Document documentToUpdate,
            Document standardSourceDocument,
            bool rebaseViewTemplates,
            bool rebaseTitleBlocks,
            IProgress<string> progress = null)
        {
            var result = new ProjectRebaseResult();
            IAiService aiService = null;

            try
            {
                LogHelper.Information("Starting ProjectRebaseService.ExecuteFullRebase");
                aiService = CreateAiService();

                if (rebaseViewTemplates)
                {
                    progress?.Report("Step 1: Rebasing view templates...");
                    LogHelper.Information("Step 1: Starting view template rebase");
                    var viewTemplateRebaseService = new ViewTemplateRebaseService(aiService);
                    var viewTemplateResult = await viewTemplateRebaseService.RebaseViewTemplatesAsync(documentToUpdate, standardSourceDocument, progress);
                    if (!viewTemplateResult.Success)
                    {
                        LogHelper.Error($"Step 1 failed: {viewTemplateResult.ErrorMessage}");
                        result.ErrorMessage = viewTemplateResult.ErrorMessage;
                        return result;
                    }
                    LogHelper.Information($"Step 1 completed: {viewTemplateResult.ViewsMapped} views mapped, {viewTemplateResult.TemplatesCopied} templates copied");

                    var viewReplacementService = new ViewReplacementService();
                    progress?.Report("Step 2: Replacing legends...");
                    LogHelper.Information("Step 2: Starting legend replacement");
                    var legendResult = viewReplacementService.ReplaceLegends(documentToUpdate, standardSourceDocument, progress);
                    if (!legendResult.Success)
                    {
                        LogHelper.Error($"Step 2 failed: {legendResult.ErrorMessage}");
                        result.ErrorMessage = legendResult.ErrorMessage;
                        return result;
                    }
                    LogHelper.Information($"Step 2 completed: {legendResult.ViewsProcessed} legends processed");

                    progress?.Report("Step 3: Replacing drafting views...");
                    LogHelper.Information("Step 3: Starting drafting view replacement");
                    var draftingResult = viewReplacementService.ReplaceDraftingViews(documentToUpdate, standardSourceDocument, progress);
                    if (!draftingResult.Success)
                    {
                        LogHelper.Error($"Step 3 failed: {draftingResult.ErrorMessage}");
                        result.ErrorMessage = draftingResult.ErrorMessage;
                        return result;
                    }
                    LogHelper.Information($"Step 3 completed: {draftingResult.ViewsProcessed} drafting views processed");
                }

                if (rebaseTitleBlocks)
                {
                    progress?.Report("Step 4: Rebasing title blocks...");
                    LogHelper.Information("Step 4: Starting title block rebase");
                    var titleBlocksRebaseService = new TitleBlockCategoryRebaseService(aiService);
                    await titleBlocksRebaseService.RebaseAsync(documentToUpdate, standardSourceDocument, progress);
                    LogHelper.Information("Step 4 completed: Title blocks rebased.");
                }

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
}