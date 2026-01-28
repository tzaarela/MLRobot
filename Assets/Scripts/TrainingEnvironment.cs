using UnityEngine;
using System.Collections.Generic;

namespace RobotArm
{
    /// <summary>
    /// Manages a training environment with a robot, target object, and drop zone.
    /// Can be duplicated for parallel training.
    /// </summary>
    public class TrainingEnvironment : MonoBehaviour
    {
        [Header("Environment Components")]
        public RobotController robotController;
        public RobotPickAndPlaceAgent agent;
        public Transform targetObject;
        public Transform dropOffZone;
        
        [Header("Spawn Settings")]
        public Transform objectSpawnArea;
        
        [Header("Visual")]
        [Tooltip("Unique color for this environment's drop zone")]
        public Color environmentColor = Color.green;
        
        private Renderer dropZoneRenderer;
        
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
            }
            
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
    }
    
    
}
