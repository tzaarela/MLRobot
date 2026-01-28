using UnityEngine;
using System.Collections.Generic;

namespace RobotArm
{
	/// <summary>
	/// Simulates a magnetic/suction gripper that can pick up objects.
	/// Requires 80% of the magnet face to be in contact with the object's surface.
	/// Attach this to the Magnet GameObject (the disc face).
	/// </summary>
	public class MagnetGripper: MonoBehaviour
	{
		[Header("Magnet Settings")]
		[Tooltip("Radius of the magnet disc face")]
		public float magnetRadius = 0.05f;

		[Tooltip("Maximum distance to detect objects")]
		public float detectionRange = 0.02f;

		[Tooltip("If transform.position is not at the contact face, set this offset. Distance from transform to the actual magnet face along the normal direction.")]
		public float contactPointOffset = 0f;

		[Tooltip("Minimum contact percentage required to grip (0-1)")]
		[Range(0f, 1f)]
		public float minContactPercentage = 0.8f;

		[Tooltip("Layer mask for objects that can be picked up")]
		public LayerMask pickupLayerMask = ~0;

		[Tooltip("Tag for objects that can be picked up (leave empty for any)")]
		public string pickupTag = "Pickup";

		[Tooltip("Root transform of the robot (to exclude from detection). If not set, uses transform.root which may be too broad.")]
		public Transform robotRoot;

		[Tooltip("Local direction the magnet face points (the direction it 'pushes' objects). Default is up (0,1,0). Use down (0,-1,0) if rays point wrong way.")]
		public Vector3 magnetFaceDirection = Vector3.up;

		[Header("Visual Feedback")]
		[Tooltip("Renderer to change color when active")]
		public Renderer magnetRenderer;

		[Tooltip("Color when magnet is off")]
		public Color inactiveColor = Color.gray;

		[Tooltip("Color when magnet is on but not holding")]
		public Color activeColor = Color.blue;

		[Tooltip("Color when holding an object")]
		public Color holdingColor = Color.green;

		[Header("Debug")]
		public bool showDebugGizmos = true;
		public bool showDebugRays = false;

		[Header("Collision Prevention")]
		[Tooltip("Prevent magnet from pushing through objects")]
		public bool preventPushThrough = true;

		[Tooltip("Force applied to push objects away instead of going through")]
		public float pushForce = 10f;

		// State
		private bool isActive = false;
		private GameObject heldObject = null;
		private Rigidbody heldRigidbody = null;
		private FixedJoint holdJoint = null;
		private Vector3 localHoldPosition;
		private Quaternion localHoldRotation;
		private bool isColliding = false;
		private Vector3 lastValidPosition;
		private Rigidbody magnetRigidbody;

		// Cached
		private MaterialPropertyBlock propertyBlock;
		private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

		// Debug visualization
		private List<(Vector3 start, Vector3 end, bool hit)> debugRays = new List<(Vector3, Vector3, bool)>();

		/// <summary>
		/// Is the magnet currently activated?
		/// </summary>
		public bool IsActive => isActive;

		/// <summary>
		/// Is the magnet currently holding an object?
		/// </summary>
		public bool IsHoldingObject => heldObject != null;

		/// <summary>
		/// Reference to the currently held object (null if not holding)
		/// </summary>
		public GameObject HeldObject => heldObject;

		/// <summary>
		/// Get the contact percentage with an object (0-1)
		/// </summary>
		public float CurrentContactPercentage { get; private set; }

		/// <summary>
		/// Distance to nearest pickable object (float.MaxValue if none in range)
		/// </summary>
		public float DistanceToNearestObject { get; private set; } = float.MaxValue;

		/// <summary>
		/// Nearest detected object (null if none)
		/// </summary>
		public GameObject NearestObject { get; private set; }

		private void Awake()
		{
			propertyBlock = new MaterialPropertyBlock();
			lastValidPosition = transform.position;

			// Add rigidbody if needed for collision detection
			magnetRigidbody = GetComponent<Rigidbody>();
			if (magnetRigidbody == null && preventPushThrough)
			{
				magnetRigidbody = gameObject.AddComponent<Rigidbody>();
				magnetRigidbody.isKinematic = true;
				magnetRigidbody.useGravity = false;
			}

			UpdateVisuals();
		}

