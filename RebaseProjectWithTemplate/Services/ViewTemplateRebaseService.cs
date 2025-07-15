using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RebaseProjectWithTemplate.Models;

namespace RebaseProjectWithTemplate.Services
{
    public class ViewTemplateRebaseService : IDisposable
    {
        private readonly GrokApiService _grokService;
        private const string UnmatchedViewSizeValue = "UNMATCHED";
        private const string FilterPrefix = "Z-OLD-";
        private Dictionary<ElementId, string> _originalTemplateNames = new Dictionary<ElementId, string>();



        public ViewTemplateRebaseService()
        {
            _grokService = new GrokApiService();
        }

        public async Task<ViewTemplateRebaseResult> RebaseViewTemplatesAsync(
            Document sourceDocument,
            Document templateDocument,
            IProgress<string> progress = null)
        {
            var result = new ViewTemplateRebaseResult();
            
            try
            {
                progress?.Report("Collecting source view plans with templates...");

                // Step 1: Collect all ViewPlans with assigned view templates
                var sourceViewsWithTemplates = CollectViewPlansWithTemplates(sourceDocument);
                result.SourceViewsProcessed = sourceViewsWithTemplates.Count;

                if (sourceViewsWithTemplates.Count == 0)
                {
                    throw new Exception("No ViewPlans found with view templates and numeric View Size parameter.");
                }

                // Store original template names before removing them
                StoreOriginalTemplateNames(sourceDocument, sourceViewsWithTemplates);

                progress?.Report($"Found {sourceViewsWithTemplates.Count} views with templates");

                // Step 2: Collect view templates from both documents
                var sourceTemplates = CollectViewTemplateNames(sourceDocument);
                var targetTemplates = CollectViewTemplateNames(templateDocument);

                progress?.Report($"Source templates: {sourceTemplates.Count}, Target templates: {targetTemplates.Count}");
                progress?.Report("Mapping view templates using AI...");

                // Step 3: Map view templates using Grok API
                var mappingResponse = await _grokService.MapViewTemplatesAsync(sourceTemplates, targetTemplates);
                result.MappingResponse = mappingResponse;
                
                progress?.Report("Starting view template rebase process...");
                
                using (var transaction = new Transaction(sourceDocument, "Rebase View Templates"))
                {
                    transaction.Start();

                    try
                    {
                        // Step 4a: Mark unmapped view templates for deletion and mapped for replacement
                        progress?.Report("Analyzing view templates for deletion/replacement...");
                        MarkViewTemplatesForProcessing(sourceDocument, mappingResponse);

                        // Step 4b: Remove view templates from source views
                        progress?.Report("Removing view templates from source views...");
                        RemoveViewTemplatesFromViews(sourceViewsWithTemplates);

                        // Step 4c: Rename filters with Z-OLD prefix
                        progress?.Report("Renaming existing filters...");
                        RenameFiltersWithPrefix(sourceDocument);

                        // Step 4d: Delete unmapped view templates
                        progress?.Report("Deleting unmapped view templates...");
                        var deletedTemplatesCount = DeleteUnmappedViewTemplates(sourceDocument, mappingResponse);
                        result.TemplatesDeleted = deletedTemplatesCount;

                        // Step 4e: Copy view templates from template document
                        progress?.Report("Copying view templates from template...");
                        var copiedTemplates = CopyViewTemplatesFromTemplate(sourceDocument, templateDocument, targetTemplates);
                        result.TemplatesCopied = copiedTemplates.Count;

                        // Step 4f: Apply mapped view templates
                        progress?.Report("Applying mapped view templates...");
                        ApplyMappedViewTemplates(sourceDocument, sourceViewsWithTemplates, mappingResponse.Mappings);
                        result.ViewsMapped = mappingResponse.Mappings.Count;

                        // Step 4g: Set UNMATCHED for unmapped views
                        progress?.Report("Setting UNMATCHED for unmapped views...");
                        SetUnmatchedViewSize(sourceDocument, sourceViewsWithTemplates, mappingResponse.Unmapped);
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
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                progress?.Report($"Error: {ex.Message}");
            }
            
            return result;
        }

        private List<ViewPlan> CollectViewPlansWithTemplates(Document document)
        {
            var viewSizeParam = GetViewSizeParameter(document);

            var collector = new FilteredElementCollector(document)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.ViewTemplateId != ElementId.InvalidElementId &&
                           !v.IsTemplate &&
                           HasNumericViewSize(v, viewSizeParam))
                .ToList();

            return collector;
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
            var templates = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .ToList();

            return templates;
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
                .ToList();

            foreach (var filter in filters)
            {
                if (!filter.Name.StartsWith(FilterPrefix))
                {
                    filter.Name = FilterPrefix + filter.Name;
                }
            }
        }

        private List<Autodesk.Revit.DB.View> CopyViewTemplatesFromTemplate(Document sourceDoc, Document templateDoc, List<string> templateNames)
        {
            var templateElements = new FilteredElementCollector(templateDoc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate && templateNames.Contains(v.Name))
                .Select(v => v.Id)
                .ToList();

            if (templateElements.Any())
            {
                var copiedIds = ElementTransformUtils.CopyElements(
                    templateDoc,
                    templateElements,
                    sourceDoc,
                    Transform.Identity,
                    new CopyPasteOptions());

                return copiedIds.Select(id => sourceDoc.GetElement(id) as Autodesk.Revit.DB.View)
                              .Where(v => v != null)
                              .ToList();
            }

            return new List<Autodesk.Revit.DB.View>();
        }

        private void ApplyMappedViewTemplates(Document document, List<ViewPlan> views, List<ViewTemplateMapping> mappings)
        {
            var templateLookup = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
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

        private void SetUnmatchedViewSize(Document document, List<ViewPlan> views, List<UnmappedViewTemplate> unmapped)
        {
            var viewSizeParam = GetViewSizeParameter(document);
            if (viewSizeParam == null) return;

            var unmappedTemplateNames = unmapped.Select(u => u.SourceTemplate).ToHashSet();

            foreach (var view in views)
            {
                var originalTemplateName = GetOriginalTemplateName(view);
                if (unmappedTemplateNames.Contains(originalTemplateName))
                {
                    var param = view.get_Parameter(viewSizeParam);
                    if (param != null && !param.IsReadOnly)
                    {
                        param.Set(UnmatchedViewSizeValue);
                    }
                }
            }
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

            return null;
        }

        private void MarkViewTemplatesForProcessing(Document document, ViewTemplateMappingResponse mappingResponse)
        {
            // Get all view templates in the source document
            var allTemplates = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate)
                .ToList();

            // Get mapped and unmapped template names
            var mappedTemplateNames = mappingResponse.Mappings.Select(m => m.SourceTemplate).ToHashSet();
            var unmappedTemplateNames = mappingResponse.Unmapped.Select(u => u.SourceTemplate).ToHashSet();

            // Mark templates for deletion (unmapped) or replacement (mapped)
            foreach (var template in allTemplates)
            {
                if (unmappedTemplateNames.Contains(template.Name))
                {
                    // Mark for deletion - we'll delete these after copying new templates
                }
                else if (mappedTemplateNames.Contains(template.Name))
                {
                    // Mark for replacement - these will be replaced by new templates
                }
            }
        }

        private int DeleteUnmappedViewTemplates(Document document, ViewTemplateMappingResponse mappingResponse)
        {
            // Get all view templates in the source document
            var allTemplates = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.IsTemplate)
                .ToList();

            // Get unmapped template names
            var unmappedTemplateNames = mappingResponse.Unmapped.Select(u => u.SourceTemplate).ToHashSet();

            // Also include mapped template names that will be replaced
            var mappedTemplateNames = mappingResponse.Mappings.Select(m => m.SourceTemplate).ToHashSet();

            // Collect templates to delete (unmapped + mapped that will be replaced)
            var templatesToDelete = allTemplates
                .Where(t => unmappedTemplateNames.Contains(t.Name) || mappedTemplateNames.Contains(t.Name))
                .ToList();

            int deletedCount = 0;
            foreach (var template in templatesToDelete)
            {
                try
                {
                    document.Delete(template.Id);
                    deletedCount++;
                }
                catch (Exception)
                {
                    // Continue with other templates even if one fails
                }
            }

            return deletedCount;
        }

        private void StoreOriginalTemplateNames(Document document, List<ViewPlan> views)
        {
            var templateLookup = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
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
                : "";
        }



        public void Dispose()
        {
            _grokService?.Dispose();
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
}
