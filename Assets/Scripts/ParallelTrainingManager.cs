using System.Collections.Generic;
using UnityEngine;

namespace RobotArm
{

	/// <summary>
	/// Manager for creating and managing multiple parallel training environments.
	/// </summary>
	public class ParallelTrainingManager: MonoBehaviour
	{
		[Header("Environment Template")]
		[Tooltip("The template environment to duplicate")]
		public TrainingEnvironment templateEnvironment;

		[Header("Grid Settings")]
		[Tooltip("Number of environments in X direction")]
		public int gridX = 2;

		[Tooltip("Number of environments in Z direction")]
		public int gridZ = 2;

		[Tooltip("Spacing between environments")]
		public float spacing = 5f;

		[Header("Options")]
		[Tooltip("Create environments on start")]
		public bool createOnStart = true;

		[Tooltip("Disable template after creating copies")]
		public bool disableTemplate = true;

		private List<TrainingEnvironment> environments = new List<TrainingEnvironment>();

		private void Start()
		{
			if (createOnStart)
			{
				CreateEnvironments();
			}
		}

		[ContextMenu("Create Environments")]
		public void CreateEnvironments()
		{
			if (templateEnvironment == null)
			{
				Debug.LogError("No template environment assigned!");
				return;
			}

			// Clear existing (except template)
			foreach (var env in environments)
			{
				if (env != null && env != templateEnvironment)
				{
					Destroy(env.gameObject);
				}
			}
			environments.Clear();

			// Create grid of environments
			Vector3 startPos = templateEnvironment.transform.position;

			for (int x = 0; x < gridX; x++)
			{
				for (int z = 0; z < gridZ; z++)
				{
					// Skip the first position if we're keeping the template
					if (x == 0 && z == 0 && !disableTemplate)
					{
						environments.Add(templateEnvironment);
						continue;
					}

					Vector3 position = startPos + new Vector3(x * spacing, 0, z * spacing);
					TrainingEnvironment env = TrainingEnvironment.Duplicate(templateEnvironment, position);
					env.name = $"TrainingEnvironment_{x}_{z}";
					environments.Add(env);
				}
			}

			if (disableTemplate)
			{
				templateEnvironment.gameObject.SetActive(false);
			}

			Debug.Log($"Created {environments.Count} training environments");
		}

		[ContextMenu("Clear Environments")]
		public void ClearEnvironments()
		{
			foreach (var env in environments)
			{
				if (env != null && env != templateEnvironment)
				{
					if (Application.isPlaying)
						Destroy(env.gameObject);
					else
						DestroyImmediate(env.gameObject);
				}
			}
			environments.Clear();

			if (templateEnvironment != null)
			{
				templateEnvironment.gameObject.SetActive(true);
			}
		}
	}
}