using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using System;
using System.Threading.Tasks;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class ProjectRebaseOrchestrator
    {
        private readonly CategoryRebaseOrchestrator _categoryRebaseOrchestrator;
        private readonly ViewTemplateRebaseOrchestrator _viewTemplateRebaseOrchestrator;
        private readonly ViewRebaseOrchestrator _viewRebaseOrchestrator;
        private readonly ElementTypeRebaseOrchestrator _elementTypeRebaseOrchestrator;
        private readonly SchedulesRebaseOrchestrator _schedulesRebaseOrchestrator;
        private readonly SharedParametersRebaseOrchestrator _sharedParametersRebaseOrchestrator;
        private readonly Document _document;

        public ProjectRebaseOrchestrator(
            Document document,
            CategoryRebaseOrchestrator categoryRebaseOrchestrator,
            ViewTemplateRebaseOrchestrator viewTemplateRebaseOrchestrator,
            ViewRebaseOrchestrator viewRebaseOrchestrator,
            ElementTypeRebaseOrchestrator elementTypeRebaseOrchestrator,
            SchedulesRebaseOrchestrator schedulesRebaseOrchestrator,
            SharedParametersRebaseOrchestrator sharedParametersRebaseOrchestrator)
        {
            _document = document;
            _categoryRebaseOrchestrator = categoryRebaseOrchestrator;
            _viewTemplateRebaseOrchestrator = viewTemplateRebaseOrchestrator;
            _viewRebaseOrchestrator = viewRebaseOrchestrator;
            _elementTypeRebaseOrchestrator = elementTypeRebaseOrchestrator;
            _schedulesRebaseOrchestrator = schedulesRebaseOrchestrator;
            _sharedParametersRebaseOrchestrator = sharedParametersRebaseOrchestrator;
        }

        public async Task<ProjectRebaseResult> ExecuteFullRebase(
            bool rebaseViewTemplates,
            bool rebaseTitleBlocks,
            bool rebaseSystemElements = false,
            bool rebaseSchedules = false,
            bool rebaseSharedParameters = false,
            BuiltInCategory systemElementCategory = BuiltInCategory.OST_Floors,
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
                    progress?.Report("Step 4: Rebasing Families (Enhanced)...");
                    var mappingResult = await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_SpecialityEquipment, new CategoryMappingPromptStrategy(), progress);

                    LogHelper.Information($"Enhanced rebase completed: {mappingResult.TotalFamilies} total, {mappingResult.ExactMatches} exact matches, {mappingResult.AiMapped} AI mapped, {mappingResult.SwitchedInstances} instances switched");
                }

                if (rebaseSystemElements)
                {
                    progress?.Report($"Step 5: Rebasing {systemElementCategory} Types...");
                    var systemResult = await _elementTypeRebaseOrchestrator.RebaseAsync(
                        systemElementCategory,
                        new CategoryMappingPromptStrategy(),
                        progress);

                    LogHelper.Information($"{systemElementCategory} types rebase completed: {systemResult.TotalSourceTypes} total, {systemResult.ExactMatches} exact matches, {systemResult.AiMapped} AI mapped, {systemResult.SwitchedInstances} instances switched, {systemResult.DeletedTypes} deleted");
                }

                if (rebaseSharedParameters)
                {
                    progress?.Report("Step 7: Rebasing Shared Parameters...");
                    var sharedParamsResult = _sharedParametersRebaseOrchestrator.Rebase(progress);

                    LogHelper.Information($"Shared parameters rebase completed: {sharedParamsResult.AddedParameters} added, {sharedParamsResult.UpdatedParameters} updated, {sharedParamsResult.DeletedParameters} deleted");
                }

                if (rebaseSchedules)
                {
                    progress?.Report("Step 6: Rebasing Schedules...");
                    var schedulesResult = _schedulesRebaseOrchestrator.Rebase(progress);

                    LogHelper.Information($"Schedules rebase completed: {schedulesResult.DeletedInstances} instances deleted, {schedulesResult.DeletedSchedules} schedules deleted, {schedulesResult.CopiedSchedules} schedules copied");
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