namespace Emmy.Core;

/// <summary>All plans and drafts are invalidated together. A late result cannot restart a stopped action.</summary>
public sealed class Lifetime : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource source = new();
    private long generation = 1;
    public (long Generation, CancellationToken Token) Snapshot { get { lock(gate) return (generation,source.Token); } }
    public bool IsCurrent(long value) { lock(gate) return generation == value && !source.IsCancellationRequested; }
    public long Stop() { lock(gate) { source.Cancel(); source.Dispose(); source = new(); return ++generation; } }
    public void Dispose() { lock(gate) { source.Cancel(); source.Dispose(); } }
}
public sealed class RequestBudget
{
    private readonly Queue<DateTimeOffset> requests = new();
    private readonly object gate = new();
    public bool Reserve(int limit, DateTimeOffset now)
    {
        lock(gate) { while(requests.TryPeek(out var at) && at <= now.AddMinutes(-1)) requests.Dequeue();
            if(limit <= 0 || requests.Count >= limit) return false; requests.Enqueue(now); return true; }
    }
}
public static class ActionPolicy
{
    public static bool IsTransition(ActionRequest action) => action.Kind is ActionKind.Teleport or ActionKind.Aethernet or ActionKind.ChangeWorld || action.Kind==ActionKind.Menu && action.ExpectedTerritory>0;
    public static bool TransitionReached(ActionRequest action,WorldState world) => IsTransition(action)&&world.ClientId==action.ClientId&&world.CanAct&&!world.TravelBusy&&world.ZoneGeneration!=action.ZoneGeneration&&
        (action.ExpectedTerritory==0||world.Territory==action.ExpectedTerritory)&&(action.ExpectedWorld==0||world.CurrentWorld==action.ExpectedWorld)&&
        (action.ExpectedLocationKey.Length==0||action.ExpectedLocationKey==world.LocationKey);
    public static string? Validate(ActionRequest action, Settings settings, WorldState? world, long generation, DateTimeOffset now)
    {
        if(!Enum.IsDefined(action.Kind)||!Enum.IsDefined(action.Channel)||!Enum.IsDefined(settings.Mode))return "Ungültiger Aktions- oder Betriebsmodus";
        if(action.Kind == ActionKind.Stop) return null;
        if(action.Generation != generation || action.Deadline <= now) return "Auftrag veraltet";
        if(settings.Mode is Mode.Off or Mode.Observe || settings.Mode == Mode.Assisted && !action.Confirmed) return "Betriebsmodus erlaubt keine Ausführung";
        if(world is null || world.ClientId != action.ClientId || now-world.At > TimeSpan.FromSeconds(3)) return "Spielclient nicht aktuell";
        if(action.ZoneGeneration != world.ZoneGeneration) return "Gebiet hat sich geändert";
        if(!world.CanAct) return "Charakter kann gerade nicht handeln";
        if(action.Kind == ActionKind.Chat && ChatCommand.Build(action.Channel,action.Text,action.Target) is null) return "Ungültige Chatnachricht";
        if(action.Kind is ActionKind.Follow or ActionKind.Move && (!settings.Movement || !world.NavReady || world.TravelBusy)) return "Bewegung nicht verfügbar oder Reise aktiv";
        if(action.Kind is ActionKind.Follow or ActionKind.Interact && (action.Target is null || world.Entities.Count(e=>e.Identity.Key==action.Target.Key && e.Targetable && (action.TargetObject.Length==0||e.ObjectKey==action.TargetObject)) != 1)) return "Ziel fehlt oder ist mehrdeutig";
        if(action.Kind == ActionKind.Move && action.Position is not {Valid:true}) return "Position ungültig";
        if(action.Kind==ActionKind.Move&&action.ExpectedLocationKey.Length>0&&action.ExpectedLocationKey!=world.LocationKey)return "Gespeicherter Ort gehört zu einer anderen Instanz";
        if(action.Kind == ActionKind.Interact && !settings.Menus) return "Interaktionen deaktiviert";
        if(action.Kind == ActionKind.Menu && (!settings.Menus || !action.Confirmed || world.Menu is null || world.Menu.Signature != action.MenuSignature ||
            !world.Menu.Options.Any(o=>o.Index == action.Option && o.Enabled))) return "Dialog geändert oder Auswahl deaktiviert";
        if(action.Kind==ActionKind.Menu && action.ExpectedTerritory>0 && action.ExpectedTerritory==world.Territory)return "Eintritt braucht ein anderes, gespeichertes Zielgebiet";
        if(action.Kind == ActionKind.Interact && world.Menu is not null) return "Ein Dialog ist bereits geöffnet";
        if(action.Kind is ActionKind.Teleport or ActionKind.Aethernet or ActionKind.ChangeWorld && (!settings.Travel || !world.TravelReady || world.TravelBusy || action.ExpectedTerritory == 0 && action.ExpectedWorld == 0)) return "Reise nicht verfügbar oder Zielzustand fehlt";
        if(action.Kind == ActionKind.Capture && !settings.Vision) return "Vision deaktiviert";
        if(action.Kind == ActionKind.Emote && (!settings.Emotes || !new[]{"wave","bow","nod","smile","sit"}.Contains(action.Text))) return "Emote nicht erlaubt";
        return null;
    }
}
