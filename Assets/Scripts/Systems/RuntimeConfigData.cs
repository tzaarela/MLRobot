namespace MLRobot.Systems
{
    [System.Serializable]
    public class RuntimeConfigData
    {
        public AgentConfig agent = new AgentConfig();
        public TrainingConfig training = new TrainingConfig();
    }

    [System.Serializable]
    public class AgentConfig
    {
        // Default MUST match inspector default in RobotPickAndPlaceAgent.cs
        public int maxEpisodeSteps = 5000;
    }

    [System.Serializable]
    public class TrainingConfig
    {
        // Defaults MUST match inspector defaults in ParallelTrainingManager.cs
        public int gridX = 4;
        public int gridZ = 2;
        public float spacing = 5f;
    }
}
