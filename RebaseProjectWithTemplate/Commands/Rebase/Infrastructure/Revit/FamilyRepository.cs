
using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Revit
{
    // Helper class for storing group information
    internal class GroupInfo
    {
        public ElementId GroupId { get; set; }
        public GroupType GroupType { get; set; }
        public List<ElementId> MemberIds { get; set; } = new List<ElementId>();
    }
    public class FamilyRepository : IFamilyRepository
    {
        private readonly Document _doc;
        private readonly Document _tpl;

        public FamilyRepository(Document doc, Document tpl)
        {
            _doc = doc;
            _tpl = tpl;
        }

        public List<FamilyData> CollectFamilyData(BuiltInCategory category, bool fromTemplate = false)
        {
            var doc = fromTemplate ? _tpl : _doc;
            var list = new List<FamilyData>();
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>();

            foreach (var grp in symbols.GroupBy(s => s.Family.Name))
            {
                string famName = grp.Key;

                list.Add(new FamilyData
                {
                    FamilyName = famName,
                    FamilyId = grp.First().Family.Id.IntegerValue,
                    Types = grp.Select(t => new FamilyTypeData
                    {
                        TypeName = t.Name,
                        TypeId = t.Id.IntegerValue
                    }).ToList()
                });
            }

            return list;
        }

        public void RenameFamilies(BuiltInCategory category, IEnumerable<string> looseNames, string suffix, IProgress<string> progress = null)
        {
            var famsToRename = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamCmp())
                .Where(f => looseNames.Contains(f.Name))
                .ToList();

            if (!famsToRename.Any())
            {
                LogHelper.Information("No families to rename.");
                return;
            }

            var allFamilyNames = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(Family)).Select(f => f.Name));
            int renamedCount = 0;

            for (int i = 0; i < famsToRename.Count; i++)
            {
                var f = famsToRename[i];
                string originalName = f.Name;

                using (var tx = new Transaction(_doc, $"Rename family {originalName}"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();

                    string baseName = originalName + suffix;
                    string newName = baseName;
                    int j = 1;
                    while (allFamilyNames.Contains(newName))
                    {
                        newName = baseName + "_" + (j++);
                    }

                    try
                    {
                        f.Name = newName;
                        tx.Commit();

                        allFamilyNames.Remove(originalName); 
                        allFamilyNames.Add(newName);
                        renamedCount++;

                        LogHelper.Information($"Renamed family '{originalName}' to '{newName}'");
                        progress?.Report($"Renaming families: {i + 1}/{famsToRename.Count}");
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"Failed to rename family '{originalName}'. Error: {ex.Message}");
                        tx.RollBack();
                    }
                }
            }
            LogHelper.Information($"Renamed {renamedCount} families in total.");
        }

        public FamilyData LoadSingleFamily(BuiltInCategory category, string familyName)
        {
            try
            {
                var famSym = new FilteredElementCollector(_tpl)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Family.Name == familyName);
                if (famSym == null)
                {
                    throw new InvalidOperationException($"Family '{familyName}' not found in template.");
                }

                // Открываем семейственный документ ВНЕ транзакции
                var famDoc = _tpl.EditFamily(famSym.Family);

                // Загружаем напрямую из семейственного документа ВНЕ транзакции
                Family loadedFamily = famDoc.LoadFamily(_doc, new OverwriteOpts());

                famDoc.Close(false); // Закрываем без сохранения

                if (loadedFamily == null)
                {
                    throw new InvalidOperationException($"Failed to load family '{familyName}' - LoadFamily returned null.");
                }

                // Получаем типы напрямую из загруженного семейства
                var symbolIds = loadedFamily.GetFamilySymbolIds();
                var familyData = new FamilyData
                {
                    FamilyName = loadedFamily.Name,
                    FamilyId = loadedFamily.Id.IntegerValue,
                    Types = symbolIds.Select(id =>
                    {
                        var symbol = _doc.GetElement(id) as FamilySymbol;
                        return new FamilyTypeData
                        {
                            TypeName = symbol.Name,
                            TypeId = symbol.Id.IntegerValue
                        };
                    }).ToList()
                };
                LogHelper.Information($"Successfully loaded family '{familyName}' with {familyData.Types.Count} types.");
                return familyData;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error loading family '{familyName}': {ex.Message}");
                throw;
            }
        }

        public bool HasInstances(BuiltInCategory category, string familyName)
    {
        try
        {
            var instances = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(inst => inst.Symbol.Family.Name == familyName)
                .Any();

            return instances;
        }
        catch (Exception ex)
        {
            LogHelper.Warning($"Failed to check instances for family {familyName}: {ex.Message}");
            return false;
        }
    }

    public Document GetDocument()
    {
        return _doc;
    }

        public SwitchInstancesResult SwitchInstancesAndRemoveOldEnhanced(BuiltInCategory category, Dictionary<ElementId, ElementId> idMap, List<MappingResult> mappedFamilies, IProgress<string> progress = null)
        {
            var result = new SwitchInstancesResult();

            if (idMap.Count == 0) return result;

            // Collect all instances that need to be switched
            var allInstances = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(inst => idMap.ContainsKey(inst.Symbol.Id))
                .ToList();

            if (allInstances.Count == 0) return result;

            // Collect all groups that contain instances we need to switch
            var groupsToUngroup = allInstances
                .Where(inst => inst.GroupId != ElementId.InvalidElementId)
                .Select(inst => inst.GroupId)
                .Distinct()
                .Select(groupId => _doc.GetElement(groupId) as Group)
                .Where(group => group != null)
                .ToList();

            // Store group information for re-grouping later
            var groupInfos = new List<GroupInfo>();
            foreach (var group in groupsToUngroup)
            {
                var groupInfo = new GroupInfo
                {
                    GroupId = group.Id,
                    GroupType = group.GroupType,
                    MemberIds = group.GetMemberIds().ToList()
                };
                groupInfos.Add(groupInfo);
            }

            // Ungroup all groups that contain our instances
            if (groupsToUngroup.Any())
            {
                progress?.Report($"Ungrouping {groupsToUngroup.Count} groups before switching instances...");
                using (var tx = new Transaction(_doc, "Ungroup elements before switching instances"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();
                    try
                    {
                        foreach (var group in groupsToUngroup)
                        {
                            try
                            {
                                group.UngroupMembers();
                                LogHelper.Information($"Ungrouped group: {group.Id}");

                                // Validate that group members still exist after ungrouping
                                var groupInfo = groupInfos.FirstOrDefault(gi => gi.GroupId == group.Id);
                                if (groupInfo != null)
                                {
                                    var existingMemberIds = groupInfo.MemberIds
                                        .Select(id => _doc.GetElement(id))
                                        .Where(el => el != null && el.IsValidObject)
                                        .Select(el => el.Id)
                                        .ToList();

                                    if (!existingMemberIds.Any())
                                    {
                                        LogHelper.Warning($"All members of group {groupInfo.GroupId} became invalid after ungrouping");
                                    }
                                    else if (existingMemberIds.Count < groupInfo.MemberIds.Count)
                                    {
                                        LogHelper.Warning($"Group {groupInfo.GroupId}: {groupInfo.MemberIds.Count - existingMemberIds.Count} members became invalid after ungrouping");
                                    }

                                    // Update the group info with valid members only
                                    groupInfo.MemberIds = existingMemberIds;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Warning($"Failed to ungroup group {group.Id}: {ex.Message}");
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Failed to ungroup elements: {ex.Message}";
                        result.Errors.Add(errorMsg);
                        LogHelper.Error(errorMsg);
                    }
                }
            }

            // Group instances by old type for mass replacement
            var instancesByOldType = allInstances.GroupBy(inst => inst.Symbol.Id).ToList();
            int totalGroups = instancesByOldType.Count;
            int currentGroup = 0;
            var processedFamilies = new HashSet<string>();
            var failedFamilies = new HashSet<string>();

            foreach (var group in instancesByOldType)
            {
                var oldTypeId = group.Key;
                var newTypeId = idMap[oldTypeId];
                var instancesOfType = group.ToList();
                currentGroup++;

                // Get type names for progress reporting
                var oldSymbol = _doc.GetElement(oldTypeId) as FamilySymbol;
                var newSymbol = _doc.GetElement(newTypeId) as FamilySymbol;

                if (oldSymbol == null || newSymbol == null) continue;

                string oldFamilyName = oldSymbol.Family.Name;
                string oldTypeName = oldSymbol.Name;
                string newFamilyName = newSymbol.Family.Name;
                string newTypeName = newSymbol.Name;

                progress?.Report($"Mass switching {currentGroup}/{totalGroups}: '{oldFamilyName}:{oldTypeName}' → '{newFamilyName}:{newTypeName}' ({instancesOfType.Count} instances)");

                using (var tx = new Transaction(_doc, $"Switch instances from '{oldFamilyName}:{oldTypeName}' to '{newFamilyName}:{newTypeName}'"))
                {
                    try
                    {
                        CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                        tx.Start();

                        // Cache parameters for all instances of this type
                        var parameterCache = new Dictionary<ElementId, Dictionary<string, object>>();
                        foreach (var inst in instancesOfType)
                        {
                            parameterCache[inst.Id] = CacheInstanceParametersByName(inst);
                        }

                        // Enhanced validation of instances before mass replace
                        var validInstanceIds = new List<ElementId>();
                        foreach (var inst in instancesOfType)
                        {
                            var element = _doc.GetElement(inst.Id);
                            if (element != null && element.IsValidObject)
                            {
                                validInstanceIds.Add(inst.Id);
                            }
                            else
                            {
                                LogHelper.Warning($"Instance {inst.Id} became invalid after ungrouping or preprocessing - skipping");
                            }
                        }

                        if (!validInstanceIds.Any())
                        {
                            LogHelper.Warning($"All instances of type '{oldTypeName}' became invalid before replacement - skipping type group");
                            tx.Commit();
                            continue;
                        }

                        LogHelper.Information($"Validated {validInstanceIds.Count}/{instancesOfType.Count} instances for type '{oldTypeName}'");

                        // Enhanced validation of target type
                        var newTypeElement = _doc.GetElement(newTypeId) as FamilySymbol;
                        if (newTypeElement == null || !newTypeElement.IsValidObject)
                        {
                            LogHelper.Error($"New family type '{newTypeId}' (target: '{newTypeName}') is invalid or was deleted - skipping type group");
                            tx.Commit();
                            continue;
                        }

                        // Note: Family symbols are automatically activated when used in Element.ChangeTypeId()
                        // No manual activation needed

                        LogHelper.Information($"Switching {validInstanceIds.Count}/{instancesOfType.Count} valid instances");

                        // Mass replace types
                        Element.ChangeTypeId(_doc, validInstanceIds, newTypeId);

                        // Validate that type change was successful and restore parameters
                        var successfullyChangedCount = 0;
                        foreach (var inst in instancesOfType)
                        {
                            if (validInstanceIds.Contains(inst.Id))
                            {
                                var currentElement = _doc.GetElement(inst.Id) as FamilyInstance;
                                if (currentElement != null && currentElement.Symbol.Id == newTypeId)
                                {
                                    // Type change was successful, restore parameters
                                    if (parameterCache.TryGetValue(inst.Id, out var cachedParameters))
                                    {
                                        RestoreInstanceParametersByName(currentElement, cachedParameters);
                                    }
                                    successfullyChangedCount++;
                                }
                                else
                                {
                                    LogHelper.Warning($"Instance {inst.Id} type change verification failed - type may not have changed correctly");
                                }
                            }
                        }

                        LogHelper.Information($"Successfully changed and verified {successfullyChangedCount}/{validInstanceIds.Count} instances");

                        tx.Commit();
                        result.SwitchedInstancesCount += successfullyChangedCount;
                        processedFamilies.Add(oldFamilyName);
                        LogHelper.Information($"Mass switched {successfullyChangedCount} instances from '{oldFamilyName}:{oldTypeName}' to '{newFamilyName}:{newTypeName}'");
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            tx.RollBack();
                        }
                        catch (Exception rollbackEx)
                        {
                            LogHelper.Warning($"Failed to rollback transaction: {rollbackEx.Message}");
                        }

                        failedFamilies.Add(oldFamilyName);
                        var errorMsg = $"Failed to switch instances from '{oldFamilyName}:{oldTypeName}' to '{newFamilyName}:{newTypeName}': {ex.Message}";
                        result.Errors.Add(errorMsg);
                        LogHelper.Error(errorMsg);
                    }
                }
            }

            result.SuccessfullyProcessedFamilies = processedFamilies.ToList();
            result.FailedFamilies = failedFamilies.ToList();

            // Mark families for deletion but don't delete them yet
            // Deletion will be done after all switching is complete
            var successfullyMappedFamilies = mappedFamilies.Where(m => processedFamilies.Contains(m.Old)).ToList();
            result.FamiliesMarkedForDeletion = successfullyMappedFamilies.Select(m => m.Old).ToList();
            LogHelper.Information($"Marked {successfullyMappedFamilies.Count} families for deletion after switching completion");

            // Re-group elements that were ungrouped
            if (groupInfos.Any())
            {
                progress?.Report($"Re-grouping {groupInfos.Count} groups after switching instances...");
                using (var tx = new Transaction(_doc, "Re-group elements after switching instances"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();
                    try
                    {
                        foreach (var groupInfo in groupInfos)
                        {
                            try
                            {
                                // Enhanced validation: check both existence and validity of elements
                                var existingMemberIds = groupInfo.MemberIds
                                    .Select(id => _doc.GetElement(id))
                                    .Where(el => el != null && el.IsValidObject)
                                    .Select(el => el.Id)
                                    .ToList();

                                if (existingMemberIds.Count > 1) // Need at least 2 elements to create a group
                                {
                                    var newGroup = _doc.Create.NewGroup(existingMemberIds);
                                    // Note: Revit automatically calculates the group location based on member elements
                                    // No need to manually set the location
                                    LogHelper.Information($"Re-grouped {existingMemberIds.Count}/{groupInfo.MemberIds.Count} valid elements into new group: {newGroup?.Id}");

                                    if (existingMemberIds.Count < groupInfo.MemberIds.Count)
                                    {
                                        LogHelper.Warning($"Group {groupInfo.GroupId}: {groupInfo.MemberIds.Count - existingMemberIds.Count} elements were invalid and excluded from re-grouping");
                                    }
                                }
                                else if (existingMemberIds.Count == 1)
                                {
                                    LogHelper.Warning($"Cannot re-group - only 1 valid element remaining from original group {groupInfo.GroupId} (had {groupInfo.MemberIds.Count} originally)");
                                }
                                else
                                {
                                    LogHelper.Warning($"Cannot re-group - no valid elements remaining from original group {groupInfo.GroupId} (had {groupInfo.MemberIds.Count} originally)");
                                }
                            }
                            catch (Exception ex)
                            {
                                var errorMsg = $"Failed to re-group elements from group {groupInfo.GroupId}: {ex.Message}";
                                result.Errors.Add(errorMsg);
                                LogHelper.Warning(errorMsg);
                            }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = $"Failed to re-group elements: {ex.Message}";
                        result.Errors.Add(errorMsg);
                        LogHelper.Error(errorMsg);
                    }
                }
            }

            return result;
        }

        public List<string> DeleteFamilies(BuiltInCategory category, List<string> familyNames, IProgress<string> progress = null)
        {
            var deletedFamilies = new List<string>();
            if (!familyNames.Any()) return deletedFamilies;

            // Стратегия: делаем несколько проходов удаления
            // Каждый проход пытается удалить семейства, которые не удалось удалить в предыдущем
            var remainingFamilies = new HashSet<string>(familyNames);
            var maxPasses = 3; // Максимум 3 прохода для избежания бесконечного цикла
            var pass = 0;

            while (remainingFamilies.Any() && pass < maxPasses)
            {
                pass++;
                var familiesDeletedInThisPass = new List<string>();
                var familiesToTry = remainingFamilies.ToList();

                LogHelper.Information($"Deletion pass {pass} - trying to delete {familiesToTry.Count} families");

                foreach (var familyName in familiesToTry)
                {
                    if (pass > 1)
                    {
                        progress?.Report($"Pass {pass}: Deleting family {familyName}");
                    }
                    else
                    {
                        progress?.Report($"Deleting family {familyName}");
                    }

                    using (var tx = new Transaction(_doc, $"Delete family: {familyName}"))
                    {
                        CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                        tx.Start();

                        try
                        {
                            // Ищем семейство
                            var family = new FilteredElementCollector(_doc)
                                .OfClass(typeof(Family))
                                .Cast<Family>()
                                .FirstOrDefault(f => f.Name == familyName);

                            if (family == null)
                            {
                                // Семейство не найдено - возможно, уже удалено
                                remainingFamilies.Remove(familyName);
                                tx.RollBack();
                                continue;
                            }

                            // Проверяем использование
                            var symbolIds = family.GetFamilySymbolIds();
                            bool hasInstances = false;

                            foreach (var symbolId in symbolIds)
                            {
                                var instances = new FilteredElementCollector(_doc)
                                    .OfCategory(category)
                                    .WhereElementIsNotElementType()
                                    .Cast<FamilyInstance>()
                                    .Any(inst => inst.Symbol.Id == symbolId);

                                if (instances)
                                {
                                    hasInstances = true;
                                    break;
                                }
                            }

                            if (hasInstances)
                            {
                                LogHelper.Warning($"Family '{familyName}' has instances, removing from deletion list");
                                remainingFamilies.Remove(familyName);
                                tx.RollBack();
                                continue;
                            }

                            // Пытаемся удалить
                            _doc.Delete(family.Id);
                            tx.Commit();

                            familiesDeletedInThisPass.Add(familyName);
                            remainingFamilies.Remove(familyName);
                            LogHelper.Information($"Deleted family: {familyName}");
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException ex) when (ex.Message.Contains("can't be deleted"))
                        {
                            // Это семейство используется другим семейством - попробуем в следующем проходе
                            tx.RollBack();
                            LogHelper.Debug($"Family '{familyName}' might be nested, will retry in next pass");
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            LogHelper.Warning($"Failed to delete family '{familyName}': {ex.Message}");
                            // Не удаляем из remainingFamilies - попробуем в следующем проходе
                        }
                    }
                }

                deletedFamilies.AddRange(familiesDeletedInThisPass);

                if (!familiesDeletedInThisPass.Any())
                {
                    // Если в этом проходе ничего не удалили, прерываем цикл
                    break;
                }
            }

            if (remainingFamilies.Any())
            {
                LogHelper.Warning($"Could not delete {remainingFamilies.Count} families: {string.Join(", ", remainingFamilies)}");
            }

            LogHelper.Information($"Deleted {deletedFamilies.Count}/{familyNames.Count} families in {pass} passes");
            return deletedFamilies;
        }

        public int DeleteSpecificUnusedFamilies(BuiltInCategory category, List<string> originalFamilyNames, IProgress<string> progress = null)
        {
            try
            {
                var namesToCheck = originalFamilyNames.ToHashSet();

                // Безопасно собираем используемые типы
                var usedTypeIds = new HashSet<ElementId>();

                try
                {
                    var instances = new FilteredElementCollector(_doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .ToList();

                    foreach (var inst in instances)
                    {
                        try
                        {
                            if (inst.IsValidObject && inst.Symbol != null && inst.Symbol.IsValidObject)
                            {
                                usedTypeIds.Add(inst.Symbol.Id);
                            }
                        }
                        catch
                        {
                            // Игнорируем невалидные экземпляры
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"Error collecting used types: {ex.Message}");
                }

                // Собираем информацию о неиспользуемых семействах с кэшированием имён
                var unusedFamilyInfo = new List<(Family Family, string Name)>();

                try
                {
                    var allSymbols = new FilteredElementCollector(_doc)
                        .OfCategory(category)
                        .WhereElementIsElementType()
                        .Cast<FamilySymbol>()
                        .ToList();

                    var familyGroups = new Dictionary<ElementId, List<FamilySymbol>>();
                    var familyNames = new Dictionary<ElementId, string>();

                    // Группируем символы по семействам и кэшируем имена
                    foreach (var symbol in allSymbols)
                    {
                        try
                        {
                            if (symbol.IsValidObject && symbol.Family != null && symbol.Family.IsValidObject)
                            {
                                var familyId = symbol.Family.Id;

                                // Кэшируем имя семейства
                                if (!familyNames.ContainsKey(familyId))
                                {
                                    try
                                    {
                                        string familyName = symbol.Family.Name;
                                        if (namesToCheck.Contains(familyName))
                                        {
                                            familyNames[familyId] = familyName;
                                        }
                                    }
                                    catch
                                    {
                                        continue;
                                    }
                                }

                                // Добавляем символ в группу
                                if (familyNames.ContainsKey(familyId))
                                {
                                    if (!familyGroups.ContainsKey(familyId))
                                    {
                                        familyGroups[familyId] = new List<FamilySymbol>();
                                    }
                                    familyGroups[familyId].Add(symbol);
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем проблемные символы
                        }
                    }

                    // Проверяем использование
                    foreach (var kvp in familyGroups)
                    {
                        var familyId = kvp.Key;
                        var symbols = kvp.Value;

                        // Проверяем, используется ли хотя бы один тип
                        bool hasUsedTypes = symbols.Any(s => usedTypeIds.Contains(s.Id));

                        if (!hasUsedTypes && familyNames.TryGetValue(familyId, out string familyName))
                        {
                            try
                            {
                                var family = _doc.GetElement(familyId) as Family;
                                if (family != null)
                                {
                                    unusedFamilyInfo.Add((family, familyName));
                                }
                            }
                            catch
                            {
                                // Игнорируем невалидные семейства
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Error collecting unused families: {ex.Message}");
                    return 0;
                }

                if (!unusedFamilyInfo.Any())
                {
                    LogHelper.Information("No unused families found for deletion");
                    return 0;
                }

                // Сортируем для правильного порядка удаления
                var sortedFamilies = SortFamiliesForDeletionCached(unusedFamilyInfo, category);

                progress?.Report($"Deleting {sortedFamilies.Count} unused families...");

                using (var tx = new Transaction(_doc, "Delete unused families"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();

                    int deleted = 0;
                    foreach (var (family, cachedName) in sortedFamilies)
                    {
                        try
                        {
                            // Пытаемся удалить по ID
                            var element = _doc.GetElement(family.Id);
                            if (element != null)
                            {
                                _doc.Delete(family.Id);
                                deleted++;
                                LogHelper.Information($"Deleted unused family: {cachedName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Warning($"Failed to delete unused family '{cachedName}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                    return deleted;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error in DeleteSpecificUnusedFamilies: {ex.Message}");
                return 0;
            }
        }

        public Dictionary<string, object> CacheInstanceParametersByName(FamilyInstance inst)
        {
            var snapshot = new Dictionary<string, object>();

            foreach (Parameter p in inst.ParametersMap)
            {
                if ((p.Id.IntegerValue > 0 || p.IsShared) && !p.IsReadOnly && p.HasValue)
                {
                    snapshot[p.Definition.Name] = GetParameterValue(p);
                }
            }

            return snapshot;
        }

        private void RestoreInstanceParametersByName(FamilyInstance inst, Dictionary<string, object> cachedParameters)
        {
            foreach (var kvp in cachedParameters)
            {
                var paramName = kvp.Key;
                var val = kvp.Value;

                var p = inst.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly)
                {
                    try
                    {
                        SetParameterValue(p, val);
                    }
                    catch
                    {
                        // Ignore parameter restore errors
                    }
                }
            }
        }

        private static object GetParameterValue(Parameter p)
        {
            if (!p.HasValue) return null;

            switch (p.StorageType)
            {
                case StorageType.Double: return p.AsDouble();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.String: return p.AsString();
                case StorageType.ElementId: return p.AsValueString(); // Use AsValueString for ElementId
                default: return null;
            }
        }

        private static void SetParameterValue(Parameter p, object val)
        {
            if (val == null) return;

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        if (val is double d) p.Set(d);
                        break;
                    case StorageType.Integer:
                        if (val is int i) p.Set(i);
                        break;
                    case StorageType.String:
                        if (val is string s) p.Set(s);
                        break;
                    case StorageType.ElementId:
                        if (val is string st) p.SetValueString(st); // Use SetValueString for ElementId
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Failed to set parameter {p.Definition.Name}: {ex.Message}");
            }
        }



        private class OverwriteOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool inUse, out bool overwrite) { overwrite = true; return true; }
            public bool OnSharedFamilyFound(Family shared, bool inUse, out FamilySource src, out bool ov) { src = FamilySource.Family; ov = true; return true; }
        }

        // Новый метод сортировки с кэшированными именами
        private List<(Family Family, string Name)> SortFamiliesForDeletionCached(
            List<(Family Family, string Name)> familyInfoList,
            BuiltInCategory category)
        {
            // Создаём граф зависимостей используя кэшированные имена
            var dependencies = new Dictionary<string, HashSet<string>>();
            var familyMap = familyInfoList.ToDictionary(f => f.Name, f => f);

            foreach (var (family, name) in familyInfoList)
            {
                dependencies[name] = new HashSet<string>();

                try
                {
                    // Безопасно получаем ID типов
                    var symbolIds = new List<ElementId>();
                    try
                    {
                        symbolIds = family.GetFamilySymbolIds().ToList();
                    }
                    catch
                    {
                        // Если не можем получить типы, пропускаем анализ зависимостей
                        continue;
                    }

                    foreach (var symbolId in symbolIds)
                    {
                        try
                        {
                            var symbol = _doc.GetElement(symbolId) as FamilySymbol;
                            if (symbol == null || !symbol.IsValidObject) continue;

                            // Получаем все вложенные семейства через параметры типа FamilyType
                            foreach (Parameter param in symbol.Parameters)
                            {
                                try
                                {
                                    if (param.Definition.ParameterType == ParameterType.FamilyType)
                                    {
                                        var nestedId = param.AsElementId();
                                        if (nestedId == null || nestedId == ElementId.InvalidElementId) continue;

                                        var nestedSymbol = _doc.GetElement(nestedId) as FamilySymbol;
                                        if (nestedSymbol?.Family != null && nestedSymbol.Family.IsValidObject)
                                        {
                                            string nestedName = nestedSymbol.Family.Name;
                                            if (familyMap.ContainsKey(nestedName))
                                            {
                                                dependencies[name].Add(nestedName);
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // Игнорируем ошибки параметров
                                }
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки символов
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"Error analyzing dependencies for family '{name}': {ex.Message}");
                }
            }

            // Топологическая сортировка
            var sorted = new List<(Family Family, string Name)>();
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();

            void Visit(string familyName)
            {
                if (visited.Contains(familyName)) return;
                if (visiting.Contains(familyName))
                {
                    LogHelper.Warning($"Circular dependency detected for family '{familyName}'");
                    return;
                }

                visiting.Add(familyName);

                if (dependencies.ContainsKey(familyName))
                {
                    foreach (var dep in dependencies[familyName])
                    {
                        Visit(dep);
                    }
                }

                visiting.Remove(familyName);
                visited.Add(familyName);

                if (familyMap.ContainsKey(familyName))
                {
                    sorted.Add(familyMap[familyName]);
                }
            }

            foreach (var (family, name) in familyInfoList)
            {
                Visit(name);
            }

            // Возвращаем в обратном порядке (сначала вложенные, потом родительские)
            sorted.Reverse();
            return sorted;
        }

        internal class FamCmp : IEqualityComparer<Family> { public bool Equals(Family a, Family b) => a?.Id == b?.Id; public int GetHashCode(Family f) => f.Id.IntegerValue; }
        internal class FamilyComparer : IEqualityComparer<Family> { public bool Equals(Family a, Family b) => a?.Id == b?.Id; public int GetHashCode(Family f) => f.Id.IntegerValue; }
        internal class IdCmp : IEqualityComparer<ElementId> { public bool Equals(ElementId a, ElementId b) => a.IntegerValue == b.IntegerValue; public int GetHashCode(ElementId id) => id.IntegerValue; }
        internal class DestTypesHandler : IDuplicateTypeNamesHandler { public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes; }
    }
}
