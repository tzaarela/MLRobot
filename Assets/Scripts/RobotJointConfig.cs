using UnityEngine;

namespace RobotArm
{
    /// <summary>
    /// Configuration for a single robot joint.
    /// </summary>
    [System.Serializable]
    public class JointConfig
    {
        [Tooltip("Reference to the joint transform (Rig_Arm_X)")]
        public Transform jointTransform;
        
        [Tooltip("Local axis this joint rotates around")]
        public Vector3 rotationAxis = Vector3.up;
        
        [Tooltip("Minimum rotation angle in degrees")]
        public float minAngle = -180f;
        
        [Tooltip("Maximum rotation angle in degrees")]
        public float maxAngle = 180f;
        
        [Tooltip("Rotation speed in degrees per second")]
        public float rotationSpeed = 90f;
        
        [Tooltip("Starting angle (0 = current rotation when scene starts)")]
        public float startAngle = 0f;
        
        // Runtime state
        [HideInInspector] public float currentAngle;
        [HideInInspector] public Quaternion initialLocalRotation;
        
        /// <summary>
        /// Returns the normalized angle (0-1) for ML observations
        /// </summary>
        public float NormalizedAngle
        {
            get
            {
                if (Mathf.Approximately(maxAngle, minAngle)) return 0.5f;
                return (currentAngle - minAngle) / (maxAngle - minAngle);
            }
        }
        
        /// <summary>
        /// Sets angle from normalized value (0-1)
        /// </summary>
        public void SetNormalizedAngle(float normalized)
        {
            currentAngle = Mathf.Lerp(minAngle, maxAngle, Mathf.Clamp01(normalized));
        }
    }
}
