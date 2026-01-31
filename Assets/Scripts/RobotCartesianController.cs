using UnityEngine;

namespace RobotArm
{
	/// <summary>
	/// Provides Cartesian (XYZ) linear movement control for a 6-axis robot arm.
	/// Uses numerical inverse kinematics (Jacobian pseudo-inverse) to convert 
	/// Cartesian velocity commands into joint velocities.
	/// 
	/// Industry standard approach: "Cartesian Jogging" or "Cartesian Jog Mode"
	/// </summary>
	public class RobotCartesianController: MonoBehaviour
	{
		[Header("References")]
		[Tooltip("The robot controller to drive")]
		public RobotController robotController;

		[Header("Cartesian Movement Settings")]
		[Tooltip("Linear movement speed in meters per second")]
		[Range(0.01f, 1f)]
		public float linearSpeed = 0.1f;

		[Tooltip("Angular rotation speed in radians per second")]
		[Range(0.1f, 5f)]
		public float angularSpeed = 1.5f;

		[Tooltip("Maximum step size per iteration (prevents large jumps)")]
		[Range(0.001f, 0.1f)]
		public float maxStepSize = 0.01f;

		[Tooltip("Damping factor for pseudo-inverse (prevents singularities)")]
		[Range(0f, 1f)]
		public float dampingFactor = 0.5f;

		[Header("Iteration Settings")]
		[Tooltip("Maximum iterations for IK convergence")]
		public int maxIterations = 10;

		[Tooltip("Convergence threshold (meters)")]
		public float convergenceThreshold = 0.001f;

		[Header("Safety Limits")]
		[Tooltip("Maximum reach distance from J1 base to J6 flange (outer sphere)")]
		public float maxReachDistance = 2f;

		[Tooltip("Minimum reach distance from J1 base to J6 flange (inner sphere - prevents collision with base)")]
		public float minReachDistance = 0.3f;

		[Tooltip("Minimum allowed height (Y position) for the tool - prevents going through the ground")]
		public float groundPlaneHeight = 0f;

		[Tooltip("Safety buffer to avoid singularities at max reach (0.9 = use 90% of max reach)")]
		[Range(0.8f, 1f)]
		public float maxReachSafetyFactor = 0.95f;

		[Tooltip("Enable workspace limits (max/min reach + ground plane)")]
		public bool enforceWorkspaceLimits = true;

		[Header("Debug")]
		public bool showDebugInfo = false;
		public bool showDebugGizmos = true;

		// Numerical derivative step size for Jacobian calculation (in degrees)
		// Increased for better numerical stability with rotations
		private const float EPSILON = 0.01f;

		// Current target in Cartesian space
		private Vector3 targetPosition;
		private Quaternion targetRotation;
		private bool isInitialized = false;

		// Previous valid target (for rollback on IK failure)
		private Vector3 previousValidPosition;
		private Quaternion previousValidRotation;

		private void Start()
		{
			Initialize();
		}

		/// <summary>
		/// Get the J1 base joint position (the rotation axis of the base).
		/// This is the reference point for calculating maximum reach.
		/// </summary>
		private Vector3 GetJ1BasePosition()
		{
			if (robotController == null || robotController.joints == null || robotController.joints.Length == 0)
			{
				Debug.LogWarning("[CartesianController] Cannot get J1 position - robot controller not properly configured");
				return transform.position;
			}

			// J1 is the first joint (index 0) - the base rotation
			var j1Joint = robotController.joints[0];
			if (j1Joint.jointTransform != null)
			{
				return j1Joint.jointTransform.position;
			}

			// Fallback to robot controller's root transform
			Debug.LogWarning("[CartesianController] J1 joint transform is null, using robot root position as fallback");
			return robotController.transform.position;
		}

		private void Initialize()
		{
			if (robotController == null)
			{
				Debug.LogError("[CartesianController] No RobotController assigned!");
				return;
			}

			targetPosition = robotController.GetToolPosition();
			targetRotation = robotController.GetToolRotation();
			previousValidPosition = targetPosition;
			previousValidRotation = targetRotation;
			isInitialized = true;

			if (showDebugInfo)
			{
				Vector3 j1Pos = GetJ1BasePosition();
				float currentReach = Vector3.Distance(j1Pos, targetPosition);
				Debug.Log($"[CartesianController] Initialized at position: {targetPosition}, rotation: {targetRotation.eulerAngles}, current reach: {currentReach:F3}m");
			}
		}

