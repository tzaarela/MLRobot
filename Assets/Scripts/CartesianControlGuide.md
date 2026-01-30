# Cartesian Linear Movement Control - Setup Guide

## Overview

This system adds **industry-standard Cartesian (XYZ) linear movement control** to your 6-axis robot arm. Instead of rotating individual joints, you can move the robot's end effector directly in 3D space - just like professional industrial robots!

## How It Works

### Industry Approach: "Cartesian Jogging"

Professional industrial robots (FANUC, ABB, KUKA, Universal Robots, etc.) all support "Cartesian Jog Mode" where operators can move the robot in straight lines along X, Y, Z axes. This is essential for:

- **Precise positioning** - Move exactly where you want
- **Intuitive control** - Think in 3D space, not joint angles  
- **Task-oriented programming** - Define movements relative to workpieces

### Technical Implementation: Jacobian-Based Inverse Kinematics

The system uses the **Jacobian pseudo-inverse method** - a numerical approach that:

1. **Calculates the Jacobian matrix** - Relates joint velocities to end-effector velocities
2. **Computes the pseudo-inverse** - Uses damped least squares to handle singularities
3. **Converts Cartesian commands to joint movements** - Iteratively solves IK

This is the **industry standard** for 6-axis robots because it:
- ✅ Works for any robot configuration
- ✅ Handles singularities gracefully
- ✅ Provides smooth, continuous motion
- ✅ Doesn't require analytical inverse kinematics

## File Structure

```
RobotCartesianController.cs        - Core IK solver for Cartesian movement
RobotKeyboardInputEnhanced.cs      - Enhanced keyboard input (Joint + Cartesian modes)
```

## Setup Instructions

### Step 1: Add Cartesian Controller

1. **Select your Robot GameObject**
2. **Add Component** → `RobotCartesianController`
3. **Configure settings:**

```
References:
  ✓ Robot Controller: (drag your RobotController here)

Cartesian Movement Settings:
  Linear Speed: 0.1 m/s (adjust for faster/slower movement)
  Max Step Size: 0.01 m (prevents large jumps)
  Damping Factor: 0.1 (higher = more stable near singularities)

Iteration Settings:
  Max Iterations: 10 (usually converges in 2-5)
  Convergence Threshold: 0.001 m

Safety Limits:
  Max Reach Distance: 2.0 m
  ✓ Enforce Workspace Limits

Debug:
  □ Show Debug Info (enable for console logs)
  ✓ Show Debug Gizmos (shows target position in scene)
```

### Step 2: Replace Keyboard Input (Optional)

If you want the enhanced keyboard controls with Cartesian mode:

1. **Remove** the old `RobotKeyboardInput` component from your Robot
2. **Add Component** → `RobotKeyboardInputEnhanced`
3. **Configure:**

```
References:
  ✓ Robot Controller: (auto-filled)
  ✓ Cartesian Controller: (auto-filled)
  ✓ Pick And Place Agent: (drag your agent here)

Input Settings:
  Joint Rotation Speed Multiplier: 1.0
  Cartesian Speed Multiplier: 1.0
  ✓ Show Control Hints

Control Mode:
  □ Start In Cartesian Mode (false = start in Joint mode)
```

### Step 3: Test the Setup

1. **Press Play** in Unity
2. **Press C** to toggle between Joint and Cartesian modes
3. **In Cartesian mode**, use:
   - **Arrow keys** to move in XZ plane
   - **PageUp/PageDown** to move forward/backward
   - **Alternative**: I/K (up/down), J/L (left/right), U/O (forward/back)

## Keyboard Controls Reference

### Mode Switching
- **C** - Toggle between Joint and Cartesian modes
- **V** - Toggle between World and Tool coordinate frames (Cartesian mode only)

### Joint Mode (Default)
- **Q/W/E/R/T/Y** - Select joint 1-6
- **Arrow Up/Down** - Rotate selected joint
- **Space** - Toggle magnet
- **Backspace** - Reset robot

