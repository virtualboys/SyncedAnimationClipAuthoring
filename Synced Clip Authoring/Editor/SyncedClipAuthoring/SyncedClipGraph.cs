using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VB.SyncedClipAuthoring.Editor
{
    // Invoked by the unsaved bridge component; all graph logic stays in the editor assembly.
    internal sealed class SyncedClipGraph : IAnimationWindowPreview
    {
        internal SyncedClipPair Pair;
        internal AnimationClip SelectedClip;
        internal AnimationWindowAdapter Adapter;
        internal string Error { get; private set; }
        private AnimationLayerMixerPlayable mixer;
        private AnimationClipPlayable sister;
        private bool wired;

        public void StartPreview() { }
        public void StopPreview() { mixer = default; sister = default; wired = false; }

        public Playable BuildPreviewGraph(PlayableGraph graph, Playable inputPlayable)
        {
            Error = null;
            wired = false;
            mixer = default;
            sister = default;
            if (Pair == null || SelectedClip == null || Adapter == null) return inputPlayable;
            // Unity 6000.0 supplies an unconnected mixer, then connects default pose, edited clip,
            // and candidate edits to ports 0, 1, 2 after this callback. Reserve port 3 now so the
            // sister's bindings exist during stream/default-pose creation. Reorder before sampling.
            if (inputPlayable.GetPlayableType() != typeof(AnimationLayerMixerPlayable) || inputPlayable.GetInputCount() != 3)
            {
                Error = "Another preview extension or a Unity API change altered the Animation window graph. Sister sampling is disabled.";
                return inputPlayable;
            }
            try
            {
                mixer = (AnimationLayerMixerPlayable)inputPlayable;
                sister = AnimationClipPlayable.Create(graph, Pair.Sister(SelectedClip));
                sister.SetApplyFootIK(false);
                sister.SetApplyPlayableIK(false);
                Adapter.DisableLooping(sister);
                mixer.SetInputCount(4);
                mixer.ConnectInput(3, sister, 0, 1f);
            }
            catch (Exception exception)
            {
                Error = "Could not extend the Animation window graph: " + exception.GetBaseException().Message;
                if (mixer.IsValid()) mixer.SetInputCount(3);
                mixer = default;
            }
            return inputPlayable;
        }

        public void UpdatePreviewGraph(PlayableGraph graph)
        {
            if (Pair == null || SelectedClip == null || wired || !mixer.IsValid() || !sister.IsValid()) return;
            var selected = mixer.GetInput(1);
            var candidates = mixer.GetInput(2);
            if (!selected.IsValid() || !candidates.IsValid()) return;

            // Preserve Unity's candidate layer at the very top so dragging an Inspector value
            // remains visible before the author records it, including overlapping bindings.
            mixer.DisconnectInput(1);
            mixer.DisconnectInput(2);
            mixer.DisconnectInput(3);
            bool selectedIsSource = SelectedClip == Pair.SourceClip;
            Playable source = selectedIsSource ? selected : (Playable)sister;
            Playable synced = selectedIsSource ? (Playable)sister : selected;
            bool sourceFirst = Pair.SourceLayer < Pair.SyncedLayer;
            mixer.ConnectInput(1, sourceFirst ? source : synced, 0, 1f);
            mixer.ConnectInput(2, sourceFirst ? synced : source, 0, 1f);
            mixer.ConnectInput(3, candidates, 0, 1f);
            ApplyLayer(1, sourceFirst ? Pair.SourceBlend : Pair.SyncedBlend, sourceFirst ? Pair.SourceMask : Pair.SyncedMask);
            ApplyLayer(2, sourceFirst ? Pair.SyncedBlend : Pair.SourceBlend, sourceFirst ? Pair.SyncedMask : Pair.SourceMask);
            wired = true;
        }

        private void ApplyLayer(uint index, UnityEditor.Animations.AnimatorLayerBlendingMode blend, AvatarMask mask)
        {
            mixer.SetLayerAdditive(index, blend == UnityEditor.Animations.AnimatorLayerBlendingMode.Additive);
            if (mask != null) mixer.SetLayerMaskFromAvatarMask(index, mask);
        }
    }
}
