using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DalamudEmmy.Conversation;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using DalamudEmmy.Chat;
using DalamudEmmy.People;
using DalamudEmmy.Persona;
using DalamudEmmy.Provider;

namespace DalamudEmmy.Reply;

public sealed class ReplyDecisionService
{
    private static readonly Regex QuestionRegex = new(@"\?|\b(who|what|when|where|why|how|can|could|would|will|are|is|do|did|hast|bist|kannst|koenntest|wie|warum|wer|was|wo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GreetingRegex = new(@"\b(hi|hey|hello|hallo|yo|sup|moin|servus|guten)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecondPersonRegex = new(@"\b(you|your|u|du|dich|dir|dein|deine)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LaughterOnlyRegex = new(@"^(x+d+|lol+|lmao+|rofl+|haha+|hehe+|hihi+)+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OpinionQuestionRegex = new(@"\b(opinion|think of|think about|feel about|h[aä]ltst du|denkst du|meinung|findest du|was sagst du zu)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HelpRequestRegex = new(@"\b(help|tips|advice|recommend|explain|tell me|show me|hilf|hilfe|tipps|rat|erkl[aä]r)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Configuration configuration;
    private readonly ConversationContextService conversationContext;
    private readonly PeopleService people;
    private readonly LLMRequestService llmRequest;
    private readonly IPluginLog log;

    public ReplyDecisionService(Configuration configuration, ConversationContextService conversationContext, PeopleService people, LLMRequestService llmRequest, IPluginLog log)
    {
        this.configuration = configuration;
        this.conversationContext = conversationContext;
        this.people = people;
        this.llmRequest = llmRequest;
        this.log = log;
    }

    public async Task<ReplyEvaluationResult> EvaluateAsync(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, CancellationToken cancellationToken = default)
    {
        if (!this.configuration.IsEnabled)
        {
            return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Plugin disabled");
        }

        var activePersona = this.configuration.GetActivePersona();
        if (activePersona == null)
        {
            return CreateResult(ReplyDecision.Unknown, 0, false, "No active persona");
        }

        var conversation = this.conversationContext.Analyze(message, recentContext);
        var trustGate = this.EvaluateTrustGate(message, activePersona);
        if (trustGate != null)
        {
            return CreateResult(trustGate.Decision, trustGate.Score, trustGate.WasAiEvaluated, trustGate.Reason, conversation);
        }

        if (IsLowContentChatTrigger(message, activePersona.Name))
        {
            return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Low-content chat trigger; do not answer older context", conversation);
        }

        if (IsBriefAcknowledgementAfterOwnReply(message, recentContext, activePersona.Name))
        {
            return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Brief acknowledgement after Emmy reply", conversation);
        }

        // If decision mode is Disabled, always reply (skip all decision logic)
        if (activePersona.DecisionMode == ReplyDecisionMode.Disabled)
        {
            return CreateResult(ReplyDecision.ShouldReply, 100, false, "Always reply (decision disabled)", conversation);
        }

        var heuristic = this.CalculateHeuristicScore(message, recentContext, activePersona, conversation);
        if (IsDirectTargetedRequest(message))
        {
            return CreateResult(ReplyDecision.ShouldReply, Math.Max(heuristic.Score, activePersona.AutoReplyScoreThreshold), false, $"{heuristic.Reason}; direct targeted request", conversation);
        }

        if (ShouldReplyFromStrongConversationSignal(message, recentContext, heuristic.Score, conversation))
        {
            return CreateResult(ReplyDecision.ShouldReply, heuristic.Score, false, $"{heuristic.Reason}; strong ongoing conversation signal", conversation);
        }

        // If decision mode is HeuristicsOnly, decide based solely on heuristics
        if (activePersona.DecisionMode == ReplyDecisionMode.HeuristicsOnly)
        {
            var decision = heuristic.Score >= activePersona.AutoReplyScoreThreshold
                ? ReplyDecision.ShouldReply
                : ReplyDecision.ShouldNotReply;
            return CreateResult(decision, heuristic.Score, false, heuristic.Reason, conversation);
        }

        // If decision mode is AiAndHeuristics, use AI for borderline cases
        // If heuristic score is already high enough, skip AI check
        if (heuristic.Score >= activePersona.AutoReplyScoreThreshold)
        {
            return CreateResult(ReplyDecision.ShouldReply, heuristic.Score, false, heuristic.Reason, conversation);
        }

        // If heuristic score is too low for AI check, reject
        if (heuristic.Score < activePersona.AiRelevanceMinScore)
        {
            return CreateResult(ReplyDecision.ShouldNotReply, heuristic.Score, false, heuristic.Reason, conversation);
        }

        // Use AI reasoning for borderline cases
        var aiEvaluation = await this.EvaluateWithAiAsync(message, recentContext, activePersona, heuristic.Score, heuristic.Reason, conversation, cancellationToken);
        if (aiEvaluation != null)
        {
            return aiEvaluation;
        }

        return CreateResult(
            heuristic.Score >= activePersona.AutoReplyScoreThreshold ? ReplyDecision.ShouldReply : ReplyDecision.ShouldNotReply,
            heuristic.Score,
            true,
            $"{heuristic.Reason}; AI fallback parse failed",
            conversation);
    }

    private ReplyEvaluationResult? EvaluateTrustGate(CapturedChatMessage message, PersonaConfig activePersona)
    {
        if (activePersona.TrustMode == PersonaTrustMode.None)
        {
            return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Trust mode is None");
        }

        var person = this.people.KnownPeople.FirstOrDefault(p => p.DisplayName == message.SenderWithWorld);
        if (person == null)
        {
            if (!activePersona.ReplyToUnknown)
            {
                return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Unknown sender blocked by trust rules");
            }

            return null;
        }

        if (person.TrustState == PersonTrustState.Ignored && !activePersona.ReplyToIgnored)
        {
            return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Ignored sender blocked by trust rules");
        }

        if (person.TrustState == PersonTrustState.Whitelisted)
        {
            return null;
        }

        if (activePersona.TrustMode == PersonaTrustMode.All)
        {
            return null;
        }

        return CreateResult(ReplyDecision.ShouldNotReply, 0, false, "Sender is not whitelisted");
    }

    private (int Score, string Reason) CalculateHeuristicScore(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, PersonaConfig activePersona, ConversationAnalysisResult conversation)
    {
        var score = 0;
        var reasons = new List<string>();
        var normalizedMessage = message.Message.Trim();
        var personaName = activePersona.Name.Trim();

        switch (message.Type)
        {
            case XivChatType.TellIncoming:
                score += activePersona.AlwaysReplyToIncomingTells ? 75 : 45;
                reasons.Add(activePersona.AlwaysReplyToIncomingTells ? "incoming tell priority" : "incoming tell");
                break;
            case XivChatType.Party:
                score += 20;
                reasons.Add("party context");
                break;
            case XivChatType.Say:
                score += 10;
                reasons.Add("say context");
                break;
            default:
                score += 5;
                reasons.Add("base channel score");
                break;
        }

        var directMention = !string.IsNullOrWhiteSpace(personaName) && ContainsWord(normalizedMessage, personaName);
        if (directMention)
        {
            score += 35;
            reasons.Add("direct persona mention");
        }

        if (directMention && LooksLikeThirdPersonMentionOfPersona(normalizedMessage, personaName))
        {
            score -= 30;
            reasons.Add("persona mentioned as topic, not addressee");
        }

        var isQuestion = activePersona.ReplyToQuestions && QuestionRegex.IsMatch(normalizedMessage);
        if (isQuestion)
        {
            score += 20;
            reasons.Add("question-like phrasing");
        }

        if (GreetingRegex.IsMatch(normalizedMessage))
        {
            score += 10;
            reasons.Add("greeting");
        }

        if (SecondPersonRegex.IsMatch(normalizedMessage))
        {
            score += 8;
            reasons.Add("second-person wording");
        }

        if (HelpRequestRegex.IsMatch(normalizedMessage))
        {
            score += 30;
            reasons.Add("help/advice request");
        }

        if (OpinionQuestionRegex.IsMatch(normalizedMessage) && this.people.FindKnownPersonMentionedIn(normalizedMessage, message.Sender) != null)
        {
            score += 35;
            reasons.Add("asks Emmy opinion about known person");
        }

        if (message.Type == XivChatType.Say && activePersona.RequireDirectMentionInSay && !directMention)
        {
            score -= 25;
            reasons.Add("say without direct mention");
        }

        if (normalizedMessage.Length <= 20 && directMention)
        {
            score += 10;
            reasons.Add("short direct callout");
        }

        if (message.IsSenderTargetingEmmy)
        {
            score += activePersona.SenderTargetingScoreBonus;
            reasons.Add($"sender currently targets Emmy +{activePersona.SenderTargetingScoreBonus}");
        }

        var recentFromSameSender = recentContext
            .Where(entry => !ReferenceEquals(entry, message) && string.Equals(entry.Sender, message.Sender, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(2)
            .ToArray();

        if (recentFromSameSender.Length > 0)
        {
            score += 10;
            reasons.Add("recent ongoing conversation");

            var latest = recentFromSameSender[0];
            if (message.CapturedAt - latest.CapturedAt <= TimeSpan.FromSeconds(45))
            {
                score += 7;
                reasons.Add("rapid follow-up");
            }
        }

        if (IsFollowUpAfterOwnReply(message, recentContext, activePersona.Name))
        {
            score += 25;
            reasons.Add("follow-up after Emmy reply");
        }

        if (IsClarificationQuestionAboutOwnReply(message, recentContext, activePersona.Name))
        {
            score += 40;
            reasons.Add("clarification question about Emmy reply");
        }

        score += conversation.IsEmmyAddressedScore / 3;
        if (conversation.IsEmmyAddressedScore > 0)
        {
            reasons.Add($"conversation says Emmy-addressed {conversation.IsEmmyAddressedScore}");
        }

        score += conversation.ReplyExpectationScore / 5;
        if (conversation.ReplyExpectationScore >= 40)
        {
            reasons.Add($"reply expectation {conversation.ReplyExpectationScore}");
        }

        score += conversation.ThreadParticipationScore / 6;
        if (conversation.ThreadParticipationScore >= 35)
        {
            reasons.Add($"thread participation {conversation.ThreadParticipationScore}");
        }

        score -= conversation.OtherTargetClarityScore / 3;
        if (conversation.OtherTargetClarityScore >= 30)
        {
            reasons.Add($"other target clarity {conversation.OtherTargetClarityScore}");
        }

        score -= conversation.GroupChatStrength / 4;
        if (conversation.GroupChatStrength >= 30)
        {
            reasons.Add($"group chat strength {conversation.GroupChatStrength}");
        }

        switch (conversation.ConversationType)
        {
            case ConversationType.Direct:
                score += 12;
                reasons.Add("direct conversation");
                break;
            case ConversationType.MultiParty:
                score -= 12;
                reasons.Add("multi-party conversation");
                break;
            case ConversationType.Broadcast:
                score -= 16;
                reasons.Add("broadcast-like conversation");
                break;
            case ConversationType.Ambient:
                score -= 18;
                reasons.Add("ambient conversation");
                break;
        }

        score = Math.Clamp(score, 0, 100);
        return (score, string.Join(", ", reasons));
    }

    private async Task<ReplyEvaluationResult?> EvaluateWithAiAsync(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, PersonaConfig activePersona, int heuristicScore, string heuristicReason, ConversationAnalysisResult conversation, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = this.BuildAiRelevancePrompt(message, recentContext, activePersona, heuristicScore, heuristicReason, conversation);
            var response = await this.llmRequest.GetResponseAsync(prompt, "relevance check", cancellationToken);
            if (!response.Success) return null;
            var json = ExtractJsonObject(response.Text);
            if (string.IsNullOrWhiteSpace(json))
            {
                this.log.Warning("AI relevance check returned no parseable JSON");
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var shouldReply = root.TryGetProperty("shouldReply", out var shouldReplyValue) && shouldReplyValue.ValueKind == JsonValueKind.True;
            var isAddressedToMe = root.TryGetProperty("isAddressedToMe", out var addressedValue) && addressedValue.ValueKind == JsonValueKind.True;
            var confidence = root.TryGetProperty("confidence", out var confidenceValue) && confidenceValue.TryGetDouble(out var parsedConfidence)
                ? parsedConfidence
                : shouldReply ? 0.75 : 0.25;
            var reason = root.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() ?? "AI relevance check" : "AI relevance check";

            var aiScore = Math.Clamp((int)Math.Round(confidence * 100), 0, 100);
            var decision = shouldReply && isAddressedToMe
                ? ReplyDecision.ShouldReply
                : ReplyDecision.ShouldNotReply;
            var finalScore = decision == ReplyDecision.ShouldReply
                ? Math.Clamp(Math.Max(heuristicScore, aiScore), 0, 100)
                : Math.Clamp(Math.Min(heuristicScore, aiScore), 0, 100);

            return CreateResult(decision, finalScore, true, $"{heuristicReason}; AI: {reason}", conversation);
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "AI relevance evaluation failed");
            return null;
        }
    }

    private static bool ShouldReplyFromStrongConversationSignal(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, int heuristicScore, ConversationAnalysisResult conversation)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party))
        {
            return false;
        }

        if (heuristicScore < 60 && !HelpRequestRegex.IsMatch(message.Message))
        {
            return false;
        }

        var isHelpRequest = HelpRequestRegex.IsMatch(message.Message);
        if (!message.IsSenderTargetingEmmy && conversation.ReplyExpectationScore < 50 && !isHelpRequest)
        {
            return false;
        }

        if (conversation.OtherTargetClarityScore >= 50 && !message.IsSenderTargetingEmmy)
        {
            return false;
        }

        var recentSameSender = recentContext
            .Where(entry => entry.Type == message.Type)
            .Where(entry => string.Equals(entry.Sender, message.Sender, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CapturedAt)
            .FirstOrDefault();

        return recentSameSender != null && message.CapturedAt - recentSameSender.CapturedAt <= TimeSpan.FromSeconds(60);
    }

    private static bool IsDirectTargetedRequest(CapturedChatMessage message)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party) || !message.IsSenderTargetingEmmy)
        {
            return false;
        }

