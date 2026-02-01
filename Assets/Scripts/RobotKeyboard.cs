using UnityEngine;
using Unity.MLAgents.Policies;

namespace RobotArm
{
	/// <summary>
	/// Enhanced keyboard input for robot control with both joint and Cartesian modes.
	/// 
	/// JOINT MODE (default):
	///   Q/W/E/R/T/Y - Select joint 1-6
	///   Arrow Up/Down - Rotate selected joint
	///   +/- - Increase/Decrease rotation speed
	///   Space - Toggle magnet
	///   Backspace - Reset robot
	/// 
	/// CARTESIAN MODE (press C to toggle):
	///   POSITION:
	///     W/A/S/D - Move in XZ plane (forward/left/back/right)
	///     Q/E - Move in Y axis (down/up)
	///   ORIENTATION:
	///     Arrow Up/Down - Pitch (rotate around Y)
	///     Arrow Left/Right - Yaw (rotate around Z)
	///     Numpad 7/9 - Roll (rotate around X)
	///   +/- - Increase/Decrease movement speed
	///   Space - Toggle magnet
	///   Backspace - Reset robot
	///   
	/// MODE SWITCHING:
	///   C - Toggle between Joint and Cartesian mode
	///   V - Toggle coordinate frame (World/Tool)
	///   +/- - Adjust speed (0.2x to 5.0x in 0.2x steps)
	/// </summary>
	public class RobotKeyboard: MonoBehaviour
	{
		[Header("References")]
		[Tooltip("The robot controller to drive")]
		public RobotController robotController;

		[Tooltip("Cartesian controller for XYZ movement")]
		public RobotCartesianController cartesianController;

		[Tooltip("ML Agent (optional, for reset)")]
		public RobotPickAndPlaceAgent pickAndPlaceAgent;

		[Tooltip("Training environment (for centralized reset)")]
		public TrainingEnvironment trainingEnvironment;

		[Header("Input Settings")]
		[Tooltip("Rotation speed multiplier for joint mode")]
		public float jointRotationSpeedMultiplier = 1f;

		[Tooltip("Movement speed for Cartesian mode (m/s)")]
		public float cartesianSpeedMultiplier = 1f;

		[Tooltip("Rotation speed for Cartesian orientation (rad/s)")]
		public float cartesianRotationSpeed = 1f;

		[Tooltip("Show on-screen control hints")]
		public bool showControlHints = true;

		[Header("Speed Control")]
		[Tooltip("Speed adjustment step when pressing +/- keys")]
		[Range(0.1f, 1f)]
		public float speedAdjustmentStep = 0.2f;

		[Tooltip("Minimum speed multiplier")]
		[Range(0.1f, 1f)]
		public float minSpeedMultiplier = 0.2f;

		[Tooltip("Maximum speed multiplier")]
		[Range(1f, 10f)]
		public float maxSpeedMultiplier = 5f;

		[Header("Control Mode")]
		[Tooltip("Start in Cartesian mode (false = Joint mode)")]
		public bool startInCartesianMode = false;

		// Control state
		private enum ControlMode { Joint, Cartesian }
		private enum CartesianFrame { World, Tool }

		private ControlMode currentMode = ControlMode.Joint;
		private CartesianFrame currentFrame = CartesianFrame.World;
		private int selectedJoint = 0;

		// Key mappings for joint selection
		private readonly KeyCode[] jointSelectKeys = new KeyCode[]
		{
			KeyCode.Q, // J1
			KeyCode.W, // J2
			KeyCode.E, // J3
			KeyCode.R, // J4
			KeyCode.T, // J5
			KeyCode.Y  // J6
		};

