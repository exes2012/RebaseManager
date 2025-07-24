
using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Reporting;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.UI;
using RebaseProjectWithTemplate.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class CategoryRebaseOrchestrator
    {
        private readonly IFamilyRepository _familyRepo;
        private readonly IAiService _aiService;
        private readonly CategoryRebaseReportBuilder _reportBuilder;
        private readonly CategoryRebaseReportService _reportService;
        private readonly string _oldSuffix = "_rebase_old";

        public CategoryRebaseOrchestrator(IFamilyRepository repo, IAiService aiService)
        {
            _familyRepo = repo;
            _aiService = aiService;
            _reportBuilder = new CategoryRebaseReportBuilder();
            _reportService = new CategoryRebaseReportService();
        }

        public async Task RebaseAsync(Document document, BuiltInCategory bic, IPromptStrategy strategy, IProgress<string> progress)
        {
            LogHelper.Information($"[{bic}] Starting rebase process...");
            progress?.Report($"[{bic}] Collecting family data...");

            var srcData = _familyRepo.CollectFamilyData(bic);
            var tplData = _familyRepo.CollectFamilyData(bic, fromTemplate: true);
            LogHelper.Information($"[{bic}] Found {srcData.Count} source families and {tplData.Count} template families.");

            if (srcData.Count == 0 || tplData.Count == 0)
            {
                LogHelper.Warning($"[{bic}] No families found in source or template. Skipping rebase.");
                progress?.Report($"[{bic}] No families to rebase. Skipped.");
                return;
            }

            var exactNames = new HashSet<string>(srcData.Select(f => f.FamilyName).Intersect(tplData.Select(f => f.FamilyName)));
            var looseSrc = srcData.Where(f => !exactNames.Contains(f.FamilyName)).ToList();
            LogHelper.Information($"[{bic}] Found {exactNames.Count} families with exact name matches and {looseSrc.Count} loose families.");

            progress?.Report($"[{bic}] Getting AI mapping for {looseSrc.Count} families...");
            var mapping = await GetAiMappingAsync(looseSrc, tplData, strategy);
            LogHelper.Information($"[{bic}] AI mapping received with {mapping.Count} results.");

            var oldData = looseSrc.ToDictionary(f => f.FamilyName, f => f);

            progress?.Report($"[{bic}] Renaming old families...");
            _familyRepo.RenameFamilies(bic, oldData.Keys, _oldSuffix, progress);

            progress?.Report($"[{bic}] Loading new families...");
            _familyRepo.LoadFamilies(bic, exactNames);

            var typesToCopy = tplData
                .Where(f => !exactNames.Contains(f.FamilyName))
                .SelectMany(f => f.Types)
                .Select(t => new ElementId(Convert.ToInt32(t.TypeId)))
                .Distinct()
                .ToList();

            progress?.Report($"[{bic}] Copying {typesToCopy.Count} new types from template...");
            _familyRepo.CopyTemplateTypes(typesToCopy, progress);

            var mappedNewNames = mapping
                .Where(m => !string.IsNullOrEmpty(m.New) && m.New != "No Match")
                .Select(m => m.New)
                .Distinct()
                .ToList();

            var newData = _familyRepo.CollectFamilyData(bic).Where(f => mappedNewNames.Contains(f.FamilyName)).ToDictionary(f => f.FamilyName, f => f);

            progress?.Report($"[{bic}] Switching instances...");
            var idMap = _familyRepo.BuildIdMap(oldData, newData, mapping);
            _familyRepo.SwitchInstances(bic, idMap);

            progress?.Report($"[{bic}] Purging obsolete families...");
            _familyRepo.PurgeFamilies(bic, _oldSuffix);

            // Generate Excel report
            progress?.Report($"[{bic}] Generating Excel report...");
            var report = _reportBuilder.BuildReport(
                document.Title,
                bic,
                srcData,
                tplData,
                exactNames,
                mapping,
                idMap);

            _reportService.GenerateExcelReport(report);
            LogHelper.Information($"[{bic}] Excel report generated successfully.");

            LogHelper.Information($"[{bic}] Rebase process completed.");
        }

        private async Task<List<MappingResult>> GetAiMappingAsync(List<FamilyData> looseSrc, List<FamilyData> templateData, IPromptStrategy strategy)
        {
            var promptData = new RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting.CategoryMappingPromptData
            {
                OldFamilies = looseSrc,
                NewFamilies = templateData
            };

            return await _aiService.GetMappingAsync<List<MappingResult>>(strategy, promptData);
        }
    }
}
