using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai.Prompting;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Services;

public class ViewTemplateRebaseOrchestrator
{
    private readonly IAiService _aiService;
    private readonly IViewTemplateRepository _repo;

    public ViewTemplateRebaseOrchestrator(IViewTemplateRepository repo, IAiService aiService)
    {
        _repo = repo;
        _aiService = aiService;
    }

    public async Task RebaseAsync(IPromptStrategy strategy)
    {
        var sourceViewsWithTemplates = _repo.CollectViewPlansWithTemplates();
        if (sourceViewsWithTemplates.Count == 0) return;

        var sourceTemplates = _repo.CollectViewTemplateNames();
        var targetTemplates = _repo.CollectViewTemplateNames(true);

        var mappingResponse = await GetAiMappingAsync(sourceTemplates, targetTemplates, strategy);

        _repo.RemoveViewTemplatesFromViews(sourceViewsWithTemplates.Select(v => v.Id).ToList());
        _repo.DeleteUnmappedViewTemplates(mappingResponse);
        _repo.CopyViewTemplatesFromTemplate(targetTemplates);

        var originalTemplateNames = sourceViewsWithTemplates.ToDictionary(v => v.Id, v => v.Name);
        _repo.ApplyMappedViewTemplates(originalTemplateNames, mappingResponse.Mappings);
    }

    private async Task<ViewTemplateMappingResponse> GetAiMappingAsync(List<string> sourceTemplates,
        List<string> targetTemplates, IPromptStrategy strategy)
    {
        var promptData = new ViewTemplateMappingPromptData
        {
            SourceTemplates = sourceTemplates,
            TargetTemplates = targetTemplates
        };

        return await _aiService.GetMappingAsync<ViewTemplateMappingResponse>(strategy, promptData);
    }
}