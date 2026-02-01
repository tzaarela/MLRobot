# Movement Modes Setup Guide

## Overview

The ML-Agent now supports 4 different movement modes:

1. **Joint** - Direct joint angle control (traditional)
2. **Cartesian: World** - IK-based movement in world coordinates
3. **Cartesian: Tool** - IK-based movement in tool/local coordinates
4. **Auto** - Agent learns to switch between the 3 modes

## Unity Inspector Configuration

### 1. Agent Component Setup

On your `RobotPickAndPlaceAgent` GameObject:

- **Robot Controller**: Assign your `RobotController` component
- **Cartesian Controller**: Assign your `RobotCartesianController` component
- **Movement Mode**: Choose from dropdown:
  - `Joint` - Fixed joint control mode
  - `CartesianWorld` - Fixed world-space Cartesian mode
  - `CartesianTool` - Fixed tool-space Cartesian mode
  - `Auto` - Agent can switch modes dynamically

### 2. Behavior Parameters Configuration

Configure the `Behavior Parameters` component:

#### Vector Observation Space
- **Space Size**: `30`

#### Actions
- **Continuous Actions**: `6`
- **Discrete Branches**: `2`
  - Branch 0 (Size): `2` (magnet: off/on)
  - Branch 1 (Size): `3` (mode: Joint/CartesianWorld/CartesianTool)

**Important**: Even if not using Auto mode, you must configure 2 discrete branches. The mode selection branch is simply ignored in non-Auto modes.

## Movement Mode Details

### Joint Mode
- **Actions**: 6 continuous values = joint angle deltas
- **Range**: -1 to 1 (scaled by joint rotation speed, typically 90 deg/s)
- **Effective Speed**: Action of 1.0 → ~1.8 degrees per step (at 50Hz)
- **Best for**: Fine control, singularity-free movement
- **Limitations**: Harder to learn direct Cartesian paths

### Cartesian: World Mode
- **Actions**: 6 continuous values = [deltaX, deltaY, deltaZ, deltaRoll, deltaPitch, deltaYaw]
- **Coordinate Frame**: World space (fixed global coordinates)
- **Action Scaling**:
  - Position actions multiplied by `cartesianPositionActionScale` (default: 50)
  - Orientation actions multiplied by `cartesianOrientationActionScale` (default: 20)
  - This scaling is needed because base Cartesian speeds are much slower than joint speeds
- **Effective Speed**: Action of 1.0 → ~10cm position, ~40° orientation per step (at 50Hz with default scaling)
- **Best for**: Straight-line movements, positional tasks
- **Limitations**: IK may fail near singularities or workspace limits

### Cartesian: Tool Mode
- **Actions**: 6 continuous values = [deltaX, deltaY, deltaZ, deltaRoll, deltaPitch, deltaYaw]
- **Coordinate Frame**: Tool/local space (relative to current tool orientation)
- **Action Scaling**: Same as Cartesian World mode
- **Best for**: Tool-centric tasks, approach movements
- **Limitations**: IK may fail near singularities or workspace limits

### Auto Mode
- **Actions**: Same 6 continuous + mode selection discrete action
- **Agent Decides**: Which mode to use at each step
- **Best for**: Complex tasks requiring mode switching strategies
- **Training**: Longer training time, but potentially more robust policy

## Action Scaling for Cartesian Modes

**Why scaling is needed:**
- ML-Agents outputs actions in range [-1, 1]
- Base Cartesian speeds (linearSpeed: 0.1 m/s, angularSpeed: 1.5 rad/s) are designed for smooth keyboard control
- Without scaling, action of 1.0 would only move 2mm per step - too small for learning!

**Tuning the scaling:**
- Increase `cartesianPositionActionScale` if robot moves too slowly in Cartesian mode
- Decrease if robot jerks around or IK frequently fails
- Typical range: 20-100 for position, 10-50 for orientation
- Balance: Higher = faster learning, but more IK failures and instability

## Training Configuration Examples

### Example 1: Train with Joint Mode Only
```
Movement Mode: Joint
```
Train with default PPO config. Faster convergence, proven approach.

### Example 2: Train with Cartesian World Mode
```
Movement Mode: CartesianWorld
```
Requires CartesianController properly configured with IK settings.

### Example 3: Train with Auto Mode
```
Movement Mode: Auto
```
Agent learns when to use Joint vs Cartesian modes. Add mode-switching reward if desired.

## Troubleshooting

### "Cartesian mode selected but no CartesianController assigned!"
- Ensure `RobotCartesianController` component exists on the agent GameObject
- Assign it in the Inspector under "Cartesian Controller"

### IK Failures in Cartesian Modes
- Check workspace limits in `RobotCartesianController`
- Adjust `maxReachDistance`, `minReachDistance`, `groundPlaneHeight`
- Increase `dampingFactor` for better singularity handling

### Agent Not Switching Modes in Auto
- Check that observation space includes mode (one-hot encoding)
- Verify discrete action branch 1 is configured with size 3
- Consider adding mode diversity reward during training

## Keyboard Testing

Use `RobotKeyboardInputEnhanced` for manual testing:
- Press **C** to toggle Joint/Cartesian mode
- Press **V** to toggle World/Tool frame (in Cartesian mode)
- This helps verify all modes work before training

## Observations Breakdown (Total: 30)

1. **Joint Angles** (6 values): Normalized joint positions
2. **Tool State** (7 values): TCP position, orientation, distance to drop zone
3. **Object State** (7 values): Target position, orientation, distance to TCP
4. **Goal State** (4 values): Drop zone position, distance to goal
5. **Gripper State** (3 values): Magnet on/off, holding object, can activate
6. **Current Mode** (3 values): One-hot [isJoint, isCartesianWorld, isCartesianTool]

The current mode observation allows the agent to condition its policy on which mode it's using.
