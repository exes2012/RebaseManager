using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    /// <summary>
    /// Universal orchestrator for migrating element types (floors, walls, ceilings, roofs, etc.) from template to project
    /// </summary>
    public class ElementTypeRebaseOrchestrator
    {
        private readonly Document _sourceDoc;
        private readonly Document _templateDoc;
        private readonly IAiService _aiService;

        private const string OLD_SUFFIX = "_REBASE_OLD";

        public ElementTypeRebaseOrchestrator(Document sourceDoc, Document templateDoc, IAiService aiService)
        {
            _sourceDoc = sourceDoc;
            _templateDoc = templateDoc;
            _aiService = aiService;
        }

        public async Task<ElementTypeRebaseResult> RebaseAsync(
            BuiltInCategory category,
            IPromptStrategy strategy,
            IProgress<string> progress)
        {
            var result = new ElementTypeRebaseResult();
            var startTime = DateTime.Now;

            try
            {
                LogHelper.Information($"[{category}] Starting system element rebase...");

                // STAGE 1: Collect element type data
                progress?.Report($"[{category}] Collecting element type data...");
                var sourceTypes = CollectElementTypes(_sourceDoc, category);
                var templateTypes = CollectElementTypes(_templateDoc, category);

                result.TotalSourceTypes = sourceTypes.Count;
                result.TotalTemplateTypes = templateTypes.Count;

                LogHelper.Information($"[{category}] Found {sourceTypes.Count} source types and {templateTypes.Count} template types");

                if (sourceTypes.Count == 0 || templateTypes.Count == 0)
                {
                    LogHelper.Warning($"[{category}] No element types to process");
                    return result;
                }

                // STAGE 2: Create type mapping (exact + AI)
                progress?.Report($"[{category}] Creating type mapping...");
                var mappingEntries = await CreateTypeMapping(sourceTypes, templateTypes, strategy);
                result.ExactMatches = mappingEntries.Count(e => e.MappingSource == MappingSource.ExactMatch);
                result.AiMapped = mappingEntries.Count(e => e.MappingSource == MappingSource.AI);

                // STAGE 3: Rename existing types
                try
                {
                    progress?.Report($"[{category}] Renaming existing element types...");
                    RenameExistingTypes(mappingEntries, category, progress);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to rename types: {ex.Message}");
                    LogHelper.Error($"Failed to rename types: {ex}");
                }

                // STAGE 4: Copy types from template
                try
                {
                    progress?.Report($"[{category}] Copying element types from template...");
                    CopyTypesFromTemplate(mappingEntries, progress);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to copy types: {ex.Message}");
                    LogHelper.Error($"Failed to copy types: {ex}");
                }

                // STAGE 5: Switch element instances
                try
                {
                    progress?.Report($"[{category}] Switching element instances...");
                    var switchResult = SwitchElementInstances(mappingEntries, category, progress);
                    result.SwitchedInstances = switchResult.SwitchedCount;
                    result.Errors.AddRange(switchResult.Errors);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to switch instances: {ex.Message}");
                    LogHelper.Error($"Failed to switch instances: {ex}");
                }

                // STAGE 6: Delete old types
                try
                {
                    progress?.Report($"[{category}] Cleaning up old types...");
                    result.DeletedTypes = DeleteOldTypes(mappingEntries, category, progress);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to delete old types: {ex.Message}");
                    LogHelper.Error($"Failed to delete old types: {ex}");
                }

                // STAGE 7: Process unmapped types
                try
                {
                    ProcessUnmappedTypes(mappingEntries, category, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to process unmapped types: {ex.Message}");
                    LogHelper.Error($"Failed to process unmapped types: {ex}");
                }

                result.Duration = DateTime.Now - startTime;
                LogHelper.Information($"[{category}] System element rebase completed in {result.Duration.TotalSeconds:F2} seconds");
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Fatal error: {ex.Message}");
                result.Duration = DateTime.Now - startTime;
                LogHelper.Error($"Fatal error in {category} rebase: {ex}");
                return result;
            }
        }

        #region STAGE 1: Data Collection

        private List<ElementTypeInfo> CollectElementTypes(Document doc, BuiltInCategory category)
        {
            var elementTypes = new List<ElementTypeInfo>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .Where(et => et.IsValidObject);

                foreach (var elementType in collector)
                {
                    try
                    {
                        var name = elementType.Name;
                        if (string.IsNullOrEmpty(name) || name.Contains(OLD_SUFFIX)) continue;

                        var info = new ElementTypeInfo
                        {
                            TypeId = elementType.Id,
                            TypeName = name,
                            FamilyName = elementType.FamilyName ?? "Unknown",
                            Category = category,
                            Properties = GetElementTypeProperties(elementType, category)
                        };

                        elementTypes.Add(info);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to collect element type info: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to collect element types for {category}: {ex.Message}");
            }

            return elementTypes;
        }

        private Dictionary<string, object> GetElementTypeProperties(ElementType elementType, BuiltInCategory category)
        {
            var properties = new Dictionary<string, object>();

            try
            {
                if (!elementType.IsValidObject) return properties;

                properties["FamilyName"] = elementType.FamilyName;

                switch (category)
                {
                    case BuiltInCategory.OST_Floors:
                        if (elementType is FloorType floorType)
                        {
                            properties["Thickness"] = GetFloorThickness(floorType);
                            properties["StructuralMaterial"] = GetStructuralMaterial(floorType);
                            try { properties["IsFoundationSlab"] = floorType.IsFoundationSlab; } catch { }
                        }
                        break;
                    case BuiltInCategory.OST_Walls:
                        if (elementType is WallType wallType)
                        {
                            properties["Width"] = GetWallWidth(wallType);
                            try { properties["Function"] = wallType.Function.ToString(); } catch { }
                        }
                        break;
                    case BuiltInCategory.OST_Ceilings:
                        if (elementType is CeilingType ceilingType)
                        {
                            properties["Thickness"] = GetCeilingThickness(ceilingType);
                        }
                        break;
                    case BuiltInCategory.OST_Roofs:
                        if (elementType is RoofType roofType)
                        {
                            properties["Thickness"] = GetRoofThickness(roofType);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Failed to get properties for element type: {ex.Message}");
            }

            return properties;
        }

        private double GetFloorThickness(FloorType floorType)
        {
            try
            {
                if (!floorType.IsValidObject) return 0.0;
                var compStructure = floorType.GetCompoundStructure();
                return compStructure?.GetWidth() ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private double GetWallWidth(WallType wallType)
        {
            try
            {
                return wallType?.IsValidObject == true ? wallType.Width : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private double GetCeilingThickness(CeilingType ceilingType)
        {
            try
            {
                if (!ceilingType.IsValidObject) return 0.0;
                var compStructure = ceilingType.GetCompoundStructure();
                return compStructure?.GetWidth() ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private double GetRoofThickness(RoofType roofType)
        {
            try
            {
                if (!roofType.IsValidObject) return 0.0;
                var compStructure = roofType.GetCompoundStructure();
                return compStructure?.GetWidth() ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private string GetStructuralMaterial(FloorType floorType)
        {
            try
            {
                if (!floorType.IsValidObject) return "Unknown";

                var compStructure = floorType.GetCompoundStructure();
                if (compStructure == null) return "Unknown";

                var layers = compStructure.GetLayers();
                var structLayer = layers.FirstOrDefault(l => l.Function == MaterialFunctionAssignment.Structure);

                if (structLayer != null)
                {
                    var material = _sourceDoc.GetElement(structLayer.MaterialId) as Material;
                    return material?.IsValidObject == true ? material.Name : "Unknown";
                }

                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        #endregion

        #region STAGE 2: Create Mapping

        private async Task<List<ElementTypeMappingEntry>> CreateTypeMapping(
            List<ElementTypeInfo> sourceTypes,
            List<ElementTypeInfo> templateTypes,
            IPromptStrategy strategy)
        {
            var mappingEntries = new List<ElementTypeMappingEntry>();
            var templateTypeNames = templateTypes.Select(t => t.TypeName).ToHashSet();

            // First exact matches
            foreach (var sourceType in sourceTypes)
            {
                var entry = new ElementTypeMappingEntry
                {
                    SourceType = sourceType,
                    ProcessedAt = DateTime.Now
                };

                if (templateTypeNames.Contains(sourceType.TypeName))
                {
                    entry.TargetTypeName = sourceType.TypeName;
                    entry.TargetType = templateTypes.First(t => t.TypeName == sourceType.TypeName);
                    entry.MappingSource = MappingSource.ExactMatch;
                    entry.Status = MappingStatus.Mapped;
                }
                else
                {
                    entry.Status = MappingStatus.Pending;
                }

                mappingEntries.Add(entry);
            }

            // AI mapping for unmapped
            var pendingEntries = mappingEntries.Where(e => e.Status == MappingStatus.Pending).ToList();
            if (pendingEntries.Any())
            {
                try
                {
                    await ProcessAiMapping(pendingEntries, sourceTypes, templateTypes, strategy);
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"AI mapping failed, continuing with exact matches only: {ex.Message}");
                }
            }

            return mappingEntries;
        }

        private async Task ProcessAiMapping(
            List<ElementTypeMappingEntry> pendingEntries,
            List<ElementTypeInfo> sourceTypes,
            List<ElementTypeInfo> templateTypes,
            IPromptStrategy strategy)
        {
            try
            {
                var sourceFamilyData = ConvertToFamilyData(sourceTypes.Where(st =>
                    pendingEntries.Any(pe => pe.SourceType.TypeName == st.TypeName)).ToList());
                var templateFamilyData = ConvertToFamilyData(templateTypes);

                var promptData = new CategoryMappingPromptData
                {
                    OldFamilies = sourceFamilyData,
                    NewFamilies = templateFamilyData
                };

                var aiResults = await _aiService.GetMappingAsync<List<MappingResult>>(strategy, promptData);

                foreach (var aiResult in aiResults)
                {
                    var entry = pendingEntries.FirstOrDefault(e => e.SourceType.TypeName == aiResult.Old);
                    if (entry == null) continue;

                    if (!string.IsNullOrEmpty(aiResult.New) &&
                        aiResult.New != "No Match" &&
                        templateTypes.Any(t => t.TypeName == aiResult.New))
                    {
                        entry.TargetTypeName = aiResult.New;
                        entry.TargetType = templateTypes.First(t => t.TypeName == aiResult.New);
                        entry.MappingSource = MappingSource.AI;
                        entry.Status = MappingStatus.Mapped;

                        LogHelper.Information($"AI mapped system element type: {aiResult.Old} -> {aiResult.New}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"AI mapping failed for system element types: {ex.Message}");
                throw;
            }
        }

        private List<FamilyData> ConvertToFamilyData(List<ElementTypeInfo> elementTypes)
        {
            return elementTypes.Select(et => new FamilyData
            {
                FamilyName = et.TypeName,
                FamilyId = et.TypeId.IntegerValue,
                Types = new List<FamilyTypeData>
                {
                    new FamilyTypeData
                    {
                        TypeName = et.TypeName,
                        TypeId = et.TypeId.IntegerValue
                    }
                }
            }).ToList();
        }

        #endregion

        #region STAGE 3: Rename Types

        private void RenameExistingTypes(List<ElementTypeMappingEntry> mappingEntries, BuiltInCategory category, IProgress<string> progress)
        {
            var toRename = mappingEntries
                .Where(e => e.Status == MappingStatus.Mapped)
                .Select(e => e.SourceType)
                .ToList();

            if (!toRename.Any()) return;

            using (var tx = new Transaction(_sourceDoc, $"Rename existing {category} types"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    foreach (var typeInfo in toRename)
                    {
                        try
                        {
                            var elementType = _sourceDoc.GetElement(typeInfo.TypeId) as ElementType;
                            if (elementType != null && elementType.IsValidObject)
                            {
                                var currentName = elementType.Name;
                                if (!string.IsNullOrEmpty(currentName) && !currentName.Contains(OLD_SUFFIX))
                                {
                                    elementType.Name = currentName + OLD_SUFFIX;
                                    LogHelper.Information($"Renamed {category} type to: {elementType.Name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Warning($"Failed to rename type {typeInfo.TypeName}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Transaction error in rename types: {ex.Message}");
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }
                    throw;
                }
            }
        }

        #endregion

        #region STAGE 4: Copy Types

        private void CopyTypesFromTemplate(List<ElementTypeMappingEntry> mappingEntries, IProgress<string> progress)
        {
            var toCopy = mappingEntries
                .Where(e => e.Status == MappingStatus.Mapped && e.TargetType != null)
                .ToList();

            if (!toCopy.Any()) return;

            var elementIds = toCopy
                .Select(e => e.TargetType.TypeId)
                .ToList();

            using (var tx = new Transaction(_sourceDoc, "Copy element types from template"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    var copyOptions = new CopyPasteOptions();
                    copyOptions.SetDuplicateTypeNamesHandler(new DuplicateTypeNamesHandler());

                    var copiedIds = ElementTransformUtils.CopyElements(
                        _templateDoc,
                        elementIds,
                        _sourceDoc,
                        null,  // Используем null для безопасности
                        copyOptions);

                    for (int i = 0; i < toCopy.Count && i < copiedIds.Count; i++)
                    {
                        toCopy[i].CopiedTypeId = copiedIds.ElementAt(i);
                        var copiedType = _sourceDoc.GetElement(copiedIds.ElementAt(i)) as ElementType;
                        if (copiedType != null && copiedType.IsValidObject)
                        {
                            LogHelper.Information($"Copied element type: {copiedType.Name}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Failed to copy element types: {ex.Message}");
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }
                    throw;
                }
            }
        }

        #endregion

        #region STAGE 5: Switch Instances

        private SwitchResult SwitchElementInstances(List<ElementTypeMappingEntry> mappingEntries, BuiltInCategory category, IProgress<string> progress)
        {
            var result = new SwitchResult();

            var toSwitch = mappingEntries
                .Where(e => e.Status == MappingStatus.Mapped && e.CopiedTypeId != null)
                .ToList();

            if (!toSwitch.Any()) return result;

            var allElements = new FilteredElementCollector(_sourceDoc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToList();

            using (var tx = new Transaction(_sourceDoc, $"Switch {category} instances"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    foreach (var entry in toSwitch)
                    {
                        var oldTypeName = entry.SourceType.TypeName + OLD_SUFFIX;
                        var elementsToSwitch = allElements
                            .Where(e => e.IsValidObject && GetElementTypeName(e) == oldTypeName)
                            .ToList();

                        foreach (var element in elementsToSwitch)
                        {
                            try
                            {
                                if (element.IsValidObject)
                                {
                                    element.ChangeTypeId(entry.CopiedTypeId);
                                    result.SwitchedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add($"Failed to switch {category} instance: {ex.Message}");
                            }
                        }

                        if (elementsToSwitch.Any())
                        {
                            entry.Status = MappingStatus.Processed;
                            LogHelper.Information($"Switched {elementsToSwitch.Count} instances of type {oldTypeName}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Transaction error in switch instances: {ex.Message}");
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }
                    throw;
                }
            }

            return result;
        }

        private string GetElementTypeName(Element element)
        {
            try
            {
                if (!element.IsValidObject) return "Unknown";

                var param = element.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM);
                if (param?.HasValue == true)
                {
                    return param.AsValueString();
                }

                var typeId = element.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var type = _sourceDoc.GetElement(typeId);
                    if (type?.IsValidObject == true)
                    {
                        return type.Name;
                    }
                }

                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        #endregion

        #region STAGE 6: Delete Old Types

        private int DeleteOldTypes(List<ElementTypeMappingEntry> mappingEntries, BuiltInCategory category, IProgress<string> progress)
        {
            var toDelete = mappingEntries
                .Where(e => e.Status == MappingStatus.Processed)
                .Select(e => e.SourceType.TypeName + OLD_SUFFIX)
                .ToList();

            if (!toDelete.Any()) return 0;

            int deletedCount = 0;

            using (var tx = new Transaction(_sourceDoc, $"Delete old {category} types"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    // Сначала собираем информацию о типах для удаления
                    var typesToDelete = new List<(ElementId id, string name)>();

                    var oldTypes = new FilteredElementCollector(_sourceDoc)
                        .OfCategory(category)
                        .WhereElementIsElementType()
                        .Cast<ElementType>()
                        .Where(et => et.IsValidObject)
                        .ToList();

                    foreach (var oldType in oldTypes)
                    {
                        try
                        {
                            var typeName = oldType.Name;

                            if (!string.IsNullOrEmpty(typeName) && toDelete.Contains(typeName))
                            {
                                // Проверяем, что тип не используется
                                var usedByElements = new FilteredElementCollector(_sourceDoc)
                                    .OfCategory(category)
                                    .WhereElementIsNotElementType()
                                    .Any(e => e.IsValidObject && e.GetTypeId() == oldType.Id);

                                if (!usedByElements)
                                {
                                    typesToDelete.Add((oldType.Id, typeName));
                                }
                                else
                                {
                                    LogHelper.Warning($"Cannot delete {typeName} - still in use");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Warning($"Error checking type for deletion: {ex.Message}");
                        }
                    }

                    // Теперь удаляем по ID
                    foreach (var (typeId, typeName) in typesToDelete)
                    {
                        try
                        {
                            var element = _sourceDoc.GetElement(typeId);
                            if (element != null && element.IsValidObject)
                            {
                                _sourceDoc.Delete(typeId);
                                deletedCount++;
                                LogHelper.Information($"Deleted old {category} type: {typeName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Warning($"Failed to delete type {typeName}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Error in delete transaction: {ex.Message}");
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }
                }
            }

            return deletedCount;
        }

        #endregion

        #region STAGE 7: Process Unmapped

        private void ProcessUnmappedTypes(List<ElementTypeMappingEntry> mappingEntries, BuiltInCategory category, ElementTypeRebaseResult result)
        {
            try
            {
                var unmapped = mappingEntries.Where(e => e.Status == MappingStatus.Pending).ToList();

                foreach (var entry in unmapped)
                {
                    try
                    {
                        var isUsed = new FilteredElementCollector(_sourceDoc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType()
                            .Where(e => e.IsValidObject)
                            .Any(e => e.GetTypeId() == entry.SourceType.TypeId);

                        if (isUsed)
                        {
                            result.UnmappedTypesWithInstances.Add(entry.SourceType.TypeName);
                            LogHelper.Warning($"Unmapped {category} type '{entry.SourceType.TypeName}' has instances");
                        }
                        else
                        {
                            result.UnmappedTypesWithoutInstances.Add(entry.SourceType.TypeName);
                            LogHelper.Information($"Unmapped {category} type '{entry.SourceType.TypeName}' has no instances");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Error processing unmapped type {entry.SourceType.TypeName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error processing unmapped types: {ex.Message}");
            }
        }

        #endregion

        #region Helper Classes

        public class ElementTypeInfo
        {
            public ElementId TypeId { get; set; }
            public string TypeName { get; set; }
            public string FamilyName { get; set; }
            public BuiltInCategory Category { get; set; }
            public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
        }

        public class ElementTypeMappingEntry
        {
            public ElementTypeInfo SourceType { get; set; }
            public ElementTypeInfo TargetType { get; set; }
            public string TargetTypeName { get; set; }
            public ElementId CopiedTypeId { get; set; }
            public MappingStatus Status { get; set; }
            public MappingSource MappingSource { get; set; }
            public DateTime ProcessedAt { get; set; }
        }

        public enum MappingStatus
        {
            Pending,
            Mapped,
            Processed,
            Failed
        }

        public enum MappingSource
        {
            ExactMatch,
            AI,
            Manual,
            NotMapped
        }

        public class ElementTypeRebaseResult
        {
            public int TotalSourceTypes { get; set; }
            public int TotalTemplateTypes { get; set; }
            public int ExactMatches { get; set; }
            public int AiMapped { get; set; }
            public int SwitchedInstances { get; set; }
            public int DeletedTypes { get; set; }
            public List<string> UnmappedTypesWithInstances { get; set; } = new List<string>();
            public List<string> UnmappedTypesWithoutInstances { get; set; } = new List<string>();
            public List<string> Errors { get; set; } = new List<string>();
            public TimeSpan Duration { get; set; }
        }

        private class SwitchResult
        {
            public int SwitchedCount { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
        }

        private class DuplicateTypeNamesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                // Use types from source (don't create duplicates)
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        #endregion
    }
}