using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Configuration;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Gemini
{
    public class GeminiApiService : IAiService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public GeminiApiService()
        {
            _httpClient = new HttpClient();
            _apiKey = ConfigurationService.GetGeminiApiKey();
        }

        public async Task<TResponse> GetMappingAsync<TResponse>(IPromptStrategy strategy, PromptData data)
        {
            var systemPrompt = strategy.GetSystemPrompt();
            var userPrompt = strategy.CreateUserPrompt(data);
            var fullPrompt = $"{systemPrompt}\n\n{userPrompt}";

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new 
                    {
                        role = "user",
                        parts = new[] { new { text = fullPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    response_mime_type = "application/json",
                    response_schema = new 
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                Old = new { type = "STRING" },
                                New = new { type = "STRING" },
                                TypeMatches = new
                                {
                                    type = "ARRAY",
                                    items = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            OldType = new { type = "STRING" },
                                            NewType = new { type = "STRING" }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    thinkingConfig = new
                    {
                    thinkingBudget = 0
                }
                },
            };

            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(requestBody, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Gemini API error: {response.StatusCode} - {responseContent}");

                var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent, jsonOptions);
                var mappedJson = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text;

                if (string.IsNullOrEmpty(mappedJson))
                    throw new Exception("Failed to extract JSON from Gemini API response.");

                return JsonSerializer.Deserialize<TResponse>(mappedJson, jsonOptions);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to execute Gemini API request: {ex.Message}", ex);
            }
        }
    }

    // Helper classes for parsing Gemini's response
    internal class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate> Candidates { get; set; }
    }

    internal class Candidate
    {
        [JsonPropertyName("content")]
        public Content Content { get; set; }
    }

    internal class Content
    {
        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; }
    }

    internal class Part
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }
}