### Cartesian Mode
- **Arrow Left/Right** - Move in X axis (left/right)
- **Arrow Up/Down** - Move in Z axis (up/down)
- **Page Up/Down** - Move in Y axis (forward/backward)

**Alternative WASD-style:**
- **J/L** - Move left/right (X)
- **I/K** - Move up/down (Z)
- **U/O** - Move forward/backward (Y)

**Common:**
- **Space** - Toggle magnet
- **Backspace** - Reset robot

## Coordinate Frames Explained

### World Frame
Movement is relative to the **global coordinate system**:
- **X+** = Right (when looking at robot from front)
- **Y+** = Forward
- **Z+** = Up

This is the default and most intuitive frame.

### Tool Frame
Movement is relative to the **tool's orientation**:
- **X+** = Tool's "right"
- **Y+** = Tool's "forward" 
- **Z+** = Tool's "up"

Useful when the tool is rotated and you want to move relative to it.

## Programming API

### Using Cartesian Controller in Code

```csharp
// Get reference
RobotCartesianController cartesian = GetComponent<RobotCartesianController>();

// Move in world space
cartesian.MoveWorld(new Vector3(0.1f, 0, 0), Time.deltaTime); // Move right

// Move in tool space
cartesian.MoveTool(Vector3.forward * 0.1f, Time.deltaTime); // Move tool forward

// Set absolute position
cartesian.SetTargetPosition(new Vector3(0.5f, 0.3f, 0.2f));

// Move with individual deltas
cartesian.MoveCartesian(deltaX: 0.1f, deltaY: 0, deltaZ: 0, Time.deltaTime);
```

### Integration with ML Agent

You can use Cartesian control in your ML agent:

```csharp
public class RobotPickAndPlaceAgent : Agent
{
    public RobotCartesianController cartesianController;
    
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Option 1: Use Cartesian actions (actions 0-2 for XYZ)
        float deltaX = actions.ContinuousActions[0];
        float deltaY = actions.ContinuousActions[1];
        float deltaZ = actions.ContinuousActions[2];
        
        cartesianController.MoveCartesian(deltaX, deltaY, deltaZ, Time.fixedDeltaTime);
        
        // Option 2: Keep using joint actions
        // (existing joint control code)
    }
}
```

## Performance Characteristics

### Convergence Speed
- Typical convergence: **2-5 iterations**
- Max iterations: **10** (configurable)
- Update frequency: **Every FixedUpdate** (~50 Hz)

### Accuracy
- Position error: **< 1mm** (0.001m convergence threshold)
- Repeatable and stable

