using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dona.Crm.UI.Components;

/// <summary>Compares editable model values, including nested lists, with the last saved state.</summary>
public sealed class FormChangeTracker
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new EmptyStringConverter() }
    };

    private string? baseline;

    public void Accept(object? model, object? additionalState = null) => baseline = Snapshot(model, additionalState);

    public bool IsChanged(object? model, object? additionalState = null) =>
        baseline is not null && baseline != Snapshot(model, additionalState);

    private static string Snapshot(object? model, object? additionalState) =>
        JsonSerializer.Serialize(model, Options) + "\n" + JsonSerializer.Serialize(additionalState);

    private sealed class EmptyStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetString();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value.Length == 0) writer.WriteNullValue(); else writer.WriteStringValue(value);
        }
    }
}
