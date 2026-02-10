using UnityEngine;

namespace MLRobot.Systems
{
	/// <summary>
	/// Smoothly follows a target transform with configurable distance, rotation, and smoothing.
	/// If no target is assigned, the camera stays at its default position without errors.
	/// </summary>
	public class CameraFollow : MonoBehaviour
	{
		[Header("Target")]
		[Tooltip("Transform to follow. If null, camera stays at its default position")]
		[SerializeField] private Transform target;

		[Header("Orbit")]
		[Tooltip("Distance from the target")]
		[SerializeField] private float distance = 3.5f;

		[Tooltip("Vertical offset above the target pivot")]
		[SerializeField] private float heightOffset = 1.3f;

		[Tooltip("Orbital rotation around the target (Euler angles)")]
		[SerializeField] private Vector3 rotationAngles = new Vector3(9f, 78f, 0f);

		[Header("Smoothing")]
		[Tooltip("SmoothDamp speed. 0 = instant repositioning")]
		[Range(0f, 20f)]
		[SerializeField] private float smoothSpeed = 8f;

		[Tooltip("Camera always faces the target point")]
		[SerializeField] private bool lookAtTarget = true;

		[Header("Debug")]
		[Tooltip("Draw gizmo lines in Scene view when selected")]
		[SerializeField] private bool showDebugGizmos = true;

		private Vector3 defaultPosition;
		private Quaternion defaultRotation;
		private Vector3 smoothVelocity;

		private void Awake()
		{
			defaultPosition = transform.position;
			defaultRotation = transform.rotation;
		}

		private void Start()
		{
			if (target == null)
			{
				Debug.LogWarning($"[CameraFollow] No target assigned on '{gameObject.name}'. Camera will stay at default position.", this);
			}
		}

		private void LateUpdate()
		{
			if (target == null) return;

			Vector3 lookPoint = target.position + Vector3.up * heightOffset;
			Vector3 desiredPosition = lookPoint + Quaternion.Euler(rotationAngles) * (Vector3.back * distance);

			if (smoothSpeed <= 0f)
			{
				transform.position = desiredPosition;
			}
			else
			{
				transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref smoothVelocity, 1f / smoothSpeed);
			}

			if (lookAtTarget)
			{
				transform.LookAt(lookPoint);
			}
		}

		/// <summary>
		/// Switch the follow target at runtime. Resets smoothing velocity.
		/// </summary>
		public void SetTarget(Transform newTarget)
		{
			target = newTarget;
			smoothVelocity = Vector3.zero;

			if (target == null)
			{
				transform.position = defaultPosition;
				transform.rotation = defaultRotation;
			}
		}

		/// <summary>
		/// Instantly reposition to the current target, skipping smoothing.
		/// </summary>
		public void SnapToTarget()
		{
			if (target == null) return;

			smoothVelocity = Vector3.zero;
			Vector3 lookPoint = target.position + Vector3.up * heightOffset;
			transform.position = lookPoint + Quaternion.Euler(rotationAngles) * (Vector3.back * distance);

			if (lookAtTarget)
			{
				transform.LookAt(lookPoint);
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			if (!showDebugGizmos || target == null) return;

			Vector3 lookPoint = target.position + Vector3.up * heightOffset;
			Vector3 orbitPosition = lookPoint + Quaternion.Euler(rotationAngles) * (Vector3.back * distance);

			// Look-at point
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(lookPoint, 0.08f);

			// Line from camera to look-at point
			Gizmos.color = Color.yellow;
			Gizmos.DrawLine(transform.position, lookPoint);

			// Desired orbit position
			Gizmos.color = Color.green;
			Gizmos.DrawWireSphere(orbitPosition, 0.06f);
		}
#endif
	}
}
