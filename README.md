# ML-Robot

A 6-axis robot arm simulation built in Unity where you can control the robot manually or train an ML agent to perform pick-and-place tasks.

## Overview

Control a 6-axis robot arm with a magnetic gripper to pick up objects and place them in a drop zone. Either play it yourself or let a trained ML-Agents model handle it.

![Unity 2022.3.50f1](https://img.shields.io/badge/Unity-2022.3.50f1-black) ![ML-Agents](https://img.shields.io/badge/ML--Agents-PPO-blue) ![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

<img width="1919" height="1032" alt="Screenshot 2026-02-17 113802" src="https://github.com/user-attachments/assets/67c725e9-ec04-4029-88bb-5ae5848e6565" />


## Gameplay Loop

1. Episode starts with randomized object and drop zone positions
2. Control the robot arm to pick up the object and place it in the drop zone
3. Object must stay in the drop zone for 2-3 seconds to confirm success
4. Episode ends on success, timeout (30s), object out of bounds, or 5 errors

## Control Modes

### Joint Mode
Direct control of individual joints using number keys 1-6.

### Cartesian Mode
Intuitive 3D position and orientation control using inverse kinematics.

| Input | Action |
|-------|--------|
| WASD / Q/E | Move tool tip in 3D space |
| Arrow keys | Rotate tool orientation |
| Numpad 7/9 | Rotate J6 |
| Tab | Toggle world/tool reference frame |
| Space | Toggle Magnet |
| V | Toggle joint/cartesion mode |
| F9 | Hide Reward Frame |
| Numpad 1/2/3 | Different Cameras |


## ML Training

The agent uses PPO (Proximal Policy Optimization) via Unity ML-Agents. Training supports multiple parallel environments.

To train:
```bash
mlagents-learn.exe Training\RobotPickAndPlace.yaml --run-id={runId}
```

## Requirements

- Unity 2022.3.50f1
- Universal Render Pipeline (URP)
- [ML-Agents](https://github.com/Unity-Technologies/ml-agents) package
