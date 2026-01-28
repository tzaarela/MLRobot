using UnityEngine;

namespace RobotArm
{
	/// <summary>
	/// Handles keyboard input for manual robot control.
	/// QWERTY selects axes 1-6, Arrow Up/Down rotates the selected axis.
	/// Space toggles the magnet.
	/// </summary>
	public class RobotKeyboardInput: MonoBehaviour
	{
		[Header("References")]
		[Tooltip("The robot controller to drive")]
		public RobotController robotController;
		public RobotPickAndPlaceAgent pickAndPlaceAgent;


		[Header("Input Settings")]
		[Tooltip("Rotation speed multiplier for keyboard control")]
		public float rotationSpeedMultiplier = 1f;

		[Tooltip("Show on-screen control hints")]
		public bool showControlHints = true;

		// Currently selected joint (0-5)
		private int selectedJoint = 0;

		// Key mappings
		private readonly KeyCode[] jointSelectKeys = new KeyCode[]
		{
			KeyCode.Q, // J1
            KeyCode.W, // J2
            KeyCode.E, // J3
            KeyCode.R, // J4
            KeyCode.T, // J5
            KeyCode.Y  // J6
        };

		private void Update()
		{
			if (robotController == null) return;

			HandleJointSelection();
			HandleJointRotation();
			HandleMagnetControl();
			HandleResetControl();
		}

		private void HandleJointSelection()
		{
			for (int i = 0; i < jointSelectKeys.Length; i++)
			{
				if (Input.GetKeyDown(jointSelectKeys[i]))
				{
					selectedJoint = i;
					Debug.Log($"Selected Joint {i + 1} ({jointSelectKeys[i]})");
				}
			}
		}

		private void HandleJointRotation()
		{
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
				robotController.RotateJointNormalized(selectedJoint, rotationInput * rotationSpeedMultiplier, Time.deltaTime);
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
				pickAndPlaceAgent.EndEpisode();
				
				Debug.Log("Robot EndPisode");
			}
		}

		private void OnGUI()
		{
			if (!showControlHints) return;

			GUILayout.BeginArea(new Rect(10, 10, 300, 250));
			GUILayout.BeginVertical("box");

			GUILayout.Label("<b>Robot Keyboard Controls</b>", new GUIStyle(GUI.skin.label) { richText = true });
			GUILayout.Space(5);

			for (int i = 0; i < jointSelectKeys.Length; i++)
			{
				string prefix = (i == selectedJoint) ? "► " : "   ";
				float angle = robotController?.GetJointAngle(i) ?? 0f;
				GUILayout.Label($"{prefix}{jointSelectKeys[i]}: Joint {i + 1} ({angle:F1}°)");
			}

			GUILayout.Space(5);
			GUILayout.Label("↑/↓: Rotate selected joint");
			GUILayout.Label($"Space: Toggle magnet ({(robotController?.magnet?.IsActive ?? false ? "ON" : "OFF")})");
			GUILayout.Label("Backspace: Reset robot");

			if (robotController?.magnet != null)
			{
				GUILayout.Space(5);
				var magnet = robotController.magnet;
				GUILayout.Label($"Contact: {magnet.CurrentContactPercentage * 100:F0}%");
				GUILayout.Label($"Holding: {(magnet.IsHoldingObject ? "Yes" : "No")}");
			}

			GUILayout.EndVertical();
			GUILayout.EndArea();
		}
	}
}