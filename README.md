# Notion Tables Connector

Syncs Unity ScriptableObject fields with Notion database columns via Push/Pull.

## Installation

**Package Manager → + → Add package from git URL:**
```
https://github.com/TyRadman/unity-notion-tables-connector.git
```

Or clone and drop into `Packages/`.

---

## Setup

### 1. Configure the API token

Open **Tools → Notion Syncer**, paste your [Notion integration token](https://www.notion.so/my-integrations) into the **Notion API Token** field, and click **Save Token**. The token is AES-256 encrypted and stored locally — never in source control.

### 2. Share your database with the integration

In Notion, open your database → **···** menu → **Connect to** → select your integration.

### 3. Get your database ID

Open the database as a full page. The ID is the 32-character hex string in the URL:
```
https://www.notion.so/My-DB-<DATABASE_ID>?v=...
```

### 4. Annotate your ScriptableObject

```csharp
[TNotionDatabase("your-32-char-database-id")]
[CreateAssetMenu(fileName = "Item", menuName = "Item")]
public class Item : ScriptableObject
{
    [TNotionProperty("Name")]  private string _name;
    [TNotionProperty("Value")] private float  _value;
    [TNotionProperty("Ready")] private bool   _ready;
}
```

`[TNotionProperty]` maps a field to a Notion column by name. The Notion column type must match the C# type:

| C# type | Notion column type |
|---|---|
| `string` | Text |
| `int / long / float / double` | Number |
| `bool` | Checkbox |

The **Name** column (title) in Notion is used as the page key and maps to `asset.name` — do not tag a field with `[TNotionProperty("Name")]` unless it is actually the title column.

### 5. Push / Pull

Open **Tools → Notion Syncer**, select the databases you want to sync, then click **Push** or **Pull**.

- **Push** — creates a new page for each asset that has no matching row, or updates an existing one.
- **Pull** — reads Notion values into the matching assets and saves them.

Pages are matched by the asset's filename (without extension).

---

## Per-asset database ID

If different assets of the same type should sync to different Notion databases, implement `ITNotionSyncUniqueTable` and use the no-argument attribute form:

```csharp
[TNotionDatabase]
public class Item : ScriptableObject, ITNotionSyncUniqueTable
{
    [field: SerializeField] public string TableID { get; set; } = "your-32-char-database-id";
}
```

---

## Requirements

Unity **2021.3** or newer.

## License

MIT — see [LICENSE](LICENSE) for details.