        return QuestionRegex.IsMatch(message.Message)
            || HelpRequestRegex.IsMatch(message.Message)
            || SecondPersonRegex.IsMatch(message.Message);
    }

    private static bool IsFollowUpAfterOwnReply(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, string personaName)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party))
        {
            return false;
        }

        var recentChannelTurns = recentContext
            .Where(entry => entry.Type == message.Type)
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(4)
            .ToArray();

        var latestOwnReply = recentChannelTurns.FirstOrDefault(entry => string.Equals(entry.Sender, personaName, StringComparison.OrdinalIgnoreCase));
        if (latestOwnReply == null || message.CapturedAt - latestOwnReply.CapturedAt > TimeSpan.FromSeconds(90))
        {
            return false;
        }

        return recentChannelTurns.Any(entry => string.Equals(entry.Sender, message.Sender, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClarificationQuestionAboutOwnReply(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, string personaName)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party) || !QuestionRegex.IsMatch(message.Message))
        {
            return false;
        }

        var latestOwnReply = recentContext
            .Where(entry => entry.Type == message.Type)
            .Where(entry => string.Equals(entry.Sender, personaName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CapturedAt)
            .FirstOrDefault();

        if (latestOwnReply == null || message.CapturedAt - latestOwnReply.CapturedAt > TimeSpan.FromMinutes(2))
        {
            return false;
        }

        var messageTerms = ExtractMeaningfulTerms(message.Message).ToArray();
        if (messageTerms.Length == 0)
        {
            return Regex.IsMatch(message.Message, @"\b(what|was|wie|wieso|warum|huh|hä)\b", RegexOptions.IgnoreCase);
        }

        var ownReplyTerms = ExtractMeaningfulTerms(latestOwnReply.Message).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return messageTerms.Any(ownReplyTerms.Contains);
    }

    private static IEnumerable<string> ExtractMeaningfulTerms(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "what", "was", "wer", "wie", "why", "warum", "wieso", "the", "a", "an", "and", "or", "und", "oder", "you", "du", "her", "him", "sie", "er",
        };

        foreach (Match match in Regex.Matches(text, @"[\p{L}]{4,}"))
        {
            var value = match.Value.Trim();
            if (!stopWords.Contains(value))
            {
                yield return value;
            }
        }
    }

    private string BuildAiRelevancePrompt(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, PersonaConfig activePersona, int heuristicScore, string heuristicReason, ConversationAnalysisResult conversation)
    {
        var currentSettings = activePersona.GetCurrentModeSettings();
        var description = activePersona.Description.Trim();
        var anchoredKnowledge = activePersona.AnchoredKnowledge.Trim();
        var tone = currentSettings.Tone.Trim();
        var verbosity = currentSettings.Verbosity.Trim();
        var noGoRules = currentSettings.NoGoRules.Trim();
        var personContext = this.people.BuildPromptContext(message.Sender);
        var selectedContext = this.SelectRelevantContextMessages(message, recentContext, activePersona);

        var builder = new StringBuilder();
        builder.AppendLine("You are deciding whether an RP chat assistant should reply.");
        builder.AppendLine("Return ONLY JSON with fields: isAddressedToMe (bool), shouldReply (bool), confidence (0-1), reason (string).");
        builder.AppendLine("Be conservative. Only shouldReply=true when the assistant is clearly being addressed or socially expected to respond.");
        builder.AppendLine("In Say and Party, a direct name mention is not the only valid address signal. If an allowed sender is targeting Emmy and the recent context shows an ongoing exchange with Emmy, short fragments and continuations can be socially addressed to Emmy.");
        builder.AppendLine();
        builder.AppendLine($"Assistant name: {activePersona.Name}");
        builder.AppendLine("Identity rule: The assistant is Emmy, not a generic chatbot.");

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.AppendLine($"Persona description: {description}");
        }

        if (!string.IsNullOrWhiteSpace(anchoredKnowledge))
        {
            builder.AppendLine($"Anchored knowledge: {anchoredKnowledge}");
        }

        if (!string.IsNullOrWhiteSpace(tone) || !string.IsNullOrWhiteSpace(verbosity))
        {
            builder.AppendLine($"Current mode style: tone={ValueOrFallback(tone, "neutral")}, verbosity={ValueOrFallback(verbosity, "medium")}");
        }

        if (!string.IsNullOrWhiteSpace(noGoRules))
        {
            builder.AppendLine($"No-go rules: {noGoRules}");
        }

        builder.AppendLine("Current interlocutor:");
        builder.AppendLine($"- Name: {message.SenderWithWorld}");
        builder.AppendLine($"- You are speaking with: {message.SenderWithWorld}");
        builder.AppendLine(personContext);
        builder.AppendLine($"Channel: {message.Type}");
        builder.AppendLine($"Sender: {message.SenderWithWorld}");
        builder.AppendLine($"Message: {message.Message}");
        builder.AppendLine($"Heuristic score: {heuristicScore}");
        builder.AppendLine($"Heuristic reason: {heuristicReason}");
        builder.AppendLine($"Conversation type: {conversation.ConversationType}");
        builder.AppendLine($"Likely addressee(s): {string.Join(", ", conversation.LikelyAddressees)}");
        builder.AppendLine($"Active participants: {string.Join(", ", conversation.ActiveParticipants)}");
        builder.AppendLine($"IsEmmyAddressedScore: {conversation.IsEmmyAddressedScore}");
        builder.AppendLine($"OtherTargetClarityScore: {conversation.OtherTargetClarityScore}");
        builder.AppendLine($"GroupChatStrength: {conversation.GroupChatStrength}");
        builder.AppendLine($"ReplyExpectationScore: {conversation.ReplyExpectationScore}");
        builder.AppendLine($"Thread summary: {conversation.ThreadSummary}");
        builder.AppendLine("Recent relevant context:");

        foreach (var entry in selectedContext)
        {
            builder.AppendLine($"- [{entry.Type}] {entry.SenderWithWorld}: {entry.Message}");
        }

        return builder.ToString();
    }

    private static ReplyEvaluationResult CreateResult(ReplyDecision decision, int score, bool wasAiEvaluated, string reason, ConversationAnalysisResult? conversation = null)
    {
        return new ReplyEvaluationResult
        {
            Decision = decision,
            Score = score,
            WasAiEvaluated = wasAiEvaluated,
            Reason = reason,
            ConversationAnalysis = conversation ?? new ConversationAnalysisResult(),
        };
    }

    private static bool ContainsWord(string text, string word)
    {
        return Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
    }

    private static bool IsLowContentChatTrigger(CapturedChatMessage message, string personaName)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party))
        {
            return false;
        }

        var normalized = message.Message.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var stripped = normalized.Trim(',', ':', '.', '!', '?', '-', '_', '~', '\'', '"', ' ');
        if (!string.IsNullOrWhiteSpace(personaName) && string.Equals(stripped, personaName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (GreetingRegex.IsMatch(normalized))
        {
            return false;
        }

        if (!normalized.Any(char.IsLetterOrDigit))
        {
            return true;
        }

        if (LaughterOnlyRegex.IsMatch(stripped))
        {
            return true;
        }

        return stripped.Length <= 1;
    }

    private static bool IsBriefAcknowledgementAfterOwnReply(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext, string personaName)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party))
        {
            return false;
        }

        var normalized = message.Message.Trim();
        if (normalized.Length > 80 || QuestionRegex.IsMatch(normalized))
        {
            return false;
        }

        if (HelpRequestRegex.IsMatch(normalized))
        {
            return false;
        }

        var looksLikeAck = Regex.IsMatch(
            normalized,
            @"\b(ok|okay|oki|alright|fine|sure|thanks|thank you|danke|passt|alles gut|kein problem|no worries|its okay|it's okay|is okay|cee)\b",
            RegexOptions.IgnoreCase);

        if (!looksLikeAck)
        {
            return false;
        }

        var latestTurn = recentContext
            .Where(entry => entry.Type == message.Type)
            .OrderByDescending(entry => entry.CapturedAt)
            .FirstOrDefault();

        return latestTurn != null
            && string.Equals(latestTurn.Sender, personaName, StringComparison.OrdinalIgnoreCase)
            && message.CapturedAt - latestTurn.CapturedAt <= TimeSpan.FromSeconds(45);
    }

    private static bool LooksLikeThirdPersonMentionOfPersona(string message, string personaName)
    {
        if (string.IsNullOrWhiteSpace(personaName))
        {
            return false;
        }

        var escapedName = Regex.Escape(personaName);
        return Regex.IsMatch(message, $@"\b(she|her|he|him|they|them|sie|er)\b[^.!?]{{0,40}}\b{escapedName}\b|\b{escapedName}\b[^.!?]{{0,40}}\b(she|her|he|him|they|them|sie|er)\b", RegexOptions.IgnoreCase);
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }

    private static IReadOnlyList<CapturedChatMessage> SelectRelevantContextMessages(CapturedChatMessage currentMessage, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        var candidateMessages = recentContext
            .Where(entry => entry.ConversationKey == currentMessage.ConversationKey)
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(12)
            .OrderBy(entry => entry.CapturedAt)
            .ToList();

        return candidateMessages.Count == 0 ? [] : candidateMessages;
    }

    private IReadOnlyList<CapturedChatMessage> SelectRelevantContextMessages(CapturedChatMessage currentMessage, IReadOnlyList<CapturedChatMessage> recentContext, PersonaConfig activePersona)
    {
        var candidateMessages = recentContext
            .Where(entry => entry.ConversationKey == currentMessage.ConversationKey)
            .Where(entry => this.people.IsAllowedForPersonaContext(entry.Sender, activePersona))
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(12)
            .OrderBy(entry => entry.CapturedAt)
            .ToList();

        return candidateMessages.Count == 0 ? [] : candidateMessages;
    }

    private static string ValueOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