		/// <summary>
		/// Move the tool in Cartesian space by the specified delta.
		/// Called from keyboard input or other control sources.
		/// </summary>
		/// <param name="deltaX">Movement in X direction (meters)</param>
		/// <param name="deltaY">Movement in Y direction (meters)</param>
		/// <param name="deltaZ">Movement in Z direction (meters)</param>
		/// <param name="deltaTime">Time step for movement</param>
		public void MoveCartesian(float deltaX, float deltaY, float deltaZ, float deltaTime)
		{
			if (!isInitialized)
			{
				Initialize();
				return;
			}

			// Update target rotation to current rotation before moving
			// This prevents orientation drift during position-only movements
			targetRotation = robotController.GetToolRotation();

			// Calculate desired movement scaled by speed and time
			Vector3 desiredDelta = new Vector3(deltaX, deltaY, deltaZ) * linearSpeed * deltaTime;

			if (showDebugInfo && desiredDelta.magnitude > 0.0001f)
			{
				Debug.Log($"[CartesianController] MoveCartesian called: delta=({deltaX:F3}, {deltaY:F3}, {deltaZ:F3}), " +
						 $"scaled delta={desiredDelta}, speed={linearSpeed}, dt={deltaTime:F4}");
			}

			// Clamp to max step size
			if (desiredDelta.magnitude > maxStepSize)
			{
				desiredDelta = desiredDelta.normalized * maxStepSize;
				if (showDebugInfo)
				{
					Debug.Log($"[CartesianController] Clamped delta to max step size: {desiredDelta}");
				}
			}

			// Update target position
			Vector3 newTarget = targetPosition + desiredDelta;

			// Check workspace limits (donut-shaped envelope: max/min reach + ground plane)
			if (enforceWorkspaceLimits)
			{
				Vector3 j1BasePosition = GetJ1BasePosition();
				float reachDistance = Vector3.Distance(j1BasePosition, newTarget);

				// Apply safety factor to max reach to avoid singularities
				float safeMaxReach = maxReachDistance * maxReachSafetyFactor;

				// Check maximum reach (outer sphere with safety buffer)
				if (reachDistance > safeMaxReach)
				{
					if (showDebugInfo)
					{
						Debug.LogWarning($"[CartesianController] Target exceeds safe max reach: {reachDistance:F3}m > {safeMaxReach:F3}m (safety factor: {maxReachSafetyFactor:F2})");
					}
					return;
				}

				// Check minimum reach (inner sphere - prevents collision with base)
				if (reachDistance < minReachDistance)
				{
					if (showDebugInfo)
					{
						Debug.LogWarning($"[CartesianController] Target too close to base: {reachDistance:F3}m < {minReachDistance}m");
					}
					return;
				}

				// Check ground plane limit
				if (newTarget.y < groundPlaneHeight)
				{
					if (showDebugInfo)
					{
						Debug.LogWarning($"[CartesianController] Target below ground plane: {newTarget.y:F3}m < {groundPlaneHeight}m");
					}
					return;
				}
			}

			// Store previous valid target for potential rollback
			Vector3 previousTarget = targetPosition;
			Quaternion previousRotation = targetRotation;

			targetPosition = newTarget;

			if (showDebugInfo)
			{
				Debug.Log($"[CartesianController] New target position: {targetPosition}, rotation: {targetRotation.eulerAngles}");
			}

			// Solve IK to reach target (with updated current rotation as target)
			bool ikSuccess = SolveIK(targetPosition, targetRotation);

			// Rollback if IK failed
			if (!ikSuccess)
			{
				targetPosition = previousTarget;
				targetRotation = previousRotation;

				if (showDebugInfo)
				{
					Debug.LogWarning("[CartesianController] IK failed - rolling back to previous valid target");
				}
			}
			else
			{
				// Update previous valid targets
				previousValidPosition = targetPosition;
				previousValidRotation = targetRotation;
			}
		}

