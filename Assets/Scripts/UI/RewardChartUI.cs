using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MLRobot.UI
{
	/// <summary>
	/// Canvas-based UI showing stacked bar chart of rewards by category.
	/// Displays positive and negative rewards as separate horizontal bars.
	/// </summary>
	public class RewardChartUI : MonoBehaviour
	{
		[Header("References")]
		[Tooltip("The RewardTracker to visualize")]
		[SerializeField] private RewardTracker rewardTracker;

		[Header("UI Elements")]
		[SerializeField] private RectTransform positiveBarContainer;
		[SerializeField] private RectTransform negativeBarContainer;
		[SerializeField] private RectTransform legendContainer;
		[SerializeField] private Text totalRewardText;
		[SerializeField] private Text titleText;

		[Header("Prefabs")]
		[SerializeField] private GameObject barSegmentPrefab;
		[SerializeField] private GameObject legendItemPrefab;

		[Header("Settings")]
		[SerializeField] private float maxBarWidth = 300f;
		[SerializeField] private float rewardScaleFactor = 10f;

		// Category colors
		private static readonly Dictionary<RewardCategory, Color> CategoryColors = new Dictionary<RewardCategory, Color>
		{
			{ RewardCategory.Efficiency, new Color(0.502f, 0.502f, 0.502f) },  // Gray
			{ RewardCategory.Approach, new Color(0.298f, 0.686f, 0.314f) },    // Green
			{ RewardCategory.Alignment, new Color(0.129f, 0.588f, 0.953f) },   // Blue
			{ RewardCategory.GoalAlignment, new Color(1f, 1f, 1f )}, 		   //black
			{ RewardCategory.Pickup, new Color(1f, 0.596f, 0f) },              // Orange
			{ RewardCategory.Delivery, new Color(0.612f, 0.153f, 0.690f) },    // Purple
			{ RewardCategory.Placement, new Color(0f, 0.737f, 0.831f) },       // Cyan
			{ RewardCategory.DropFall, new Color(0.957f, 0.263f, 0.212f) },    // Red
			{ RewardCategory.Home, new Color(1f, 0.922f, 0.231f) },            // Yellow
			{ RewardCategory.Success, new Color(0.545f, 0.765f, 0.290f) }      // Bright Green
		};

		// Runtime UI elements
		private Dictionary<RewardCategory, Image> positiveSegments = new Dictionary<RewardCategory, Image>();
		private Dictionary<RewardCategory, Image> negativeSegments = new Dictionary<RewardCategory, Image>();
		private Dictionary<RewardCategory, Text> legendValues = new Dictionary<RewardCategory, Text>();

		private void Start()
		{
			if (rewardTracker == null)
			{
				Debug.LogWarning("[RewardChartUI] No RewardTracker assigned");
				return;
			}

			CreateUI();

			rewardTracker.OnRewardAdded += OnRewardAdded;
			rewardTracker.OnTrackingReset += OnTrackingReset;
		}

		private void OnDestroy()
		{
			if (rewardTracker != null)
			{
				rewardTracker.OnRewardAdded -= OnRewardAdded;
				rewardTracker.OnTrackingReset -= OnTrackingReset;
			}
		}

		private void CreateUI()
		{
			foreach (RewardCategory category in Enum.GetValues(typeof(RewardCategory)))
			{
				Color color = CategoryColors[category];

				// Positive segment
				if (positiveBarContainer != null && barSegmentPrefab != null)
				{
					GameObject posSegment = Instantiate(barSegmentPrefab, positiveBarContainer);
					Image posImage = posSegment.GetComponent<Image>();
					if (posImage != null)
					{
						posImage.color = color;
						positiveSegments[category] = posImage;
					}
					posSegment.SetActive(false);
				}

				// Negative segment
				if (negativeBarContainer != null && barSegmentPrefab != null)
				{
					GameObject negSegment = Instantiate(barSegmentPrefab, negativeBarContainer);
					Image negImage = negSegment.GetComponent<Image>();
					if (negImage != null)
					{
						negImage.color = color;
						negativeSegments[category] = negImage;
					}
					negSegment.SetActive(false);
				}

				// Legend item
				if (legendContainer != null && legendItemPrefab != null)
				{
					GameObject legendItem = Instantiate(legendItemPrefab, legendContainer);
					legendItem.SetActive(true);

					Image colorSwatch = legendItem.transform.Find("ColorSwatch")?.GetComponent<Image>();
					if (colorSwatch != null)
						colorSwatch.color = color;

					Text labelText = legendItem.transform.Find("Label")?.GetComponent<Text>();
					if (labelText != null)
						labelText.text = category.ToString();

					Text valueText = legendItem.transform.Find("Value")?.GetComponent<Text>();
					if (valueText != null)
					{
						valueText.text = "0.00";
						legendValues[category] = valueText;
					}
				}
			}

			UpdateDisplay();
		}

		private void OnRewardAdded(RewardCategory category, float amount)
		{
			UpdateDisplay();
		}

		private void OnTrackingReset()
		{
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			if (rewardTracker == null) return;

			var totals = rewardTracker.GetCategoryTotals();

			float positiveTotal = 0f;
			float negativeTotal = 0f;

			foreach (var kvp in totals)
			{
				if (kvp.Value > 0)
					positiveTotal += kvp.Value;
				else
					negativeTotal += Mathf.Abs(kvp.Value);
			}

			// Update positive bar segments
			float posOffset = 0f;
			foreach (RewardCategory category in Enum.GetValues(typeof(RewardCategory)))
			{
				float value = totals[category];

				if (positiveSegments.TryGetValue(category, out Image posImage))
				{
					if (value > 0)
					{
						posImage.gameObject.SetActive(true);
						float width = positiveTotal > 0
							? maxBarWidth * (value / positiveTotal)
							: 0f;
						width = Mathf.Max(width, 2f);

						RectTransform rt = posImage.rectTransform;
						rt.anchorMin = new Vector2(0, 0);
						rt.anchorMax = new Vector2(0, 1);
						rt.pivot = new Vector2(0, 0.5f);
						rt.anchoredPosition = new Vector2(posOffset, 0);
						rt.sizeDelta = new Vector2(width, 0);

						posOffset += width;
					}
					else
					{
						posImage.gameObject.SetActive(false);
					}
				}
			}

			// Update negative bar segments
			float negOffset = 0f;
			foreach (RewardCategory category in Enum.GetValues(typeof(RewardCategory)))
			{
				float value = totals[category];

				if (negativeSegments.TryGetValue(category, out Image negImage))
				{
					if (value < 0)
					{
						negImage.gameObject.SetActive(true);
						float absValue = Mathf.Abs(value);
						float width = negativeTotal > 0
							? maxBarWidth * (absValue / negativeTotal)
							: 0f;
						width = Mathf.Max(width, 2f);

						RectTransform rt = negImage.rectTransform;
						rt.anchorMin = new Vector2(0, 0);
						rt.anchorMax = new Vector2(0, 1);
						rt.pivot = new Vector2(0, 0.5f);
						rt.anchoredPosition = new Vector2(negOffset, 0);
						rt.sizeDelta = new Vector2(width, 0);

						negOffset += width;
					}
					else
					{
						negImage.gameObject.SetActive(false);
					}
				}
			}

			// Update legend values
			foreach (RewardCategory category in Enum.GetValues(typeof(RewardCategory)))
			{
				if (legendValues.TryGetValue(category, out Text valueText))
				{
					float value = totals[category];
					valueText.text = value >= 0 ? $"+{value:F2}" : $"{value:F2}";
				}
			}

			// Update total
			if (totalRewardText != null)
			{
				float total = rewardTracker.TotalReward;
				totalRewardText.text = $"TOTAL: {(total >= 0 ? "+" : "")}{total:F2}";
			}
		}

		/// <summary>
		/// Set the RewardTracker reference at runtime.
		/// </summary>
		public void SetRewardTracker(RewardTracker tracker)
		{
			if (rewardTracker != null)
			{
				rewardTracker.OnRewardAdded -= OnRewardAdded;
				rewardTracker.OnTrackingReset -= OnTrackingReset;
			}

			rewardTracker = tracker;

			if (rewardTracker != null)
			{
				rewardTracker.OnRewardAdded += OnRewardAdded;
				rewardTracker.OnTrackingReset += OnTrackingReset;
			}

			UpdateDisplay();
		}

		/// <summary>
		/// Get the color for a reward category.
		/// </summary>
		public static Color GetCategoryColor(RewardCategory category)
		{
			return CategoryColors.TryGetValue(category, out Color color) ? color : Color.white;
		}
	}
}
