using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Infrastructure;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    /// <summary>
    /// Оркестратор для замены спецификаций (Schedules) из шаблона
    /// </summary>
    public class SchedulesRebaseOrchestrator
    {
        private readonly Document _sourceDoc;
        private readonly Document _templateDoc;



        public SchedulesRebaseOrchestrator(Document sourceDoc, Document templateDoc)
        {
            _sourceDoc = sourceDoc;
            _templateDoc = templateDoc;
        }

        public SchedulesRebaseResult Rebase(IProgress<string> progress = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SchedulesRebaseResult();

            try
            {
                LogHelper.Information("Starting schedules rebase...");



                // ЭТАП 1: Удаление всех schedule instances
                progress?.Report("Removing schedule instances from sheets...");
                result.DeletedInstances = DeleteAllScheduleInstances();

                // ЭТАП 2: Удаление всех schedules
                progress?.Report("Deleting existing schedules...");
                result.DeletedSchedules = DeleteAllSchedules();

                // ЭТАП 3: Анализ и копирование schedules
                progress?.Report("Analyzing and copying schedules from template...");
                var copyResult = AnalyzeAndCopySchedules(progress);

                result.CopiedSchedules = copyResult.CopiedCount;
                result.FailedSchedules = copyResult.FailedSchedules;
                result.SkippedSchedules = copyResult.SkippedSchedules;

                // Закрываем все открытые виды спецификаций
                CloseAllScheduleViews();

                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                result.Success = true;

                LogHelper.Information($"Schedules rebase completed in {result.Duration.TotalSeconds:F2}s. " +
                    $"Deleted: {result.DeletedInstances} instances, {result.DeletedSchedules} schedules. " +
                    $"Copied: {result.CopiedSchedules} schedules. " +
                    $"Skipped: {result.SkippedSchedules.Count}, Failed: {result.FailedSchedules.Count}");

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LogHelper.Error($"Fatal error in schedules rebase: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Закрывает все открытые виды спецификаций
        /// </summary>
        private void CloseAllScheduleViews()
        {
            try
            {
                var uiDoc = new UIDocument(_sourceDoc);
                var openUIViews = uiDoc.GetOpenUIViews().ToList();

                foreach (var uiView in openUIViews)
                {
                    var view = _sourceDoc.GetElement(uiView.ViewId) as View;
                    if (view is ViewSchedule)
                    {
                        try
                        {
                            uiView.Close();
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Debug($"Could not close schedule view {view.Name}: {ex.Message}");
                        }
                    }
                }

                // Альтернатива: использовать PostCommand для закрытия всех видов
                // var commandId = RevitCommandId.LookupPostableCommandId(PostableCommand.CloseInactiveViews);
                // if (commandId != null && uiDoc.Application.CanPostCommand(commandId))
                // {
                //     uiDoc.Application.PostCommand(commandId);
                // }
            }
            catch (Exception ex)
            {
                LogHelper.Debug($"Error closing schedule views: {ex.Message}");
            }
        }



        #region ЭТАП 1: Удаление schedule instances

        private int DeleteAllScheduleInstances()
        {
            int deletedCount = 0;

            using (var tx = new Transaction(_sourceDoc, "Delete schedule instances"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    var scheduleInstances = new FilteredElementCollector(_sourceDoc)
                        .OfClass(typeof(ScheduleSheetInstance))
                        .Cast<ScheduleSheetInstance>()
                        .ToList();

                    LogHelper.Information($"Found {scheduleInstances.Count} schedule instances on sheets");

                    // Используем batch deletion для производительности
                    var idsToDelete = scheduleInstances.Select(si => si.Id).ToList();

                    if (idsToDelete.Any())
                    {
                        _sourceDoc.Delete(idsToDelete);
                        deletedCount = idsToDelete.Count;
                    }

                    tx.Commit();
                    LogHelper.Information($"Deleted {deletedCount} schedule instances");
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    LogHelper.Error($"Failed to delete schedule instances: {ex.Message}");
                    throw;
                }
            }

            return deletedCount;
        }

        #endregion

        #region ЭТАП 2: Удаление schedules

        private int DeleteAllSchedules()
        {
            int deletedCount = 0;

            using (var tx = new Transaction(_sourceDoc, "Delete all schedules"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    var allSchedules = new FilteredElementCollector(_sourceDoc)
                        .OfClass(typeof(ViewSchedule))
                        .Cast<ViewSchedule>()
                        .Where(vs => !vs.IsTemplate &&
                                    !vs.IsTitleblockRevisionSchedule &&
                                    vs.Definition != null)
                        .ToList();

                    LogHelper.Information($"Found {allSchedules.Count} schedules to delete");

                    // Batch deletion
                    var idsToDelete = allSchedules.Select(s => s.Id).ToList();

                    if (idsToDelete.Any())
                    {
                        _sourceDoc.Delete(idsToDelete);
                        deletedCount = idsToDelete.Count;
                    }

                    tx.Commit();
                    LogHelper.Information($"Deleted {deletedCount} schedules");
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    LogHelper.Error($"Failed to delete schedules: {ex.Message}");
                    throw;
                }
            }

            return deletedCount;
        }

        #endregion

        #region ЭТАП 3: Анализ и копирование schedules

        /// <summary>
        /// Анализирует и копирует спецификации из шаблона
        /// </summary>
        private CopySchedulesResult AnalyzeAndCopySchedules(IProgress<string> progress)
        {
            var result = new CopySchedulesResult();

            // Подготовка UI для минимизации открытия видов
            UIDocument uiDoc = null;
            View dummyView = null;

            try
            {
                uiDoc = new UIDocument(_sourceDoc);

                // Создаем временный 3D вид для переключения
                using (var tx = new Transaction(_sourceDoc, "Create temporary view"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();

                    var view3DType = new FilteredElementCollector(_sourceDoc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                    if (view3DType != null)
                    {
                        dummyView = View3D.CreateIsometric(_sourceDoc, view3DType.Id);
                        dummyView.Name = "Temp_ScheduleCopy_View";
                    }

                    tx.Commit();
                }

                // Активируем временный вид
                if (dummyView != null)
                {
                    uiDoc.ActiveView = dummyView;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug($"Could not create temporary view: {ex.Message}");
            }

            // Получаем все спецификации из шаблона
            var templateSchedules = new FilteredElementCollector(_templateDoc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(vs => CanBeCopied(vs))
                .ToList();

            LogHelper.Information($"Found {templateSchedules.Count} schedules in template for analysis");

            // Копируем все спецификации батчами
            if (templateSchedules.Any())
            {
                progress?.Report($"Copying {templateSchedules.Count} schedules...");
                result.CopiedCount = CopySchedulesBatch(templateSchedules, result.FailedSchedules);
            }

            // Удаляем временный вид
            if (dummyView != null)
            {
                try
                {
                    using (var tx = new Transaction(_sourceDoc, "Delete temporary view"))
                    {
                        CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                        tx.Start();
                        _sourceDoc.Delete(dummyView.Id);
                        tx.Commit();
                    }
                }
                catch { }
            }

            return result;
        }





        /// <summary>
        /// Копирует спецификации батчами для лучшей производительности
        /// </summary>
        private int CopySchedulesBatch(List<ViewSchedule> schedules, List<FailedScheduleInfo> failedList)
        {
            int copiedCount = 0;
            const int batchSize = 50; // Копируем по 50 спецификаций за раз

            // Сохраняем текущий активный вид
            var uiDoc = new UIDocument(_sourceDoc);
            var originalActiveView = uiDoc.ActiveView;

            for (int i = 0; i < schedules.Count; i += batchSize)
            {
                var batch = schedules.Skip(i).Take(batchSize).ToList();
                var batchIds = batch.Select(s => s.Id).ToList();

                using (var tx = new Transaction(_sourceDoc, $"Copy schedules batch {i / batchSize + 1}"))
                {
                    CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                    tx.Start();

                    try
                    {
                        var copyOptions = new CopyPasteOptions();
                        copyOptions.SetDuplicateTypeNamesHandler(new DuplicateNamesHandler());

                        var copiedIds = ElementTransformUtils.CopyElements(
                            _templateDoc,
                            batchIds,
                            _sourceDoc,
                            Transform.Identity,
                            copyOptions);

                        copiedCount += copiedIds.Count;

                        // Закрываем скопированные виды
                        CloseViews(copiedIds, uiDoc);

                        // Логируем результаты батча
                        if (copiedIds.Count < batchIds.Count)
                        {
                            LogHelper.Warning($"Batch copy: requested {batchIds.Count}, copied {copiedIds.Count}");

                            // Определяем какие спецификации не скопировались
                            var copiedElements = copiedIds.Select(id => _sourceDoc.GetElement(id)).Where(e => e != null);
                            var copiedNames = new HashSet<string>(copiedElements.Select(e => e.Name));

                            foreach (var schedule in batch)
                            {
                                if (!copiedNames.Any(name => name.Contains(schedule.Name)))
                                {
                                    failedList.Add(new FailedScheduleInfo
                                    {
                                        Name = schedule.Name,
                                        Type = GetScheduleType(schedule),
                                        Reason = "Failed during batch copy"
                                    });
                                }
                            }
                        }

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();

                        // При ошибке батча пробуем скопировать по одной
                        LogHelper.Warning($"Batch copy failed: {ex.Message}. Trying individual copy...");

                        foreach (var schedule in batch)
                        {
                            if (CopyScheduleIndividually(schedule, failedList))
                                copiedCount++;
                        }
                    }
                }
            }

            // Восстанавливаем оригинальный активный вид
            try
            {
                if (originalActiveView != null && originalActiveView.IsValidObject)
                {
                    uiDoc.ActiveView = originalActiveView;
                }
            }
            catch { }

            return copiedCount;
        }

        /// <summary>
        /// Копирует спецификацию индивидуально (fallback метод)
        /// </summary>
        private bool CopyScheduleIndividually(ViewSchedule schedule, List<FailedScheduleInfo> failedList)
        {
            var uiDoc = new UIDocument(_sourceDoc);

            using (var tx = new Transaction(_sourceDoc, $"Copy schedule: {schedule.Name}"))
            {
                CommonFailuresPreprocessor.SetFailuresPreprocessor(tx);
                tx.Start();

                try
                {
                    var copyOptions = new CopyPasteOptions();
                    copyOptions.SetDuplicateTypeNamesHandler(new DuplicateNamesHandler());

                    var copiedIds = ElementTransformUtils.CopyElements(
                        _templateDoc,
                        new List<ElementId> { schedule.Id },
                        _sourceDoc,
                        Transform.Identity,
                        copyOptions);

                    if (copiedIds.Count > 0)
                    {
                        // Закрываем скопированный вид
                        CloseViews(copiedIds, uiDoc);

                        tx.Commit();
                        return true;
                    }
                    else
                    {
                        tx.RollBack();
                        failedList.Add(new FailedScheduleInfo
                        {
                            Name = schedule.Name,
                            Type = GetScheduleType(schedule),
                            Reason = "Copy returned no elements"
                        });
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    failedList.Add(new FailedScheduleInfo
                    {
                        Name = schedule.Name,
                        Type = GetScheduleType(schedule),
                        Reason = ex.Message
                    });
                    return false;
                }
            }
        }

        /// <summary>
        /// Закрывает открытые виды после копирования
        /// </summary>
        private void CloseViews(ICollection<ElementId> viewIds, UIDocument uiDoc)
        {
            try
            {
                foreach (var viewId in viewIds)
                {
                    var view = _sourceDoc.GetElement(viewId) as View;
                    if (view != null)
                    {
                        // Получаем все открытые UIViews
                        var openUIViews = uiDoc.GetOpenUIViews();

                        foreach (var uiView in openUIViews)
                        {
                            if (uiView.ViewId == viewId)
                            {
                                // Закрываем вид
                                uiView.Close();
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Debug($"Error closing views: {ex.Message}");
                // Не критичная ошибка, продолжаем работу
            }
        }

        #endregion

        #region Helper Methods



        private bool CanBeCopied(ViewSchedule schedule)
        {
            if (schedule.IsTemplate)
                return false;

            if (schedule.IsTitleblockRevisionSchedule)
                return false;

            if (schedule.Definition == null)
                return false;

            if (IsInternalSchedule(schedule))
                return false;

            // Проверка на embedded schedules
            try
            {
                if (schedule.OwnerViewId != ElementId.InvalidElementId)
                    return false;
            }
            catch { }

            // Проверка на assembly schedules
            if (schedule.AssociatedAssemblyInstanceId != ElementId.InvalidElementId)
                return false;

            return true;
        }

        private bool IsInternalSchedule(ViewSchedule schedule)
        {
            var name = schedule.Name;

            if (string.IsNullOrEmpty(name))
                return true;

            if (name.StartsWith("<") && name.EndsWith(">"))
                return true;

            if (name.Contains("Revision Schedule"))
                return true;

            if (name.StartsWith("System_"))
                return true;

            return false;
        }

        private string GetScheduleType(ViewSchedule schedule)
        {
            try
            {
                if (schedule.Definition.IsKeySchedule)
                    return "Key Schedule";

                var categoryId = schedule.Definition.CategoryId;
                if (categoryId == null)
                    return "Multi-Category";

                var category = Category.GetCategory(_templateDoc, categoryId);
                return category?.Name ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        #endregion

        #region Result Classes

        public class SchedulesRebaseResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int DeletedInstances { get; set; }
            public int DeletedSchedules { get; set; }
            public int CopiedSchedules { get; set; }
            public List<SkippedScheduleInfo> SkippedSchedules { get; set; } = new List<SkippedScheduleInfo>();
            public List<FailedScheduleInfo> FailedSchedules { get; set; } = new List<FailedScheduleInfo>();
            public TimeSpan Duration { get; set; }
        }

        private class CopySchedulesResult
        {
            public int CopiedCount { get; set; }
            public List<SkippedScheduleInfo> SkippedSchedules { get; set; } = new List<SkippedScheduleInfo>();
            public List<FailedScheduleInfo> FailedSchedules { get; set; } = new List<FailedScheduleInfo>();
        }



        public class SkippedScheduleInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Reason { get; set; }
        }

        public class FailedScheduleInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Reason { get; set; }
        }

        private class DuplicateNamesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        #endregion
    }
}