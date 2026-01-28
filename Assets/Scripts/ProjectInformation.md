Project Introduction:

Hi, im building a Unity URP project with MLAgents. I have zero code at the moment.
But I have imported and modified a "6-axis robot" model. All pieces are moveable in the way it's supposed to work. But rather than animating it. I want to agents to try and learn how to pick up a box and move it into target zone.  The tool on the robot works just like a magnet or gripping with suction, so the end of the tool is just like a disc laying down and acting like a magnet on the local Vector3.Up axis. The magnet can be turned on and off at any moment. But atleast 80% of the disc-face needs to be in contact with the cube to be able to lift it up.

If it's possible to detect the cube by image or color or something cool that would be nice.  

Im probably going to add more stations with copies of the same robot in one environment, to add more effectiveness to ml training. So lets keep our code suited to this.

We really need to nail down the code for controlling 6-axis on the robot. And lets just go with some basic observations to begin with, we can add more later.

I want to be able to control each axis with the keyboard by toggling QWERTY each axis with QWERTY->Axis1-6 and pressing Arrow Up or ArrowDown to rotate the axis both ways.

The agent knows about the cube dropoff zone position, so it doesn't have to scan for that.

Lets separate the controller logic of the robot from the agent script itself. So a player or an ai can easily switch control. 

Lets try that for now. 



Additional info about all the joints/6-axis. (J1 == The bottom one):

J1: ~±180° (360° total). Axis: Y
J2: ~185° total swing. Axis: Z
J3: ~365° total swing. Axis: Z
J4: ~720° total (two full rotations). Axis: Y
J5: ~250° total. Axis: Z
J6: ~720° total (two full rotations) All six axes are rotational.  Axis: X (revolute) — no prismatic joints.

Hirearchy in unity:

Each joint is a child of the previous one.

Robot → Armature → Rig_Arm_1 → Rig_Arm_2 → Rig_Arm_3 → Rig_Arm_4 → Rig_Arm_5 → Rig_Arm_6 → Rig_Arm_7_Tool → MagnetBase → Magnet
