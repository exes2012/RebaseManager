using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using RebaseProjectWithTemplate.Infrastructure;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services;

public class ViewTemplateRebaseOrchestrator
{
    private readonly IAiService _aiService;
    private readonly Document _sourceDocument;
    private readonly Document _templateDocument;
    private const string UnmatchedViewSizeValue = "UNMATCHED";
    private const string FilterPrefix = "Z-OLD-";
    private Dictionary<ElementId, string> _originalTemplateNames = new Dictionary<ElementId, string>();

    public ViewTemplateRebaseOrchestrator(Document sourceDocument, Document templateDocument, IAiService aiService)
    {
        _sourceDocument = sourceDocument;
        _templateDocument = templateDocument;
        _aiService = aiService;
    }

    public async Task<ViewTemplateRebaseResult> RebaseAsync(IPromptStrategy strategy, IProgress<string> progress = null, CancellationToken cancellationToken = default)
    {
        var result = new ViewTemplateRebaseResult();

        try
        {
            LogHelper.Information("Starting ViewTemplate rebase operation");
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Collecting source view plans with templates...");

            // Step 1: Collect all ViewPlans with assigned view templates
            var sourceViewsWithTemplates = CollectViewPlansWithTemplates(_sourceDocument);
            result.SourceViewsProcessed = sourceViewsWithTemplates.Count;

            if (sourceViewsWithTemplates.Count == 0)
            {
                throw new Exception("No ViewPlans found with view templates and numeric View Size parameter.");
            }

            // Store original template names before removing them
            StoreOriginalTemplateNames(_sourceDocument, sourceViewsWithTemplates);

            progress?.Report($"Found {sourceViewsWithTemplates.Count} views with templates");

            // Step 2: Collect view templates from both documents
            var sourceTemplates = CollectViewTemplateNames(_sourceDocument);
            var targetTemplates = CollectViewTemplateNames(_templateDocument);

            progress?.Report($"Source templates: {sourceTemplates.Count}, Target templates: {targetTemplates.Count}");
            progress?.Report("Mapping view templates using AI...");

            cancellationToken.ThrowIfCancellationRequested();

            // Step 3: Map view templates using AI
            var mappingResponse = await GetAiMappingAsync(sourceTemplates, targetTemplates, strategy);
            result.MappingResponse = mappingResponse;

            progress?.Report("Starting view template rebase process...");

            using (var transaction = new Transaction(_sourceDocument, "Rebase View Templates"))
            {
                transaction.Start();

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4a: Remove view templates from source views (освобождаем виды)
                    progress?.Report("Removing view templates from source views...");
                    RemoveViewTemplatesFromViews(sourceViewsWithTemplates);

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4b: Rename filters with Z-OLD prefix (сохраняем фильтры)
                    progress?.Report("Renaming existing filters...");
                    RenameFiltersWithPrefix(_sourceDocument);

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4c: Delete ALL view templates (под корень!)
                    progress?.Report("Deleting ALL view templates...");
                    var deletedTemplatesCount = DeleteAllViewTemplates(_sourceDocument);
                    result.TemplatesDeleted = deletedTemplatesCount;

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4d: Copy ALL view templates from template document
                    progress?.Report("Copying ALL view templates from template...");
                    var copiedTemplates = CopyAllViewTemplatesFromTemplate(_sourceDocument, _templateDocument, targetTemplates);
                    result.TemplatesCopied = copiedTemplates.Count;

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4e: Apply mapped view templates (где есть mapping)
                    progress?.Report("Applying mapped view templates...");
                    var appliedCount = ApplyMappedViewTemplates(_sourceDocument, sourceViewsWithTemplates, mappingResponse.Mappings);
                    result.ViewsMapped = appliedCount;

                    cancellationToken.ThrowIfCancellationRequested();

                    // Step 4f: Set UNMATCHED for unmapped views (маркируем для юзера)
                    progress?.Report("Setting UNMATCHED for unmapped views...");
                    SetUnmatchedViewSize(_sourceDocument, sourceViewsWithTemplates, mappingResponse.Unmapped);
                    result.ViewsUnmatched = mappingResponse.Unmapped.Count;

                    transaction.Commit();
                    result.Success = true;
                    progress?.Report("View template rebase completed successfully!");
                }
                catch (Exception ex)
                {
                    transaction.RollBack();
                    throw new Exception($"Transaction failed: {ex.Message}", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Operation was cancelled by user.";
            progress?.Report("View template rebase was cancelled.");
            LogHelper.Warning("ViewTemplate rebase operation was cancelled by user");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            progress?.Report($"Error: {ex.Message}");
            LogHelper.Error($"ViewTemplate rebase operation failed: {ex.Message}");
        }

        return result;
    }

    private async Task<ViewTemplateMappingResponse> GetAiMappingAsync(List<string> sourceTemplates,
        List<string> targetTemplates, IPromptStrategy strategy)
    {
        var promptData = new ViewTemplateMappingPromptData
        {
            SourceTemplates = sourceTemplates,
            TargetTemplates = targetTemplates
        };

        return await _aiService.GetMappingAsync<ViewTemplateMappingResponse>(strategy, promptData);
    }

    private List<ViewPlan> CollectViewPlansWithTemplates(Document document)
    {
        var viewSizeParam = GetViewSizeParameter(document);

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(v => v.ViewTemplateId != ElementId.InvalidElementId &&
                       !v.IsTemplate &&
                       HasNumericViewSize(v, viewSizeParam))
            .ToList();
    }

    private bool HasNumericViewSize(ViewPlan view, Definition viewSizeParam)
    {
        if (viewSizeParam == null) return false;

        var param = view.get_Parameter(viewSizeParam);
        if (param == null || !param.HasValue) return false;

        var viewSizeValue = param.AsString();
        if (string.IsNullOrWhiteSpace(viewSizeValue)) return false;

        // Check if the value contains any digits
        return viewSizeValue.Any(char.IsDigit);
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

    private void StoreOriginalTemplateNames(Document document, List<ViewPlan> views)
    {
        var templateLookup = new FilteredElementCollector(document)
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
        return _originalTemplateNames.TryGetValue(view.Id, out string name) ? name : string.Empty;
    }

    private void RemoveViewTemplatesFromViews(List<ViewPlan> views)
    {
        foreach (var view in views)
        {
            view.ViewTemplateId = ElementId.InvalidElementId;
        }
    }

    private void RenameFiltersWithPrefix(Document document)
    {
        var filters = new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .Where(f => !f.Name.StartsWith(FilterPrefix)) // Не трогаем уже переименованные
            .ToList();

        foreach (var filter in filters)
        {
            try
            {
                filter.Name = FilterPrefix + filter.Name;
            }
            catch (Exception ex)
            {
                // Логируем, но продолжаем работу
                LogHelper.Warning($"Failed to rename filter {filter.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Удаляет ВСЕ view templates из проекта под корень
    /// </summary>
    private int DeleteAllViewTemplates(Document document)
    {
        var allTemplates = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToList();

        int deletedCount = 0;
        foreach (var template in allTemplates)
        {
            try
            {
                document.Delete(template.Id);
                deletedCount++;
            }
            catch (Exception ex)
            {
                // Некоторые templates могут быть системными и не удаляться
                LogHelper.Warning($"Failed to delete template {template.Name}: {ex.Message}");
            }
        }

        LogHelper.Information($"Deleted {deletedCount} view templates out of {allTemplates.Count}");
        return deletedCount;
    }

    /// <summary>
    /// Копирует ВСЕ view templates из template документа
    /// </summary>
    private List<View> CopyAllViewTemplatesFromTemplate(Document sourceDoc, Document templateDoc, List<string> targetTemplateNames)
    {
        var templateElements = new FilteredElementCollector(templateDoc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && targetTemplateNames.Contains(v.Name))
            .Select(v => v.Id)
            .ToList();

        if (!templateElements.Any())
        {
            LogHelper.Warning("No target templates found to copy");
            return new List<View>();
        }

        try
        {
            var copiedIds = ElementTransformUtils.CopyElements(
                templateDoc,
                templateElements,
                sourceDoc,
                Transform.Identity,
                new CopyPasteOptions());

            var copiedTemplates = copiedIds.Select(id => sourceDoc.GetElement(id) as View)
                          .Where(v => v != null)
                          .ToList();

            LogHelper.Information($"Successfully copied {copiedTemplates.Count} view templates");
            return copiedTemplates;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Failed to copy view templates: {ex.Message}");
            throw new Exception($"Failed to copy view templates: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Применяет mapped view templates к видам и возвращает количество успешно примененных
    /// </summary>
    private int ApplyMappedViewTemplates(Document document, List<ViewPlan> views, List<ViewTemplateMapping> mappings)
    {
        // Обновляем lookup после копирования новых templates
        var templateLookup = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToDictionary(v => v.Name, v => v.Id);

        int appliedCount = 0;
        int failedCount = 0;

        foreach (var view in views)
        {
            var originalTemplateName = GetOriginalTemplateName(view);
            var mapping = mappings.FirstOrDefault(m => m.SourceTemplate == originalTemplateName);

            if (mapping != null && templateLookup.ContainsKey(mapping.TargetTemplate))
            {
                try
                {
                    view.ViewTemplateId = templateLookup[mapping.TargetTemplate];
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    LogHelper.Warning($"Failed to apply template {mapping.TargetTemplate} to view {view.Name}: {ex.Message}");
                }
            }
        }

        LogHelper.Information($"Applied view templates: {appliedCount} successful, {failedCount} failed");
        return appliedCount;
    }

    /// <summary>
    /// Устанавливает UNMATCHED для видов без mapping - чтобы юзер их увидел в браузере
    /// </summary>
    private void SetUnmatchedViewSize(Document document, List<ViewPlan> views, List<UnmappedViewTemplate> unmapped)
    {
        var viewSizeParam = GetViewSizeParameter(document);
        if (viewSizeParam == null)
        {
            LogHelper.Warning("View Size parameter not found - cannot mark unmatched views");
            return;
        }

        var unmappedTemplateNames = unmapped.Select(u => u.SourceTemplate).ToHashSet();
        int markedCount = 0;
        int failedCount = 0;

        foreach (var view in views)
        {
            var originalTemplateName = GetOriginalTemplateName(view);
            if (unmappedTemplateNames.Contains(originalTemplateName))
            {
                var param = view.get_Parameter(viewSizeParam);
                if (param != null && !param.IsReadOnly)
                {
                    try
                    {
                        param.Set(UnmatchedViewSizeValue);
                        markedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        LogHelper.Warning($"Failed to set UNMATCHED for view {view.Name}: {ex.Message}");
                    }
                }
                else
                {
                    failedCount++;
                    LogHelper.Warning($"View Size parameter is read-only or not found for view {view.Name}");
                }
            }
        }

        LogHelper.Information($"Marked unmatched views: {markedCount} successful, {failedCount} failed");
    }

    private Definition GetViewSizeParameter(Document document)
    {
        // Look for View Size parameter - this might need adjustment based on actual parameter name
        var collector = new FilteredElementCollector(document)
            .OfClass(typeof(ParameterElement));

        foreach (ParameterElement param in collector)
        {
            if (param.Name.Equals("View Size", StringComparison.OrdinalIgnoreCase))
            {
                return param.GetDefinition();
            }
        }

        LogHelper.Warning("View Size parameter not found in project parameters");
        return null;
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