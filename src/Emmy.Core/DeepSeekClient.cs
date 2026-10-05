using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Emmy.Core;

public sealed class DeepSeekClient(HttpClient http)
{
    public async Task<ProviderResult> CompleteAsync(string key, string model, string system, object[] messages,
        CancellationToken cancellationToken, bool json = false)
    {
        if(string.IsNullOrWhiteSpace(key)) return new(false, Error:"Kein API-Schlüssel gespeichert");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Post,"https://api.deepseek.com/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",key);
            var allMessages = new List<object>{new {role="system",content=system}}; allMessages.AddRange(messages);
            var body = new Dictionary<string,object> { ["model"]=model, ["messages"]=allMessages, ["max_tokens"]=800, ["thinking"]=new {type="disabled"} };
            if(json) body["response_format"] = new {type="json_object"};
            request.Content = new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
            using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode) return new(false,Error:$"DeepSeek HTTP {(int)response.StatusCode}");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream,cancellationToken:timeout.Token);
            var root = document.RootElement;
            if(!root.TryGetProperty("choices",out var choices) || choices.GetArrayLength()==0 ||
                !choices[0].TryGetProperty("message",out var message) || !message.TryGetProperty("content",out var content) ||
                content.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString())) return new(false,Error:"Modell lieferte keinen Antworttext");
            var input=0; var output=0;
            if(root.TryGetProperty("usage",out var usage)) { if(usage.TryGetProperty("prompt_tokens",out var i)) input=i.GetInt32(); if(usage.TryGetProperty("completion_tokens",out var o)) output=o.GetInt32(); }
            return new(true,content.GetString()!,InputTokens:input,OutputTokens:output);
        }
        catch(OperationCanceledException) { return new(false,Error:cancellationToken.IsCancellationRequested ? "Abgebrochen" : "Modelltimeout"); }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException or FormatException) { return new(false,Error:"Modellantwort oder Verbindung ungültig"); }
    }
    public static object VisionMessage(string text, Frame frame) => new {role="user", content=new object[]{ new {type="text",text},new {type="image_url",image_url=new {url=frame.DataUrl,detail="low"}} }};
}