		/// <summary>
		/// Rotate the tool by angular deltas (in radians)
		/// </summary>
		/// <param name="deltaRoll">Rotation around X axis (radians)</param>
		/// <param name="deltaPitch">Rotation around Y axis (radians)</param>
		/// <param name="deltaYaw">Rotation around Z axis (radians)</param>
		/// <param name="deltaTime">Time step</param>
		public void RotateCartesian(float deltaRoll, float deltaPitch, float deltaYaw, float deltaTime)
		{
			if (!isInitialized)
			{
				Initialize();
				return;
			}

			// Update target position to current position before rotating
			// This prevents position drift during orientation-only movements
			targetPosition = robotController.GetToolPosition();

			// Scale by angular speed (radians per second)
			Vector3 angularDelta = new Vector3(deltaRoll, deltaPitch, deltaYaw) * angularSpeed * deltaTime;

			if (showDebugInfo && angularDelta.magnitude > 0.0001f)
			{
				Debug.Log($"[CartesianController] RotateCartesian called: delta=({deltaRoll:F3}, {deltaPitch:F3}, {deltaYaw:F3}) rad");
			}

			// Store previous valid target for potential rollback
			Vector3 previousTarget = targetPosition;
			Quaternion previousRotation = targetRotation;

			// Apply rotation as Euler angles (world space)
			Quaternion deltaRotation = Quaternion.Euler(
				angularDelta.x * Mathf.Rad2Deg,
				angularDelta.y * Mathf.Rad2Deg,
				angularDelta.z * Mathf.Rad2Deg
			);

			targetRotation = deltaRotation * targetRotation;

			if (showDebugInfo)
			{
				Debug.Log($"[CartesianController] New target rotation: {targetRotation.eulerAngles}, position: {targetPosition}");
			}

			// Solve IK to reach target rotation (with updated current position as target)
			bool ikSuccess = SolveIK(targetPosition, targetRotation);

			// Rollback if IK failed
			if (!ikSuccess)
			{
				targetPosition = previousTarget;
				targetRotation = previousRotation;

				if (showDebugInfo)
				{
					Debug.LogWarning("[CartesianController] IK failed on rotation - rolling back to previous valid target");
				}
			}
			else
			{
				// Update previous valid targets
				previousValidPosition = targetPosition;
				previousValidRotation = targetRotation;
			}
		}

		/// <summary>
		/// Move in world space directions
		/// </summary>
		public void MoveWorld(Vector3 direction, float deltaTime)
		{
			MoveCartesian(direction.x, direction.y, direction.z, deltaTime);
		}

		/// <summary>
		/// Move in local tool space (relative to tool orientation)
		/// </summary>
		public void MoveTool(Vector3 localDirection, float deltaTime)
		{
			Quaternion toolRotation = robotController.GetToolRotation();
			Vector3 worldDirection = toolRotation * localDirection;
			MoveCartesian(worldDirection.x, worldDirection.y, worldDirection.z, deltaTime);
		}

		/// <summary>
		/// Set absolute target position in Cartesian space (maintains current rotation)
		/// </summary>
		public void SetTargetPosition(Vector3 position)
		{
			Vector3 previousTarget = targetPosition;
			targetPosition = position;

			bool ikSuccess = SolveIK(targetPosition, targetRotation);

			if (!ikSuccess)
			{
				targetPosition = previousTarget;
			}
			else
			{
				previousValidPosition = targetPosition;
				previousValidRotation = targetRotation;
			}
		}

		/// <summary>
		/// Set absolute target position and rotation in Cartesian space
		/// </summary>
		public void SetTargetPose(Vector3 position, Quaternion rotation)
		{
			Vector3 previousTarget = targetPosition;
			Quaternion previousRotation = targetRotation;

			targetPosition = position;
			targetRotation = rotation;

			bool ikSuccess = SolveIK(targetPosition, targetRotation);

			if (!ikSuccess)
			{
				targetPosition = previousTarget;
				targetRotation = previousRotation;
			}
			else
			{
				previousValidPosition = targetPosition;
				previousValidRotation = targetRotation;
			}
		}

