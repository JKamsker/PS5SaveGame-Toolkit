using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.XorJson.Tests;

internal static class AttributeSearchData
{
    public const string CollectionId = "character/attributes";
    public const int UnmatchedRows = QueryableCollectionLimits.MaxPageSize + 5;

    public static byte[] Hero(IEnumerable<(string Name, int Value)> attributes)
    {
        var rows = JsonSerializer.Serialize(attributes.Select(attribute => new
        {
            AttributeName = attribute.Name, CurrentValue = attribute.Value,
        }));
        // Keep unknown spelling/trivia in the fixture to check token-only edits and undo.
        return Encoding.UTF8.GetBytes("""
            {"SerializeMeta":{"HardFormat":"FCharacterSaveV1","SoftVersion":2,"FormatHash":3777917472},
             "CharacterSaveV1":{"MetaData":{"IsOnline":false},"Ability":{"Attributes":
            """ + rows + "},\"FutureUnknown\":1.2300E+02,\"escaped\":\"\\u0041\"}}");
    }
}