### Singularity Handling
- Uses **damped least squares** (Levenberg-Marquardt)
- Damping factor prevents instability
- Gracefully degrades near singularities (may move slower but won't crash)

## Common Issues & Solutions

### Issue: Robot moves erratically
**Solution:** 
- Increase `dampingFactor` (try 0.2 or 0.3)
- Decrease `maxStepSize` (try 0.005)
- Check that joint limits are correct

### Issue: Movement is too slow
**Solution:**
- Increase `linearSpeed` (try 0.2 or 0.5)
- Increase `cartesianSpeedMultiplier` in keyboard input
- Check `maxStepSize` isn't too small

### Issue: Target position unreachable
**Solution:**
- Check `maxReachDistance` limit
- Verify target is within robot's workspace
- Enable `showDebugGizmos` to visualize workspace

### Issue: Robot gets stuck near singularities
**Solution:**
- Increase `dampingFactor` significantly (0.3-0.5)
- Move away from singularities in Joint mode
- Singularities typically occur when wrist is fully extended or joints align

### Issue: Jacobian calculation seems wrong
**Solution:**
- Verify all joint transforms are assigned correctly
- Check rotation axes are correct for each joint
- Ensure robot hierarchy is correct

## Advanced Configuration

### Tuning for Your Robot

Different robot sizes need different settings:

**Small robot (reach < 0.5m):**
```
linearSpeed: 0.05 m/s
maxStepSize: 0.005 m
dampingFactor: 0.05
```

**Medium robot (reach 0.5-1m):**
```
linearSpeed: 0.1 m/s
maxStepSize: 0.01 m
dampingFactor: 0.1
```

**Large robot (reach > 1m):**
```
linearSpeed: 0.2 m/s
maxStepSize: 0.02 m
dampingFactor: 0.2
```

### Understanding the Jacobian

The Jacobian matrix (J) relates joint velocities (θ̇) to end-effector velocities (ẋ):

```
ẋ = J * θ̇
```

For our 6-axis robot with 3 DOF end-effector (XYZ position only):
```
J is 3×6 matrix:
[∂x/∂θ₁  ∂x/∂θ₂  ∂x/∂θ₃  ∂x/∂θ₄  ∂x/∂θ₅  ∂x/∂θ₆]
[∂y/∂θ₁  ∂y/∂θ₂  ∂y/∂θ₃  ∂y/∂θ₄  ∂y/∂θ₅  ∂y/∂θ₆]
[∂z/∂θ₁  ∂z/∂θ₂  ∂z/∂θ₃  ∂z/∂θ₄  ∂z/∂θ₅  ∂z/∂θ₆]
```

We calculate this numerically using finite differences (no need for analytical derivatives).

### Damped Least Squares Explained

Standard pseudo-inverse fails near singularities. We use damped least squares:

```
J^# = J^T * (J*J^T + λ²I)^(-1)
```

Where:
- `λ` = damping factor (prevents division by near-zero)
- `I` = identity matrix
- Higher λ = more stable but less accurate
- Lower λ = more accurate but less stable near singularities

## Comparison: Cartesian vs Joint Control

| Feature | Joint Control | Cartesian Control |
|---------|--------------|-------------------|
| **Intuitive?** | ❌ No - must think in angles | ✅ Yes - think in 3D space |
| **Straight lines?** | ❌ No - curved paths | ✅ Yes - linear paths |
| **Speed** | ⚡ Fast - direct control | 🐢 Slower - IK computation |
| **Singularities?** | ✅ No issues | ⚠️ Can be problematic |
| **Training ML?** | ✅ Direct, simple | ⚠️ More complex |
| **Real-world use?** | 🔧 Low-level control | 👨‍🏭 User-friendly operation |

## Best Practices

1. **Start in Joint mode** when teaching positions
2. **Switch to Cartesian** for fine adjustments
3. **Use Tool frame** when approaching objects at angles
4. **Monitor convergence** with debug logs if issues arise
5. **Stay away from singularities** (fully extended or aligned joints)
6. **Test workspace limits** before autonomous operation

## Future Enhancements

Possible additions (not implemented yet):
- ✨ Orientation control (roll, pitch, yaw)
- ✨ Path planning (move along bezier curves)
- ✨ Collision avoidance
- ✨ Force control
- ✨ Multi-robot coordination

## References

This implementation is based on industry-standard approaches used in:
- FANUC robots (Cartesian jog mode)
- ABB robots (Linear movement)
- Universal Robots (MoveL commands)
- KUKA robots (Linear interpolation)

**Key papers/resources:**
- "Robot Modeling and Control" by Spong, Hutchinson, Vidyasagar
- "Introduction to Robotics: Mechanics and Control" by John J. Craig
- "Robotics: Modelling, Planning and Control" by Siciliano et al.

## Summary

You now have **professional-grade Cartesian control** for your robot arm! This allows you to:

✅ Move in straight lines (just like real industrial robots)
✅ Control position intuitively in 3D space
✅ Switch between Joint and Cartesian modes seamlessly
✅ Work in different coordinate frames (World/Tool)
✅ Integrate with ML training

The system uses the **Jacobian pseudo-inverse** method - the same approach used in professional robotics. It's robust, flexible, and handles the complexity of 6-axis kinematics automatically.

Happy robot controlling! 🤖
