using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using DalamudEmmy.Chat;
using DalamudEmmy.People;
using DalamudEmmy.Persona;
using DalamudEmmy.Reply;

namespace DalamudEmmy.UI;

public sealed class PluginWindow : IDisposable
{
    private readonly Configuration configuration;
    private readonly ChatCaptureService chatCapture;
    private readonly PeopleService people;
    private string peopleSearchQuery = string.Empty;

    public PluginWindow(Configuration configuration, ChatCaptureService chatCapture, PeopleService people)
    {
        this.configuration = configuration;
        this.chatCapture = chatCapture;
        this.people = people;
    }

    public bool IsOpen { get; set; }

    public void Dispose()
    {
    }

    public void Draw()
    {
        if (!this.IsOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(820, 560), ImGuiCond.FirstUseEver);

        var isOpen = this.IsOpen;
        var shouldDraw = ImGui.Begin("Dalamud Emmy", ref isOpen);

        if (shouldDraw)
        {
            this.DrawHeader();

            if (ImGui.BeginTabBar("DalamudEmmyTabs"))
            {
                this.DrawOverviewTab();
                this.DrawChatTab();
                this.DrawPeopleTab();
                this.DrawPersonaTab();
                this.DrawProviderTab();
                this.DrawSafetyTab();
                this.DrawNextTab();
                ImGui.EndTabBar();
            }
        }

        ImGui.End();
        this.IsOpen = isOpen;
    }

    private void DrawHeader()
    {
        ImGui.TextColored(new Vector4(0.55f, 0.78f, 1.00f, 1.00f), "Dalamud Emmy");
        ImGui.SameLine();
        ImGui.TextUnformatted("guarded assisted companion");
        ImGui.Separator();
    }

    private void DrawOverviewTab()
    {
        if (!ImGui.BeginTabItem("Overview"))
        {
            return;
        }

        this.DrawSectionTitle("Runtime");

        var enabled = this.configuration.IsEnabled;
        if (ImGui.Checkbox("Plugin enabled", ref enabled))
        {
            this.configuration.IsEnabled = enabled;
            this.configuration.Save();
        }

        ImGui.SameLine();
        this.DrawStatusPill(this.configuration.IsEnabled ? "Enabled" : "Paused", this.configuration.IsEnabled);

        ImGui.Separator();
        this.DrawSectionTitle("Current foundation");
        ImGui.BulletText("Minimal Dalamud plugin shell");
        ImGui.BulletText("/emmy command and persisted configuration");
        ImGui.BulletText("Chat capture for Say, incoming tells, outgoing tells, and party");
        ImGui.BulletText("No direct chat sending implemented");

        ImGui.Separator();
        this.DrawSectionTitle("Session snapshot");

        if (ImGui.BeginTable("DalamudEmmyOverviewTable", 2))
        {
            this.DrawKeyValueRow("Active persona", this.configuration.ActivePersonaName);
            this.DrawKeyValueRow("Provider", this.configuration.ProviderName);
            this.DrawKeyValueRow("Chat capture", this.configuration.CaptureChatMessages ? "Enabled" : "Paused");
            this.DrawKeyValueRow("Captured messages", this.chatCapture.TotalCaptured.ToString());
            this.DrawKeyValueRow("Known people", this.people.KnownPeople.Count.ToString());
            this.DrawKeyValueRow("Whitelisted", this.people.WhitelistedCount.ToString());
            this.DrawKeyValueRow("Ignored", this.people.IgnoredCount.ToString());
            this.DrawKeyValueRow("Mode", this.configuration.OperatingMode.ToString());
            ImGui.EndTable();
        }

        ImGui.EndTabItem();
    }

