namespace MLRobot.UI
{
	/// <summary>
	/// Categories for grouping rewards in the visualization UI.
	/// </summary>
	public enum RewardCategory
	{
		Efficiency,   // Step penalty
		Approach,     // Tool-to-object distance, ray alignment
		Alignment,    // Magnet alignment, upright, rotation alignment
		GoalAlignment,// Alignment towards goal
		Pickup,       // One-time pickup reward
		Delivery,     // Object-to-goal distance, lift progress
		Placement,    // Dropped in zone, in-zone per-step
		DropFall,     // Drop penalty, height penalties, fall penalties
		Home,         // Home arrival, at-home, approach, return bonus
		Success       // Completion bonus
	}
}