		private void Start()
		{
			// Check if in heuristic mode
			var behaviorParams = GetComponent<BehaviorParameters>();
			bool isHeuristicMode = behaviorParams != null &&
				behaviorParams.BehaviorType == BehaviorType.HeuristicOnly;

			if (!isHeuristicMode)
			{
				this.enabled = false;
				return;
			}

			// Set initial mode
			currentMode = startInCartesianMode ? ControlMode.Cartesian : ControlMode.Joint;

			// Auto-find Cartesian controller if not assigned
			if (cartesianController == null)
			{
				cartesianController = GetComponent<RobotCartesianController>();
				if (cartesianController == null)
				{
					Debug.LogWarning("[KeyboardInput] No CartesianController found. Cartesian mode disabled.");
				}
			}

			// Auto-find TrainingEnvironment if not assigned
			if (trainingEnvironment == null)
			{
				trainingEnvironment = GetComponentInParent<TrainingEnvironment>();
				if (trainingEnvironment == null)
				{
					trainingEnvironment = FindObjectOfType<TrainingEnvironment>();
				}
			}

			// Subscribe cartesian controller to environment reset event
			if (cartesianController != null && trainingEnvironment != null)
			{
				cartesianController.SubscribeToEnvironment(trainingEnvironment);
			}

			// Sync target angles if starting in Joint mode
			if (currentMode == ControlMode.Joint)
			{
				robotController.SyncTargetAnglesToCurrent();
			}

			Debug.Log($"[KeyboardInput] Initialized in {currentMode} mode - selected joint: J{selectedJoint + 1}");
		}

		private void Update()
		{
			if (robotController == null) return;

			// Mode switching
			HandleModeSwitch();

			// Speed control (works in both modes)
			HandleSpeedControl();

			// Mode-specific controls
			if (currentMode == ControlMode.Joint)
			{
				HandleJointMode();
			}
			else if (currentMode == ControlMode.Cartesian)
			{
				HandleCartesianMode();
			}

			// Common controls
			HandleMagnetControl();
			HandleResetControl();
		}

		private void HandleModeSwitch()
		{
			// Toggle control mode
			if (Input.GetKeyDown(KeyCode.C))
			{
				if (currentMode == ControlMode.Joint)
				{
					if (cartesianController != null)
					{
						currentMode = ControlMode.Cartesian;
						cartesianController.ResetTarget();
						Debug.Log("[KeyboardInput] Switched to CARTESIAN mode");
					}
					else
					{
						Debug.LogWarning("[KeyboardInput] Cannot switch to Cartesian mode - no controller!");
					}
				}
				else
				{
					currentMode = ControlMode.Joint;
					// Sync target angles to prevent smooth movement conflicts
					robotController.SyncTargetAnglesToCurrent();
					Debug.Log($"[KeyboardInput] Switched to JOINT mode - selected joint: J{selectedJoint + 1}");
				}
			}

			// Toggle coordinate frame (Cartesian mode only)
			if (Input.GetKeyDown(KeyCode.V) && currentMode == ControlMode.Cartesian)
			{
				currentFrame = currentFrame == CartesianFrame.World ? CartesianFrame.Tool : CartesianFrame.World;
				Debug.Log($"[KeyboardInput] Coordinate frame: {currentFrame}");
			}
		}

