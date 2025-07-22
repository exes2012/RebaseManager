using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Prompts;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class ViewTemplateRebaseService
    {
        private readonly IAiService _aiService;
        private readonly Dictionary<ElementId, string> _originalTemplateNames = new Dictionary<ElementId, string>();

        public ViewTemplateRebaseService(IAiService aiService)
        {
            _aiService = aiService;
        }

        private static void UpdateProgress(IProgress<string> progress, string message)
        {
            progress?.Report(message);
            LogHelper.Information($"ViewTemplateRebaseService: {message}");
            Application.Current?.Dispatcher?.Invoke(() => { }, DispatcherPriority.Background);
        }

        public async Task<ViewTemplateRebaseResult> RebaseViewTemplatesAsync(
            Document documentToUpdate,
            Document standardSourceDocument,
            IProgress<string> progress = null)
        {
            var result = new ViewTemplateRebaseResult();
            try
            {
                LogHelper.Information("Starting ViewTemplateRebaseService.RebaseViewTemplatesAsync");
                UpdateProgress(progress, "Collecting source view plans with templates...");

                var sourceViewsWithTemplates = CollectViewPlansWithTemplates(documentToUpdate);
                result.SourceViewsProcessed = sourceViewsWithTemplates.Count;
                LogHelper.Information($"Found {sourceViewsWithTemplates.Count} source views with templates");

                if (sourceViewsWithTemplates.Count == 0)
                {
                    throw new Exception("No ViewPlans found with view templates and numeric View Size parameter.");
                }

                StoreOriginalTemplateNames(documentToUpdate, sourceViewsWithTemplates);

                UpdateProgress(progress, $"Found {sourceViewsWithTemplates.Count} views with templates");

                var sourceTemplates = CollectViewTemplateNames(documentToUpdate);
                var targetTemplates = CollectViewTemplateNames(standardSourceDocument);
                LogHelper.Information($"Collected templates - Source: {sourceTemplates.Count}, Target: {targetTemplates.Count}");

                UpdateProgress(progress, $"Source templates: {sourceTemplates.Count}, Target templates: {targetTemplates.Count}");
                UpdateProgress(progress, "Mapping view templates using AI...");

                var strategy = new ViewTemplateMappingPromptStrategy();
                var data = new ViewTemplateMappingPromptData
                {
                    SourceTemplates = sourceTemplates,
                    TargetTemplates = targetTemplates
                };

                LogHelper.Information("Calling AI for template mapping");
                var mappingResponse = await _aiService.GetMappingAsync<ViewTemplateMappingResponse>(strategy, data);
                result.MappingResponse = mappingResponse;
                LogHelper.Information($"AI mapping completed - Mapped: {mappingResponse.Mappings?.Count ?? 0}, Unmapped: {mappingResponse.Unmapped?.Count ?? 0}");

                UpdateProgress(progress, "Starting view template rebase process...");

                using (var transaction = new Transaction(documentToUpdate, "Rebase View Templates"))
                {
                    transaction.Start();
                    try
                    {
                        LogHelper.Information("Starting transaction operations");

                        UpdateProgress(progress, "Removing view templates from source views...");
                        RemoveViewTemplatesFromViews(sourceViewsWithTemplates);
                        LogHelper.Information($"Removed view templates from {sourceViewsWithTemplates.Count} views");

                        UpdateProgress(progress, "Deleting unmapped view templates...");
                        result.TemplatesDeleted = DeleteUnmappedViewTemplates(documentToUpdate, mappingResponse);
                        LogHelper.Information($"Deleted {result.TemplatesDeleted} unmapped view templates");

                        UpdateProgress(progress, "Copying view templates from template...");
                        result.TemplatesCopied = CopyViewTemplatesFromTemplate(documentToUpdate, standardSourceDocument, targetTemplates).Count;
                        LogHelper.Information($"Copied {result.TemplatesCopied} view templates from template");

                        UpdateProgress(progress, "Applying mapped view templates...");
                        ApplyMappedViewTemplates(documentToUpdate, sourceViewsWithTemplates, mappingResponse.Mappings);
                        result.ViewsMapped = mappingResponse.Mappings.Count;
                        LogHelper.Information($"Applied view templates to {result.ViewsMapped} views");

                        LogHelper.Information("Committing transaction");
                        transaction.Commit();
                        result.Success = true;
                        UpdateProgress(progress, "View template rebase completed successfully!");
                        LogHelper.Information("ViewTemplateRebaseService.RebaseViewTemplatesAsync completed successfully");
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"Transaction failed in ViewTemplateRebaseService: {ex.Message}");
                        LogHelper.Error($"Stack trace: {ex.StackTrace}");
                        throw new Exception($"Transaction failed: {ex.Message}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"ViewTemplateRebaseService.RebaseViewTemplatesAsync failed: {ex.Message}");
                LogHelper.Error($"Stack trace: {ex.StackTrace}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                UpdateProgress(progress, $"Error: {ex.Message}");
            }

            return result;
        }

        private List<ViewPlan> CollectViewPlansWithTemplates(Document documentToUpdate)
        {
            return new FilteredElementCollector(documentToUpdate)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewTemplateId != ElementId.InvalidElementId && !v.IsTemplate)
                .ToList();
        }

        private List<string> CollectViewTemplateNames(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .ToList();
        }

        private void StoreOriginalTemplateNames(Document documentToUpdate, List<ViewPlan> views)
        {
            var templateLookup = new FilteredElementCollector(documentToUpdate)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToDictionary(v => v.Id, v => v.Name);

            foreach (var view in views)
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId &&
                    templateLookup.ContainsKey(view.ViewTemplateId))
                {
                    _originalTemplateNames[view.Id] = templateLookup[view.ViewTemplateId];
                }
            }
        }

        private string GetOriginalTemplateName(ViewPlan view)
        {
            return _originalTemplateNames.ContainsKey(view.Id)
                ? _originalTemplateNames[view.Id]
                : string.Empty;
        }

        private void RemoveViewTemplatesFromViews(List<ViewPlan> views)
        {
            foreach (var view in views)
            {
                view.ViewTemplateId = ElementId.InvalidElementId;
            }
        }

        private List<View> CopyViewTemplatesFromTemplate(Document documentToUpdate, Document standardSourceDocument, List<string> templateNames)
        {
            var templateElements = new FilteredElementCollector(standardSourceDocument)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && templateNames.Contains(v.Name))
                .Select(v => v.Id)
                .ToList();

            if (templateElements.Any())
            {
                var copiedIds = ElementTransformUtils.CopyElements(
                    standardSourceDocument,
                    templateElements,
                    documentToUpdate,
                    Transform.Identity,
                    new CopyPasteOptions());

                return copiedIds.Select(id => documentToUpdate.GetElement(id) as View)
                              .Where(v => v != null)
                              .ToList();
            }

            return new List<View>();
        }

        private void ApplyMappedViewTemplates(Document documentToUpdate, List<ViewPlan> views, List<ViewTemplateMapping> mappings)
        {
            var templateLookup = new FilteredElementCollector(documentToUpdate)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToDictionary(v => v.Name, v => v.Id);

            foreach (var view in views)
            {
                var originalTemplateName = GetOriginalTemplateName(view);
                var mapping = mappings.FirstOrDefault(m => m.SourceTemplate == originalTemplateName);

                if (mapping != null && templateLookup.ContainsKey(mapping.TargetTemplate))
                {
                    view.ViewTemplateId = templateLookup[mapping.TargetTemplate];
                }
            }
        }

        private int DeleteUnmappedViewTemplates(Document documentToUpdate, ViewTemplateMappingResponse mappingResponse)
        {
            var allTemplates = new FilteredElementCollector(documentToUpdate)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .ToList();

            var unmappedTemplateNames = mappingResponse.Unmapped.Select(u => u.SourceTemplate).ToHashSet();
            var mappedTemplateNames = mappingResponse.Mappings.Select(m => m.SourceTemplate).ToHashSet();

            var templatesToDelete = allTemplates
                .Where(t => unmappedTemplateNames.Contains(t.Name) || mappedTemplateNames.Contains(t.Name))
                .ToList();

            int deletedCount = 0;
            foreach (var template in templatesToDelete)
            {
                try
                {
                    documentToUpdate.Delete(template.Id);
                    deletedCount++;
                }
                catch (Exception)
                {
                    // Continue with other templates even if one fails
                }
            }

            return deletedCount;
        }
    }

    public class ViewTemplateRebaseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int SourceViewsProcessed { get; set; }
        public int TemplatesDeleted { get; set; }
        public int TemplatesCopied { get; set; }
        public int ViewsMapped { get; set; }
        public int ViewsUnmatched { get; set; }
        public ViewTemplateMappingResponse MappingResponse { get; set; }
    }

    public class ViewTemplateMappingResponse
    {
        public List<ViewTemplateMapping> Mappings { get; set; }
        public List<UnmappedViewTemplate> Unmapped { get; set; }
    }

    public class ViewTemplateMapping
    {
        public string SourceTemplate { get; set; }
        public string TargetTemplate { get; set; }
    }

    public class UnmappedViewTemplate
    {
        public string SourceTemplate { get; set; }
    }
}