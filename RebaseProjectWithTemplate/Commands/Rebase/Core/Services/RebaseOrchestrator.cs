using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using System;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class RebaseOrchestrator
    {
        private readonly CategoryRebaseOrchestrator _categoryRebaseOrchestrator;
        private readonly ViewTemplateRebaseOrchestrator _viewTemplateRebaseOrchestrator;
        private readonly ViewRebaseOrchestrator _viewRebaseOrchestrator;
        private readonly Document _document;

        public RebaseOrchestrator(
            Document document,
            CategoryRebaseOrchestrator categoryRebaseOrchestrator,
            ViewTemplateRebaseOrchestrator viewTemplateRebaseOrchestrator,
            ViewRebaseOrchestrator viewRebaseOrchestrator)
        {
            _document = document;
            _categoryRebaseOrchestrator = categoryRebaseOrchestrator;
            _viewTemplateRebaseOrchestrator = viewTemplateRebaseOrchestrator;
            _viewRebaseOrchestrator = viewRebaseOrchestrator;
        }

        public async Task<ProjectRebaseResult> ExecuteFullRebase(
            bool rebaseViewTemplates,
            bool rebaseTitleBlocks,
            IProgress<string> progress = null)
        {
            var result = new ProjectRebaseResult();

            try
            {
                //if (rebaseViewTemplates)
                //{
                //    progress?.Report("Step 1: Rebasing view templates...");
                //    await _viewTemplateRebaseOrchestrator.RebaseAsync(new ViewTemplateMappingPromptStrategy());

                //    _viewRebaseOrchestrator.RebaseDraftingViewsAndLegends(progress);
                //}

                if (rebaseTitleBlocks)
                {
                    //progress?.Report("Step 4: Rebasing title blocks...");
                    //await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_TitleBlocks, new CategoryMappingPromptStrategy(), progress);

                    progress?.Report("Step 4: Rebasing Families...");
                    await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_SpecialityEquipment, new CategoryMappingPromptStrategy(), progress);
                }

                result.Success = true;
                progress?.Report("Project rebase completed successfully!");
            }
            catch (Exception ex)
            {                
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }
}