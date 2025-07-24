using System.IO;
using System.Reflection;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.IO;

public static class PromptLoaderService
{
    public static string LoadPrompt(string promptName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Find the resource name that ends with the prompt name, accounting for folder structure.
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(str => str.EndsWith(promptName));

        if (resourceName == null)
            throw new FileNotFoundException(
                $"Prompt resource '{promptName}' not found. Make sure it is set as an Embedded Resource and the name is correct.");

        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                // This should theoretically not happen if resourceName is not null, but it's good practice to check.
                throw new Exception($"Could not load the prompt resource stream for '{promptName}'.");
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}