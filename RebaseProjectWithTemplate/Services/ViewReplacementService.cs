using Autodesk.Revit.DB;
using System;
using System.Linq;
using System.Threading;

namespace RebaseProjectWithTemplate.Services
{
    public class ViewReplacementService : IDisposable
    {
        public ViewReplacementResult ReplaceLegends(
            Document sourceDocument,
            Document templateDocument,
            IProgress<string> progress = null)
        {
            var result = new ViewReplacementResult();

            using (var transaction = new Transaction(sourceDocument, "Replace Legends"))
            {
                transaction.Start();

                try
                {
                    // Get template legend from source document (will be used as template)
                    var templateLegend = ViewCopyService.GetFirstLegendTemplate(sourceDocument);
                    if (templateLegend == null)
                    {
                        throw new Exception("No legend found in source document to use as template");
                    }

                    // Get all legends from template document
                    var templateLegends = ViewCopyService.GetLegendViews(templateDocument);
                    if (templateLegends.Count == 0)
                    {
                        progress?.Report("No legends found in template document");
                        result.Success = true;
                        transaction.Commit();
                        return result;
                    }

                    // Get all legends from source document except the template
                    var sourceLegendsToDelete = ViewCopyService.GetLegendViews(sourceDocument)
                        .Where(v => v.Id != templateLegend.Id)
                        .ToList();

                    // Delete existing legends (except template)
                    ViewCopyService.DeleteViews(sourceDocument, sourceLegendsToDelete);

                    // Create new legends based on template document
                    foreach (var sourceLegend in templateLegends)
                    {
                        // Duplicate the template legend
                        var newLegendId = templateLegend.Duplicate(ViewDuplicateOption.Duplicate);
                        var newLegend = sourceDocument.GetElement(newLegendId) as Autodesk.Revit.DB.View;

                        if (newLegend != null)
                        {
                            // Set properties (name and scale)
                            ViewCopyService.SetViewProperties(newLegend, sourceLegend, sourceLegend.Name);

                            // Copy content from template legend
                            ViewCopyService.CopyViewContent(sourceLegend, newLegend, CancellationToken.None);
                        }
                    }

                    // Delete the template legend
                    sourceDocument.Delete(templateLegend.Id);

                    transaction.Commit();
                    result.Success = true;
                    result.ViewsProcessed = templateLegends.Count;
                    progress?.Report($"Replaced {templateLegends.Count} legends");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to replace legends: {ex.Message}", ex);
                }
            }

            return result;
        }

        public ViewReplacementResult ReplaceDraftingViews(
            Document sourceDocument,
            Document templateDocument,
            IProgress<string> progress = null)
        {
            var result = new ViewReplacementResult();

            using (var transaction = new Transaction(sourceDocument, "Replace Drafting Views"))
            {
                transaction.Start();

                try
                {
                    // Get template drafting view from source document (will be used as template)
                    var templateDrafting = ViewCopyService.GetFirstDraftingTemplate(sourceDocument);
                    if (templateDrafting == null)
                    {
                        throw new Exception("No drafting view found in source document to use as template");
                    }

                    // Get all drafting views from template document
                    var templateDraftingViews = ViewCopyService.GetDraftingViews(templateDocument);
                    if (templateDraftingViews.Count == 0)
                    {
                        progress?.Report("No drafting views found in template document");
                        result.Success = true;
                        transaction.Commit();
                        return result;
                    }

                    // Get all drafting views from source document except the template
                    var sourceDraftingToDelete = ViewCopyService.GetDraftingViews(sourceDocument)
                        .Where(v => v.Id != templateDrafting.Id)
                        .ToList();

                    // Delete existing drafting views (except template)
                    ViewCopyService.DeleteViews(sourceDocument, sourceDraftingToDelete);

                    // Create new drafting views based on template document
                    foreach (var sourceDraftingView in templateDraftingViews)
                    {
                        // Duplicate the template drafting view
                        var newDraftingId = templateDrafting.Duplicate(ViewDuplicateOption.Duplicate);
                        var newDraftingView = sourceDocument.GetElement(newDraftingId) as Autodesk.Revit.DB.View;

                        if (newDraftingView != null)
                        {
                            // Set properties (name, scale, and parameters)
                            ViewCopyService.SetViewProperties(newDraftingView, sourceDraftingView, sourceDraftingView.Name);

                            // Copy content from template drafting view
                            ViewCopyService.CopyViewContent(sourceDraftingView, newDraftingView, CancellationToken.None);
                        }
                    }

                    // Delete the template drafting view
                    sourceDocument.Delete(templateDrafting.Id);

                    transaction.Commit();
                    result.Success = true;
                    result.ViewsProcessed = templateDraftingViews.Count;
                    progress?.Report($"Replaced {templateDraftingViews.Count} drafting views");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to replace drafting views: {ex.Message}", ex);
                }
            }

            return result;
        }

        public void Dispose()
        {
            // Nothing to dispose currently
        }
    }

    public class ViewReplacementResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int ViewsProcessed { get; set; }
    }
}
