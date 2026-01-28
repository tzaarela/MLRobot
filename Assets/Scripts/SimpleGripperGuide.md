# Simple Raycast Gripper - Setup Guide

## Overview

This is a much simpler gripper that uses **4 raycasts from the corners of the disc**. All 4 rays must hit the same object to establish full contact and allow gripping.

## How It Works

```
        [Ray1]
          |
  [Ray4]--●--[Ray2]  ← Magnet disc (top view)
          |
        [Ray3]

All 4 rays shoot downward (or in magnetFaceDirection)
If all 4 hit the cube → Full Contact → Can Grip!
```

## Setup Instructions

### 1. Remove Old Gripper (if applicable)

If you have the old `MagnetGripper` component:
1. Select your **Magnet** GameObject
2. Remove the old **MagnetGripper** component
3. Add the new **SimpleRaycastGripper** component

### 2. Configure SimpleRaycastGripper

**Required Settings:**
- **Magnet Radius**: Size of disc (e.g., 0.12 for 24cm diameter)
- **Raycast Distance**: How far to cast rays (e.g., 0.02 = 2cm)
- **Magnet Face Direction**: Direction rays shoot
  - For disc facing down: (0, -1, 0)
  - For disc facing up: (0, 1, 0)

**Optional Settings:**
- **Robot Root**: Drag your Robot GameObject here (prevents self-grabbing)
- **Pickup Tag**: "Pickup" (or leave empty for any object)
- **Pickup Layer Mask**: Select which layers can be picked up

**Visual Settings:**
- **Magnet Renderer**: Drag the Magnet's MeshRenderer here
- **Colors**: Set inactive/active/holding colors

**Collision Prevention:**
- **Prevent Push Through**: Enable to push objects away instead of going through them
- **Push Force On Enter**: Force applied on initial collision (impulse, default: 10)
- **Push Force On Stay**: Continuous force while in contact (0 = disabled, default: 5)

### 3. Update RobotController Reference

If using `RobotController.cs`:

1. Open `RobotController.cs`
2. Change the magnet field type from:
   ```csharp
   public MagnetGripper magnet;
   ```
   To:
   ```csharp
   public SimpleRaycastGripper magnet;
   ```

3. Update methods that reference magnet:
   ```csharp
   public bool IsHoldingObject()
   {
       return magnet != null && magnet.IsHoldingObject;
   }
   ```

### 4. Update Agent Script

If using `RobotPickAndPlaceAgent.cs`:

The agent should work mostly as-is, but you can now use:
```csharp
// Check full contact (all 4 rays hitting)
bool fullContact = robotController.magnet.HasFullContact;

// Check partial contact
int raysHit = robotController.magnet.RaysHitting; // 0 to 4

// Add to observations if desired
sensor.AddObservation(robotController.magnet.RaysHitting / 4f); // Normalized 0-1
```

## Key Properties

### Public Properties You Can Access:

```csharp
bool IsActive              // Is magnet turned on?
bool IsHoldingObject       // Is magnet holding something?
GameObject HeldObject      // What object is held (null if none)
bool HasFullContact        // All 4 rays hitting? (required to grip)
int RaysHitting            // How many rays hit (0-4)
GameObject DetectedObject  // Object all 4 rays hit (null if not all 4)
bool IsColliding           // Is magnet colliding with something?
```

### Methods:

```csharp
SetActive(bool active)     // Turn magnet on/off
Toggle()                   // Toggle magnet state
Release()                  // Drop held object
```

## Testing

### Visual Debugging

When you select the Magnet in the scene:
- **Cyan/Gray disc** = Magnet outline
- **Green arrow** = Face normal direction
- **4 colored lines** = The raycasts
  - Green = Ray hit object
  - Red = Ray missed
- **Green cube** = Detected object (when all 4 rays hit)
- **Label** = "X/4 rays hit"

### Test Procedure

1. **Press Play**
2. **Position magnet over cube** (use keyboard controls)
3. **Watch the 4 rays**:
   - All 4 green = Full contact ✓
   - Any red = Not enough contact ✗
4. **Turn magnet on** (Space key)
5. **Should grip when all 4 rays green**

## Advantages of This Approach