		/// <summary>
		/// Solve inverse kinematics using Jacobian pseudo-inverse method.
		/// This is an iterative numerical approach commonly used in industry.
		/// Returns true if IK converged successfully, false otherwise.
		/// </summary>
		private bool SolveIK(Vector3 targetPos, Quaternion targetRot)
		{
			// Store original smooth movement setting
			bool originalSmoothSetting = robotController.useSmoothMovement;

			// Disable smooth movement for direct IK calculation
			robotController.useSmoothMovement = false;

			// Store original joint angles for rollback if IK fails
			float[] originalAngles = new float[6];
			for (int i = 0; i < 6; i++)
			{
				originalAngles[i] = robotController.GetJointAngle(i);
			}

			// Track final error
			float finalPosError = 0f;
			float finalRotError = 0f;

			for (int iteration = 0; iteration < maxIterations; iteration++)
			{
				Vector3 currentPos = robotController.GetToolPosition();
				Quaternion currentRot = robotController.GetToolRotation();

				// Position error
				Vector3 posError = targetPos - currentPos;

				// Orientation error (axis-angle representation)
				Quaternion rotError = targetRot * Quaternion.Inverse(currentRot);
				Vector3 angularError = QuaternionToAxisAngle(rotError);

				// Combined 6D error vector
				float[] error = new float[6];
				error[0] = posError.x;
				error[1] = posError.y;
				error[2] = posError.z;
				error[3] = angularError.x;
				error[4] = angularError.y;
				error[5] = angularError.z;

				// Calculate total error magnitude
				float posErrorMag = posError.magnitude;
				float rotErrorMag = angularError.magnitude;

				// Store final errors
				finalPosError = posErrorMag;
				finalRotError = rotErrorMag;

				// Check convergence
				if (posErrorMag < convergenceThreshold && rotErrorMag < 0.01f) // 0.01 rad ≈ 0.57°
				{
					if (showDebugInfo)
					{
						Debug.Log($"[CartesianController] Converged in {iteration} iterations. " +
								 $"Pos error: {posErrorMag:F6}m, Rot error: {rotErrorMag * Mathf.Rad2Deg:F3}°");
					}
					break;
				}

				// Calculate Jacobian matrix (6x6 for position + orientation)
				float[,] jacobian = CalculateJacobian();

				// Calculate pseudo-inverse with damping
				float[,] jacobianPseudoInverse = CalculateDampedPseudoInverse(jacobian);

				// Calculate joint velocity deltas
				float[] jointDeltas = new float[6];
				for (int i = 0; i < 6; i++)
				{
					jointDeltas[i] = 0f;
					for (int j = 0; j < 6; j++)
					{
						jointDeltas[i] += jacobianPseudoInverse[i, j] * error[j];
					}
				}

				// Clamp joint deltas to prevent large jumps (max 5 degrees per iteration)
				float maxJointDelta = 5f * Mathf.Deg2Rad; // 5 degrees in radians
				for (int i = 0; i < 6; i++)
				{
					jointDeltas[i] = Mathf.Clamp(jointDeltas[i], -maxJointDelta, maxJointDelta);
				}

				// Apply joint movements (convert to degrees)
				for (int i = 0; i < 6; i++)
				{
					float currentAngle = robotController.GetJointAngle(i);
					float newAngle = currentAngle + jointDeltas[i] * Mathf.Rad2Deg;
					robotController.SetJointAngle(i, newAngle);
				}

				if (showDebugInfo && iteration % 5 == 0)
				{
					float maxJointDelta2 = 0f;
					for (int i = 0; i < 6; i++)
						maxJointDelta2 = Mathf.Max(maxJointDelta2, Mathf.Abs(jointDeltas[i] * Mathf.Rad2Deg));

					Debug.Log($"[CartesianController] Iteration {iteration}: " +
							 $"Pos error = {posErrorMag:F6}m, Rot error = {rotErrorMag * Mathf.Rad2Deg:F3}°, " +
							 $"Max joint delta = {maxJointDelta2:F3}°");
				}

				if (showDebugInfo && iteration == maxIterations - 1)
				{
					Debug.LogWarning($"[CartesianController] Max iterations reached. " +
								   $"Pos error: {posErrorMag:F4}m, Rot error: {rotErrorMag * Mathf.Rad2Deg:F2}°. " +
								   $"Target may be unreachable or in singularity.");
				}
			}

			// Check if IK converged to acceptable error
			// More lenient thresholds: 1cm position error, 3° rotation error
			bool ikConverged = (finalPosError < 0.01f && finalRotError < 0.05f); // 0.05 rad ≈ 2.86°

			// If IK failed to converge, rollback joint angles
			if (!ikConverged)
			{
				for (int i = 0; i < 6; i++)
				{
					robotController.SetJointAngle(i, originalAngles[i]);
				}

				if (showDebugInfo)
				{
					Debug.LogWarning($"[CartesianController] IK failed to converge - joints rolled back. " +
									$"Final error: pos={finalPosError:F4}m, rot={finalRotError * Mathf.Rad2Deg:F2}°");
				}
			}

			// Restore original smooth movement setting
			robotController.useSmoothMovement = originalSmoothSetting;

			return ikConverged;
		}

