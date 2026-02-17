using UnityEngine;
using System.IO;

namespace MLRobot.Systems
{
    public static class RuntimeConfig
    {
        public static AgentConfig Agent => _data.agent;
        public static TrainingConfig Training => _data.training;

        public static bool Loaded { get => _loaded; set => _loaded = value; }

        private static RuntimeConfigData _data = new RuntimeConfigData();
        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            _data = new RuntimeConfigData();
            _loaded = false;

            LoadFromFile();
            ApplyCommandLineOverrides();

            _loaded = true;
        }

        private static void LoadFromFile()
        {
            string path = GetConfigPath();

            if (!File.Exists(path))
            {
                Debug.Log($"[RuntimeConfig] No config file at {path} — using defaults.");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                JsonUtility.FromJsonOverwrite(json, _data);
                Debug.Log($"[RuntimeConfig] Loaded config from {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RuntimeConfig] Failed to parse {path}: {e.Message} — using defaults.");
                _data = new RuntimeConfigData();
            }
        }

        private static void ApplyCommandLineOverrides()
        {
            string[] args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--config."))
                    continue;

                // Format: --config.section.field=value
                string arg = args[i].Substring("--config.".Length);
                int eqIndex = arg.IndexOf('=');
                if (eqIndex < 0)
                {
                    Debug.LogWarning($"[RuntimeConfig] Malformed override (missing '='): {args[i]}");
                    continue;
                }

                string key = arg.Substring(0, eqIndex);
                string value = arg.Substring(eqIndex + 1);

                if (ApplyOverride(key, value))
                    Debug.Log($"[RuntimeConfig] CLI override: {key} = {value}");
                else
                    Debug.LogWarning($"[RuntimeConfig] Unknown config key: {key}");
            }
        }

        /// <summary>
        /// Manual switch for command-line overrides. No reflection — add new cases as needed.
        /// </summary>
        private static bool ApplyOverride(string key, string value)
        {
            switch (key)
            {
                case "agent.maxEpisodeSteps":
                    if (int.TryParse(value, out int steps))
                    {
                        _data.agent.maxEpisodeSteps = steps;
                        return true;
                    }
                    Debug.LogWarning($"[RuntimeConfig] Invalid int for agent.maxEpisodeSteps: {value}");
                    return false;

                case "training.gridX":
                    if (int.TryParse(value, out int gx))
                    {
                        _data.training.gridX = gx;
                        return true;
                    }
                    Debug.LogWarning($"[RuntimeConfig] Invalid int for training.gridX: {value}");
                    return false;

                case "training.gridZ":
                    if (int.TryParse(value, out int gz))
                    {
                        _data.training.gridZ = gz;
                        return true;
                    }
                    Debug.LogWarning($"[RuntimeConfig] Invalid int for training.gridZ: {value}");
                    return false;

                case "training.spacing":
                    if (float.TryParse(value, out float sp))
                    {
                        _data.training.spacing = sp;
                        return true;
                    }
                    Debug.LogWarning($"[RuntimeConfig] Invalid float for training.spacing: {value}");
                    return false;

                default:
                    return false;
            }
        }

        private static string GetConfigPath()
        {
            // Application.dataPath:
            //   Build:  <exe_folder>/ML-Robot_Data
            //   Editor: <project_root>/Assets
            // Parent gives us the folder next to the .exe (build) or project root (editor)
            string root = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(root, "config.json");
        }
    }
}
