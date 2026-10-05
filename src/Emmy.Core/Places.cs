namespace Emmy.Core;
public record Place(Guid Id,string Name,uint Territory,uint World,Point Position,DateTimeOffset At,string LocationKey="");
public record Itinerary(Guid Id,string Title,OperatorRequest[] Steps,int Step,string Status);
public record HouseVisitRequest(Guid EntrancePlace,Guid RoomPlace,Identity Door,string MenuPrompt,string MenuText,Identity? Guest=null,string Question="Beschreibe die sichtbare Einrichtung.",bool Confirmed=false,string DoorObject="");
public record VisitEpisode(Guid Id,Place Room,Identity? Guest,string Status,Guid[] Actions,Guid? Observation=null);