		/// <summary>
		/// Solve IK with position only (orientation free)
		/// Returns true if IK converged successfully, false otherwise.
		/// </summary>
		private bool SolveIK(Vector3 targetPos)
		{
			// Use current orientation as target
			Quaternion currentRot = robotController.GetToolRotation();
			return SolveIK(targetPos, currentRot);
		}

		/// <summary>
		/// Calculate the Jacobian matrix using numerical differentiation.
		/// Jacobian relates joint velocities to end-effector velocities.
		/// J = [∂x/∂θ1,  ∂x/∂θ2,  ..., ∂x/∂θ6 ]
		///     [∂y/∂θ1,  ∂y/∂θ2,  ..., ∂y/∂θ6 ]
		///     [∂z/∂θ1,  ∂z/∂θ2,  ..., ∂z/∂θ6 ]
		///     [∂rx/∂θ1, ∂rx/∂θ2, ..., ∂rx/∂θ6]
		///     [∂ry/∂θ1, ∂ry/∂θ2, ..., ∂ry/∂θ6]
		///     [∂rz/∂θ1, ∂rz/∂θ2, ..., ∂rz/∂θ6]
		/// </summary>
		private float[,] CalculateJacobian()
		{
			float[,] jacobian = new float[6, 6]; // 6 DOF (XYZ + RPY) x 6 joints

			Vector3 basePosition = robotController.GetToolPosition();
			Quaternion baseRotation = robotController.GetToolRotation();

			for (int joint = 0; joint < 6; joint++)
			{
				// Save current angle
				float originalAngle = robotController.GetJointAngle(joint);

				// Perturb joint forward
				robotController.SetJointAngle(joint, originalAngle + EPSILON);
				Vector3 posPlus = robotController.GetToolPosition();
				Quaternion rotPlus = robotController.GetToolRotation();

				// Perturb joint backward
				robotController.SetJointAngle(joint, originalAngle - EPSILON);
				Vector3 posMinus = robotController.GetToolPosition();
				Quaternion rotMinus = robotController.GetToolRotation();

				// Restore original angle
				robotController.SetJointAngle(joint, originalAngle);

				// Calculate position derivative (central difference)
				Vector3 posDerivative = (posPlus - posMinus) / (2f * EPSILON * Mathf.Deg2Rad);

				// Calculate orientation derivative (axis-angle representation)
				Quaternion deltaRotPlus = rotPlus * Quaternion.Inverse(baseRotation);
				Quaternion deltaRotMinus = rotMinus * Quaternion.Inverse(baseRotation);

				// Convert quaternions to axis-angle vectors (radians)
				Vector3 axisAnglePlus = QuaternionToAxisAngle(deltaRotPlus);
				Vector3 axisAngleMinus = QuaternionToAxisAngle(deltaRotMinus);

				// Calculate derivative: ∂rotation/∂θ (radians/radian)
				Vector3 rotDerivative = (axisAnglePlus - axisAngleMinus) / (2f * EPSILON * Mathf.Deg2Rad);

				// Fill Jacobian column
				jacobian[0, joint] = posDerivative.x;  // ∂x/∂θ
				jacobian[1, joint] = posDerivative.y;  // ∂y/∂θ
				jacobian[2, joint] = posDerivative.z;  // ∂z/∂θ
				jacobian[3, joint] = rotDerivative.x;  // ∂rx/∂θ (roll)
				jacobian[4, joint] = rotDerivative.y;  // ∂ry/∂θ (pitch)
				jacobian[5, joint] = rotDerivative.z;  // ∂rz/∂θ (yaw)
			}

			return jacobian;
		}

