using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class LocalsBrushContractSerializer
{
    // LOCALBRUSH pins type/strokes and integer deltas, but not the remaining key spellings.
    // This measurement pins mode/radius/feather/flow/points explicitly; BR-WP2 must preserve
    // this shape or remeasure. Retain the real compact v4 envelope, without sending brushes
    // through the production serializer (which intentionally rejects unknown local types).
    internal static string Serialize(BrushDocument document)
    {
        var root = JsonNode.Parse(EditSettingsJson.Serialize(new EditSettings()))!.AsObject();
        var strokes = new JsonArray();
        foreach (var stroke in LocalsBrushOracle.Encode(document))
        {
            var points = new JsonArray();
            foreach (var point in stroke.Points) points.Add(point);
            strokes.Add(new JsonObject
            {
                ["mode"] = stroke.Erase ? "erase" : "paint", ["radius"] = stroke.Radius,
                ["feather"] = stroke.Feather, ["flow"] = stroke.Flow, ["points"] = points
            });
        }
        root["locals"] = new JsonArray(new JsonObject
        {
            ["id"] = "27100000000000000000000000000000", ["ordinal"] = 1, ["type"] = "brush",
            ["enabled"] = true, ["exposure"] = 2, ["strokes"] = strokes
        });
        return root.ToJsonString();
    }
    internal static string SerializeDocuments(BrushDocument[] documents)
    {
        LocalsBrushOracle.ValidateDocuments(documents);
        var root = JsonNode.Parse(EditSettingsJson.Serialize(new EditSettings()))!.AsObject();
        var locals = new JsonArray();
        for (var i = 0; i < documents.Length; i++)
        {
            var local = JsonNode.Parse(Serialize(documents[i]))!["locals"]![0]!.DeepClone();
            local["id"] = $"2710000000000000000000000000000{i}";
            local["ordinal"] = i + 1;
            locals.Add(local);
        }
        root["locals"] = locals;
        return root.ToJsonString();
    }

}
