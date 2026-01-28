using UnityEngine;
using System.Collections.Generic;

namespace RobotArm
{
	/// <summary>
	/// Simple magnetic gripper with 4-corner raycast detection.
	/// All 4 rays must hit the object to grip it.
	/// </summary>
	public class SimpleRaycastGripper: MonoBehaviour
	{
		[Header("Magnet Settings")]
		[Tooltip("Radius of the magnet disc face")]
		public float magnetRadius = 0.05f;

		[Tooltip("Maximum distance to raycast downward")]
		public float raycastDistance = 0.02f;

		[Tooltip("Layer mask for objects that can be picked up")]
		public LayerMask pickupLayerMask = ~0;

		[Tooltip("Tag for objects that can be picked up (leave empty for any)")]
		public string pickupTag = "Pickup";

		[Tooltip("Root transform of the robot (to exclude from detection)")]
		public Transform robotRoot;

		[Tooltip("Local direction the magnet face points (usually down: 0,-1,0)")]
		public Vector3 magnetFaceDirection = Vector3.down;

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
		public bool showDebugRays = true;

		[Header("Collision Prevention")]
		[Tooltip("Prevent magnet from pushing through objects")]
		public bool preventPushThrough = true;

		[Tooltip("Force applied on initial collision (impulse)")]
		public float pushForceOnEnter = 10f;

		[Tooltip("Continuous force applied while in contact (0 = disabled)")]
		public float pushForceOnStay = 5f;

		// State
		private bool isActive = false;
		private GameObject heldObject = null;
		private Rigidbody heldRigidbody = null;
		private FixedJoint holdJoint = null;

		// Detection results
		private int raysHitting = 0;
		private GameObject detectedObject = null;
		private Vector3[] rayStartPoints = new Vector3[4];
		private Vector3[] rayEndPoints = new Vector3[4];
		private bool[] rayHits = new bool[4];
		private bool isColliding = false;
		private Rigidbody magnetRigidbody;

		// Cached
		private MaterialPropertyBlock propertyBlock;
		private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

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
		/// Do all 4 raycasts hit an object? (true = full contact)
		/// </summary>
		public bool HasFullContact => raysHitting == 4;

		/// <summary>
		/// How many of the 4 rays are hitting (0-4)
		/// </summary>
		public int RaysHitting => raysHitting;

		/// <summary>
		/// The object detected by raycasts (null if none)
		/// </summary>
		public GameObject DetectedObject => detectedObject;

		/// <summary>
		/// Returns true if the magnet is currently colliding with something
		/// </summary>
		public bool IsColliding => isColliding;

		private void Awake()
		{
			propertyBlock = new MaterialPropertyBlock();

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
			// Scan for objects
			PerformRaycastScan();

			// Try to grab if active and have full contact
			if (isActive && !IsHoldingObject && HasFullContact && detectedObject != null)
			{
				TryGrabObject(detectedObject);
			}

			// Maintain hold
			if (IsHoldingObject)
			{
				MaintainHold();
			}
		}

		/// <summary>
		/// Perform 4-corner raycast scan
		/// </summary>
		private void PerformRaycastScan()
		{
			raysHitting = 0;
			detectedObject = null;

			// Get magnet face normal in world space
			Vector3 magnetNormal = transform.TransformDirection(magnetFaceDirection).normalized;
			Vector3 magnetCenter = transform.position;

			// Calculate 4 corner positions on the disc edge
			// Positions at 0°, 90°, 180°, 270° around the disc
			Vector3 right = transform.right;
			Vector3 forward = transform.forward;

			Vector3[] corners = new Vector3[4]
			{
				magnetCenter + right * magnetRadius,      // 0° (right)
				magnetCenter + forward * magnetRadius,    // 90° (forward)
				magnetCenter - right * magnetRadius,      // 180° (left)
				magnetCenter - forward * magnetRadius     // 270° (back)
			};

			// Cast rays from each corner
			GameObject firstHitObject = null;

			for (int i = 0; i < 4; i++)
			{
				Vector3 rayStart = corners[i];
				Vector3 rayDir = magnetNormal;
				float rayDist = raycastDistance;

				rayStartPoints[i] = rayStart;
				rayEndPoints[i] = rayStart + rayDir * rayDist;
				rayHits[i] = false;

				// Raycast
				RaycastHit hit;
				if (Physics.Raycast(rayStart, rayDir, out hit, rayDist, pickupLayerMask))
				{
					// Skip if wrong tag
					if (!string.IsNullOrEmpty(pickupTag) && !hit.collider.CompareTag(pickupTag))
						continue;

					// Skip if part of robot
					Transform rootToCheck = robotRoot != null ? robotRoot : transform.root;
					if (hit.collider.transform.IsChildOf(rootToCheck))
						continue;

					// Valid hit!
					rayHits[i] = true;
					raysHitting++;

					// Track which object we hit
					if (firstHitObject == null)
					{
						firstHitObject = hit.collider.gameObject;
					}
					else if (hit.collider.gameObject != firstHitObject)
					{
						// Different objects on different rays - not valid for gripping
						// Keep counting but don't set detected object
						continue;
					}
				}
			}

			// Only set detected object if all rays hit the SAME object
			if (raysHitting == 4 && firstHitObject != null)
			{
				detectedObject = firstHitObject;
			}
			else
			{
				detectedObject = null;
			}
		}

