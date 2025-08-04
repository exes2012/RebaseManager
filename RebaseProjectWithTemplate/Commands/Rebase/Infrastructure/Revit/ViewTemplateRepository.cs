using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Infrastructure;
using System;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit;

public class ViewTemplateRepository : IViewTemplateRepository
{
    private readonly Document _doc;
    private readonly Document _tpl;

    public ViewTemplateRepository(Document doc, Document tpl)
    {
        _doc = doc;
        _tpl = tpl;
    }

    public List<ViewPlan> CollectViewPlansWithTemplates()
    {
        var viewSizeParam = GetViewSizeParameter();

        return new FilteredElementCollector(_doc)
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

    public List<string> CollectViewTemplateNames(bool fromTemplate = false)
    {
        var doc = fromTemplate ? _tpl : _doc;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .Select(v => v.Name)
            .ToList();
    }

    public void RemoveViewTemplatesFromViews(List<ElementId> viewIds)
    {
        using (var tx = new Transaction(_doc, "Remove View Templates"))
        {
            CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
            tx.Start();

            try
            {
                foreach (var viewId in viewIds)
                {
                    try
                    {
                        var view = _doc.GetElement(viewId) as ViewPlan;
                        if (view != null && view.IsValidObject)
                        {
                            view.ViewTemplateId = ElementId.InvalidElementId;
                            LogHelper.Information($"Removed view template from view: {view.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to remove view template from view {viewId}: {ex.Message}");
                    }
                }

                tx.Commit();
                LogHelper.Information($"Successfully removed view templates from {viewIds.Count} views");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to remove view templates: {ex.Message}");
                throw;
            }
        }
    }

    public int DeleteUnmappedViewTemplates(ViewTemplateMappingResponse mappingResponse)
    {
        var allTemplates = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToList();

        var unmappedTemplateNames = mappingResponse.Unmapped.Select(u => u.SourceTemplate).ToHashSet();
        var mappedTemplateNames = mappingResponse.Mappings.Select(m => m.SourceTemplate).ToHashSet();

        var templatesToDelete = allTemplates
            .Where(t => unmappedTemplateNames.Contains(t.Name) || mappedTemplateNames.Contains(t.Name))
            .ToList();

        var deletedCount = 0;
        using (var tx = new Transaction(_doc, "Delete Unmapped View Templates"))
        {
            CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
            tx.Start();

            try
            {
                foreach (var template in templatesToDelete)
                {
                    try
                    {
                        if (template.IsValidObject)
                        {
                            _doc.Delete(template.Id);
                            deletedCount++;
                            LogHelper.Information($"Deleted view template: {template.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to delete view template '{template.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
                LogHelper.Information($"Successfully deleted {deletedCount} view templates");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to delete view templates: {ex.Message}");
                throw;
            }
        }

        return deletedCount;
    }

    public List<ElementId> CopyViewTemplatesFromTemplate(List<string> templateNames)
    {
        var templateElements = new FilteredElementCollector(_tpl)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate && templateNames.Contains(v.Name))
            .Select(v => v.Id)
            .ToList();

        if (templateElements.Any())
        {
            using (var tx = new Transaction(_doc, "Copy View Templates"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    var copyOptions = new CopyPasteOptions();
                    var copiedIds = ElementTransformUtils.CopyElements(_tpl, templateElements, _doc, Transform.Identity, copyOptions);

                    tx.Commit();
                    LogHelper.Information($"Successfully copied {copiedIds.Count} view templates from template document");
                    return copiedIds.ToList();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Failed to copy view templates: {ex.Message}");
                    throw;
                }
            }
        }

        LogHelper.Information("No view templates found to copy");
        return new List<ElementId>();
    }

    public void ApplyMappedViewTemplates(Dictionary<ElementId, string> originalTemplateNames,
        List<ViewTemplateMapping> mappings)
    {
        var templateLookup = new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .ToDictionary(v => v.Name, v => v.Id);

        using (var tx = new Transaction(_doc, "Apply Mapped View Templates"))
        {
            CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
            tx.Start();

            try
            {
                int appliedCount = 0;
                foreach (var viewId in originalTemplateNames.Keys)
                {
                    try
                    {
                        var view = _doc.GetElement(viewId) as ViewPlan;
                        if (view == null || !view.IsValidObject) continue;

                        var originalTemplateName = originalTemplateNames[viewId];
                        var mapping = mappings.FirstOrDefault(m => m.SourceTemplate == originalTemplateName);

                        if (mapping != null && templateLookup.ContainsKey(mapping.TargetTemplate))
                        {
                            view.ViewTemplateId = templateLookup[mapping.TargetTemplate];
                            appliedCount++;
                            LogHelper.Information($"Applied view template '{mapping.TargetTemplate}' to view '{view.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to apply view template to view {viewId}: {ex.Message}");
                    }
                }

                tx.Commit();
                LogHelper.Information($"Successfully applied view templates to {appliedCount} views");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to apply view templates: {ex.Message}");
                throw;
            }
        }
    }

    public void RenameFiltersWithPrefix(string filterPrefix)
    {
        using (var tx = new Transaction(_doc, "Rename Filters"))
        {
            CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
            tx.Start();

            try
            {
                var filters = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .ToList();

                int renamedCount = 0;
                foreach (var filter in filters)
                {
                    try
                    {
                        if (!filter.Name.StartsWith(filterPrefix) && filter.IsValidObject)
                        {
                            var oldName = filter.Name;
                            filter.Name = filterPrefix + filter.Name;
                            renamedCount++;
                            LogHelper.Information($"Renamed filter '{oldName}' to '{filter.Name}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to rename filter '{filter.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
                LogHelper.Information($"Successfully renamed {renamedCount} filters with prefix '{filterPrefix}'");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to rename filters: {ex.Message}");
                throw;
            }
        }
    }

    public void SetUnmatchedViewSize(List<ViewPlan> views, List<UnmappedViewTemplate> unmapped, string unmatchedValue)
    {
        using (var tx = new Transaction(_doc, "Set Unmatched View Size"))
        {
            CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
            tx.Start();

            try
            {
                var viewSizeParam = GetViewSizeParameter();
                if (viewSizeParam == null)
                {
                    LogHelper.Warning("View Size parameter not found, skipping unmatched view size setting");
                    tx.Commit();
                    return;
                }

                var unmappedTemplateNames = unmapped.Select(u => u.SourceTemplate).ToHashSet();
                int updatedCount = 0;

                foreach (var view in views)
                {
                    try
                    {
                        var originalTemplateName = GetOriginalTemplateName(view);
                        if (unmappedTemplateNames.Contains(originalTemplateName))
                        {
                            var param = view.get_Parameter(viewSizeParam);
                            if (param != null && !param.IsReadOnly && view.IsValidObject)
                            {
                                param.Set(unmatchedValue);
                                updatedCount++;
                                LogHelper.Information($"Set view size to '{unmatchedValue}' for view '{view.Name}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to set unmatched view size for view '{view.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
                LogHelper.Information($"Successfully set unmatched view size for {updatedCount} views");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to set unmatched view sizes: {ex.Message}");
                throw;
            }
        }
    }

    private Definition GetViewSizeParameter()
    {
        var collector = new FilteredElementCollector(_doc)
            .OfClass(typeof(ParameterElement));

        foreach (ParameterElement param in collector)
        {
            if (param.Name.Equals("View Size", StringComparison.OrdinalIgnoreCase))
            {
                return param.GetDefinition();
            }
        }

        return null;
    }

    private string GetOriginalTemplateName(ViewPlan view)
    {
        // This would need to be coordinated with the orchestrator's stored names
        // For now, return empty string as fallback
        return "";
    }
}