using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RebaseProjectWithTemplate.Models;

namespace RebaseProjectWithTemplate.Services
{
    public class GrokApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://api.x.ai/v1/chat/completions";

        public GrokApiService()
        {
            _httpClient = new HttpClient();
            var apiKey = ConfigurationService.GetGrokApiKey();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<ViewTemplateMappingResponse> MapViewTemplatesAsync(
            List<string> sourceTemplates,
            List<string> targetTemplates)
        {
            var prompt = PromptService.CreateViewTemplateMappingPrompt(sourceTemplates, targetTemplates);

            var request = new GrokRequest
            {
                Model = "grok-3-mini",
                Stream = false,
                Temperature = 0,
                Messages = new List<GrokMessage>
                {
                    new GrokMessage { Role = "system", Content = PromptService.GetViewTemplateMappingSystemPrompt() },
                    new GrokMessage { Role = "user",   Content = prompt }
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(BaseUrl, content);

                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Grok API error: {response.StatusCode} - {responseContent}");

                var grokResponse = JsonSerializer.Deserialize<GrokResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (grokResponse?.Choices == null || grokResponse.Choices.Count == 0)
                    throw new Exception("Invalid response from Grok API: no choices returned");

                var mappingJson = grokResponse.Choices[0].Message.Content;

                // Вырезаем чистый JSON, если модель добавила префикс / постфикс
                var jsonStart = mappingJson.IndexOf('{');
                var jsonEnd = mappingJson.LastIndexOf('}') + 1;
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                    mappingJson = mappingJson.Substring(jsonStart, jsonEnd - jsonStart);

                var result = JsonSerializer.Deserialize<ViewTemplateMappingResponse>(mappingJson, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to map view templates: {ex.Message}", ex);
            }
        }



        public void Dispose() => _httpClient?.Dispose();
    }


}
