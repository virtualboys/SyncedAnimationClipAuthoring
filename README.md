# Synced Animation Clip Authoring

Workflow tool to add keys of Unity only properties to animation clips without having 
to duplicate out the clip from the fbx (thus losing the ability to modify the original 
animation in Blender / Maya etc.).

This is accomplished with a synced layer in the animator controller that plays your
`sister` clip when the clip from the fbx plays. The tool allows you to preview both
clips at the same time so you can easily align unity only keys with the OG animation.

## Usage

1. Assign the animation clip from the FBX to a state in your animator controller
2. Create a new layer and set its **Sync** to the motion layer.
3. Create your `sister` animation clip for your Unity keys and drag it into the corresponding
   state in the synced layer
4. Open the Animation window and preview your `sister` clip
5. Open the tool **Tools → Animation → Synced Clip Authoring** and check **Synced Clip Authoring**
6. Now you can press the **Record** button in the animation window and key whatever while
   previewing both clips.
