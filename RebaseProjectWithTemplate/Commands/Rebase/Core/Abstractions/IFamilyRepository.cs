using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

public interface IFamilyRepository
{
    List<FamilyData> CollectFamilyData(BuiltInCategory category, bool fromTemplate = false);
    void RenameFamilies(BuiltInCategory category, IEnumerable<string> familyNames, string suffix, IProgress<string> progress = null);
    void LoadFamilies(BuiltInCategory category, IEnumerable<string> familyNames);
    void CopyTemplateTypes(IEnumerable<ElementId> typeIds, IProgress<string> progress = null);

    Dictionary<ElementId, ElementId> BuildIdMap(Dictionary<string, FamilyData> oldData,
        Dictionary<string, FamilyData> newData, List<MappingResult> mapping);

    int SwitchInstances(BuiltInCategory category, Dictionary<ElementId, ElementId> idMap);
    int PurgeFamilies(BuiltInCategory category, string suffix);
}