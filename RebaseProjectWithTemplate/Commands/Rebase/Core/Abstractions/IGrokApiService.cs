using System.Threading.Tasks;

namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions
{
    public interface IGrokApiService
    {
        Task<TResponse> ExecuteChatCompletionAsync<TResponse>(IPromptStrategy strategy, PromptData data);
    }
}
