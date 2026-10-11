using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VB.SyncedClipAuthoring.Editor
{
    internal static class SyncedClipDuration
    {
        internal static bool CanMatch(AnimationClip selected, AnimationClip sister, out string reason)
        {
            reason = null;
            if (selected == null || sister == null || selected == sister)
                reason = "Choose a pair of different clips first.";
            else
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsSubAsset(selected) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                    reason = "Select an editable Unity .anim clip to match its length. Imported clips cannot be changed here.";
                else if ((selected.hideFlags & HideFlags.NotEditable) != 0 ||
                         (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0))
                    reason = "The selected clip is read-only.";
                else if (!AssetDatabase.IsOpenForEdit(selected, out var editReason))
                    reason = "The selected clip is not open for editing. " + editReason;
                else if (!HasDuration(selected))
                    reason = "Add a property key after time 0 to the selected clip first; a zero-length clip has no timing to scale.";
                else if (!HasDuration(sister))
                    reason = "The sister clip must have a duration greater than zero before matching lengths.";
                else
                    TryGetScale(selected, sister.length, out _, out reason);
            }
            return reason == null;
        }

        internal static bool TryMatch(AnimationClip selected, AnimationClip sister, out string reason)
        {
            // Validate again at the time of the click; the selection or asset permissions may have changed.
            if (!CanMatch(selected, sister, out reason)) return false;
            if (!TryGetScale(selected, sister.length, out double scale, out reason)) return false;
            var bindings = AnimationUtility.GetCurveBindings(selected);
            var curves = bindings.Select(binding => AnimationUtility.GetEditorCurve(selected, binding)).ToArray();
            var referenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(selected);
            var references = referenceBindings.Select(binding => AnimationUtility.GetObjectReferenceCurve(selected, binding)).ToArray();
            var events = AnimationUtility.GetAnimationEvents(selected);
            var settings = AnimationUtility.GetAnimationClipSettings(selected);

            foreach (var curve in curves)
            {
                var keys = curve.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].time = (float)(keys[i].time * scale);
                    // Tangents are value/second. Weights are normalized and stay unchanged, as do tangent modes.
                    keys[i].inTangent = (float)(keys[i].inTangent / scale);
                    keys[i].outTangent = (float)(keys[i].outTangent / scale);
                }
                curve.keys = keys;
            }
            foreach (var keys in references)
                for (int i = 0; i < keys.Length; i++) keys[i].time = (float)(keys[i].time * scale);
            foreach (var marker in events) marker.time = (float)(marker.time * scale);
            settings.startTime = (float)(settings.startTime * scale);
            settings.stopTime = sister.length;
            if (settings.additiveReferencePoseClip == selected)
                settings.additiveReferencePoseTime = (float)(settings.additiveReferencePoseTime * scale);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            const string undoName = "Match Sister Clip Length";
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(selected, undoName);
            AnimationUtility.SetEditorCurves(selected, bindings, curves);
            AnimationUtility.SetObjectReferenceCurves(selected, referenceBindings, references);
            AnimationUtility.SetAnimationEvents(selected, events);
            AnimationUtility.SetAnimationClipSettings(selected, settings);
            EditorUtility.SetDirty(selected);
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
            return true;
        }

        private static bool HasDuration(AnimationClip clip)
        {
            return clip.length > 0 && !float.IsNaN(clip.length) && !float.IsInfinity(clip.length);
        }

        private static bool TryGetScale(AnimationClip selected, float targetLength, out double scale, out string reason)
        {
            var numericKeys = AnimationUtility.GetCurveBindings(selected)
                .SelectMany(binding => AnimationUtility.GetEditorCurve(selected, binding).keys).ToArray();
            var events = AnimationUtility.GetAnimationEvents(selected);
            double numericEnd = numericKeys.Select(key => (double)key.time)
                .Concat(events.Select(marker => (double)marker.time)).DefaultIfEmpty(0).Max();
            var references = AnimationUtility.GetObjectReferenceCurveBindings(selected)
                .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(selected, binding)).ToArray();
            double referenceEnd = references.Select(key => (double)key.time).DefaultIfEmpty(0).Max();
            scale = numericEnd > 0 ? targetLength / numericEnd : double.PositiveInfinity;
            reason = null;
            if (numericEnd <= 0 && referenceEnd <= 0)
                reason = "Add a property key after time 0 to the selected clip first; it has no timing to scale.";
            else if (references.Length > 0)
            {
                // Unity keeps the last object reference alive for one extra frame. This fixed tail
                // contributes to clip.length, so targetLength / oldLength would miss the target.
                double tail = 1.0 / selected.frameRate;
                if (targetLength < tail || (referenceEnd > 0 && targetLength <= tail))
                    reason = "The sister is too short to fit this clip's object-reference keys at its current sample rate.";
                else if (referenceEnd > 0)
                    scale = Math.Min(scale, (targetLength - tail) / referenceEnd);
            }
            return reason == null;
        }
    }
}
