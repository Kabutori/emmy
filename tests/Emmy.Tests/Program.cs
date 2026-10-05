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
    var social=new Companion(store,new Secrets(directory),provider);
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
    Environment.SetEnvironmentVariable("EMMY_DEEPSEEK_KEY",oldKey);
}
finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
Console.WriteLine($"{passed} checks passed.");

sealed class FakeHandler:HttpMessageHandler
{
    public System.Collections.Concurrent.ConcurrentQueue<string> Requests {get;}=new();
    public string Response {get;set;}="{}";public HttpStatusCode Status{get;set;}=HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();Requests.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));return new HttpResponseMessage(Status){Content=new StringContent(Response,Encoding.UTF8,"application/json")};}
}