		private void FixedUpdate()
		{
			ScanForObjects();

			if (isActive && !IsHoldingObject)
			{
				TryGrabObject();
			}

			if (IsHoldingObject)
			{
				// Keep object attached
				MaintainHold();
			}
		}

		/// <summary>
		/// Turn magnet on/off
		/// </summary>
		public void SetActive(bool active)
		{
			isActive = active;

			if (!active && IsHoldingObject)
			{
				Release();
			}

			UpdateVisuals();
		}

		/// <summary>
		/// Toggle magnet state
		/// </summary>
		public void Toggle()
		{
			SetActive(!isActive);
		}

		/// <summary>
		/// Release any held object
		/// </summary>
		public void Release()
		{
			if (holdJoint != null)
			{
				Destroy(holdJoint);
				holdJoint = null;
			}

			if (heldRigidbody != null)
			{
				heldRigidbody.isKinematic = false;
				heldRigidbody = null;
			}

			heldObject = null;
			UpdateVisuals();
		}

		private void ScanForObjects()
		{
			NearestObject = null;
			DistanceToNearestObject = float.MaxValue;
			CurrentContactPercentage = 0f;

			// Calculate actual magnet face position
			Vector3 magnetNormal = transform.TransformDirection(magnetFaceDirection).normalized;
			Vector3 magnetCenter = transform.position - magnetNormal * contactPointOffset;

			// Find all colliders in range
			Collider[] colliders = Physics.OverlapSphere(magnetCenter, magnetRadius + detectionRange, pickupLayerMask);

			// Debug: Log what we found
			if (colliders.Length > 0 && showDebugGizmos)
			{
				Debug.Log($"[MagnetGripper] Found {colliders.Length} colliders in range");
				if (contactPointOffset > 0.001f)
				{
					Debug.Log($"[MagnetGripper] Using contact offset: {contactPointOffset}m");
					Debug.Log($"[MagnetGripper] Transform pos: {transform.position}, Contact pos: {magnetCenter}");
				}
			}

			foreach (var col in colliders)
			{
				// Skip if wrong tag
				if (!string.IsNullOrEmpty(pickupTag) && !col.CompareTag(pickupTag))
				{
					if (showDebugGizmos)
					{
						Debug.Log($"[MagnetGripper] Skipping {col.name} - wrong tag (has '{col.tag}', need '{pickupTag}')");
					}
					continue;
				}

				// Skip self and children of robot
				// Use robotRoot if assigned, otherwise fall back to transform.root
				Transform rootToCheck = robotRoot != null ? robotRoot : transform.root;
				if (col.transform.IsChildOf(rootToCheck))
				{
					if (showDebugGizmos)
					{
						Debug.Log($"[MagnetGripper] Skipping {col.name} - is child of robot root ({rootToCheck.name})");
					}
					continue;
				}

				// Calculate contact percentage
				float contactPercent = CalculateContactPercentage(col, magnetCenter, magnetNormal);

				if (contactPercent > CurrentContactPercentage)
				{
					CurrentContactPercentage = contactPercent;
					NearestObject = col.gameObject;

					// Calculate distance to surface
					Vector3 closestPoint = col.ClosestPoint(magnetCenter);
					DistanceToNearestObject = Vector3.Distance(magnetCenter, closestPoint);
				}
			}
		}

