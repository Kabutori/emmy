using Emmy.Core;
using Emmy.Host;
using System.Net;
using System.Text;
using System.Text.Json;
using Channel = Emmy.Core.Channel;

var passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
var now=DateTimeOffset.UtcNow;
var a=new Identity("Alice Example",21,"Ragnarok");var b=new Identity("Bob Example",22,"Shiva");var self=new Identity("Emmy Miranda",21,"Ragnarok");
ChatEvent Tell(Identity who,string text,bool own=false)=>new(Guid.NewGuid(),Channel.Tell,own?self:who,own?who:self,text,own,"",1,now);
Check(a.Key!=new Identity(a.Name,22,"Shiva").Key,"Same name on different worlds stays separate");
Check(Tell(a,"hi").Conversation==Tell(a,"hello",true).Conversation,"Incoming and outgoing tell share the same thread");
Check(Tell(a,"hi").Conversation!=Tell(b,"hi").Conversation,"Parallel tells remain isolated");
Check(ChatCommand.Build(Channel.Tell,"hello",a)=="/tell Alice Example@Ragnarok hello","Tell has explicit recipient and world");
Check(ChatCommand.Build(Channel.Tell,"hello",a with {HomeWorld=0}) is null,"Unknown homeworld cannot receive a tell");
Check(ChatCommand.Build(Channel.Say,"NO_REPLY",null)is null,"Silence marker is never sent");
Check(ChatCommand.Build(Channel.Say,new string('界',200),null)is null,"UTF-8 limit is enforced including prefix");
Check(ChatCommand.Build(Channel.Operator,"private",null)is null,"Private operator voice/text never becomes game chat");
Check(ChatCommand.Build(Channel.Tell,"hello",a with {WorldName="World /p"})is null,"Recipient cannot inject a game command");
using(var lifetime=new Lifetime()){var ticket=lifetime.Snapshot;lifetime.Stop();Check(ticket.Token.IsCancellationRequested,"Stop cancels an outstanding request");Check(!lifetime.IsCurrent(ticket.Generation),"Late completed work cannot restart a stopped plan");}
var budget=new RequestBudget();var reservations=0;Parallel.For(0,100,_=>{if(budget.Reserve(10,now))Interlocked.Increment(ref reservations);});Check(reservations==10,"Concurrent requests reserve the shared budget atomically");Check(budget.Reserve(10,now.AddMinutes(2)),"Rate budget recovers after expiry");
var world=new WorldState("client",1,self,100,21,new(0,0,0),[new(1,a,new(5,0,0),true,"Player")],new("SelectString","fresh","pick",[new(0,"Open",true),new(1,"Disabled",false)]),true,true,false,true,false,now);
var settings=Settings.Default with {Mode=Mode.Companion,Movement=true,Menus=true,Travel=true,Vision=true,Emotes=true};
ActionRequest Action(ActionKind kind)=>new(Guid.NewGuid(),kind,1,"client",1,now.AddSeconds(30),Target:a,Confirmed:true);
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world,1,now)is null,"Allowed follow with unique observed target");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world,2,now)is not null,"Old generation rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Follow) with {Deadline=now},settings,world,1,now)is not null,"Expired action rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world with {ZoneGeneration=2},1,now)is not null,"Old zone rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world with {At=now.AddSeconds(-10)},1,now)is not null,"Stale snapshot rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world with {Entities=[world.Entities[0],world.Entities[0]]},1,now)is not null,"Ambiguous target rejected");
var twinDoor=new Identity("Entrance",0);var twinWorld=world with {Menu=null,Entities=[new(2,twinDoor,new(1,0,0),true,"EventObj"),new(3,twinDoor,new(2,0,0),true,"EventObj")]};
Check(ActionPolicy.Validate(Action(ActionKind.Interact) with {Target=twinDoor,TargetObject="2"},settings,twinWorld,1,now)is null,"Object key distinguishes same-named doors within the current zone");
Check(ActionPolicy.Validate(Action(ActionKind.Interact) with {Target=twinDoor,TargetObject="4"},settings,twinWorld,1,now)is not null,"Object key must be in the observed snapshot");
Check(JsonSerializer.SerializeToElement(new Entity(ulong.MaxValue,twinDoor,new(0,0,0),true,"EventObj"),Wire.Json).GetProperty("objectKey").GetString()==ulong.MaxValue.ToString(),"64-bit object keys remain lossless for browser selection");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings,world with {TravelBusy=true},1,now)is not null,"Follow cannot compete with travel");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings with {Mode=Mode.Observe},world,1,now)is not null,"Observation mode cannot execute actions");
Check(ActionPolicy.Validate(Action(ActionKind.Follow) with {Confirmed=false},settings with {Mode=Mode.Assisted},world,1,now)is not null,"Assisted action needs its own confirmation");
Check(ActionPolicy.Validate(Action(ActionKind.Menu) with {Option=0,MenuSignature="old"},settings,world,1,now)is not null,"Changed menu rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Menu) with {Option=1,MenuSignature="fresh"},settings,world,1,now)is not null,"Disabled menu option rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Menu) with {Option=0,MenuSignature="fresh",Confirmed=false},settings,world,1,now)is not null,"Generic confirmation dialog cannot be selected autonomously");
Check(ActionPolicy.Validate(Action(ActionKind.Menu) with {Option=0,MenuSignature="fresh"},settings,world,1,now)is null,"Fresh explicitly confirmed menu option allowed");
Check(ActionPolicy.Validate(Action(ActionKind.Teleport),settings,world,1,now)is not null,"Travel needs a verifiable destination state");
Check(ActionPolicy.Validate(Action(ActionKind.Chat) with {Kind=(ActionKind)999},settings,world,1,now)is not null,"Unknown numeric action rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Follow),settings with {Mode=(Mode)999},world,1,now)is not null,"Unknown numeric mode rejected");
Check(ActionPolicy.Validate(Action(ActionKind.Emote) with {Text="logout"},settings,world,1,now)is not null,"Emotes cannot execute arbitrary commands");

var handler=new FakeHandler();var provider=new DeepSeekClient(new HttpClient(handler));
handler.Response="{\"choices\":[{\"message\":{\"content\":\"hello\"}}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3}}";
var response=await provider.CompleteAsync("test-key","deepseek-flash","persona",[new{role="user",content="hello"}],default);
Check(response.Success&&response.Text=="hello"&&response.InputTokens==12,"Typed successful provider response and usage");
Check((await provider.CompleteAsync("","deepseek-flash","",[],default)).Success==false,"Missing key is a typed failure");
handler.Status=HttpStatusCode.TooManyRequests;response=await provider.CompleteAsync("test-key","model","",[],default);Check(!response.Success&&response.Text.Length==0,"HTTP error cannot become chat text");
handler.Status=HttpStatusCode.OK;handler.Response="invalid";Check(!(await provider.CompleteAsync("test-key","model","",[],default)).Success,"Malformed provider JSON is rejected");
handler.Response="{\"choices\":[]}";Check(!(await provider.CompleteAsync("test-key","model","",[],default)).Success,"Empty model result is a failure");
using(var cancel=new CancellationTokenSource()){cancel.Cancel();Check(!(await provider.CompleteAsync("test-key","model","",[],cancel.Token)).Success,"Provider honors cancellation");}
var frame=new Frame("client",1,now,"data:image/png;base64,aGVsbG8=");
var vision=JsonSerializer.Serialize(DeepSeekClient.VisionMessage("What is visible?",frame));Check(vision.Contains("image_url")&&vision.Contains("data:image/png"),"Vision uses multimodal content blocks");

var directory=Path.Combine(Path.GetTempPath(),"emmy-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);
try
{
    var store=new Store(directory);store.Save(settings);Check(store.Load().Mode==Mode.Companion,"Settings survive reopening the SQLite store");
    var messageA=Tell(a,"my private preference");var messageB=Tell(b,"my other preference");
    Check(store.Message(messageA)&&!store.Message(messageA),"Repeated chat delivery is deduplicated");store.Message(messageB);store.Message(Tell(a,"hello",true));
    Check(store.History(messageA.Conversation).Length==2&&store.History(messageA.Conversation).All(e=>e.Speaker.Key!=b.Key),"Private history includes own replies and excludes other tells");
    store.Memory(new(Guid.NewGuid(),a.Key,messageA.Conversation,"secret A","message:A","confirmed",now));store.Memory(new(Guid.NewGuid(),b.Key,messageB.Conversation,"secret B","message:B","confirmed",now));
    Check(store.Memories(messageA.Conversation).All(m=>m.Text!="secret B"),"Memory audience filtering happens before prompt construction");
    store.Forget(a.Key,true,true);Check(store.History(messageA.Conversation).Length==0&&store.Memories(messageA.Conversation).Length==0,"Forget removes incoming and outgoing history plus private memories");
    Check(store.History(messageB.Conversation).Length==1,"Forgetting A preserves B's private conversation");
    var engine=new Companion(store,new Secrets(directory),provider);
    var output=engine.Exchange(new(Wire.Version,world,[],[],null));Check(output.Protocol==Wire.Version,"Bridge protocol roundtrip");
    var actionError=engine.Submit(new(ActionKind.Follow,a,Confirmed:true));Check(actionError is null,"Operator can schedule valid action");
    var batch=engine.Exchange(new(Wire.Version,world,[],[],null));Check(batch.Actions.Length==1,"Action dispatched once");
    Check(engine.Exchange(new(Wire.Version,world,[],[],null)).Actions.Length==0,"Polling does not resend dispatched actions");
    engine.Stop();Check(engine.Exchange(new(Wire.Version,world,[],[],null)).Generation>batch.Generation,"Stop changes bridge generation");
    Check(!engine.Configure(settings with {Revision=0}),"Conflicting configuration revision rejected");
    Check(!engine.Confirm(Guid.NewGuid(),"reply"),"Unknown or consumed draft cannot send");
    try{engine.Exchange(new(Wire.Version+1,world,[],[],null));Check(false,"Unknown protocol rejected");}catch(InvalidOperationException){Check(true,"Unknown protocol rejected");}
    Check(engine.SavePlace("Our bench") is null,"Named meeting point binds observed world, territory and position");
    Check(store.Places().Length==1,"Meeting point persists");
    Check(engine.VisitPlace(store.Places()[0].Id) is null,"Saved meeting point produces a checked move");
    engine.Stop();
    Check(engine.StartPlan("Two steps",[new(ActionKind.Move,Position:new(5,0,0),Confirmed:true),new(ActionKind.Emote,Text:"wave",Confirmed:true)]) is null,"Finite itinerary accepted");
    var planOutput=engine.Exchange(new(Wire.Version,world,[],[],null));
    Check(planOutput.Actions.Length==1&&planOutput.Actions[0].Kind==ActionKind.Move,"Itinerary starts exactly the first step");
    var completion=new ActionResult(planOutput.Actions[0].Id,Outcome.Succeeded,"arrived",now);
    var nextOutput=engine.Exchange(new(Wire.Version,world,[],[completion],null));
    Check(nextOutput.Actions.Length==1&&nextOutput.Actions[0].Kind==ActionKind.Emote,"Next itinerary step requires confirmed prior success");
    engine.Stop();Check(engine.StartPlan("Invalid",[new(ActionKind.Follow,a,Confirmed:true)]) is not null,"Unbounded follow cannot block a finite itinerary");
    engine.Dispose();
    var oldKey=Environment.GetEnvironmentVariable("EMMY_DEEPSEEK_KEY");
    Environment.SetEnvironmentVariable("EMMY_DEEPSEEK_KEY","unit-test-placeholder");
    handler.Response=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=JsonSerializer.Serialize(new{reply="A short reply",action=(object?)null,memory=(string?)null})}}}});
    var socialSecrets=new Secrets(directory);
    if(OperatingSystem.IsWindows())socialSecrets.Write("unit-test-placeholder");
    var social=new Companion(store,socialSecrets,provider);
    var initial=social.Settings;
    Check(social.Configure(initial with {Mode=Mode.Assisted,People=[new(a,true,true,false),new(b,true,true,false)]}),"Social test config is accepted");
    store.Memory(new(Guid.NewGuid(),a.Key,Tell(a,"").Conversation,"private-A-only","source:A","confirmed",now));
    store.Memory(new(Guid.NewGuid(),b.Key,Tell(b,"").Conversation,"private-B-only","source:B","confirmed",now));
    await social.StartAsync(default);
    var first=Tell(a,"How are you?");var second=Tell(b,"What are we doing?");
    social.Exchange(new(Wire.Version,world, [first,second],[],null));
    JsonElement socialState=default;
    for(var attempt=0;attempt<100;attempt++){await Task.Delay(20);socialState=JsonSerializer.SerializeToElement(social.State(),Wire.Json);if(socialState.GetProperty("drafts").GetArrayLength()==2)break;}
    Check(socialState.GetProperty("drafts").GetArrayLength()==2,"Two simultaneous private tells produce independent drafts");
    var lastRequests=handler.Requests.TakeLast(2).ToArray();
    Check(lastRequests[0].Contains("private-A-only")&&!lastRequests[0].Contains("private-B-only"),"A's model request excludes B's private memory");
    Check(lastRequests[1].Contains("private-B-only")&&!lastRequests[1].Contains("private-A-only"),"B's model request excludes A's private memory");
    var id=socialState.GetProperty("drafts")[0].GetProperty("id").GetGuid();
    Check(social.Confirm(id,"Edited reply"),"Assisted draft is editable and confirmable");
    Check(!social.Confirm(id,"Edited reply"),"Double confirmation cannot send twice");
    var sends=social.Exchange(new(Wire.Version,world,[],[],null));
    Check(sends.Actions.Length==1&&sends.Actions[0].Target?.Key==a.Key&&sends.Actions[0].Text=="Edited reply","Draft dispatch preserves recipient, world and edited text");
    social.Stop();
    Check(!social.Confirm(socialState.GetProperty("drafts")[1].GetProperty("id").GetGuid(),null),"Stop invalidates all other drafts");
    await social.StopAsync(default);social.Dispose();
    handler.Response=JsonSerializer.Serialize(new{choices=new[]{new{message=new{content="Ein Tisch steht im sichtbaren Raum."}}}});
    store.Save(settings with {People=[new(a,true,true,false)]});
    var visits=new Companion(store,new Secrets(directory),provider);
    var door=new Identity("Entrance",0);
    var entranceWorld=world with {Menu=null,LocationKey="housing-outdoor:3:1",Entities=[world.Entities[0],new(2,door,new(1,0,0),true,"EventObj")],At=DateTimeOffset.UtcNow};
    visits.Exchange(new(Wire.Version,entranceWorld,[],[],null));
    visits.SavePlace("House entrance");
    var entrance=store.Places().Single(p=>p.Name=="House entrance");
    var room=new Place(Guid.NewGuid(),"Known room",101,21,new(8,0,0),DateTimeOffset.UtcNow,"housing-indoor:1234:0");store.Place(room);
    var recipe=new HouseVisitRequest(entrance.Id,room.Id,door,"Das Haus betreten?","Ja",a,Confirmed:true);
    Check(visits.StartHouseVisit(recipe with {Confirmed=false}) is not null,"House visit needs explicit confirmation");
    Check(visits.StartHouseVisit(recipe with {Guest=b}) is not null,"Unobserved/unpermitted guest cannot acquire a visit memory");
    Check(visits.StartHouseVisit(recipe with {Door=a}) is not null,"House entry must target an observed non-player door");
    Check(visits.StartHouseVisit(recipe) is null,"House recipe binds saved exterior/interior and observed guest");
    Check(visits.Submit(new(ActionKind.Emote,Text:"wave",Confirmed:true))is not null,"Manual action cannot overwrite an active visit's step ownership");
    BridgeOutput VisitExchange(WorldState w,ActionResult[]? results=null,Frame? image=null)=>visits.Exchange(new(Wire.Version,w with {At=DateTimeOffset.UtcNow},[],results??[],image));
    ActionResult Success(ActionRequest request)=>new(request.Id,Outcome.Succeeded,"Observed result",DateTimeOffset.UtcNow);
    var approach=VisitExchange(entranceWorld).Actions.Single();Check(approach.Kind==ActionKind.Move,"Visit approaches the saved entrance first");
    Check(VisitExchange(entranceWorld,[new(approach.Id,Outcome.Accepted,"pending",DateTimeOffset.UtcNow)]).Actions.Length==0,"Accepted approach is not completed approach");
    var interact=VisitExchange(entranceWorld,[Success(approach)]).Actions.Single();Check(interact.Kind==ActionKind.Interact&&interact.Target==door,"Only confirmed arrival permits door interaction");
    Check(VisitExchange(entranceWorld,[Success(interact)]).Actions.Length==0,"Visit waits for the real entry dialog");
    var wrongPrompt=entranceWorld with {Menu=new("SelectYesno","wrong","Purchase item?",[new(0,"Ja",true),new(1,"Nein",true)])};
    Check(VisitExchange(wrongPrompt).Actions.Length==0,"Unrelated confirmation cannot become house entry");
    Check(JsonSerializer.SerializeToElement(visits.State(),Wire.Json).GetProperty("visit").GetProperty("status").GetString()!.StartsWith("Angehalten"),"Unexpected menu stops the visit");
    visits.Stop();VisitExchange(entranceWorld);Check(visits.StartHouseVisit(recipe)is null,"Stopped recipe needs a new explicit start");
    approach=VisitExchange(entranceWorld).Actions.Single();interact=VisitExchange(entranceWorld,[Success(approach)]).Actions.Single();
    var entryWorld=entranceWorld with {Menu=new("SelectYesno","entry","Das Haus betreten?",[new(0,"Ja",true),new(1,"Nein",true)])};
    var entry=VisitExchange(entryWorld,[Success(interact)]).Actions.Single();
    Check(entry.Kind==ActionKind.Menu&&entry.MenuSignature=="entry"&&entry.ExpectedTerritory==room.Territory,"Entry binds fresh menu and the saved interior destination");
    Check(!ActionPolicy.TransitionReached(entry,entryWorld with {Menu=null}),"Closing a dialog does not prove entry");
    var inside=entryWorld with {ZoneGeneration=2,Territory=room.Territory,LocationKey=room.LocationKey,Menu=null,Position=room.Position,Entities=[new(1,a,room.Position,true,"Player")]};
    Check(!ActionPolicy.TransitionReached(entry,inside with {Territory=999}),"Wrong territory is not a successful house arrival");
    Check(!ActionPolicy.TransitionReached(entry,inside with {CanAct=false}),"Loading does not count as arrival");
    Check(!ActionPolicy.TransitionReached(entry,inside with {CurrentWorld=22}),"Wrong world is not a successful house arrival");
    Check(!ActionPolicy.TransitionReached(entry,inside with {LocationKey="housing-indoor:5678:0"}),"A different estate sharing the same territory is not the saved house");
    Check(ActionPolicy.TransitionReached(entry,inside),"Correct observed interior completes the entry transition");
    var entryGeneration=VisitExchange(entryWorld,[new(entry.Id,Outcome.Accepted,"entering",DateTimeOffset.UtcNow)]).Generation;
    Check(VisitExchange(inside with {CanAct=false}).Generation==entryGeneration,"Deliberate house transition survives loading without restarting actions");
    var roomMove=VisitExchange(inside,[Success(entry)]).Actions.Single();Check(roomMove.Kind==ActionKind.Move&&roomMove.Position==room.Position,"Verified entry permits movement to the saved room");
    var capture=VisitExchange(inside,[Success(roomMove)]).Actions.Single();Check(capture.Kind==ActionKind.Capture,"Room arrival requests a fresh frame");
    var roomFrame=new Frame(inside.ClientId,inside.ZoneGeneration,DateTimeOffset.UtcNow,frame.DataUrl);
    VisitExchange(inside,[Success(capture)],roomFrame);
    JsonElement visitState=default;
    for(var attempt=0;attempt<100;attempt++){await Task.Delay(10);visitState=JsonSerializer.SerializeToElement(visits.State(),Wire.Json);if(visitState.GetProperty("visit").GetProperty("status").GetString()=="Abgeschlossen")break;}
    Check(visitState.GetProperty("visit").GetProperty("status").GetString()=="Abgeschlossen","House visit finishes after actual arrival, frame and typed image response");
    var visitFact=store.Memories(Tell(a,"").Conversation).Single(m=>m.Text.StartsWith("Besuch am gespeicherten Ort Known room"));
    Check(visitFact.Status=="confirmed"&&visitFact.Source.Contains("action:"+entry.Id)&&visitFact.Source.Contains("observation:"),"Visit memory carries action and image provenance");
    Check(store.Memories(Tell(a,"").Conversation).Any(m=>m.Text.Contains("Ein Tisch")&&m.Status=="derived"),"Model room interpretation remains a candidate, not an observed fact");
    Check(!store.Memories(Tell(b,"").Conversation).Any(m=>m.Text.StartsWith("Besuch")),"Visit memory does not enter a different private thread");
    var memoryCount=store.Memories().Length;VisitExchange(inside,[Success(capture)],roomFrame);
    Check(store.Memories().Length==memoryCount,"Repeated capture result cannot create a duplicate episode");
    visits.Stop();VisitExchange(entranceWorld);visits.StartHouseVisit(recipe);
    approach=VisitExchange(entranceWorld).Actions.Single();interact=VisitExchange(entranceWorld,[Success(approach)]).Actions.Single();entry=VisitExchange(entryWorld,[Success(interact)]).Actions.Single();
    VisitExchange(inside with {Self=null,CanAct=false});
    var changed=VisitExchange(inside with {Self=b});
    Check(changed.Generation>entry.Generation&&changed.Actions.Length==0,"Character change invalidates even an active entry transition");
    visits.Stop();VisitExchange(entranceWorld);visits.StartHouseVisit(recipe);
    approach=VisitExchange(entranceWorld).Actions.Single();interact=VisitExchange(entranceWorld,[Success(approach)]).Actions.Single();entry=VisitExchange(entryWorld,[Success(interact)]).Actions.Single();
    roomMove=VisitExchange(inside,[Success(entry)]).Actions.Single();capture=VisitExchange(inside,[Success(roomMove)]).Actions.Single();
    var beforeAbsentGuest=store.Memories().Length;
    VisitExchange(inside with {Entities=[]},[Success(capture)],roomFrame with {At=DateTimeOffset.UtcNow});await Task.Delay(30);
    Check(store.Memories().Length==beforeAbsentGuest,"Absent guest cannot receive a fabricated shared-visit episode");
    visits.Stop();VisitExchange(entranceWorld);visits.StartHouseVisit(recipe);
    approach=VisitExchange(entranceWorld).Actions.Single();interact=VisitExchange(entranceWorld,[Success(approach)]).Actions.Single();entry=VisitExchange(entryWorld,[Success(interact)]).Actions.Single();
    roomMove=VisitExchange(inside,[Success(entry)]).Actions.Single();capture=VisitExchange(inside,[Success(roomMove)]).Actions.Single();
    var modelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);handler.BeforeResponse=modelGate.Task;
    var beforeStopMemory=store.Memories().Length;
    VisitExchange(inside,[Success(capture)],roomFrame with {At=DateTimeOffset.UtcNow});
    visits.Stop("Stopped during visit analysis");modelGate.SetResult();await Task.Delay(30);
    Check(store.Memories().Length==beforeStopMemory,"Stop during vision cannot later recreate a visit memory");
    handler.BeforeResponse=null;
    visits.Dispose();
    Environment.SetEnvironmentVariable("EMMY_DEEPSEEK_KEY",oldKey);
}
finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler:HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Requests {get;}=new();
    public string Response {get;set;}="{}";public HttpStatusCode Status{get;set;}=HttpStatusCode.OK;
    public Task? BeforeResponse{get;set;}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();Requests.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));if(BeforeResponse is not null)await BeforeResponse.WaitAsync(cancellationToken);return new HttpResponseMessage(Status){Content=new StringContent(Response,Encoding.UTF8,"application/json")};}
}
