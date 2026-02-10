using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using MLRobot.UI;

public class SetupRewardUI
{
	public static string Execute()
	{
		// Find Robot GameObject
		var robot = GameObject.Find("TrainingEnvironment/Robot");
		if (robot == null)
			return "ERROR: Could not find TrainingEnvironment/Robot";

		var agent = robot.GetComponent<RobotArm.RobotPickAndPlaceAgent>();
		if (agent == null)
			return "ERROR: Robot has no RobotPickAndPlaceAgent component";

		// Add RewardTracker to Robot if not already present
		var tracker = robot.GetComponent<RewardTracker>();
		if (tracker == null)
		{
			tracker = robot.AddComponent<RewardTracker>();
			Debug.Log("[SetupRewardUI] Added RewardTracker to Robot");
		}

		// Wire tracker's agent reference via SerializedObject
		var trackerSO = new SerializedObject(tracker);
		var agentProp = trackerSO.FindProperty("agent");
		if (agentProp != null)
		{
			agentProp.objectReferenceValue = agent;
			trackerSO.ApplyModifiedProperties();
			Debug.Log("[SetupRewardUI] Wired RewardTracker.agent -> RobotPickAndPlaceAgent");
		}

		// Wire agent's rewardTracker reference via SerializedObject
		var agentSO = new SerializedObject(agent);
		var rewardTrackerProp = agentSO.FindProperty("rewardTracker");
		if (rewardTrackerProp != null)
		{
			rewardTrackerProp.objectReferenceValue = tracker;
			agentSO.ApplyModifiedProperties();
			Debug.Log("[SetupRewardUI] Wired Agent.rewardTracker -> RewardTracker");
		}
		else
		{
			return "ERROR: Could not find 'rewardTracker' field on agent. Check compilation.";
		}

		// Create Canvas at scene root
		var existingCanvas = GameObject.Find("RewardChartCanvas");
		if (existingCanvas != null)
		{
			Object.DestroyImmediate(existingCanvas);
		}

		var canvasGO = new GameObject("RewardChartCanvas");
		var canvas = canvasGO.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 100;
		var scaler = canvasGO.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920, 1080);
		canvasGO.AddComponent<GraphicRaycaster>();

		// Create main panel (top-right, 350x340)
		var panelGO = CreateUIElement("RewardPanel", canvasGO.transform);
		var panelRT = panelGO.GetComponent<RectTransform>();
		panelRT.anchorMin = new Vector2(1, 1);
		panelRT.anchorMax = new Vector2(1, 1);
		panelRT.pivot = new Vector2(1, 1);
		panelRT.anchoredPosition = new Vector2(-10, -10);
		panelRT.sizeDelta = new Vector2(350, 340);
		var panelImg = panelGO.AddComponent<Image>();
		panelImg.color = new Color(0, 0, 0, 0.75f);

		// Add vertical layout
		var panelLayout = panelGO.AddComponent<VerticalLayoutGroup>();
		panelLayout.padding = new RectOffset(10, 10, 8, 8);
		panelLayout.spacing = 4;
		panelLayout.childForceExpandWidth = true;
		panelLayout.childForceExpandHeight = false;
		panelLayout.childControlWidth = true;
		panelLayout.childControlHeight = true;

		// Title
		var titleGO = CreateTextElement("Title", panelGO.transform, "EPISODE REWARDS", 16, FontStyle.Bold, TextAnchor.MiddleCenter);
		var titleLE = titleGO.AddComponent<LayoutElement>();
		titleLE.preferredHeight = 24;

		// --- Positive bar section ---
		var posLabelGO = CreateTextElement("PosLabel", panelGO.transform, "POSITIVE:", 11, FontStyle.Normal, TextAnchor.MiddleLeft);
		var posLabelLE = posLabelGO.AddComponent<LayoutElement>();
		posLabelLE.preferredHeight = 16;

		var posBarContainerGO = CreateUIElement("PositiveBarContainer", panelGO.transform);
		var posBarRT = posBarContainerGO.GetComponent<RectTransform>();
		var posBarImg = posBarContainerGO.AddComponent<Image>();
		posBarImg.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
		var posBarLE = posBarContainerGO.AddComponent<LayoutElement>();
		posBarLE.preferredHeight = 30;

		// --- Negative bar section ---
		var negLabelGO = CreateTextElement("NegLabel", panelGO.transform, "NEGATIVE:", 11, FontStyle.Normal, TextAnchor.MiddleLeft);
		var negLabelLE = negLabelGO.AddComponent<LayoutElement>();
		negLabelLE.preferredHeight = 16;

		var negBarContainerGO = CreateUIElement("NegativeBarContainer", panelGO.transform);
		var negBarRT = negBarContainerGO.GetComponent<RectTransform>();
		var negBarImg = negBarContainerGO.AddComponent<Image>();
		negBarImg.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
		var negBarLE = negBarContainerGO.AddComponent<LayoutElement>();
		negBarLE.preferredHeight = 30;

		// --- Legend section ---
		var legendLabelGO = CreateTextElement("LegendLabel", panelGO.transform, "LEGEND:", 11, FontStyle.Normal, TextAnchor.MiddleLeft);
		var legendLabelLE = legendLabelGO.AddComponent<LayoutElement>();
		legendLabelLE.preferredHeight = 16;

