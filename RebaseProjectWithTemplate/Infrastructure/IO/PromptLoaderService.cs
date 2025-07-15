using System.IO;
using System.Reflection;

namespace RebaseProjectWithTemplate.Infrastructure.IO;

public static class PromptLoaderService
{
    public static string LoadPrompt(string promptName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"RebaseProjectWithTemplate.Prompts.{promptName}";

        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                throw new Exception(
                    $"Prompt resource '{promptName}' not found. Make sure it is set as an Embedded Resource.");
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}