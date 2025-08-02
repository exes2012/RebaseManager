using Newtonsoft.Json;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.IO;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;

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
        var sourceJson = JsonConvert.SerializeObject(promptData.SourceTemplates);
        var targetJson = JsonConvert.SerializeObject(promptData.TargetTemplates);

        return string.Format(userPromptTemplate, sourceJson, targetJson);
    }
}