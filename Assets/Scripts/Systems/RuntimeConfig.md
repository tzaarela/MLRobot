# Runtime Configuration System

Loads settings from a JSON file and/or command-line arguments at startup, before any scene logic runs. Useful for configuring standalone builds without recompiling.

## Config File Location

| Context | Path |
|---------|------|
| Editor  | `<project root>/config.json` |
| Build   | Next to the `.exe` (same folder) |

If the file is missing, all values use their C# defaults. Partial JSON is fine — only specified fields are overridden.

## Example `config.json`

```json
{
  "agent": {
    "maxEpisodeSteps": 10000
  },
  "training": {
    "gridX": 6,
    "gridZ": 3,
    "spacing": 8.0
  }
}
```

## Available Parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `agent.maxEpisodeSteps` | int | 5000 | Max steps per episode |
| `training.gridX` | int | 4 | Number of parallel environments in X |
| `training.gridZ` | int | 2 | Number of parallel environments in Z |
| `training.spacing` | float | 5.0 | Spacing between environments |

## Command-Line Overrides

CLI args override the JSON file. Format: `--config.<section>.<field>=<value>`

```
ML-Robot.exe --config.agent.maxEpisodeSteps=15000 --config.training.gridX=6
```

## Adding a New Parameter

### 1. Add the field to the data class

In `RuntimeConfigData.cs`, add the field to the appropriate section class (or create a new section):

```csharp
[System.Serializable]
public class AgentConfig
{
    public int maxEpisodeSteps = 5000;
    public float newParam = 1.0f;  // <-- default MUST match the inspector default
}
```

### 2. Apply it in the consuming component

In the component's `Initialize()` or `Awake()`:

```csharp
myField = RuntimeConfig.Agent.newParam;
```

### 3. (Optional) Add a CLI override case

In `RuntimeConfig.cs`, add a case to `ApplyOverride()`:

```csharp
case "agent.newParam":
    if (float.TryParse(value, out float f))
    {
        _data.agent.newParam = f;
        return true;
    }
    Debug.LogWarning($"[RuntimeConfig] Invalid float for agent.newParam: {value}");
    return false;
```

## Adding a New Config Section

1. Create the section class in `RuntimeConfigData.cs`:

```csharp
[System.Serializable]
public class EnvironmentConfig
{
    public float spawnRadius = 0.3f;
}
```

2. Add it to the root class:

```csharp
public class RuntimeConfigData
{
    public AgentConfig agent = new AgentConfig();
    public EnvironmentConfig environment = new EnvironmentConfig();
}
```

3. Expose it in `RuntimeConfig.cs`:

```csharp
public static EnvironmentConfig Environment => _data.environment;
```

4. Add CLI cases with the `environment.` prefix.

## Architecture Notes

- `RuntimeConfig.Load()` runs via `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` — guaranteed before any `Awake()` or `Initialize()`
- Uses `JsonUtility.FromJsonOverwrite` for partial config support
- CLI parsing is a manual switch (no reflection) per architecture.md's "minimal magic" rule
- `RuntimeConfig.Loaded` can be checked if needed, but consumers don't need to — defaults are always valid
