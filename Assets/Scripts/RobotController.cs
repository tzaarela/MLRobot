using UnityEngine;
using System;

namespace RobotArm
{
	/// <summary>
	/// Controls a 6-axis robot arm. Can be driven by keyboard input, AI agent, or any other input source.
	/// Attach this to the root Robot GameObject.
	/// </summary>
	public class RobotController: MonoBehaviour
	{
		[Header("Robot Joints (J1-J6)")]
		[Tooltip("Configure all 6 joints. Order: J1 (base) to J6 (wrist)")]
		public JointConfig[] joints = new JointConfig[6];

		[Header("End Effector")]
		[Tooltip("The magnet/gripper at the end of the arm")]
		public SimpleRaycastGripper magnet;

		[Tooltip("Transform representing the tool center point (TCP)")]
		public Transform toolCenterPoint;

		[Header("Settings")]
		[Tooltip("If true, joints smoothly interpolate to target angles")]
		public bool useSmoothMovement = true;

		[Tooltip("Smoothing factor for joint movement (higher = faster)")]
		[Range(1f, 20f)]
		public float smoothingSpeed = 10f;

		// Target angles for smooth movement
		private float[] targetAngles = new float[6];

		// Events for state changes
		public event Action<int, float> OnJointAngleChanged;
		public event Action<bool> OnMagnetStateChanged;

		/// <summary>
		/// Get the current angle of a joint (0-5)
		/// </summary>
		public float GetJointAngle(int jointIndex)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return 0f;
			return joints[jointIndex].currentAngle;
		}

