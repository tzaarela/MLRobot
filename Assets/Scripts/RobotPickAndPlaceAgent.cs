using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using MLRobot.UI;
using MLRobot.Systems;

namespace RobotArm
{
	/// <summary>
	/// Movement modes for the robot agent
	/// </summary>
	public enum MovementMode
	{
		Joint = 0,           // Direct joint control (default)
		CartesianWorld = 1,  // Cartesian movement in world space
		CartesianTool = 2,   // Cartesian movement in tool/local space
		Auto = 3             // Agent learns to switch between modes
	}

	/// <summary>
	/// ML-Agents agent for robot arm pick and place task.
	/// Learns to pick up a cube and place it in a target zone.
	///
	/// MOVEMENT MODES:
	/// - Joint: Direct control of joint angles (traditional approach)
	/// - CartesianWorld: Control TCP position/orientation in world space using IK
	/// - CartesianTool: Control TCP position/orientation in tool-local space using IK
	/// - Auto: Agent learns to switch between Joint/CartesianWorld/CartesianTool modes
	///
	/// BEHAVIOR PARAMETERS CONFIGURATION (Unity Inspector):
	///
	/// Observations: 32 (Vector)
	///   - 6: Joint angles (normalized)
	///   - 7: Tool state (position, orientation, distance to drop zone)
	///   - 7: Target object state (position, orientation, distance)
	///   - 5: Goal state (drop zone position, distance, Y rotation)
	///   - 3: Gripper state (magnet on/off, holding object, can activate)
	///   - 3: Current movement mode (one-hot: Joint/CartesianWorld/CartesianTool)
	///   - 1: Ground distance (object height normalized to 0.5m range)
	///
	/// Actions:
	///   Continuous: 6
	///     - Joint mode: 6 joint angle deltas (normalized -1 to 1)
	///     - Cartesian modes: [deltaX, deltaY, deltaZ, deltaRoll, deltaPitch, deltaYaw]
	///
	///   Discrete: 2 branches
	///     - Branch 0: Magnet control (2 values: 0=off, 1=on)
	///     - Branch 1: Mode selection (3 values: 0=Joint, 1=CartesianWorld, 2=CartesianTool)
	///                 Only used when movementMode = Auto
	/// </summary>
	public class RobotPickAndPlaceAgent: Agent
	{
		[Header("References")]
		[Tooltip("The robot controller this agent controls")]
		public RobotController robotController;

		[Tooltip("Cartesian controller for IK-based movement")]
		public RobotCartesianController cartesianController;

		[Tooltip("The object to pick up")]
		public Transform targetObject;

		[Tooltip("The drop-off zone")]
		public Transform dropOffZone;

		[Header("Environment")]
		[Tooltip("Parent transform for this training instance (for parallel training)")]
		public Transform environmentRoot;

		[Tooltip("Training environment reference (auto-assigned)")]
		public TrainingEnvironment trainingEnvironment;

		[Header("Movement Mode")]
		[Tooltip("Select the movement mode for this agent")]
		public MovementMode movementMode = MovementMode.Joint;

		[Tooltip("Action scale multiplier for Cartesian position movements (increases sensitivity)")]
		[Range(1f, 100f)]
		public float cartesianPositionActionScale = 50f;

		[Tooltip("Action scale multiplier for Cartesian orientation movements (increases sensitivity)")]
		[Range(1f, 100f)]
		public float cartesianOrientationActionScale = 20f;

		[Tooltip("Enable roll (X-axis rotation) in Cartesian mode")]
		public bool useRoll = true;

		[Tooltip("Enable pitch (Y-axis rotation) in Cartesian mode")]
		public bool usePitch = true;

		[Tooltip("Enable yaw (Z-axis rotation) in Cartesian mode")]
		public bool useYaw = true;

		[Header("Reward Settings")]
		[Tooltip("Reward for successfully completing task (object stays in zone for full duration)")]
		public float successReward = 1.0f;

		[Tooltip("Big reward when object is first placed correctly in drop zone")]
		public float placementReward = 0.5f;

		[Tooltip("Continuous reward per step while object stays in drop zone")]
		public float inZoneRewardPerStep = 0.01f;

		[Tooltip("Penalty for dropping object outside drop zone")]
		public float dropPenalty = -0.5f;

		[Tooltip("Reward scale for getting closer to object")]
		public float approachRewardScale = 0.01f;

		[Tooltip("Reward for picking up the object")]
		public float pickupReward = 0.5f;

		public float safeLiftHeight = 0.1f; // 10cm above pickup (target)
		public float liftIncrement = 0.001f; // 1mm increments
		public float liftRewardPerMM = 0.003f; // 0.3f total over 100mm

		[Tooltip("Reward for holding magnet downwards towards target object")]
		public float alignmentRewardScale = 0.01f;