    private void DrawChatTab()
    {
        if (!ImGui.BeginTabItem("Chat"))
        {
            return;
        }

        this.DrawSectionTitle("Capture scope");

        var captureChatMessages = this.configuration.CaptureChatMessages;
        if (ImGui.Checkbox("Capture Say, Tell, and Party", ref captureChatMessages))
        {
            this.configuration.CaptureChatMessages = captureChatMessages;
            this.configuration.Save();
        }

        ImGui.SameLine();
        this.DrawStatusPill(this.configuration.CaptureChatMessages ? "Listening" : "Paused", this.configuration.CaptureChatMessages);

        ImGui.TextUnformatted($"Captured this session: {this.chatCapture.TotalCaptured}");
        ImGui.Separator();

        this.DrawSectionTitle("Recent messages");

        var recentMessages = this.chatCapture.RecentMessages;
        if (recentMessages.Count == 0)
        {
            ImGui.TextUnformatted("No relevant chat messages captured yet.");
            ImGui.EndTabItem();
            return;
        }

        if (ImGui.BeginTable("DalamudEmmyRecentChat", 6))
        {
            ImGui.TableSetupColumn("Time");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("Sender");
            ImGui.TableSetupColumn("Message");
            ImGui.TableSetupColumn("Reply");
            ImGui.TableSetupColumn("Suggested");
            ImGui.TableHeadersRow();

            foreach (var message in recentMessages)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(message.CapturedAt.ToLocalTime().ToString("HH:mm:ss"));
                ImGui.TableSetColumnIndex(1);
                var typeLabel = message.Type switch
                {
                    XivChatType.TellIncoming => "Tell (In)",
                    XivChatType.TellOutgoing => "Tell (Out)",
                    _ => message.Type.ToString()
                };
                ImGui.TextUnformatted(typeLabel);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(message.Sender);
                ImGui.TableSetColumnIndex(3);
                ImGui.TextWrapped(message.Message);
                ImGui.TableSetColumnIndex(4);
                this.DrawReplyDecision(message.ReplyDecision);
                ImGui.TableSetColumnIndex(5);
                this.DrawSuggestedReply(message);
            }

            ImGui.EndTable();
        }

