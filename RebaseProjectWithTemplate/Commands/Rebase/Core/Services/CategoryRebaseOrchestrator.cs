using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    /// <summary>
    /// Оркестратор для миграции семейств по категориям
    /// </summary>
    public class CategoryRebaseOrchestrator
    {
        private readonly IFamilyRepository _familyRepo;
        private readonly IAiService _aiService;

        private const string OLD_SUFFIX = "_REBASE_OLD";

        public CategoryRebaseOrchestrator(IFamilyRepository familyRepo, IAiService aiService)
        {
            _familyRepo = familyRepo;
            _aiService = aiService;
        }



        public async Task<MappingExecutionResult> RebaseAsync(
            Document document,
            BuiltInCategory category,
            IPromptStrategy strategy,
            IProgress<string> progress,
            bool ungroupInstances = true)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new MappingExecutionResult();
            var mappingData = new MappingData();

            LogHelper.Information($"[{category}] Registered global failure handler for rebase operation");

            try
            {
                LogHelper.Information($"[{category}] Starting rebase V3...");

                // ЭТАП 1: Сбор данных
                progress?.Report($"[{category}] Collecting family data...");
                CollectFamilyData(category, mappingData, result);

                if (mappingData.SourceFamilies.Count == 0 || mappingData.TemplateFamilies.Count == 0)
                {
                    LogHelper.Warning($"[{category}] No families to process");
                    return result;
                }

                // ЭТАП 2: Загрузка ВСЕХ семейств из шаблона
                progress?.Report($"[{category}] Loading all template families...");
                await LoadAllTemplateFamilies(category, mappingData, progress);

                // ЭТАП 3: Создание маппинга (exact + AI)
                progress?.Report($"[{category}] Creating family mapping...");
                await CreateCompleteMapping(mappingData, strategy, result);

                // ЭТАП 4: Переключение экземпляров
                progress?.Report($"[{category}] Switching instances...");
                await SwitchAllInstances(category, mappingData, progress, result, ungroupInstances);

                // ЭТАП 5: Финальная обработка (переименование и удаление)
                progress?.Report($"[{category}] Final processing...");
                await FinalProcessing(category, mappingData, progress, result);


            }
            catch (Exception ex)
            {
                result.Errors.Add($"Fatal error: {ex.Message}");
                LogHelper.Error($"[{category}] Fatal error in rebase: {ex}" );
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                LogHelper.Information($"[{category}] Rebase completed in {result.Duration.TotalSeconds:F2}s. " +
                    $"Processed: {result.TotalFamilies}, Switched: {result.SwitchedInstances}, " +
                    $"Renamed: {result.RenamedFamilies}, Deleted: {result.DeletedFamilies}, " +
                    $"Errors: {result.Errors.Count}");
            }

            return result;
        }

        #region ЭТАП 1: Сбор данных

        private void CollectFamilyData(BuiltInCategory category, MappingData mappingData, MappingExecutionResult result)
        {
            try
            {
                // Собираем данные из текущего проекта
                var allSourceFamilies = _familyRepo.CollectFamilyData(category, fromTemplate: false);

                // Фильтруем только семейства верхнего уровня (имеющие размещённые экземпляры)
                mappingData.SourceFamilies = FilterTopLevelFamilies(allSourceFamilies, category);

                // Собираем данные из шаблона
                mappingData.TemplateFamilies = _familyRepo.CollectFamilyData(category, fromTemplate: true);

                result.TotalFamilies = mappingData.SourceFamilies.Count;

                LogHelper.Information($"Collected {mappingData.SourceFamilies.Count} source families " +
                    $"(filtered from {allSourceFamilies.Count}) and {mappingData.TemplateFamilies.Count} template families");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error collecting family data: {ex.Message}");
                result.Errors.Add($"Data collection error: {ex.Message}");
            }
        }

        private List<FamilyData> FilterTopLevelFamilies(List<FamilyData> families, BuiltInCategory category)
        {
            var doc = _familyRepo.GetDocument();
            var topLevel = new List<FamilyData>();

            foreach (var family in families)
            {
                try
                {
                    // Проверяем наличие размещённых экземпляров
                    var hasInstances = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .Any(inst => inst.Symbol.Family.Name == family.FamilyName && inst.SuperComponent == null);

                    if (hasInstances)
                    {
                        topLevel.Add(family);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"Error checking family '{family.FamilyName}': {ex.Message}");
                    // В случае ошибки включаем семейство в обработку
                    topLevel.Add(family);
                }
            }

            return topLevel;
        }

        #endregion

        #region ЭТАП 2: Загрузка семейств из шаблона

        private async Task LoadAllTemplateFamilies(BuiltInCategory category, MappingData mappingData, IProgress<string> progress)
        {
            var total = mappingData.TemplateFamilies.Count;
            var current = 0;

            foreach (var templateFamily in mappingData.TemplateFamilies)
            {
                current++;
                progress?.Report($"Loading template {current}/{total}: {templateFamily.FamilyName}");

                try
                {
                    var loaded = _familyRepo.LoadSingleFamily(category, templateFamily.FamilyName);
                    mappingData.LoadedTemplateFamilies[templateFamily.FamilyName] = loaded;
                    LogHelper.Information($"Loaded: {templateFamily.FamilyName}");
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Failed to load '{templateFamily.FamilyName}': {ex.Message}");
                    // Продолжаем загрузку остальных
                }
            }

            LogHelper.Information($"Loaded {mappingData.LoadedTemplateFamilies.Count}/{total} template families");
        }

        #endregion

        #region ЭТАП 3: Создание полного маппинга

        private async Task CreateCompleteMapping(MappingData mappingData, IPromptStrategy strategy, MappingExecutionResult result)
        {
            var templateNames = mappingData.LoadedTemplateFamilies.Keys.ToHashSet();

            // Сначала находим exact matches
            foreach (var sourceFamily in mappingData.SourceFamilies)
            {
                var entry = new FamilyMappingEntry
                {
                    SourceFamilyName = sourceFamily.FamilyName,
                    SourceFamily = sourceFamily,
                    ProcessedAt = DateTime.Now
                };

                if (templateNames.Contains(sourceFamily.FamilyName))
                {
                    // Exact match
                    entry.TargetFamilyName = sourceFamily.FamilyName;
                    entry.LoadedFamily = mappingData.LoadedTemplateFamilies[sourceFamily.FamilyName];
                    entry.Status = MappingStatus.ExactMatch;
                    entry.Source = MappingSource.ExactMatch;
                    CreateExactMatchTypeMapping(entry);
                    result.ExactMatches++;
                }
                else
                {
                    // Нужен AI маппинг
                    entry.Status = MappingStatus.Pending;
                }

                mappingData.MappingEntries.Add(entry);
            }

            // Теперь обрабатываем через AI те, что не нашли exact match
            await ProcessAiMapping(mappingData, strategy, result);
        }

        private void CreateExactMatchTypeMapping(FamilyMappingEntry entry)
        {
            if (entry.SourceFamily?.Types == null || entry.LoadedFamily?.Types == null) return;

            var targetTypes = entry.LoadedFamily.Types.ToDictionary(t => t.TypeName);

            foreach (var sourceType in entry.SourceFamily.Types)
            {
                var mapping = new TypeMappingEntry
                {
                    SourceTypeName = sourceType.TypeName,
                    SourceTypeId = (int)sourceType.TypeId
                };

                if (targetTypes.TryGetValue(sourceType.TypeName, out var targetType))
                {
                    mapping.TargetTypeName = targetType.TypeName;
                    mapping.TargetTypeId = (int)targetType.TypeId;
                    mapping.Status = TypeMappingStatus.Mapped;
                }
                else
                {
                    mapping.Status = TypeMappingStatus.NotFound;
                }

                entry.TypeMappings.Add(mapping);
            }
        }

        private async Task ProcessAiMapping(MappingData mappingData, IPromptStrategy strategy, MappingExecutionResult result)
        {
            var pendingEntries = mappingData.MappingEntries.Where(e => e.Status == MappingStatus.Pending).ToList();
            if (!pendingEntries.Any()) return;

            try
            {
                var sourceFamilies = pendingEntries.Select(e => e.SourceFamily).ToList();
                var templateFamilies = mappingData.LoadedTemplateFamilies.Values.ToList();

                var promptData = new RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting.CategoryMappingPromptData
                {
                    OldFamilies = sourceFamilies,
                    NewFamilies = templateFamilies
                };

                var aiResults = await _aiService.GetMappingAsync<List<MappingResult>>(strategy, promptData);

                // Обрабатываем результаты AI
                foreach (var aiResult in aiResults)
                {
                    var entry = pendingEntries.FirstOrDefault(e => e.SourceFamilyName == aiResult.Old);
                    if (entry == null) continue;

                    if (!string.IsNullOrEmpty(aiResult.New) &&
                        aiResult.New != "No Match" &&
                        mappingData.LoadedTemplateFamilies.ContainsKey(aiResult.New))
                    {
                        entry.TargetFamilyName = aiResult.New;
                        entry.LoadedFamily = mappingData.LoadedTemplateFamilies[aiResult.New];
                        entry.Status = MappingStatus.AiMapped;
                        entry.Source = MappingSource.AI;
                        CreateAiTypeMapping(entry, aiResult);
                        result.AiMapped++;

                        LogHelper.Information($"AI mapped: {aiResult.Old} -> {aiResult.New}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"AI mapping failed: {ex.Message}");
                result.Errors.Add($"AI mapping error: {ex.Message}");
            }
        }

        private void CreateAiTypeMapping(FamilyMappingEntry entry, MappingResult aiResult)
        {
            if (entry.LoadedFamily?.Types == null) return;

            var targetTypes = entry.LoadedFamily.Types.ToDictionary(t => t.TypeName);

            foreach (var typeMatch in aiResult.TypeMatches)
            {
                if (typeMatch.NewType == "No Match") continue;

                var sourceType = entry.SourceFamily.Types.FirstOrDefault(t => t.TypeName == typeMatch.OldType);
                if (sourceType == null) continue;

                var mapping = new TypeMappingEntry
                {
                    SourceTypeName = typeMatch.OldType,
                    SourceTypeId = (int)sourceType.TypeId,
                    TargetTypeName = typeMatch.NewType
                };

                if (targetTypes.TryGetValue(typeMatch.NewType, out var targetType))
                {
                    mapping.TargetTypeId = (int)targetType.TypeId;
                    mapping.Status = TypeMappingStatus.Mapped;
                }
                else
                {
                    mapping.Status = TypeMappingStatus.NotFound;
                }

                entry.TypeMappings.Add(mapping);
            }
        }

        #endregion

        #region ЭТАП 4: Переключение экземпляров

        private async Task SwitchAllInstances(BuiltInCategory category, MappingData mappingData, IProgress<string> progress, MappingExecutionResult result, bool ungroupInstances = true)
        {
            var doc = _familyRepo.GetDocument();

            // Собираем все записи для переключения
            var entriesToSwitch = mappingData.MappingEntries
                .Where(e => (e.Status == MappingStatus.ExactMatch || e.Status == MappingStatus.AiMapped) &&
                            e.LoadedFamily != null)
                .ToList();

            if (!entriesToSwitch.Any())
            {
                LogHelper.Information("No families to switch");
                return;
            }

            // Сразу помечаем exact match как обработанные (Revit сделал это автоматически)
            foreach (var exactMatch in entriesToSwitch.Where(e => e.Status == MappingStatus.ExactMatch))
            {
                exactMatch.Status = MappingStatus.Processed;
                LogHelper.Information($"Exact match '{exactMatch.SourceFamilyName}' was automatically updated by Revit");
            }

            // Строим маппинг типов только для AI-mapped семейств
            var aiMappedEntries = entriesToSwitch.Where(e => e.Source == MappingSource.AI).ToList();
            if (!aiMappedEntries.Any())
            {
                LogHelper.Information("No AI-mapped families to switch");
                return;
            }

            var typeIdMap = new Dictionary<ElementId, ElementId>();
            foreach (var entry in aiMappedEntries)
            {
                foreach (var typeMapping in entry.TypeMappings.Where(tm => tm.Status == TypeMappingStatus.Mapped))
                {
                    typeIdMap[new ElementId(typeMapping.SourceTypeId)] = new ElementId(typeMapping.TargetTypeId);
                }
            }

            if (!typeIdMap.Any())
            {
                LogHelper.Warning("No type mappings available for switching");
                return;
            }

            try
            {
                // Собираем все экземпляры для переключения (только верхнего уровня)
                var allInstances = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(inst => inst.SuperComponent == null && typeIdMap.ContainsKey(inst.Symbol.Id))
                    .ToList();

                LogHelper.Information($"Found {allInstances.Count} instances to switch");

                if (!allInstances.Any()) return;

                // Разгруппировываем (если требуется)
                var groupsUngrouped = 0;
                if (ungroupInstances)
                {
                    groupsUngrouped = UngroupInstances(allInstances);
                    LogHelper.Information($"Ungrouped {groupsUngrouped} groups for category {category}");
                }
                else
                {
                    LogHelper.Information($"Skipping ungrouping for category {category} as requested");
                }

                // Переключаем по группам типов
                var instancesByType = allInstances.GroupBy(inst => inst.Symbol.Id);
                var totalGroups = instancesByType.Count();
                var current = 0;

                foreach (var typeGroup in instancesByType)
                {
                    current++;
                    var oldTypeId = typeGroup.Key;
                    var newTypeId = typeIdMap[oldTypeId];
                    var instances = typeGroup.ToList();

                    var oldSymbol = doc.GetElement(oldTypeId) as FamilySymbol;
                    if (oldSymbol == null) continue;

                    progress?.Report($"Switching {current}/{totalGroups}: {oldSymbol.Family.Name}:{oldSymbol.Name}");

                    if (SwitchInstanceGroup(instances, oldTypeId, newTypeId))
                    {
                        result.SwitchedInstances += instances.Count;

                        // Помечаем семейство как успешно обработанное
                        var entry = entriesToSwitch.FirstOrDefault(e => e.SourceFamilyName == oldSymbol.Family.Name);
                        if (entry != null)
                        {
                            entry.Status = MappingStatus.Processed;
                            mappingData.ProcessedFamilies.Add(oldSymbol.Family.Name);
                        }
                    }
                }

                LogHelper.Information($"Successfully switched {result.SwitchedInstances} instances");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error during instance switching: {ex.Message}");
                result.Errors.Add($"Instance switching error: {ex.Message}");
            }
        }

        private int UngroupInstances(List<FamilyInstance> instances)
        {
            var doc = _familyRepo.GetDocument();
            var groupIds = instances
                .Where(i => i.GroupId != ElementId.InvalidElementId)
                .Select(i => i.GroupId)
                .Distinct()
                .ToList();

            if (!groupIds.Any()) return 0;

            int ungrouped = 0;
            using (var tx = new Transaction(doc, "Ungroup for switching"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                foreach (var groupId in groupIds)
                {
                    try
                    {
                        var group = doc.GetElement(groupId) as Group;
                        if (group != null)
                        {
                            group.UngroupMembers();
                            ungrouped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to ungroup {groupId}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            LogHelper.Information($"Ungrouped {ungrouped} groups");
            return ungrouped;
        }

        private bool SwitchInstanceGroup(List<FamilyInstance> instances, ElementId oldTypeId, ElementId newTypeId)
        {
            var doc = _familyRepo.GetDocument();

            using (var tx = new Transaction(doc, "Switch instances"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    // Кэшируем параметры
                    var paramCache = new Dictionary<ElementId, Dictionary<string, object>>();
                    foreach (var inst in instances)
                    {
                        try
                        {
                            paramCache[inst.Id] = _familyRepo.CacheInstanceParametersByName(inst);
                        }
                        catch
                        {
                            // Игнорируем ошибки кэширования
                        }
                    }

                    // Массовое переключение типов
                    var instanceIds = instances.Select(i => i.Id).ToList();
                    Element.ChangeTypeId(doc, instanceIds, newTypeId);

                    // Восстанавливаем параметры
                    foreach (var instId in instanceIds)
                    {
                        try
                        {
                            var inst = doc.GetElement(instId) as FamilyInstance;
                            if (inst != null && paramCache.TryGetValue(instId, out var cached))
                            {
                                RestoreParameters(inst, cached);
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки восстановления
                        }
                    }

                    tx.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    LogHelper.Error($"Failed to switch type {oldTypeId}: {ex.Message}");
                    return false;
                }
            }
        }

        private void RestoreParameters(FamilyInstance instance, Dictionary<string, object> cached)
        {
            foreach (var kvp in cached)
            {
                try
                {
                    var param = instance.LookupParameter(kvp.Key);
                    if (param != null && !param.IsReadOnly)
                    {
                        switch (param.StorageType)
                        {
                            case StorageType.Double:
                                if (kvp.Value is double d) param.Set(d);
                                break;
                            case StorageType.Integer:
                                if (kvp.Value is int i) param.Set(i);
                                break;
                            case StorageType.String:
                                if (kvp.Value is string s) param.Set(s);
                                break;
                            case StorageType.ElementId:
                                if (kvp.Value is string vs) param.SetValueString(vs);
                                break;
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки параметров
                }
            }
        }

        #endregion

        #region ЭТАП 5: Финальная обработка

        private async Task FinalProcessing(BuiltInCategory category, MappingData mappingData, IProgress<string> progress, MappingExecutionResult result)
        {
            var doc = _familyRepo.GetDocument();

            // ВАЖНО: Понимаем, что произошло с семействами:
            // 1. Exact match - Revit автоматически заменил старые на новые, старые стали невалидными
            // 2. AI mapped + switched - старые семейства всё ещё существуют и их нужно удалить
            // 3. Pending (unmapped) - нужно либо переименовать (если есть экземпляры), либо удалить

            // Шаг 1: Собираем семейства для удаления (только AI-mapped, которые были успешно переключены)
            var familiesToDelete = new List<string>();

            foreach (var entry in mappingData.MappingEntries)
            {
                if (entry.Source == MappingSource.AI &&
                    entry.Status == MappingStatus.Processed &&
                    mappingData.ProcessedFamilies.Contains(entry.SourceFamilyName))
                {
                    familiesToDelete.Add(entry.SourceFamilyName);
                }
            }

            if (familiesToDelete.Any())
            {
                progress?.Report($"Deleting {familiesToDelete.Count} AI-mapped families after switching...");
                var deleted = _familyRepo.DeleteFamilies(category, familiesToDelete, progress);
                result.DeletedFamilies += deleted.Count;
                LogHelper.Information($"Deleted {deleted.Count} AI-mapped families");
            }

            // Шаг 2: Обрабатываем unmapped семейства
            var unmappedFamilies = mappingData.MappingEntries
                .Where(e => e.Status == MappingStatus.Pending)
                .ToList();

            if (unmappedFamilies.Any())
            {
                // Проверяем наличие экземпляров
                var toRename = new List<string>();
                var toDeleteUnmapped = new List<string>();

                foreach (var entry in unmappedFamilies)
                {
                    try
                    {
                        var hasInstances = new FilteredElementCollector(doc)
                            .OfCategory(category)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Any(inst => inst.Symbol?.Family?.Name == entry.SourceFamilyName);

                        if (hasInstances)
                        {
                            toRename.Add(entry.SourceFamilyName);
                            entry.Status = MappingStatus.ToRename;
                        }
                        else
                        {
                            toDeleteUnmapped.Add(entry.SourceFamilyName);
                            entry.Status = MappingStatus.ToDelete;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Error checking instances for '{entry.SourceFamilyName}': {ex.Message}");
                        // В случае ошибки лучше переименовать
                        toRename.Add(entry.SourceFamilyName);
                        entry.Status = MappingStatus.ToRename;
                    }
                }

                // Переименовываем
                if (toRename.Any())
                {
                    progress?.Report($"Renaming {toRename.Count} families with instances...");
                    _familyRepo.RenameFamilies(category, toRename, OLD_SUFFIX, progress);
                    result.RenamedFamilies = toRename.Count;
                }

                // Удаляем неиспользуемые
                if (toDeleteUnmapped.Any())
                {
                    progress?.Report($"Deleting {toDeleteUnmapped.Count} unused unmapped families...");
                    var deleted = _familyRepo.DeleteFamilies(category, toDeleteUnmapped, progress);
                    result.DeletedFamilies += deleted.Count;
                }
            }

            LogHelper.Information($"Final processing complete. Deleted: {result.DeletedFamilies}, Renamed: {result.RenamedFamilies}");
        }

        #endregion



        #region Вспомогательные классы

        private class MappingData
        {
            public List<FamilyData> SourceFamilies { get; set; } = new List<FamilyData>();
            public List<FamilyData> TemplateFamilies { get; set; } = new List<FamilyData>();
            public Dictionary<string, FamilyData> LoadedTemplateFamilies { get; set; } = new Dictionary<string, FamilyData>();
            public List<FamilyMappingEntry> MappingEntries { get; set; } = new List<FamilyMappingEntry>();
            public HashSet<string> ProcessedFamilies { get; set; } = new HashSet<string>();
        }

        #endregion
    }
}