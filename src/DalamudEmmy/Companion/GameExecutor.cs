using System.Numerics;
using Emmy.Core;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DalamudEmmy.Companion;

/// <summary>Only the framework thread accesses IPC and native game structures.</summary>
public sealed class GameExecutor : IDisposable
{
    private readonly MenuAdapter menu;
    private readonly Action<ActionResult> result;
    private readonly Action<Frame> captured;
    private readonly Func<WorldState> observe;
    private CancellationTokenSource? pathCancel;
    private Task<List<Vector3>>? path;
    private ActionRequest? moving;
    private readonly List<ActionRequest> pending = new();
    private readonly HashSet<Guid> seen = new();
    private readonly Queue<Guid> seenOrder = new();
    private Point? lastDestination;
    private DateTimeOffset lastPath, started;
    private bool ownsNav,ownsTravel;
    private Point? progressPosition;
    private DateTimeOffset progressAt;
    private readonly HashSet<Guid> chatEchoes = new();
    public string Status { get; private set; } = "Bereit";
    public GameExecutor(MenuAdapter menu,Func<WorldState> observe,Action<ActionResult> result,Action<Frame> captured)
    {this.menu=menu;this.observe=observe;this.result=result;this.captured=captured;}
    public static bool NavReady => Probe("vnavmesh.Nav.IsReady");
    public static bool NavBusy => Probe("vnavmesh.Path.IsRunning") || Probe("vnavmesh.SimpleMove.PathfindInProgress");
    public static bool TravelReady {get {try{Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy").InvokeFunc();return true;}catch{return false;}}}
    public static bool TravelBusy => Probe("Lifestream.IsBusy");
    private static bool Probe(string name){try{return Plugin.PluginInterface.GetIpcSubscriber<bool>(name).InvokeFunc();}catch{return false;}}
    private void Report(ActionRequest a,Outcome outcome,string detail){Status=detail;result(new(a.Id,outcome,detail,DateTimeOffset.UtcNow));}
    public void Stop(string reason)
    {
        pathCancel?.Cancel();pathCancel?.Dispose();pathCancel=null;path=null;lastDestination=null;
        if(ownsNav){try{Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();}catch(Exception ex){Plugin.Log.Warning(ex,"Could not confirm navigation stop");}ownsNav=false;}
        if(ownsTravel){try{Plugin.PluginInterface.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction();}catch(Exception ex){Plugin.Log.Warning(ex,"Could not confirm travel stop");}ownsTravel=false;}
        if(moving is not null)Report(moving,Outcome.Cancelled,reason);moving=null;
        foreach(var a in pending)Report(a,Outcome.Unknown,reason);pending.Clear();chatEchoes.Clear();Status=reason;
    }
    public void Execute(ActionRequest a,Settings settings,long generation)
    {
        if(!seen.Add(a.Id))return;
        seenOrder.Enqueue(a.Id);while(seenOrder.Count>512)seen.Remove(seenOrder.Dequeue());
        var world=observe();var error=ActionPolicy.Validate(a,settings,world,generation,DateTimeOffset.UtcNow);
        if(error is not null){Report(a,Outcome.Failed,error);return;}
        try
        {
            switch(a.Kind)
            {
                case ActionKind.Stop:Stop("Gestoppt");Report(a,Outcome.Succeeded,"Gestoppt");break;
                case ActionKind.Chat:
                    var command=ChatCommand.Build(a.Channel,a.Text,a.Target);
                    if(command is null||!Command(command)){Report(a,Outcome.Failed,"Chat konnte nicht übergeben werden");break;}
                    pending.Add(a);Report(a,Outcome.Accepted,"Chat übergeben; wartet auf ausgehendes Echo");break;
                case ActionKind.Follow:
                case ActionKind.Move:
                    if(moving is not null||ownsNav||ownsTravel||NavBusy||TravelBusy){Report(a,Outcome.Failed,"Ein anderer Controller bewegt den Charakter");break;}
                    moving=a;started=DateTimeOffset.UtcNow;progressAt=started;progressPosition=world.Position;lastDestination=null;Report(a,Outcome.Accepted,"Bewegung angenommen");break;
                case ActionKind.Interact:
                    if(world.Menu is not null){Report(a,Outcome.Failed,"Ein Dialog ist schon geöffnet");break;}
                    var entity=Resolve(a.Target);
                    if(entity is null||!entity.IsTargetable||world.Position is null||world.Position.Distance(ToPoint(entity.Position))>4){Report(a,Outcome.Failed,"Ziel nicht in Interaktionsreichweite");break;}
                    unsafe{var target=TargetSystem.Instance();if(target==null){Report(a,Outcome.Failed,"Zielsystem nicht bereit");break;}target->InteractWithObject((GameObject*)entity.Address);}
                    pending.Add(a);Report(a,Outcome.Accepted,"Interaktion angefordert; wartet auf Dialog");break;
                case ActionKind.Menu:
                    if(!menu.Select(a)){Report(a,Outcome.Failed,"Menüauswahl ist nicht mehr gültig");break;}
                    pending.Add(a);Report(a,Outcome.Accepted,"Auswahl übergeben; wartet auf neuen Dialogzustand");break;
                case ActionKind.Capture:
                    captured(WindowCapture.Capture(a.ClientId,a.ZoneGeneration));Report(a,Outcome.Succeeded,"Aktuelles Spielfenster aufgenommen");break;
                case ActionKind.Emote:
                    if(Command("/"+a.Text+" motion"))Report(a,Outcome.Unknown,"Emote übergeben; Animation nicht bestätigt");else Report(a,Outcome.Failed,"Emote nicht übergeben");break;
                case ActionKind.Teleport:
                case ActionKind.Aethernet:
                case ActionKind.ChangeWorld:
                    if(moving is not null||ownsNav||ownsTravel||NavBusy||TravelBusy){Report(a,Outcome.Failed,"Bewegungsressource beschäftigt");break;}
                    var accepted=a.Kind switch
                    {
                        ActionKind.Teleport=>Plugin.PluginInterface.GetIpcSubscriber<uint,byte,bool>("Lifestream.Teleport").InvokeFunc(a.Destination,0),
                        ActionKind.Aethernet=>Plugin.PluginInterface.GetIpcSubscriber<uint,bool>("Lifestream.AethernetTeleportById").InvokeFunc(a.Destination),
                        _=>Plugin.PluginInterface.GetIpcSubscriber<uint,bool>("Lifestream.ChangeWorldById").InvokeFunc(a.Destination)
                    };
                    if(accepted){ownsTravel=true;pending.Add(a);Report(a,Outcome.Accepted,"Reise angenommen; Ankunft noch nicht bestätigt");}else Report(a,Outcome.Failed,"Lifestream hat die Reise abgelehnt");break;
            }
        }
        catch(Exception ex){Plugin.Log.Warning(ex,"Companion action {Kind} failed",a.Kind);Report(a,Outcome.Failed,"Spieladapter nicht verfügbar");}
    }
    public void Echo(ChatEvent e)
    {
        if(!e.Own)return;
        foreach(var a in pending.Where(a=>a.Kind==ActionKind.Chat&&a.Channel==e.Channel&&a.Text==e.Text&&
            (a.Channel!=Emmy.Core.Channel.Tell||a.Target?.Key==e.Recipient?.Key)).ToArray())chatEchoes.Add(a.Id);
    }
    public bool HasTravel => ownsTravel;
    public bool HasControl => ownsNav || ownsTravel || moving is not null;
    public void Tick(WorldState world,long generation)
    {
        if(moving is not null)
        {
            var a=moving;
            if(a.Generation!=generation||a.ZoneGeneration!=world.ZoneGeneration||!world.CanAct||TravelBusy){Stop("Bewegung unterbrochen");return;}
            if(a.Kind==ActionKind.Move&&DateTimeOffset.UtcNow>a.Deadline){Stop("Bewegungszeit überschritten");return;}
            var destination=a.Kind==ActionKind.Move?a.Position:world.Entities.SingleOrDefault(e=>e.Identity.Key==a.Target?.Key)?.Position;
            if(destination is null||world.Position is null){Stop("Ziel verloren; wartet auf neuen Auftrag");return;}
            var distance=world.Position.Distance(destination);
            if(progressPosition is null || progressPosition.Distance(world.Position)>0.5f) {progressPosition=world.Position;progressAt=DateTimeOffset.UtcNow;}
            if(ownsNav && distance>3 && DateTimeOffset.UtcNow-progressAt>TimeSpan.FromSeconds(12)) {Stop("Weg blockiert; bitte neuen Auftrag erteilen");return;}
            if(distance<3)
            {
                pathCancel?.Cancel();path=null;
                if(ownsNav){Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();ownsNav=false;}
                if(a.Kind==ActionKind.Move){Report(a,Outcome.Succeeded,"Zielposition erreicht");moving=null;}else Status="Wartet in Gesprächsabstand";
            }
            else if(world.NavReady)
            {
                if(path is not null&&path.IsCompleted)
                {
                    var completed=path;path=null;
                    if(completed.IsCompletedSuccessfully&&completed.Result.Count>0&&moving?.Id==a.Id&&a.Generation==generation)
                    {Plugin.PluginInterface.GetIpcSubscriber<List<Vector3>,bool,object>("vnavmesh.Path.MoveTo").InvokeAction(completed.Result,false);ownsNav=true;Status="Unterwegs";}
                    else {Stop("Kein nutzbarer Weg");return;}
                }
                if(path is null&&DateTimeOffset.UtcNow-lastPath>TimeSpan.FromSeconds(2)&&(!world.NavBusy||lastDestination is null||lastDestination.Distance(destination)>4))
                {
                    if(!ownsNav&&NavBusy){Stop("Navigation durch anderen Controller belegt");return;}
                    pathCancel?.Cancel();pathCancel?.Dispose();pathCancel=new();
                    path=Plugin.PluginInterface.GetIpcSubscriber<Vector3,Vector3,bool,CancellationToken,Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable")
                        .InvokeFunc(ToVector(world.Position),ToVector(destination),false,pathCancel.Token);
                    lastDestination=destination;lastPath=DateTimeOffset.UtcNow;Status="Berechnet Weg";
                }
                if(path is not null&&DateTimeOffset.UtcNow-lastPath>TimeSpan.FromSeconds(15)){Stop("Wegberechnung zu langsam");return;}
            }
            else {Stop("vnavmesh nicht bereit");return;}
        }
        foreach(var a in pending.ToArray())
        {
            if(a.Generation!=generation){pending.Remove(a);Report(a,Outcome.Unknown,"Auftrag entwertet");continue;}
            if(a.Kind==ActionKind.Chat&&chatEchoes.Remove(a.Id)){pending.Remove(a);Report(a,Outcome.Succeeded,"Ausgehende Chatnachricht beobachtet");continue;}
            if(a.Kind==ActionKind.Interact&&world.Menu is not null){pending.Remove(a);Report(a,Outcome.Succeeded,"Dialog nach Interaktion beobachtet");continue;}
            if(a.Kind==ActionKind.Menu&&world.Menu?.Signature!=a.MenuSignature){pending.Remove(a);Report(a,Outcome.Succeeded,"Dialogzustand nach Auswahl geändert");continue;}
            if(a.Kind is ActionKind.Teleport or ActionKind.Aethernet or ActionKind.ChangeWorld && world.CanAct&&!world.TravelBusy&&world.ZoneGeneration!=a.ZoneGeneration&&
                (a.ExpectedTerritory==0||world.Territory==a.ExpectedTerritory)&&(a.ExpectedWorld==0||world.CurrentWorld==a.ExpectedWorld))
            {pending.Remove(a);ownsTravel=false;Report(a,Outcome.Succeeded,"Reiseziel im aktuellen Spielzustand bestätigt");continue;}
            if(DateTimeOffset.UtcNow>a.Deadline){pending.Remove(a);if(a.Kind is ActionKind.Teleport or ActionKind.Aethernet or ActionKind.ChangeWorld)Stop("Reisezeit überschritten");Report(a,Outcome.Unknown,"Ergebnis nicht bestätigt; keine automatische Wiederholung");}
        }
    }
    private static IGameObject? Resolve(Identity? target)
    {
        if(target is null)return null;
        var found=Plugin.ObjectTable.Where(o=>o.IsValid()&&o.Name.ToString().Equals(target.Name,StringComparison.OrdinalIgnoreCase)&&
            (o is IPlayerCharacter p?p.HomeWorld.RowId==target.HomeWorld:target.HomeWorld==0)).ToArray();return found.Length==1?found[0]:null;
    }
    public static Point ToPoint(Vector3 p)=>new(p.X,p.Y,p.Z);
    public static Vector3 ToVector(Point p)=>new(p.X,p.Y,p.Z);
    public static unsafe bool Command(string command)
    {
        var ui=UIModule.Instance();if(ui==null||ui->GetRaptureShellModule()==null)return false;
        var text=Utf8String.FromString(command);try{ui->GetRaptureShellModule()->ExecuteCommandInner(text,ui);return true;}finally{text->Dtor(true);}
    }
    public void Dispose()=>Stop("Plugin beendet");
}