✓ **Simple**: Just 4 raycasts, easy to understand
✓ **Fast**: Much faster than 25 raycasts
✓ **Clear logic**: All 4 hit = can grip, simple!
✓ **Works with any shape**: As long as 4 corners hit
✓ **Easy to debug**: Can see exactly which rays hit/miss
✓ **Collision prevention**: Pushes objects away instead of going through them

## Common Issues

### Issue: "3/4 rays hit" but won't grip
**Solution**: All 4 rays MUST hit. Try:
- Moving magnet slightly
- Increasing `raycastDistance`
- Decreasing `magnetRadius` (smaller disc = easier to get all 4)

### Issue: Rays point wrong direction
**Solution**: Change `magnetFaceDirection`
- Try (0, -1, 0) for down
- Try (0, 1, 0) for up

### Issue: Rays hit robot parts
**Solution**: Assign `robotRoot` field
- Drag your "Robot" GameObject into this field

### Issue: Won't detect cube
**Solution**: Check cube setup
- Has "Pickup" tag?
- Has Rigidbody?
- Has Collider (not trigger)?
- On correct layer?

### Issue: Magnet pushes through objects
**Solution**: Enable collision prevention
- Check **Prevent Push Through** is enabled
- Increase **Push Force On Enter** if objects not pushed away enough
- Adjust **Push Force On Stay** (or set to 0 to disable continuous force)
- Make sure magnet has a collider attached

### Issue: Objects bounce too much on collision
**Solution**: Reduce collision forces
- Decrease **Push Force On Enter** (reduces initial impulse)
- Set **Push Force On Stay** to 0 (disables continuous pushing)

## Comparison to Old Gripper

| Feature | Old Gripper | New Simple Gripper |
|---------|-------------|-------------------|
| Raycasts | 13 (5x5 grid) | 4 (corners) |
| Contact Logic | 80% threshold | All 4 must hit |
| Speed | Slower | Faster |
| Complexity | High | Low |
| Contact % | 0-100% | Binary (full or none) |

## Adjusting Difficulty

### Make Easier to Grip:
- Increase `raycastDistance` (0.05 instead of 0.02)
- Decrease `magnetRadius` (smaller disc = easier alignment)
- Increase cube size

### Make Harder to Grip:
- Decrease `raycastDistance` (0.01 instead of 0.02)
- Increase `magnetRadius` (larger disc = needs better alignment)
- Require precise positioning

## For ML Training

The agent can observe contact state:
```csharp
// Binary: has full contact or not
sensor.AddObservation(magnet.HasFullContact ? 1f : 0f);

// Analog: how many rays hitting (0-4 normalized to 0-1)
sensor.AddObservation(magnet.RaysHitting / 4f);
```

This gives the agent clear feedback on alignment quality!

## Collision Force Control

The gripper has two separate force parameters for fine control:

### Push Force On Enter (Impulse)
- Applied once when collision **starts**
- Uses `ForceMode.Impulse` (instant velocity change)
- Good for: Pushing objects away from magnet
- Default: 10
- Set to 0: No force on initial contact

### Push Force On Stay (Continuous)
- Applied every physics frame **while in contact**
- Uses `ForceMode.Force` (continuous acceleration)
- Good for: Keeping objects from sticking to magnet
- Default: 5
- **Set to 0: Disables continuous force completely**

### Common Configurations:

**Aggressive Push (default):**
```
Push Force On Enter: 10
Push Force On Stay: 5
Result: Objects pushed away and kept at distance
```

**Gentle Push:**
```
Push Force On Enter: 5
Push Force On Stay: 2
Result: Lighter touch, less bouncing
```

**Initial Push Only:**
```
Push Force On Enter: 10
Push Force On Stay: 0
Result: Objects pushed once, then no more force
Use case: When you want physics to settle naturally
```

**No Push (collision detection only):**
```
Push Force On Enter: 0
Push Force On Stay: 0
Result: Collision detected but no forces applied
Use case: When you only need IsColliding property
```

## Summary

**Old way**: 25 raycasts, 80% contact threshold, complex
**New way**: 4 raycasts, all must hit, simple!

Much easier to understand, faster, and should work great for your pick-and-place task!
