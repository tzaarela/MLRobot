# architecture.md — Unity Architecture & Code Contracts

> **Role of this file**
> This document defines *how the game is built* at a technical level.
> Claude Code should use this to stay consistent with the project’s architecture.

---

## 1. Architectural Goals

- High gameplay iteration speed
- Predictable data flow
- Debuggable systems
- Minimal hidden magic

---

## 2. Unity Programming Model

### Primary Paradigm
- [x] MonoBehaviour‑driven
- [x] ScriptableObject‑centric
- [ ] ECS / DOTS
- [ ] Hybrid
---

## 3. Data vs Behavior

### ScriptableObjects Are Used For:
- Configuration data
- Tunable gameplay values
- Shared references

### MonoBehaviours Are Used For:
- Runtime behavior
- Scene‑bound logic
- Controllers

---

## 4. Gameplay System Breakdown

### Input System
- Legacy

### Player/Keyboard Controller
- Responsibilities: Inputs for movement of the robot, inputs for controlling of the robots tool and control of different systems of the robot. this should talk to the robotcontroller

### Agent Controller
- Decision model: PPO
- Responsibilites: Training of the model, control of the robot using the robotcontroller.

### Robot Controller
- Responsibilites: Handling the robots movements and internal systems.

### Level Controller
- **Responsibilities**:
  - Episode lifecycle management (Idle, Running, Success, Failure states)
  - Spawn randomization (object and drop zone positions within safe bounds)
  - Episode timer tracking (30-second timeout)
  - Error count monitoring (fail at 5 errors)
  - Out-of-bounds detection (object leaves play area)
  - Drop zone confirmation timer (2-3 seconds)
  - Episode end event triggering
  - Environment reset for next episode
- **State Machine**: Episode states (Idle → Running → Success/Failure → Reset)
- **Events Fired**: OnEpisodeStart, OnEpisodeSuccess, OnEpisodeFailure, OnEpisodeReset
- **Events Listened**: RobotController errors, DropZone object entered/exited
- **Configuration**:
  - Timeout: 30 seconds
  - Error limit: 5
  - Confirmation time: 2-3 seconds
  - Spawn bounds: Table/platform area on ground plane

### Progression Controller
- **Responsibilities**:
  - Track completion times (best, average, session history)
  - Count and categorize errors by type (dropped, failed pickup, collision, timeout)
  - Calculate episode score based on time + error penalty
  - Maintain session statistics (success rate, average metrics)
  - Persist data to JSON file
  - Provide statistics API for UI components
- **Persistence**: JSON file in Application.persistentDataPath
- **Data Tracked**:
  - Best completion time
  - Average completion time
  - Session history (list of episode results)
  - Error breakdown by type
  - Success/failure counts
- **Events Listened**: LevelController episode end events
- **API**: Provides statistics to UI systems

### Spawn Manager
- **Responsibilities**:
  - Randomize object position within safe bounds
  - Randomize drop zone position within safe bounds
  - Ensure positions are within robot's reachable workspace
  - Avoid configurations requiring extreme joint angles
- **Constraints**: Ground plane table/platform area (custom safe zone)
- **Future**: Support object property variation, shapes, obstacles
- **Note**: May be implemented as subsystem of LevelController

### Training Stats Overlay (UI System)
- **Responsibilities**:
  - Real-time display of training metrics during ML training mode
  - Show: Episode count, success rate, average reward, average completion time
  - Toggle visibility based on agent behavior mode (training vs inference)
  - Update display on every episode end
- **Data Source**: ProgressionController statistics API
- **Visibility**: Only shown during ML training, hidden during player control

### Episode Summary Panel (UI System)
- **Responsibilities**:
  - Display post-episode summary screen
  - Show: Time taken, error breakdown by type, comparison to best/average
  - Provide restart/continue controls
- **Data Source**: ProgressionController statistics API
- **Trigger**: LevelController OnEpisodeSuccess or OnEpisodeFailure events
---

## 5. Event & Communication Patterns

> Avoid tight coupling.

- **Events used**: C# events
- **State Ownership**:
  - RobotController owns robot state (joint positions, tool state, movement)
  - LevelController owns episode state (running, success, failure, timer, error count)
  - ProgressionController owns historical statistics (best times, averages, session data)
- **Event Flow**:
  1. RobotController → LevelController (error events, state changes)
  2. DropZone → LevelController (object entered/exited)
  3. LevelController → ProgressionController (episode end events)
  4. ProgressionController → UI Systems (statistics updates)
- **Communication Rules**:
  - Controllers never directly reference UI
  - UI queries ProgressionController API for data
  - Events flow one direction (no circular dependencies)

---

## 6. Folder & Namespace Rules

```
Assets/
  _Project/
    Scripts/
      Core/
      Gameplay/
      Systems/
        LevelController.cs
        ProgressionController.cs
        SpawnManager.cs (or part of LevelController if subsystem)
      UI/
        TrainingStatsOverlay.cs
        EpisodeSummaryPanel.cs
        KeyboardControlsOverlay.cs
```

**Namespace Convention:**
`MLRobot.Module`

**Examples:**
- `MLRobot.Systems` for LevelController, ProgressionController
- `MLRobot.UI` for UI components
- `MLRobot.Gameplay` for gameplay-specific controllers

---

## 7. Coding Standards (Enforced)

### General
- One class per file
- No logic in constructors
- No Find() calls at runtime

### Performance
- No LINQ in Update / FixedUpdate
- Cache component references
- Object pooling where relevant

### Data Persistence
- Use JSON serialization for statistics (System.Text.Json or JsonUtility)
- Save to Application.persistentDataPath
- Async file I/O where possible (avoid blocking main thread)
- Handle file corruption gracefully (fallback to defaults)
- Save on episode end, load on game start

---

## 8. Testing & Debugging

- **Play Mode testing approach**: Both player and agent should be able to test new features in play mode
- **Debug visualizations**:
  - Raycasts and boundaries (Gizmos)
  - Spawn bounds visualization (ground plane safe zone)
  - Drop zone confirmation radius
  - Robot workspace reachability volume
  - Episode state display in scene view
- **Logging rules**:
  - Add logging for debugging purposes
  - All logging toggleable via checkboxes/inspector flags
  - Error triggers should log with clear categorization
  - Episode state transitions should be logged
  - Timer and error count events should be loggable 

---

## 9. Other

### Current Decisions
- **Input System**: Legacy Input for now (no migration planned yet)
- **Joint Configurations**: **PLANNED MIGRATION** to ScriptableObjects (currently inspector-serialized)
- **Training Visualization**: In-game overlay preferred over external tools (e.g., TensorBoard)
- **Decision Model**: PPO (already implemented)
- **Parallel Training**: Support for 4-8 training environments

### Future Features (Document Only - Do Not Implement)
- **Additional Tools**:
  - Mechanical gripper/claw
  - Welder
- **Object Detection**: Vision-based detection (image/color recognition)
- **Multi-Station Training**: Multiple robot copies in one environment for training efficiency (architecture already supports parallel environments)
- **Task Variations**: Object properties, shapes, obstacles

## 10. Change Log

- 2026‑01‑31: Initial architecture.md created
- 2026‑01‑31: Expanded system documentation (LevelController, ProgressionController, SpawnManager, UI systems), added detailed event flow, data persistence rules, debug visualization requirements, and future feature notes

