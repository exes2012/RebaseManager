using System.Text.Json;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.IO;
using RebaseProjectWithTemplate.Commands.Rebase.Models;
using System.Collections.Generic;
using System.Linq;
using RebaseProjectWithTemplate.Commands.Export.Models;
using System;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Prompts
{
    public class CategoryMappingPromptData : PromptData
    {
        public List<FamilyData> OldFamilies { get; set; }
        public List<FamilyData> NewFamilies { get; set; }
    }

    public class CategoryMappingPromptStrategy : IPromptStrategy
    {
        public string GetSystemPrompt()
        {
            return PromptLoaderService.LoadPrompt("CategoryMapping_System.txt");
        }

        public string CreateUserPrompt(PromptData data)
        {
            if (!(data is CategoryMappingPromptData promptData))
                throw new ArgumentException("Invalid data type for this prompt strategy", nameof(data));

            var oldFamiliesJson = JsonSerializer.Serialize(promptData.OldFamilies.Select(f => new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));
            var newFamiliesJson = JsonSerializer.Serialize(promptData.NewFamilies.Select(f => new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));

            return $"\n\nOLD_FAMILIES:\n{oldFamiliesJson}\n\nNEW_FAMILIES:\n{newFamiliesJson}";
        }
    }
}
