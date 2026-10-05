namespace DalamudEmmy.Provider;

public interface IProvider
{
    string Name { get; }

    Task<Emmy.Core.ProviderResult> GetResponseAsync(string prompt, CancellationToken cancellationToken = default);
}
