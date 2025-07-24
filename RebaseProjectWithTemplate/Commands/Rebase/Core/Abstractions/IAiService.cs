namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

public interface IAiService
{
    Task<TResponse> GetMappingAsync<TResponse>(IPromptStrategy strategy, PromptData data);
}