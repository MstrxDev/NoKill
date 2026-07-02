using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoKill.Vault;

/// <summary>System.Text.Json refuses IntPtr; window handles serialize as plain numbers.</summary>
internal sealed class NintJsonConverter : JsonConverter<nint>
{
    public override nint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        (nint)reader.GetInt64();

    public override void Write(Utf8JsonWriter writer, nint value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((long)value);
}
