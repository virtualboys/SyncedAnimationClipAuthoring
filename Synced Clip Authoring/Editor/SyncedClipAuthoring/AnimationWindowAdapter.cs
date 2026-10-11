using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace VB.SyncedClipAuthoring.Editor
{
    // All private Unity API access is contained here. Verified against Unity 6000.0.68f1.
    internal sealed class AnimationWindowAdapter
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly PropertyInfo stateProperty, rootProperty, playerProperty, controlProperty;
        private readonly FieldInfo linkedField, postprocessField;
        private readonly MethodInfo resample, overrideLoop, loopTime;
        private readonly object rebuildFlags;

        private AnimationWindowAdapter()
        {
            stateProperty = Require(typeof(AnimationWindow).GetProperty("state", Flags), "AnimationWindow.state");
            var stateType = stateProperty.PropertyType;
            rootProperty = Require(stateType.GetProperty("activeRootGameObject", Flags), "activeRootGameObject");
            playerProperty = Require(stateType.GetProperty("activeAnimationPlayer", Flags), "activeAnimationPlayer");
            controlProperty = Require(stateType.GetProperty("controlInterface", Flags), "controlInterface");
            linkedField = Require(stateType.GetField("linkedWithSequencer", Flags), "linkedWithSequencer");
            var controlType = Require(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditorInternal.AnimationWindowControl"), "AnimationWindowControl");
            var flagsType = Require(controlType.GetNestedType("ResampleFlags", BindingFlags.NonPublic), "ResampleFlags");
            rebuildFlags = Enum.Parse(flagsType, "RebuildGraph, RefreshViews, FlushUndos");
            resample = Require(controlType.GetMethod("ResampleAnimation", Flags, null, new[] { flagsType }, null), "ResampleAnimation(flags)");
            postprocessField = Require(controlType.GetField("m_UsesPostProcessComponents", Flags), "m_UsesPostProcessComponents");
            overrideLoop = Require(typeof(AnimationClipPlayable).GetMethod("SetOverrideLoopTime", Flags), "SetOverrideLoopTime");
            loopTime = Require(typeof(AnimationClipPlayable).GetMethod("SetLoopTime", Flags), "SetLoopTime");
        }

        public static bool TryCreate(out AnimationWindowAdapter adapter, out string error)
        {
            try { adapter = new AnimationWindowAdapter(); error = null; return true; }
            catch (Exception exception)
            {
                adapter = null;
                error = $"Synced Clip Authoring is unavailable in Unity {Application.unityVersion}: {exception.Message}. " +
                        "The editor integration was written for Unity 6000.0.68f1. Normal Animation window editing is unaffected.";
                return false;
            }
        }

        public bool TryGetTarget(AnimationWindow window, out GameObject root, out Animator animator, out string message)
        {
            root = null; animator = null; message = null;
            if (window == null) { message = "Open the Animation window and select an animated scene object."; return false; }
            var state = stateProperty.GetValue(window);
            if (state == null) { message = "The Animation window is initializing."; return false; }
            if ((bool)linkedField.GetValue(state))
            {
                message = "Unlink the Animation window from Timeline before enabling synced clip authorship.";
                return false;
            }
            root = rootProperty.GetValue(state) as GameObject;
            animator = playerProperty.GetValue(state) as Animator;
            if (root == null || animator == null || EditorUtility.IsPersistent(root))
            {
                message = "Select an animated scene object or open its prefab. An FBX or clip asset alone has no authoring target.";
                return false;
            }
            return true;
        }

        // Rebuild without toggling Preview: StopPreview would discard the candidate clip and pending Inspector edits.
        public void Rebuild(AnimationWindow window)
        {
            if (window == null || !window.previewing) return;
            var state = stateProperty.GetValue(window);
            var control = controlProperty.GetValue(state);
            if (!resample.DeclaringType.IsInstanceOfType(control))
                throw new InvalidOperationException("The Animation window has another sampling controller attached.");
            var root = rootProperty.GetValue(state) as GameObject;
            postprocessField.SetValue(control, root != null && root.GetComponents<IAnimationWindowPreview>().Length > 0);
            resample.Invoke(control, new[] { rebuildFlags });
        }

        public void DisableLooping(AnimationClipPlayable playable)
        {
            object boxed = playable;
            overrideLoop.Invoke(boxed, new object[] { true });
            loopTime.Invoke(boxed, new object[] { false });
        }

        private static T Require<T>(T value, string name) where T : class
        {
            return value ?? throw new MissingMemberException(name);
        }
    }
}
