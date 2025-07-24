using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

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
        return new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(v => v.ViewTemplateId != ElementId.InvalidElementId && !v.IsTemplate)
            .ToList();
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
            tx.Start();
            foreach (var viewId in viewIds)
            {
                var view = _doc.GetElement(viewId) as ViewPlan;
                if (view != null) view.ViewTemplateId = ElementId.InvalidElementId;
            }

            tx.Commit();
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
            tx.Start();
            foreach (var template in templatesToDelete)
                try
                {
                    _doc.Delete(template.Id);
                    deletedCount++;
                }
                catch (Exception)
                {
                }

            tx.Commit();
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
            using (var tx = new Transaction(_doc, "Copy View Templates"))
            {
                tx.Start();
                var copiedIds = ElementTransformUtils.CopyElements(_tpl, templateElements, _doc, Transform.Identity,
                    new CopyPasteOptions());
                tx.Commit();
                return copiedIds.ToList();
            }

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
            tx.Start();
            foreach (var viewId in originalTemplateNames.Keys)
            {
                var view = _doc.GetElement(viewId) as ViewPlan;
                if (view == null) continue;

                var originalTemplateName = originalTemplateNames[viewId];
                var mapping = mappings.FirstOrDefault(m => m.SourceTemplate == originalTemplateName);

                if (mapping != null && templateLookup.ContainsKey(mapping.TargetTemplate))
                    view.ViewTemplateId = templateLookup[mapping.TargetTemplate];
            }

            tx.Commit();
        }
    }
}