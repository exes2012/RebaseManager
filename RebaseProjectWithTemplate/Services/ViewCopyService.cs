using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RebaseProjectWithTemplate.Services
{
    public static class ViewCopyService
    {
        public static void CopyViewContent(Autodesk.Revit.DB.View sourceView, Autodesk.Revit.DB.View targetView, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elementsToCopy = GetAnnotations(sourceView);

            if (elementsToCopy.Count == 0)
                return;

            var copyPasteOptions = new CopyPasteOptions();
            copyPasteOptions.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

            ElementTransformUtils.CopyElements(sourceView, elementsToCopy, targetView, null, copyPasteOptions);
        }

        public static List<ElementId> GetAnnotations(Autodesk.Revit.DB.View view)
        {
            var categoryList = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Dimensions,
                BuiltInCategory.OST_Tags,
                BuiltInCategory.OST_Lines,
                BuiltInCategory.OST_DetailComponents,
                BuiltInCategory.OST_DetailComponentTags,
                BuiltInCategory.OST_RasterImages,
                BuiltInCategory.OST_TextNotes,
                BuiltInCategory.OST_ModelText,
                BuiltInCategory.OST_FilledRegion
            };

            var output = new FilteredElementCollector(view.Document)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementOwnerViewFilter(view.Id))
                .WherePasses(new ElementMulticategoryFilter(categoryList, false))
                .Where(e => e.Category != null)
                .Select(e => e.Id)
                .ToList();

            var dwgElements = new FilteredElementCollector(view.Document)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementOwnerViewFilter(view.Id))
                .OfClass(typeof(ImportInstance))
                .Select(e => e.Id)
                .ToList();

            output.AddRange(dwgElements);

            return output;
        }

        public static List<Autodesk.Revit.DB.View> GetLegendViews(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.ViewType == ViewType.Legend)
                .ToList();
        }

        public static List<Autodesk.Revit.DB.View> GetDraftingViews(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .Where(v => v.ViewType == ViewType.DraftingView)
                .ToList();
        }

        public static Autodesk.Revit.DB.View GetFirstLegendTemplate(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .FirstOrDefault(v => v.ViewType == ViewType.Legend);
        }

        public static Autodesk.Revit.DB.View GetFirstDraftingTemplate(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .FirstOrDefault(v => v.ViewType == ViewType.DraftingView);
        }

        public static void SetViewProperties(Autodesk.Revit.DB.View targetView, Autodesk.Revit.DB.View sourceView, string newName)
        {
            // Set name
            if (!string.IsNullOrEmpty(newName))
            {
                targetView.Name = newName;
            }

            // Set scale
            try
            {
                targetView.Scale = sourceView.Scale;
            }
            catch (Exception)
            {
                // Scale setting failed, continue anyway
            }

            // For drafting views, set additional parameters
            if (targetView.ViewType == ViewType.DraftingView && sourceView.ViewType == ViewType.DraftingView)
            {
                SetDraftingViewParameters(targetView, sourceView);
            }
        }

        private static void SetDraftingViewParameters(Autodesk.Revit.DB.View targetView, Autodesk.Revit.DB.View sourceView)
        {
            try
            {
                // Set View Size parameter
                var viewSizeParam = sourceView.LookupParameter("View Size");
                var targetViewSizeParam = targetView.LookupParameter("View Size");
                if (viewSizeParam != null && targetViewSizeParam != null && !viewSizeParam.IsReadOnly)
                {
                    if (viewSizeParam.StorageType == StorageType.String)
                    {
                        targetViewSizeParam.Set(viewSizeParam.AsString());
                    }
                }

                // Set View Category parameter
                var viewCategoryParam = sourceView.LookupParameter("View Category");
                var targetViewCategoryParam = targetView.LookupParameter("View Category");
                if (viewCategoryParam != null && targetViewCategoryParam != null && !viewCategoryParam.IsReadOnly)
                {
                    if (viewCategoryParam.StorageType == StorageType.String)
                    {
                        targetViewCategoryParam.Set(viewCategoryParam.AsString());
                    }
                }

                // Set Category parameter
                var categoryParam = sourceView.LookupParameter("Category");
                var targetCategoryParam = targetView.LookupParameter("Category");
                if (categoryParam != null && targetCategoryParam != null && !categoryParam.IsReadOnly)
                {
                    if (categoryParam.StorageType == StorageType.String)
                    {
                        targetCategoryParam.Set(categoryParam.AsString());
                    }
                }
            }
            catch (Exception)
            {
                // Parameter setting failed, continue anyway
            }
        }

        public static void DeleteViews(Document document, List<Autodesk.Revit.DB.View> viewsToDelete)
        {
            foreach (var view in viewsToDelete)
            {
                try
                {
                    document.Delete(view.Id);
                }
                catch (Exception)
                {
                    // Continue if deletion fails
                }
            }
        }
    }

    public class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