		/// <summary>
		/// Try to grab the detected object
		/// </summary>
		private void TryGrabObject(GameObject obj)
		{
			// Get rigidbody
			Rigidbody rb = obj.GetComponent<Rigidbody>();
			if (rb == null)
			{
				rb = obj.GetComponentInParent<Rigidbody>();
			}

			if (rb == null) return;

			// Grab the object
			heldObject = obj;
			heldRigidbody = rb;

			// Create fixed joint to hold object
			holdJoint = gameObject.AddComponent<FixedJoint>();
			holdJoint.connectedBody = rb;
			holdJoint.breakForce = Mathf.Infinity;
			holdJoint.breakTorque = Mathf.Infinity;

			UpdateVisuals();

			if (showDebugGizmos)
			{
				Debug.Log($"[SimpleGripper] Grabbed: {obj.name}");
			}
		}

		/// <summary>
		/// Maintain hold on object
		/// </summary>
		private void MaintainHold()
		{
			// Check if object still exists
			if (heldObject == null || heldRigidbody == null)
			{
				Release();
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

			if (showDebugGizmos && heldObject != null)
			{
				Debug.Log($"[SimpleGripper] Released: {heldObject.name}");
			}

			heldObject = null;
			UpdateVisuals();
		}

		private void UpdateVisuals()
		{
			if (magnetRenderer == null) return;

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

		private void OnCollisionEnter(Collision collision)
		{
			if (!preventPushThrough) return;
			if (IsHoldingObject && collision.gameObject == heldObject) return;

			// Apply force to the object we're colliding with
			if (pushForceOnEnter > 0f)
			{
				Rigidbody otherRb = collision.rigidbody;
				if (otherRb != null && !otherRb.isKinematic)
				{
					Vector3 pushDirection = collision.contacts[0].normal;
					otherRb.AddForce(-pushDirection * pushForceOnEnter, ForceMode.Impulse);
				}
			}

			isColliding = true;
		}

		private void OnCollisionStay(Collision collision)
		{
			if (!preventPushThrough) return;
			if (IsHoldingObject && collision.gameObject == heldObject) return;

			// Apply continuous force if enabled (set to 0 to disable)
			if (pushForceOnStay > 0f)
			{
				Rigidbody otherRb = collision.rigidbody;
				if (otherRb != null && !otherRb.isKinematic)
				{
					Vector3 pushDirection = collision.contacts[0].normal;
					otherRb.AddForce(-pushDirection * pushForceOnStay, ForceMode.Force);
				}
			}

			isColliding = true;
		}

		private void OnCollisionExit(Collision collision)
		{
			isColliding = false;
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			if (!showDebugGizmos) return;

			// Draw magnet disc
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

			// Draw normal
			Gizmos.color = Color.green;
			Gizmos.DrawRay(Vector3.zero, magnetFaceDirection * 0.1f);

			Gizmos.matrix = Matrix4x4.identity;

			// Draw raycast results
			if (Application.isPlaying && showDebugRays)
			{
				for (int i = 0; i < 4; i++)
				{
					Gizmos.color = rayHits[i] ? Color.green : Color.red;
					Gizmos.DrawLine(rayStartPoints[i], rayEndPoints[i]);

					// Draw sphere at ray start
					Gizmos.DrawWireSphere(rayStartPoints[i], 0.005f);
				}
			}

			// Draw detected object
			if (Application.isPlaying && detectedObject != null)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireCube(detectedObject.transform.position, Vector3.one * 0.05f);

#if UNITY_EDITOR
				UnityEditor.Handles.Label(
					detectedObject.transform.position + Vector3.up * 0.1f,
					$"{raysHitting}/4 rays hit"
				);
#endif
			}
		}
#endif
	}
}