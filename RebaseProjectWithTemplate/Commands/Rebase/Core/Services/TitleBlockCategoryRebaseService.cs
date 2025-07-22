using Autodesk.Revit.DB;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Prompts;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using RebaseProjectWithTemplate.Commands.Export.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services
{
    public class TitleBlockCategoryRebaseService : CategoryRebaseService
    {
        public TitleBlockCategoryRebaseService(IAiService ai)
            : base(ai, BuiltInCategory.OST_TitleBlocks) { }

        protected override async Task<List<MappingResult>> GetAiMappingAsync(
            List<FamilyData> looseSrc, List<FamilyData> templateData)
        {
            var prompt = new CategoryMappingPromptData
            {
                OldFamilies = looseSrc,
                NewFamilies = templateData
            };

            var strategy = new CategoryMappingPromptStrategy();
            return await _ai.GetMappingAsync<List<MappingResult>>(strategy, prompt);
        }
    }
}