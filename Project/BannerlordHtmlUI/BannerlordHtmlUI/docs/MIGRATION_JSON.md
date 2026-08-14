# v0.37 C# JSON API migration

The v0.37 runtime removes `System.Text.Json` from BannerlordHtmlUI.

Change handlers from:

```csharp
Action<JsonElement>
Func<JsonElement, Task<object>>
```

to:

```csharp
Action<JToken>
Func<JToken, Task<object>>
```

Add:

```csharp
using Newtonsoft.Json.Linq;
```

Common replacements:

- `payload.TryGetProperty("x", out var value)` -> `var value = payload["x"];`
- `value.GetString()` -> `value.Value<string>()`
- `value.GetBoolean()` -> `value.Value<bool>()`
- `payload.ValueKind == JsonValueKind.Object` -> `payload?.Type == JTokenType.Object`
