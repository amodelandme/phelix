using System.Text.Json.Serialization;

namespace Phelix.Core.Session;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SensorStatus { Passed, Failed, Skipped }
