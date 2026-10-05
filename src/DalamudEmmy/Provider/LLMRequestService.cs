using Dalamud.Plugin.Services;

namespace DalamudEmmy.Provider;

public sealed class LLMRequestService : IDisposable
{
    private readonly Configuration configuration;
    private readonly IPluginLog log;
    private IProvider? currentProvider;
    private string fingerprint = "";
    private readonly SemaphoreSlim slots = new(1);
    private readonly Emmy.Core.RequestBudget budget = new();
    public string LastError { get; private set; } = "";

    public LLMRequestService(Configuration configuration, IPluginLog log)
    {
        this.configuration = configuration;
        this.log = log;
        this.InitializeProvider();
    }

    public Task<Emmy.Core.ProviderResult> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return this.GetResponseAsync(prompt, "reply", cancellationToken);
    }

    public async Task<Emmy.Core.ProviderResult> GetResponseAsync(string prompt, string purpose, CancellationToken cancellationToken = default)
    {
        if (!budget.Reserve(10, DateTimeOffset.UtcNow)) return new(false, Error: "Request budget exhausted");
        await slots.WaitAsync(cancellationToken);
        try
        {
            var next = configuration.ProviderName + "\0" + configuration.GetProviderApiKey(configuration.ProviderName);
            if (next != fingerprint) InitializeProvider();
            if (currentProvider is null) return new(false, Error: "No provider configured");
            var result = await currentProvider.GetResponseAsync(prompt, cancellationToken);
            LastError = result.Error;
            return result;
        }
        catch (OperationCanceledException) { return new(false, Error: "Cancelled"); }
        catch (Exception) { LastError = "Provider request failed"; return new(false, Error: LastError); }
        finally { slots.Release(); }
    }

    public void ReinitializeProvider()
    {
        this.InitializeProvider();
    }

    private void InitializeProvider()
    {
        (currentProvider as IDisposable)?.Dispose();
        var providerName = this.configuration.ProviderName;
        var apiKey = this.configuration.GetProviderApiKey(providerName);

        fingerprint = providerName + "\0" + apiKey;
        this.currentProvider = providerName switch
        {
            "OpenAI" => new OpenAIProvider(apiKey, log: this.log),
            "DeepSeek" => new DeepSeekProvider(apiKey, log: this.log),
            _ => null,
        };

        this.log.Information("Provider initialized: {Provider}", this.currentProvider?.Name ?? "None");
    }
    public void Dispose() => (currentProvider as IDisposable)?.Dispose();
}