		private float CalculateContactPercentage(Collider targetCollider, Vector3 magnetCenter, Vector3 magnetNormal)
		{
			// Clear previous debug rays
			if (showDebugRays)
			{
				debugRays.Clear();
			}

			// Sample points across the magnet face in a grid pattern
			int samplesPerAxis = 5;
			int totalSamples = 0;
			int contactSamples = 0;

			Debug.Log($"[MagnetGripper] ===== CALCULATING CONTACT =====");
			Debug.Log($"[MagnetGripper] Target: {targetCollider.name}");
			Debug.Log($"[MagnetGripper] Magnet Center: {magnetCenter}");
			Debug.Log($"[MagnetGripper] Magnet Normal: {magnetNormal}");
			Debug.Log($"[MagnetGripper] Magnet Radius: {magnetRadius}");
			Debug.Log($"[MagnetGripper] Detection Range: {detectionRange}");
			Debug.Log($"[MagnetGripper] Layer Mask: {pickupLayerMask.value}");

			// Test one sample ray first
			Vector3 testRayStart = magnetCenter + magnetNormal * 0.001f;
			Vector3 testRayDir = -magnetNormal;
			float testRayDist = detectionRange + 0.003f;

			Debug.Log($"[MagnetGripper] TEST RAY:");
			Debug.Log($"  Start: {testRayStart}");
			Debug.Log($"  Direction: {testRayDir}");
			Debug.Log($"  Distance: {testRayDist}");

			RaycastHit[] testHits = Physics.RaycastAll(testRayStart, testRayDir, testRayDist, pickupLayerMask);
			Debug.Log($"  Hits: {testHits.Length}");
			foreach (var h in testHits)
			{
				bool isTarget = h.collider == targetCollider;
				bool isChildOfRoot = h.collider.transform.IsChildOf(transform.root);
				Debug.Log($"    - {h.collider.name} at {h.distance:F4}m [Target:{isTarget}, ChildOfRoot:{isChildOfRoot}]");
			}

			for (int x = 0; x < samplesPerAxis; x++)
			{
				for (int z = 0; z < samplesPerAxis; z++)
				{
					// Calculate sample position on magnet face
					float u = (x / (float)(samplesPerAxis - 1)) * 2f - 1f;
					float v = (z / (float)(samplesPerAxis - 1)) * 2f - 1f;

					// Skip points outside the circle
					if (u * u + v * v > 1f) continue;

					totalSamples++;

					// Get world position of sample point on the magnet face
					// The magnet face is in the XZ plane of the magnet's local space
					Vector3 localOffset = new Vector3(u * magnetRadius, 0f, v * magnetRadius);
					Vector3 samplePoint = transform.TransformPoint(localOffset);

					// Raycast downward (along negative magnet normal)
					// Start slightly above the magnet face
					Vector3 rayStart = samplePoint + magnetNormal * 0.001f;
					Vector3 rayDirection = -magnetNormal;
					float rayDistance = detectionRange + 0.003f;

					bool hitTarget = false;

					// Use RaycastAll to get all hits and check if target is among them
					RaycastHit[] hits = Physics.RaycastAll(rayStart, rayDirection, rayDistance, pickupLayerMask);

					foreach (var hit in hits)
					{
						// Skip if we hit ourselves
						Transform rootToCheck = robotRoot != null ? robotRoot : transform.root;
						if (hit.collider.transform.IsChildOf(rootToCheck))
							continue;

						if (hit.collider == targetCollider)
						{
							contactSamples++;
							hitTarget = true;
							break;
						}
					}

					// Store for debug visualization
					if (showDebugRays)
					{
						debugRays.Add((rayStart, rayStart + rayDirection * rayDistance, hitTarget));
					}
				}
			}

			float percentage = totalSamples > 0 ? (float)contactSamples / totalSamples : 0f;

			Debug.Log($"[MagnetGripper] ===== RESULT =====");
			Debug.Log($"[MagnetGripper] Contact: {contactSamples}/{totalSamples} = {percentage * 100f:F1}%");
			Debug.Log($"[MagnetGripper] ===================");

			return percentage;
		}

		private void TryGrabObject()
		{
			if (NearestObject == null) return;
			if (CurrentContactPercentage < minContactPercentage) return;

			// Check if object is close enough
			if (DistanceToNearestObject > detectionRange) return;

			// Get rigidbody
			Rigidbody rb = NearestObject.GetComponent<Rigidbody>();
			if (rb == null)
			{
				rb = NearestObject.GetComponentInParent<Rigidbody>();
			}

			if (rb == null)
			{
				Debug.LogWarning($"Cannot grab {NearestObject.name}: No Rigidbody found!");
				return;
			}

			// Grab the object
			heldObject = NearestObject;
			heldRigidbody = rb;

			Debug.Log($"Grabbed {heldObject.name}!");

			// Create fixed joint to hold object
			holdJoint = gameObject.AddComponent<FixedJoint>();
			holdJoint.connectedBody = rb;
			holdJoint.breakForce = Mathf.Infinity;
			holdJoint.breakTorque = Mathf.Infinity;

			// Store local offset for reference
			localHoldPosition = transform.InverseTransformPoint(rb.position);
			localHoldRotation = Quaternion.Inverse(transform.rotation) * rb.rotation;

			UpdateVisuals();
		}