		[Tooltip("Reward scale for maintaining object alignment with drop zone rotation")]
		public float goalAlignmentRewardScale = 0.01f;

		[Tooltip("One-time reward when held object is first fully contained and aligned in drop zone")]
		public float heldInZoneAlignedReward = 2.0f;

		[Tooltip("Reward scale for holding object upright (1.0 at 0° tilt, 0 at 90°, negative beyond)")]
		public float uprightRewardScale = 0.02f;

		[Tooltip("Reward scale for bringing object closer to goal")]
		public float deliveryRewardScale = 0.02f;

		[Tooltip("Small penalty per step to encourage efficiency")]
		public float stepPenalty = -0.0002f;

		[Header("Home Return Settings")]
		[Tooltip("Reward bonus for returning home after successful placement")]
		public float homeReturnBonus = 0.5f;

		[Tooltip("Continuous reward per step for moving toward home (after placement)")]
		public float homeApproachRewardScale = 0.005f;

		[Tooltip("One-time reward for first reaching home position")]
		public float homeArrivalReward = 0.1f;

		[Tooltip("Continuous reward per step while at home during timer")]
		public float atHomeRewardPerStep = 0.005f;

		[Tooltip("Average joint angle tolerance (degrees) to consider robot at home")]
		[Range(1f, 20f)]
		public float homePositionTolerance = 5f;

		[Tooltip("Maximum rotation angle (degrees) from drop zone to start timer")]
		[Range(5f, 90f)]
		public float alignmentToleranceForTimer = 30f;

		[Header("Episode Settings")]
		[Tooltip("Maximum steps before episode ends")]
		public int maxEpisodeSteps = 5000;

		[Tooltip("Height below which object is considered fallen")]
		public float objectFallThreshold = 0.05f;

		[Header("Reward Tracking")]
		[Tooltip("Tracks rewards by category for visualization (optional)")]
		[SerializeField] private RewardTracker rewardTracker;

		[Header("Debug")]
		public bool showDebugLogs = false;

		// State tracking
		private Rigidbody targetRigidbody;
		private Vector3 initialObjectPosition;
		private Quaternion initialObjectRotation;
		private float previousDistanceToObject;
		private float previousDistanceToGoal;
		private bool wasHolding = false;
		private int currentStep = 0;
		private Vector3 dropZoneSize;

		// Phase state machine
		private AgentPhase currentPhase;
		private float dropZoneTimer = 0f;

		// Lift bonus tracking (incremental rewards)
		private float objectPickupHeight = 0f;
		private float maxLiftHeightReached = 0f; // Max lift achieved this episode

		// Held-in-zone milestone
		private bool hasReachedZoneAligned = false;

		// Home return tracking
		private float[] homeJointAngles = new float[6];
		private bool isAtHome = false;
		private bool hasReturnedHome = false;
		private float previousJointDistanceToHome = 0f;

		// Movement mode tracking (for Auto mode)
		private MovementMode currentActiveMode = MovementMode.Joint;

		// Cached components and pre-allocated buffers (performance)
		private bool isHeuristicMode = false;
		private float[] jointDeltasBuffer = new float[6];
		private Vector3[] cornersBuffer = new Vector3[8];
		private Renderer targetRenderer;
		private Collider targetCollider;

		private void Awake()
		{
			dropZoneSize = dropOffZone.GetComponent<DropZoneVisualizer>().dropZoneSize;
		}

		public override void Initialize()
		{
			// Cache BehaviorParameters to avoid per-frame GetComponent
			var behaviorParams = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
			isHeuristicMode = behaviorParams != null &&
				behaviorParams.BehaviorType == Unity.MLAgents.Policies.BehaviorType.HeuristicOnly;

			if (targetObject != null)
			{
				targetRigidbody = targetObject.GetComponent<Rigidbody>();
				targetRenderer = targetObject.GetComponent<Renderer>();
				targetCollider = targetObject.GetComponent<Collider>();
				initialObjectPosition = GetLocalPosition(targetObject.position);
				initialObjectRotation = targetObject.rotation;
			}

			// Auto-find TrainingEnvironment if not assigned
			if (trainingEnvironment == null)
			{
				trainingEnvironment = GetComponentInParent<TrainingEnvironment>();
			}

			// Auto-find CartesianController if not assigned
			if (cartesianController == null)
			{
				cartesianController = GetComponent<RobotCartesianController>();
			}

			// Ensure CartesianController has proper robotController reference
			if (cartesianController != null && cartesianController.robotController == null)
			{
				cartesianController.robotController = robotController;
			}

			// Subscribe cartesian controller to environment reset
			if (cartesianController != null && trainingEnvironment != null)
			{
				cartesianController.SubscribeToEnvironment(trainingEnvironment);
			}

			// Initialize current mode based on movement mode setting
			currentActiveMode = (movementMode == MovementMode.Auto) ? MovementMode.Joint : movementMode;

			// Apply runtime config overrides (config.json / command-line)
			maxEpisodeSteps = RuntimeConfig.Agent.maxEpisodeSteps;
		}

