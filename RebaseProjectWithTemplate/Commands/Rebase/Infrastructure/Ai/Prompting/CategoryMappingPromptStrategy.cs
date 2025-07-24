using System.Text.Json;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.IO;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;

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

        var oldFamiliesJson = JsonSerializer.Serialize(promptData.OldFamilies.Select(f =>
            new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));
        var newFamiliesJson = JsonSerializer.Serialize(promptData.NewFamilies.Select(f =>
            new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));

        return
            $"{Environment.NewLine}{Environment.NewLine}OLD_FAMILIES:{Environment.NewLine}{oldFamiliesJson}{Environment.NewLine}{Environment.NewLine}NEW_FAMILIES:{Environment.NewLine}{newFamiliesJson}";
    }
}