using System.Text.Json.Serialization;

namespace ParkingManagement.Api.Contracts.Webhook;

public sealed record VehicleEventEnvelope(
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("license_plate")] string LicensePlate,
    [property: JsonPropertyName("entry_time")] DateTime? EntryTime,
    [property: JsonPropertyName("exit_time")] DateTime? ExitTime,
    [property: JsonPropertyName("lat")] double? Lat,
    [property: JsonPropertyName("lng")] double? Lng
);

public static class VehicleEventTypes
{
    public const string Entry = "ENTRY";
    public const string Parked = "PARKED";
    public const string Exit = "EXIT";
}
