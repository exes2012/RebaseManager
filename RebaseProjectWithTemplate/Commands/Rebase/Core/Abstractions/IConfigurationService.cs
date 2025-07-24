namespace RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;

public interface IConfigurationService
{
    string GetAiProvider();
    string GetGrokApiKey();
    string GetGrokApiUrl();
    string GetGeminiApiKey();
}