using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using RebaseProjectWithTemplate.Commands.Rebase.Core.Abstractions;
using RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Configuration;
using RebaseProjectWithTemplate.Commands.Rebase.DTOs;

namespace RebaseProjectWithTemplate.Commands.Rebase.Infrastructure.Ai;

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
        var fullPrompt = $"{systemPrompt}{Environment.NewLine}{Environment.NewLine}{userPrompt}";

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}"; ;

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
                response_schema = GetResponseSchema<TResponse>(),
                thinkingConfig = new
                {
                    thinkingBudget = 0
                }
            },
        };

        try
        {
            var jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };

            var json = JsonConvert.SerializeObject(requestBody, jsonSettings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini API error: {response.StatusCode} - {responseContent}");

            var geminiResponse = JsonConvert.DeserializeObject<GeminiResponse>(responseContent, jsonSettings);
            var mappedJson = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text;

            if (string.IsNullOrEmpty(mappedJson))
                throw new Exception("Failed to extract JSON from Gemini API response.");

            return JsonConvert.DeserializeObject<TResponse>(mappedJson, jsonSettings);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to execute Gemini API request: {ex.Message}", ex);
        }
    }

    private object GetResponseSchema<TResponse>()
    {
        var responseType = typeof(TResponse);

        // Schema for ViewTemplateMappingResponse
        if (responseType.Name == "ViewTemplateMappingResponse")
        {
            return new
            {
                type = "OBJECT",
                properties = new
                {
                    mappings = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                sourceTemplate = new { type = "STRING" },
                                targetTemplate = new { type = "STRING" }
                            }
                        }
                    },
                    unmapped = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                sourceTemplate = new { type = "STRING" },
                                reason = new { type = "STRING" }
                            }
                        }
                    }
                }
            };
        }

        // Default schema for List<MappingResult> and other types
        return new
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
        };
    }
}

// Helper classes for parsing Gemini's response
internal class GeminiResponse
{
    [JsonProperty("candidates")] public List<Candidate> Candidates { get; set; }
}

internal class Candidate
{
    [JsonProperty("content")] public Content Content { get; set; }
}

internal class Content
{
    [JsonProperty("parts")] public List<Part> Parts { get; set; }
}

internal class Part
{
    [JsonProperty("text")] public string Text { get; set; }
}