		var legendContainerGO = CreateUIElement("LegendContainer", panelGO.transform);
		var legendLayout = legendContainerGO.AddComponent<GridLayoutGroup>();
		legendLayout.cellSize = new Vector2(160, 18);
		legendLayout.spacing = new Vector2(4, 2);
		legendLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		legendLayout.constraintCount = 2;
		legendLayout.childAlignment = TextAnchor.UpperLeft;
		var legendLE = legendContainerGO.AddComponent<LayoutElement>();
		legendLE.preferredHeight = 95;

		// --- Total ---
		var totalGO = CreateTextElement("TotalReward", panelGO.transform, "TOTAL: +0.00", 14, FontStyle.Bold, TextAnchor.MiddleCenter);
		var totalLE = totalGO.AddComponent<LayoutElement>();
		totalLE.preferredHeight = 22;

		// --- Create bar segment prefab template (hidden) ---
		var barSegmentPrefab = CreateUIElement("BarSegmentTemplate", canvasGO.transform);
		barSegmentPrefab.AddComponent<Image>();
		var segRT = barSegmentPrefab.GetComponent<RectTransform>();
		segRT.anchorMin = new Vector2(0, 0);
		segRT.anchorMax = new Vector2(0, 1);
		segRT.pivot = new Vector2(0, 0.5f);
		barSegmentPrefab.SetActive(false);

		// --- Create legend item prefab template (hidden) ---
		var legendItemPrefab = CreateUIElement("LegendItemTemplate", canvasGO.transform);
		var legendItemLayout = legendItemPrefab.AddComponent<HorizontalLayoutGroup>();
		legendItemLayout.spacing = 4;
		legendItemLayout.childForceExpandWidth = false;
		legendItemLayout.childForceExpandHeight = true;
		legendItemLayout.childControlWidth = true;
		legendItemLayout.childControlHeight = true;
		legendItemLayout.childAlignment = TextAnchor.MiddleLeft;

		// Color swatch
		var swatchGO = CreateUIElement("ColorSwatch", legendItemPrefab.transform);
		swatchGO.AddComponent<Image>().color = Color.white;
		var swatchLE = swatchGO.AddComponent<LayoutElement>();
		swatchLE.preferredWidth = 12;
		swatchLE.preferredHeight = 12;

		// Label
		var labelGO = CreateTextElement("Label", legendItemPrefab.transform, "Category", 10, FontStyle.Normal, TextAnchor.MiddleLeft);
		var labelLE = labelGO.AddComponent<LayoutElement>();
		labelLE.preferredWidth = 68;

		// Value
		var valueGO = CreateTextElement("Value", legendItemPrefab.transform, "0.00", 10, FontStyle.Normal, TextAnchor.MiddleRight);
		var valueLE = valueGO.AddComponent<LayoutElement>();
		valueLE.preferredWidth = 60;

		legendItemPrefab.SetActive(false);

		// --- Add RewardChartUI component and wire references ---
		var chartUI = panelGO.AddComponent<RewardChartUI>();
		var chartSO = new SerializedObject(chartUI);

		SetObjectRef(chartSO, "rewardTracker", tracker);
		SetObjectRef(chartSO, "positiveBarContainer", posBarContainerGO.GetComponent<RectTransform>());
		SetObjectRef(chartSO, "negativeBarContainer", negBarContainerGO.GetComponent<RectTransform>());
		SetObjectRef(chartSO, "legendContainer", legendContainerGO.GetComponent<RectTransform>());
		SetObjectRef(chartSO, "totalRewardText", totalGO.GetComponent<Text>());
		SetObjectRef(chartSO, "titleText", titleGO.GetComponent<Text>());
		SetObjectRef(chartSO, "barSegmentPrefab", barSegmentPrefab);
		SetObjectRef(chartSO, "legendItemPrefab", legendItemPrefab);

		chartSO.ApplyModifiedProperties();

		// Mark scene dirty
		UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
			UnityEngine.SceneManagement.SceneManager.GetActiveScene());

		Debug.Log("[SetupRewardUI] Setup complete!");
		return "SUCCESS: Reward UI setup complete. RewardTracker on Robot, Canvas with chart UI created.";
	}

	private static GameObject CreateUIElement(string name, Transform parent)
	{
		var go = new GameObject(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		return go;
	}

	private static GameObject CreateTextElement(string name, Transform parent, string text, int fontSize, FontStyle style, TextAnchor alignment)
	{
		var go = CreateUIElement(name, parent);
		var txt = go.AddComponent<Text>();
		txt.text = text;
		txt.fontSize = fontSize;
		txt.fontStyle = style;
		txt.alignment = alignment;
		txt.color = Color.white;
		txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		return go;
	}

	private static void SetObjectRef(SerializedObject so, string propName, Object value)
	{
		var prop = so.FindProperty(propName);
		if (prop != null)
		{
			prop.objectReferenceValue = value;
		}
		else
		{
			Debug.LogWarning($"[SetupRewardUI] Could not find property '{propName}'");
		}
	}
}
