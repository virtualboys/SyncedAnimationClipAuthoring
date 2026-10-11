using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace VB.SyncedClipAuthoring.Editor
{
    internal sealed class SyncedClipPair
    {
        public AnimatorController Controller;
        public AnimatorState State;
        public string StatePath;
        public int SourceLayer;
        public int SyncedLayer;
        public AnimationClip SourceClip;
        public AnimationClip SyncedClip;
        public AnimatorLayerBlendingMode SourceBlend;
        public AnimatorLayerBlendingMode SyncedBlend;
        public AvatarMask SourceMask;
        public AvatarMask SyncedMask;

        public string Key => $"{Controller.GetInstanceID()}:{State.GetInstanceID()}:{SourceLayer}:{SyncedLayer}";
        public string Label { get; internal set; }
        public bool DurationsMatch => Mathf.Abs(SourceClip.length - SyncedClip.length) <= 0.0001f;
        public AnimationClip Sister(AnimationClip selected) => selected == SourceClip ? SyncedClip : SourceClip;

        public bool HasSameGraphSettings(SyncedClipPair other)
        {
            return other != null && Key == other.Key && SourceClip == other.SourceClip &&
                   SyncedClip == other.SyncedClip && SourceBlend == other.SourceBlend &&
                   SyncedBlend == other.SyncedBlend && SourceMask == other.SourceMask && SyncedMask == other.SyncedMask;
        }
    }

    internal sealed class SyncedClipResolution
    {
        public readonly List<SyncedClipPair> Pairs = new List<SyncedClipPair>();
        public string Message;
    }

    internal static class SyncedClipResolver
    {
        public static SyncedClipResolution Resolve(RuntimeAnimatorController runtimeController, AnimationClip selected)
        {
            var result = new SyncedClipResolution();
            if (runtimeController == null || selected == null)
            {
                result.Message = "Select a clip on an object with an Animator Controller in the Animation window.";
                return result;
            }

            var overrides = new List<AnimatorOverrideController>();
            var visited = new HashSet<RuntimeAnimatorController>();
            while (runtimeController is AnimatorOverrideController replacement && visited.Add(runtimeController))
            {
                overrides.Add(replacement);
                runtimeController = replacement.runtimeAnimatorController;
            }
            if (!(runtimeController is AnimatorController controller))
            {
                result.Message = "The Animator must use an Animator Controller (optionally through an Override Controller).";
                return result;
            }

            // Resolve inner overrides first. Some nested override assets still expose the original clip as their key.
            overrides.Reverse();
            var maps = new List<Dictionary<AnimationClip, AnimationClip>>();
            foreach (var replacement in overrides)
            {
                var entries = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                replacement.GetOverrides(entries);
                var map = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var entry in entries)
                    if (entry.Key != null) map[entry.Key] = entry.Value;
                maps.Add(map);
            }

            AnimationClip EffectiveClip(AnimationClip original)
            {
                var clip = original;
                if (clip == null) return null;
                foreach (var map in maps)
                {
                    if (map.TryGetValue(clip, out var replacement) && replacement != null)
                        clip = replacement;
                    else if (map.TryGetValue(original, out replacement) && replacement != null)
                        clip = replacement;
                }
                return clip;
            }

            var layers = controller.layers;
            bool foundBlendTree = false, foundMissing = false, foundSameClip = false;
            for (int synced = 0; synced < layers.Length; synced++)
            {
                int source = layers[synced].syncedLayerIndex;
                if (source < 0 || source >= layers.Length || source == synced) continue;
                int root = source;
                var chain = new HashSet<int> { synced };
                while (root >= 0 && root < layers.Length && chain.Add(root) && layers[root].syncedLayerIndex >= 0)
                    root = layers[root].syncedLayerIndex;
                if (root < 0 || root >= layers.Length || layers[root].syncedLayerIndex >= 0) continue;

                foreach (var entry in States(layers[root].stateMachine, layers[root].name))
                {
                    Motion sourceMotion = controller.GetStateEffectiveMotion(entry.state, source);
                    Motion syncedMotion = controller.GetStateEffectiveMotion(entry.state, synced);
                    var sourceClip = EffectiveClip(sourceMotion as AnimationClip);
                    var syncedClip = EffectiveClip(syncedMotion as AnimationClip);
                    bool selectedInTree = Contains(sourceMotion, selected, EffectiveClip) || Contains(syncedMotion, selected, EffectiveClip);
                    if (sourceClip != selected && syncedClip != selected && !selectedInTree) continue;
                    if (sourceMotion is BlendTree || syncedMotion is BlendTree)
                    {
                        foundBlendTree = true;
                        continue;
                    }
                    if (sourceClip == null || syncedClip == null)
                    {
                        foundMissing = true;
                        continue;
                    }
                    if (sourceClip == syncedClip)
                    {
                        foundSameClip = true;
                        continue;
                    }
                    if (sourceClip.legacy || syncedClip.legacy)
                    {
                        result.Message = "Legacy clips cannot use the Animation window sampling graph.";
                        continue;
                    }

                    result.Pairs.Add(new SyncedClipPair
                    {
                        Controller = controller, State = entry.state, StatePath = entry.path,
                        SourceLayer = source, SyncedLayer = synced, SourceClip = sourceClip, SyncedClip = syncedClip,
                        SourceBlend = layers[source].blendingMode, SyncedBlend = layers[synced].blendingMode,
                        SourceMask = layers[source].avatarMask, SyncedMask = layers[synced].avatarMask,
                        Label = $"{entry.path}  ({layers[source].name} + {layers[synced].name})"
                    });
                }
            }

            if (result.Pairs.Count == 0 && result.Message == null)
                result.Message = foundBlendTree ? "This pairing uses a Blend Tree. Synced Clip Authoring supports a single clip per state." :
                    foundMissing ? "The matching synced state has no sister motion assigned." :
                    foundSameClip ? "The synced state uses the same clip as its source. Assign a distinct sister clip in the controller." :
                    "No synced-layer sister was found for this clip in the object's Animator Controller.";
            return result;
        }

        private static IEnumerable<(AnimatorState state, string path)> States(AnimatorStateMachine machine, string path)
        {
            if (machine == null) yield break;
            foreach (var child in machine.states)
                yield return (child.state, path + "/" + child.state.name);
            foreach (var child in machine.stateMachines)
                foreach (var entry in States(child.stateMachine, path + "/" + child.stateMachine.name))
                    yield return entry;
        }

        private static bool Contains(Motion motion, AnimationClip selected, Func<AnimationClip, AnimationClip> effective)
        {
            if (motion is AnimationClip clip) return effective(clip) == selected;
            if (motion is BlendTree tree)
                foreach (var child in tree.children)
                    if (Contains(child.motion, selected, effective)) return true;
            return false;
        }
    }
}
