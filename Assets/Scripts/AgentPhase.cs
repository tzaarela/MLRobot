namespace RobotArm
{
	/// <summary>
	/// Explicit phases of the pick-and-place task.
	/// Transitions are managed by EvaluateTransitions() in the agent.
	/// </summary>
	public enum AgentPhase
	{
		Approaching,  // Moving toward target, aligning magnet
		Delivering,   // Holding object, moving to drop zone
		Placed,       // Object in zone, timer running, returning home
		Succeeded,    // Terminal: timer completed
		Failed        // Terminal: drop outside zone, floor exploit, etc.
	}
}
