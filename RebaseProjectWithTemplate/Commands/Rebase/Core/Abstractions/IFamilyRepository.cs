using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

public interface IFamilyRepository
{
    List<FamilyData> CollectFamilyData(BuiltInCategory category, bool fromTemplate = false);
    void RenameFamilies(BuiltInCategory category, IEnumerable<string> familyNames, string suffix, IProgress<string> progress = null);
    SwitchInstancesResult SwitchInstancesAndRemoveOldEnhanced(BuiltInCategory category, Dictionary<ElementId, ElementId> idMap, List<MappingResult> mappedFamilies, IProgress<string> progress = null);
    List<string> DeleteFamilies(BuiltInCategory category, List<string> familyNames, IProgress<string> progress = null);
    int DeleteSpecificUnusedFamilies(BuiltInCategory category, List<string> originalFamilyNames, IProgress<string> progress = null);
    Dictionary<string, object> CacheInstanceParametersByName(FamilyInstance inst);

    // New methods for enhanced orchestrator
    FamilyData LoadSingleFamily(BuiltInCategory category, string familyName);
    bool HasInstances(BuiltInCategory category, string familyName);
    Document GetDocument();
}