		/// <summary>
		/// Get normalized joint angle (0-1) for ML observations
		/// </summary>
		public float GetNormalizedJointAngle(int jointIndex)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return 0.5f;
			return joints[jointIndex].NormalizedAngle;
		}

		/// <summary>
		/// Get all normalized joint angles as an array
		/// </summary>
		public float[] GetAllNormalizedAngles()
		{
			float[] angles = new float[joints.Length];
			for (int i = 0; i < joints.Length; i++)
			{
				angles[i] = joints[i].NormalizedAngle;
			}
			return angles;
		}

		/// <summary>
		/// Set joint angle directly (in degrees)
		/// </summary>
		public void SetJointAngle(int jointIndex, float angle)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return;

			var joint = joints[jointIndex];
			float clampedAngle = Mathf.Clamp(angle, joint.minAngle, joint.maxAngle);

			if (useSmoothMovement)
			{
				targetAngles[jointIndex] = clampedAngle;
			}
			else
			{
				joint.currentAngle = clampedAngle;
				ApplyJointRotation(jointIndex);
				OnJointAngleChanged?.Invoke(jointIndex, clampedAngle);
			}
		}

		/// <summary>
		/// Set joint angle from normalized value (0-1)
		/// </summary>
		public void SetNormalizedJointAngle(int jointIndex, float normalizedAngle)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return;

			var joint = joints[jointIndex];
			float angle = Mathf.Lerp(joint.minAngle, joint.maxAngle, Mathf.Clamp01(normalizedAngle));
			SetJointAngle(jointIndex, angle);
		}

		/// <summary>
		/// Rotate a joint by a delta amount (in degrees)
		/// </summary>
		public void RotateJoint(int jointIndex, float deltaDegrees)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return;

			var joint = joints[jointIndex];
			float newAngle = joint.currentAngle + deltaDegrees;
			SetJointAngle(jointIndex, newAngle);
		}

		/// <summary>
		/// Rotate joint using normalized input (-1 to 1) scaled by speed and deltaTime
		/// </summary>
		public void RotateJointNormalized(int jointIndex, float normalizedInput, float deltaTime)
		{
			if (jointIndex < 0 || jointIndex >= joints.Length) return;

			var joint = joints[jointIndex];
			float deltaDegrees = normalizedInput * joint.rotationSpeed * deltaTime;
			RotateJoint(jointIndex, deltaDegrees);
		}

		/// <summary>
		/// Set all joints from normalized array (for ML agent actions)
		/// </summary>
		public void SetAllJointsNormalized(float[] normalizedAngles)
		{
			int count = Mathf.Min(normalizedAngles.Length, joints.Length);
			for (int i = 0; i < count; i++)
			{
				SetNormalizedJointAngle(i, normalizedAngles[i]);
			}
		}

		/// <summary>
		/// Apply rotation deltas to all joints (for ML agent continuous actions)
		/// Immediately applies rotations (bypasses smooth movement for ML training)
		/// </summary>
		public void ApplyJointDeltas(float[] deltas, float deltaTime)
		{
			int count = Mathf.Min(deltas.Length, joints.Length);
			for (int i = 0; i < count; i++)
			{
				var joint = joints[i];
				if (joint.jointTransform == null) continue;

				float deltaDegrees = deltas[i] * joint.rotationSpeed * deltaTime;
				float newAngle = Mathf.Clamp(joint.currentAngle + deltaDegrees, joint.minAngle, joint.maxAngle);

				joint.currentAngle = newAngle;
				targetAngles[i] = newAngle; // Keep in sync
				ApplyJointRotation(i);
			}
		}

		/// <summary>
		/// Turn magnet on/off
		/// </summary>
		public void SetMagnetActive(bool active)
		{
			if (magnet != null)
			{
				magnet.SetActive(active);
				OnMagnetStateChanged?.Invoke(active);
			}
		}

		/// <summary>
		/// Toggle magnet state
		/// </summary>
		public void ToggleMagnet()
		{
			if (magnet != null)
			{
				magnet.Toggle();
				OnMagnetStateChanged?.Invoke(magnet.IsActive);
			}
		}

		/// <summary>
		/// Check if magnet is currently holding an object
		/// </summary>
		public bool IsHoldingObject()
		{
			return magnet != null && magnet.IsHoldingObject;
		}

		/// <summary>
		/// Get the world position of the tool center point
		/// </summary>
		public Vector3 GetToolPosition()
		{
			if (toolCenterPoint != null)
				return toolCenterPoint.position;
			if (magnet != null)
				return magnet.transform.position;
			return transform.position;
		}

		/// <summary>
		/// Get the world rotation of the tool
		/// </summary>
		public Quaternion GetToolRotation()
		{
			if (toolCenterPoint != null)
				return toolCenterPoint.rotation;
			if (magnet != null)
				return magnet.transform.rotation;
			return transform.rotation;
		}

		/// <summary>
		/// Get the "up" direction of the magnet face
		/// </summary>
		public Vector3 GetMagnetFaceNormal()
		{
			if (magnet != null)
				return magnet.transform.up;
			if (toolCenterPoint != null)
				return toolCenterPoint.up;
			return Vector3.up;
		}

		/// <summary>
		/// Reset all joints to their starting positions
		/// </summary>
		public void ResetToStartPosition()
		{
			for (int i = 0; i < joints.Length; i++)
			{
				var joint = joints[i];
				joint.currentAngle = joint.startAngle;
				targetAngles[i] = joint.startAngle;
				ApplyJointRotation(i);
			}

			if (magnet != null)
			{
				magnet.Release();
				magnet.SetActive(false);
			}
		}

		private void Awake()
		{
			InitializeJoints();
		}

		private void InitializeJoints()
		{
			for (int i = 0; i < joints.Length; i++)
			{
				var joint = joints[i];
				if (joint.jointTransform != null)
				{
					joint.initialLocalRotation = joint.jointTransform.localRotation;
					joint.currentAngle = joint.startAngle;
					targetAngles[i] = joint.startAngle;
					ApplyJointRotation(i);
				}
			}
		}

		private void Update()
		{
			if (useSmoothMovement)
			{
				UpdateSmoothMovement();
			}
		}

		private void UpdateSmoothMovement()
		{
			for (int i = 0; i < joints.Length; i++)
			{
				var joint = joints[i];
				if (joint.jointTransform == null) continue;

				if (!Mathf.Approximately(joint.currentAngle, targetAngles[i]))
				{
					joint.currentAngle = Mathf.Lerp(joint.currentAngle, targetAngles[i], smoothingSpeed * Time.deltaTime);

					// Snap if very close
					if (Mathf.Abs(joint.currentAngle - targetAngles[i]) < 0.01f)
					{
						joint.currentAngle = targetAngles[i];
					}

					ApplyJointRotation(i);
					OnJointAngleChanged?.Invoke(i, joint.currentAngle);
				}
			}
		}

		private void ApplyJointRotation(int jointIndex)
		{
			var joint = joints[jointIndex];
			if (joint.jointTransform == null) return;

			Quaternion rotation = Quaternion.AngleAxis(joint.currentAngle, joint.rotationAxis);
			joint.jointTransform.localRotation = joint.initialLocalRotation * rotation;
		}

		private void OnValidate()
		{
			// Ensure we always have exactly 6 joints
			if (joints == null || joints.Length != 6)
			{
				var oldJoints = joints;
				joints = new JointConfig[6];

				if (oldJoints != null)
				{
					for (int i = 0; i < Mathf.Min(oldJoints.Length, 6); i++)
					{
						joints[i] = oldJoints[i];
					}
				}

				for (int i = 0; i < 6; i++)
				{
					if (joints[i] == null)
					{
						joints[i] = new JointConfig();
					}
				}
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			// Draw joint axes
			foreach (var joint in joints)
			{
				if (joint?.jointTransform == null) continue;

				Vector3 worldAxis = joint.jointTransform.TransformDirection(joint.rotationAxis);
				Gizmos.color = Color.yellow;
				Gizmos.DrawRay(joint.jointTransform.position, worldAxis * 0.2f);
			}

			// Draw tool position
			Vector3 toolPos = GetToolPosition();
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(toolPos, 0.05f);

			// Draw magnet face normal
			Gizmos.color = Color.green;
			Gizmos.DrawRay(toolPos, GetMagnetFaceNormal() * 0.15f);
		}
#endif
	}
}