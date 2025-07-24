using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RebaseProjectWithTemplate.Commands.Export.Models;

namespace RebaseProjectWithTemplate.Commands.Export.Services;

public class AnnotationSymbolsExportService
{
    private readonly Document _document;

    public AnnotationSymbolsExportService(Document document)
    {
        _document = document;
    }

    public void ExportAnnotationSymbols()
    {
        var categoriesToExport = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_ElectricalEquipmentTags,
            BuiltInCategory.OST_GenericAnnotation,
            BuiltInCategory.OST_SpecialityEquipmentTags,
            BuiltInCategory.OST_ElectricalFixtureTags,
            BuiltInCategory.OST_FlexPipeTags,
            BuiltInCategory.OST_MultiCategoryTags,
            BuiltInCategory.OST_StairsRailingTags,
            BuiltInCategory.OST_TitleBlocks
        };

        var allCategoryData = new List<CategoryData>();
        var dirPath = "C:\\temp";
        Directory.CreateDirectory(dirPath);
        var projectName = _document.ProjectInformation.Name;

        foreach (var builtInCategory in categoriesToExport)
        {
            var category = Category.GetCategory(_document, builtInCategory);
            if (category == null) continue;

            var categoryData = new CategoryData
            {
                CategoryName = category.Name,
                Families = new List<FamilyData>()
            };

            var symbols = new FilteredElementCollector(_document)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(builtInCategory)
                .Cast<FamilySymbol>()
                .ToList();

            foreach (var symbol in symbols)
            {
                var family = symbol.Family;
                var familyDataItem = categoryData.Families.FirstOrDefault(f => f.FamilyId == family.Id.IntegerValue);

                if (familyDataItem == null)
                {
                    familyDataItem = new FamilyData
                    {
                        FamilyName = family.Name,
                        FamilyId = family.Id.IntegerValue,
                        Types = new List<FamilyTypeData>()
                    };
                    categoryData.Families.Add(familyDataItem);
                }

                familyDataItem.Types.Add(new FamilyTypeData
                {
                    TypeName = symbol.Name,
                    TypeId = symbol.Id.IntegerValue
                });
            }

            if (categoryData.Families.Any())
            {
                allCategoryData.Add(categoryData);

                // Create simplified data for neural network
                var simplifiedData = categoryData.Families.Select(f => new
                {
                    f.FamilyName,
                    Types = f.Types.Select(t => t.TypeName).ToList()
                }).ToList();

                var simplifiedJson = JsonConvert.SerializeObject(simplifiedData, Formatting.Indented);
                var safeCategoryName = string.Join("_", category.Name.Split(Path.GetInvalidFileNameChars()));
                var simplifiedFilePath = Path.Combine(dirPath, $"{projectName}_{safeCategoryName}.json");
                File.WriteAllText(simplifiedFilePath, simplifiedJson);
            }
        }

        var json = JsonConvert.SerializeObject(allCategoryData, Formatting.Indented);
        var filePath = Path.Combine(dirPath, $"{projectName}.json");

        File.WriteAllText(filePath, json);
    }

    /*
    private IEnumerable<FamilySymbol> GetUsedAnnotationSymbols()
    {
        var annotationCategories = _document.Settings.Categories
            .Cast<Category>()
            .Where(c => c.CategoryType == CategoryType.Annotation && c.AllowsBoundParameters)
            .Select(c => c.Id)
            .ToList();

        if (!annotationCategories.Any())
        {
            return Enumerable.Empty<FamilySymbol>();
        }

        var categoryFilter = new ElementMulticategoryFilter(annotationCategories);

        return new FilteredElementCollector(_document)
            .WherePasses(categoryFilter)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Select(instance => instance.Symbol)
            .Where(symbol => symbol != null)
            .Distinct(new FamilySymbolComparer());
    }
    */
}

internal class FamilySymbolComparer : IEqualityComparer<FamilySymbol>
{
    public bool Equals(FamilySymbol x, FamilySymbol y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
        return x.Id == y.Id;
    }

    public int GetHashCode(FamilySymbol obj)
    {
        return obj.Id.GetHashCode();
    }
}