# Robot Arm ML-Agents Setup Guide

## Project Structure

```
Scripts/
├── RobotJointConfig.cs      - Joint configuration data class
├── RobotController.cs       - Main robot control (joints + magnet)
├── MagnetGripper.cs         - Magnetic gripper with 80% contact rule
├── RobotKeyboardInput.cs    - Keyboard control for manual testing
├── RobotPickAndPlaceAgent.cs - ML-Agents agent implementation
├── TrainingEnvironment.cs   - Environment manager for parallel training
└── ColorBasedObjectDetector.cs - Optional color-based object detection

Config/
└── RobotPickAndPlace.yaml   - ML-Agents training configuration
```

## Unity Setup Instructions

### Step 1: Import Scripts

1. Create a folder `Assets/Scripts/RobotArm`
2. Copy all `.cs` files into this folder
3. Wait for Unity to compile

### Step 2: Setup the Robot

1. **Add RobotController to Robot GameObject:**
   - Select your `Robot` GameObject
   - Add Component → `RobotController`

2. **Configure Joints:**
   Based on your hierarchy, configure each joint:

   | Joint | Transform | Rotation Axis | Min Angle | Max Angle |
   |-------|-----------|---------------|-----------|-----------|
   | J1 | Rig_Arm_1 | Y (0,1,0) | -180 | 180 |
   | J2 | Rig_Arm_2 | Z (0,0,1) | -92.5 | 92.5 |
   | J3 | Rig_Arm_3 | Z (0,0,1) | -182.5 | 182.5 |
   | J4 | Rig_Arm_4 | Y (0,1,0) | -360 | 360 |
   | J5 | Rig_Arm_5 | Z (0,0,1) | -125 | 125 |
   | J6 | Rig_Arm_6 | Y (0,1,0) | -360 | 360 |

   **Note:** Adjust rotation axes based on your actual model orientation. Look at the gizmo when selecting each joint to determine the correct axis.

3. **Set rotation speeds:**
   - Default: 90 degrees/second for all joints
   - Adjust per-joint in the inspector

### Step 3: Setup the Magnet Gripper

1. **Select the `Magnet` GameObject** (child of MagnetBase)
2. **Add Component → `MagnetGripper`**
3. **Configure:**
   - `Magnet Radius`: Measure your disc radius (e.g., 0.05)
   - `Detection Range`: ~0.02 (2cm)
   - `Min Contact Percentage`: 0.8 (80%)
   - `Pickup Tag`: "Pickup" (or leave empty for any)
   - `Magnet Renderer`: Assign the Magnet's MeshRenderer

4. **Link to RobotController:**
   - Select `Robot` GameObject
   - In RobotController, assign `Magnet` field to your MagnetGripper
   - Assign `Tool Center Point` to the Magnet transform

### Step 4: Setup the Target Cube

1. **Select your `Cube` GameObject**
2. **Add/Configure Rigidbody:**
   - Mass: ~1
   - Use Gravity: ✓
   - Is Kinematic: ✗
3. **Add Tag:**
   - Create tag "Pickup" if it doesn't exist
   - Assign "Pickup" tag to the Cube
4. **Ensure it has a Collider** (BoxCollider recommended)

### Step 5: Setup Drop Zone

1. **Create an empty GameObject** named "DropZone"
2. **Position it** where you want objects to be delivered
3. **Optional:** Add a visual indicator (cylinder, plane with green material)

### Step 6: Setup the ML Agent

1. **Create empty GameObject** named "TrainingEnvironment"
2. **Parent your Robot, Cube, and DropZone under it**
3. **Add to Robot:**
   - `Behavior Parameters` component (from ML-Agents)
   - `RobotPickAndPlaceAgent` component

4. **Configure Behavior Parameters:**
   - Behavior Name: `RobotPickAndPlace`
   - Vector Observation Space Size: `27`
   - Actions:
     - Continuous Actions: `6`
     - Discrete Branches: `1` with size `2`
   - Model: Leave empty for training, assign after training

5. **Configure RobotPickAndPlaceAgent:**
   - Robot Controller: Your RobotController
   - Target Object: The Cube
   - Drop Off Zone: The DropZone
   - Environment Root: The TrainingEnvironment

### Step 7: Keyboard Controls (for Testing)

1. **Add `RobotKeyboardInput`** to the Robot GameObject
2. **Assign the RobotController**
3. **Controls:**
   - `Q/W/E/R/T/Y` - Select joint 1-6
   - `↑/↓` - Rotate selected joint
   - `Space` - Toggle magnet
   - `Backspace` - Reset robot

### Step 8: Parallel Training Setup

1. **Add `TrainingEnvironment`** component to your environment root
2. **Create a new GameObject** named "TrainingManager"
3. **Add `ParallelTrainingManager`** component
4. **Configure:**
   - Template Environment: Your TrainingEnvironment
   - Grid X/Z: Number of copies (e.g., 3x3)
   - Spacing: Distance between environments (e.g., 5)

## Training

### Prerequisites
- ML-Agents Package installed via Package Manager
- Python ML-Agents (`pip install mlagents`)

### Start Training

```bash
# From your project folder
mlagents-learn Config/RobotPickAndPlace.yaml --run-id=RobotArm_Run1

# Then press Play in Unity
```

### Monitor Training

```bash
tensorboard --logdir results
```

### Resume Training

```bash
mlagents-learn Config/RobotPickAndPlace.yaml --run-id=RobotArm_Run1 --resume
```

## Tips for Success

1. **Start simple:** Train with one robot first, then scale up
2. **Reward shaping:** The current rewards encourage:
   - Approaching the object
   - Picking it up
   - Delivering to drop zone
   - Efficiency (step penalty)

3. **Curriculum learning:** Consider starting with the cube close to the robot, then gradually increasing distance

4. **Observation debugging:** Enable Gizmos to visualize what the robot "sees"

5. **Joint limits:** Make sure your joint limits match your physical model to avoid impossible configurations

## Common Issues

**Robot doesn't move:**
- Check that joint transforms are assigned
- Verify rotation axes match your model

**Magnet doesn't pick up:**
- Verify cube has "Pickup" tag
- Check that cube has Rigidbody
- Verify contact percentage in debug UI

**Training doesn't converge:**
- Try reducing learning rate
- Add more parallel environments
- Adjust reward scales
- Enable curiosity reward signal

## Next Steps

- Add camera sensor for visual observations
- Implement curriculum learning
- Add obstacle avoidance
- Train for different object shapes
