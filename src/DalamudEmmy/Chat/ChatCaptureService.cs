using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using DalamudEmmy.People;
using DalamudEmmy.Persistence;
using DalamudEmmy.Provider;
using DalamudEmmy.Reply;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace DalamudEmmy.Chat;

public sealed class ChatCaptureService : IDisposable
{
    private const int MaxRecentMessages = 25;
    private const int MaxRequestsPerMinute = 10;
    private const float MaxReplyTargetDistance = 30.0f;
    private static readonly Regex OpinionQuestionRegex = new(@"\b(opinion|think of|think about|feel about|h[aä]ltst du|denkst du|meinung|findest du|was sagst du zu)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IChatGui chatGui;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly ITargetManager targetManager;
    private readonly Configuration configuration;
    private readonly PeopleService people;
    private readonly PersistenceService persistence;
    private readonly ReplyDecisionService replyDecision;
    private readonly LLMRequestService llmRequest;
    private readonly Action<Action> enqueueOnMainThread;
    private readonly IPluginLog log;
    private readonly Emmy.Core.Lifetime lifetime = new();
    private bool disposed;
    public string LastError => llmRequest.LastError;
    public void Stop() => lifetime.Stop();
    private readonly object syncRoot = new();
    private readonly List<CapturedChatMessage> recentMessages = [];
    private readonly Queue<DateTimeOffset> requestTimestamps = new();

    public ChatCaptureService(IChatGui chatGui, IObjectTable objectTable, IPartyList partyList, ITargetManager targetManager, Configuration configuration, PeopleService people, PersistenceService persistence, ReplyDecisionService replyDecision, LLMRequestService llmRequest, Action<Action> enqueueOnMainThread, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.targetManager = targetManager;
        this.configuration = configuration;
        this.people = people;
        this.persistence = persistence;
        this.replyDecision = replyDecision;
        this.llmRequest = llmRequest;
        this.enqueueOnMainThread = enqueueOnMainThread;
        this.log = log;

        this.log.Information("ChatCaptureService initialized, registering ChatMessageUnhandled handler");
        this.configuration.Changed += Stop;
        this.chatGui.ChatMessageUnhandled += this.OnChatMessageUnhandled;
        this.log.Information("ChatMessageUnhandled handler registered successfully");
    }

    public int TotalCaptured { get; private set; }

    public IReadOnlyList<CapturedChatMessage> RecentMessages
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.recentMessages.ToArray();
            }
        }
    }

    public int DeleteChatHistoryForSender(string sender)
    {
        lock (this.syncRoot)
        {
            Stop();
            var count = this.recentMessages.RemoveAll(m => string.Equals(m.SenderWithWorld, sender, StringComparison.OrdinalIgnoreCase) || m.ConversationKey == "tell:"+sender.ToUpperInvariant());
            using var connection = persistence.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM chat_messages WHERE sender=@sender OR conversation_key=@key";
            command.Parameters.AddWithValue("@sender",sender);
            command.Parameters.AddWithValue("@key","tell:"+sender.ToUpperInvariant());
            count += command.ExecuteNonQuery();
            this.log.Information("Deleted {Count} chat messages for sender: {Sender}", count, sender);
            return count;
        }
    }

    public void SendReply(string message, XivChatType channelType, string? targetName = null, bool confirmed = false, CapturedChatMessage? draft = null)
    {
        this.log.Information("SendReply called: Message={Message}, ChannelType={ChannelType}, TargetName={TargetName}, OperatingMode={OperatingMode}", message, channelType, targetName ?? "null", this.configuration.OperatingMode);

        if (disposed || configuration.UseCompanionHost || !configuration.IsEnabled || !configuration.CaptureChatMessages || (this.configuration.OperatingMode != OperatingMode.Active && !(confirmed && configuration.OperatingMode == OperatingMode.Assisted)))
        {
            this.log.Information("SendReply aborted: OperatingMode is not Active");
            return;
        }

        var generation = lifetime.Snapshot.Generation;
        if (draft is not null && (draft.ReplyConsumed || draft.Generation != generation)) return;
        message = NormalizeCommandText(message);
        if (message == "NO_REPLY") return;
        if (string.IsNullOrWhiteSpace(message))
        {
            this.log.Warning("SendReply aborted: message is empty after normalization");
            return;
        }

        string? commandText = null;
        switch (channelType)
        {
            case XivChatType.Say:
                commandText = $"/s {message}";
                break;
            case XivChatType.TellIncoming:
            case XivChatType.TellOutgoing:
                targetName = NormalizeCommandText(targetName ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    commandText = $"/tell {targetName} {message}";
                }
                else
                {
                    this.log.Warning("Tell reply aborted: target name is empty");
                }
                break;
            case XivChatType.Party:
                commandText = $"/p {message}";
                break;
            default:
                this.log.Information("Sending reply via Print (default)");
                this.chatGui.Print(message);
                break;
        }

        if (!string.IsNullOrWhiteSpace(commandText))
        {
            this.log.Information("Queueing reply command for main-thread execution: {CommandText}", commandText);
            if (System.Text.Encoding.UTF8.GetByteCount(commandText) > 500) return;
            if(channelType is XivChatType.TellIncoming or XivChatType.TellOutgoing)
            {
                commandText = Emmy.Core.ChatCommand.Build(Emmy.Core.Channel.Tell,message,draft?.SenderIdentity);
                if(commandText is null)return;
            }
            var replyTargetName = targetName;
            if (draft is not null) draft.ReplyConsumed = true;
            this.enqueueOnMainThread(() =>
            {
                if (disposed || !lifetime.IsCurrent(generation) || !configuration.IsEnabled || !configuration.CaptureChatMessages || configuration.UseCompanionHost ||
                    (configuration.OperatingMode != OperatingMode.Active && !(confirmed && configuration.OperatingMode == OperatingMode.Assisted))) return;
                this.TargetReplyRecipient(replyTargetName, channelType);
                if (this.ExecuteGameCommand(commandText)) this.RecordOwnReply(message, channelType, replyTargetName);
            });
        }
    }

    public void Dispose()
    {
        disposed = true;
        Stop();
        configuration.Changed -= Stop;
        this.chatGui.ChatMessageUnhandled -= this.OnChatMessageUnhandled;
    }

    private void OnChatMessageUnhandled(IChatMessage message)
    {
        var typeLabel = message.LogKind switch
        {
            XivChatType.TellIncoming => "TellIncoming",
            XivChatType.TellOutgoing => "TellOutgoing",
            _ => message.LogKind.ToString()
        };
        this.log.Information("Chat message received: Type={Type}, Sender={Sender}, Message={Message}", typeLabel, message.Sender, message.Message);

        if (this.configuration.UseCompanionHost || this.configuration.OperatingMode == OperatingMode.Disabled || !this.configuration.IsEnabled || !this.configuration.CaptureChatMessages)
        {
            this.log.Information("Plugin disabled or chat capture paused");
            return;
        }

        if (!IsRelevantChatType(message.LogKind))
        {
            this.log.Information("Chat type not relevant: {Type}", message.LogKind);
            return;
        }

        var sender = message.Sender.ToString();
        var senderWithWorld = sender;
        Emmy.Core.Identity? senderIdentity = null;

        foreach (var payload in message.Sender.Payloads)
        {
            if (payload is Dalamud.Game.Text.SeStringHandling.Payloads.PlayerPayload playerPayload)
            {
                if (playerPayload.World.RowId != 0)
                {
                    senderWithWorld = $"{playerPayload.PlayerName}@{playerPayload.World.Value.Name}";
                    senderIdentity = new(playerPayload.PlayerName,playerPayload.World.RowId,playerPayload.World.Value.Name.ToString());
                }
                break;
            }
        }

        if (this.IsOwnMessage(sender))
        {
            this.log.Information("Own message ignored: {Sender}", sender);
            return;
        }

        this.log.Information("Processing message from {Sender} ({SenderWithWorld})", sender, senderWithWorld);

        var capturedMessage = new CapturedChatMessage(
            DateTimeOffset.Now,
            message.LogKind,
            senderWithWorld,
            message.Message.ToString())
        {
            SenderWithWorld = senderWithWorld,
            SenderIdentity = senderIdentity,
            Generation = lifetime.Snapshot.Generation,
            ConversationKey = message.LogKind is XivChatType.TellIncoming or XivChatType.TellOutgoing ? "tell:"+senderWithWorld.ToUpperInvariant() : message.LogKind.ToString(),
        };

        var targetingInfo = this.DetectWhetherSenderTargetsEmmy(capturedMessage.SenderWithWorld);
        capturedMessage.IsSenderTargetingEmmy = targetingInfo.IsTargetingEmmy;
        capturedMessage.TargetingDetectionNote = targetingInfo.Note;
        this.log.Information(
            "Targeting signal: Sender={Sender}, SenderWithWorld={SenderWithWorld}, IsTargetingEmmy={IsTargetingEmmy}, Note={Note}",
            capturedMessage.Sender,
            capturedMessage.SenderWithWorld,
            capturedMessage.IsSenderTargetingEmmy,
            capturedMessage.TargetingDetectionNote);

        this.people.RegisterChatSender(capturedMessage.SenderWithWorld);
        this.SaveMessageToDatabase(capturedMessage);
        var recentContext = this.GetRecentContextSnapshot();

        lock (this.syncRoot)
        {
            this.recentMessages.Add(capturedMessage);
            this.TotalCaptured++;

            if (this.recentMessages.Count > MaxRecentMessages)
            {
                this.recentMessages.RemoveAt(0);
            }
        }

        _ = this.ProcessCapturedMessageAsync(capturedMessage, recentContext);
    }

    private async Task RequestSuggestedReplyAsync(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        try
        {
            this.RecordRequest();
            var prompt = this.BuildReplyPrompt(message, recentContext);
            var result = await this.llmRequest.GetResponseAsync(prompt, lifetime.Snapshot.Token);
            if (!result.Success || !lifetime.IsCurrent(message.Generation)) { message.IsReplyPending = false; return; }
            var response = result.Text;

            // Sanitize response: remove linebreaks, take first non-empty line or replace with spaces
            response = response.Replace("\r", "").Replace("\n", " ").Trim();
            // Remove markdown quotes if the LLM still added them
            if (response.StartsWith("\"") && response.EndsWith("\""))
            {
                response = response.Substring(1, response.Length - 2).Trim();
            }

            lock (this.syncRoot)
            {
                message.SuggestedReply = response;
                message.IsReplyPending = false;
            }

            // Auto-send if OperatingMode is Active
            if (this.configuration.OperatingMode == OperatingMode.Active)
            {
                this.log.Information("Auto-sending reply due to Active mode");
                this.SendReply(response, message.Type, message.SenderWithWorld, draft: message);
            }
        }
        catch
        {
            lock (this.syncRoot)
            {
                message.IsReplyPending = false;
            }
        }
    }

    private string BuildReplyPrompt(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        var activePersona = this.configuration.GetActivePersona();
        var personaName = activePersona?.Name ?? "Emmy";
        var currentSettings = activePersona?.GetCurrentModeSettings();
        var anchoredKnowledge = activePersona?.AnchoredKnowledge.Trim() ?? string.Empty;
        var description = activePersona?.Description.Trim() ?? string.Empty;
        var systemPrompt = currentSettings?.SystemPrompt.Trim() ?? string.Empty;
        var tone = currentSettings?.Tone.Trim() ?? "neutral";
        var verbosity = currentSettings?.Verbosity.Trim() ?? "medium";
        var noGoRules = currentSettings?.NoGoRules.Trim() ?? string.Empty;

        var prompt =
            $"You are {personaName}. You are not a generic assistant. You are {personaName} and you should speak as {personaName}.\n" +
            $"IDENTITY RULE: Stay aware that you are {personaName}. Do not describe {personaName} as a separate person. Do not talk about {personaName} in third person when referring to yourself.\n" +
            $"IDENTITY RULE: Reply from your own perspective as {personaName}, using first person when natural.\n" +
            $"CRITICAL LANGUAGE RULE: Reply in the same language as the sender's message. Mirror the sender's language choice. " +
            $"If the sender mixes languages, prefer the dominant language of their message and do not switch to another language on your own.\n" +
            $"STYLE RULE: Keep the reply natural and conversational.\n" +
            $"STYLE RULE: Do not use emojis.\n" +
            $"STYLE RULE: Avoid dash symbols such as em dashes, en dashes, and decorative separators. Prefer plain sentences, commas, or periods instead.\n" +
            $"STYLE RULE: Tone should be {tone}. Verbosity should be {verbosity}.\n";

        if (!string.IsNullOrWhiteSpace(description))
        {
            prompt += $"PERSONA DESCRIPTION: {description}\n";
        }

        if (!string.IsNullOrWhiteSpace(anchoredKnowledge))
        {
            prompt += $"ANCHORED KNOWLEDGE: {anchoredKnowledge}\n";
            prompt += "CRITICAL RULE: Treat the anchored knowledge as true background context.\n";
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            prompt += $"SYSTEM PROMPT: {systemPrompt}\n";
        }

        if (!string.IsNullOrWhiteSpace(noGoRules))
        {
            prompt += $"NO GO RULES: {noGoRules}\n";
            prompt += "CRITICAL RULE: Never violate the NO GO RULES.\n";
        }

        prompt +=
            "Current interlocutor:\n" +
            $"- Name: {message.SenderWithWorld}\n" +
            $"- You are speaking with: {message.SenderWithWorld}\n" +
            $"- Relationship context: use the person context and anchored knowledge when shaping your reply.\n";

        prompt += $"{this.people.BuildPromptContext(message.SenderWithWorld)}\n";
        prompt += this.BuildRequestedOpinionContext(message);
        prompt += this.BuildConversationContextBlock(message);

        var conversationHistory = this.SelectConversationContextMessages(message, recentContext);
        if (conversationHistory.Count > 0)
        {
            prompt +=
                "\nRecent conversation turns (state, not new obligations):\n" +
                "These turns show what has already happened, including your own previous replies. Use them to avoid repeating yourself. The current message below is the only turn you are answering now.\n";
            foreach (var entry in conversationHistory)
            {
                var senderLabel = this.GetConversationSpeakerLabel(entry, message);
                prompt += $"- [{entry.Type}] {senderLabel}: {entry.Message}\n";
            }
        }

        prompt +=
            "\nOUTPUT RULE: Output ONLY the raw reply text, no markdown, no quotes, no speaker labels, no extra options, and on a single line.\n\n" +
            "CONVERSATION FLOW RULE: Treat chat as turn-taking. First decide what the current message is doing: ping, greeting, question, answer, reaction, correction, or continuation. Then respond only to that act.\n" +
            "CURRENT MESSAGE RULE: Reply to the current message below, not to an older context line. Older turns can resolve references and show what was already answered, but they must not create a new answer unless the current message explicitly asks for it.\n" +
            "PING RULE: If the current message is only your name or a short ping, do not answer older questions. Acknowledge the ping briefly or ask what they need.\n" +
            "ABOUT-YOU RULE: If the current message talks about you in third person rather than to you, do not become defensive or over-explain. If you reply at all, keep it light and brief.\n" +
            "ACKNOWLEDGEMENT RULE: If the current message is only an acknowledgement, thanks, or reassurance after you just replied, usually do not add a new topic. A very short acknowledgement is enough.\n" +
            "CLARIFICATION RULE: If the current message asks about a word or phrase from your previous reply, explain that word or phrase briefly in context.\n" +
            $"Sender: {message.SenderWithWorld}\n" +
            $"Channel: {message.Type}\n" +
            $"Message: {message.Message}";

        return prompt;
    }

    private string BuildConversationContextBlock(CapturedChatMessage message)
    {
        return
            "Conversation context:\n" +
            $"- Channel: {message.Type}\n" +
            $"- Conversation type: {message.ConversationType}\n" +
            $"- Active participants: {ValueOrFallback(message.ActiveParticipants, "unknown")}\n" +
            $"- Likely addressee(s): {ValueOrFallback(message.DetectedAddressees, "unclear")}\n" +
            $"- Emmy involvement: {message.EmmyInvolvementLevel}\n" +
            $"- Reply expectation for Emmy: {message.ReplyExpectationScore}\n" +
            $"- IsEmmyAddressedScore: {message.IsEmmyAddressedScore}\n" +
            $"- GroupChatStrength: {message.GroupChatStrength}\n" +
            $"- Current thread summary: {ValueOrFallback(message.ConversationThreadSummary, "no thread summary")}\n";
    }

    private string BuildRequestedOpinionContext(CapturedChatMessage message)
    {
        if (!OpinionQuestionRegex.IsMatch(message.Message))
        {
            return string.Empty;
        }

        var mentionedPerson = this.people.FindKnownPersonMentionedIn(message.Message, message.Sender);
        if (mentionedPerson == null)
        {
            return string.Empty;
        }

        return
            "\nOpinion request context:\n" +
            "The current message asks what you think about this person. Use only the stored impression below and speak naturally as yourself. Do not invent private facts.\n" +
            $"{this.people.BuildOpinionPromptContext(mentionedPerson.DisplayName)}\n";
    }

    private async Task ProcessCapturedMessageAsync(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        try
        {
            if (!lifetime.IsCurrent(message.Generation)) return;
            recentContext = recentContext.Where(m=>m.ConversationKey == message.ConversationKey).ToArray();
            var evaluation = await this.replyDecision.EvaluateAsync(message, recentContext, lifetime.Snapshot.Token);
            if (!lifetime.IsCurrent(message.Generation)) return;

            lock (this.syncRoot)
            {
                message.ReplyDecision = evaluation.Decision;
                message.RelevanceScore = evaluation.Score;
                message.RelevanceReason = evaluation.Reason;
                message.WasAiRelevanceChecked = evaluation.WasAiEvaluated;
                message.ConversationType = evaluation.ConversationAnalysis.ConversationType;
                message.DetectedAddressees = string.Join(", ", evaluation.ConversationAnalysis.LikelyAddressees);
                message.ActiveParticipants = string.Join(", ", evaluation.ConversationAnalysis.ActiveParticipants);
                message.ParticipantCount = evaluation.ConversationAnalysis.ParticipantCount;
                message.IsEmmyAddressedScore = evaluation.ConversationAnalysis.IsEmmyAddressedScore;
                message.OtherTargetClarityScore = evaluation.ConversationAnalysis.OtherTargetClarityScore;
                message.GroupChatStrength = evaluation.ConversationAnalysis.GroupChatStrength;
                message.ReplyExpectationScore = evaluation.ConversationAnalysis.ReplyExpectationScore;
                message.ThreadParticipationScore = evaluation.ConversationAnalysis.ThreadParticipationScore;
                message.EmmyInvolvementLevel = evaluation.ConversationAnalysis.EmmyInvolvementLevel;
                message.ConversationThreadSummary = evaluation.ConversationAnalysis.ThreadSummary;
            }

            this.LogReplyEvaluation(message, evaluation);

            this.people.RecordInteraction(
                message.SenderWithWorld,
                message.Message,
                message.Type,
                evaluation.Score,
                evaluation.Decision == ReplyDecision.ShouldReply);

            if (evaluation.Decision != ReplyDecision.ShouldReply)
            {
                return;
            }

            if (this.CanMakeRequest())
            {
                this.log.Information("Requesting LLM reply after relevance pass");
                lock (this.syncRoot)
                {
                    message.IsReplyPending = true;
                }

                await this.RequestSuggestedReplyAsync(message, recentContext);
            }
            else
            {
                this.log.Information("Rate limit reached, skipping LLM request");
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Failed to process captured message");

            lock (this.syncRoot)
            {
                message.ReplyDecision = ReplyDecision.Unknown;
                message.RelevanceReason = "Processing error";
            }
        }
    }

    private bool CanMakeRequest()
    {
        lock (this.syncRoot)
        {
            var now = DateTimeOffset.Now;
            var oneMinuteAgo = now.AddMinutes(-1);

            while (this.requestTimestamps.Count > 0 && this.requestTimestamps.Peek() < oneMinuteAgo)
            {
                this.requestTimestamps.Dequeue();
            }

            return this.requestTimestamps.Count < MaxRequestsPerMinute;
        }
    }

    private void LogReplyEvaluation(CapturedChatMessage message, ReplyEvaluationResult evaluation)
    {
        var activePersona = this.configuration.GetActivePersona();
        var threshold = activePersona?.AutoReplyScoreThreshold ?? 0;
        var aiMinScore = activePersona?.AiRelevanceMinScore ?? 0;
        var conversation = evaluation.ConversationAnalysis;

        this.log.Information(
            """
            Reply evaluation complete:
              Decision: {Decision}
              Score: {Score}
              Thresholds:
                AutoReply: {AutoReplyThreshold}
                AiMin: {AiMinScore}
                AiChecked: {AiChecked}
              Message:
                Channel: {Channel}
                Sender: {Sender}
                Text: {Message}
              Targeting:
                SenderTargetsEmmy: {SenderTargetsEmmy}
                Note: {TargetingNote}
                ScoreBonus: {TargetingScoreBonus}
                AddressedBonus: {TargetingAddressedBonus}
                ThreadBonus: {TargetingThreadBonus}
              Conversation:
                Type: {ConversationType}
                ActiveParticipants: {ActiveParticipants}
                LikelyAddressees: {LikelyAddressees}
                IsEmmyAddressedScore: {IsEmmyAddressedScore}
                ReplyExpectationScore: {ReplyExpectationScore}
                ThreadParticipationScore: {ThreadParticipationScore}
                OtherTargetClarityScore: {OtherTargetClarityScore}
                GroupChatStrength: {GroupChatStrength}
                EmmyInvolvement: {EmmyInvolvement}
                Summary: {ThreadSummary}
              Reason:
                {Reason}
            """,
            evaluation.Decision,
            evaluation.Score,
            threshold,
            aiMinScore,
            evaluation.WasAiEvaluated,
            message.Type,
            message.SenderWithWorld,
            message.Message,
            message.IsSenderTargetingEmmy,
            ValueOrFallback(message.TargetingDetectionNote, "no targeting signal"),
            activePersona?.SenderTargetingScoreBonus ?? 0,
            activePersona?.SenderTargetingAddressedBonus ?? 0,
            activePersona?.SenderTargetingThreadBonus ?? 0,
            conversation.ConversationType,
            string.Join(", ", conversation.ActiveParticipants),
            string.Join(", ", conversation.LikelyAddressees),
            conversation.IsEmmyAddressedScore,
            conversation.ReplyExpectationScore,
            conversation.ThreadParticipationScore,
            conversation.OtherTargetClarityScore,
            conversation.GroupChatStrength,
            conversation.EmmyInvolvementLevel,
            conversation.ThreadSummary,
            evaluation.Reason);
    }

    private void RecordRequest()
    {
        lock (this.syncRoot)
        {
            this.requestTimestamps.Enqueue(DateTimeOffset.Now);
        }
    }

    private bool IsOwnMessage(string sender)
    {
        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        var localPlayerName = localPlayer.Name.ToString();
        return string.Equals(sender, localPlayerName, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveMessageToDatabase(CapturedChatMessage message)
    {
        using var connection = this.persistence.GetConnection();
        const string insertSql = @"
            INSERT INTO chat_messages (captured_at, chat_type, sender, message, conversation_key)
            VALUES (@captured_at, @chat_type, @sender, @message, @key)";

        using var command = new SqliteCommand(insertSql, connection);
        command.Parameters.AddWithValue("@captured_at", message.CapturedAt.ToString("o"));
        command.Parameters.AddWithValue("@chat_type", (int)message.Type);
        command.Parameters.AddWithValue("@sender", message.SenderWithWorld);
        command.Parameters.AddWithValue("@key", message.ConversationKey);
        command.Parameters.AddWithValue("@message", message.Message);
        command.ExecuteNonQuery();
    }

    private static bool IsRelevantChatType(XivChatType chatType)
    {
        return chatType is XivChatType.Say
            or XivChatType.TellIncoming
            or XivChatType.Party;
    }

    private IReadOnlyList<CapturedChatMessage> GetRecentContextSnapshot()
    {
        lock (this.syncRoot)
        {
            return this.recentMessages.ToArray();
        }
    }

    private bool ExecuteGameCommand(string commandText)
    {
        this.log.Information("Executing queued reply command: {CommandText}", commandText);

        try
        {
            unsafe
            {
                var uiModule = UIModule.Instance();
                if (uiModule == null)
                {
                    this.log.Error("Reply command failed: UIModule is null");
                    return false;
                }

                var chatModule = uiModule->GetRaptureShellModule();
                if (chatModule == null)
                {
                    this.log.Error("Reply command failed: RaptureShellModule is null");
                    return false;
                }

                var utf8Command = Utf8String.FromString(commandText);
                chatModule->ExecuteCommandInner(utf8Command, uiModule);
                utf8Command->Dtor(true);
                return true;
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Reply command failed during execution");
            return false;
        }
    }

    private void RecordOwnReply(string message, XivChatType channelType, string? recipient)
    {
        var personaName = this.configuration.GetActivePersona()?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(personaName))
        {
            personaName = "Emmy";
        }

        var contextType = channelType == XivChatType.TellIncoming
            ? XivChatType.TellOutgoing
            : channelType;

        var ownMessage = new CapturedChatMessage(DateTimeOffset.Now, contextType, personaName, message)
        {
            SenderWithWorld = personaName,
            IsOwn = true,
            ConversationKey = channelType is XivChatType.TellIncoming or XivChatType.TellOutgoing ? "tell:"+(recipient??"").ToUpperInvariant() : channelType.ToString(),
            ReplyDecision = ReplyDecision.ShouldReply,
            RelevanceReason = "Own outgoing reply",
        };

        SaveMessageToDatabase(ownMessage);
        lock (this.syncRoot)
        {
            this.recentMessages.Add(ownMessage);
            if (this.recentMessages.Count > MaxRecentMessages)
            {
                this.recentMessages.RemoveAt(0);
            }
        }
    }

    private void TargetReplyRecipient(string? targetName, XivChatType channelType)
    {
        if (!CanTargetReplyRecipientForChannel(channelType))
        {
            return;
        }

        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            this.log.Information("Reply target skipped: local player unavailable");
            return;
        }

        var normalizedTargetName = NormalizeTargetName(targetName);
        if (string.IsNullOrWhiteSpace(normalizedTargetName))
        {
            this.log.Information("Reply target skipped: target name unavailable");
            return;
        }

        var candidates = this.objectTable.PlayerObjects
            .Where(player => player.IsValid())
            .Where(player => player.IsTargetable)
            .Where(player => !string.Equals(player.Name.ToString(), localPlayer.Name.ToString(), StringComparison.OrdinalIgnoreCase))
            .Where(player => string.Equals(player.Name.ToString(), normalizedTargetName, StringComparison.OrdinalIgnoreCase))
            .Where(player => targetName is not null && targetName.Contains("@") && string.Equals((player as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)?.HomeWorld.Value.Name.ToString(), targetName.Split("@")[1], StringComparison.OrdinalIgnoreCase))
            .Select(player => new
            {
                Player = player,
                Distance = System.Numerics.Vector3.Distance(player.Position, localPlayer.Position),
            })
            .Where(candidate => candidate.Distance <= MaxReplyTargetDistance)
            .OrderBy(candidate => candidate.Distance)
            .ToArray();

        if (candidates.Length == 0)
        {
            this.log.Information("Reply target skipped: no nearby targetable player found for {TargetName}", normalizedTargetName);
            return;
        }

        var closest = candidates[0];
        if (candidates.Length > 1 && Math.Abs(candidates[1].Distance - closest.Distance) < 0.1f)
        {
            this.log.Warning("Reply target skipped: ambiguous nearby player match for {TargetName}", normalizedTargetName);
            return;
        }

        this.targetManager.Target = closest.Player;
        this.log.Information("Reply target set: {TargetName}, Distance={Distance:0.0}", closest.Player.Name, closest.Distance);
    }

    private static bool CanTargetReplyRecipientForChannel(XivChatType channelType)
    {
        return channelType is XivChatType.Say
            or XivChatType.Party
            or XivChatType.TellIncoming
            or XivChatType.TellOutgoing;
    }

    private static string NormalizeTargetName(string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return string.Empty;
        }

        var normalized = NormalizeCommandText(targetName);
        var worldSeparatorIndex = normalized.IndexOf('@', StringComparison.Ordinal);
        return worldSeparatorIndex > 0
            ? normalized[..worldSeparatorIndex].Trim()
            : normalized;
    }

    private static string NormalizeCommandText(string text)
    {
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string ValueOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private (bool IsTargetingEmmy, string Note) DetectWhetherSenderTargetsEmmy(string sender)
    {
        var localPlayer = this.objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return (false, "local player unavailable");
        }

        var notes = new List<string>();
        var localGameObjectId = localPlayer.GameObjectId;
        var senderObjectName = NormalizeTargetName(sender);
        if (string.IsNullOrWhiteSpace(senderObjectName))
        {
            return (false, "sender name unavailable for targeting lookup");
        }

        // Check nearby objects first
        var matchingObjects = this.objectTable.PlayerObjects
            .Where(player => string.Equals(player.Name.ToString(), senderObjectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingObjects.Length > 0)
        {
            unsafe
            {
                try
                {
                    var localPlayerPtr = (GameObject*)localPlayer.Address;
                    if (localPlayerPtr == null)
                    {
                        return (notes.Count > 0, string.Join("; ", notes));
                    }

                    var localPosition = localPlayerPtr->Position;
                    var localClientObjectId = localPlayerPtr->GetGameObjectId();
                    var isLookingAtEmmy = false;
                    var isClientStructTargetingEmmy = false;

                    foreach (var player in matchingObjects)
                    {
                        var playerPtr = (GameObject*)player.Address;
                        if (playerPtr == null)
                        {
                            continue;
                        }

                        var characterPtr = (CharacterStruct*)player.Address;
                        if (characterPtr != null && characterPtr->GetTargetId() == localClientObjectId)
                        {
                            isClientStructTargetingEmmy = true;
                            notes.Add("targets Emmy via ClientStructs");
                        }

                        var playerPosition = playerPtr->Position;
                        var playerRotation = playerPtr->Rotation;

                        // Calculate direction vector from player to local player
                        var directionToEmmy = localPosition - playerPosition;
                        var distance = (float)Math.Sqrt(directionToEmmy.X * directionToEmmy.X + directionToEmmy.Y * directionToEmmy.Y + directionToEmmy.Z * directionToEmmy.Z);
                        if (distance < 0.001f)
                        {
                            continue; // Too close to determine direction
                        }

                        directionToEmmy.X /= distance;
                        directionToEmmy.Y /= distance;
                        directionToEmmy.Z /= distance;

                        // Convert rotation to direction vector
                        var facingX = (float)Math.Sin(playerRotation);
                        var facingZ = (float)Math.Cos(playerRotation);
                        var facingVector = new System.Numerics.Vector2(facingX, facingZ);

                        // Calculate dot product to check if facing towards Emmy
                        var dotProduct = facingVector.X * directionToEmmy.X + facingVector.Y * directionToEmmy.Z;
                        const float facingThreshold = 0.707f; // ~45 degrees

                        if (dotProduct > facingThreshold)
                        {
                            isLookingAtEmmy = true;
                            notes.Add("looks at Emmy");
                            break;
                        }
                    }

                    if (isClientStructTargetingEmmy || isLookingAtEmmy)
                    {
                        return (true, string.Join("; ", notes));
                    }
                }
                catch
                {
                    return (false, "ClientStructs targeting read failed");
                }
            }
        }

        return (false, $"matched sender object '{senderObjectName}' does not target or look at Emmy");
    }

    private IReadOnlyList<CapturedChatMessage> SelectConversationContextMessages(CapturedChatMessage currentMessage, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        var activePersona = this.configuration.GetActivePersona();
        if (activePersona == null)
        {
            return [];
        }

        if (ShouldSuppressHistoryForCurrentMessage(currentMessage))
        {
            return [];
        }

        IEnumerable<CapturedChatMessage> query = recentContext;

        if (currentMessage.Type == XivChatType.TellIncoming || currentMessage.Type == XivChatType.TellOutgoing)
        {
            query = query
                .Where(entry => entry.Type == XivChatType.TellIncoming || entry.Type == XivChatType.TellOutgoing)
                .Where(entry => entry.ConversationKey == currentMessage.ConversationKey);
        }
        else
        {
            query = query
                .Where(entry => entry.Type == currentMessage.Type)
                .Where(entry => this.people.IsAllowedForPersonaContext(entry.Sender, activePersona));
        }

        var conversationMessages = query
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(8)
            .OrderBy(entry => entry.CapturedAt)
            .ToList();

        return conversationMessages;
    }

    private string GetConversationSpeakerLabel(CapturedChatMessage entry, CapturedChatMessage currentMessage)
    {
        var personaName = this.configuration.GetActivePersona()?.Name?.Trim() ?? "Emmy";

        if (entry.IsOwn || string.Equals(entry.Sender, personaName, StringComparison.OrdinalIgnoreCase))
        {
            return "You";
        }

        if (string.Equals(entry.SenderWithWorld, currentMessage.SenderWithWorld, StringComparison.OrdinalIgnoreCase))
        {
            return "Current interlocutor";
        }

        return entry.SenderWithWorld;
    }

    private static bool ShouldSuppressHistoryForCurrentMessage(CapturedChatMessage message)
    {
        if (message.Type is not (XivChatType.Say or XivChatType.Party))
        {
            return false;
        }

        var normalizedMessage = message.Message.Trim();
        var strippedMessage = normalizedMessage.Trim(',', ':', '.', '!', '?', '-', '_', '~', '\'', '"', ' ');
        return string.IsNullOrWhiteSpace(strippedMessage)
            || strippedMessage.Length <= 1;
    }
}
