using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

public interface IViewTemplateRepository
{
    List<ViewPlan> CollectViewPlansWithTemplates();
    List<string> CollectViewTemplateNames(bool fromTemplate = false);
    void RemoveViewTemplatesFromViews(List<ElementId> viewIds);
    int DeleteUnmappedViewTemplates(ViewTemplateMappingResponse mappingResponse);
    List<ElementId> CopyViewTemplatesFromTemplate(List<string> templateNames);

    void ApplyMappedViewTemplates(Dictionary<ElementId, string> originalTemplateNames,
        List<ViewTemplateMapping> mappings);

    void RenameFiltersWithPrefix(string filterPrefix);
    void SetUnmatchedViewSize(List<ViewPlan> views, List<UnmappedViewTemplate> unmapped, string unmatchedValue);
}