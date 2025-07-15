using System.Text.Json;
using RebaseProjectWithTemplate.Core.Abstractions;
using RebaseProjectWithTemplate.Infrastructure.IO;

namespace RebaseProjectWithTemplate.Core.Prompts;

public class ViewTemplateMappingPromptData : PromptData
{
    public List<string> SourceTemplates { get; set; }
    public List<string> TargetTemplates { get; set; }
}

public class ViewTemplateMappingPromptStrategy : IPromptStrategy
{
    public string GetSystemPrompt()
    {
        return PromptLoaderService.LoadPrompt("ViewTemplateMapping_System.txt");
    }

    public string CreateUserPrompt(PromptData data)
    {
        if (!(data is ViewTemplateMappingPromptData promptData))
            throw new ArgumentException("Invalid data type for this prompt strategy", nameof(data));

        var userPromptTemplate = PromptLoaderService.LoadPrompt("ViewTemplateMapping_User.txt");
        var sourceJson = JsonSerializer.Serialize(promptData.SourceTemplates);
        var targetJson = JsonSerializer.Serialize(promptData.TargetTemplates);

        return string.Format(userPromptTemplate, sourceJson, targetJson);
    }
}