using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Infrastructure.Comparers;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using RebaseProjectWithTemplate.Commands.Export.Models;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public abstract class CategoryRebaseService
    {
        protected readonly IAiService _ai;
        protected readonly BuiltInCategory _bic;
        protected readonly string _oldSuffix;

        protected CategoryRebaseService(
            IAiService ai,
            BuiltInCategory bic,
            string oldSuffix = "_rebase_old")
        {
            _ai = ai;
            _bic = bic;
            _oldSuffix = oldSuffix;
        }

        // ───────────────────────── PUBLIC PIPELINE ─────────────────────────
        public async Task RebaseAsync(Document src, Document tpl, IProgress<string> log)
        {
            LogHelper.Information($"[{_bic}] RebaseAsync started");

            // 0. Собираем полные данные (имена + ID) по src и tpl
            var srcData = CollectFamilyData(src, stripSuffix: false);
            var tplData = CollectFamilyData(tpl, stripSuffix: false);
            LogHelper.Debug($"Source families ({srcData.Count}): {string.Join(", ", srcData.Select(f => f.FamilyName))}");
            LogHelper.Debug($"Template families ({tplData.Count}): {string.Join(", ", tplData.Select(f => f.FamilyName))}");

            if (srcData.Count == 0 || tplData.Count == 0)
            {
                log.Report($"[{_bic}] no families – skip");
                LogHelper.Warning($"[{_bic}] no families found in src or tpl, skipping");
                return;
            }

            // 1. Определяем exact vs loose
            var exactNames = new HashSet<string>(
                srcData.Select(f => f.FamilyName)
                       .Intersect(tplData.Select(f => f.FamilyName)));
            var looseSrc = srcData
                .Where(f => !exactNames.Contains(f.FamilyName))
                .ToList();
            LogHelper.Debug($"Exact families ({exactNames.Count}): {string.Join(", ", exactNames)}");
            LogHelper.Debug($"Loose families ({looseSrc.Count}): {string.Join(", ", looseSrc.Select(f => f.FamilyName))}");

            // 2. AI-маппинг по именам/типам
            log.Report($"[{_bic}] Calling AI mapping for {looseSrc.Count} families…");
            var mapping = await GetAiMappingAsync(looseSrc, tplData);
            LogHelper.Debug($"AI mapping returned {mapping.Count} entries");
            LogHelper.Information("AI mapping details:");
            foreach (var fam in mapping)
            {
                var newFam = fam.New == "No Match" ? "(no match)" : fam.New;
                LogHelper.Information($"  Family: {fam.Old} → {newFam}");
                foreach (var tp in fam.TypeMatches)
                {
                    var newType = tp.NewType == "No Match" ? "(no match)" : tp.NewType;
                    LogHelper.Debug($"    Type: {tp.OldType} → {newType}");
                }
            }

            using (var tg = new TransactionGroup(src, $"[{_bic}] rebase"))
            {
                tg.Start();
                LogHelper.Debug("TransactionGroup started");

                var oldData = looseSrc.ToDictionary(f => f.FamilyName, f => f);
                LogHelper.Debug($"OldData entries: {oldData.Count}");

                RenameLooseFamilies(src, oldData.Keys);
                LogHelper.Information($"Renamed loose families: {string.Join(", ", oldData.Keys.Select(n => $"{n}→{n}{_oldSuffix}"))}");
                LoadExactFamilies(src, tpl, exactNames);
                LogHelper.Information($"Loaded exact families: {string.Join(", ", exactNames)}");

                var exactFamilyNames = exactNames;      
                var allTemplateTypeIds = tplData
                    .Where(f => !exactFamilyNames.Contains(f.FamilyName))   
                    .SelectMany(f => f.Types)
                    .Select(t => new ElementId((int)t.TypeId))
                    .Distinct()
                    .ToList();
                LogHelper.Debug($"Copying {allTemplateTypeIds.Count} template types:");
                var idSet = new HashSet<int>(allTemplateTypeIds.Select(e => e.IntegerValue));
                foreach (var f in tplData)
                    foreach (var t in f.Types)
                        if (idSet.Contains((int)t.TypeId))
                            LogHelper.Debug($"  {f.FamilyName}:{t.TypeName} (Id={t.TypeId})");
                CopyTemplateTypesById(src, tpl, allTemplateTypeIds);
                LogHelper.Information($"Copied {allTemplateTypeIds.Count} types from template");

                // 6. Собираем NEW данные только по mapped‑семействам
                var mappedNewNames = mapping
                    .Where(m => !string.IsNullOrEmpty(m.New) && m.New != "No Match")
                    .Select(m => m.New)
                    .Distinct()
                    .ToList();
                LogHelper.Debug($"Mapped new family names: {string.Join(", ", mappedNewNames)}");
                var newData = CollectFamilyData(src, stripSuffix: false)
                    .Where(f => mappedNewNames.Contains(f.FamilyName))
                    .ToDictionary(f => f.FamilyName, f => f);
                LogHelper.Debug($"NewData entries: {newData.Count}, families: {string.Join(", ", newData.Keys)}");

                // 7. Строим idMap и переключаем инстансы
                var idMap = BuildIdMap(oldData, newData, mapping);
                LogHelper.Information($"Built ID map: {idMap.Count} entries");
                int switched = SwitchById(src, idMap);
                LogHelper.Information($"Switched symbols on {switched} instances");

                // 8. Удаляем устаревшие (*_rebase_old)
                int purged = PurgeOldFamilies(src);
                LogHelper.Information($"Purged {purged} old families with suffix {_oldSuffix}");

                tg.Assimilate();
                LogHelper.Debug("TransactionGroup assimilated");
            }

            LogHelper.Information($"[{_bic}] RebaseAsync completed");
        }

        // ───────────────────────── DATA COLLECT ─────────────────────────
        protected List<FamilyData> CollectFamilyData(Document doc, bool stripSuffix)
        {
            var list = new List<FamilyData>();
            var symbols = new FilteredElementCollector(doc)
                .OfCategory(_bic)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>();

            foreach (var grp in symbols.GroupBy(s => s.Family.Name))
            {
                string famName = grp.Key;
                if (stripSuffix &&
                    famName.EndsWith(_oldSuffix, StringComparison.Ordinal))
                {
                    famName = famName.Substring(0, famName.Length - _oldSuffix.Length);
                }

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

        // ───────────────────────── RENAME *_old ─────────────────────────
        protected void RenameLooseFamilies(Document doc, IEnumerable<string> looseNames)
        {
            var fams = new FilteredElementCollector(doc)
                .OfCategory(_bic)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamCmp())
                .Where(f => looseNames.Contains(f.Name));

            using (var tx = new Transaction(doc, "mark *_old"))
            {
                tx.Start();
                int count = 0;
                foreach (var f in fams)
                {
                    f.Name = MakeUnique(doc, f.Name + _oldSuffix);
                    count++;
                }
                tx.Commit();
                LogHelper.Debug($"RenameLooseFamilies: renamed {count} families");
            }
        }

        // ───────────────────────── LOAD exact ─────────────────────────
        protected void LoadExactFamilies(Document src, Document tpl, HashSet<string> exactNames)
        {
            int loaded = 0;
            foreach (string name in exactNames)
            {
                var famSym = new FilteredElementCollector(tpl)
                    .OfCategory(_bic)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .First(s => s.Family.Name == name);

                var famDoc = tpl.EditFamily(famSym.Family);
                string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rfa");
                famDoc.SaveAs(tmp);
                famDoc.Close(false);
                src.LoadFamily(tmp, new OverwriteOpts(), out _);
                File.Delete(tmp);
                loaded++;
                LogHelper.Debug($"Loaded exact family: {name}");
            }
            LogHelper.Debug($"LoadExactFamilies: loaded {loaded} families");
        }

        // ───────────────────────── COPY шаблона ─────────────────────────
        private void CopyTemplateTypesById(Document dest, Document tpl, IEnumerable<ElementId> typeIds)
        {
            var ids = typeIds.ToList();
            if (ids.Count == 0)
            {
                LogHelper.Debug("CopyTemplateTypesById: no types to copy");
                return;
            }

            using (var tx = new Transaction(dest, "copy family types"))
            {
                tx.Start();
                var opts = new CopyPasteOptions();
                opts.SetDuplicateTypeNamesHandler(new DestTypesHandler());
                ElementTransformUtils.CopyElements(tpl, ids, dest, null, opts);
                tx.Commit();
                LogHelper.Debug($"CopyTemplateTypesById: copied {ids.Count} types");
            }
        }

        // ───────────────────────── AI‑HOOK ─────────────────────────
        protected abstract Task<List<MappingResult>> GetAiMappingAsync(
            List<FamilyData> looseSrc,
            List<FamilyData> templateData);

        // ──────────────────────── Id‑MAP builder ───────────────────────
        private Dictionary<ElementId, ElementId> BuildIdMap(
            Dictionary<string, FamilyData> oldData,
            Dictionary<string, FamilyData> newData,
            List<MappingResult> mapping)
        {
            var dict = new Dictionary<ElementId, ElementId>(new IdCmp());
            int mapped = 0, missingOld = 0, missingNew = 0;

            foreach (var fam in mapping)
            {
                if (fam.New == "No Match")
                {
                    LogHelper.Debug($"Skipping family {fam.Old}: No Match");
                    continue;
                }
                if (!oldData.TryGetValue(fam.Old, out var ofd))
                {
                    missingOld++;
                    LogHelper.Warning($"Old family not found in oldData: {fam.Old}");
                    continue;
                }
                if (!newData.TryGetValue(fam.New, out var nfd))
                {
                    missingNew++;
                    LogHelper.Warning($"New family not found in newData: {fam.New}");
                    continue;
                }

                foreach (var tp in fam.TypeMatches)
                {
                    if (tp.NewType == "No Match")
                    {
                        LogHelper.Debug($"Skipping type match {tp.OldType}: No Match");
                        continue;
                    }

                    var oldType = ofd.Types.FirstOrDefault(x => x.TypeName == tp.OldType);
                    var newType = nfd.Types.FirstOrDefault(x => x.TypeName == tp.NewType);
                    if (oldType == null || newType == null)
                    {
                        LogHelper.Warning($"Type mismatch for family {fam.Old}: {tp.OldType} or {tp.NewType} not found");
                        continue;
                    }

                    var oldId = new ElementId((int)oldType.TypeId);
                    var newId = new ElementId((int)newType.TypeId);
                    dict[oldId] = newId;
                    mapped++;
                    LogHelper.Debug($"Mapped ID {oldId.IntegerValue} ({ofd.FamilyName}:{oldType.TypeName}) → {newId.IntegerValue} ({nfd.FamilyName}:{newType.TypeName})");
                }
            }

            LogHelper.Information($"BuildIdMap: mapped={mapped}, missingOld={missingOld}, missingNew={missingNew}");
            return dict;
        }

        // ───────────────────────── SWITCH по Id ─────────────────────────
        private int SwitchById(Document doc, Dictionary<ElementId, ElementId> idMap)
        {
            if (idMap.Count == 0)
            {
                LogHelper.Warning("SwitchById: idMap is empty, nothing to switch");
                return 0;
            }

            var instIds = new FilteredElementCollector(doc)
                .OfCategory(_bic)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();
            LogHelper.Debug($"SwitchById: found {instIds.Count} instances");

            int switched = 0, skipped = 0;
            using (var tx = new Transaction(doc, "switch symbols"))
            {
                tx.Start();
                foreach (var instId in instIds)
                {
                    var inst = doc.GetElement(instId) as FamilyInstance;
                    if (inst == null)
                    {
                        skipped++;
                        continue;
                    }

                    var oldSym = inst.Symbol;
                    if (!idMap.TryGetValue(oldSym.Id, out var newSymId))
                    {
                        skipped++;
                        continue;
                    }

                    var newSym = doc.GetElement(newSymId) as FamilySymbol;
                    if (newSym == null)
                    {
                        skipped++;
                        continue;
                    }

                    newSym.Activate();

                    LogHelper.Information(
                        $"Instance {instId.IntegerValue}: " +
                        $"{oldSym.Family.Name}:{oldSym.Name} → " +
                        $"{newSym.Family.Name}:{newSym.Name}");

                    var snap = Snapshot(inst);
                    inst.ChangeTypeId(newSymId);
                    Restore(inst, snap);
                    switched++;
                }

                doc.Regenerate();
             tx.Commit();
            }

            LogHelper.Information($"SwitchById finished: switched={switched}, skipped={skipped}");
            return switched;
        }

        private static bool IsUserDefinedWritable(Parameter p)
        {
            if (p == null || p.IsReadOnly) return false;   // нельзя писать
            if (p.IsShared) return true;    // shared == «человечек»

            // family‑parameter?  (любое положительное Id)
            return p.Id.IntegerValue > 0;
        }


        // ───────────────────────── PURGE *_old ─────────────────────────
        protected int PurgeOldFamilies(Document d)
        {
            var used = new FilteredElementCollector(d)
                .OfCategory(_bic)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Select(i => i.Symbol.Id)
                .ToHashSet(new IdCmp());

            var kill = new FilteredElementCollector(d)
                .OfCategory(_bic)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamCmp())
                .Where(f => f.Name.EndsWith(_oldSuffix) &&
                            !f.GetFamilySymbolIds().Any(id => used.Contains(id)))
                .Select(f => f.Id)
                .ToList();

            if (kill.Count == 0)
            {
                LogHelper.Debug("PurgeOldFamilies: no families to delete");
                return 0;
            }

            using (var tx = new Transaction(d, "purge *_old"))
            {
                tx.Start();
                d.Delete(kill);
                tx.Commit();
            }

            LogHelper.Information($"PurgeOldFamilies: deleted {kill.Count} families");
            return kill.Count;
        }

        // ───────────────────────── SNAPSHOT / RESTORE ─────────────────────────
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
                    case StorageType.ElementId: v = p.AsValueString(); break; // ⬅️ строка!
                }
                if (v != null)
                    dict[p.Definition.Name] = (p.StorageType, v);
            }
            return dict;
        }

        private static void Restore(FamilyInstance inst,
            IReadOnlyDictionary<string, (StorageType st, object val)> snap)
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
                        case StorageType.ElementId: p.SetValueString((string)data.val); break; // ⬅️
                    }
                }
                catch { /* ignore */ }
            }
        }

        // ───────────────────────── UTILS & comparers ─────────────────────────
        protected static string MakeUnique(Document d, string baseName)
        {
            var names = new HashSet<string>(
                new FilteredElementCollector(d).OfClass(typeof(Family)).Select(f => f.Name));
            string n = baseName;
            int i = 1;
            while (names.Contains(n)) n = baseName + "_" + (i++);
            return n;
        }

        private class OverwriteOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool inUse, out bool overwrite)
            {
                overwrite = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family shared, bool inUse, out FamilySource src, out bool ov)
            {
                src = FamilySource.Family;
                ov = true;
                return true;
            }
        }

        internal class FamCmp : IEqualityComparer<Family>
        {
            public bool Equals(Family a, Family b) => a?.Id == b?.Id;
            public int GetHashCode(Family f) => f.Id.IntegerValue;
        }

        internal class IdCmp : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId a, ElementId b) => a.IntegerValue == b.IntegerValue;
            public int GetHashCode(ElementId id) => id.IntegerValue;
        }
    }
}
