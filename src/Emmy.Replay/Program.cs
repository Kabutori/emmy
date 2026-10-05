using Emmy.Core;
using System.Text.Json;
if(args.Length!=1){Console.Error.WriteLine("Usage: Emmy.Replay policy-recording.jsonl");return 2;}
var count=0;
foreach(var line in File.ReadLines(args[0]))
{
    if(string.IsNullOrWhiteSpace(line))continue;
    var recording=JsonSerializer.Deserialize<ReplayCase>(line,Wire.Json)??throw new InvalidDataException("Invalid recording");
    foreach(var action in recording.Actions)
    {
        var reason=ActionPolicy.Validate(action,recording.Settings,recording.World,recording.Generation,recording.World.At);
        Console.WriteLine(JsonSerializer.Serialize(new{action.Id,action.Kind,permitted=reason is null,reason,dryRun=true},Wire.Json));count++;
    }
}
Console.Error.WriteLine($"Validated {count} recorded actions. No game adapter or network connection was loaded.");return 0;
record ReplayCase(Settings Settings,WorldState World,ActionRequest[] Actions,long Generation);