		private void MaintainHold()
		{
			// Joint handles physics, but we can add additional logic here if needed

			// Check if object still exists
			if (heldObject == null || heldRigidbody == null)
			{
				Release();
			}
		}

		private void OnCollisionEnter(Collision collision)
		{
			if (!preventPushThrough) return;
			if (IsHoldingObject && collision.gameObject == heldObject) return;

			// Apply force to the object we're colliding with
			Rigidbody otherRb = collision.rigidbody;
			if (otherRb != null && !otherRb.isKinematic)
			{
				Vector3 pushDirection = collision.contacts[0].normal;
				otherRb.AddForce(-pushDirection * pushForce, ForceMode.Impulse);
			}

			isColliding = true;
		}

		private void OnCollisionStay(Collision collision)
		{
			if (!preventPushThrough) return;
			if (IsHoldingObject && collision.gameObject == heldObject) return;

			// Keep applying gentle force
			Rigidbody otherRb = collision.rigidbody;
			if (otherRb != null && !otherRb.isKinematic)
			{
				Vector3 pushDirection = collision.contacts[0].normal;
				otherRb.AddForce(-pushDirection * pushForce * 0.5f, ForceMode.Force);
			}

			isColliding = true;
		}

		private void OnCollisionExit(Collision collision)
		{
			isColliding = false;
		}

		/// <summary>
		/// Returns true if the magnet is currently colliding with something
		/// </summary>
		public bool IsColliding => isColliding;

		private void UpdateVisuals()
		{
			if (magnetRenderer == null) return;

			// Ensure propertyBlock is initialized
			if (propertyBlock == null)
			{
				propertyBlock = new MaterialPropertyBlock();
			}

			Color targetColor;
			if (IsHoldingObject)
				targetColor = holdingColor;
			else if (isActive)
				targetColor = activeColor;
			else
				targetColor = inactiveColor;

			magnetRenderer.GetPropertyBlock(propertyBlock);
			propertyBlock.SetColor(ColorProperty, targetColor);
			magnetRenderer.SetPropertyBlock(propertyBlock);
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			if (!showDebugGizmos) return;

			// Draw magnet face
			Gizmos.color = isActive ? Color.cyan : Color.gray;
			Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

			// Draw disc outline
			int segments = 32;
			Vector3 prevPoint = new Vector3(magnetRadius, 0, 0);
			for (int i = 1; i <= segments; i++)
			{
				float angle = (i / (float)segments) * Mathf.PI * 2f;
				Vector3 point = new Vector3(Mathf.Cos(angle) * magnetRadius, 0, Mathf.Sin(angle) * magnetRadius);
				Gizmos.DrawLine(prevPoint, point);
				prevPoint = point;
			}

			// Draw detection range
			Gizmos.color = Color.yellow;
			Gizmos.DrawWireSphere(Vector3.zero, magnetRadius + detectionRange);

			// Draw normal
			Gizmos.color = Color.green;
			Gizmos.DrawRay(Vector3.zero, magnetFaceDirection * 0.1f);

			Gizmos.matrix = Matrix4x4.identity;

			// Draw contact info
			if (Application.isPlaying && NearestObject != null)
			{
				Gizmos.color = CurrentContactPercentage >= minContactPercentage ? Color.green : Color.red;
				Gizmos.DrawWireSphere(NearestObject.transform.position, 0.05f);

				// Draw text
				UnityEditor.Handles.Label(
					NearestObject.transform.position + Vector3.up * 0.1f,
					$"Contact: {CurrentContactPercentage * 100f:F0}%\nDist: {DistanceToNearestObject:F3}m"
				);
			}

			// Draw debug rays
			if (showDebugRays && Application.isPlaying)
			{
				foreach (var ray in debugRays)
				{
					Gizmos.color = ray.hit ? Color.green : Color.red;
					Gizmos.DrawLine(ray.start, ray.end);
				}
			}
		}
#endif
	}
}