using System.Text.Json.Nodes;

namespace DocumentService.Core.Common;

public static class OcrEditMetadata
{
    public static JsonObject Protect(JsonObject incoming,JsonNode? previous,string sourcePath)
    {
        foreach(var key in incoming.Select(pair=>pair.Key).Where(key=>key.Equals("editedDocument",StringComparison.OrdinalIgnoreCase)).ToArray())incoming.Remove(key);
        if(previous is JsonObject original)
        {
            var edit=original.FirstOrDefault(pair=>pair.Key.Equals("editedDocument",StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
            if(edit?["sourcePath"]?.GetValue<string>()==sourcePath)incoming["editedDocument"]=edit.DeepClone();
        }
        return incoming;
    }
}
