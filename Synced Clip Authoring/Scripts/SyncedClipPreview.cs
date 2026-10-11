#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VB.SyncedClipAuthoring.Editor
{
    // Unity rejects components compiled in an editor-only assembly. This bridge must live in
    // a regular assembly, but its entire type is excluded from player compilation.
    // It is attached only by the editor tool with DontSave flags; never add it to a prefab.
    [AddComponentMenu("")]
    public sealed class SyncedClipPreview : MonoBehaviour, IAnimationWindowPreview
    {
        public IAnimationWindowPreview Preview { get; set; }
        public void StartPreview() => Preview?.StartPreview();
        public void StopPreview() => Preview?.StopPreview();
        public void UpdatePreviewGraph(PlayableGraph graph) => Preview?.UpdatePreviewGraph(graph);
        public Playable BuildPreviewGraph(PlayableGraph graph, Playable inputPlayable)
            => Preview != null ? Preview.BuildPreviewGraph(graph, inputPlayable) : inputPlayable;
    }
}
#endif
