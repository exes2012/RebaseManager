using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services;

public class CategoryRebaseReportBuilder
{
    public CategoryRebaseReport BuildReport(
        string documentName,
        BuiltInCategory category,
        List<FamilyData> sourceData,
        List<FamilyData> templateData,
        HashSet<string> exactMatches,
        List<MappingResult> aiMapping,
        Dictionary<ElementId, ElementId> idMap)
    {
        var categoryName = category.ToString().Replace("OST_", "");
        
        var report = new CategoryRebaseReport
        {
            DocumentName = documentName,
            CategoryName = categoryName
        };
        
        // Build exact matches
        BuildExactMatches(report, sourceData, exactMatches);
        
        // Build family mappings
        BuildFamilyMappings(report, sourceData, templateData, aiMapping, idMap);
        
        // Build template-only families
        BuildTemplateOnlyFamilies(report, sourceData, templateData, exactMatches, aiMapping);
        
        return report;
    }
    
    private void BuildExactMatches(CategoryRebaseReport report, List<FamilyData> sourceData, HashSet<string> exactMatches)
    {
        foreach (var exactMatch in exactMatches)
        {
            var sourceFamily = sourceData.FirstOrDefault(f => f.FamilyName == exactMatch);
            if (sourceFamily != null)
            {
                report.ExactMatches.Add(new ExactMatchFamily
                {
                    FamilyName = exactMatch,
                    TypeNames = sourceFamily.Types.Select(t => t.TypeName).ToList()
                });
            }
        }
    }
    
    private void BuildFamilyMappings(
        CategoryRebaseReport report, 
        List<FamilyData> sourceData, 
        List<FamilyData> templateData,
        List<MappingResult> aiMapping, 
        Dictionary<ElementId, ElementId> idMap)
    {
        foreach (var mapping in aiMapping)
        {
            var sourceFamily = sourceData.FirstOrDefault(f => f.FamilyName == mapping.Old);
            if (sourceFamily == null) continue;
            
            var familyReport = new FamilyMappingReport
            {
                SourceFamily = mapping.Old,
                TargetFamily = mapping.New,
                MappingStatus = mapping.New == "No Match" ? "No Match" : "Mapped"
            };
            
            // Build type mappings
            if (mapping.New != "No Match")
            {
                var targetFamily = templateData.FirstOrDefault(f => f.FamilyName == mapping.New);
                BuildTypeMappings(familyReport, sourceFamily, targetFamily, mapping, idMap);
            }
            else
            {
                // All types kept old for unmapped families
                foreach (var sourceType in sourceFamily.Types)
                {
                    familyReport.TypeMappings.Add(new TypeMappingReport
                    {
                        SourceType = sourceType.TypeName,
                        TargetType = "N/A",
                        MappingStatus = "Kept Old"
                    });
                }
            }
            
            report.FamilyMappings.Add(familyReport);
        }
    }
    
    private void BuildTypeMappings(
        FamilyMappingReport familyReport, 
        FamilyData sourceFamily, 
        FamilyData targetFamily,
        MappingResult mapping, 
        Dictionary<ElementId, ElementId> idMap)
    {
        foreach (var sourceType in sourceFamily.Types)
        {
            var typeMapping = mapping.TypeMatches?.FirstOrDefault(tm => tm.OldType == sourceType.TypeName);
            var sourceTypeId = new ElementId((int)sourceType.TypeId);
            
            if (typeMapping != null)
            {
                if (typeMapping.NewType == "No Match")
                {
                    familyReport.TypeMappings.Add(new TypeMappingReport
                    {
                        SourceType = sourceType.TypeName,
                        TargetType = "No Match",
                        MappingStatus = "No Match"
                    });
                }
                else
                {
                    var targetType = targetFamily?.Types.FirstOrDefault(t => t.TypeName == typeMapping.NewType);
                    var wasActuallyMapped = idMap.ContainsKey(sourceTypeId);
                    
                    familyReport.TypeMappings.Add(new TypeMappingReport
                    {
                        SourceType = sourceType.TypeName,
                        TargetType = typeMapping.NewType,
                        MappingStatus = wasActuallyMapped ? "Mapped" : "No Match"
                    });
                }
            }
            else
            {
                // Type not in mapping - kept old
                familyReport.TypeMappings.Add(new TypeMappingReport
                {
                    SourceType = sourceType.TypeName,
                    TargetType = "N/A",
                    MappingStatus = "Kept Old"
                });
            }
        }
    }
    
    private void BuildTemplateOnlyFamilies(
        CategoryRebaseReport report, 
        List<FamilyData> sourceData, 
        List<FamilyData> templateData,
        HashSet<string> exactMatches, 
        List<MappingResult> aiMapping)
    {
        var mappedTemplateNames = aiMapping
            .Where(m => m.New != "No Match")
            .Select(m => m.New)
            .ToHashSet();
        
        var templateOnlyFamilies = templateData
            .Where(tf => !exactMatches.Contains(tf.FamilyName) && !mappedTemplateNames.Contains(tf.FamilyName))
            .ToList();
        
        report.TemplateOnlyFamilies.AddRange(templateOnlyFamilies);
    }
}
