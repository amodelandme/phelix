using System.Text.Json.Serialization;

namespace Phelix.Core.Session;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SensorResultEvent), "sensorResult")]
public abstract record TurnEvent(DateTimeOffset Timestamp);

public sealed record SensorResultEvent(
    DateTimeOffset Timestamp,
    string SensorName,
    string Output,
    SensorStatus Status
) : TurnEvent(Timestamp);
