using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestCodex.Web;

public static class QuestCodexJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };
}
