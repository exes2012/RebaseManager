using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Configuration;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Grok
{
    public class GrokApiService : IAiService
    {
        private readonly string _baseUrl;
        private readonly HttpClient _httpClient;

        public GrokApiService()
        {
            _httpClient = new HttpClient();
            var apiKey = ConfigurationService.GetGrokApiKey();
            _baseUrl = ConfigurationService.GetGrokApiUrl();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<TResponse> GetMappingAsync<TResponse>(IPromptStrategy strategy, PromptData data)
        {
            var systemPrompt = strategy.GetSystemPrompt();
            var userPrompt = strategy.CreateUserPrompt(data);

            var request = new GrokRequest
            {
                Model = "grok-3-mini",
                Stream = false,
                Temperature = 0,
                Messages = new List<GrokMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_baseUrl, content);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Grok API error: {response.StatusCode} - {responseContent}");

                var grokResponse = JsonSerializer.Deserialize<GrokResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (grokResponse?.Choices == null || grokResponse.Choices.Count == 0)
                    throw new Exception("Invalid response from Grok API: no choices returned");

                var rawContent = grokResponse.Choices[0].Message.Content;
                var cleanJson = ExtractJson(rawContent);

                if (string.IsNullOrEmpty(cleanJson))
                    throw new Exception("Failed to extract JSON from Grok API response.");

                return JsonSerializer.Deserialize<TResponse>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to execute Grok API request: {ex.Message}", ex);
            }
        }

        private string ExtractJson(string text)
        {
            var match = Regex.Match(text, @"\{.*\}", RegexOptions.Singleline);
            return match.Success ? match.Value : string.Empty;
        }
    }
}
