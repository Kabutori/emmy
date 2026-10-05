using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace DalamudEmmy.Provider;

public sealed class DeepSeekProvider : IProvider, IDisposable
{
    private readonly string apiKey;
    private readonly string model;
    private readonly HttpClient httpClient;
    private readonly IPluginLog? log;

    public DeepSeekProvider(string apiKey, string model = "deepseek-flash", IPluginLog? log = null)
    {
        this.apiKey = apiKey;
        this.model = model;
        this.httpClient = new HttpClient();
        this.log = log;
    }

    public string Name => "DeepSeek";

    public async Task<Emmy.Core.ProviderResult> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(this.apiKey))
        {
            return new(false, Error: "No API key configured");
        }

        var requestBody = new
        {
            model = this.model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            max_tokens = 500,
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        this.httpClient.DefaultRequestHeaders.Clear();
        this.httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {this.apiKey}");

        try
        {
            using var response = await this.httpClient.PostAsync("https://api.deepseek.com/chat/completions", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);

            if (responseObj.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentText))
                {
                    this.log?.Information("DeepSeek response received successfully");
                    return contentText.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(contentText.GetString()) ? new(true, contentText.GetString()!) : new(false, Error: "Empty response");
                }
            }

            this.log?.Warning("Invalid DeepSeek response format");
            return new(false, Error: "Invalid response format");
        }
        catch (HttpRequestException ex)
        {
            this.log?.Error(ex, "DeepSeek HTTP error");
            return new(false, Error: $"HTTP {(int?)ex.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return new(false, Error: "Cancelled or timed out");
        }
        catch (Exception ex)
        {
            this.log?.Error(ex, "DeepSeek request error");
            return new(false, Error: "Provider request failed");
        }
    }
    public void Dispose() => this.httpClient.Dispose();
}
