using UnityEngine;
using UnityEditor;
using Unity.MLAgents.Policies;

namespace RobotArm
{
#if UNITY_EDITOR
    /// <summary>
    /// Editor window that provides quick-start buttons for switching between
    /// Heuristic (Player) mode and AI Training/Inference mode.
    /// </summary>
    public class RobotTrainingModeSelector : EditorWindow
    {
        private BehaviorParameters[] cachedBehaviorParameters;
        private bool autoFindAgents = true;
        private Vector2 scrollPosition;

        [MenuItem("Window/Robot Arm/Training Mode Selector")]
        public static void ShowWindow()
        {
            var window = GetWindow<RobotTrainingModeSelector>("Training Mode");
            window.minSize = new Vector2(300, 200);
        }

        private void OnEnable()
        {
            RefreshAgentsList();
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            
            EditorGUILayout.LabelField("Robot Arm Training Mode Selector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Quickly switch between Player Control and AI Training modes.", MessageType.Info);
            
            GUILayout.Space(10);

            // Auto-find toggle
            EditorGUILayout.BeginHorizontal();
            autoFindAgents = EditorGUILayout.Toggle("Auto-Find Agents", autoFindAgents);
            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                RefreshAgentsList();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Large buttons for mode selection
            EditorGUILayout.BeginVertical("box");
            
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
            if (GUILayout.Button("▶ START AS PLAYER (Heuristic)", GUILayout.Height(50)))
            {
                StartAsPlayer();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.HelpBox("• Control robot with QWERTY + Arrow keys\n• Space to toggle magnet\n• Backspace to reset", MessageType.None);
            
            GUILayout.Space(10);
            
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f);
            if (GUILayout.Button("▶ START AI TRAINING/INFERENCE", GUILayout.Height(50)))
            {
                StartAsAI();
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.HelpBox("• Agent controls robot\n• Use mlagents-learn to train\n• Or assign trained model for inference", MessageType.None);
            
            EditorGUILayout.EndVertical();

            GUILayout.Space(20);

            // Display current mode and agents
            ShowCurrentStatus();
        }

        private void RefreshAgentsList()
        {
            if (autoFindAgents)
            {
                cachedBehaviorParameters = FindObjectsOfType<BehaviorParameters>();
            }
        }

        private void StartAsPlayer()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => {
                    SetAllAgentsMode(BehaviorType.HeuristicOnly);
                    SetParallelTraining(false);
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                };
            }
            else
            {
                SetAllAgentsMode(BehaviorType.HeuristicOnly);
                SetParallelTraining(false);
                EditorApplication.isPlaying = true;
            }

            Debug.Log("<color=green>[Training Mode]</color> Starting in PLAYER mode (Heuristic Only, parallel off)");
        }

