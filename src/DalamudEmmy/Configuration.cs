using Dalamud.Configuration;
using Dalamud.Plugin;
using DalamudEmmy.Persona;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace DalamudEmmy;

public sealed class Configuration : IPluginConfiguration
{
    [JsonIgnore]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;

    public bool IsEnabled { get; set; } = true;

    public OperatingMode OperatingMode { get; set; } = OperatingMode.Assisted;

    public bool CaptureChatMessages { get; set; } = true;

    public string ActivePersonaName { get; set; } = "Emmy";

    public string ProviderName { get; set; } = "Not configured";

    public List<PersonaConfig> Personas { get; set; } = [];

    public Dictionary<string, string> ProviderApiKeys { get; set; } = [];
    public Dictionary<string, string> ProtectedProviderApiKeys { get; set; } = [];

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        this.EnsureDefaultPersona();
        this.EnsureAnchoredKnowledge();
        if(OperatingSystem.IsWindows() && ProviderApiKeys.Count>0)
        {
            foreach(var item in ProviderApiKeys) ProtectedProviderApiKeys[item.Key]=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(item.Value),null,DataProtectionScope.CurrentUser));
            ProviderApiKeys.Clear(); Save();
        }
    }

    [field: JsonIgnore]
    public event Action? Changed;
    public bool UseCompanionHost { get; set; } = true;

    public void Save()
    {
        Changed?.Invoke();
        this.pluginInterface?.SavePluginConfig(this);
    }

    public PersonaConfig? GetActivePersona()
    {
        return this.Personas.FirstOrDefault(p => p.Name == this.ActivePersonaName);
    }

    public string GetProviderApiKey(string providerName)
    {
        if(OperatingSystem.IsWindows() && ProtectedProviderApiKeys.TryGetValue(providerName,out var encrypted))
        {
            try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser));}
            catch(Exception ex) when(ex is CryptographicException or FormatException){return string.Empty;}
        }
        return string.Empty;
    }

    public void SetProviderApiKey(string providerName, string apiKey)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Keys are stored using Windows user protection");
        if(string.IsNullOrWhiteSpace(apiKey))ProtectedProviderApiKeys.Remove(providerName);
        else ProtectedProviderApiKeys[providerName] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey),null,DataProtectionScope.CurrentUser));
        ProviderApiKeys.Remove(providerName);
        this.Save();
    }

    private void EnsureDefaultPersona()
    {
        if (this.Personas.Count == 0)
        {
            this.Personas.Add(new PersonaConfig
            {
                Name = "Emmy",
                AnchoredKnowledge = BuildDefaultEmmyAnchoredKnowledge(),
                TrustMode = PersonaTrustMode.WhitelistedOnly,
                ReplyToUnknown = false,
                ReplyToIgnored = false,
            });
        }
    }

    private void EnsureAnchoredKnowledge()
    {
        foreach (var persona in this.Personas)
        {
            if (string.Equals(persona.Name, "Emmy", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(persona.AnchoredKnowledge))
            {
                persona.AnchoredKnowledge = BuildDefaultEmmyAnchoredKnowledge();
            }
        }
    }

    private static string BuildDefaultEmmyAnchoredKnowledge()
    {
        return
            "Cecile Volanges is your superior and you should treat her as your boss. " +
            "When someone says CCP in this context, they mean your Free Company or guild in FFXIV, and you are a member of CCP.";
    }
}
