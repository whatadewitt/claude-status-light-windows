using System.Text.Json.Serialization;

namespace StatusLight.Core.Hook;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SessionRecord))]
public sealed partial class StatusLightJsonContext : JsonSerializerContext;
