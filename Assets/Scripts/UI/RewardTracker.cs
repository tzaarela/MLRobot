using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

namespace MLRobot.UI
{
	/// <summary>
	/// Tracks rewards by category for visualization.
	/// Wraps Agent.AddReward to categorize all rewards.
	/// </summary>
	public class RewardTracker : MonoBehaviour
	{
		/// <summary>
		/// When false, AddCategorizedReward bypasses all tracking (dictionary, events)
		/// and calls Agent.AddReward directly. Toggle at runtime with F9.
		/// </summary>
		public static bool TrackingEnabled = true;

		[Header("References")]
		[Tooltip("The ML-Agent to track rewards for")]
		[SerializeField] private Agent agent;

		[Tooltip("Canvas to disable when tracking is off (optional)")]
		[SerializeField] private Canvas rewardCanvas;

		[Header("Settings")]
		[Tooltip("Whether reward tracking starts enabled on play")]
		[SerializeField] private bool startTrackingEnabled = true;

		[Header("Debug")]
		[SerializeField] private bool showDebugLogs = false;

		// Cached enum values to avoid Enum.GetValues() allocations
		private static readonly RewardCategory[] AllCategories = (RewardCategory[])Enum.GetValues(typeof(RewardCategory));

		// Category totals for current episode
		private Dictionary<RewardCategory, float> categoryTotals = new Dictionary<RewardCategory, float>();

		/// <summary>
		/// Read-only access to category totals without dictionary copy.
		/// </summary>
		public IReadOnlyDictionary<RewardCategory, float> CategoryTotals => categoryTotals;

		/// <summary>
		/// Event fired when any reward is added. Parameters: (category, amount)
		/// </summary>
		public event Action<RewardCategory, float> OnRewardAdded;

		/// <summary>
		/// Event fired when tracking is reset (new episode).
		/// </summary>
		public event Action OnTrackingReset;

		/// <summary>
		/// Total cumulative reward this episode (sum of all categories).
		/// </summary>
		public float TotalReward { get; private set; }

		private void Awake()
		{
			InitializeCategoryTotals();
			TrackingEnabled = startTrackingEnabled;
			SetCanvasEnabled(TrackingEnabled);
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.F9))
			{
				TrackingEnabled = !TrackingEnabled;
				SetCanvasEnabled(TrackingEnabled);
				Debug.Log($"[RewardTracker] Reward tracking {(TrackingEnabled ? "ENABLED" : "DISABLED")}");
			}
		}

		private void SetCanvasEnabled(bool enabled)
		{
			if (rewardCanvas != null)
				rewardCanvas.gameObject.SetActive(enabled);
		}

		private void InitializeCategoryTotals()
		{
			categoryTotals.Clear();
			for (int i = 0; i < AllCategories.Length; i++)
			{
				categoryTotals[AllCategories[i]] = 0f;
			}
			TotalReward = 0f;
		}

		/// <summary>
		/// Add a categorized reward. Tracks the reward and calls Agent.AddReward.
		/// </summary>
		public void AddCategorizedReward(RewardCategory category, float amount)
		{
			if (agent == null)
			{
				Debug.LogWarning("[RewardTracker] No agent assigned, cannot add reward");
				return;
			}

			if (!TrackingEnabled)
			{
				agent.AddReward(amount);
				return;
			}

			categoryTotals[category] += amount;
			TotalReward += amount;

			agent.AddReward(amount);

			OnRewardAdded?.Invoke(category, amount);

			if (showDebugLogs)
			{
				Debug.Log($"[RewardTracker] {category}: {amount:+0.000;-0.000} (total: {categoryTotals[category]:F3})");
			}
		}

		/// <summary>
		/// Get the current total for a specific category.
		/// </summary>
		public float GetCategoryTotal(RewardCategory category)
		{
			return categoryTotals.TryGetValue(category, out float total) ? total : 0f;
		}

		/// <summary>
		/// Get all category totals. Returns a copy to prevent external modification.
		/// </summary>
		public Dictionary<RewardCategory, float> GetCategoryTotals()
		{
			return new Dictionary<RewardCategory, float>(categoryTotals);
		}

		/// <summary>
		/// Reset all tracking. Call at the start of each episode.
		/// </summary>
		public void ResetTracking()
		{
			InitializeCategoryTotals();
			OnTrackingReset?.Invoke();

			if (showDebugLogs)
			{
				Debug.Log("[RewardTracker] Tracking reset for new episode");
			}
		}

		/// <summary>
		/// Set the agent reference at runtime.
		/// </summary>
		public void SetAgent(Agent newAgent)
		{
			agent = newAgent;
		}
	}
}
