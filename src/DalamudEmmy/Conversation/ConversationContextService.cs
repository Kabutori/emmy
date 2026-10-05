using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using DalamudEmmy.Chat;
using DalamudEmmy.People;
using DalamudEmmy.Persona;

namespace DalamudEmmy.Conversation;

public sealed class ConversationContextService
{
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromSeconds(60);
    private static readonly Regex QuestionRegex = new(@"\?|\b(who|what|when|where|why|how|can|could|would|will|are|is|do|did|hast|bist|kannst|koenntest|wie|warum|wer|was|wo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GroupPhraseRegex = new(@"\b(we|wir|ihr|alle|everyone|anyone|jemand|someone|wer kann|who can|should we|sollen wir|hat jemand|does anyone)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Configuration configuration;
    private readonly PeopleService people;

    public ConversationContextService(Configuration configuration, PeopleService people)
    {
        this.configuration = configuration;
        this.people = people;
    }

    public ConversationAnalysisResult Analyze(CapturedChatMessage message, IReadOnlyList<CapturedChatMessage> recentContext)
    {
        var activePersona = this.configuration.GetActivePersona();
        var personaName = activePersona?.Name?.Trim() ?? "Emmy";
        var relevantWindow = recentContext
            .Where(entry => entry.ConversationKey == message.ConversationKey && message.CapturedAt - entry.CapturedAt <= ConversationWindow)
            .Where(entry => activePersona == null || this.people.IsAllowedForPersonaContext(entry.Sender, activePersona))
            .OrderBy(entry => entry.CapturedAt)
            .ToArray();

        var activeParticipants = relevantWindow
            .Select(entry => entry.Sender)
            .Append(message.Sender)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var otherMentions = DetectMentionedPeople(message.Message, personaName)
            .Where(name => !string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var emmyMentioned = ContainsName(message.Message, personaName);
        var messageLooksLikeQuestion = QuestionRegex.IsMatch(message.Message);
        var groupPhrase = GroupPhraseRegex.IsMatch(message.Message);
        var sameSenderRecentCount = relevantWindow.Count(entry => string.Equals(entry.Sender, message.Sender, StringComparison.OrdinalIgnoreCase));
        var personaRecentCount = relevantWindow.Count(entry => string.Equals(entry.Sender, personaName, StringComparison.OrdinalIgnoreCase));
        var speakerTransitions = CountSpeakerTransitions(relevantWindow, message.Sender);
        var participantCount = activeParticipants.Count;

        var otherTargetClarity = 0;
        if (otherMentions.Length > 0)
        {
            otherTargetClarity += 40;
            otherTargetClarity += Math.Min(30, (otherMentions.Length - 1) * 15);
            if (MessageStartsWithMention(message.Message, otherMentions))
            {
                otherTargetClarity += 20;
            }
        }

        var groupChatStrength = 0;
        if (participantCount >= 3)
        {
            groupChatStrength += 35;
            groupChatStrength += Math.Min(25, (participantCount - 3) * 10);
        }

        if (speakerTransitions >= 2)
        {
            groupChatStrength += Math.Min(20, speakerTransitions * 5);
        }

        if (groupPhrase)
        {
            groupChatStrength += 20;
        }

        if (message.Type == XivChatType.Say && participantCount >= 3 && !emmyMentioned && otherMentions.Length == 0)
        {
            groupChatStrength += 15;
        }

        groupChatStrength = Math.Clamp(groupChatStrength, 0, 100);

        var isEmmyAddressed = 0;
        if (message.Type == XivChatType.TellIncoming)
        {
            isEmmyAddressed = 100;
        }
        else
        {
            if (emmyMentioned)
            {
                isEmmyAddressed += 60;
                if (MessageStartsWithMention(message.Message, [personaName]))
                {
                    isEmmyAddressed += 15;
                }
            }

            if (messageLooksLikeQuestion)
            {
                isEmmyAddressed += participantCount <= 2 ? 18 : 8;
            }

            if (sameSenderRecentCount > 0)
            {
                isEmmyAddressed += 10;
            }

            if (message.IsSenderTargetingEmmy)
            {
                isEmmyAddressed += activePersona?.SenderTargetingAddressedBonus ?? 45;
            }

            isEmmyAddressed -= otherTargetClarity / 2;
            isEmmyAddressed -= groupChatStrength / 3;
            isEmmyAddressed = Math.Clamp(isEmmyAddressed, 0, 100);

            if (message.IsSenderTargetingEmmy && messageLooksLikeQuestion && otherMentions.Length == 0)
            {
                isEmmyAddressed = Math.Max(isEmmyAddressed, 70);
            }
        }

        var threadParticipation = Math.Clamp((sameSenderRecentCount * 15) + (personaRecentCount * 20) + (emmyMentioned ? 20 : 0) + (message.IsSenderTargetingEmmy ? (activePersona?.SenderTargetingThreadBonus ?? 25) : 0) + (message.Type == XivChatType.TellIncoming ? 35 : 0), 0, 100);
        var replyExpectation = Math.Clamp(isEmmyAddressed + (threadParticipation / 3) - (otherTargetClarity / 2) - (groupChatStrength / 4), 0, 100);

        var conversationType = DetermineConversationType(message.Type, participantCount, emmyMentioned, otherMentions.Length, groupChatStrength, groupPhrase, isEmmyAddressed);

        var likelyAddressees = BuildLikelyAddressees(personaName, emmyMentioned, otherMentions, groupChatStrength, isEmmyAddressed);
        if (isEmmyAddressed >= 40 && !activeParticipants.Contains(personaName, StringComparer.OrdinalIgnoreCase))
        {
            activeParticipants.Add(personaName);
        }

        return new ConversationAnalysisResult
        {
            ConversationType = conversationType,
            LikelyAddressees = likelyAddressees,
            ActiveParticipants = activeParticipants,
            ParticipantCount = participantCount,
            IsEmmyAddressedScore = isEmmyAddressed,
            OtherTargetClarityScore = otherTargetClarity,
            GroupChatStrength = groupChatStrength,
            ReplyExpectationScore = replyExpectation,
            ThreadParticipationScore = threadParticipation,
            EmmyInvolvementLevel = DescribeEmmyInvolvement(isEmmyAddressed, threadParticipation),
            ThreadSummary = BuildThreadSummary(conversationType, activeParticipants, likelyAddressees, groupPhrase, messageLooksLikeQuestion),
        };
    }

    private IReadOnlyList<string> DetectMentionedPeople(string message, string personaName)
    {
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownNames = this.people.KnownPeople.Select(person => person.DisplayName).ToArray();

        if (ContainsName(message, personaName))
        {
            detected.Add(personaName);
        }

        foreach (var name in knownNames)
        {
            if (ContainsName(message, name))
            {
                detected.Add(name);
                continue;
            }

            var firstName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstName) && firstName.Length >= 3 && ContainsName(message, firstName))
            {
                detected.Add(name);
            }
        }

        return detected.ToArray();
    }

