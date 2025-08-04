using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB.Events;
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
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new ProjectRebaseResult();

            LogHelper.Information("Starting full rebase operation (using global failure handler)");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                //if (rebaseViewTemplates)
                //{
                //    progress?.Report("Step 1: Rebasing view templates...");
                //    var viewTemplateResult = await _viewTemplateRebaseOrchestrator.RebaseAsync(new ViewTemplateMappingPromptStrategy(), progress, cancellationToken);

                //    cancellationToken.ThrowIfCancellationRequested();
                //    //_viewRebaseOrchestrator.RebaseDraftingViewsAndLegends(progress);
                //}

                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report("Step 7: Rebasing Shared Parameters...");
                var sharedParamsResult = _sharedParametersRebaseOrchestrator.Rebase(progress);
                LogHelper.Information($"Shared parameters rebase completed: {sharedParamsResult.AddedParameters} added, {sharedParamsResult.UpdatedParameters} updated, {sharedParamsResult.DeletedParameters} deleted");

                progress?.Report("Step 4: Rebasing Families (Enhanced)...");
                    // Title blocks usually don't need ungrouping, so we pass false
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_TitleBlocks, new CategoryMappingPromptStrategy(), progress, ungroupInstances: false);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_ElectricalFixtures, new CategoryMappingPromptStrategy(), progress, ungroupInstances: false);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_ElectricalFixtureTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_SpecialityEquipmentTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                 await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_StairsRailingTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                 await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_MultiCategoryTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_ElectricalEquipment, new CategoryMappingPromptStrategy(), progress, ungroupInstances: false);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_ElectricalEquipmentTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_GenericAnnotation, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);
                await _categoryRebaseOrchestrator.RebaseAsync(_document, BuiltInCategory.OST_DetailComponentTags, new CategoryMappingPromptStrategy(), progress, ungroupInstances: true);

                await _elementTypeRebaseOrchestrator.RebaseAsync(systemElementCategory, new CategoryMappingPromptStrategy(), progress);
                //await _elementTypeRebaseOrchestrator.RebaseAsync(BuiltInCategory.OST_DetailComponents, new CategoryMappingPromptStrategy(), progress);
                await _elementTypeRebaseOrchestrator.RebaseAsync(BuiltInCategory.OST_FlexPipeCurves, new CategoryMappingPromptStrategy(), progress);
                 await _elementTypeRebaseOrchestrator.RebaseAsync(BuiltInCategory.OST_FlexPipeCurves, new CategoryMappingPromptStrategy(), progress);
                 await _elementTypeRebaseOrchestrator.RebaseAsync(BuiltInCategory.OST_StairsRailing, new CategoryMappingPromptStrategy(), progress);



                //if (rebaseSchedules)
                //{
                //    progress?.Report("Step 6: Rebasing Schedules...");
                //    var schedulesResult = _schedulesRebaseOrchestrator.Rebase(progress);

                //    LogHelper.Information($"Schedules rebase completed: {schedulesResult.DeletedInstances} instances deleted, {schedulesResult.DeletedSchedules} schedules deleted, {schedulesResult.CopiedSchedules} schedules copied");
                //}

                result.Success = true;
                progress?.Report("Project rebase completed successfully!");
            }
            catch (OperationCanceledException)
            {
                LogHelper.Warning("Project rebase was cancelled by user.");
                result.ErrorMessage = "Operation was cancelled by user.";
                progress?.Report("Project rebase was cancelled.");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }


            return result;
        }


    }
}