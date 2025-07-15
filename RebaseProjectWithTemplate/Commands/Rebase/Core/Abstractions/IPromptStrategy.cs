namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions
{
    public abstract class PromptData { }

    public interface IPromptStrategy
    {
        string GetSystemPrompt();
        string CreateUserPrompt(PromptData data);
    }
}