    private static ConversationType DetermineConversationType(XivChatType channelType, int participantCount, bool emmyMentioned, int otherMentionCount, int groupChatStrength, bool groupPhrase, int isEmmyAddressed)
    {
        if (channelType == XivChatType.TellIncoming || channelType == XivChatType.TellOutgoing)
        {
            return ConversationType.Direct;
        }

        if (groupChatStrength >= 65 || (participantCount >= 3 && groupPhrase))
        {
            return ConversationType.MultiParty;
        }

        if (groupPhrase && participantCount >= 2)
        {
            return ConversationType.Broadcast;
        }

        if (isEmmyAddressed >= 55 && otherMentionCount == 0)
        {
            return ConversationType.Direct;
        }

        if (participantCount >= 3 && !emmyMentioned && otherMentionCount == 0)
        {
            return ConversationType.Ambient;
        }

        return ConversationType.Unclear;
    }

    private static IReadOnlyList<string> BuildLikelyAddressees(string personaName, bool emmyMentioned, IReadOnlyList<string> otherMentions, int groupChatStrength, int isEmmyAddressed)
    {
        var addressees = new List<string>();

        if (emmyMentioned || isEmmyAddressed >= 70)
        {
            addressees.Add(personaName);
        }

        addressees.AddRange(otherMentions.Where(name => !addressees.Contains(name, StringComparer.OrdinalIgnoreCase)));

        if (addressees.Count == 0 && groupChatStrength >= 50)
        {
            addressees.Add("group");
        }

        if (addressees.Count == 0)
        {
            addressees.Add("unclear");
        }

        return addressees;
    }

    private static string DescribeEmmyInvolvement(int isEmmyAddressed, int threadParticipation)
    {
        var combined = Math.Max(isEmmyAddressed, threadParticipation);
        if (combined >= 75)
        {
            return "high";
        }

        if (combined >= 40)
        {
            return "medium";
        }

        return "low";
    }

    private static string BuildThreadSummary(ConversationType conversationType, IReadOnlyList<string> activeParticipants, IReadOnlyList<string> addressees, bool groupPhrase, bool messageLooksLikeQuestion)
    {
        var participantText = activeParticipants.Count == 0 ? "no active participants" : string.Join(", ", activeParticipants.Take(4));
        var addresseeText = string.Join(", ", addressees);
        var topicHint = groupPhrase
            ? "group-oriented coordination"
            : messageLooksLikeQuestion
                ? "question-driven exchange"
                : "ongoing casual interaction";

        return $"{conversationType} conversation with participants {participantText}; likely addressee {addresseeText}; context looks like {topicHint}";
    }

    private static int CountSpeakerTransitions(IReadOnlyList<CapturedChatMessage> relevantWindow, string currentSender)
    {
        var ordered = relevantWindow.Select(entry => entry.Sender).Append(currentSender).ToArray();
        var transitions = 0;

        for (var i = 1; i < ordered.Length; i++)
        {
            if (!string.Equals(ordered[i - 1], ordered[i], StringComparison.OrdinalIgnoreCase))
            {
                transitions++;
            }
        }

        return transitions;
    }

    private static bool MessageStartsWithMention(string message, IReadOnlyList<string> candidateNames)
    {
        var trimmed = message.TrimStart();
        return candidateNames.Any(name =>
            trimmed.StartsWith(name + ",", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsName(string message, string candidateName)
    {
        return Regex.IsMatch(message, $@"\b{Regex.Escape(candidateName)}\b", RegexOptions.IgnoreCase);
    }
}
