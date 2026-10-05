using Emmy.Core;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DalamudEmmy.Companion;

public sealed class CompanionBridge : IDisposable
{
    private readonly Configuration configuration;
    private readonly Action<Action> dispatch;
    private readonly HttpClient http=new(){BaseAddress=new Uri("http://127.0.0.1:17840"),Timeout=TimeSpan.FromSeconds(3)};
    private readonly CancellationTokenSource shutdown=new();
    private readonly ConcurrentQueue<ChatEvent> messages=new();
    private readonly ConcurrentQueue<ActionResult> results=new();
    private readonly MenuAdapter menu;
    private readonly GameExecutor executor;
    private Frame? frame;
    private readonly string clientId=Guid.NewGuid().ToString("N");
    private long zone=1,generation=1,localRevision;
    private uint territory,currentWorld;
    private string character="";
    private string location="";
    private Guid epoch;
    private int exchanging;
    private DateTimeOffset nextExchange,lastSuccess;
    private string token="";
    private bool suspended;
    public Settings Settings {get;private set;}=Settings.Default;
    public string Status {get;private set;}="Host noch nicht verbunden";
    public bool IsOpen {get;set;}=true;
    public CompanionBridge(Configuration configuration,Action<Action> dispatch)
    {
        this.configuration=configuration;this.dispatch=dispatch;
        menu=new(Plugin.GameGui,Plugin.AddonLifecycle);
        executor=new(menu,Snapshot,r=>results.Enqueue(r),f=>frame=f);
        Plugin.ChatGui.ChatMessageUnhandled+=Chat;
    }
    private string TokenPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Emmy","bridge.token");
    public void OpenWeb()
    {
        if(!File.Exists(TokenPath)){Status="Emmy.Host zuerst starten";return;}
        var value=File.ReadAllText(TokenPath).Trim();
        Process.Start(new ProcessStartInfo("http://127.0.0.1:17840/#"+value){UseShellExecute=true});
    }
    public void Stop()
    {
        localRevision++;suspended=true;executor.Stop("Manuelle Übernahme");Status="Pausiert";
        _=Post("stop",new {});
    }
    public void SetMode(Mode mode)
    {
        localRevision++;executor.Stop("Betriebsmodus geändert");suspended=true;
        _=Post("mode",new {mode});
    }
    private async Task Post(string endpoint,object body)
    {
        try {ReadToken();using var request=new HttpRequestMessage(HttpMethod.Post,"api/"+endpoint){Content=JsonContent.Create(body,options:Wire.Json)};
            request.Headers.Add("X-Emmy-Token",token);using var response=await http.SendAsync(request,shutdown.Token);response.EnsureSuccessStatusCode();}
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or IOException){dispatch(()=>Status="Host nicht erreichbar; lokal gestoppt");}
    }
    private void ReadToken(){if(File.Exists(TokenPath))token=File.ReadAllText(TokenPath).Trim();}
    public void Tick()
    {
        if(shutdown.IsCancellationRequested)return;
        if(!configuration.UseCompanionHost || !configuration.IsEnabled || !configuration.CaptureChatMessages){executor.Stop("Legacy-Modus");return;}
        if(executor.HasControl && ManualInput()) { Stop(); return; }
        var self=Plugin.ObjectTable.LocalPlayer;
        var newTerritory=Plugin.ClientState.TerritoryType;
        var newWorld=self?.CurrentWorld.RowId??0;
        var newCharacter=self is null?"":self.Name+"@"+self.HomeWorld.RowId;
        var characterChanged=newCharacter.Length>0&&character.Length>0&&character!=newCharacter;
        var newLocation=LocationKey();
        var locationChanged=newLocation.Length>0&&location.Length>0&&newLocation!=location;
        if(newCharacter.Length>0)character=newCharacter;
        if(newLocation.Length>0||!executor.HasTravel)location=newLocation;
        if(territory!=newTerritory||currentWorld!=newWorld||characterChanged||locationChanged)
        {territory=newTerritory;currentWorld=newWorld;zone++;frame=null;if(characterChanged||!executor.HasTravel)executor.Stop("Gebiet oder Charakter geändert");}
        if(DateTimeOffset.UtcNow-lastSuccess>TimeSpan.FromSeconds(4)&&lastSuccess!=default){executor.Stop("Hostverbindung verloren");suspended=true;lastSuccess=default;Status="Hostverbindung verloren; gestoppt";}
        try{executor.Tick(Snapshot(),generation);}catch(Exception ex){Plugin.Log.Warning(ex,"Companion tick failed");executor.Stop("Spielzustand nicht lesbar");}
        if(DateTimeOffset.UtcNow<nextExchange||Interlocked.CompareExchange(ref exchanging,1,0)!=0)return;
        nextExchange=DateTimeOffset.UtcNow.AddMilliseconds(500);
        var input=new BridgeInput(Wire.Version,Snapshot(),messages.Take(32).ToArray(),results.Take(32).ToArray(),frame);
        var ticket=localRevision;
        _=Exchange(input,ticket);
    }
    private static unsafe bool ManualInput()
    {
        var atk=RaptureAtkModule.Instance();
        if(atk==null||atk->IsTextInputActive()||ImGui.GetIO().WantTextInput)return false;
        return new[]{VirtualKey.W,VirtualKey.A,VirtualKey.S,VirtualKey.D,VirtualKey.UP,VirtualKey.DOWN,VirtualKey.LEFT,VirtualKey.RIGHT}.Any(k=>Plugin.KeyState[k]);
    }
    private async Task Exchange(BridgeInput input,long ticket)
    {
        try
        {
            ReadToken();using var request=new HttpRequestMessage(HttpMethod.Post,"api/bridge"){Content=JsonContent.Create(input,options:Wire.Json)};
            request.Headers.Add("X-Emmy-Token",token);using var response=await http.SendAsync(request,shutdown.Token);response.EnsureSuccessStatusCode();
            var output=await response.Content.ReadFromJsonAsync<BridgeOutput>(Wire.Json,shutdown.Token);
            if(output is null||output.Protocol!=Wire.Version)throw new InvalidOperationException("Hostprotokoll ungültig");
            dispatch(()=>
            {
                if(shutdown.IsCancellationRequested)return;
                foreach(var item in input.Messages) if(messages.TryPeek(out var queuedMessage)&&queuedMessage.Id==item.Id)messages.TryDequeue(out _);
                foreach(var item in input.Results) if(results.TryPeek(out var queuedResult)&&queuedResult.Id==item.Id&&queuedResult.At==item.At)results.TryDequeue(out _);
                if(frame?.At==input.Frame?.At)frame=null;
                lastSuccess=DateTimeOffset.UtcNow;
                if(ticket!=localRevision)return;
                if(epoch!=output.HostEpoch||generation!=output.Generation){executor.Stop("Aufträge entwertet");epoch=output.HostEpoch;generation=output.Generation;suspended=false;}
                Settings=output.Settings;Status=output.Status;
                if(Settings.Mode is Mode.Off or Mode.Observe)executor.Stop("Beobachtung ohne Spielaktionen");
                if(!suspended)foreach(var action in output.Actions)executor.Execute(action,Settings,generation);
            });
        }
        catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidOperationException)
        {dispatch(()=>Status="Host nicht erreichbar oder inkompatibel");}
        finally{dispatch(()=>Interlocked.Exchange(ref exchanging,0));}
    }
    public WorldState Snapshot()
    {
        var self=Plugin.ObjectTable.LocalPlayer;
        var entities=Plugin.ObjectTable.Where(o=>o.IsValid()&&o.Name.ToString().Length>0&&self is not null&&VectorDistance(o.Position,self.Position)<60).Take(128)
            .Select(o=>new Entity(o.GameObjectId,IdentityOf(o),GameExecutor.ToPoint(o.Position),o.IsTargetable,o.ObjectKind.ToString())).ToArray();
        var canAct=self is not null&&!Plugin.Condition[ConditionFlag.BetweenAreas]&&!Plugin.Condition[ConditionFlag.BetweenAreas51]&&!Plugin.Condition[ConditionFlag.InCombat]&&!Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent]&&!Plugin.Condition[ConditionFlag.Unconscious];
        return new(clientId,zone,self is null?null:IdentityOf(self),Plugin.ClientState.TerritoryType,self?.CurrentWorld.RowId??0,self is null?null:GameExecutor.ToPoint(self.Position),
            entities,Settings.Menus?menu.Read():null,canAct,GameExecutor.NavReady,GameExecutor.NavBusy,GameExecutor.TravelReady,GameExecutor.TravelBusy,DateTimeOffset.UtcNow,LocationKey());
    }
    private static unsafe string LocationKey()
    {
        var housing=FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
        if(housing==null||housing->CurrentTerritory==null)return "";
        if(housing->IsInside())
        {
            var id=housing->GetCurrentIndoorHouseId().Id;
            return id is 0 or ulong.MaxValue?"":$"housing-indoor:{id}:{housing->GetCurrentRoom()}";
        }
        return housing->IsOutside()?$"housing-outdoor:{housing->GetCurrentWard()}:{housing->GetCurrentDivision()}":"";
    }
    private static float VectorDistance(System.Numerics.Vector3 a,System.Numerics.Vector3 b)=>System.Numerics.Vector3.Distance(a,b);
    private static Identity IdentityOf(IGameObject o)=>o is IPlayerCharacter p?new(p.Name.ToString(),p.HomeWorld.RowId,p.HomeWorld.Value.Name.ToString()):new(o.Name.ToString(),0);
    private void Chat(IChatMessage message)
    {
        if(!configuration.UseCompanionHost||!configuration.IsEnabled||!configuration.CaptureChatMessages||Settings.Mode==Mode.Off)return;
        var channel=message.LogKind switch{XivChatType.Say=>Emmy.Core.Channel.Say,XivChatType.Party=>Emmy.Core.Channel.Party,XivChatType.TellIncoming or XivChatType.TellOutgoing=>Emmy.Core.Channel.Tell,_=>(Emmy.Core.Channel?)null};
        if(channel is null)return;
        var self=Plugin.ObjectTable.LocalPlayer;if(self is null)return;
        var payload=message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        Identity? other=payload is not null?new(payload.PlayerName,payload.World.RowId,payload.World.Value.Name.ToString()):null;
        if(other is null)
        {
            var candidates=Plugin.ObjectTable.PlayerObjects.Where(p=>p.Name.ToString()==message.Sender.ToString()).ToArray();
            if(candidates.Length==1)other=IdentityOf(candidates[0]);else return;
        }
        var own=message.LogKind==XivChatType.TellOutgoing||other.Key==IdentityOf(self).Key;
        var audience=channel==Emmy.Core.Channel.Party?string.Join(",",Plugin.PartyList.Select(p=>$"{p.Name}@{p.World.RowId}").Order()):territory.ToString();
        var e=new ChatEvent(Guid.NewGuid(),channel.Value,own?IdentityOf(self):other,channel==Emmy.Core.Channel.Tell?(own?other:IdentityOf(self)):null,message.Message.ToString(),own,audience,zone,DateTimeOffset.UtcNow);
        executor.Echo(e);
        if(messages.Count<128)messages.Enqueue(e);else{executor.Stop("Chatwarteschlange voll");Status="Host zu langsam; Aufnahme begrenzt";}
    }
    public void Draw()
    {
        if(!IsOpen||!configuration.UseCompanionHost)return;
        var open=IsOpen;
        if(ImGui.Begin("Emmy Companion",ref open,ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(Settings.Persona);ImGui.TextWrapped(Status);ImGui.TextUnformatted(executor.Status);
            if(ImGui.Button("Stop & übernehmen"))Stop();ImGui.SameLine();if(ImGui.Button("Leitstand öffnen"))OpenWeb();
            var mode=(int)Settings.Mode;if(ImGui.Combo("Modus",ref mode,"Aus\0Beobachten\0Assistiert\0Begleiten\0"))SetMode((Mode)mode);
            ImGui.TextDisabled("Details, Kontakte und Entwürfe im lokalen Leitstand");
        }
        ImGui.End();IsOpen=open;
    }
    public void Dispose(){shutdown.Cancel();executor.Dispose();menu.Dispose();Plugin.ChatGui.ChatMessageUnhandled-=Chat;http.Dispose();}
}