		/// <summary>
		/// Convert a quaternion to axis-angle representation as a 3D rotation vector.
		/// Returns: axis * angle (in radians), representing the rotation
		/// </summary>
		private Vector3 QuaternionToAxisAngle(Quaternion q)
		{
			// Ensure quaternion is in the "short way"
			if (q.w < 0)
			{
				q.x = -q.x;
				q.y = -q.y;
				q.z = -q.z;
				q.w = -q.w;
			}

			// Axis-angle: θ = 2 * acos(w), axis = [x,y,z] / sin(θ/2)
			// Return: rotation vector = axis * θ

			float angle = 2f * Mathf.Acos(Mathf.Clamp(q.w, -1f, 1f)); // θ in radians

			if (angle < 1e-6f)
			{
				// For extremely small rotations, use linear approximation
				// rotation ≈ 2 * [qx, qy, qz]
				return new Vector3(q.x, q.y, q.z) * 2f;
			}

			// Extract rotation axis and compute rotation vector
			float sinHalfAngle = Mathf.Sin(angle * 0.5f);

			if (Mathf.Abs(sinHalfAngle) < 1e-6f)
			{
				// Fallback for numerical safety
				return new Vector3(q.x, q.y, q.z) * 2f;
			}

			Vector3 axis = new Vector3(q.x, q.y, q.z) / sinHalfAngle;
			return axis * angle;
		}

		/// <summary>
		/// Convert a small quaternion rotation to angular velocity
		/// </summary>
		private Vector3 QuaternionToAngularVelocity(Quaternion q, float dt)
		{
			Vector3 axisAngle = QuaternionToAxisAngle(q);
			return axisAngle / dt;
		}

		/// <summary>
		/// Calculate damped pseudo-inverse of Jacobian.
		/// Uses damped least squares (Levenberg-Marquardt damping) to avoid singularities.
		/// J^# = J^T * (J*J^T + λ²I)^-1
		/// </summary>
		private float[,] CalculateDampedPseudoInverse(float[,] jacobian)
		{
			int m = jacobian.GetLength(0); // rows (6 for full DOF, or 3 for position only)
			int n = jacobian.GetLength(1); // columns (6 joints)

			// Calculate J * J^T (m x m)
			float[,] JJT = new float[m, m];
			for (int i = 0; i < m; i++)
			{
				for (int j = 0; j < m; j++)
				{
					JJT[i, j] = 0f;
					for (int k = 0; k < n; k++)
					{
						JJT[i, j] += jacobian[i, k] * jacobian[j, k];
					}
					// Add damping term
					if (i == j)
					{
						JJT[i, j] += dampingFactor * dampingFactor;
					}
				}
			}

			// Invert JJT
			float[,] JJT_inv;
			if (m == 3)
			{
				JJT_inv = Invert3x3Matrix(JJT);
			}
			else if (m == 6)
			{
				JJT_inv = Invert6x6Matrix(JJT);
			}
			else
			{
				Debug.LogError($"[CartesianController] Unsupported matrix size: {m}x{m}");
				return new float[n, m];
			}

			// Calculate pseudo-inverse: J^T * (JJT)^-1 (n x m)
			float[,] pseudoInverse = new float[n, m];
			for (int i = 0; i < n; i++)
			{
				for (int j = 0; j < m; j++)
				{
					pseudoInverse[i, j] = 0f;
					for (int k = 0; k < m; k++)
					{
						pseudoInverse[i, j] += jacobian[k, i] * JJT_inv[k, j];
					}
				}
			}

			return pseudoInverse;
		}

