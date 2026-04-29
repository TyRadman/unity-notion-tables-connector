# Notion Tables Connector

Connects Unity ScriptableObject data to Notion tables. Push and pull field values between your project and Notion databases using simple attributes.

## Installation

### Via Unity Package Manager (Git URL)

1. Open **Window → Package Manager** in Unity.
2. Click the **+** button → **Add package from git URL…**
3. Paste:
   ```
   https://github.com/TyRadman/unity-notion-tables-connector.git
   ```
4. Click **Add**.

### Local (drag & drop)

Clone or download this repository and drop the folder into your Unity project's `Packages/` directory.

## Quick Start

1. Add the `[TNotionDatabase("your-api-key")]` attribute to a `ScriptableObject` class.
2. Mark fields with `[TNotionProperty("Notion Column Name")]`.
3. Open **Tools → Notion Syncer** to push/pull data.

## Requirements

- Unity **2021.3** or newer

## License

MIT — see [LICENSE](LICENSE) for details.
