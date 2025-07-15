using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Core.Prompts;
using RebaseProjectWithTemplate.Infrastructure.Grok;
using RebaseProjectWithTemplate.Core.Prompts;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class ViewTemplateRebaseService
    {
        #region Constants

        private const string UnmatchedViewSizeValue = "UNMATCHED";
        private const string FilterPrefix = "Z-OLD-";

        #endregion

        #region Fields

        private readonly IGrokApiService _grokService;
        private readonly Dictionary<ElementId, string> _originalTemplateNames = new Dictionary<ElementId, string>();

        #endregion

        #region Constructor

        public ViewTemplateRebaseService(IGrokApiService grokService)
        {
            _grokService = grokService;
        }

        #endregion

        #region Public Methods

        public async Task<ViewTemplateRebaseResult> RebaseViewTemplatesAsync(
            Document documentToUpdate,
            Document standardSourceDocument,
            IProgress<string> progress = null)
        {
            var result = new ViewTemplateRebaseResult();

            try
            {
                progress?.Report("Collecting source view plans with templates...");

                var sourceViewsWithTemplates = CollectViewPlansWithTemplates(documentToUpdate);
                result.SourceViewsProcessed = sourceViewsWithTemplates.Count;

                if (sourceViewsWithTemplates.Count == 0)
                {
                    throw new Exception("No ViewPlans found with view templates and numeric View Size parameter.");
                }

                StoreOriginalTemplateNames(documentToUpdate, sourceViewsWithTemplates);

                progress?.Report($"Found {sourceViewsWithTemplates.Count} views with templates");

                var sourceTemplates = CollectViewTemplateNames(documentToUpdate);
                var targetTemplates = CollectViewTemplateNames(standardSourceDocument);

                progress?.Report($"Source templates: {sourceTemplates.Count}, Target templates: {targetTemplates.Count}");
                progress?.Report("Mapping view templates using AI...");

                var mappingStrategy = new ViewTemplateMappingPromptStrategy();
                var promptData = new ViewTemplateMappingPromptData
                {
                    SourceTemplates = sourceTemplates,
                    TargetTemplates = targetTemplates
                };

                var mappingResponse = await _grokService.ExecuteChatCompletionAsync<ViewTemplateMappingResponse>(mappingStrategy, promptData);
                result.MappingResponse = mappingResponse;

                progress?.Report("Starting view template rebase process...");

                using (var transaction = new Transaction(documentToUpdate, "Rebase View Templates"))
                {
                    transaction.Start();

                    try
                    {
                        progress?.Report("Analyzing view templates for deletion/replacement...");
                        MarkViewTemplatesForProcessing(documentToUpdate, mappingResponse);

                        progress?.Report("Removing view templates from source views...");
                        RemoveViewTemplatesFromViews(sourceViewsWithTemplates);

                        progress?.Report("Renaming existing filters...");
                        RenameFiltersWithPrefix(documentToUpdate);

                        progress?.Report("Deleting unmapped view templates...");
                        result.TemplatesDeleted = DeleteUnmappedViewTemplates(documentToUpdate, mappingResponse);

                        progress?.Report("Copying view templates from template...");
                        result.TemplatesCopied = CopyViewTemplatesFromTemplate(documentToUpdate, standardSourceDocument, targetTemplates).Count;

                        progress?.Report("Applying mapped view templates...");
                        ApplyMappedViewTemplates(documentToUpdate, sourceViewsWithTemplates, mappingResponse.Mappings);
                        result.ViewsMapped = mappingResponse.Mappings.Count;

                        progress?.Report("Setting UNMATCHED for unmapped views...");
                        SetUnmatchedViewSize(documentToUpdate, sourceViewsWithTemplates, mappingResponse.Unmapped);
                        result.ViewsUnmatched = mappingResponse.Unmapped.Count;

                        transaction.Commit();
                        result.Success = true;
                        progress?.Report("View template rebase completed successfully!");
                    }
                    catch (Exception ex)
                    {
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



        #endregion

        #region Private Methods

        #region Data Collection

        private List<ViewPlan> CollectViewPlansWithTemplates(Document documentToUpdate)
        {
            var viewSizeParam = GetViewSizeParameter(documentToUpdate);

            return new FilteredElementCollector(documentToUpdate)
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

        #endregion

        #region Revit Operations

        private void RemoveViewTemplatesFromViews(List<ViewPlan> views)
        {
            foreach (var view in views)
            {
                view.ViewTemplateId = ElementId.InvalidElementId;
            }
        }

        private void RenameFiltersWithPrefix(Document documentToUpdate)
        {
            var filters = new FilteredElementCollector(documentToUpdate)
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

        private void SetUnmatchedViewSize(Document documentToUpdate, List<ViewPlan> views, List<UnmappedViewTemplate> unmapped)
        {
            var viewSizeParam = GetViewSizeParameter(documentToUpdate);
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

        private Definition GetViewSizeParameter(Document documentToUpdate)
        {
            var collector = new FilteredElementCollector(documentToUpdate)
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

        private void MarkViewTemplatesForProcessing(Document documentToUpdate, ViewTemplateMappingResponse mappingResponse)
        {
            // This method is currently empty and can be removed or implemented.
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

        #endregion

        #endregion
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

