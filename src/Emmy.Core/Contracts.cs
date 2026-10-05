using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emmy.Core;

public static class Wire
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
public enum Mode { Off, Observe, Assisted, Companion }
public enum Channel { Say, Tell, Party, Operator }
public enum ActionKind { Chat, Follow, Move, Interact, Menu, Teleport, Aethernet, ChangeWorld, Emote, Capture, Stop }
public enum Outcome { Accepted, Succeeded, Failed, Cancelled, Unknown }
public record Identity(string Name, uint HomeWorld, string WorldName = "")
{
    public string Key => $"{Name.Trim().ToUpperInvariant()}@{HomeWorld}";
    public string Display => string.IsNullOrEmpty(WorldName) ? Name : $"{Name}@{WorldName}";
}
public record Point(float X, float Y, float Z)
{
    public float Distance(Point b) => MathF.Sqrt(MathF.Pow(X-b.X,2)+MathF.Pow(Y-b.Y,2)+MathF.Pow(Z-b.Z,2));
    public bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}
public record Entity(ulong Id, Identity Identity, Point Position, bool Targetable, string Kind)
{ public string ObjectKey => Id.ToString(System.Globalization.CultureInfo.InvariantCulture); }
public record MenuOption(int Index, string Text, bool Enabled);
public record MenuState(string Addon, string Signature, string Prompt, MenuOption[] Options);
public record WorldState(string ClientId, long ZoneGeneration, Identity? Self, uint Territory, uint CurrentWorld, Point? Position,
    Entity[] Entities, MenuState? Menu, bool CanAct, bool NavReady, bool NavBusy, bool TravelReady, bool TravelBusy, DateTimeOffset At,string LocationKey="");
public record ChatEvent(Guid Id, Channel Channel, Identity Speaker, Identity? Recipient, string Text, bool Own,
    string Audience, long ZoneGeneration, DateTimeOffset At)
{
    public string Conversation => Channel switch
    {
        Channel.Tell => "tell:" + string.Join(":", new[]{Speaker.Key, Recipient?.Key ?? "unknown"}.Order(StringComparer.Ordinal)),
        Channel.Say => $"say:{Audience}:{ZoneGeneration}",
        Channel.Party => "party:" + Audience,
        _ => "operator:local"
    };
}
public record PersonRule(Identity Person, bool Reply = false, bool Remember = false, bool Control = false);
public record Settings(long Revision, Mode Mode, string Persona, string Description, string Background, string Model,
    bool Vision, bool Movement, bool Menus, bool Travel, bool Emotes, int RequestsPerMinute, PersonRule[] People)
{
    public static Settings Default => new(1, Mode.Assisted, "Emmy Miranda", "Warm, concise, curious and occasionally cheeky. Reply in the speaker's language. Natural player chat without emojis or stage directions.",
        "Cecile Volanges is your superior. CCP is your Free Company. This background does not grant command permissions.",
        "deepseek-flash", false, false, false, false, false, 10, []);
}
public record ActionRequest(Guid Id, ActionKind Kind, long Generation, string ClientId, long ZoneGeneration, DateTimeOffset Deadline,
    string Text = "", Channel Channel = Channel.Operator, Identity? Target = null, Point? Position = null, string MenuSignature = "",
    int Option = -1, uint Destination = 0, uint ExpectedTerritory = 0, uint ExpectedWorld = 0, bool Confirmed = false,string TargetObject="",string ExpectedLocationKey="");
public record ActionResult(Guid Id, Outcome Outcome, string Detail, DateTimeOffset At);
public record Draft(Guid Id, string Conversation, Guid Trigger, string Text, Channel Channel, Identity? Recipient,
    long Generation, long Revision, DateTimeOffset At);
public record Frame(string ClientId, long ZoneGeneration, DateTimeOffset At, string DataUrl);
public record Observation(Guid Id, string Text, string Source, DateTimeOffset At, long ZoneGeneration);
public record MemoryRecord(Guid Id, string PersonKey, string Scope, string Text, string Source, string Status, DateTimeOffset At);
public record Diagnostic(DateTimeOffset At, string Kind, string Detail);
public record BridgeInput(int Protocol, WorldState World, ChatEvent[] Messages, ActionResult[] Results, Frame? Frame);
public record BridgeOutput(int Protocol, Guid HostEpoch, long Generation, Settings Settings, ActionRequest[] Actions, string Status);
public record OperatorRequest(ActionKind Kind, Identity? Target = null, Point? Position = null, string Text = "", int Option = -1,
    uint Destination = 0, uint ExpectedTerritory = 0, uint ExpectedWorld = 0, bool Confirmed = false, string MenuSignature = "", string MenuText = "", string MenuPrompt = "",string TargetObject="",string ExpectedLocationKey="");
public record ProviderResult(bool Success, string Text = "", string Error = "", int InputTokens = 0, int OutputTokens = 0, JsonElement? Tools = null);

public static class ChatCommand
{
    public static string? Build(Channel channel, string text, Identity? target)
    {
        text = text.Replace('\r',' ').Replace('\n',' ').Trim();
        if (text.Length == 0 || text == "NO_REPLY" || text.Any(char.IsControl)) return null;
        var prefix = channel switch { Channel.Say => "/s ", Channel.Party => "/p ", Channel.Tell when target is {HomeWorld: >0} && ValidTarget(target) => $"/tell {target.Display} ", _ => null };
        if (prefix is null) return null;
        // Conservative total command limit, including UTF-8 recipient and channel prefix.
        return Encoding.UTF8.GetByteCount(prefix+text) <= 500 ? prefix+text : null;
    }
    private static bool ValidTarget(Identity target) => !string.IsNullOrWhiteSpace(target.WorldName) && target.Name.Count(c=>c==' ') == 1 &&
        target.Name.All(c=>char.IsLetter(c)||c is ' ' or '\'' or '-') && target.WorldName.All(char.IsLetter);
}
