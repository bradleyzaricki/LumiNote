using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumikitApp;

public class EffectData
{
    public LightBlock.Effect Type { get; set; }
    public Dictionary<string, double> Params { get; set; } = new();

    public EffectData DeepCopy() =>
        new() { Type = Type, Params = new Dictionary<string, double>(Params) };
}

public class EffectDataListConverter : JsonConverter<List<EffectData>>
{
    public override List<EffectData> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<EffectData>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (reader.TokenType == JsonTokenType.Number)
            {
                var effect = (LightBlock.Effect)reader.GetInt32();
                // Migrate old Seperate → Combine with Direction=-1
                if (effect == LightBlock.Effect.Seperate)
                    list.Add(new EffectData { Type = LightBlock.Effect.Combine, Params = new() { ["Direction"] = -1.0 } });
                else
                    list.Add(new EffectData { Type = effect });
            }
            else
            {
                // New format: { "Type": N, "Params": { ... } }
                list.Add(JsonSerializer.Deserialize<EffectData>(ref reader, options)!);
            }
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<EffectData> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}