		/// <summary>
		/// Invert a 6x6 matrix using Gauss-Jordan elimination
		/// </summary>
		private float[,] Invert6x6Matrix(float[,] mat)
		{
			int n = 6;
			float[,] a = new float[n, n];
			float[,] inv = new float[n, n];

			// Copy input matrix and create identity
			for (int i = 0; i < n; i++)
			{
				for (int j = 0; j < n; j++)
				{
					a[i, j] = mat[i, j];
					inv[i, j] = (i == j) ? 1f : 0f;
				}
			}

			// Gauss-Jordan elimination
			for (int i = 0; i < n; i++)
			{
				// Find pivot
				float pivot = a[i, i];
				int pivotRow = i;
				for (int k = i + 1; k < n; k++)
				{
					if (Mathf.Abs(a[k, i]) > Mathf.Abs(pivot))
					{
						pivot = a[k, i];
						pivotRow = k;
					}
				}

				// Swap rows if needed
				if (pivotRow != i)
				{
					for (int j = 0; j < n; j++)
					{
						float temp = a[i, j];
						a[i, j] = a[pivotRow, j];
						a[pivotRow, j] = temp;

						temp = inv[i, j];
						inv[i, j] = inv[pivotRow, j];
						inv[pivotRow, j] = temp;
					}
					pivot = a[i, i];
				}

				// Check for singularity
				if (Mathf.Abs(pivot) < 1e-6f)
				{
					Debug.LogWarning($"[CartesianController] Singular 6x6 matrix at row {i}");
					// Return identity as fallback
					for (int ii = 0; ii < n; ii++)
						for (int jj = 0; jj < n; jj++)
							inv[ii, jj] = (ii == jj) ? 1f : 0f;
					return inv;
				}

				// Scale pivot row
				for (int j = 0; j < n; j++)
				{
					a[i, j] /= pivot;
					inv[i, j] /= pivot;
				}

				// Eliminate column
				for (int k = 0; k < n; k++)
				{
					if (k != i)
					{
						float factor = a[k, i];
						for (int j = 0; j < n; j++)
						{
							a[k, j] -= factor * a[i, j];
							inv[k, j] -= factor * inv[i, j];
						}
					}
				}
			}

			return inv;
		}

		/// <summary>
		/// Invert a 3x3 matrix using analytical formula
		/// </summary>
		private float[,] Invert3x3Matrix(float[,] mat)
		{
			float[,] inv = new float[3, 3];

			float det = mat[0, 0] * (mat[1, 1] * mat[2, 2] - mat[2, 1] * mat[1, 2]) -
						mat[0, 1] * (mat[1, 0] * mat[2, 2] - mat[1, 2] * mat[2, 0]) +
						mat[0, 2] * (mat[1, 0] * mat[2, 1] - mat[1, 1] * mat[2, 0]);

			if (Mathf.Abs(det) < 1e-6f)
			{
				Debug.LogWarning("[CartesianController] Singular matrix detected!");
				// Return identity-like matrix
				for (int i = 0; i < 3; i++)
					for (int j = 0; j < 3; j++)
						inv[i, j] = (i == j) ? 1f : 0f;
				return inv;
			}

			float invDet = 1f / det;

			inv[0, 0] = (mat[1, 1] * mat[2, 2] - mat[2, 1] * mat[1, 2]) * invDet;
			inv[0, 1] = (mat[0, 2] * mat[2, 1] - mat[0, 1] * mat[2, 2]) * invDet;
			inv[0, 2] = (mat[0, 1] * mat[1, 2] - mat[0, 2] * mat[1, 1]) * invDet;
			inv[1, 0] = (mat[1, 2] * mat[2, 0] - mat[1, 0] * mat[2, 2]) * invDet;
			inv[1, 1] = (mat[0, 0] * mat[2, 2] - mat[0, 2] * mat[2, 0]) * invDet;
			inv[1, 2] = (mat[1, 0] * mat[0, 2] - mat[0, 0] * mat[1, 2]) * invDet;
			inv[2, 0] = (mat[1, 0] * mat[2, 1] - mat[2, 0] * mat[1, 1]) * invDet;
			inv[2, 1] = (mat[2, 0] * mat[0, 1] - mat[0, 0] * mat[2, 1]) * invDet;
			inv[2, 2] = (mat[0, 0] * mat[1, 1] - mat[1, 0] * mat[0, 1]) * invDet;

			return inv;
		}

		/// <summary>
		/// Reset target to current position and rotation.
		/// Called after environment reset to sync targets with current robot state.
		/// </summary>
		public void ResetTarget()
		{
			targetPosition = robotController.GetToolPosition();
			targetRotation = robotController.GetToolRotation();
			previousValidPosition = targetPosition;
			previousValidRotation = targetRotation;

			if (showDebugInfo)
			{
				Vector3 j1Pos = GetJ1BasePosition();
				float currentReach = Vector3.Distance(j1Pos, targetPosition);
				Debug.Log($"[CartesianController] Reset to position: {targetPosition}, rotation: {targetRotation.eulerAngles}, current reach: {currentReach:F3}m");
			}
		}

		/// <summary>
		/// Subscribe to environment reset event
		/// </summary>
		public void SubscribeToEnvironment(TrainingEnvironment environment)
		{
			if (environment != null)
			{
				environment.OnEnvironmentReset += ResetTarget;
				if (showDebugInfo)
				{
					Debug.Log("[CartesianController] Subscribed to environment reset event");
				}
			}
		}

