using System.Threading.Tasks;
using RebaseProjectWithTemplate.Core.Abstractions;

namespace RebaseProjectWithTemplate.Core.Abstractions
{
    public interface IGrokApiService
    {
        Task<TResponse> ExecuteChatCompletionAsync<TResponse>(IPromptStrategy strategy, PromptData data);
    }
}
