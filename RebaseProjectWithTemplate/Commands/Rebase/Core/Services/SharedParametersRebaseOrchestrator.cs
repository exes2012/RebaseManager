using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class SharedParametersRebaseOrchestrator
    {
        private readonly Document _sourceDoc;
        private readonly Document _templateDoc;

        public SharedParametersRebaseOrchestrator(Document sourceDoc, Document templateDoc)
        {
            _sourceDoc = sourceDoc;
            _templateDoc = templateDoc;
        }

        public SharedParametersRebaseResult Rebase(IProgress<string> progress = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SharedParametersRebaseResult();

            try
            {
                LogHelper.Information("Starting shared parameters rebase...");

                progress?.Report("Collecting project shared parameters...");

                // ВАЖНО: Собираем только параметры ПРОЕКТА, а не все shared параметры
                var sourceProjectParams = CollectProjectSharedParameters(_sourceDoc);
                var templateProjectParams = CollectProjectSharedParameters(_templateDoc);

                result.SourceParametersCount = sourceProjectParams.Count;
                result.TemplateParametersCount = templateProjectParams.Count;

                LogHelper.Information($"Found {sourceProjectParams.Count} project parameters in source, {templateProjectParams.Count} in template");

                progress?.Report("Analyzing parameter differences...");
                var analysis = AnalyzeParameterDifferences(sourceProjectParams, templateProjectParams);

                // Удаляем только те параметры проекта, которых нет в шаблоне
                if (analysis.ToDelete.Any())
                {
                    progress?.Report($"Removing {analysis.ToDelete.Count} obsolete project parameters...");
                    result.DeletedParameters = DeleteObsoleteProjectParameters(analysis.ToDelete);
                }

                // Добавляем новые параметры из шаблона
                if (analysis.ToAdd.Any())
                {
                    progress?.Report($"Adding {analysis.ToAdd.Count} new parameters...");
                    result.AddedParameters = AddNewParameters(analysis.ToAdd);
                }

                // Обновляем существующие параметры (categories, instance/type)
                if (analysis.ToUpdate.Any())
                {
                    progress?.Report($"Updating {analysis.ToUpdate.Count} existing parameters...");
                    result.UpdatedParameters = UpdateParameterBindings(analysis.ToUpdate);
                }

                // Копируем значения параметров ProjectInfo
                progress?.Report("Copying project parameter values...");
                CopyProjectParameterValues();

                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                result.Success = true;

                LogHelper.Information($"Shared parameters rebase completed in {result.Duration.TotalSeconds:F2}s. " +
                    $"Added: {result.AddedParameters}, Updated: {result.UpdatedParameters}, Deleted: {result.DeletedParameters}");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LogHelper.Error($"Fatal error in shared parameters rebase: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Собирает только те shared параметры, которые относятся к проекту
        /// (имеют binding в ParameterBindings или используются в ProjectInfo)
        /// </summary>
        private Dictionary<Guid, ProjectSharedParameterInfo> CollectProjectSharedParameters(Document doc)
        {
            var parameters = new Dictionary<Guid, ProjectSharedParameterInfo>();
            var processedGuids = new HashSet<Guid>();

            // 1. Собираем параметры из ParameterBindings (основные параметры проекта)
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key as InternalDefinition;
                if (definition == null) continue;

                // Проверяем, является ли это shared параметром
                var sharedParamElement = GetSharedParameterElement(doc, definition);
                if (sharedParamElement == null)
                {
                    LogHelper.Debug($"Parameter '{definition.Name}' is not a shared parameter, skipping");
                    continue;
                }

                var binding = bindingMap.get_Item(definition);
                var categories = ExtractCategoriesFromBinding(binding);

                var info = new ProjectSharedParameterInfo
                {
                    Guid = sharedParamElement.GuidValue,
                    Name = definition.Name,
                    ParameterGroup = definition.ParameterGroup,
                    IsInstance = binding is InstanceBinding,
                    Categories = categories.Select(c => c.Id.IntegerValue).ToList(),
                    Definition = definition,
                    Element = sharedParamElement,
                    HasBinding = true,
                    BindingType = binding is InstanceBinding ? BindingType.Instance : BindingType.Type
                };

                parameters[info.Guid] = info;
                processedGuids.Add(info.Guid);

                LogHelper.Debug($"Found project parameter: {info.Name} ({info.Guid})");
            }

            // 2. Собираем параметры из ProjectInfo
            CollectProjectInfoParameters(doc, parameters, processedGuids);

            // 3. НЕ собираем orphaned параметры - они могут относиться к семействам!

            return parameters;
        }

        /// <summary>
        /// Находит SharedParameterElement по InternalDefinition
        /// </summary>
        private SharedParameterElement GetSharedParameterElement(Document doc, InternalDefinition definition)
        {
            // Сначала пробуем получить по Id параметра
            if (definition.Id != ElementId.InvalidElementId)
            {
                var elem = doc.GetElement(definition.Id) as SharedParameterElement;
                if (elem != null) return elem;
            }

            // Если не нашли, ищем по имени (менее надежный способ)
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .FirstOrDefault(sp =>
                    sp.GetDefinition() != null &&
                    sp.GetDefinition().Name == definition.Name);
        }

        /// <summary>
        /// Извлекает категории из binding
        /// </summary>
        private List<Category> ExtractCategoriesFromBinding(Binding binding)
        {
            var categories = new List<Category>();

            if (binding is InstanceBinding instanceBinding)
            {
                categories = instanceBinding.Categories.Cast<Category>().ToList();
            }
            else if (binding is TypeBinding typeBinding)
            {
                categories = typeBinding.Categories.Cast<Category>().ToList();
            }

            return categories;
        }

        /// <summary>
        /// Собирает параметры из ProjectInfo
        /// </summary>
        private void CollectProjectInfoParameters(Document doc, Dictionary<Guid, ProjectSharedParameterInfo> parameters, HashSet<Guid> processedGuids)
        {
            try
            {
                var projectInfo = doc.ProjectInformation;
                if (projectInfo == null) return;

                foreach (Parameter param in projectInfo.Parameters)
                {
                    if (!param.IsShared) continue;

                    var sharedParamElem = doc.GetElement(param.Id) as SharedParameterElement;
                    if (sharedParamElem == null) continue;

                    // Если параметр уже обработан через bindings
                    if (processedGuids.Contains(sharedParamElem.GuidValue))
                    {
                        parameters[sharedParamElem.GuidValue].IsProjectInfo = true;
                    }
                    else
                    {
                        // Это параметр только в ProjectInfo
                        var info = new ProjectSharedParameterInfo
                        {
                            Guid = sharedParamElem.GuidValue,
                            Name = param.Definition.Name,
                            ParameterGroup = param.Definition.ParameterGroup,
                            IsProjectInfo = true,
                            Element = sharedParamElem,
                            HasBinding = false,
                            BindingType = BindingType.ProjectInfoOnly
                        };
                        parameters[info.Guid] = info;

                        LogHelper.Debug($"Found ProjectInfo-only parameter: {info.Name} ({info.Guid})");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Error collecting project info parameters: {ex.Message}");
            }
        }

        /// <summary>
        /// Анализирует различия между параметрами
        /// </summary>
        private ParameterAnalysis AnalyzeParameterDifferences(
            Dictionary<Guid, ProjectSharedParameterInfo> sourceParams,
            Dictionary<Guid, ProjectSharedParameterInfo> templateParams)
        {
            var analysis = new ParameterAnalysis();

            // Параметры для удаления - те что есть в source, но нет в template
            foreach (var sourceParam in sourceParams.Values)
            {
                if (!templateParams.ContainsKey(sourceParam.Guid))
                {
                    analysis.ToDelete.Add(sourceParam);
                    LogHelper.Information($"Project parameter '{sourceParam.Name}' ({sourceParam.Guid}) marked for deletion");
                }
                else
                {
                    var templateParam = templateParams[sourceParam.Guid];
                    if (BindingsAreDifferent(sourceParam, templateParam))
                    {
                        analysis.ToUpdate.Add(templateParam);
                        LogHelper.Information($"Project parameter '{sourceParam.Name}' ({sourceParam.Guid}) marked for update");
                    }
                }
            }

            // Параметры для добавления - те что есть в template, но нет в source
            foreach (var templateParam in templateParams.Values)
            {
                if (!sourceParams.ContainsKey(templateParam.Guid))
                {
                    analysis.ToAdd.Add(templateParam);
                    LogHelper.Information($"Project parameter '{templateParam.Name}' ({templateParam.Guid}) marked for addition");
                }
            }

            return analysis;
        }

        /// <summary>
        /// Проверяет, отличаются ли bindings параметров
        /// </summary>
        private bool BindingsAreDifferent(ProjectSharedParameterInfo source, ProjectSharedParameterInfo template)
        {
            if (source.IsInstance != template.IsInstance) return true;
            if (source.IsProjectInfo != template.IsProjectInfo) return true;
            if (source.BindingType != template.BindingType) return true;

            var sourceCats = new HashSet<int>(source.Categories);
            var templateCats = new HashSet<int>(template.Categories);

            return !sourceCats.SetEquals(templateCats);
        }

        /// <summary>
        /// Удаляет только параметры проекта (не трогает параметры семейств)
        /// </summary>
        private int DeleteObsoleteProjectParameters(List<ProjectSharedParameterInfo> toDelete)
        {
            int deleted = 0;

            using (var tx = new Transaction(_sourceDoc, "Delete obsolete project parameters"))
            {
                tx.Start();

                foreach (var param in toDelete)
                {
                    try
                    {
                        // Удаляем binding, если он есть
                        if (param.Definition != null && param.HasBinding)
                        {
                            _sourceDoc.ParameterBindings.Remove(param.Definition);
                            LogHelper.Debug($"Removed binding for parameter '{param.Name}'");
                        }

                        // Проверяем, используется ли параметр где-то еще
                        if (IsParameterSafeToDelete(param))
                        {
                            _sourceDoc.Delete(param.Element.Id);
                            deleted++;
                            LogHelper.Information($"Deleted project parameter: {param.Name} ({param.Guid})");
                        }
                        else
                        {
                            LogHelper.Warning($"Parameter '{param.Name}' is used elsewhere, only binding removed");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to delete parameter '{param.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return deleted;
        }

        /// <summary>
        /// Проверяет, безопасно ли удалять SharedParameterElement
        /// </summary>
        private bool IsParameterSafeToDelete(ProjectSharedParameterInfo param)
        {
            // Если параметр имеет binding или используется в ProjectInfo - его можно удалять
            // Если параметр orphaned и не имеет привязок - возможно он из семейства, не удаляем

            if (!param.HasBinding && !param.IsProjectInfo)
            {
                // Дополнительная проверка: есть ли элементы, использующие этот параметр
                var collector = new FilteredElementCollector(_sourceDoc)
                    .WhereElementIsNotElementType();

                foreach (var elem in collector)
                {
                    var p = elem.GetParameters(param.Name).FirstOrDefault(p => p.IsShared);
                    if (p != null)
                    {
                        LogHelper.Debug($"Parameter '{param.Name}' is used by element {elem.Id}, not safe to delete");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Добавляет новые параметры из шаблона
        /// </summary>
        private int AddNewParameters(List<ProjectSharedParameterInfo> toAdd)
        {
            int added = 0;
            var parametersToProcess = new List<(ProjectSharedParameterInfo info, SharedParameterElement element)>();

            // Копируем SharedParameterElements
            using (var tx = new Transaction(_sourceDoc, "Copy shared parameter elements"))
            {
                tx.Start();

                foreach (var paramInfo in toAdd)
                {
                    try
                    {
                        // Проверяем, не существует ли уже параметр с таким GUID
                        var existingParam = new FilteredElementCollector(_sourceDoc)
                            .OfClass(typeof(SharedParameterElement))
                            .Cast<SharedParameterElement>()
                            .FirstOrDefault(p => p.GuidValue == paramInfo.Guid);

                        if (existingParam != null)
                        {
                            LogHelper.Warning($"Parameter '{paramInfo.Name}' ({paramInfo.Guid}) already exists, will create/update binding");
                            parametersToProcess.Add((paramInfo, existingParam));
                            continue;
                        }

                        // Копируем SharedParameterElement из template
                        if (paramInfo.Element != null)
                        {
                            var copyOptions = new CopyPasteOptions();
                            var copiedIds = ElementTransformUtils.CopyElements(
                                _templateDoc,
                                new List<ElementId> { paramInfo.Element.Id },
                                _sourceDoc,
                                Transform.Identity,
                                copyOptions);

                            if (copiedIds.Count > 0)
                            {
                                var copiedParam = _sourceDoc.GetElement(copiedIds.First()) as SharedParameterElement;
                                if (copiedParam != null)
                                {
                                    parametersToProcess.Add((paramInfo, copiedParam));
                                    LogHelper.Debug($"Copied parameter element '{paramInfo.Name}' ({paramInfo.Guid})");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Error($"Failed to copy parameter element '{paramInfo.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            // Создаем bindings
            using (var tx = new Transaction(_sourceDoc, "Create parameter bindings"))
            {
                tx.Start();

                foreach (var (paramInfo, paramElement) in parametersToProcess)
                {
                    try
                    {
                        if (CreateOrUpdateParameterBinding(paramElement, paramInfo))
                        {
                            added++;
                            LogHelper.Information($"Added parameter binding: {paramInfo.Name} ({paramInfo.Guid})");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to create binding for '{paramInfo.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return added;
        }

        /// <summary>
        /// Создает или обновляет binding для параметра
        /// </summary>
        private bool CreateOrUpdateParameterBinding(SharedParameterElement paramElement, ProjectSharedParameterInfo paramInfo)
        {
            try
            {
                var definition = paramElement.GetDefinition();
                if (definition == null) return false;

                // Удаляем существующий binding
                if (_sourceDoc.ParameterBindings.Contains(definition))
                {
                    _sourceDoc.ParameterBindings.Remove(definition);
                    LogHelper.Debug($"Removed existing binding for '{paramInfo.Name}'");
                }

                // Если параметр только для ProjectInfo, не создаем обычный binding
                if (paramInfo.BindingType == BindingType.ProjectInfoOnly)
                {
                    LogHelper.Debug($"Parameter '{paramInfo.Name}' is ProjectInfo only, skipping binding creation");
                    return true;
                }

                // Создаем CategorySet
                var categorySet = CreateCategorySet(paramInfo.Categories);
                if (categorySet.Size == 0)
                {
                    LogHelper.Warning($"No valid categories for parameter '{paramInfo.Name}'");
                    return false;
                }

                // Создаем binding
                Binding binding = paramInfo.IsInstance
                    ? _sourceDoc.Application.Create.NewInstanceBinding(categorySet)
                    : _sourceDoc.Application.Create.NewTypeBinding(categorySet);

                // Добавляем в ParameterBindings
                bool result = _sourceDoc.ParameterBindings.Insert(definition, binding, paramInfo.ParameterGroup);
                if (!result)
                {
                    result = _sourceDoc.ParameterBindings.ReInsert(definition, binding, paramInfo.ParameterGroup);
                }

                return result;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error creating binding for '{paramInfo.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Создает CategorySet из списка ID категорий
        /// </summary>
        private CategorySet CreateCategorySet(List<int> categoryIds)
        {
            var categories = _sourceDoc.Settings.Categories;
            var categorySet = _sourceDoc.Application.Create.NewCategorySet();

            foreach (var catId in categoryIds)
            {
                try
                {
                    var category = categories.get_Item((BuiltInCategory)catId);
                    if (category != null && category.AllowsBoundParameters)
                    {
                        categorySet.Insert(category);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Debug($"Could not add category {catId}: {ex.Message}");
                }
            }

            return categorySet;
        }

        /// <summary>
        /// Обновляет bindings существующих параметров
        /// </summary>
        private int UpdateParameterBindings(List<ProjectSharedParameterInfo> toUpdate)
        {
            int updated = 0;

            using (var tx = new Transaction(_sourceDoc, "Update parameter bindings"))
            {
                tx.Start();

                foreach (var templateParam in toUpdate)
                {
                    try
                    {
                        var sourceParam = new FilteredElementCollector(_sourceDoc)
                            .OfClass(typeof(SharedParameterElement))
                            .Cast<SharedParameterElement>()
                            .FirstOrDefault(p => p.GuidValue == templateParam.Guid);

                        if (sourceParam == null)
                        {
                            LogHelper.Warning($"Parameter '{templateParam.Name}' ({templateParam.Guid}) not found in source");
                            continue;
                        }

                        if (CreateOrUpdateParameterBinding(sourceParam, templateParam))
                        {
                            updated++;
                            LogHelper.Information($"Updated parameter binding: {templateParam.Name} ({templateParam.Guid})");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.Warning($"Failed to update parameter '{templateParam.Name}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return updated;
        }

        /// <summary>
        /// Копирует значения параметров ProjectInfo из шаблона
        /// </summary>
        private void CopyProjectParameterValues()
        {
            try
            {
                var sourceProjectInfo = _sourceDoc.ProjectInformation;
                var templateProjectInfo = _templateDoc.ProjectInformation;

                if (sourceProjectInfo == null || templateProjectInfo == null) return;

                using (var tx = new Transaction(_sourceDoc, "Copy project parameter values"))
                {
                    tx.Start();

                    foreach (Parameter templateParam in templateProjectInfo.Parameters)
                    {
                        if (!templateParam.HasValue || templateParam.IsReadOnly) continue;

                        var sourceParam = sourceProjectInfo.LookupParameter(templateParam.Definition.Name);
                        if (sourceParam == null || sourceParam.IsReadOnly) continue;

                        try
                        {
                            CopyParameterValue(templateParam, sourceParam);
                            LogHelper.Debug($"Copied value for parameter '{templateParam.Definition.Name}'");
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Debug($"Could not copy value for parameter '{templateParam.Definition.Name}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Error copying project parameter values: {ex.Message}");
            }
        }

        /// <summary>
        /// Копирует значение параметра с учетом типа хранения
        /// </summary>
        private void CopyParameterValue(Parameter source, Parameter target)
        {
            switch (source.StorageType)
            {
                case StorageType.Double:
                    target.Set(source.AsDouble());
                    break;
                case StorageType.Integer:
                    target.Set(source.AsInteger());
                    break;
                case StorageType.String:
                    target.Set(source.AsString() ?? string.Empty);
                    break;
                case StorageType.ElementId:
                    // Пропускаем ElementId параметры, так как ID элементов могут отличаться
                    break;
            }
        }

        /// <summary>
        /// Информация о shared параметре проекта
        /// </summary>
        private class ProjectSharedParameterInfo
        {
            public Guid Guid { get; set; }
            public string Name { get; set; }
            public BuiltInParameterGroup ParameterGroup { get; set; }
            public bool IsInstance { get; set; }
            public bool IsProjectInfo { get; set; }
            public bool HasBinding { get; set; }
            public BindingType BindingType { get; set; }
            public List<int> Categories { get; set; } = new List<int>();
            public InternalDefinition Definition { get; set; }
            public SharedParameterElement Element { get; set; }
        }

        /// <summary>
        /// Тип привязки параметра
        /// </summary>
        private enum BindingType
        {
            Instance,
            Type,
            ProjectInfoOnly
        }

        /// <summary>
        /// Результат анализа параметров
        /// </summary>
        private class ParameterAnalysis
        {
            public List<ProjectSharedParameterInfo> ToAdd { get; set; } = new List<ProjectSharedParameterInfo>();
            public List<ProjectSharedParameterInfo> ToUpdate { get; set; } = new List<ProjectSharedParameterInfo>();
            public List<ProjectSharedParameterInfo> ToDelete { get; set; } = new List<ProjectSharedParameterInfo>();
        }

        /// <summary>
        /// Результат операции переноса параметров
        /// </summary>
        public class SharedParametersRebaseResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int SourceParametersCount { get; set; }
            public int TemplateParametersCount { get; set; }
            public int AddedParameters { get; set; }
            public int UpdatedParameters { get; set; }
            public int DeletedParameters { get; set; }
            public TimeSpan Duration { get; set; }
        }
    }
}