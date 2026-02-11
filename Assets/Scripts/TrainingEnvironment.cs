using UnityEngine;
using System.Collections.Generic;

namespace RobotArm
{
    /// <summary>
    /// Manages a training environment with a robot, target object, and drop zone.
    /// Can be duplicated for parallel training.
    /// Provides centralized environment reset via events.
    /// </summary>
    public class TrainingEnvironment : MonoBehaviour
    {
        [Header("Environment Components")]
        public RobotController robotController;
        public RobotPickAndPlaceAgent agent;
        public Transform targetObject;
        public Transform dropOffZone;

        [Tooltip("Spawn area for the target object")]
        public Vector3 objectSpawnAreaMin = new Vector3(-0.5f, 0.1f, -0.5f);
        public Vector3 objectSpawnAreaMax = new Vector3(0.5f, 0.1f, 0.5f);

        [Header("Drop Zone Settings")]
        [Tooltip("Check if object must be fully contained (true) or just center inside (false)")]
        public bool requireFullContainment = true;

        [Tooltip("Time (in seconds) the object must stay in drop zone before success")]
        [Range(0f, 5f)]
        public float dropZoneStayDuration = 1f;

        [Header("Visual")]
        [Tooltip("Unique color for this environment's drop zone")]
        public Color environmentColor = Color.green;

        [Header("Debug")]
        public bool showDebugLogs = false;

		private Renderer dropZoneRenderer;
		private Rigidbody targetRigidbody;

        // Reset event - components subscribe to this to handle their own reset
        public event System.Action OnEnvironmentReset;

        private void Awake()
        {
            // Setup references if not assigned
            if (agent != null)
            {
                agent.environmentRoot = transform;

                if (robotController != null)
                    agent.robotController = robotController;
                if (targetObject != null)
                    agent.targetObject = targetObject;
                if (dropOffZone != null)
                    agent.dropOffZone = dropOffZone;

                // Wire up CartesianController to use this environment's robot
                RobotCartesianController cartesianController = agent.GetComponent<RobotCartesianController>();
                if (cartesianController != null && robotController != null)
                {
                    cartesianController.robotController = robotController;
                }
            }
            
            // Cache target rigidbody
            if (targetObject != null)
                targetRigidbody = targetObject.GetComponent<Rigidbody>();

            // Set environment color
            if (dropOffZone != null)
            {
                dropZoneRenderer = dropOffZone.GetComponent<Renderer>();
                if (dropZoneRenderer != null)
                {
                    MaterialPropertyBlock props = new MaterialPropertyBlock();
                    dropZoneRenderer.GetPropertyBlock(props);
                    props.SetColor("_BaseColor", environmentColor);
                    dropZoneRenderer.SetPropertyBlock(props);
                }
            }
        }

        /// <summary>
        /// Reset the environment to starting state.
        /// Triggers OnEnvironmentReset event for components to handle their own resets.
        /// </summary>
        public void ResetEnvironment()
        {
            // Release magnet first so cube doesn't follow robot joint movement
            if (robotController != null && robotController.magnet != null)
            {
                robotController.magnet.Release();
            }

            // Reset robot to starting position
            if (robotController != null)
            {
                robotController.ResetToStartPosition();
            }

            // Reset target object position and physics
            if (targetObject != null)
            {
                Vector3 randomLocalPos = GetRandomSpawnPosition();
                Vector3 worldPos = transform.TransformPoint(randomLocalPos);
                Quaternion newRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                if (targetRigidbody != null)
                {
                    // Disable interpolation to prevent visual blending from old position
                    var previousInterpolation = targetRigidbody.interpolation;
                    targetRigidbody.interpolation = RigidbodyInterpolation.None;
                    targetRigidbody.isKinematic = true;

                    targetObject.position = worldPos;
                    targetObject.rotation = newRotation;
                    Physics.SyncTransforms();

                    targetRigidbody.isKinematic = false;
                    targetRigidbody.velocity = Vector3.zero;
                    targetRigidbody.angularVelocity = Vector3.zero;
                    targetRigidbody.interpolation = previousInterpolation;
                }
                else
                {
                    targetObject.position = worldPos;
                    targetObject.rotation = newRotation;
                }
            }

            // Trigger reset event for all subscribed components
            OnEnvironmentReset?.Invoke();

            if (showDebugLogs)
                Debug.Log("[TrainingEnvironment] Environment reset");
        }

        /// <summary>
        /// Get a random spawn position within the configured spawn area bounds.
        /// Returns position in local space (relative to environment root).
        /// </summary>
        public Vector3 GetRandomSpawnPosition()
        {
            return new Vector3(
                Random.Range(objectSpawnAreaMin.x, objectSpawnAreaMax.x),
                Random.Range(objectSpawnAreaMin.y, objectSpawnAreaMax.y),
                Random.Range(objectSpawnAreaMin.z, objectSpawnAreaMax.z)
            );
        }

        /// <summary>
        /// Create a duplicate of this environment at the specified position
        /// </summary>
        public static TrainingEnvironment Duplicate(TrainingEnvironment source, Vector3 position)
        {
            GameObject copy = Instantiate(source.gameObject, position, Quaternion.identity);
            TrainingEnvironment env = copy.GetComponent<TrainingEnvironment>();

            // Assign a random color to distinguish environments
            env.environmentColor = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.5f, 1f);

            return env;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw spawn area bounds
            Gizmos.color = Color.yellow;
            Vector3 center = transform.TransformPoint((objectSpawnAreaMin + objectSpawnAreaMax) / 2f);
            Vector3 size = objectSpawnAreaMax - objectSpawnAreaMin;
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }


}
