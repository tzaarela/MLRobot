using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

namespace RobotArm
{
	/// <summary>
	/// ML-Agents agent for robot arm pick and place task.
	/// Learns to pick up a cube and place it in a target zone.
	/// </summary>
	public class RobotPickAndPlaceAgent: Agent
	{
		[Header("References")]
		[Tooltip("The robot controller this agent controls")]
		public RobotController robotController;

		[Tooltip("The object to pick up")]
		public Transform targetObject;

		[Tooltip("The drop-off zone")]
		public Transform dropOffZone;

		[Header("Environment")]
		[Tooltip("Parent transform for this training instance (for parallel training)")]
		public Transform environmentRoot;

		[Tooltip("Spawn area for the target object")]
		public Vector3 objectSpawnAreaMin = new Vector3(-0.5f, 0.1f, -0.5f);
		public Vector3 objectSpawnAreaMax = new Vector3(0.5f, 0.1f, 0.5f);

		[Header("Reward Settings")]
		[Tooltip("Reward for successfully placing object in drop zone")]
		public float successReward = 1.0f;

		[Tooltip("Penalty for dropping object outside drop zone")]
		public float dropPenalty = -0.5f;

		[Tooltip("Reward scale for getting closer to object")]
		public float approachRewardScale = 0.01f;

		[Tooltip("Reward for picking up the object")]
		public float pickupReward = 0.5f;

		[Tooltip("Reward scale for bringing object closer to goal")]
		public float deliveryRewardScale = 0.02f;

		[Tooltip("Small penalty per step to encourage efficiency")]
		public float stepPenalty = -0.0002f;

		[Header("Episode Settings")]
		[Tooltip("Maximum steps before episode ends")]
		public int maxEpisodeSteps = 5000;

		[Tooltip("Height below which object is considered fallen")]
		public float objectFallThreshold = 0.05f;

		[Header("Debug")]
		public bool showDebugLogs = true;

		// State tracking
		private Rigidbody targetRigidbody;
		private Vector3 initialObjectPosition;
		private Quaternion initialObjectRotation;
		private float previousDistanceToObject;
		private float previousDistanceToGoal;
		private bool hasPickedUp = false;
		private bool wasHolding = false;
		private int currentStep = 0;

		public override void Initialize()
		{
			if (targetObject != null)
			{
				targetRigidbody = targetObject.GetComponent<Rigidbody>();
				initialObjectPosition = GetLocalPosition(targetObject.position);
				initialObjectRotation = targetObject.rotation;
			}
		}

		public override void OnEpisodeBegin()
		{
			currentStep = 0;
			hasPickedUp = false;
			wasHolding = false;

			// Reset robot
			robotController.ResetToStartPosition();

			// Reset and randomize object position
			ResetTargetObject();

			// Cache initial distances
			previousDistanceToObject = Vector3.Distance(robotController.GetToolPosition(), targetObject.position);
			previousDistanceToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);
		}

		private void ResetTargetObject()
		{
			if (targetObject == null || targetRigidbody == null) return;

			// Release if held
			if (robotController.IsHoldingObject())
			{
				robotController.SetMagnetActive(false);
			}

			// Randomize position within spawn area
			Vector3 randomLocalPos = new Vector3(
				Random.Range(objectSpawnAreaMin.x, objectSpawnAreaMax.x),
				Random.Range(objectSpawnAreaMin.y, objectSpawnAreaMax.y),
				Random.Range(objectSpawnAreaMin.z, objectSpawnAreaMax.z)
			);

			// Randomize y-rotation of object
			targetObject.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

			// Convert to world position relative to environment root
			Vector3 worldPos = GetWorldPosition(randomLocalPos);

			// Reset physics
			targetRigidbody.velocity = Vector3.zero;
			targetRigidbody.angularVelocity = Vector3.zero;
			targetObject.position = worldPos;
			targetObject.rotation = initialObjectRotation;
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

			// Tool orientation (as forward and up vectors for stability)
			Vector3 toolUp = robotController.GetMagnetFaceNormal();
			sensor.AddObservation(toolUp);

			//vector between drop off and magnet
			sensor.AddObservation(dropOffZone.position - toolPos);

			// === Target Object State (7 values) ===
			// Object position
			Vector3 objectPos = GetLocalPosition(targetObject.position);
			sensor.AddObservation(objectPos);

			// Object Rotation
			sensor.AddObservation(targetObject.up.y); // “is it upright?”

			// Vector from tool to object
			Vector3 toolToObject = targetObject.position - robotController.GetToolPosition();
			sensor.AddObservation(toolToObject.normalized);

			// Distance to object (normalized)
			float distToObject = toolToObject.magnitude;
			sensor.AddObservation(Mathf.Clamp01(distToObject / 2f));

			// === Goal State (4 values) ===
			// Drop zone position
			Vector3 goalPos = GetLocalPosition(dropOffZone.position);
			sensor.AddObservation(goalPos);

			// Distance from object to goal (normalized)
			float distToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);
			sensor.AddObservation(Mathf.Clamp01(distToGoal / 2f));

			// === Gripper State (3 values) ===
			sensor.AddObservation(robotController.magnet.IsActive ? 1f : 0f);
			sensor.AddObservation(robotController.IsHoldingObject() ? 1f : 0f);

