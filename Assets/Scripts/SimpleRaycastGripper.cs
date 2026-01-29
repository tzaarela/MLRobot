using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

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

		[Header("Magnetic Pull")]
		[Tooltip("Distance within which magnet starts pulling")]
		public float magneticPullDistance = 0.05f;

		[Tooltip("Interpolation speed toward magnet")]
		public float magneticPullSpeed = 10f;

		public Transform anchorPoint;

		public float pullCompleteThreshold = 0.002f;

		[Header("Cooldown")]
		[Tooltip("Seconds to wait after release before magnet can activate again")]
		public float pickupCooldown = 1f;

		private float nextAllowedPickupTime = 0f;

		[Tooltip("Layer mask for objects that can be picked up")]
		public LayerMask pickupLayerMask = ~0;

		[Tooltip("Tag for objects that can be picked up (leave empty for any)")]
		public string pickupTag = "Pickup";

		[Tooltip("Root transform of the robot (to exclude from detection)")]
		public Transform robotRoot;

		public List<Collider> robotColliders;

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

		[Header("Debug Logging")]
		public bool showDebugGizmos = true;
		public bool showDebugRays = true;
		public bool debugMagneticPull = true;

		[Tooltip("Log state transitions (activate, grab, release)")]
		public bool logStateChanges = true;

		[Tooltip("Log collision monitor events")]
		public bool logCollisionMonitor = true;

		[Tooltip("Log cooldown status")]
		public bool logCooldown = true;

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
		private Transform originalParent = null;
		private CollisionReleaseMonitor collisionMonitor = null;

		// Detection results
		private int raysHitting = 0;
		private GameObject detectedObject = null;
		private Vector3[] rayStartPoints = new Vector3[4];
		private Vector3[] rayEndPoints = new Vector3[4];
		private bool[] rayHits = new bool[4];
		private bool isColliding = false;
		private Vector3[] rayHitPoints = new Vector3[4];

		//Magnetic Pull
		private Coroutine magneticPullRoutine = null;
		private bool pullComplete = false;
		public bool CanActivateMagnet => Time.time >= nextAllowedPickupTime;

		// Cached
		private MaterialPropertyBlock propertyBlock;
		private Rigidbody magnetRigidbody;
		private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");

		// Debug tracking
		private int frameCount = 0;
		private GameObject lastDetectedObject = null;

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

			if (logStateChanges)
			{
				Debug.Log($"[Magnet-{gameObject.name}] Initialized | MagnetRadius={magnetRadius}, RaycastDist={raycastDistance}, Cooldown={pickupCooldown}s");
			}

			UpdateVisuals();
		}

		private void FixedUpdate()
		{
			frameCount++;

			PerformRaycastScan();


			if (isActive && CanActivateMagnet && !IsHoldingObject && detectedObject != null)
			{
				// Start pull once
				if (magneticPullRoutine == null)
				{
					if (debugMagneticPull)
					{
						Debug.Log($"[Magnet-{gameObject.name}] Starting magnetic pull on {detectedObject.name} | Rays={raysHitting}/4");
					}
					magneticPullRoutine = StartCoroutine(MagneticPullCoroutine(detectedObject));
				}

				// Only grab AFTER pull finishes
				if (pullComplete && HasFullContact)
				{
					if (logStateChanges)
					{
						Debug.Log($"[Magnet-{gameObject.name}] Pull complete! Attempting grab | Rays={raysHitting}/4");
					}
					TryGrabObject(detectedObject);
					StopMagneticPull();
				}
			}
			else if (isActive && !CanActivateMagnet && logCooldown && frameCount % 60 == 0)
			{
				float timeRemaining = nextAllowedPickupTime - Time.time;
				Debug.Log($"[Magnet-{gameObject.name}] In cooldown | Time remaining: {timeRemaining:F2}s");
			}

			if (IsHoldingObject)
			{
				MaintainHold();
			}
		}

		private IEnumerator MagneticPullCoroutine(GameObject obj)
		{
			pullComplete = false;

			Rigidbody rb = obj.GetComponent<Rigidbody>();
			if (rb == null || rb.isKinematic)
			{
				if (debugMagneticPull)
					Debug.LogWarning($"[MagneticPull-{gameObject.name}] ABORT: {obj.name} has no valid Rigidbody (isKinematic={rb?.isKinematic})");

				StopMagneticPull();
				yield break;
			}

			if (debugMagneticPull)
				Debug.Log($"[MagneticPull-{gameObject.name}] Started pulling {obj.name} | InitialPos={obj.transform.position}");

			int pullIterations = 0;
			while (true)
			{
				pullIterations++;

				// --- Abort conditions ---
				if (!isActive)
				{
					if (debugMagneticPull)
						Debug.Log($"[MagneticPull-{gameObject.name}] ABORT: Magnet deactivated after {pullIterations} iterations");

					StopMagneticPull();
					yield break;
				}

				if (IsHoldingObject)
				{
					if (debugMagneticPull)
						Debug.Log($"[MagneticPull-{gameObject.name}] ABORT: Already holding an object after {pullIterations} iterations");

					StopMagneticPull();
					yield break;
				}

				if (detectedObject != obj)
				{
					if (debugMagneticPull)
						Debug.Log($"[MagneticPull-{gameObject.name}] ABORT: Lost target ({obj.name}) after {pullIterations} iterations");

					StopMagneticPull();
					yield break;
				}


				// Get magnet face normal in world space
				Vector3 magnetNormal = transform.TransformDirection(magnetFaceDirection).normalized;

				// Raycast
				RaycastHit hit;
				if (Physics.Raycast(anchorPoint.position, magnetNormal, out hit, raycastDistance, pickupLayerMask))
				{
					float distance = Vector3.Distance(hit.point, anchorPoint.position);

					// --- Close enough → finish ---
					if (distance <= pullCompleteThreshold)
					{
						pullComplete = true;

						if (debugMagneticPull)
							Debug.Log($"[MagneticPull-{gameObject.name}] ✓ Pull COMPLETE for {obj.name} after {pullIterations} iterations | FinalDistance={distance:F5}");

						yield break;
					}

					// Log pull progress every 10 iterations
					if (debugMagneticPull && pullIterations % 10 == 0)
					{
						Debug.Log($"[MagneticPull-{gameObject.name}] Pulling {obj.name} | Iteration={pullIterations}, Distance={distance:F5}, Threshold={pullCompleteThreshold:F5}");
					}

					// --- Move rigidbody (position) ---
					Vector3 targetOffset = anchorPoint.position - hit.point;
					Vector3 targetPosition = rb.position + targetOffset;
					rb.MovePosition(targetPosition);

					// --- Align object normal to magnet normal ---
					Quaternion alignRotation = Quaternion.FromToRotation(
						hit.normal,
						-magnetNormal
					);

					Quaternion targetRotation = alignRotation * rb.rotation;
					rb.MoveRotation(targetRotation);
				}
				else
				{
					if (debugMagneticPull && pullIterations % 30 == 0)
					{
						Debug.LogWarning($"[MagneticPull-{gameObject.name}] Raycast MISS during pull | Iteration={pullIterations}");
					}
				}

				yield return new WaitForFixedUpdate();
			}
		}

		private void StopMagneticPull()
		{
			if (magneticPullRoutine != null)
			{
				if (debugMagneticPull)
				{
					Debug.Log($"[MagneticPull-{gameObject.name}] Stopping magnetic pull coroutine");
				}
				StopCoroutine(magneticPullRoutine);
				magneticPullRoutine = null;
			}

			pullComplete = false;
		}

		/// <summary>
		/// Perform 4-corner raycast scan
		/// </summary>
		private void PerformRaycastScan()
		{
			// Don't scan if already holding something (object is parented, no need for raycasts)
			if (IsHoldingObject)
			{
				raysHitting = 4; // Keep showing full contact in debug
				detectedObject = heldObject;
				return;
			}

			raysHitting = 0;
			GameObject previousDetectedObject = detectedObject;
			detectedObject = null;

			// Get magnet face normal in world space
			Vector3 magnetNormal = transform.TransformDirection(magnetFaceDirection).normalized;
			Vector3 magnetCenter = transform.position;

			// Calculate 4 corner positions on the disc edge
			// Positions at 0°, 90°, 180°, 270° around the disc
			Vector3 up = transform.up;
			Vector3 forward = transform.forward;

			Vector3[] corners = new Vector3[4]
			{
				magnetCenter + up * magnetRadius,      // 0° (right)
				magnetCenter + forward * magnetRadius,    // 90° (forward)
				magnetCenter - up * magnetRadius,      // 180° (left)
				magnetCenter - forward * magnetRadius     // 270° (back)
			};

			// Cast rays from each corner
			GameObject firstHitObject = null;
			List<string> hitObjects = new List<string>();

			for (int i = 0; i < 4; i++)
			{
				Vector3 rayStart = corners[i];
				Vector3 rayDir = magnetNormal;
				float rayDist = raycastDistance;

				rayStartPoints[i] = rayStart;
				rayEndPoints[i] = rayStart + rayDir * rayDist;
				rayHits[i] = false;
				rayHitPoints[i] = Vector3.zero;

				// Raycast
				RaycastHit hit;
				if (Physics.Raycast(rayStart, rayDir, out hit, rayDist, pickupLayerMask))
				{
					// Skip if wrong tag
					if (!string.IsNullOrEmpty(pickupTag) && !hit.collider.CompareTag(pickupTag))
					{
						continue;
					}

					// Skip if part of robot
					Transform rootToCheck = robotRoot != null ? robotRoot : transform.root;
					if (hit.collider.transform.IsChildOf(rootToCheck))
					{
						continue;
					}

					// Valid hit!
					rayHits[i] = true;
					raysHitting++;
					rayHitPoints[i] = hit.point;
					hitObjects.Add($"{hit.collider.name}(dist:{hit.distance:F4})");

					// Track which object we hit
					if (firstHitObject == null)
					{
						firstHitObject = hit.collider.gameObject;
					}
					else if (hit.collider.gameObject != firstHitObject)
					{
						continue;
					}
				}

				rayHitPoints[i] = Vector3.zero;
			}

			// Only set detected object if all rays hit the SAME object
			if (raysHitting == 4 && firstHitObject != null)
			{
				detectedObject = firstHitObject;

				// Log when we first detect a new object
				if (detectedObject != previousDetectedObject && logStateChanges)
				{
					Debug.Log($"[Magnet-{gameObject.name}] ✓ NEW DETECTION: {detectedObject.name} | All 4 rays hit | Hits: {string.Join(", ", hitObjects)}");
				}
			}
			else
			{
				detectedObject = null;

				// Log when we lose detection
				if (previousDetectedObject != null && logStateChanges)
				{
					Debug.Log($"[Magnet-{gameObject.name}] ✗ LOST DETECTION: {previousDetectedObject.name} | Rays={raysHitting}/4");
				}
			}

			lastDetectedObject = detectedObject;
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

			if (rb == null)
			{
				Debug.LogError($"[Magnet-{gameObject.name}] GRAB FAILED: {obj.name} has no Rigidbody!");
				return;
			}

			// Grab the object
			heldObject = obj;
			heldRigidbody = rb;

			// Store original state
			originalParent = obj.transform.parent;

			// Parent to magnet and make kinematic
			obj.transform.SetParent(transform, true);
			rb.isKinematic = true;
			rb.useGravity = false;
			rb.interpolation = RigidbodyInterpolation.None;

			// Add collision monitor to auto-release on collision
			collisionMonitor = obj.AddComponent<CollisionReleaseMonitor>();
			collisionMonitor.gripper = this;
			collisionMonitor.collidersToAvoid = robotColliders;
			collisionMonitor.logEvents = logCollisionMonitor;

			UpdateVisuals();

			if (logStateChanges)
			{
				Debug.Log($"[Magnet-{gameObject.name}] ✓ GRABBED: {obj.name} | Parented to magnet, made kinematic");
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
				if (logStateChanges)
				{
					Debug.LogWarning($"[Magnet-{gameObject.name}] Held object destroyed! Auto-releasing.");
				}
				Release();
			}
		}

		/// <summary>
		/// Turn magnet on/off
		/// </summary>
		public void SetActive(bool active)
		{
			// Trying to turn ON during cooldown → ignore
			if (active && !CanActivateMagnet)
			{
				if (logCooldown)
				{
					float timeRemaining = nextAllowedPickupTime - Time.time;
					Debug.LogWarning($"[Magnet-{gameObject.name}] Cannot activate - in cooldown! Time remaining: {timeRemaining:F2}s");
				}
				isActive = false;
				UpdateVisuals();
				return;
			}

			bool wasActive = isActive;
			isActive = active;

			// Log state change
			if (wasActive != isActive && logStateChanges)
			{
				Debug.Log($"[Magnet-{gameObject.name}] Magnet {(isActive ? "ACTIVATED" : "DEACTIVATED")} | Holding={IsHoldingObject}");
			}

			// Turning off always releases
			if (!isActive && IsHoldingObject)
			{
				Release();
			}

			UpdateVisuals();
		}

		/// <summary>
		/// Toggle magnet state
		/// </summary>
		[ContextMenu("Toggle Magnet")]
		public void Toggle()
		{
			SetActive(!isActive);
		}

		/// <summary>
		/// Release any held object
		/// </summary>
		public void Release()
		{
			if (logStateChanges)
			{
				string objName = heldObject != null ? heldObject.name : "NULL";
				Debug.Log($"[Magnet-{gameObject.name}] RELEASING: {objName} | Starting {pickupCooldown}s cooldown");
			}

			// Force magnet OFF
			isActive = false;

			// Start cooldown
			nextAllowedPickupTime = Time.time + pickupCooldown;

			if (logCooldown)
			{
				Debug.Log($"[Magnet-{gameObject.name}] Cooldown started | Can activate again at t={nextAllowedPickupTime:F2} (current t={Time.time:F2})");
			}

			// Stop magnetic pull if active
			StopMagneticPull();

			// Remove collision monitor
			if (collisionMonitor != null)
			{
				if (logCollisionMonitor)
				{
					Debug.Log($"[Magnet-{gameObject.name}] Destroying collision monitor on {heldObject?.name}");
				}
				Destroy(collisionMonitor);
				collisionMonitor = null;
			}

			if (heldObject != null)
			{
				heldObject.transform.SetParent(originalParent, true);
			}

			if (heldRigidbody != null)
			{
				heldRigidbody.isKinematic = false;
				heldRigidbody.useGravity = true;
				heldRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
				heldRigidbody = null;
			}

			heldObject = null;
			originalParent = null;

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
			if (magneticPullRoutine != null) return; // Ignore collisions during pull
			if (!CanActivateMagnet) return; // Ignore during cooldown


			if (logStateChanges)
			{
				Debug.Log($"[Magnet-{gameObject.name}] OnCollisionEnter with {collision.gameObject.name}");
			}

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
			if (magneticPullRoutine != null) return;

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
			if (logStateChanges)
			{
				Debug.Log($"[Magnet-{gameObject.name}] OnCollisionExit with {collision.gameObject.name}");
			}
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

	/// <summary>
	/// Helper component that monitors collisions on held objects and triggers auto-release.
	/// Automatically added to objects when grabbed, removed when released.
	/// </summary>
	internal class CollisionReleaseMonitor: MonoBehaviour
	{
		public SimpleRaycastGripper gripper;
		public List<Collider> collidersToAvoid;
		public bool logEvents = true;

		private float pickupTime;
		private float pickupHeight;

		[Tooltip("Grace period after pickup")]
		public float pickupGracePeriod = 0.2f;

		[Tooltip("Height object must be lifted before collision detection")]
		public float minLiftHeight = 0.03f;

		[Header("Monitoring (Read-Only)")]
		[Tooltip("Log collision detection status every N frames (0 = disabled)")]
		public int monitorLogInterval = 10;

		private int monitorFrameCount = 0;

		// Public properties for monitoring
		public bool GracePeriodPassed => Time.time - pickupTime >= pickupGracePeriod;
		public bool HasBeenLifted => transform.position.y >= pickupHeight + minLiftHeight;
		public bool IsCollisionDetectionActive => GracePeriodPassed && HasBeenLifted;

		// Additional monitoring info
		public float TimeSincePickup => Time.time - pickupTime;
		public float CurrentHeight => transform.position.y;
		public float HeightAbovePickup => transform.position.y - pickupHeight;

		private void Awake()
		{
			pickupTime = Time.time;
			pickupHeight = transform.position.y;

			if (logEvents)
			{
				Debug.Log($"[CollisionMonitor-{gameObject.name}] Created | PickupTime={pickupTime:F2}, PickupHeight={pickupHeight:F3}, GracePeriod={pickupGracePeriod}s, MinLift={minLiftHeight:F3}");
			}
		}

		private void Update()
		{
			monitorFrameCount++;

			// Log monitoring status periodically
			if (logEvents && monitorLogInterval > 0 && monitorFrameCount % monitorLogInterval == 0)
			{
				Debug.Log($"[CollisionMonitor-{gameObject.name}] Status | " +
					$"IsActive={IsCollisionDetectionActive}, " +
					$"GracePassed={GracePeriodPassed} ({TimeSincePickup:F2}s/{pickupGracePeriod:F2}s), " +
					$"Lifted={HasBeenLifted} ({HeightAbovePickup:F4}m/{minLiftHeight:F3}m), " +
					$"CurHeight={CurrentHeight:F3}");
			}

			if (!IsCollisionDetectionActive)
			{
				// Log why we're not active (only occasionally to avoid spam)
				if (logEvents && monitorLogInterval > 0 && monitorFrameCount % (monitorLogInterval * 3) == 0)
				{
					string reason = "";
					if (!GracePeriodPassed) reason += $"Waiting for grace period ({pickupGracePeriod - TimeSincePickup:F2}s remaining)";
					if (!HasBeenLifted)
					{
						if (reason.Length > 0) reason += ", ";
						reason += $"Not lifted enough ({minLiftHeight - HeightAbovePickup:F4}m more needed)";
					}
					Debug.Log($"[CollisionMonitor-{gameObject.name}] Collision detection INACTIVE: {reason}");
				}
				return;
			}

			pickupHeight = -10; // Disable further height checks after activation

			Collider objectCollider = GetComponent<Collider>();
			if (collidersToAvoid != null && objectCollider != null)
			{
				foreach (var col in collidersToAvoid)
				{
					if (col == objectCollider)
						continue;

					if (logEvents && monitorLogInterval > 0 && monitorFrameCount % (monitorLogInterval * 3) == 0)
					{ 
						Debug.Log($"[CollisionMonitor-{gameObject.name}] Checking penetration against {col.name}");
					}

						bool intersecting = Physics.ComputePenetration(
						objectCollider, objectCollider.transform.position, objectCollider.transform.rotation,
						col, col.transform.position, col.transform.rotation,
						out Vector3 direction, out float distance
					);

					if (intersecting)
					{
						Debug.Log($"[CollisionMonitor-{gameObject.name} | Intersecting");

						if (logEvents)
						{
							Debug.LogWarning($"[CollisionMonitor-{gameObject.name}] ⚠ PENETRATION with {col.name} | Distance={distance:F4}, Direction={direction} - RELEASING!");
						}

						if (gripper != null)
						{
							gripper.Release();
						}
						else 
						{ 
							Debug.LogError($"[CollisionMonitor-{gameObject.name}] No gripper reference to release!");
						}
						break;
					}
				}
			}
		}

		private void OnDestroy()
		{
			if (logEvents)
			{
				Debug.Log($"[CollisionMonitor-{gameObject.name}] Destroyed | FinalHeight={CurrentHeight:F3}, TotalLift={HeightAbovePickup:F3}, Duration={TimeSincePickup:F2}s");
			}
		}

		// Optional: Display in Scene view
#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (!Application.isPlaying) return;

			// Draw pickup height line
			Gizmos.color = Color.yellow;
			Vector3 pos = transform.position;
			Vector3 pickupPos = new Vector3(pos.x, pickupHeight, pos.z);
			Gizmos.DrawLine(pickupPos - Vector3.right * 0.1f, pickupPos + Vector3.right * 0.1f);
			Gizmos.DrawLine(pickupPos - Vector3.forward * 0.1f, pickupPos + Vector3.forward * 0.1f);

			// Draw minimum lift height line
			Gizmos.color = HasBeenLifted ? Color.green : Color.red;
			Vector3 minLiftPos = new Vector3(pos.x, pickupHeight + minLiftHeight, pos.z);
			Gizmos.DrawLine(minLiftPos - Vector3.right * 0.15f, minLiftPos + Vector3.right * 0.15f);
			Gizmos.DrawLine(minLiftPos - Vector3.forward * 0.15f, minLiftPos + Vector3.forward * 0.15f);

			// Draw current position
			Gizmos.color = IsCollisionDetectionActive ? Color.green : Color.yellow;
			Gizmos.DrawWireSphere(pos, 0.05f);

			// Draw label
			UnityEditor.Handles.Label(pos + Vector3.up * 0.15f,
				$"Monitor: {(IsCollisionDetectionActive ? "ACTIVE" : "INACTIVE")}\n" +
				$"Grace: {(GracePeriodPassed ? "✓" : "✗")} ({TimeSincePickup:F2}s)\n" +
				$"Lifted: {(HasBeenLifted ? "✓" : "✗")} ({HeightAbovePickup:F4}m)");
		}
#endif
	}
}