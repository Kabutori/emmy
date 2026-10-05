using Emmy.Core;
using Emmy.Host;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var directory=Environment.GetEnvironmentVariable("EMMY_DATA_DIR")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Emmy");
Directory.CreateDirectory(directory);
using var instance=new Mutex(true,"Emmy.CompanionHost.v1",out var ownsInstance);
if(!ownsInstance)throw new InvalidOperationException("Emmy Host läuft bereits für diesen Benutzer");
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:17840");
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=5_000_000);
builder.Logging.ClearProviders();builder.Logging.AddConsole();builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Services.ConfigureHttpJsonOptions(o=>o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(new Store(directory));builder.Services.AddSingleton(new Secrets(directory));
builder.Services.AddSingleton(new DeepSeekClient(new HttpClient()));builder.Services.AddSingleton<Companion>();
builder.Services.AddHostedService(s=>s.GetRequiredService<Companion>());
var app=builder.Build();
// Rotate the local capability at every host start. The plugin reads it under the same OS user.
var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var tokenPath=Path.Combine(directory,"bridge.token");File.WriteAllText(tokenPath,token);
if(!OperatingSystem.IsWindows())File.SetUnixFileMode(tokenPath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
app.Use(async (context,next)=>
{
    var host=context.Request.Host;
    if(host.Host!="127.0.0.1" || host.Port!=17840){context.Response.StatusCode=400;return;}
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    if(context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl="no-store";
        var origin=context.Request.Headers.Origin.ToString();
        if(origin.Length>0&&origin!="http://127.0.0.1:17840"){context.Response.StatusCode=403;return;}
        var supplied=context.Request.Headers["X-Emmy-Token"].ToString();
        if(supplied.Length!=token.Length||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(token))){context.Response.StatusCode=401;return;}
    }
    try{await next(context);}catch(Exception ex) when(ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new {error=ex.Message});}
});
app.UseDefaultFiles();app.UseStaticFiles();
app.MapGet("/api/state",(Companion c)=>c.State());
app.MapPut("/api/settings",(Settings s,Companion c)=>c.Configure(s)?Results.Ok(c.Settings):Results.Conflict(new {error="Einstellungen ungültig oder zwischenzeitlich geändert"}));
app.MapPost("/api/mode",(ModeChange s,Companion c)=>{c.SetMode(s.Mode);return Results.Ok();});
app.MapPost("/api/stop",(Companion c)=>{c.SetMode(Mode.Assisted);return Results.Ok();});
app.MapPost("/api/action",(OperatorRequest a,Companion c)=>{var error=c.Submit(a);return error is null?Results.Ok():Results.BadRequest(new {error});});
app.MapPost("/api/confirm",(ConfirmDraft d,Companion c)=>c.Confirm(d.Id,d.Text)?Results.Ok():Results.Conflict(new {error="Entwurf veraltet, ungültig oder bereits verbraucht"}));
app.MapPost("/api/chat",(OperatorChat chat,Companion c)=>{if(string.IsNullOrWhiteSpace(chat.Text)||chat.Text.Length>3000)return Results.BadRequest();c.OperatorChat(chat.Text);return Results.Accepted();});
app.MapPost("/api/key",(KeyChange k,Secrets secrets,Companion c)=>{if(k.Key.Length>5000)return Results.BadRequest(new {error="Schlüssel ungültig"});c.Stop("Modellschlüssel geändert");secrets.Write(k.Key);return Results.Ok();});
app.MapPost("/api/test",async (Companion c,CancellationToken t)=>await c.TestProvider(t));
app.MapPost("/api/vision",async (OperatorChat q,Companion c,CancellationToken t)=>await c.Observe(q.Text,t));
app.MapPost("/api/forget",(Forget f,Companion c)=>{c.Forget(f.Person,f.History,f.Memories);return Results.Ok();});
app.MapPut("/api/memory",(MemoryRecord m,Companion c)=>{if(m.Text.Length>4000||m.Source.Length>1000)return Results.BadRequest();c.AddMemory(m);return Results.Ok();});
app.MapDelete("/api/memory/{id:guid}",(Guid id,Companion c)=>{c.DeleteMemory(id);return Results.Ok();});
app.MapPost("/api/plan",(PlanInput p,Companion c)=>{var error=c.StartPlan(p.Title,p.Steps);return error is null?Results.Ok():Results.BadRequest(new {error});});
app.MapPost("/api/place",(OperatorChat p,Companion c)=>{var error=c.SavePlace(p.Text);return error is null?Results.Ok():Results.BadRequest(new {error});});
app.MapPost("/api/place/{id:guid}/visit",(Guid id,Companion c)=>{var error=c.VisitPlace(id);return error is null?Results.Ok():Results.BadRequest(new {error});});
app.MapDelete("/api/place/{id:guid}",(Guid id,Companion c)=>{c.DeletePlace(id);return Results.Ok();});
app.MapPost("/api/bridge",(BridgeInput input,Companion c)=>c.Exchange(input));
if(args.Contains("--open")&&OperatingSystem.IsWindows())System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://127.0.0.1:17840/#{token}"){UseShellExecute=true});
Console.WriteLine("Emmy Host startet auf http://127.0.0.1:17840");
await app.RunAsync();
record PlanInput(string Title,OperatorRequest[] Steps);
record ModeChange(Mode Mode);
record ConfirmDraft(Guid Id,string? Text);
record OperatorChat(string Text);
record KeyChange(string Key);
record Forget(string Person,bool History,bool Memories);