			// Total: 6 + 7 + 7 + 4 + 3 = 27 observations
		}

		public override void OnActionReceived(ActionBuffers actions)
		{
			currentStep++;

			// Get continuous actions for joint movement
			float[] jointDeltas = new float[6];
			for (int i = 0; i < 6; i++)
			{
				jointDeltas[i] = actions.ContinuousActions[i];
			}

			// Apply joint movements
			robotController.ApplyJointDeltas(jointDeltas, Time.fixedDeltaTime);

			// Discrete action for magnet (0 = off, 1 = on)
			// Only control magnet if not in Heuristic mode (let keyboard handle it in Heuristic)
			if (GetComponent<Unity.MLAgents.Policies.BehaviorParameters>().BehaviorType != Unity.MLAgents.Policies.BehaviorType.HeuristicOnly)
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
			// Step penalty for efficiency
			AddReward(stepPenalty);

			float currentDistToObject = Vector3.Distance(robotController.GetToolPosition(), targetObject.position);
			float currentDistToGoal = Vector3.Distance(targetObject.position, dropOffZone.position);

			bool isHolding = robotController.IsHoldingObject();

			// Phase 1: Approaching object (when not holding)
			if (!isHolding)
			{
				// Reward for getting closer to object
				float approachDelta = previousDistanceToObject - currentDistToObject;

				if (approachDelta > 0) 
				{
					AddReward(0.05f);
				}
				else
				{
					AddReward(-0.55f);
				}

				if (previousDistanceToGoal > currentDistToGoal)
				{
					AddReward(-0.1f);      
				}

				// Bonus for good alignment (magnet facing down toward object)
				float alignment = Vector3.Dot(robotController.GetMagnetFaceNormal(),
					(targetObject.position - robotController.GetToolPosition()).normalized);

				if (alignment > 0.8f) 
					AddReward(0.2f);
				else if (alignment > 0.6f)
					AddReward(0.1f);

				// Debug logging every 100 steps
				if (showDebugLogs && currentStep % 100 == 0)
				{
					Vector3 toolPos = robotController.GetToolPosition();
					Vector3 objPos = targetObject.position;
					Debug.Log($"[Step {currentStep}] ToolPos: {toolPos}, ObjPos: {objPos}, Dist: {currentDistToObject:F3}, PrevDist: {previousDistanceToObject:F3}, Delta: {approachDelta:F4}");
				}

				float alignmentReward = robotController.magnet.RaysHitting * 0.05f;
				AddReward(alignmentReward);
			}

			// Pickup reward (one-time)
			if (isHolding && !wasHolding)
			{
				AddReward(pickupReward);
				hasPickedUp = true;
			}

			// Phase 2: Delivering object (when holding)
			if (isHolding)
			{
				AddReward(0.5f);
				// Reward for bringing object closer to goal
				float deliveryDelta = previousDistanceToGoal - currentDistToGoal;

				if (deliveryDelta > 0)
				{
					AddReward(0.5f);
				}
				else
				{
					AddReward(-0.55f);
				}
			}

			// Dropped object after picking up
			if (wasHolding && !isHolding && hasPickedUp)
			{
				// Check if dropped in goal zone
				if (IsObjectInDropZone())
				{
					AddReward(successReward);
					EndEpisode();
				}
				else
				{
					AddReward(dropPenalty);
				}
			}

			// Update tracking
			previousDistanceToObject = currentDistToObject;
			previousDistanceToGoal = currentDistToGoal;
			wasHolding = isHolding;
		}

		private void CheckEpisodeEnd()
		{
			// Success: Object in drop zone and released
			if (IsObjectInDropZone() && !robotController.IsHoldingObject() && hasPickedUp)
			{
				AddReward(successReward);
				EndEpisode();
				return;
			}

			// Failure: Object fell off table
			if (targetObject.position.y < objectFallThreshold)
			{
				AddReward(dropPenalty);
				EndEpisode();
				return;
			}

			// Timeout
			if (currentStep >= maxEpisodeSteps)
			{
				EndEpisode();
			}
		}

		private bool IsObjectInDropZone()
		{
			// Simple distance check - you could use a collider trigger instead
			float horizontalDist = Vector3.Distance(
				new Vector3(targetObject.position.x, 0, targetObject.position.z),
				new Vector3(dropOffZone.position.x, 0, dropOffZone.position.z)
			);

			return horizontalDist < 0.15f; // Adjust based on your drop zone size
		}

		public override void Heuristic(in ActionBuffers actionsOut)
		{
			// This allows the keyboard controller to work alongside ML training
			// The keyboard input is handled separately by RobotKeyboardInput
			// Here we just provide neutral actions

			var continuousActions = actionsOut.ContinuousActions;
			for (int i = 0; i < 6; i++)
			{
				continuousActions[i] = 0f;
			}

			var discreteActions = actionsOut.DiscreteActions;
			discreteActions[0] = robotController.magnet.IsActive ? 1 : 0;
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

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			// Draw spawn area
			if (environmentRoot != null)
			{
				Gizmos.color = Color.yellow;
				Vector3 center = environmentRoot.TransformPoint((objectSpawnAreaMin + objectSpawnAreaMax) / 2f);
				Vector3 size = objectSpawnAreaMax - objectSpawnAreaMin;
				Gizmos.DrawWireCube(center, size);
			}

			// Draw drop zone
			if (dropOffZone != null)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(dropOffZone.position, 0.15f);
			}
		}
#endif
	}
}