		/// <summary>
		/// Unsubscribe from environment reset event
		/// </summary>
		public void UnsubscribeFromEnvironment(TrainingEnvironment environment)
		{
			if (environment != null)
			{
				environment.OnEnvironmentReset -= ResetTarget;
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (!showDebugGizmos || robotController == null) return;

			// Draw current tool position (J6 flange)
			Vector3 currentPos = robotController.GetToolPosition();
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(currentPos, 0.02f);

			// Draw target position
			if (isInitialized)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawWireSphere(targetPosition, 0.025f);
				Gizmos.DrawLine(currentPos, targetPosition);
			}

			// Draw workspace limits (donut-shaped envelope)
			if (enforceWorkspaceLimits)
			{
				Vector3 j1BasePosition = GetJ1BasePosition();

				// Draw J1 base position marker
				Gizmos.color = Color.red;
				Gizmos.DrawWireSphere(j1BasePosition, 0.03f);

				// Draw actual maximum reach sphere (faint - for reference)
				Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
				Gizmos.DrawWireSphere(j1BasePosition, maxReachDistance);

				// Draw safe maximum reach sphere (enforced boundary)
				float safeMaxReach = maxReachDistance * maxReachSafetyFactor;
				Gizmos.color = new Color(1f, 0.8f, 0f, 0.4f);
				Gizmos.DrawWireSphere(j1BasePosition, safeMaxReach);

				// Draw minimum reach sphere (inner boundary - donut hole)
				Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
				Gizmos.DrawWireSphere(j1BasePosition, minReachDistance);

				// Draw ground plane limit
				Gizmos.color = new Color(0.5f, 0.3f, 0.1f, 0.5f);
				float planeSize = maxReachDistance * 2f;
				Vector3 planeCenter = new Vector3(j1BasePosition.x, groundPlaneHeight, j1BasePosition.z);

				// Draw ground plane as a grid
				DrawGroundPlaneGrid(planeCenter, planeSize);

				// Draw line from J1 to tool showing current reach
				Gizmos.color = Color.green;
				Gizmos.DrawLine(j1BasePosition, currentPos);

				// Draw reach distance label in scene view
				float currentReach = Vector3.Distance(j1BasePosition, currentPos);
				float safeReachMax = maxReachDistance * maxReachSafetyFactor;
				if (showDebugInfo)
				{
					UnityEditor.Handles.Label(
						Vector3.Lerp(j1BasePosition, currentPos, 0.5f),
						$"Reach: {currentReach:F3}m ({minReachDistance:F2}-{safeReachMax:F2}m safe)\nHeight: {currentPos.y:F3}m (min: {groundPlaneHeight:F2}m)"
					);
				}
			}

			// Draw coordinate axes at tool
			float axisLength = 0.1f;
			Gizmos.color = Color.red;
			Gizmos.DrawRay(currentPos, Vector3.right * axisLength);
			Gizmos.color = Color.green;
			Gizmos.DrawRay(currentPos, Vector3.up * axisLength);
			Gizmos.color = Color.blue;
			Gizmos.DrawRay(currentPos, Vector3.forward * axisLength);
		}

		/// <summary>
		/// Draw a grid to visualize the ground plane limit
		/// </summary>
		private void DrawGroundPlaneGrid(Vector3 center, float size)
		{
			int gridLines = 10;
			float step = size / gridLines;
			float halfSize = size * 0.5f;

			Gizmos.color = new Color(0.5f, 0.3f, 0.1f, 0.5f);

			// Draw grid lines along X axis
			for (int i = 0; i <= gridLines; i++)
			{
				float offset = -halfSize + (i * step);
				Vector3 start = center + new Vector3(offset, 0, -halfSize);
				Vector3 end = center + new Vector3(offset, 0, halfSize);
				Gizmos.DrawLine(start, end);
			}

			// Draw grid lines along Z axis
			for (int i = 0; i <= gridLines; i++)
			{
				float offset = -halfSize + (i * step);
				Vector3 start = center + new Vector3(-halfSize, 0, offset);
				Vector3 end = center + new Vector3(halfSize, 0, offset);
				Gizmos.DrawLine(start, end);
			}
		}
#endif
	}
}