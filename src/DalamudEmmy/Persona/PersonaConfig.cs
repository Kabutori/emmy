namespace DalamudEmmy.Persona;

public sealed class PersonaConfig
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string AnchoredKnowledge { get; set; } = string.Empty;

    public PersonaTrustMode TrustMode { get; set; } = PersonaTrustMode.WhitelistedOnly;

    public bool ReplyToUnknown { get; set; } = false;

    public bool ReplyToIgnored { get; set; } = false;

    public bool AlwaysReplyToIncomingTells { get; set; } = true;

    public bool RequireDirectMentionInSay { get; set; } = true;

    public bool ReplyToQuestions { get; set; } = true;

    public ReplyDecisionMode DecisionMode { get; set; } = ReplyDecisionMode.AiAndHeuristics;

    public int AiRelevanceMinScore { get; set; } = 35;

    public int AutoReplyScoreThreshold { get; set; } = 40;

    public int SenderTargetingScoreBonus { get; set; } = 50;

    public int SenderTargetingAddressedBonus { get; set; } = 45;

    public int SenderTargetingThreadBonus { get; set; } = 25;

    public PersonaMode Mode { get; set; } = PersonaMode.Neutral;

    public Dictionary<PersonaMode, PersonaModeSettings> ModeSettings { get; set; } = [];

    public bool AvoidAssistantTone { get; set; } = true;

    public PersonaModeSettings GetCurrentModeSettings()
    {
        if (!this.ModeSettings.TryGetValue(this.Mode, out var settings))
        {
            settings = new PersonaModeSettings();
            this.ModeSettings[this.Mode] = settings;
        }

        return settings;
    }
}
