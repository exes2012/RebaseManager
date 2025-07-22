using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Export.Models;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Prompts;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using System; 
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class TitleBlocksRebaseService
    {
        private const string OldSuffix = "_rebase_old";
        private readonly IAiService _aiService;

        public TitleBlocksRebaseService(IAiService aiService) => _aiService = aiService;

        public async Task RebaseTitleBlocksAsync(Document sourceDoc,
            Document templateDoc,
            IProgress<string> progress)
        {
            progress.Report("Collecting title‑block data…");
            var templateTB = CollectTitleBlocks(templateDoc, false);
            var sourceTB = CollectTitleBlocks(sourceDoc, false);

            if (!templateTB.Any() || !sourceTB.Any())
            {
                progress.Report("No title blocks found – rebase skipped.");
                return;
            }

            var masterValues = ReadSourceMasterParams(sourceDoc);
            /* 1️⃣  AI‑mapping – это длительный I/O, делаем его до транзакций */
            progress.Report("Generating AI mapping…");
            var mapping = await GetAiMappingAsync(sourceDoc, templateDoc);   // <<< await здесь безопасен

            /* 2️⃣  Теперь открываем TransactionGroup и меняем модель без await‑ов */
            using (var tg = new TransactionGroup(sourceDoc, "Title‑block rebase"))
            {
                tg.Start();

                progress.Report("Renaming conflicting title‑block families…");
                RenameAllSourceFamilies(sourceDoc);

                progress.Report("Copying template title‑blocks…");
                CopyAllTitleBlocks(templateDoc, sourceDoc);

                progress.Report("Replacing title‑blocks on sheets…");
                ReplaceTitleBlocks(sourceDoc, mapping);

                progress.Report("Sync TB instance parameters…");
                ApplyMasterParamsToAll(sourceDoc, masterValues);

                progress.Report("Purging obsolete title‑blocks…");
                DeleteObsoleteFamilies(sourceDoc);

                tg.Assimilate();
            }
        }

        private void ApplyMasterParamsToAll(Document doc,
            (string gridNestedName, int? nfcValue) master)
        {
            if (master.gridNestedName == null && master.nfcValue == null) return;

            // ищем nested‑тип для Gridd Height
            ElementId gridTypeId = ElementId.InvalidElementId;
            if (master.gridNestedName != null)
            {
                gridTypeId = new FilteredElementCollector(doc)
                                 .OfClass(typeof(FamilySymbol))
                                 .Cast<FamilySymbol>()
                                 .FirstOrDefault(fs => fs.Name == master.gridNestedName)?.Id
                             ?? ElementId.InvalidElementId;
            }

            var ids = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToElementIds();

            using var tx = new Transaction(doc, "Apply TB master parameters");
            tx.Start();

            foreach (var id in ids)
            {
                var inst = doc.GetElement(id) as FamilyInstance;
                if (inst == null) continue;

                // Not for Construction
                if (master.nfcValue.HasValue)
                {
                    var p = inst.LookupParameter("Not for Construction");
                    if (p != null && !p.IsReadOnly)
                        p.Set(master.nfcValue.Value);
                }

                // Gridd Height
                if (gridTypeId != ElementId.InvalidElementId)
                {
                    var p = inst.LookupParameter("Gridd Height");
                    if (p != null && !p.IsReadOnly &&
                        p.StorageType == StorageType.ElementId &&
                        p.AsElementId() != gridTypeId)
                    {
                        p.Set(gridTypeId);
                    }
                }
            }

            doc.Regenerate();
            tx.Commit();
        }

        #region Rename conflicts
        private void RenameAllSourceFamilies(Document doc)
        {
            var families = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamilyEqualityComparer())   // ← правильный компаратор
                .Where(f => !f.Name.EndsWith(OldSuffix))
                .ToList();

            if (!families.Any()) return;

            using var tx = new Transaction(doc, "Mark source TB families as old");
            tx.Start();
            foreach (var fam in families)
                fam.Name = MakeUnique(doc, fam.Name + OldSuffix);
            tx.Commit();
        }
        #endregion

        #region AI mapping
        private async Task<List<MappingResult>> GetAiMappingAsync(Document source, Document template)
        {
            var srcData = CollectTitleBlocks(source, true);
            var tplData = CollectTitleBlocks(template, false);

            var promptData = new TitleBlockMappingPromptData
            {
                OldFamilies = srcData,
                NewFamilies = tplData
            };

            var strategy = new TitleBlockMappingPromptStrategy();
            return await _aiService.GetMappingAsync<List<MappingResult>>(strategy, promptData);
        }
        #endregion

        #region Copy symbols
        private void CopyAllTitleBlocks(Document template, Document dest)
        {
            var symbolIds = new FilteredElementCollector(template)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .ToElementIds();

            if (!symbolIds.Any()) return;

            using var tx = new Transaction(dest, "Copy TB symbols from template");
            tx.Start();
            var opts = new CopyPasteOptions();
            opts.SetDuplicateTypeNamesHandler(new DestTypesHandler());
            ElementTransformUtils.CopyElements(template, symbolIds, dest, null, opts);
            tx.Commit();
        }
        #endregion

        #region Replace on sheets
        private void ReplaceTitleBlocks(Document doc, List<MappingResult> mapping)
        {
            // lookup: [FamilyName][TypeName] → symbol
            var tbLookup = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(s => !s.Family.Name.EndsWith(OldSuffix))
                .GroupBy(s => s.Family.Name)
                .ToDictionary(g => g.Key, g => g.ToDictionary(t => t.Name, t => t));

            // «Снимок» всех экземпляров: список Id, не живой FEC
            var allTbIds = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToElementIds();    // ← материализован, безопасен для изменений

            using var tx = new Transaction(doc, "Swap title‑block instances");
            tx.Start();

            foreach (var id in allTbIds)
            {
                var inst = doc.GetElement(id) as FamilyInstance;
                if (inst == null) continue;

                var sym = inst.Symbol;
                var famName = TrimOldSuffix(sym.Family.Name);
                var typeName = sym.Name;

                var famMap = mapping.FirstOrDefault(m => m.Old == famName);
                if (famMap == null || famMap.New == "No Match") continue;

                var typeMap = famMap.TypeMatches.FirstOrDefault(t => t.OldType == typeName);
                if (typeMap == null || typeMap.NewType == "No Match") continue;

                if (tbLookup.TryGetValue(famMap.New, out var dict) &&
                    dict.TryGetValue(typeMap.NewType, out var newSym) &&
                    newSym.Id != sym.Id)
                {
                    inst.Symbol = newSym;   // изменение больше не ломает итератор
                }
            }

            doc.Regenerate();   // один regen вместо сотен
            tx.Commit();
        }
        #endregion

        private (string gridNestedName, int? nfcValue) ReadSourceMasterParams(Document doc)
        {
            var master = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .FirstOrDefault(i =>
                    !i.Symbol.Family.Name.Contains("Cover"));

            if (master == null) return (null, null);

            string nestedName = null;
            var gridParam = master.LookupParameter("Gridd Height");
            if (gridParam?.StorageType == StorageType.ElementId)
            {
                var nestedSym = doc.GetElement(gridParam.AsElementId()) as FamilySymbol;
                nestedName = nestedSym?.Name;
            }

            int? nfc = master.LookupParameter("Not for Construction")?.AsInteger();

            return (nestedName, nfc);
        }

        #region Purge obsolete
        private void DeleteObsoleteFamilies(Document doc)
        {
            // 1. Символы, у которых есть экземпляры на листах
            var usedSymbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Select(i => i.Symbol.Id)
                .ToHashSet(new ElementIdEqualityComparer());

            // 2. Все «старые» семьи — берем через символы, затем Distinct
            var oldFamilies = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(s => s.Family)
                .Distinct(new FamilyEqualityComparer())      // тот же компаратор
                .Where(f => f.Name.EndsWith(OldSuffix))      // помечены как _rebase_old
                .ToList();

            // 3. Удаляем семейство, если ни один его тип больше не используется
            var familiesToDelete = oldFamilies
                .Where(f => !f.GetFamilySymbolIds().Any(id => usedSymbols.Contains(id)))
                .Select(f => f.Id)
                .ToList();

            if (!familiesToDelete.Any()) return;

            using var tx = new Transaction(doc, "Delete obsolete TB families");
            tx.Start();
            doc.Delete(familiesToDelete);
            tx.Commit();
        }
        #endregion

        #region Helpers
        private static string TrimOldSuffix(string name) =>
            name.EndsWith(OldSuffix)
                ? name.Substring(0, name.Length - OldSuffix.Length)
                : name;

        private static string MakeUnique(Document doc, string baseName)
        {
            string name = baseName;
            int i = 1;
            var names = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Select(f => f.Name));

            while (names.Contains(name))
                name = $"{baseName}_{i++}";

            return name;
        }

        private List<FamilyData> CollectTitleBlocks(Document doc, bool stripSuffix)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .GroupBy(s => s.Family.Name)
                .Select(g =>
                {
                    var famName = stripSuffix ? TrimOldSuffix(g.Key) : g.Key;
                    return new FamilyData
                    {
                        FamilyName = famName,
                        FamilyId = g.First().Family.Id.IntegerValue,
                        Types = g.Select(t => new FamilyTypeData
                        {
                            TypeName = t.Name,
                            TypeId = t.Id.IntegerValue
                        })
                                      .ToList()
                    };
                }).ToList();
        }
        #endregion
    }

    internal class DestTypesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(
            DuplicateTypeNamesHandlerArgs args) =>
            DuplicateTypeAction.UseDestinationTypes;
    }

    internal class ElementIdEqualityComparer : IEqualityComparer<ElementId>
    {
        public bool Equals(ElementId x, ElementId y) => x.IntegerValue == y.IntegerValue;
        public int GetHashCode(ElementId obj) => obj.IntegerValue;
    }

    internal class FamilyEqualityComparer : IEqualityComparer<Family>
    {
        public bool Equals(Family x, Family y) =>
            x?.Id.IntegerValue == y?.Id.IntegerValue;

        public int GetHashCode(Family obj) => obj.Id.IntegerValue;
    }
}