		public override void OnEpisodeBegin()
		{
			currentStep = 0;
			currentPhase = AgentPhase.Approaching;
			wasHolding = false;

			// Reset reward tracking for new episode
			if (rewardTracker != null)
			{
				rewardTracker.ResetTracking();
			}

			// Reset drop zone timer
			dropZoneTimer = 0f;

			// Reset lift bonus tracking
			maxLiftHeightReached = 0f;
			objectPickupHeight = 0f;

			// Reset held-in-zone milestone
			hasReachedZoneAligned = false;

			// Reset current mode (for Auto mode, start with Joint)
			currentActiveMode = (movementMode == MovementMode.Auto) ? MovementMode.Joint : movementMode;

			// Reset home return tracking
			hasReturnedHome = false;
			isAtHome = false;

			// Cache home joint angles
			for (int i = 0; i < 6; i++)
			{
				homeJointAngles[i] = robotController.joints[i].startAngle;
			}

			// Calculate initial distance to home
			previousJointDistanceToHome = CalculateJointDistanceToHome();

			// Reset environment (robot + target object + all subscribed components)
			if (trainingEnvironment != null)
			{
				trainingEnvironment.ResetEnvironment();
			}
			else
			{
				// Fallback: manual reset if no training environment
				robotController.ResetToStartPosition();
			}

			// Reset Cartesian controller target to current robot pose
			if (cartesianController != null)
			{
				cartesianController.ResetTarget();
			}

			// Cache initial distances
			previousDistanceToObject = Vector3.Distance(robotController.GetToolPosition(), targetObject.position);
			previousDistanceToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);
		}

		private void ResetTargetObject()
		{
			// Magnet release and position/physics reset are now handled by
			// OnEpisodeBegin (magnet) and TrainingEnvironment.ResetEnvironment() (position/physics).
			// This method is kept for any agent-specific target reset logic if needed in the future.
		}

		public override void CollectObservations(VectorSensor sensor)
		{
			// === Joint States (6 values) ===
			// Normalized joint angles
			for (int i = 0; i < 6; i++)
			{
				sensor.AddObservation(robotController.GetNormalizedJointAngle(i));
			}

			// === Tool State (7 values) ===
			// Tool position (relative to environment root)
			Vector3 toolPos = GetLocalPosition(robotController.GetToolPosition());
			sensor.AddObservation(toolPos);

			// Tool orientation (as forward vector)
			Vector3 toolRight = robotController.GetMagnetFaceNormal();
			sensor.AddObservation(toolRight);

			//vector between drop off and magnet
			sensor.AddObservation(dropOffZone.position - toolPos);

			// === Target Object State (7 values) ===
			// Object position
			Vector3 objectPos = GetLocalPosition(targetObject.position);
			sensor.AddObservation(objectPos);

			// Object Rotation
			sensor.AddObservation(targetObject.up.y); // "is it upright?"

			// Vector from tool to object
			Vector3 toolToObject = targetObject.position - robotController.GetToolPosition();
			sensor.AddObservation(toolToObject.normalized);

			// Distance to object (normalized)
			float distToObject = toolToObject.magnitude;
			sensor.AddObservation(Mathf.Clamp01(distToObject / 2f));

			// === Goal State (5 values) ===
			// Drop zone position
			Vector3 goalPos = GetLocalPosition(dropOffZone.position);
			sensor.AddObservation(goalPos);

			// Distance from object to goal (normalized)
			float distToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);
			sensor.AddObservation(Mathf.Clamp01(distToGoal / 2f));

			// Drop zone Y rotation (yaw) - normalized to 0-1
			float dropZoneYaw = dropOffZone.eulerAngles.y / 360f;
			sensor.AddObservation(dropZoneYaw);

			// === Gripper State (3 values) ===
			sensor.AddObservation(robotController.magnet.IsActive ? 1f : 0f);
			sensor.AddObservation(robotController.IsHoldingObject() ? 1f : 0f);
			sensor.AddObservation(robotController.magnet.CanActivateMagnet);

			// === Current Movement Mode (3 values - one-hot encoding) ===
			// Only relevant for Auto mode, but always included for consistent observation space
			sensor.AddObservation(currentActiveMode == MovementMode.Joint ? 1f : 0f);
			sensor.AddObservation(currentActiveMode == MovementMode.CartesianWorld ? 1f : 0f);
			sensor.AddObservation(currentActiveMode == MovementMode.CartesianTool ? 1f : 0f);

