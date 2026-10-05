using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using DalamudEmmy.Persistence;
using DalamudEmmy.Persona;
using Microsoft.Data.Sqlite;

namespace DalamudEmmy.People;

public sealed class PeopleService
{
    private const int OpinionRefreshInterval = 10;

    private static readonly Regex GermanHintRegex = new(@"\b(und|oder|nicht|bitte|danke|hallo|wie|warum|wer|was|wo|ja|nein)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EnglishHintRegex = new(@"\b(and|or|not|please|thanks|hello|how|why|who|what|where|yes|no)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DirectStyleRegex = new(@"\b(you|your|u|du|dich|dir|dein|deine)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlayfulStyleRegex = new(@"[:;][)D(]|haha|hehe|lol|lmao|xD", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PoliteRegex = new(@"\b(please|thanks|thank you|bitte|danke)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BoundaryRiskRegex = new(@"\b(idiot|stupid|shut up|fuck|bitch|moron|dumm|halt die klappe|fick|idiot)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex QuestionRegex = new(@"\?|\b(who|what|when|where|why|how|can|could|would|will|are|is|do|did|hast|bist|kannst|koenntest|wie|warum|wer|was|wo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object syncRoot = new();
    private readonly Dictionary<string, KnownPerson> people = new(StringComparer.OrdinalIgnoreCase);
    private readonly PersistenceService persistence;

    public PeopleService(PersistenceService persistence)
    {
        this.persistence = persistence;
        this.LoadFromDatabase();
    }

    public IReadOnlyList<KnownPerson> KnownPeople
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.people.Values
                    .OrderByDescending(person => person.LastSeenAt)
                    .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public int WhitelistedCount
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.people.Values.Count(person => person.TrustState == PersonTrustState.Whitelisted);
            }
        }
    }

    public int IgnoredCount
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.people.Values.Count(person => person.TrustState == PersonTrustState.Ignored);
            }
        }
    }

    public void RegisterChatSender(string sender)
    {
        var normalizedSender = NormalizeSender(sender);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return;
        }

        lock (this.syncRoot)
        {
            var person = this.EnsurePersonLocked(normalizedSender);
            person.RegisterSeen();
            this.UpdatePersonInDatabase(person);
        }
    }

    public void RecordInteraction(string displayName, string message, XivChatType channelType, int relevanceScore, bool shouldReply)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return;
        }

        lock (this.syncRoot)
        {
            var person = this.EnsurePersonLocked(normalizedSender);
            this.UpdateOpinionLocked(person, message, channelType, relevanceScore, shouldReply);
            this.UpdatePersonInDatabase(person);
        }
    }

    public void SetTrustState(string displayName, PersonTrustState trustState)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return;
        }

        lock (this.syncRoot)
        {
            var person = this.EnsurePersonLocked(normalizedSender);
            person.TrustState = trustState;

            if (trustState == PersonTrustState.Whitelisted)
            {
                person.TrustScore = Math.Max(person.TrustScore, 65);
                person.RelationshipSummary = "Trusted recurring contact";
            }
            else if (trustState == PersonTrustState.Ignored)
            {
                person.TrustScore = Math.Min(person.TrustScore, -40);
                person.BoundaryRiskScore = Math.Max(person.BoundaryRiskScore, 55);
                person.RelationshipSummary = "Ignored or blocked contact";
            }

            this.UpdatePersonInDatabase(person);
        }
    }

    public void ResetPersonKnowledge(string displayName)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return;
        }

        lock (this.syncRoot)
        {
            var person = this.EnsurePersonLocked(normalizedSender);
            person.ResetKnowledge();
            this.DeleteChatHistoryForPerson(person.DisplayName);
            this.UpdatePersonInDatabase(person);
        }
    }

    public string BuildPromptContext(string displayName)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return "Person context:\n- Relationship: unknown\n- Trust level: neutral";
        }

        lock (this.syncRoot)
        {
            if (!this.people.TryGetValue(normalizedSender, out var person))
            {
                return "Person context:\n- Relationship: unknown\n- Trust level: neutral";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Person context:");
            builder.AppendLine($"- Relationship: {ValueOrFallback(person.RelationshipSummary, "new or weakly-known contact")}");
            builder.AppendLine($"- Trust level: {DescribeTrust(person)}");
            builder.AppendLine($"- Preferred language: {ValueOrFallback(person.PreferredLanguage, "unknown")}");
            builder.AppendLine($"- Interaction style: {ValueOrFallback(person.InteractionStyleSummary, "not enough data yet")}");
            builder.AppendLine($"- Boundary note: {DescribeBoundary(person)}");
            builder.AppendLine($"- Memory summary: {ValueOrFallback(person.MemorySummary, "no durable impression yet")}");
            builder.Append($"- Low-info opinion meta: {ValueOrFallback(person.LowInfoOpinionMeta, "not enough data yet")}");
            return builder.ToString();
        }
    }

    public string BuildOpinionPromptContext(string displayName)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender))
        {
            return string.Empty;
        }

        lock (this.syncRoot)
        {
            if (!this.people.TryGetValue(normalizedSender, out var person))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Requested opinion target:");
            builder.AppendLine($"- Name: {person.DisplayName}");
            builder.AppendLine($"- Relationship: {ValueOrFallback(person.RelationshipSummary, "new or weakly-known contact")}");
            builder.AppendLine($"- Trust: {DescribeTrust(person)}");
            builder.AppendLine($"- Affinity score: {person.AffinityScore}");
            builder.AppendLine($"- Engagement score: {person.EngagementScore}");
            builder.AppendLine($"- Boundary note: {DescribeBoundary(person)}");
            builder.AppendLine($"- Interaction style: {ValueOrFallback(person.InteractionStyleSummary, "not enough data yet")}");
            builder.AppendLine($"- Memory summary: {ValueOrFallback(person.MemorySummary, "no durable impression yet")}");
            builder.AppendLine($"- Last interaction: {ValueOrFallback(person.LastInteractionSummary, "no interaction summary yet")}");
            builder.Append($"- Low-info opinion meta: {ValueOrFallback(person.LowInfoOpinionMeta, "not enough data yet")}");
            return builder.ToString();
        }
    }

    public KnownPerson? FindKnownPersonMentionedIn(string message, string? excludedDisplayName = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        lock (this.syncRoot)
        {
            return this.people.Values
                .Where(person => !string.Equals(person.DisplayName, excludedDisplayName, StringComparison.OrdinalIgnoreCase))
                .Where(person => MessageMentionsPerson(message, person.DisplayName))
                .OrderByDescending(person => person.DisplayName.Length)
                .FirstOrDefault();
        }
    }

    public bool IsAllowedForPersonaContext(string displayName, PersonaConfig activePersona)
    {
        var normalizedSender = NormalizeSender(displayName);
        if (string.IsNullOrWhiteSpace(normalizedSender) || activePersona.TrustMode == PersonaTrustMode.None)
        {
            return false;
        }

        if (string.Equals(normalizedSender, activePersona.Name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        lock (this.syncRoot)
        {
            if (!this.people.TryGetValue(normalizedSender, out var person))
            {
                return activePersona.TrustMode == PersonaTrustMode.All && activePersona.ReplyToUnknown;
            }

            if (person.TrustState == PersonTrustState.Ignored)
            {
                return activePersona.ReplyToIgnored;
            }

            if (person.TrustState == PersonTrustState.Whitelisted)
            {
                return true;
            }

            return activePersona.TrustMode == PersonaTrustMode.All;
        }
    }

    private static string NormalizeSender(string sender)
    {
        return sender.Trim();
    }

    private static bool MessageMentionsPerson(string message, string displayName)
    {
        if (message.Contains(displayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var nameParts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (nameParts.Length == 0)
        {
            return false;
        }

        return nameParts.Any(part => part.Length >= 3 && Regex.IsMatch(message, $@"\b{Regex.Escape(part)}\b", RegexOptions.IgnoreCase));
    }

    private void LoadFromDatabase()
    {
        using var connection = this.persistence.GetConnection();
        const string selectSql = @"
            SELECT display_name, trust_state, message_count, preferred_language, trust_score, affinity_score,
                   engagement_score, boundary_risk_score, interaction_style_summary, relationship_summary,
                   memory_summary, last_interaction_summary, low_info_opinion_meta,
                   last_opinion_refresh_message_count, first_seen_at, last_seen_at
            FROM people";

        using var command = new SqliteCommand(selectSql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var person = new KnownPerson(reader.GetString(0))
            {
                TrustState = (PersonTrustState)reader.GetInt32(1),
                MessageCount = reader.GetInt32(2),
                PreferredLanguage = reader.GetString(3),
                TrustScore = reader.GetInt32(4),
                AffinityScore = reader.GetInt32(5),
                EngagementScore = reader.GetInt32(6),
                BoundaryRiskScore = reader.GetInt32(7),
                InteractionStyleSummary = reader.GetString(8),
                RelationshipSummary = reader.GetString(9),
                MemorySummary = reader.GetString(10),
                LastInteractionSummary = reader.GetString(11),
                LowInfoOpinionMeta = reader.GetString(12),
                LastOpinionRefreshMessageCount = reader.GetInt32(13),
                FirstSeenAt = DateTimeOffset.Parse(reader.GetString(14)),
                LastSeenAt = DateTimeOffset.Parse(reader.GetString(15)),
            };

            this.people[person.DisplayName] = person;
        }
    }

    private KnownPerson EnsurePersonLocked(string normalizedSender)
    {
        if (!this.people.TryGetValue(normalizedSender, out var person))
        {
            person = new KnownPerson(normalizedSender);
            this.people.Add(normalizedSender, person);
            this.InsertPersonToDatabase(person);
        }

        return person;
    }

    private void UpdateOpinionLocked(KnownPerson person, string message, XivChatType channelType, int relevanceScore, bool shouldReply)
    {
        var normalizedMessage = message.Trim();
        var detectedLanguage = DetectLanguage(normalizedMessage);
        if (!string.IsNullOrWhiteSpace(detectedLanguage))
        {
            person.PreferredLanguage = detectedLanguage;
        }

        var trustDelta = 0;
        var affinityDelta = 0;
        var engagementDelta = 0;
        var boundaryDelta = 0;

        switch (channelType)
        {
            case XivChatType.TellIncoming:
                affinityDelta += 4;
                engagementDelta += 8;
                break;
            case XivChatType.Party:
                engagementDelta += 3;
                break;
            case XivChatType.Say:
                engagementDelta += 1;
                break;
        }

        if (shouldReply)
        {
            trustDelta += 3;
            engagementDelta += 4;
        }

        if (QuestionRegex.IsMatch(normalizedMessage))
        {
            engagementDelta += 4;
        }

        if (PoliteRegex.IsMatch(normalizedMessage))
        {
            trustDelta += 3;
            affinityDelta += 5;
        }

        if (PlayfulStyleRegex.IsMatch(normalizedMessage))
        {
            affinityDelta += 3;
        }

        if (DirectStyleRegex.IsMatch(normalizedMessage))
        {
            engagementDelta += 3;
        }

        if (BoundaryRiskRegex.IsMatch(normalizedMessage))
        {
            trustDelta -= 10;
            affinityDelta -= 12;
            boundaryDelta += 20;
        }

        if (HasAggressiveFormatting(normalizedMessage))
        {
            affinityDelta -= 4;
            boundaryDelta += 8;
        }

        if (relevanceScore >= 70)
        {
            engagementDelta += 4;
        }

        person.TrustScore = Math.Clamp(person.TrustScore + trustDelta, -100, 100);
        person.AffinityScore = Math.Clamp(person.AffinityScore + affinityDelta, -100, 100);
        person.EngagementScore = Math.Clamp(person.EngagementScore + engagementDelta, 0, 100);
        person.BoundaryRiskScore = Math.Clamp(person.BoundaryRiskScore + boundaryDelta, 0, 100);

        person.LastInteractionSummary = BuildLastInteractionSummary(normalizedMessage, channelType, relevanceScore, shouldReply, detectedLanguage);
        if (ShouldRefreshOpinion(person))
        {
            person.InteractionStyleSummary = BuildInteractionStyleSummary(normalizedMessage, channelType);
            person.RelationshipSummary = BuildRelationshipSummary(person);
            person.MemorySummary = BuildMemorySummary(person);
            person.LowInfoOpinionMeta = BuildLowInfoOpinionMeta(person);
            person.LastOpinionRefreshMessageCount = person.MessageCount;
        }
    }

    private void InsertPersonToDatabase(KnownPerson person)
    {
        using var connection = this.persistence.GetConnection();
        const string insertSql = @"
            INSERT INTO people (
                display_name, trust_state, message_count, preferred_language, trust_score, affinity_score,
                engagement_score, boundary_risk_score, interaction_style_summary, relationship_summary,
                memory_summary, last_interaction_summary, low_info_opinion_meta,
                last_opinion_refresh_message_count, first_seen_at, last_seen_at)
            VALUES (
                @display_name, @trust_state, @message_count, @preferred_language, @trust_score, @affinity_score,
                @engagement_score, @boundary_risk_score, @interaction_style_summary, @relationship_summary,
                @memory_summary, @last_interaction_summary, @low_info_opinion_meta,
                @last_opinion_refresh_message_count, @first_seen_at, @last_seen_at)";

        using var command = new SqliteCommand(insertSql, connection);
        this.BindPersonParameters(command, person);
        command.ExecuteNonQuery();
    }

    private void UpdatePersonInDatabase(KnownPerson person)
    {
        using var connection = this.persistence.GetConnection();
        const string updateSql = @"
            UPDATE people
            SET trust_state = @trust_state,
                message_count = @message_count,
                preferred_language = @preferred_language,
                trust_score = @trust_score,
                affinity_score = @affinity_score,
                engagement_score = @engagement_score,
                boundary_risk_score = @boundary_risk_score,
                interaction_style_summary = @interaction_style_summary,
                relationship_summary = @relationship_summary,
                memory_summary = @memory_summary,
                last_interaction_summary = @last_interaction_summary,
                low_info_opinion_meta = @low_info_opinion_meta,
                last_opinion_refresh_message_count = @last_opinion_refresh_message_count,
                last_seen_at = @last_seen_at
            WHERE display_name = @display_name";

        using var command = new SqliteCommand(updateSql, connection);
        this.BindPersonParameters(command, person);
        command.ExecuteNonQuery();
    }

    private void BindPersonParameters(SqliteCommand command, KnownPerson person)
    {
        command.Parameters.AddWithValue("@display_name", person.DisplayName);
        command.Parameters.AddWithValue("@trust_state", (int)person.TrustState);
        command.Parameters.AddWithValue("@message_count", person.MessageCount);
        command.Parameters.AddWithValue("@preferred_language", person.PreferredLanguage);
        command.Parameters.AddWithValue("@trust_score", person.TrustScore);
        command.Parameters.AddWithValue("@affinity_score", person.AffinityScore);
        command.Parameters.AddWithValue("@engagement_score", person.EngagementScore);
        command.Parameters.AddWithValue("@boundary_risk_score", person.BoundaryRiskScore);
        command.Parameters.AddWithValue("@interaction_style_summary", person.InteractionStyleSummary);
        command.Parameters.AddWithValue("@relationship_summary", person.RelationshipSummary);
        command.Parameters.AddWithValue("@memory_summary", person.MemorySummary);
        command.Parameters.AddWithValue("@last_interaction_summary", person.LastInteractionSummary);
        command.Parameters.AddWithValue("@low_info_opinion_meta", person.LowInfoOpinionMeta);
        command.Parameters.AddWithValue("@last_opinion_refresh_message_count", person.LastOpinionRefreshMessageCount);
        command.Parameters.AddWithValue("@first_seen_at", person.FirstSeenAt.ToString("o"));
        command.Parameters.AddWithValue("@last_seen_at", person.LastSeenAt.ToString("o"));
    }

    private void DeleteChatHistoryForPerson(string displayName)
    {
        using var connection = this.persistence.GetConnection();
        const string deleteSql = "DELETE FROM chat_messages WHERE sender = @sender";

        using var command = new SqliteCommand(deleteSql, connection);
        command.Parameters.AddWithValue("@sender", displayName);
        command.ExecuteNonQuery();
    }

    private static string DetectLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var germanHits = GermanHintRegex.Matches(message).Count;
        var englishHits = EnglishHintRegex.Matches(message).Count;

        if (germanHits > englishHits)
        {
            return "German";
        }

        if (englishHits > germanHits)
        {
            return "English";
        }

        return string.Empty;
    }

    private static bool HasAggressiveFormatting(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var letters = message.Count(char.IsLetter);
        if (letters == 0)
        {
            return false;
        }

        var uppercaseLetters = message.Count(char.IsUpper);
        var uppercaseRatio = (double)uppercaseLetters / letters;
        return uppercaseRatio >= 0.65 || message.Contains("!!!", StringComparison.Ordinal);
    }

    private static string BuildInteractionStyleSummary(string message, XivChatType channelType)
    {
        var traits = new List<string>();

        if (channelType == XivChatType.TellIncoming)
        {
            traits.Add("personally directed");
        }

        if (QuestionRegex.IsMatch(message))
        {
            traits.Add("question-driven");
        }

        if (DirectStyleRegex.IsMatch(message))
        {
            traits.Add("direct");
        }

        if (PlayfulStyleRegex.IsMatch(message))
        {
            traits.Add("playful");
        }

        if (PoliteRegex.IsMatch(message))
        {
            traits.Add("polite");
        }

        if (BoundaryRiskRegex.IsMatch(message) || HasAggressiveFormatting(message))
        {
            traits.Add("pushes boundaries");
        }

        if (traits.Count == 0)
        {
            traits.Add("still being learned");
        }

        return string.Join(", ", traits.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildRelationshipSummary(KnownPerson person)
    {
        if (person.TrustState == PersonTrustState.Ignored || person.BoundaryRiskScore >= 70)
        {
            return "High-caution contact";
        }

        if (person.TrustState == PersonTrustState.Whitelisted || person.TrustScore >= 60)
        {
            return "Trusted recurring contact";
        }

        if (person.AffinityScore >= 35 && person.EngagementScore >= 35)
        {
            return "Warm and familiar contact";
        }

        if (person.MessageCount >= 5 || person.EngagementScore >= 25)
        {
            return "Known contact with developing rapport";
        }

        return "New or lightly known contact";
    }

    private static bool ShouldRefreshOpinion(KnownPerson person)
    {
        return person.MessageCount == 1
            || string.IsNullOrWhiteSpace(person.LowInfoOpinionMeta)
            || person.MessageCount - person.LastOpinionRefreshMessageCount >= OpinionRefreshInterval;
    }

    private static string BuildMemorySummary(KnownPerson person)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(person.PreferredLanguage))
        {
            parts.Add($"usually writes in {person.PreferredLanguage}");
        }

        if (!string.IsNullOrWhiteSpace(person.InteractionStyleSummary) && person.InteractionStyleSummary != "still being learned")
        {
            parts.Add(person.InteractionStyleSummary);
        }

        if (person.BoundaryRiskScore >= 50)
        {
            parts.Add("needs careful boundaries");
        }
        else if (person.AffinityScore >= 25)
        {
            parts.Add("has been mostly pleasant");
        }

        return parts.Count == 0 ? "No stable impression yet" : string.Join("; ", parts);
    }

    private static string BuildLowInfoOpinionMeta(KnownPerson person)
    {
        var trust = person.TrustScore switch
        {
            >= 60 => "trusted",
            >= 20 => "positive",
            <= -40 => "distrusted",
            <= -10 => "cautious",
            _ => "neutral",
        };

        var affinity = person.AffinityScore switch
        {
            >= 35 => "warm",
            >= 10 => "pleasant",
            <= -25 => "strained",
            <= -5 => "cool",
            _ => "unknown rapport",
        };

        var engagement = person.EngagementScore switch
        {
            >= 60 => "frequent contact",
            >= 25 => "recurring contact",
            > 0 => "light contact",
            _ => "new contact",
        };

        var boundary = person.BoundaryRiskScore switch
        {
            >= 70 => "high caution",
            >= 35 => "some caution",
            _ => "no notable boundary issue",
        };

        return $"{trust}, {affinity}, {engagement}, {boundary}";
    }

    private static string BuildLastInteractionSummary(string message, XivChatType channelType, int relevanceScore, bool shouldReply, string detectedLanguage)
    {
        var language = string.IsNullOrWhiteSpace(detectedLanguage) ? "unknown language" : detectedLanguage;
        var questionState = QuestionRegex.IsMatch(message) ? "question-like" : "statement-like";
        var replyState = shouldReply ? "reply-worthy" : "not selected for reply";
        return $"{channelType} in {language}, {questionState}, relevance {relevanceScore}, {replyState}";
    }

    private static string DescribeTrust(KnownPerson person)
    {
        if (person.TrustState == PersonTrustState.Whitelisted)
        {
            return $"whitelisted, trust score {person.TrustScore}";
        }

        if (person.TrustState == PersonTrustState.Ignored)
        {
            return $"ignored, trust score {person.TrustScore}";
        }

        return $"neutral, trust score {person.TrustScore}";
    }

    private static string DescribeBoundary(KnownPerson person)
    {
        if (person.BoundaryRiskScore >= 70)
        {
            return "high caution";
        }

        if (person.BoundaryRiskScore >= 35)
        {
            return "some caution";
        }

        return "no strong issues observed";
    }

    private static string ValueOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
