namespace Emmy.Core;
public record Place(Guid Id,string Name,uint Territory,uint World,Point Position,DateTimeOffset At);
public record Itinerary(Guid Id,string Title,OperatorRequest[] Steps,int Step,string Status);
