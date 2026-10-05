namespace DalamudEmmy.Persona;

public sealed class PersonaModeSettings
{
    public string Tone { get; set; } = "neutral";

    public float RoleplayLevel { get; set; } = 0.5f;

    public float HumorLevel { get; set; } = 0.5f;

    public string Verbosity { get; set; } = "medium";

    public string SystemPrompt { get; set; } = string.Empty;

    public string NoGoRules { get; set; } = string.Empty;
}
