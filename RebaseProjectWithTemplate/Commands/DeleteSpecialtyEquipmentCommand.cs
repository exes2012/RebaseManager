using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RebaseProjectWithTemplate.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeleteSpecialtyEquipmentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var doc = uiApp.ActiveUIDocument.Document;

                if (doc.IsLinked)
                {
                    TaskDialog.Show("Error", "Cannot run command on a linked document.");
                    return Result.Cancelled;
                }

                LogHelper.Information("Starting Delete Specialty Equipment command");

                // Get all levels sorted by elevation
                var allLevels = GetAllLevelsSortedByElevation(doc);
                
                if (allLevels.Count < 3)
                {
                    TaskDialog.Show("Warning", "Project has less than 3 levels. No elements will be deleted.");
                    LogHelper.Warning("Project has less than 3 levels, operation cancelled");
                    return Result.Cancelled;
                }

                // Get levels to process (exclude first two levels)
                var levelsToProcess = allLevels.Skip(2).ToList();
                var levelNames = string.Join(", ", levelsToProcess.Select(l => l.Name));
                
                LogHelper.Information($"Will process levels: {levelNames}");

                // Show confirmation dialog
                var confirmResult = TaskDialog.Show("Confirm Deletion", 
                    $"This will delete ALL Specialty Equipment family instances from the following levels:\n\n{levelNames}\n\n" +
                    $"Levels 1 and 2 ({allLevels[0].Name}, {allLevels[1].Name}) will be preserved.\n\n" +
                    "Do you want to continue?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (confirmResult != TaskDialogResult.Yes)
                {
                    LogHelper.Information("Operation cancelled by user");
                    return Result.Cancelled;
                }

                // Get all Specialty Equipment instances on levels to process
                var instancesToDelete = GetSpecialtyEquipmentInstancesOnLevels(doc, levelsToProcess);
                
                if (instancesToDelete.Count == 0)
                {
                    TaskDialog.Show("Information", "No Specialty Equipment instances found on the specified levels.");
                    LogHelper.Information("No Specialty Equipment instances found to delete");
                    return Result.Succeeded;
                }

                // Delete instances
                int deletedCount = DeleteInstances(doc, instancesToDelete);

                // Show result
                var resultMessage = $"Successfully deleted {deletedCount} Specialty Equipment instances from levels:\n{levelNames}";
                TaskDialog.Show("Deletion Complete", resultMessage);
                
                LogHelper.Information($"Delete Specialty Equipment command completed. Deleted {deletedCount} instances");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                LogHelper.Error($"Delete Specialty Equipment command failed: {ex.Message}");
                return Result.Failed;
            }
        }

        private List<Level> GetAllLevelsSortedByElevation(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => level.Elevation)
                .ToList();
        }

        private List<FamilyInstance> GetSpecialtyEquipmentInstancesOnLevels(Document doc, List<Level> levels)
        {
            var levelIds = levels.Select(l => l.Id).ToHashSet();
            var instances = new List<FamilyInstance>();

            // Get all Specialty Equipment instances
            var allInstances = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_SpecialityEquipment)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            LogHelper.Information($"Found {allInstances.Count} total Specialty Equipment instances");

            foreach (var instance in allInstances)
            {
                var instanceLevel = GetInstanceLevel(instance);
                if (instanceLevel != null && levelIds.Contains(instanceLevel.Id))
                {
                    instances.Add(instance);
                }
            }

            LogHelper.Information($"Found {instances.Count} Specialty Equipment instances on target levels");
            return instances;
        }

        private Level GetInstanceLevel(FamilyInstance instance)
        {
            try
            {
                // Try to get level from FAMILY_LEVEL_PARAM parameter
                var levelParam = instance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                if (levelParam != null && levelParam.HasValue)
                {
                    var levelId = levelParam.AsElementId();
                    if (levelId != ElementId.InvalidElementId)
                    {
                        return instance.Document.GetElement(levelId) as Level;
                    }
                }

                // Try to get level from Host property
                if (instance.Host is Level hostLevel)
                {
                    return hostLevel;
                }

                // Try to get level from LevelId property (if available)
                var levelIdProperty = instance.LevelId;
                if (levelIdProperty != ElementId.InvalidElementId)
                {
                    return instance.Document.GetElement(levelIdProperty) as Level;
                }

                // Try to get level from "Level" parameter by name
                var levelParamByName = instance.LookupParameter("Level");
                if (levelParamByName != null && levelParamByName.HasValue)
                {
                    var levelId = levelParamByName.AsElementId();
                    if (levelId != ElementId.InvalidElementId)
                    {
                        return instance.Document.GetElement(levelId) as Level;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Failed to get level for instance {instance.Id}: {ex.Message}");
                return null;
            }
        }

        private int DeleteInstances(Document doc, List<FamilyInstance> instances)
        {
            int deletedCount = 0;

            using (var transaction = new Transaction(doc, "Delete Specialty Equipment Instances"))
            {
                transaction.Start();

                try
                {
                    var instanceIds = instances.Select(i => i.Id).ToList();
                    var deletedIds = doc.Delete(instanceIds);
                    deletedCount = deletedIds.Count;

                    transaction.Commit();
                    LogHelper.Information($"Successfully deleted {deletedCount} instances in transaction");
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"Failed to delete instances: {ex.Message}");
                    throw;
                }
            }

            return deletedCount;
        }
    }
}
