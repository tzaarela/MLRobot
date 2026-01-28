using UnityEngine;

namespace RobotArm
{
    /// <summary>
    /// Debug helper script for diagnosing MagnetGripper issues.
    /// Attach this to the Magnet GameObject alongside MagnetGripper.
    /// Press 'L' key in Play Mode to run diagnostics.
    /// </summary>
    public class MagnetGripperDebugger : MonoBehaviour
    {
        [Header("References")]
        public MagnetGripper magnet;
        public GameObject targetCube;

        [Header("Debug Options")]
        public bool autoRunOnStart = false;
        public bool continuousDebug = false;

        private void Start()
        {
            if (magnet == null)
                magnet = GetComponent<MagnetGripper>();

            if (autoRunOnStart)
            {
                Invoke(nameof(RunDiagnostics), 1f);
            }
        }

        private void Update()
        {
            // Press 'L' to run diagnostics
            if (Input.GetKeyDown(KeyCode.L))
            {
                RunDiagnostics();
            }

            // Continuous debug mode
            if (continuousDebug && Time.frameCount % 60 == 0) // Every 60 frames
            {
                RunDiagnostics();
            }
        }

        public void RunDiagnostics()
        {
            Debug.Log("========================================");
            Debug.Log("MAGNET GRIPPER DIAGNOSTICS");
            Debug.Log("========================================");

            // 1. Check magnet component
            if (magnet == null)
            {
                Debug.LogError("❌ MagnetGripper component not found!");
                return;
            }
            Debug.Log("✓ MagnetGripper component found");

            // 2. Check magnet settings
            Debug.Log($"\n--- Magnet Settings ---");
            Debug.Log($"Magnet Radius: {magnet.magnetRadius}");
            Debug.Log($"Detection Range: {magnet.detectionRange}");
            Debug.Log($"Min Contact %: {magnet.minContactPercentage * 100f}%");
            Debug.Log($"Pickup Tag: '{magnet.pickupTag}'");
            Debug.Log($"Pickup Layer Mask: {LayerMaskToString(magnet.pickupLayerMask)}");

            // 3. Check magnet transform
            Debug.Log($"\n--- Magnet Transform ---");
            Debug.Log($"Position: {transform.position}");
            Debug.Log($"Local Up (face normal): {transform.up}");
            Debug.Log($"World Up dot product: {Vector3.Dot(transform.up, Vector3.down):F3} (should be ~1.0 if facing down)");
            Debug.Log($"Layer: {LayerMask.LayerToName(gameObject.layer)}");

            // 4. Check for target cube
            if (targetCube == null)
            {
                Debug.LogWarning("⚠ Target Cube not assigned in debugger - trying to find one...");
                targetCube = GameObject.FindWithTag(magnet.pickupTag);
                
                if (targetCube == null)
                {
                    Debug.LogError($"❌ Could not find any object with tag '{magnet.pickupTag}'");
                    return;
                }
                Debug.Log($"✓ Found cube: {targetCube.name}");
            }

            // 5. Check cube setup
            Debug.Log($"\n--- Target Cube Setup ---");
            Debug.Log($"Name: {targetCube.name}");
            Debug.Log($"Position: {targetCube.transform.position}");
            Debug.Log($"Layer: {LayerMask.LayerToName(targetCube.layer)}");
            Debug.Log($"Tag: '{targetCube.tag}'");

            // Check if cube's layer is in pickup mask
            int cubeLayer = targetCube.layer;
            bool layerIncluded = ((1 << cubeLayer) & magnet.pickupLayerMask) != 0;
            if (layerIncluded)
            {
                Debug.Log($"✓ Cube layer '{LayerMask.LayerToName(cubeLayer)}' IS in pickup layer mask");
            }
            else
            {
                Debug.LogError($"❌ Cube layer '{LayerMask.LayerToName(cubeLayer)}' is NOT in pickup layer mask!");
            }

            // Check collider
            Collider cubeCollider = targetCube.GetComponent<Collider>();
            if (cubeCollider == null)
            {
                Debug.LogError("❌ Cube has no Collider!");
            }
            else
            {
                Debug.Log($"✓ Cube has {cubeCollider.GetType().Name}");
                Debug.Log($"  Enabled: {cubeCollider.enabled}");
                Debug.Log($"  Is Trigger: {cubeCollider.isTrigger}");
            }

            // Check rigidbody
            Rigidbody cubeRb = targetCube.GetComponent<Rigidbody>();
            if (cubeRb == null)
            {
                Debug.LogError("❌ Cube has no Rigidbody!");
            }
            else
            {
                Debug.Log($"✓ Cube has Rigidbody");
                Debug.Log($"  Mass: {cubeRb.mass}");
                Debug.Log($"  Is Kinematic: {cubeRb.isKinematic}");
                Debug.Log($"  Use Gravity: {cubeRb.useGravity}");
            }

            // 6. Check distance
            float distance = Vector3.Distance(transform.position, targetCube.transform.position);
            float maxDetectionDist = magnet.magnetRadius + magnet.detectionRange;
            Debug.Log($"\n--- Distance Check ---");
            Debug.Log($"Distance to cube: {distance:F4}m");
            Debug.Log($"Max detection distance: {maxDetectionDist:F4}m");
            if (distance <= maxDetectionDist)
            {
                Debug.Log($"✓ Cube is within detection range");
            }
            else
            {
                Debug.LogWarning($"⚠ Cube is TOO FAR (by {distance - maxDetectionDist:F4}m)");
            }

            // 7. Manual sphere overlap test
            Debug.Log($"\n--- Manual Detection Test ---");
            Vector3 magnetCenter = transform.position;
            Collider[] colliders = Physics.OverlapSphere(
                magnetCenter,
                magnet.magnetRadius + magnet.detectionRange,
                magnet.pickupLayerMask
            );

            Debug.Log($"OverlapSphere found {colliders.Length} colliders:");
            foreach (var col in colliders)
            {
                bool isRoot = col.transform.IsChildOf(transform.root);
                bool hasTag = string.IsNullOrEmpty(magnet.pickupTag) || col.CompareTag(magnet.pickupTag);
                string status = "";
                
                if (isRoot) status = "❌ (is child of robot root)";
                else if (!hasTag) status = $"❌ (wrong tag: '{col.tag}')";
                else status = "✓ (valid target!)";
                
                Debug.Log($"  - {col.name} {status}");
            }

            // 8. Manual raycast test
            Debug.Log($"\n--- Manual Raycast Test ---");
            Vector3 magnetNormal = transform.up;
            Vector3 rayStart = magnetCenter + magnetNormal * 0.001f;
            Vector3 rayDirection = -magnetNormal;
            float rayDistance = magnet.detectionRange + 0.003f;

            Debug.Log($"Testing center ray:");
            Debug.Log($"  Start: {rayStart}");
            Debug.Log($"  Direction: {rayDirection}");
            Debug.Log($"  Distance: {rayDistance}");

            RaycastHit[] hits = Physics.RaycastAll(rayStart, rayDirection, rayDistance, magnet.pickupLayerMask);
            Debug.Log($"  Found {hits.Length} hits:");
            foreach (var hit in hits)
            {
                bool isCube = hit.collider.gameObject == targetCube;
                Debug.Log($"    - {hit.collider.name} at {hit.distance:F4}m {(isCube ? "✓ TARGET" : "")}");
            }

            // 9. Current magnet state
            Debug.Log($"\n--- Current Magnet State ---");
            Debug.Log($"Is Active: {magnet.IsActive}");
            Debug.Log($"Is Holding: {magnet.IsHoldingObject}");
            Debug.Log($"Nearest Object: {(magnet.NearestObject ? magnet.NearestObject.name : "none")}");
            Debug.Log($"Contact %: {magnet.CurrentContactPercentage * 100f:F1}%");
            Debug.Log($"Distance to Nearest: {magnet.DistanceToNearestObject:F4}m");

            Debug.Log("========================================");
            Debug.Log("END DIAGNOSTICS");
            Debug.Log("========================================\n");
        }

        private string LayerMaskToString(LayerMask mask)
        {
            string result = "";
            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) != 0)
                {
                    if (result.Length > 0) result += ", ";
                    result += LayerMask.LayerToName(i) + $" ({i})";
                }
            }
            return string.IsNullOrEmpty(result) ? "None" : result;
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || magnet == null) return;

            // Draw detection sphere
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, magnet.magnetRadius + magnet.detectionRange);

            // Draw direction arrow
            Gizmos.color = Color.red;
            Vector3 start = transform.position;
            Vector3 end = start - transform.up * 0.15f;
            Gizmos.DrawLine(start, end);
            
            // Draw arrow head
            Vector3 right = transform.right * 0.03f;
            Vector3 forward = transform.forward * 0.03f;
            Gizmos.DrawLine(end, end + right + transform.up * 0.03f);
            Gizmos.DrawLine(end, end - right + transform.up * 0.03f);
            Gizmos.DrawLine(end, end + forward + transform.up * 0.03f);
            Gizmos.DrawLine(end, end - forward + transform.up * 0.03f);

            // Draw line to target
            if (targetCube != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, targetCube.transform.position);
            }
        }
    }
}
