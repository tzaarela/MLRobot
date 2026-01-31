# planning.md — Project Planning & AI Context

> **Role of this file**
> This document defines *what* we are building and *how the AI should help right now*.
> It is intentionally lightweight on low‑level architecture details (see `architecture.md`).
>
> Claude Code should treat this as the **primary source of truth**.

---

## My first draft of the project:




## 1. Project Snapshot

**Project Name:**
ML-Robot
**One‑Sentence Elevator Pitch:**
A 6-axis arm robot, controlled by the player or trained and controlled with MLAgents.
**Target Platform(s):**
Windows
**Unity Version / Render Pipeline:**
Unity 2022.3.50f1 / URP
---

## 2. Design Pillars (Gameplay‑Driven)

> These are non‑negotiable. If a suggestion violates one, it should be rejected.

1. **Gameplay Feel First** – moment‑to‑moment responsiveness matters more than systems elegance.
2. **Readable State** – the player should always understand what is happening.
3. **Iteration Speed** – features must be fast to tweak and rebalance.

---

## 3. Core Gameplay Loop

> Describe this from the player's perspective.

1. **Episode Start**: Player/Agent spawns in environment with randomized object and drop zone positions within a defined safe area (ground plane table/platform bounds)
2. **Task Execution**: Control 6-axis robot with magnetic gripper to pick up object and place it in the drop zone
3. **Feedback Systems**:
   - Real-time error tracking (dropped object, failed pickup, collision, timeout)
   - 30-second episode timer
   - Drop zone confirmation (object must stay for 2-3 seconds)
4. **Episode End**:
   - Success: Object confirmed in drop zone → Show time and error summary with comparison to best/average
   - Failure: Timeout, out-of-bounds, or 5-error limit reached → Show failure reason and stats
5. **Loop Repeats**: New episode with fresh randomized object/drop zone positions 

---

## 4. Player Abilities & Systems (High Level)

### Player/Agent Capabilities
- Movement of all 6-axis of the robot. 
- Toggling of the tool on and off. The first tool will be a magnet.

### Core Systems
- **RobotController**: Handles all movement. Both agent and player hook up to this.
- **LevelController**: Manages episode lifecycle, spawn randomization, timeout tracking, error monitoring, out-of-bounds detection, and drop zone confirmation
- **ProgressionController**: Tracks completion times (best/average/session), categorizes errors by type, calculates scores, persists data to JSON
- **Agent System** (ML-Agent): Training functionality using PPO decision model
- **Keyboard Controller**: Human player robot control
- **ToolController**: Magnetic gripper toggle
- **UI Systems**:
  - Keyboard controls overlay (during play)
  - Episode summary screen (after episode end)
  - Training stats overlay (real-time metrics during ML training)
---

## 5. Current Development Phase

**Phase:** Vertical Slice - Core Systems Complete

**Remaining Work:**
- Implement LevelController (episode lifecycle, spawn randomization)
- Implement ProgressionController (stats tracking, JSON persistence)
- Implement UI systems (summary screen, training overlay)
- Migrate joint configurations to ScriptableObjects

---

## 6. Episode Configuration

### Time & Limits
- **Episode Timeout**: 30 seconds
- **Error Limit**: 5 errors (episode fails when reached)
- **Success Confirmation**: Object must stay in drop zone for 2-3 seconds

### Failure Conditions
1. **Timeout**: Exceeded 30-second limit
2. **Out of Bounds**: Object leaves defined play area (table/platform)
3. **Error Limit**: Accumulated 5 errors during episode

### Error Types
1. **Dropped Object**: Object was picked up but fell before placement in drop zone
2. **Failed Pickup**: Attempted to grab object but missed
3. **Collision**: Robot arm collided with ground or obstacles
4. **Timeout Error**: Counted when episode times out

### Spawn & Randomization
- **Positions Randomized**: Object spawn position and drop zone position
- **Safe Zone**: Ground plane table/platform bounds (custom defined area)
- **Constraints**: Keep within robot's reachable workspace, avoid extreme joint angles
- **Future Variations**: Object properties, shapes, obstacles

---

## 7. AI ASSISTANT INSTRUCTIONS (Claude Code)

> This section is authoritative. Claude must follow it strictly.

### Role
You are a **senior Unity gameplay engineer** assisting an experienced developer.

### Expectations
- Prioritize gameplay clarity over abstraction
- Optimize only when required or requested
- Prefer Unity‑idiomatic solutions

### When Requirements Are Unclear
- Ask **Max three concise clarifying question** before writing code
- Do not invent mechanics or rules

### Output Rules
- Provide complete, compilable C# scripts
- Use clear comments explaining intent
- State assumptions explicitly

---

## 8. Change Log

- 2026‑01‑31: Initial planning.md created
- 2026‑01‑31: Added detailed episode configuration, expanded core gameplay loop, documented missing systems (LevelController, ProgressionController, UI), updated development phase status

