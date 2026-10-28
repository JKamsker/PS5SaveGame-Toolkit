# Explicit insertion templates

Array and object insertion offers either custom JSON, an empty JSON type, or generation from a
schema you explicitly supply. Every operation goes through Preview/Apply and one structural undo.
This is a syntax workbench: no built-in game schema, game default or safe progression preset is implied.

The optional schema uses a deliberately restricted JSON Schema vocabulary. It validates the **inserted
fragment only** during preview/apply; it neither validates the entire save nor constrains subsequent edits.
Leave it empty for ordinary round-1 custom JSON insertion. A supplied schema also validates custom JSON
and empty-type choices without filling, coercing or deleting anything.

Supported keywords:

| Keyword | Accepted form / behavior |
| --- | --- |
| `type` | Required at every schema node: one of `null`, `string`, `number`, `boolean`, `object`, `array`. No type unions or `integer`. |
| `default` | Any JSON literal. Only explicit generation uses it; the emitted fragment must satisfy the schema. Exact number and string escape spelling is preserved. |
| `properties` | Object mapping exact names to explicit schemas. Duplicate schema names reject. |
| `required` | Array of unique strings, each declared in `properties` for this subset. Generation emits these members in listed order, leaving optional members absent. |
| `additionalProperties` | Boolean only, default `true`. `false` rejects undeclared members. Schema-covered objects with duplicate occurrences reject as ambiguous. |
| `items` | One explicit item schema. No tuple/boolean forms. |
| `minItems`, `maxItems` | Nonnegative decimal integer count literals up to 250,000 (no fraction/exponent spelling), with minimum no greater than maximum. Both are enforced on the fragment. |

Unsupported keywords, including `$ref`, `$schema`, `enum`, numeric/string limits and combinators, reject
explicitly even in optional property schemas. This is not a full JSON Schema validator and resolves no
network or filesystem references. The supported vocabulary follows the [JSON Schema project reference](https://json-schema.org/understanding-json-schema/);
the implementation is original and no external schema pack is embedded.

Without an explicit `default`, generation emits required object properties, an array's `minItems`
entries, or an empty primitive (`null`, `""`, `0`, `false`). A nonempty generated array needs an `items`
schema. No neighboring row is consulted. JSON Schema's `default` remains an annotation during ordinary
validation; using it to generate a new fragment is an explicit action here.

For example, this user-authored synthetic schema generates `{"entries":[{"amount":1.2300e-99}]}`:

```json
{
  "type": "object",
  "required": ["entries"],
  "additionalProperties": false,
  "properties": {
    "entries": {
      "type": "array",
      "minItems": 1,
      "maxItems": 3,
      "items": {
        "type": "object",
        "required": ["amount"],
        "properties": {
          "amount": { "type": "number", "default": 1.2300e-99 }
        }
      }
    }
  }
}
```

Schema text is limited to 65,536 UTF-16 characters and JSON depth 16. Generation is limited to 4,096
output nodes and 128 generated entries per array; a larger explicit default can bypass the per-array
generation count but must still fit the total node/text bounds. Fragment text is limited to 1 MiB of
characters, and final output must also fit the document's UTF-8/depth/node/source-retention budgets.
Failed schema checks and resource limits leave live bytes/history unchanged.