        ImGui.EndTabItem();
    }

    private void DrawPeopleTab()
    {
        if (!ImGui.BeginTabItem("People"))
        {
            return;
        }

        this.DrawSectionTitle("Known people");
        ImGui.TextUnformatted("People are discovered from captured chat senders during this session.");
        ImGui.SetNextItemWidth(320);
        ImGui.InputTextWithHint("##PeopleSearch", "Search people", ref this.peopleSearchQuery, 128);
        ImGui.Separator();

        var knownPeople = this.people.KnownPeople
            .Where(person => this.MatchesPeopleSearch(person))
            .ToArray();
        if (knownPeople.Length == 0)
        {
            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(this.peopleSearchQuery)
                ? "No people discovered yet."
                : "No people match the current search.");
            ImGui.EndTabItem();
            return;
        }

        if (ImGui.BeginTable("DalamudEmmyPeople", 5))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Trust");
            ImGui.TableSetupColumn("Messages");
            ImGui.TableSetupColumn("Last seen");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            foreach (var person in knownPeople)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(person.DisplayName);
                ImGui.TableSetColumnIndex(1);
                this.DrawTrustState(person.TrustState);
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(person.MessageCount.ToString());
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(person.LastSeenAt.ToLocalTime().ToString("HH:mm:ss"));
                ImGui.TableSetColumnIndex(4);
                this.DrawTrustActions(person);
            }

            ImGui.EndTable();
        }

        ImGui.EndTabItem();
    }

    private void DrawPersonaTab()
    {
        if (!ImGui.BeginTabItem("Persona"))
        {
            return;
        }

        this.DrawSectionTitle("Active persona");

        var activePersona = this.configuration.GetActivePersona();
        if (activePersona == null)
        {
            ImGui.TextUnformatted("No active persona found.");
            ImGui.EndTabItem();
            return;
        }

        ImGui.TextUnformatted($"Current: {activePersona.Name}");
        ImGui.Separator();

        this.DrawSectionTitle("Trust rules");

        var trustMode = activePersona.TrustMode;
        if (ImGui.BeginCombo("Trust mode", trustMode.ToString()))
        {
            foreach (PersonaTrustMode mode in Enum.GetValues(typeof(PersonaTrustMode)))
            {
                var isSelected = mode == trustMode;
                if (ImGui.Selectable(mode.ToString(), isSelected))
                {
                    activePersona.TrustMode = mode;
                    this.configuration.Save();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Separator();

        var replyToUnknown = activePersona.ReplyToUnknown;
        if (ImGui.Checkbox("Reply to unknown people", ref replyToUnknown))
        {
            activePersona.ReplyToUnknown = replyToUnknown;
            this.configuration.Save();
        }

        var replyToIgnored = activePersona.ReplyToIgnored;
        if (ImGui.Checkbox("Reply to ignored people", ref replyToIgnored))
        {
            activePersona.ReplyToIgnored = replyToIgnored;
            this.configuration.Save();
        }

        ImGui.Separator();
        this.DrawSectionTitle("Persona mode and tone");

        var personaMode = activePersona.Mode;
        if (ImGui.BeginCombo("Mode", personaMode.ToString()))
        {
            foreach (PersonaMode mode in Enum.GetValues(typeof(PersonaMode)))
            {
                var isSelected = mode == personaMode;
                if (ImGui.Selectable(mode.ToString(), isSelected))
                {
                    activePersona.Mode = mode;
                    this.configuration.Save();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        var currentSettings = activePersona.GetCurrentModeSettings();
        var wideTextSize = new Vector2(520, 90);
        var largeTextSize = new Vector2(520, 140);

        var tone = currentSettings.Tone;
        if (ImGui.InputTextMultiline("Tone", ref tone, 512, wideTextSize))
        {
            currentSettings.Tone = tone;
            this.configuration.Save();
        }

        var roleplayLevel = currentSettings.RoleplayLevel;
        if (ImGui.SliderFloat("Roleplay level", ref roleplayLevel, 0.0f, 1.0f))
        {
            currentSettings.RoleplayLevel = roleplayLevel;
            this.configuration.Save();
        }

        var humorLevel = currentSettings.HumorLevel;
        if (ImGui.SliderFloat("Humor level", ref humorLevel, 0.0f, 1.0f))
        {
            currentSettings.HumorLevel = humorLevel;
            this.configuration.Save();
        }

        var verbosity = currentSettings.Verbosity;
        if (ImGui.BeginCombo("Verbosity", verbosity))
        {
            if (ImGui.Selectable("short"))
            {
                currentSettings.Verbosity = "short";
                this.configuration.Save();
            }

            if (ImGui.Selectable("medium"))
            {
                currentSettings.Verbosity = "medium";
                this.configuration.Save();
            }

            if (ImGui.Selectable("long"))
            {
                currentSettings.Verbosity = "long";
                this.configuration.Save();
            }

            ImGui.EndCombo();
        }

        var noGoRules = currentSettings.NoGoRules;
        if (ImGui.InputTextMultiline("NoGo Rules", ref noGoRules, 2048, wideTextSize))
        {
            currentSettings.NoGoRules = noGoRules;
            this.configuration.Save();
        }

        var systemPrompt = currentSettings.SystemPrompt;
        if (ImGui.InputTextMultiline("System Prompt", ref systemPrompt, 4096, largeTextSize))
        {
            currentSettings.SystemPrompt = systemPrompt;
            this.configuration.Save();
        }

        ImGui.Separator();
        this.DrawSectionTitle("Character description");

        var description = activePersona.Description;
        if (ImGui.InputTextMultiline("Description", ref description, 4096, largeTextSize))
        {
            activePersona.Description = description;
            this.configuration.Save();
        }

        ImGui.Separator();
        this.DrawSectionTitle("Available personas");

        if (ImGui.BeginTable("DalamudEmmyPersonas", 2))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Trust mode");
            ImGui.TableHeadersRow();

            foreach (var persona in this.configuration.Personas)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(persona.Name);
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(persona.TrustMode.ToString());
            }

            ImGui.EndTable();
        }

        ImGui.EndTabItem();
    }

    private void DrawProviderTab()
    {
        if (!ImGui.BeginTabItem("Provider"))
        {
            return;
        }

        this.DrawSectionTitle("Provider selection");

        var providerName = this.configuration.ProviderName;
        if (ImGui.BeginCombo("Provider", providerName))
        {
            if (ImGui.Selectable("OpenAI"))
            {
                this.configuration.ProviderName = "OpenAI";
                this.configuration.Save();
            }

            if (ImGui.Selectable("DeepSeek"))
            {
                this.configuration.ProviderName = "DeepSeek";
                this.configuration.Save();
            }

            if (ImGui.Selectable("Not configured"))
            {
                this.configuration.ProviderName = "Not configured";
                this.configuration.Save();
            }

            ImGui.EndCombo();
        }

        ImGui.Separator();
        this.DrawSectionTitle("API keys");

        var openAiKey = this.configuration.GetProviderApiKey("OpenAI");
        if (ImGui.InputTextWithHint("OpenAI API Key", "sk-...", ref openAiKey, 128, ImGuiInputTextFlags.Password))
        {
            this.configuration.SetProviderApiKey("OpenAI", openAiKey);
        }

        var deepSeekKey = this.configuration.GetProviderApiKey("DeepSeek");
        if (ImGui.InputTextWithHint("DeepSeek API Key", "sk-...", ref deepSeekKey, 128, ImGuiInputTextFlags.Password))
        {
            this.configuration.SetProviderApiKey("DeepSeek", deepSeekKey);
        }

        ImGui.Separator();
        this.DrawSectionTitle("Status");
        ImGui.TextUnformatted("Provider calls are not fully implemented yet.");
        ImGui.TextUnformatted("This is a scaffold for future LLM integration.");

        ImGui.EndTabItem();
    }

    private void DrawSafetyTab()
    {
        if (!ImGui.BeginTabItem("Safety"))
        {
            return;
        }

        this.DrawSectionTitle("Guardrails");

        var operatingMode = this.configuration.OperatingMode;
        if (ImGui.BeginCombo("Operating mode", operatingMode.ToString()))
        {
            foreach (OperatingMode mode in Enum.GetValues(typeof(OperatingMode)))
            {
                var isSelected = mode == operatingMode;
                if (ImGui.Selectable(mode.ToString(), isSelected))
                {
                    this.configuration.OperatingMode = mode;
                    this.configuration.Save();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var modeDescription = operatingMode switch
        {
            OperatingMode.Disabled => "No responses",
            OperatingMode.Assisted => "Preview only",
            OperatingMode.Active => "Auto-send",
            _ => "Unknown",
        };
        this.DrawStatusPill(modeDescription, operatingMode == OperatingMode.Assisted);

        ImGui.Separator();

        var activePersona = this.configuration.GetActivePersona();
        if (activePersona != null)
        {
            var decisionMode = activePersona.DecisionMode;
            if (ImGui.BeginCombo("Reply decision mode", decisionMode.ToString()))
            {
                foreach (ReplyDecisionMode mode in Enum.GetValues(typeof(ReplyDecisionMode)))
                {
                    var isSelected = mode == decisionMode;
                    if (ImGui.Selectable(mode.ToString(), isSelected))
                    {
                        activePersona.DecisionMode = mode;
                        this.configuration.Save();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            var decisionModeDescription = decisionMode switch
            {
                ReplyDecisionMode.Disabled => "Always reply",
                ReplyDecisionMode.HeuristicsOnly => "Heuristics only",
                ReplyDecisionMode.AiAndHeuristics => "AI + Heuristics",
                _ => "Unknown",
            };
            this.DrawStatusPill(decisionModeDescription, decisionMode == ReplyDecisionMode.AiAndHeuristics);

            if (activePersona.DecisionMode != ReplyDecisionMode.Disabled)
            {
                ImGui.Separator();
                var autoReplyThreshold = activePersona.AutoReplyScoreThreshold;
                if (ImGui.SliderInt("Auto-reply threshold", ref autoReplyThreshold, 0, 100, "%d"))
                {
                    activePersona.AutoReplyScoreThreshold = autoReplyThreshold;
                    this.configuration.Save();
                }
                ImGui.SameLine();
                ImGui.TextUnformatted($"Score >= {autoReplyThreshold} triggers reply");

                var targetScoreBonus = activePersona.SenderTargetingScoreBonus;
                if (ImGui.SliderInt("Targeting score bonus", ref targetScoreBonus, 0, 100, "%d"))
                {
                    activePersona.SenderTargetingScoreBonus = targetScoreBonus;
                    this.configuration.Save();
                }
                this.DrawTooltip("Adds directly to the reply decision score when the sender currently targets Emmy. Higher values make targeted Say/Party messages more likely to pass the reply threshold.");

                var targetAddressedBonus = activePersona.SenderTargetingAddressedBonus;
                if (ImGui.SliderInt("Targeting addressed bonus", ref targetAddressedBonus, 0, 100, "%d"))
                {
                    activePersona.SenderTargetingAddressedBonus = targetAddressedBonus;
                    this.configuration.Save();
                }
                this.DrawTooltip("Adds to the conversation analysis score for whether Emmy is being addressed. This helps the AI understand that the current message is probably meant for Emmy, not just nearby chatter.");

                var targetThreadBonus = activePersona.SenderTargetingThreadBonus;
                if (ImGui.SliderInt("Targeting thread bonus", ref targetThreadBonus, 0, 100, "%d"))
                {
                    activePersona.SenderTargetingThreadBonus = targetThreadBonus;
                    this.configuration.Save();
                }
                this.DrawTooltip("Adds to thread participation when the sender targets Emmy. Higher values make follow-up fragments in an ongoing Say/Party exchange count more as part of Emmy's conversation.");
            }
        }

        ImGui.Separator();
        this.DrawSectionTitle("Current limits");
        ImGui.BulletText("No direct chat sending");
        ImGui.BulletText("No provider calls");
        ImGui.BulletText("No memory persistence yet");
        ImGui.BulletText("Captured chat remains in the current session buffer");

        ImGui.EndTabItem();
    }

    private void DrawNextTab()
    {
        if (!ImGui.BeginTabItem("Next"))
        {
            return;
        }

        this.DrawSectionTitle("Recommended next build step");
        ImGui.TextWrapped("Persist people, whitelist state, and captured chat messages locally before adding LLM decision logic.");

        ImGui.Separator();
        this.DrawSectionTitle("Why this is next");
        ImGui.BulletText("Current people and whitelist state is still session-local");
        ImGui.BulletText("Persistence makes future reply decisions auditable");
        ImGui.BulletText("It prepares persona-aware context building");

        ImGui.Separator();
        this.DrawSectionTitle("After that");
        ImGui.BulletText("Add SQLite storage for people and chat messages");
        ImGui.BulletText("Add persona-aware reply decision scaffolding");
        ImGui.BulletText("Show assisted reply suggestions without sending them");

        ImGui.EndTabItem();
    }

    private void DrawSectionTitle(string text)
    {
        ImGui.TextColored(new Vector4(0.80f, 0.88f, 1.00f, 1.00f), text);
    }

    private void DrawStatusPill(string text, bool positive)
    {
        var color = positive
            ? new Vector4(0.35f, 0.95f, 0.55f, 1.00f)
            : new Vector4(1.00f, 0.65f, 0.30f, 1.00f);

        ImGui.TextColored(color, text);
    }

    private void DrawTrustState(PersonTrustState trustState)
    {
        var color = trustState switch
        {
            PersonTrustState.Whitelisted => new Vector4(0.35f, 0.95f, 0.55f, 1.00f),
            PersonTrustState.Ignored => new Vector4(1.00f, 0.35f, 0.35f, 1.00f),
            _ => new Vector4(1.00f, 0.75f, 0.35f, 1.00f),
        };

        ImGui.TextColored(color, trustState.ToString());
    }

    private void DrawTrustActions(KnownPerson person)
    {
        var whitelistLabel = $"Whitelist##{person.DisplayName}";
        if (ImGui.SmallButton(whitelistLabel))
        {
            this.people.SetTrustState(person.DisplayName, PersonTrustState.Whitelisted);
        }

        ImGui.SameLine();

        var ignoreLabel = $"Ignore##{person.DisplayName}";
        if (ImGui.SmallButton(ignoreLabel))
        {
            this.people.SetTrustState(person.DisplayName, PersonTrustState.Ignored);
        }

        ImGui.SameLine();

        var resetLabel = $"Reset##{person.DisplayName}";
        if (ImGui.SmallButton(resetLabel))
        {
            this.people.SetTrustState(person.DisplayName, PersonTrustState.Unknown);
        }

        ImGui.SameLine();

        var clearMemoryLabel = $"Clear Memory##{person.DisplayName}";
        if (ImGui.SmallButton(clearMemoryLabel))
        {
            this.people.ResetPersonKnowledge(person.DisplayName);
        }

        ImGui.SameLine();

        var deleteHistoryLabel = $"Delete History##{person.DisplayName}";
        if (ImGui.SmallButton(deleteHistoryLabel))
        {
            this.chatCapture.DeleteChatHistoryForSender(person.DisplayName);
        }
    }

    private void DrawReplyDecision(ReplyDecision? decision)
    {
        if (!decision.HasValue)
        {
            ImGui.TextUnformatted("-");
            return;
        }

        var color = decision.Value switch
        {
            ReplyDecision.ShouldReply => new Vector4(0.35f, 0.95f, 0.55f, 1.00f),
            ReplyDecision.ShouldNotReply => new Vector4(1.00f, 0.65f, 0.30f, 1.00f),
            _ => new Vector4(1.00f, 0.75f, 0.35f, 1.00f),
        };

        ImGui.TextColored(color, decision.Value.ToString());
    }

    private void DrawTooltip(string text)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    private void DrawSuggestedReply(CapturedChatMessage message)
    {
        if (message.IsReplyPending)
        {
            ImGui.TextUnformatted("...");
            return;
        }

        if (string.IsNullOrEmpty(message.SuggestedReply))
        {
            ImGui.TextUnformatted("-");
            return;
        }

        var truncatedReply = message.SuggestedReply.Length > 30
            ? message.SuggestedReply[..30] + "..."
            : message.SuggestedReply;

        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.00f), truncatedReply);

        if (this.configuration.OperatingMode == OperatingMode.Assisted && !message.ReplyConsumed)
        {
            ImGui.SameLine();
            var sendLabel = $"Send##{message.GetHashCode()}";
            if (ImGui.SmallButton(sendLabel))
            {
                this.chatCapture.SendReply(message.SuggestedReply, message.Type, message.SenderWithWorld, confirmed: true, draft: message);
            }
        }
    }

    private void DrawKeyValueRow(string key, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(key);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }

    private bool MatchesPeopleSearch(KnownPerson person)
    {
        if (string.IsNullOrWhiteSpace(this.peopleSearchQuery))
        {
            return true;
        }

        return person.DisplayName.Contains(this.peopleSearchQuery, StringComparison.OrdinalIgnoreCase) ||
               person.RelationshipSummary.Contains(this.peopleSearchQuery, StringComparison.OrdinalIgnoreCase) ||
               person.MemorySummary.Contains(this.peopleSearchQuery, StringComparison.OrdinalIgnoreCase);
    }
}
