using UnityEngine;

namespace RobotArm
{
	/// <summary>
	/// Visualizes the drop zone bounds in both editor and game mode.
	/// Attach this to your drop zone GameObject.
	/// </summary>
	public class DropZoneVisualizer : MonoBehaviour
	{
		[Header("Visualization Settings")]
		[Tooltip("Size of the drop zone bounding box")]
		public Vector3 dropZoneSize = new Vector3(1.1f, 1.1f, 1.1f);

		[Tooltip("Show wireframe in game mode")]
		public bool showInGameMode = true;

		[Tooltip("Show wireframe in editor")]
		public bool showInEditor = true;

		[Header("Colors")]
		[Tooltip("Default color when no cube is within bounds")]
		public Color startColor = Color.green;

		[Tooltip("Color when robot is holding cube within bounds (with good alignment)")]
		public Color holdingCubeWithinBounds = Color.yellow;

		[Tooltip("Color when cube is dropped within bounds (with good alignment)")]
		public Color cubeDroppedWithinBounds = Color.cyan;

		[Tooltip("Line thickness for runtime visualization")]
		[Range(0.001f, 0.05f)]
		public float lineThickness = 0.01f;

		private RobotPickAndPlaceAgent agent;

		private void OnDrawGizmos()
		{
			if (!showInEditor) return;

			DrawBounds();
		}

		private void DrawBounds()
		{
			// Draw wireframe
			Gizmos.color = startColor;
			Gizmos.DrawWireCube(transform.position, dropZoneSize);
		}

		private LineRenderer lineRenderer;

		private void Start()
		{
			if (showInGameMode)
			{
				SetupLineRenderer();
			}

		}

		private void SetupLineRenderer()
		{
			// Create LineRenderer if it doesn't exist
			lineRenderer = GetComponent<LineRenderer>();
			if (lineRenderer == null)
			{
				lineRenderer = gameObject.AddComponent<LineRenderer>();
			}

			// Configure LineRenderer
			lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
			lineRenderer.startColor = startColor;
			lineRenderer.endColor = startColor;
			lineRenderer.startWidth = lineThickness;
			lineRenderer.endWidth = lineThickness;
			lineRenderer.useWorldSpace = true;
			lineRenderer.loop = true;

			// Create the wireframe box with LineRenderer
			UpdateLineRenderer();
		}

		private void Update()
		{
			if (lineRenderer != null)
			{
				UpdateLineRenderer();
			}
		}

		private void UpdateLineRenderer()
		{
			Vector3 center = transform.position;
			Vector3 halfSize = dropZoneSize * 0.5f;

			// 12 edges of a box = 24 vertices (each edge needs 2 vertices)
			// We'll draw it as a continuous line by ordering vertices cleverly
			Vector3[] positions = new Vector3[16];

			// Bottom square
			positions[0] = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
			positions[1] = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
			positions[2] = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
			positions[3] = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
			positions[4] = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z); // close bottom

			// Up to top-left-back
			positions[5] = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);

			// Top square
			positions[6] = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
			positions[7] = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
			positions[8] = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);
			positions[9] = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z); // close top

			// Remaining vertical edges
			positions[10] = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
			positions[11] = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
			positions[12] = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
			positions[13] = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
			positions[14] = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);
			positions[15] = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);

			lineRenderer.positionCount = positions.Length;
			lineRenderer.SetPositions(positions);

			if (agent == null)
			{
				TrainingEnvironment env = GetComponentInParent<TrainingEnvironment>();
				if (env != null)
				{
					agent = env.agent;
				}
			}

			// Determine color based on state
			Color targetColor = GetDropZoneColor();

			lineRenderer.startColor = targetColor;
			lineRenderer.endColor = targetColor;
		}

		/// <summary>
		/// Determine drop zone color based on cube state and alignment
		/// </summary>
		private Color GetDropZoneColor()
		{
			if (agent == null || agent.targetObject == null)
				return startColor;

			bool objectInZone = agent.IsObjectInDropZone();

			if (!objectInZone)
				return startColor;

			// Object is in zone - check yaw alignment (matches agent's CalculateYawDifference logic)
			Vector3 objectForward = Vector3.ProjectOnPlane(agent.targetObject.forward, Vector3.up);
			Vector3 goalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
			float yawDiff = (objectForward.sqrMagnitude < 0.001f || goalForward.sqrMagnitude < 0.001f)
				? 180f
				: Vector3.Angle(objectForward.normalized, goalForward.normalized);
			bool goodAlignment = yawDiff <= agent.alignmentToleranceForTimer;

			if (!goodAlignment)
				return startColor; // In zone but misaligned - show default color

			// In zone with good alignment - check if holding or dropped
			bool isHolding = agent.robotController.IsHoldingObject();

			if (isHolding)
				return holdingCubeWithinBounds;
			else
				return cubeDroppedWithinBounds;
		}

		private void OnValidate()
		{
			// Update LineRenderer when values change in inspector
			if (lineRenderer != null && Application.isPlaying)
			{
				lineRenderer.startColor = startColor;
				lineRenderer.endColor = startColor;
				lineRenderer.startWidth = lineThickness;
				lineRenderer.endWidth = lineThickness;
				UpdateLineRenderer();
			}
		}
	}
}
