using System.Text.Json;
using System.Text.Json.Serialization;

namespace JSBA.CloudCore.Contracts
{
    public static class RimJson
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        public static string Serialize(RimDocument doc)
            => JsonSerializer.Serialize(doc, _options);

        public static RimDocument? Deserialize(string json)
            => JsonSerializer.Deserialize<RimDocument>(json, _options);
    }
}
