using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Configuration;

public static class ConfigurationService
{
    private static AppSettings _settings;

    public static string GetAiProvider()
    {
        if (_settings == null) LoadSettings();
        return _settings.AiProvider;
    }

    public static string GetGrokApiKey()
    {
        if (_settings == null) LoadSettings();

        if (string.IsNullOrEmpty(_settings?.GrokApiKey) || _settings.GrokApiKey == "YOUR_GROK_API_KEY_HERE")
            throw new Exception("Grok API key not configured. Please set your API key in appsettings.json file.");

        return _settings.GrokApiKey;
    }

    public static string GetGrokApiUrl()
    {
        if (_settings == null) LoadSettings();

        if (string.IsNullOrEmpty(_settings?.GrokApiUrl))
            throw new Exception("Grok API URL not configured. Please set your API URL in appsettings.json file.");

        return _settings.GrokApiUrl;
    }

    public static string GetGeminiApiKey()
    {
        if (_settings == null) LoadSettings();

        if (string.IsNullOrEmpty(_settings?.GeminiApiKey) || _settings.GeminiApiKey == "YOUR_GEMINI_API_KEY_HERE")
            throw new Exception("Gemini API key not configured. Please set your API key in appsettings.json file.");

        return _settings.GeminiApiKey;
    }

    private static void LoadSettings()
    {
        try
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
            var settingsPath = Path.Combine(assemblyDirectory, "appsettings.json");

            if (!File.Exists(settingsPath))
                throw new Exception(
                    $"Configuration file not found: {settingsPath}. Please copy appsettings.example.json to appsettings.json and set your API key.");

            var json = File.ReadAllText(settingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load configuration: {ex.Message}", ex);
        }
    }
}

public class AppSettings
{
    public string AiProvider { get; set; }
    public string GrokApiKey { get; set; }
    public string GrokApiUrl { get; set; }
    public string GeminiApiKey { get; set; }
}