		private void HandleSpeedControl()
		{
			// Increase speed with + or = key
			if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
			{
				if (currentMode == ControlMode.Joint)
				{
					jointRotationSpeedMultiplier = Mathf.Clamp(
						jointRotationSpeedMultiplier + speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					Debug.Log($"[KeyboardInput] Joint speed: {jointRotationSpeedMultiplier:F1}x");
				}
				else
				{
					cartesianSpeedMultiplier = Mathf.Clamp(
						cartesianSpeedMultiplier + speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					cartesianRotationSpeed = Mathf.Clamp(
						cartesianRotationSpeed + speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					Debug.Log($"[KeyboardInput] Cartesian speed: {cartesianSpeedMultiplier:F1}x");
				}
			}

			// Decrease speed with - key
			if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
			{
				if (currentMode == ControlMode.Joint)
				{
					jointRotationSpeedMultiplier = Mathf.Clamp(
						jointRotationSpeedMultiplier - speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					Debug.Log($"[KeyboardInput] Joint speed: {jointRotationSpeedMultiplier:F1}x");
				}
				else
				{
					cartesianSpeedMultiplier = Mathf.Clamp(
						cartesianSpeedMultiplier - speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					cartesianRotationSpeed = Mathf.Clamp(
						cartesianRotationSpeed - speedAdjustmentStep,
						minSpeedMultiplier,
						maxSpeedMultiplier
					);
					Debug.Log($"[KeyboardInput] Cartesian speed: {cartesianSpeedMultiplier:F1}x");
				}
			}
		}

		private void HandleJointMode()
		{
			// Joint selection
			for (int i = 0; i < jointSelectKeys.Length; i++)
			{
				if (Input.GetKeyDown(jointSelectKeys[i]))
				{
					selectedJoint = i;
					Debug.Log($"[KeyboardInput] Selected J{selectedJoint + 1} ({jointSelectKeys[i]})");
				}
			}

			// Joint rotation
			float rotationInput = 0f;
			if (Input.GetKey(KeyCode.UpArrow))
			{
				rotationInput = 1f;
			}
			else if (Input.GetKey(KeyCode.DownArrow))
			{
				rotationInput = -1f;
			}

			if (!Mathf.Approximately(rotationInput, 0f))
			{
				robotController.RotateJointNormalized(
					selectedJoint,
					rotationInput * jointRotationSpeedMultiplier,
					Time.deltaTime
				);
			}
		}

		private void HandleCartesianMode()
		{
			if (cartesianController == null) return;

			Vector3 movement = Vector3.zero;
			Vector3 rotation = Vector3.zero;

			// POSITION CONTROL: WASD + QE
			if (Input.GetKey(KeyCode.W))
			{
				movement.x = -1f; // Forward
			}
			else if (Input.GetKey(KeyCode.S))
			{
				movement.x = 1f; // Back
			}

			if (Input.GetKey(KeyCode.A))
			{
				movement.z = -1f; // Left
			}
			else if (Input.GetKey(KeyCode.D))
			{
				movement.z = 1f; // Right
			}

			if (Input.GetKey(KeyCode.E))
			{
				movement.y = 1f; // Up
			}
			else if (Input.GetKey(KeyCode.Q))
			{
				movement.y = -1f; // Down
			}

			// ORIENTATION CONTROL: Arrow keys + Numpad 7/9

			// Roll (around X axis) 
			if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.Keypad8))
			{
				rotation.z = 1f; // Yaw left
			}
			else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.Keypad5))
			{
				rotation.z = -1f; // Yaw right
			}

			// Yaw (around Z axis)t
			if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.Keypad4))
			{
				rotation.x = 1f; // Roll left
			}
			else if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.Keypad6))
			{
				rotation.x = -1f; // Roll right
			}


			// Pitch (around Y axis)
			if (Input.GetKey(KeyCode.Keypad7))
			{ 
				rotation.y = 1f; // Pitch up{
			}
			else if (Input.GetKey(KeyCode.Keypad9))
			{
				rotation.y = -1f; // Pitch down
			}

			// Apply position movement
			if (movement.magnitude > 0.01f)
			{
				movement = movement.normalized * cartesianSpeedMultiplier;

				if (currentFrame == CartesianFrame.World)
				{
					cartesianController.MoveWorld(movement, Time.deltaTime);
				}
				else
				{
					cartesianController.MoveTool(movement, Time.deltaTime);
				}
			}

			// Apply orientation rotation
			if (rotation.magnitude > 0.01f)
			{
				rotation = rotation.normalized * cartesianRotationSpeed;

				// rotation is in radians
				cartesianController.RotateCartesian(rotation.x, rotation.y, rotation.z, Time.deltaTime);
			}
		}

		private void HandleMagnetControl()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				if (robotController.magnet != null)
				{
					bool wasActive = robotController.magnet.IsActive;
					robotController.magnet.SetActive(!wasActive);
					Debug.Log($"Magnet: {(!wasActive ? "ON" : "OFF")}");
				}
			}
		}

		private void HandleResetControl()
		{
			if (Input.GetKeyDown(KeyCode.Backspace))
			{
				if (pickAndPlaceAgent != null)
				{
					pickAndPlaceAgent.EndEpisode();
					Debug.Log("Robot EndEpisode");
				}
				else if (trainingEnvironment != null)
				{
					// Use centralized environment reset
					trainingEnvironment.ResetEnvironment();
				}
				else
				{
					// Fallback: manual reset if no training environment found
					robotController.ResetToStartPosition();
					if (cartesianController != null)
					{
						cartesianController.ResetTarget();
					}
					Debug.Log("Robot Reset (fallback)");
				}
			}
		}

		private void OnGUI()
		{
			if (!showControlHints) return;

			GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
			{
				richText = true,
				fontSize = 12,
				fontStyle = FontStyle.Bold
			};

			GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
			{
				richText = true,
				fontSize = 11
			};

			GUILayout.BeginArea(new Rect(10, 10, 450, 600));
			GUILayout.BeginVertical("box");

			// Title
			GUILayout.Label("<b>Robot Control System</b>", headerStyle);
			GUILayout.Space(5);

			// Current mode indicator
			string modeColor = currentMode == ControlMode.Joint ? "#FFD700" : "#00FFFF";
			GUILayout.Label($"<color={modeColor}>■</color> <b>Mode: {currentMode}</b>", labelStyle);

			if (currentMode == ControlMode.Cartesian)
			{
				GUILayout.Label($"   Frame: <b>{currentFrame}</b>", labelStyle);
			}

			// Speed indicator
			float currentSpeed = currentMode == ControlMode.Joint ? jointRotationSpeedMultiplier : cartesianSpeedMultiplier;
			string speedColor = currentSpeed > 2f ? "#FF6666" : currentSpeed > 1f ? "#FFD700" : "#66FF66";
			GUILayout.Label($"   Speed: <color={speedColor}><b>{currentSpeed:F1}x</b></color>", labelStyle);

			GUILayout.Space(10);

			// Mode switching controls
			GUILayout.Label("<b>Mode Switching:</b>", headerStyle);
			GUILayout.Label("  <b>C</b> - Toggle Joint/Cartesian mode", labelStyle);
			if (currentMode == ControlMode.Cartesian)
			{
				GUILayout.Label("  <b>V</b> - Toggle World/Tool frame", labelStyle);
			}
			GUILayout.Label("  <b>+/-</b> - Increase/Decrease speed", labelStyle);

			GUILayout.Space(10);

			// Mode-specific controls
			if (currentMode == ControlMode.Joint)
			{
				GUILayout.Label("<b>Joint Mode Controls:</b>", headerStyle);

				for (int i = 0; i < jointSelectKeys.Length; i++)
				{
					string prefix = (i == selectedJoint) ? "<color=#00FF00>►</color> " : "   ";
					float angle = robotController?.GetJointAngle(i) ?? 0f;
					GUILayout.Label($"{prefix}<b>{jointSelectKeys[i]}</b>: Joint {i + 1} ({angle:F1}°)", labelStyle);
				}

				GUILayout.Space(5);
				GUILayout.Label("  <b>↑/↓</b>: Rotate selected joint", labelStyle);
			}
			else if (currentMode == ControlMode.Cartesian)
			{
				GUILayout.Label("<b>Cartesian Mode Controls:</b>", headerStyle);

				Vector3 toolPos = robotController?.GetToolPosition() ?? Vector3.zero;
				Quaternion toolRot = robotController?.GetToolRotation() ?? Quaternion.identity;
				Vector3 euler = toolRot.eulerAngles;

				GUILayout.Label($"  Position: ({toolPos.x:F3}, {toolPos.y:F3}, {toolPos.z:F3})", labelStyle);
				GUILayout.Label($"  Rotation: ({euler.x:F1}°, {euler.y:F1}°, {euler.z:F1}°)", labelStyle);

				GUILayout.Space(5);
				GUILayout.Label("<b>Position (WASD+QE):</b>", headerStyle);
				GUILayout.Label("  <b>A/D</b>: Move Left/Right", labelStyle);
				GUILayout.Label("  <b>W/S</b>: Move Forward/Back", labelStyle);
				GUILayout.Label("  <b>Q/E</b>: Move Down/Up", labelStyle);

				GUILayout.Space(5);
				GUILayout.Label("<b>Orientation (Arrows+Numpad):</b>", headerStyle);
				GUILayout.Label("  <b>↑/↓</b>: Pitch (Y axis)", labelStyle);
				GUILayout.Label("  <b>←/→</b>: Yaw (Z axis)", labelStyle);
				GUILayout.Label("  <b>Numpad 7/9</b>: Roll (X axis)", labelStyle);
			}

			GUILayout.Space(10);

			// Common controls
			GUILayout.Label("<b>Common Controls:</b>", headerStyle);
			bool magnetActive = robotController?.magnet?.IsActive ?? false;
			bool isHolding = robotController?.IsHoldingObject() ?? false;

			string magnetStatus = magnetActive ? "<color=#00FF00>ON</color>" : "<color=#FF0000>OFF</color>";
			string holdingStatus = isHolding ? "<color=#00FF00>Yes</color>" : "<color=#808080>No</color>";

			GUILayout.Label($"  <b>Space</b>: Toggle magnet ({magnetStatus})", labelStyle);
			GUILayout.Label($"  <b>Backspace</b>: Reset robot", labelStyle);
			GUILayout.Label($"  Holding object: {holdingStatus}", labelStyle);

			GUILayout.EndVertical();
			GUILayout.EndArea();
		}
	}
}