        private void StartAsAI()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => {
                    SetAllAgentsMode(BehaviorType.Default);
                    SetParallelTraining(true);
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
                };
            }
            else
            {
                SetAllAgentsMode(BehaviorType.Default);
                SetParallelTraining(true);
                EditorApplication.isPlaying = true;
            }

            Debug.Log("<color=blue>[Training Mode]</color> Starting in AI mode (Training/Inference, parallel on)");
        }

        private void SetAllAgentsMode(BehaviorType mode)
        {
            RefreshAgentsList();
            
            if (cachedBehaviorParameters == null || cachedBehaviorParameters.Length == 0)
            {
                Debug.LogWarning("No BehaviorParameters found in scene. Make sure your agents have BehaviorParameters components.");
                return;
            }

            int count = 0;
            foreach (var bp in cachedBehaviorParameters)
            {
                if (bp != null)
                {
                    bp.BehaviorType = mode;
                    EditorUtility.SetDirty(bp);
                    count++;
                }
            }

            Debug.Log($"Set {count} agent(s) to {mode} mode");
        }

        private void ShowCurrentStatus()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Current Status", EditorStyles.boldLabel);
            
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Editor is not playing", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Simulation is running", MessageType.None);
            }

            GUILayout.Space(5);

            // Show agents
            RefreshAgentsList();
            if (cachedBehaviorParameters != null && cachedBehaviorParameters.Length > 0)
            {
                EditorGUILayout.LabelField($"Found {cachedBehaviorParameters.Length} Agent(s):", EditorStyles.miniBoldLabel);
                
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(150));
                foreach (var bp in cachedBehaviorParameters)
                {
                    if (bp != null)
                    {
                        Color modeColor = bp.BehaviorType == BehaviorType.HeuristicOnly ? Color.green : Color.cyan;
                        GUI.color = modeColor;
                        EditorGUILayout.BeginHorizontal("box");
                        GUI.color = Color.white;
                        
                        EditorGUILayout.LabelField(bp.gameObject.name, GUILayout.Width(150));
                        EditorGUILayout.LabelField($"[{bp.BehaviorType}]", EditorStyles.miniLabel);
                        
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("No agents found in scene", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private static void SetParallelTraining(bool enabled)
        {
            var ptm = Object.FindObjectOfType<ParallelTrainingManager>();
            if (ptm != null)
            {
                ptm.createOnStart = enabled;
                EditorUtility.SetDirty(ptm);
                Debug.Log($"[Training Mode] Parallel training {(enabled ? "enabled" : "disabled")}");
            }
        }

        private void OnInspectorUpdate()
        {
            // Refresh UI periodically
            Repaint();
        }
    }

    /// <summary>
    /// Adds convenient menu items for quick mode switching
    /// </summary>
    public static class RobotTrainingModeMenu
    {
        [MenuItem("Robot Arm/Play as Player %&p", priority = 1)]
        public static void PlayAsPlayer()
        {
            SetAllAgentModes(BehaviorType.HeuristicOnly);
            SetParallelTraining(false);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
            Debug.Log("<color=green>[Training Mode]</color> Set to PLAYER mode (Heuristic Only, parallel off)");
        }

        [MenuItem("Robot Arm/Play as AI %&a", priority = 2)]
        public static void PlayAsAI()
        {
            SetAllAgentModes(BehaviorType.Default);
            SetParallelTraining(true);
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
            Debug.Log("<color=blue>[Training Mode]</color> Set to AI mode (Training/Inference, parallel on)");
        }

        [MenuItem("Robot Arm/Set Mode/Heuristic Only (Player)", priority = 11)]
        public static void SetHeuristicMode()
        {
            SetAllAgentModes(BehaviorType.HeuristicOnly);
            Debug.Log("<color=green>[Training Mode]</color> All agents set to Heuristic Only");
        }

        [MenuItem("Robot Arm/Set Mode/Default (AI)", priority = 12)]
        public static void SetDefaultMode()
        {
            SetAllAgentModes(BehaviorType.Default);
            Debug.Log("<color=blue>[Training Mode]</color> All agents set to Default (AI)");
        }

        [MenuItem("Robot Arm/Set Mode/Inference Only", priority = 13)]
        public static void SetInferenceMode()
        {
            SetAllAgentModes(BehaviorType.InferenceOnly);
            Debug.Log("<color=cyan>[Training Mode]</color> All agents set to Inference Only");
        }

        private static void SetAllAgentModes(BehaviorType mode)
        {
            var agents = Object.FindObjectsOfType<BehaviorParameters>();
            int count = 0;

            foreach (var agent in agents)
            {
                agent.BehaviorType = mode;
                EditorUtility.SetDirty(agent);
                count++;
            }

            if (count == 0)
            {
                Debug.LogWarning("No agents found with BehaviorParameters component");
            }
            else
            {
                Debug.Log($"Updated {count} agent(s) to {mode} mode");
            }
        }

        private static void SetParallelTraining(bool enabled)
        {
            var ptm = Object.FindObjectOfType<ParallelTrainingManager>();
            if (ptm != null)
            {
                ptm.createOnStart = enabled;
                EditorUtility.SetDirty(ptm);
            }
        }
    }
#endif
}