			// === Ground Distance (1 value) ===
			// Distance from object to ground plane (normalized to 0.5m range)
			float groundDistance = Mathf.Max(0f, targetObject.position.y);
			sensor.AddObservation(Mathf.Clamp01(groundDistance / 0.5f));

			// Total: 6 + 7 + 7 + 5 + 3 + 3 + 1 = 32 observations
		}

		public override void OnActionReceived(ActionBuffers actions)
		{
			currentStep++;

			if (!isHeuristicMode)
			{
				// === Mode Selection (Auto mode only) ===
				if (movementMode == MovementMode.Auto)
				{
					// Discrete action branch 1: mode selection (0=Joint, 1=CartesianWorld, 2=CartesianTool)
					int modeAction = actions.DiscreteActions[1];
					MovementMode newMode = (MovementMode)modeAction;

					// Switch mode if different
					if (newMode != currentActiveMode)
					{
						currentActiveMode = newMode;

						// Reset Cartesian target when switching to Cartesian modes
						if ((currentActiveMode == MovementMode.CartesianWorld || currentActiveMode == MovementMode.CartesianTool)
							&& cartesianController != null)
						{
							cartesianController.ResetTarget();
						}
					}
				}

				// === Apply Movement Based on Current Active Mode ===
				if (currentActiveMode == MovementMode.Joint)
			{
				// Joint mode: 6 continuous actions control joint angle deltas
				for (int i = 0; i < 6; i++)
				{
					jointDeltasBuffer[i] = actions.ContinuousActions[i];
				}
				robotController.ApplyJointDeltas(jointDeltasBuffer, Time.fixedDeltaTime);
			}
			else if (currentActiveMode == MovementMode.CartesianWorld || currentActiveMode == MovementMode.CartesianTool)
			{
				// Cartesian mode: 6 continuous actions = 3 position deltas (XYZ) + 3 orientation deltas (RPY in radians)
				if (cartesianController != null)
				{
					// Scale actions for faster learning (actions are normalized -1 to 1)
					float deltaX = actions.ContinuousActions[0] * cartesianPositionActionScale;
					float deltaY = actions.ContinuousActions[1] * cartesianPositionActionScale;
					float deltaZ = actions.ContinuousActions[2] * cartesianPositionActionScale;
					float deltaRoll = useRoll ? actions.ContinuousActions[3] * cartesianOrientationActionScale : 0f;
					float deltaPitch = usePitch ? actions.ContinuousActions[4] * cartesianOrientationActionScale : 0f;
					float deltaYaw = useYaw ? actions.ContinuousActions[5] * cartesianOrientationActionScale : 0f;

					// Calculate position delta in appropriate frame
					Vector3 positionDelta = new Vector3(deltaX, deltaY, deltaZ);
					Vector3 worldPositionDelta;

					if (currentActiveMode == MovementMode.CartesianWorld)
					{
						// World space - use delta directly
						worldPositionDelta = positionDelta;
					}
					else // CartesianTool
					{
						// Tool space - transform delta to world space
						Quaternion toolRotation = robotController.GetToolRotation();
						worldPositionDelta = toolRotation * positionDelta;
					}

					cartesianController.MoveWorld(worldPositionDelta, Time.fixedDeltaTime);

					if (Mathf.Abs(deltaRoll) > 0.01f || Mathf.Abs(deltaPitch) > 0.01f || Mathf.Abs(deltaYaw) > 0.01f)
					{
						cartesianController.RotateCartesian(deltaRoll, deltaPitch, deltaYaw, Time.fixedDeltaTime);
					}
				}
				else
				{
					Debug.LogWarning("[Agent] Cartesian mode selected but no CartesianController assigned! Falling back to joint control.");
					// Fallback to joint control if Cartesian controller missing
					for (int i = 0; i < 6; i++)
					{
						jointDeltasBuffer[i] = actions.ContinuousActions[i];
					}
					robotController.ApplyJointDeltas(jointDeltasBuffer, Time.fixedDeltaTime);
				}
			}
			} // End of !isHeuristicMode check

			// === Magnet Control ===
			// Discrete action branch 0: magnet (0 = off, 1 = on)
			// Only control magnet if not in Heuristic mode (let keyboard handle it in Heuristic)
			if (!isHeuristicMode)
			{
				bool magnetOn = actions.DiscreteActions[0] == 1;
				robotController.SetMagnetActive(magnetOn);
			}

			// Calculate rewards
			CalculateRewards();

			// Check episode end conditions
			CheckEpisodeEnd();
		}

		private void CalculateRewards()
		{
			// Step penalty (always)
			AddCategorizedReward(RewardCategory.Efficiency, stepPenalty);

			// Cache values used across phases
			float currentDistToObject = Vector3.Distance(robotController.GetToolPosition(), targetObject.position);
			float currentDistToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);
			bool isHolding = robotController.IsHoldingObject();

			// Evaluate phase transitions (may change currentPhase and fire one-time rewards)
			EvaluateTransitions(isHolding);

			// Dispatch to per-phase reward logic
			switch (currentPhase)
			{
				case AgentPhase.Approaching:
					RewardApproaching(currentDistToObject);
					break;
				case AgentPhase.Delivering:
					RewardDelivering(currentDistToGoal);
					break;
				case AgentPhase.Placed:
					RewardPlaced();
					break;
				// Succeeded/Failed are terminal — no per-step rewards
			}

			// Update tracking for next step
			previousDistanceToObject = currentDistToObject;
			previousDistanceToGoal = currentDistToGoal;
			wasHolding = isHolding;
		}

		/// <summary>
		/// Check phase exit conditions and fire one-time transition rewards.
		/// Order matters: check most specific transitions first.
		/// </summary>
		private void EvaluateTransitions(bool isHolding)
		{
			switch (currentPhase)
			{
				case AgentPhase.Approaching:
					// Pickup: just grabbed the object
					if (isHolding && !wasHolding)
					{
						AddCategorizedReward(RewardCategory.Pickup, pickupReward);
						objectPickupHeight = targetObject.position.y;
						TransitionTo(AgentPhase.Delivering);
					}
					break;

				case AgentPhase.Delivering:
					// Floor exploit: held object below threshold
					if (isHolding && targetObject.position.y < objectFallThreshold)
					{
						if (showDebugLogs)
							Debug.LogWarning($"[Failure] Agent pushed held object below floor: y={targetObject.position.y:F3}m");
						AddCategorizedReward(RewardCategory.DropFall, dropPenalty * 2f);
						TransitionTo(AgentPhase.Failed);
						EndEpisode();
						return;
					}
					// Dropped the object while delivering
					if (wasHolding && !isHolding)
					{
						HandleObjectDropped();
					}
					break;

				case AgentPhase.Placed:
					// Object left the zone — reset timer, go back to Approaching
					if (!IsObjectInDropZone())
					{
						if (showDebugLogs)
							Debug.Log($"[Timer Reset] Object left drop zone after {dropZoneTimer:F2}s");
						dropZoneTimer = 0f;
						TransitionTo(AgentPhase.Approaching);
					}
					// Agent re-grabbed the object during Placed phase
					else if (isHolding && !wasHolding)
					{
						dropZoneTimer = 0f;
						AddCategorizedReward(RewardCategory.Pickup, pickupReward);
						objectPickupHeight = targetObject.position.y;
						TransitionTo(AgentPhase.Delivering);
					}
					break;
			}
		}

		/// <summary>
		/// Encapsulates the 3-way drop logic: in-zone+aligned, in-zone+misaligned, outside zone.
		/// Called from EvaluateTransitions when wasHolding && !isHolding in Delivering phase.
		/// </summary>
		private void HandleObjectDropped()
		{
			if (IsObjectInDropZone())
			{
				float yawDiff = CalculateYawDifference();

				if (yawDiff <= alignmentToleranceForTimer)
				{
					// Good alignment — reward and start timer
					AddCategorizedReward(RewardCategory.Placement, placementReward);
					dropZoneTimer = 0f;
					TransitionTo(AgentPhase.Placed);

					if (showDebugLogs)
						Debug.Log($"[Placed in zone] Reward: +{placementReward} | Yaw: {yawDiff:F1}° | Timer started");
				}
				else
				{
					// Poor alignment — penalty, back to Approaching (can re-pick)
					float alignmentPenalty = -0.3f * (yawDiff / 180f);
					AddCategorizedReward(RewardCategory.Alignment, alignmentPenalty);
					TransitionTo(AgentPhase.Approaching);

					if (showDebugLogs)
						Debug.Log($"[Poor Alignment] Yaw: {yawDiff:F1}° (max: {alignmentToleranceForTimer}°) | Penalty: {alignmentPenalty:F3}");
				}
			}
			else
			{
				// Dropped outside zone — fail
				if (showDebugLogs)
					Debug.Log($"[Reward] Value: {dropPenalty} | Dropped object outside goal zone.");
				AddCategorizedReward(RewardCategory.DropFall, dropPenalty);
				TransitionTo(AgentPhase.Failed);
				EndEpisode();
			}
		}

		/// <summary>
		/// Set phase and log transition for debugging.
		/// </summary>
		private void TransitionTo(AgentPhase newPhase)
		{
			if (showDebugLogs)
				Debug.Log($"[Phase] {currentPhase} → {newPhase} (step {currentStep})");
			currentPhase = newPhase;
		}

		/// <summary>
		/// Rewards for Approaching phase: move toward object, align magnet.
		/// </summary>
		private void RewardApproaching(float currentDistToObject)
		{
			// Reward for getting closer to object
			float approachDelta = previousDistanceToObject - currentDistToObject;
			AddCategorizedReward(RewardCategory.Approach, approachDelta > 0 ? approachRewardScale : -approachRewardScale);

			// Bonus for good alignment (magnet facing toward object)
			float alignment = Vector3.Dot(robotController.GetMagnetFaceNormal(),
				(targetObject.position - robotController.GetToolPosition()).normalized);
			if (alignment > 0.8f)
				AddCategorizedReward(RewardCategory.Alignment, alignmentRewardScale);
		}

		/// <summary>
		/// Rewards for Delivering phase: move to goal, lift, align, keep upright.
		/// </summary>
		private void RewardDelivering(float currentDistToGoal)
		{
			// Reward for bringing object closer to goal
			float deliveryDelta = previousDistanceToGoal - currentDistToGoal;
			if (deliveryDelta > 0)
				AddCategorizedReward(RewardCategory.Delivery, deliveryRewardScale);
			else if (deliveryDelta < 0)
				AddCategorizedReward(RewardCategory.Delivery, -deliveryRewardScale);

			// Incremental rewards for lifting object toward safe height
			float objectHeight = targetObject.position.y;
			float liftHeight = Mathf.Min(objectHeight - objectPickupHeight, safeLiftHeight);

			if (liftHeight > maxLiftHeightReached)
			{
				float heightGain = liftHeight - maxLiftHeightReached;
				int incrementsCrossed = Mathf.FloorToInt(heightGain / liftIncrement);

				if (incrementsCrossed > 0)
				{
					float reward = incrementsCrossed * liftRewardPerMM;
					AddCategorizedReward(RewardCategory.Delivery, reward);
					maxLiftHeightReached += incrementsCrossed * liftIncrement;

					if (showDebugLogs)
						Debug.Log($"[Lift Progress] +{reward:F4} for {incrementsCrossed}mm lift (now at {maxLiftHeightReached:F3}m / {safeLiftHeight}m)");
				}
			}

			// Reward for maintaining yaw alignment with drop zone rotation (quadratic mapping)
			float yawDiff = CalculateYawDifference();
			float normalizedYaw = yawDiff / 180f;
			float alignmentReward = goalAlignmentRewardScale * (1f - 2f * normalizedYaw * normalizedYaw);
			AddCategorizedReward(RewardCategory.GoalAlignment, alignmentReward);

			// Reward for holding object upright
			float uprightness = Vector3.Dot(targetObject.up, Vector3.up);
			float uprightReward = uprightness * uprightRewardScale;
			AddCategorizedReward(RewardCategory.Alignment, uprightReward);

			if (showDebugLogs && currentStep % 100 == 0)
			{
				float tiltAngle = Mathf.Acos(Mathf.Clamp(uprightness, -1f, 1f)) * Mathf.Rad2Deg;
				Debug.Log($"[Alignment] Yaw: {yawDiff:F1}° | Upright: {tiltAngle:F1}° (factor: {uprightness:F3}) | Rewards: align={alignmentReward:F4}, upright={uprightReward:F4}");
			}

			// One-time reward for first reaching the drop zone fully contained and aligned while holding
			if (!hasReachedZoneAligned && IsObjectInDropZone() && yawDiff <= alignmentToleranceForTimer)
			{
				hasReachedZoneAligned = true;
				AddCategorizedReward(RewardCategory.Placement, heldInZoneAlignedReward);

				if (showDebugLogs)
					Debug.Log($"[Held In Zone] One-time reward: +{heldInZoneAlignedReward} | Yaw: {yawDiff:F1}°");
			}

			// Penalize holding object at dangerous heights
			if (objectHeight < objectFallThreshold)
			{
				AddCategorizedReward(RewardCategory.DropFall, -0.5f);
				if (showDebugLogs)
					Debug.LogWarning($"[Agent] Object critically low: {objectHeight:F3}m - HIGH PENALTY");
			}
			else if (objectHeight < objectFallThreshold + 0.05f)
			{
				AddCategorizedReward(RewardCategory.DropFall, -0.1f);
			}
		}

		/// <summary>
		/// Rewards for Placed phase: timer, home return, success check.
		/// </summary>
		private void RewardPlaced()
		{
			dropZoneTimer += Time.fixedDeltaTime;

			// Continuous reward for keeping object in zone
			AddCategorizedReward(RewardCategory.Placement, inZoneRewardPerStep);

			// Update home status
			bool wasAtHome = isAtHome;
			isAtHome = CheckIfAtHome();

			// One-time reward for arriving at home
			if (isAtHome && !wasAtHome && !hasReturnedHome)
			{
				AddCategorizedReward(RewardCategory.Home, homeArrivalReward);
				hasReturnedHome = true;

				if (showDebugLogs)
					Debug.Log($"[Home Arrival] Reward: +{homeArrivalReward}");
			}

			// Continuous reward for being at home
			if (isAtHome)
			{
				AddCategorizedReward(RewardCategory.Home, atHomeRewardPerStep);
			}
			else
			{
				// Reward for moving toward home
				float currentDistToHome = CalculateJointDistanceToHome();
				float homeApproachDelta = previousJointDistanceToHome - currentDistToHome;

				if (homeApproachDelta > 0)
				{
					float homeApproachReward = homeApproachDelta * homeApproachRewardScale;
					AddCategorizedReward(RewardCategory.Home, homeApproachReward);
				}

				previousJointDistanceToHome = currentDistToHome;
			}

			if (showDebugLogs && currentStep % 50 == 0)
			{
				float stayDuration = trainingEnvironment != null ? trainingEnvironment.dropZoneStayDuration : 1f;
				Debug.Log($"[Timer] {dropZoneTimer:F2}s / {stayDuration:F2}s | At home: {isAtHome} | Joint dist: {CalculateJointDistanceToHome():F1}°");
			}

			// Success! Timer completed
			float requiredDuration = trainingEnvironment != null ? trainingEnvironment.dropZoneStayDuration : 1f;
			if (dropZoneTimer >= requiredDuration)
			{
				// Base success reward (50%)
				float baseReward = successReward * 0.5f;
				AddCategorizedReward(RewardCategory.Success, baseReward);

				// Bonus for being at home (50%)
				if (isAtHome)
				{
					AddCategorizedReward(RewardCategory.Home, homeReturnBonus);
					if (showDebugLogs)
						Debug.Log($"[SUCCESS + HOME!] Base: +{baseReward:F2} | Home bonus: +{homeReturnBonus:F2}");
				}
				else
				{
					if (showDebugLogs)
						Debug.Log($"[SUCCESS (no home)] Base: +{baseReward:F2} | Missed bonus: {homeReturnBonus:F2}");
				}

				TransitionTo(AgentPhase.Succeeded);
				EndEpisode();
			}
		}

		private void CheckEpisodeEnd()
		{
			// Failure: Object fell off table
			if (targetObject.position.y < objectFallThreshold)
			{
				if (showDebugLogs)
				{
					Debug.Log($"[Failure] Object fell below floor: y={targetObject.position.y:F3}m < {objectFallThreshold}m");
				}
				AddCategorizedReward(RewardCategory.DropFall, dropPenalty);
				EndEpisode();
				return;
			}

			// Timeout (only in training mode, not in manual/heuristic mode)
			if (!isHeuristicMode && currentStep >= maxEpisodeSteps)
			{
				EndEpisode();
			}
		}

		/// <summary>
		/// Check if target object is fully contained within the drop zone bounds.
		/// Supports both center-only and full-containment modes.
		/// </summary>
		public bool IsObjectInDropZone()
		{
			if (targetObject == null || dropOffZone == null) return false;

			// Get drop zone bounds in world space
			Bounds dropZoneBounds = new Bounds(dropOffZone.position, dropZoneSize);

			if (trainingEnvironment != null && trainingEnvironment.requireFullContainment)
			{
				// Check if entire object bounds are contained within drop zone
				Bounds objectBounds = GetObjectBounds(targetObject);

				if (objectBounds.size == Vector3.zero)
				{
					// No renderer found, fall back to position check
					return dropZoneBounds.Contains(targetObject.position);
				}

				// Check if all 8 corners of the object bounds are inside the drop zone
				Vector3 min = objectBounds.min;
				Vector3 max = objectBounds.max;

				cornersBuffer[0] = new Vector3(min.x, min.y, min.z);
				cornersBuffer[1] = new Vector3(min.x, min.y, max.z);
				cornersBuffer[2] = new Vector3(min.x, max.y, min.z);
				cornersBuffer[3] = new Vector3(min.x, max.y, max.z);
				cornersBuffer[4] = new Vector3(max.x, min.y, min.z);
				cornersBuffer[5] = new Vector3(max.x, min.y, max.z);
				cornersBuffer[6] = new Vector3(max.x, max.y, min.z);
				cornersBuffer[7] = new Vector3(max.x, max.y, max.z);

				for (int i = 0; i < 8; i++)
				{
					if (!dropZoneBounds.Contains(cornersBuffer[i]))
					{
						return false; // At least one corner is outside
					}
				}

				return true; // All corners are inside
			}
			else
			{
				// Simple center-based check
				return dropZoneBounds.Contains(targetObject.position);
			}
		}

		/// <summary>
		/// Get the world-space axis-aligned bounding box of an object.
		/// Takes rotation into account by using Renderer.bounds.
		/// </summary>
		private Bounds GetObjectBounds(Transform obj)
		{
			// Use cached references for the target object (hot path)
			if (obj == targetObject)
			{
				if (targetRenderer != null)
					return targetRenderer.bounds;
				if (targetCollider != null)
					return targetCollider.bounds;
			}
			else
			{
				// Fallback for non-target objects
				Renderer renderer = obj.GetComponent<Renderer>();
				if (renderer != null)
					return renderer.bounds;

				Collider collider = obj.GetComponent<Collider>();
				if (collider != null)
					return collider.bounds;
			}

			Debug.LogWarning($"No Renderer or Collider found on {obj.name} for bounds calculation");
			return new Bounds(obj.position, Vector3.zero);
		}

		public override void Heuristic(in ActionBuffers actionsOut)
		{
			// This allows the keyboard controller to work alongside ML training
			// The keyboard input is handled separately by RobotKeyboardInputEnhanced
			// Here we just provide neutral actions

			var continuousActions = actionsOut.ContinuousActions;
			for (int i = 0; i < 6; i++)
			{
				continuousActions[i] = 0f;
			}

			var discreteActions = actionsOut.DiscreteActions;
			// Branch 0: Magnet state
			discreteActions[0] = robotController.magnet.IsActive ? 1 : 0;
			// Branch 1: Current movement mode (for Auto mode)
			discreteActions[1] = (int)currentActiveMode;
		}

		// Helper methods for multi-environment support
		private Vector3 GetLocalPosition(Vector3 worldPosition)
		{
			if (environmentRoot != null)
			{
				return environmentRoot.InverseTransformPoint(worldPosition);
			}
			return worldPosition;
		}

		private Vector3 GetWorldPosition(Vector3 localPosition)
		{
			if (environmentRoot != null)
			{
				return environmentRoot.TransformPoint(localPosition);
			}
			return localPosition;
		}

		/// <summary>
		/// Calculate average joint angle distance from home position
		/// </summary>
		private float CalculateJointDistanceToHome()
		{
			float totalError = 0f;

			for (int i = 0; i < 6; i++)
			{
				float currentAngle = robotController.GetJointAngle(i);
				float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentAngle, homeJointAngles[i]));
				totalError += angleDiff;
			}

			return totalError / 6f;  // Average error per joint
		}

		/// <summary>
		/// Check if robot is at home position
		/// </summary>
		private bool CheckIfAtHome()
		{
			return CalculateJointDistanceToHome() <= homePositionTolerance;
		}

		/// <summary>
		/// Calculate yaw-only rotation difference between target object and drop zone.
		/// Projects forward vectors onto the horizontal plane, ignoring tilt (handled by uprightRewardScale).
		/// </summary>
		private float CalculateYawDifference()
		{
			Vector3 objectForward = Vector3.ProjectOnPlane(targetObject.forward, Vector3.up);
			Vector3 goalForward = Vector3.ProjectOnPlane(dropOffZone.forward, Vector3.up);
			if (objectForward.sqrMagnitude < 0.001f || goalForward.sqrMagnitude < 0.001f)
				return 180f; // Degenerate (cube nearly upside-down) — treat as worst case
			return Vector3.Angle(objectForward.normalized, goalForward.normalized);
		}

		/// <summary>
		/// Add reward with optional category tracking.
		/// Routes through RewardTracker if available, otherwise calls base AddReward.
		/// </summary>
		private void AddCategorizedReward(RewardCategory category, float amount)
		{
			if (rewardTracker != null)
			{
				rewardTracker.AddCategorizedReward(category, amount);
			}
			else
			{
				AddReward(amount);
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			// Draw spawn area
			if (environmentRoot != null && trainingEnvironment != null)
			{
				Gizmos.color = Color.yellow;
				Vector3 center = environmentRoot.TransformPoint((trainingEnvironment.objectSpawnAreaMin + trainingEnvironment.objectSpawnAreaMax) / 2f);
				Vector3 size = trainingEnvironment.objectSpawnAreaMax - trainingEnvironment.objectSpawnAreaMin;
				Gizmos.DrawWireCube(center, size);
			}

			// Draw object bounds (in play mode)
			if (Application.isPlaying && targetObject != null)
			{
				Bounds objBounds = GetObjectBounds(targetObject);

				if (objBounds.size != Vector3.zero)
				{
					bool isInZone = IsObjectInDropZone();
					Gizmos.color = isInZone ? Color.cyan : Color.red;
					Gizmos.DrawWireCube(objBounds.center, objBounds.size);
				}
			}

			// Draw home position indicator
			if (Application.isPlaying && isAtHome)
			{
				Vector3 toolPos = robotController.GetToolPosition();
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(toolPos, 0.05f);
			}
		}
#endif
	}
}