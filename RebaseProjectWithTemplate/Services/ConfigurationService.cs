using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RebaseProjectWithTemplate.Services
{
    public static class ConfigurationService
    {
        private static AppSettings _settings;

        public static string GetGrokApiKey()
        {
            if (_settings == null)
            {
                LoadSettings();
            }

            if (string.IsNullOrEmpty(_settings?.GrokApiKey) || _settings.GrokApiKey == "YOUR_GROK_API_KEY_HERE")
            {
                throw new Exception("Grok API key not configured. Please set your API key in appsettings.json file.");
            }

            return _settings.GrokApiKey;
        }

        private static void LoadSettings()
        {
            try
            {
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                var settingsPath = Path.Combine(assemblyDirectory, "appsettings.json");

                if (!File.Exists(settingsPath))
                {
                    throw new Exception($"Configuration file not found: {settingsPath}. Please copy appsettings.example.json to appsettings.json and set your API key.");
                }

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
        public string GrokApiKey { get; set; }
    }
}
