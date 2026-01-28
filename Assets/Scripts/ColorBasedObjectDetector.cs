using UnityEngine;

namespace RobotArm
{
    /// <summary>
    /// Detects cubes/objects by color using a simulated "sensor" approach.
    /// Can be used as additional observation data for the ML agent.
    /// </summary>
    public class ColorBasedObjectDetector : MonoBehaviour
    {
        [Header("Detection Settings")]
        [Tooltip("Target color to detect")]
        public Color targetColor = Color.white;
        
        [Tooltip("Color matching tolerance (0-1)")]
        [Range(0f, 1f)]
        public float colorTolerance = 0.2f;
        
        [Tooltip("Maximum detection range")]
        public float detectionRange = 2f;
        
        [Tooltip("Field of view angle")]
        [Range(10f, 180f)]
        public float fieldOfView = 60f;
        
        [Tooltip("Layer mask for detectable objects")]
        public LayerMask detectionLayerMask = ~0;
        
        [Header("Sensor Position")]
        [Tooltip("Where the sensor is located (e.g., on the tool)")]
        public Transform sensorTransform;
        
        [Tooltip("Direction the sensor faces (local space)")]
        public Vector3 sensorDirection = Vector3.forward;
        
        [Header("Debug")]
        public bool showDebugVisuals = true;
        
        // Detection results
        public bool ObjectDetected { get; private set; }
        public Vector3 DetectedObjectPosition { get; private set; }
        public float DetectedObjectDistance { get; private set; }
        public Vector3 DirectionToObject { get; private set; }
        public GameObject DetectedObject { get; private set; }
        
        private void Update()
        {
            Scan();
        }
        
        /// <summary>
        /// Perform a scan for objects matching the target color
        /// </summary>
        public void Scan()
        {
            ObjectDetected = false;
            DetectedObject = null;
            DetectedObjectDistance = float.MaxValue;
            
            Transform sensor = sensorTransform != null ? sensorTransform : transform;
            Vector3 sensorPos = sensor.position;
            Vector3 sensorDir = sensor.TransformDirection(sensorDirection).normalized;
            
            // Find all colliders in range
            Collider[] colliders = Physics.OverlapSphere(sensorPos, detectionRange, detectionLayerMask);
            
            float closestMatchDistance = float.MaxValue;
            GameObject closestMatch = null;
            
            foreach (var col in colliders)
            {
                // Skip self
                if (col.transform.IsChildOf(transform.root)) continue;
                
                Vector3 toObject = col.transform.position - sensorPos;
                float distance = toObject.magnitude;
                
                // Check if within field of view
                float angle = Vector3.Angle(sensorDir, toObject);
                if (angle > fieldOfView / 2f) continue;
                
                // Check color
                if (MatchesTargetColor(col.gameObject))
                {
                    if (distance < closestMatchDistance)
                    {
                        closestMatchDistance = distance;
                        closestMatch = col.gameObject;
                    }
                }
            }
            
            if (closestMatch != null)
            {
                ObjectDetected = true;
                DetectedObject = closestMatch;
                DetectedObjectPosition = closestMatch.transform.position;
                DetectedObjectDistance = closestMatchDistance;
                DirectionToObject = (DetectedObjectPosition - sensorPos).normalized;
            }
        }
        
        /// <summary>
        /// Check if an object's color matches the target color
        /// </summary>
        private bool MatchesTargetColor(GameObject obj)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer == null) return false;
            
            // Try to get the material color
            Color objectColor = Color.white;
            
            if (renderer.sharedMaterial != null)
            {
                // Try common color property names
                if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                    objectColor = renderer.sharedMaterial.GetColor("_BaseColor");
                else if (renderer.sharedMaterial.HasProperty("_Color"))
                    objectColor = renderer.sharedMaterial.GetColor("_Color");
                else
                    return false;
            }
            
            // Compare colors (ignoring alpha)
            float colorDiff = Mathf.Abs(objectColor.r - targetColor.r) +
                              Mathf.Abs(objectColor.g - targetColor.g) +
                              Mathf.Abs(objectColor.b - targetColor.b);
            
            return colorDiff / 3f <= colorTolerance;
        }
        
        /// <summary>
        /// Get detection data as a normalized vector for ML observations
        /// Returns: [detected (0/1), direction.x, direction.y, direction.z, normalized_distance]
        /// </summary>
        public float[] GetObservationData()
        {
            float[] data = new float[5];
            
            data[0] = ObjectDetected ? 1f : 0f;
            
            if (ObjectDetected)
            {
                data[1] = DirectionToObject.x;
                data[2] = DirectionToObject.y;
                data[3] = DirectionToObject.z;
                data[4] = Mathf.Clamp01(DetectedObjectDistance / detectionRange);
            }
            else
            {
                data[1] = 0f;
                data[2] = 0f;
                data[3] = 0f;
                data[4] = 1f;
            }
            
            return data;
        }
        
        #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showDebugVisuals) return;
            
            Transform sensor = sensorTransform != null ? sensorTransform : transform;
            Vector3 sensorPos = sensor.position;
            Vector3 sensorDir = sensor.TransformDirection(sensorDirection).normalized;
            
            // Draw detection cone
            Gizmos.color = ObjectDetected ? Color.green : Color.yellow;
            
            // Draw center ray
            Gizmos.DrawRay(sensorPos, sensorDir * detectionRange);
            
            // Draw cone edges
            float halfAngle = fieldOfView / 2f * Mathf.Deg2Rad;
            Vector3 right = sensor.right;
            Vector3 up = sensor.up;
            
            int segments = 16;
            Vector3 prevPoint = Vector3.zero;
            
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 offset = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up) * Mathf.Sin(halfAngle);
                Vector3 dir = (sensorDir * Mathf.Cos(halfAngle) + offset).normalized;
                Vector3 point = sensorPos + dir * detectionRange;
                
                if (i > 0)
                {
                    Gizmos.DrawLine(prevPoint, point);
                }
                Gizmos.DrawLine(sensorPos, point);
                prevPoint = point;
            }
            
            // Draw detected object
            if (ObjectDetected && DetectedObject != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(DetectedObjectPosition, 0.1f);
                Gizmos.DrawLine(sensorPos, DetectedObjectPosition);
            }
        }
        #endif
    }
}
