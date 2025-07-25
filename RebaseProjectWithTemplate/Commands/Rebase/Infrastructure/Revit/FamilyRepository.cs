
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

        public void LoadFamilies(BuiltInCategory category, IEnumerable<string> exactNames)
        {
            int count = 0;
            foreach (string name in exactNames)
            {
                var famSym = new FilteredElementCollector(_tpl)
                    .OfCategory(category)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .First(s => s.Family.Name == name);

                var famDoc = _tpl.EditFamily(famSym.Family);
                string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rfa");
                famDoc.SaveAs(tmp);
                famDoc.Close(false);
                _doc.LoadFamily(tmp, new OverwriteOpts(), out _);
                File.Delete(tmp);
                LogHelper.Information($"Loaded family '{name}' from template.");
                count++;
            }
            LogHelper.Information($"Loaded {count} families.");
        }

        public void CopyTemplateTypes(IEnumerable<ElementId> typeIds, IProgress<string> progress = null)
        {
            var ids = typeIds.ToList();
            if (ids.Count == 0) return;

            LogHelper.Information($"Starting to copy {ids.Count} types from template...");

            int successCount = 0;
            int errorCount = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                var typeId = ids[i];
                progress?.Report($"Copying type {i + 1}/{ids.Count}...");

                try
                {
                    using (var tx = new Transaction(_doc, $"copy type {typeId}"))
                    {
                        tx.Start();
                        var opts = new CopyPasteOptions();
                        opts.SetDuplicateTypeNamesHandler(new DestTypesHandler());

                        var singleTypeList = new List<ElementId> { typeId };
                        ElementTransformUtils.CopyElements(_tpl, singleTypeList, _doc, null, opts);

                        tx.Commit();
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"Failed to copy type {typeId}: {ex.Message}");
                    errorCount++;
                }
            }

            LogHelper.Information($"Copied {successCount} types successfully, {errorCount} failed.");
        }

        public Dictionary<ElementId, ElementId> BuildIdMap(Dictionary<string, FamilyData> oldData, Dictionary<string, FamilyData> newData, List<MappingResult> mapping)
        {
            var dict = new Dictionary<ElementId, ElementId>(new IdCmp());

            foreach (var fam in mapping)
            {
                if (fam.New == "No Match") continue;
                if (!oldData.TryGetValue(fam.Old, out var ofd)) continue;
                if (!newData.TryGetValue(fam.New, out var nfd)) continue;

                foreach (var tp in fam.TypeMatches)
                {
                    if (tp.NewType == "No Match") continue;

                    var oldType = ofd.Types.FirstOrDefault(x => x.TypeName == tp.OldType);
                    var newType = nfd.Types.FirstOrDefault(x => x.TypeName == tp.NewType);
                    if (oldType == null || newType == null) continue;

                    var oldId = new ElementId(Convert.ToInt32(oldType.TypeId));
                    var newId = new ElementId(Convert.ToInt32(newType.TypeId));
                    dict[oldId] = newId;
                    LogHelper.Information($"Mapping ID {oldId} to {newId} ('{oldType.TypeName}' -> '{newType.TypeName}')");
                }
            }
            return dict;
        }

        public int SwitchInstances(BuiltInCategory category, Dictionary<ElementId, ElementId> idMap)
        {
            if (idMap.Count == 0) return 0;

            var instIds = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();

            int switched = 0;
            LogHelper.Information($"Switching {instIds.Count} instances...");
            using (var tx = new Transaction(_doc, "switch symbols"))
            {
                tx.Start();
                LogHelper.Information("Transaction started for switching instances.");
                foreach (var instId in instIds)
                {
                    var inst = _doc.GetElement(instId) as FamilyInstance;
                    if (inst == null) continue;

                    var oldSym = inst.Symbol;
                    if (!idMap.TryGetValue(oldSym.Id, out var newSymId)) continue;

                    var newSym = _doc.GetElement(newSymId) as FamilySymbol;
                    if (newSym == null) continue;

                    newSym.Activate();

                    var snap = Snapshot(inst);
                    inst.ChangeTypeId(newSymId);
                    Restore(inst, snap);
                    switched++;
                }

                LogHelper.Information("Committing transaction for switching instances...");

                //_doc.Regenerate();
                tx.Commit();
            }
            LogHelper.Information($"Switched {switched} instances.");
            return switched;
        }

        public int PurgeFamilies(BuiltInCategory category, string suffix)
        {
            var used = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Select(i => i.Symbol.Id)
                .ToHashSet(new IdCmp());

            var kill = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamCmp())
                .Where(f => f.Name.EndsWith(suffix) && !f.GetFamilySymbolIds().Any(id => used.Contains(id)))
                .Select(f => f.Id)
                .ToList();

            if (kill.Count == 0) return 0;

            using (var tx = new Transaction(_doc, "purge *_old"))
            {
                tx.Start();
                _doc.Delete(kill);
                tx.Commit();
            }
            LogHelper.Information($"Purged {kill.Count} old families.");
            return kill.Count;
        }

        private static Dictionary<string, (StorageType st, object val)> Snapshot(FamilyInstance inst)
        {
            var dict = new Dictionary<string, (StorageType, object)>();
            foreach (Parameter p in inst.ParametersMap)
            {
                if (!IsUserDefinedWritable(p)) continue;

                object v = null;
                switch (p.StorageType)
                {
                    case StorageType.Double: v = p.AsDouble(); break;
                    case StorageType.Integer: v = p.AsInteger(); break;
                    case StorageType.String: v = p.AsString(); break;
                    case StorageType.ElementId: v = p.AsValueString(); break;
                }
                if (v != null)
                    dict[p.Definition.Name] = (p.StorageType, v);
            }
            return dict;
        }

        private static void Restore(FamilyInstance inst, IReadOnlyDictionary<string, (StorageType st, object val)> snap)
        {
            foreach (var kvp in snap)
            {
                var name = kvp.Key;
                var data = kvp.Value;

                var p = inst.LookupParameter(name);
                if (p == null || p.IsReadOnly) continue;

                try
                {
                    switch (data.st)
                    {
                        case StorageType.Double: p.Set((double)data.val); break;
                        case StorageType.Integer: p.Set((int)data.val); break;
                        case StorageType.String: p.Set((string)data.val); break;
                        case StorageType.ElementId: p.SetValueString((string)data.val); break;
                    }
                }
                catch { /* ignore */ }
            }
        }

        private static bool  IsUserDefinedWritable(Parameter p)
        { 
            if (p == null || p.IsReadOnly) return false;
            if (p.IsShared) return true;
            return p.Id.IntegerValue > 0;
        }

        

        private class OverwriteOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool inUse, out bool overwrite) { overwrite = true; return true; }
            public bool OnSharedFamilyFound(Family shared, bool inUse, out FamilySource src, out bool ov) { src = FamilySource.Family; ov = true; return true; }
        }

        internal class FamCmp : IEqualityComparer<Family> { public bool Equals(Family a, Family b) => a?.Id == b?.Id; public int GetHashCode(Family f) => f.Id.IntegerValue; }
        internal class IdCmp : IEqualityComparer<ElementId> { public bool Equals(ElementId a, ElementId b) => a.IntegerValue == b.IntegerValue; public int GetHashCode(ElementId id) => id.IntegerValue; }
        internal class DestTypesHandler : IDuplicateTypeNamesHandler { public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes; }
    }
}
