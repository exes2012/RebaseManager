using Newtonsoft.Json;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Models;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.IO;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;

public class TitleBlockMappingPromptData : PromptData
{
    public List<FamilyData> OldFamilies { get; set; }
    public List<FamilyData> NewFamilies { get; set; }
}

public class TitleBlockMappingPromptStrategy : IPromptStrategy
{
    public string GetSystemPrompt()
    {
        return PromptLoaderService.LoadPrompt("TitleBlockMapping_System.txt");
    }

    public string CreateUserPrompt(PromptData data)
    {
        if (!(data is TitleBlockMappingPromptData promptData))
            throw new ArgumentException("Invalid data type for this prompt strategy", nameof(data));

        var userPromptTemplate = PromptLoaderService.LoadPrompt("TitleBlockMapping_User.txt");

        var oldFamiliesJson = JsonConvert.SerializeObject(promptData.OldFamilies.Select(f =>
            new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));
        var newFamiliesJson = JsonConvert.SerializeObject(promptData.NewFamilies.Select(f =>
            new { f.FamilyName, Types = f.Types.Select(t => t.TypeName).ToList() }));

        return string.Format(userPromptTemplate, oldFamiliesJson, newFamiliesJson);
    }
}