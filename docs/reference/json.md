# JSON Utilities Reference

Instruction-first reference for Koan’s JSON helpers. These APIs live under `Koan.Core.Json` and use Newtonsoft.Json, centralized via `JsonDefaults`.

## Contract
- Inputs/Outputs: strings, `JToken`/`JObject`/`JArray`, and POCOs.
- Serializer: `JsonDefaults.Settings` (camelCase, ignore null/defaults, DateTimeOffset, invariant culture, no whitespace).
- Merge: first layer wins on conflicts; arrays configurable (Union default, Replace, Concat, Union-by-key for arrays of objects).
- Path mapping: flatten/expand between JSON and a normalized dotted-key map; arrays with `[index]`; dots escaped as `\u002e`.
- Error modes: `FromJson<T>` throws on invalid JSON; `TryFromJson<T>` returns false and sets out var to default.

## Core utilities

- `JsonDefaults.Settings`
  - Central serializer settings used by all helpers.

- `string ToJson(this object? value)`
- `T? FromJson<T>(this string json)` / `object? FromJson(this string json, Type type)`
- `bool TryFromJson<T>(this string json, out T? value)` / `bool TryFromJson(this string json, Type type, out object? value)`
  - Ergonomic serialize/deserialize with standard settings; `TryFromJson` avoids exceptions for control paths.

- `string ToCanonicalJson(this string json)` / `string ToCanonicalJson(this JToken token)`
  - Produces a stable JSON string with object properties ordered for stability: non-array properties come first (sorted lexicographically), then array-valued properties (also sorted by name among themselves). Arrays keep their element order. No extra whitespace. Useful for hashing/signatures and diff-friendly output.

## Merge utilities

- `enum ArrayMergeStrategy { Union, Replace, Concat }`
- `sealed class JsonMergeOptions { ArrayStrategy, string? ArrayObjectKey }`
- `JToken JsonMerge.Merge(ArrayMergeStrategy strategy = Union, params string[] layers)`
- `JToken JsonMerge.Merge(JsonMergeOptions options, params string[] layers)`

Semantics:
- Earlier layers are stronger (first wins). Type conflicts resolve in favor of earlier layer at that node.
- Arrays:
  - Union (default): union-by-index; strong values overwrite at the same index; missing tail comes from weaker.
  - Replace: the strongest array replaces weaker arrays entirely.
  - Concat: append weaker arrays after stronger arrays.
  - Union-by-key: When `options.ArrayObjectKey` is provided and both arrays are objects containing that key, merge by key — order from strong preserved, strong wins conflicts, unseen keys from weak appended.

### Merge examples

```csharp
using Koan.Core.Json;

var strong = "{\"arr\":[1,2],\"obj\":{\"x\":1}}";
var weak   = "{\"arr\":[9,8,7],\"obj\":{\"x\":2,\"y\":3}}";

var merged = JsonMerge.Merge(ArrayMergeStrategy.Union, strong, weak);
// arr => [1,2,7]; obj.x => 1; obj.y => 3

var replaced = JsonMerge.Merge(ArrayMergeStrategy.Replace, strong, weak);
// arr => [1,2]

var byKey = JsonMerge.Merge(
    new JsonMerge.JsonMergeOptions { ArrayStrategy = ArrayMergeStrategy.Union, ArrayObjectKey = "id" },
    "{\"items\":[{\"id\":\"a\",\"v\":1},{\"id\":\"b\",\"v\":2}]}",
    "{\"items\":[{\"id\":\"b\",\"v\":9,\"extra\":1},{\"id\":\"c\",\"v\":3}]}"
);
// items => [ {id:a,v:1}, {id:b,v:2,extra:1}, {id:c,v:3} ]
```

## Path mapper

- `IDictionary<string,JToken?> JsonPathMapper.Flatten(JToken token)`
- `JToken JsonPathMapper.Expand(IDictionary<string,JToken?> map)`

Rules:
- Object keys use dot separators; literal dots must be escaped as `\u002e`.
- Arrays use `name[index]` or root `[index]`.

Example:
```csharp
var token = JToken.Parse("{ \"a\": { \"b.c\": 1 }, \"list\": [ { \"x\": 1 }, 2 ] }");
var flat = JsonPathMapper.Flatten(token);
// contains key: "a.b\\u002ec" -> 1
var back = JsonPathMapper.Expand(flat);
```

## Samples

```csharp
var obj = new { Name = "Koan", Count = 2, Skip = (string?)null };
var json = obj.ToJson(); // {"name":"Koan","count":2}

if (json.TryFromJson(out dynamic? dyn)) { /* ... */ }
var canonical = json.ToCanonicalJson();
```

## Edge cases
- Null/empty layers in merge are ignored.
- Mixed types at the same path: strong side takes whole node.
- Union-by-key applies only when both arrays are objects containing the configured key.

## References
- decisions/ARCH-0052-core-ids-and-json-merge-policy.md
- engineering/index.md
