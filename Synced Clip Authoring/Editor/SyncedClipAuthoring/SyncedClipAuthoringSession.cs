using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VB.SyncedClipAuthoring.Editor
{
    internal sealed class SyncedClipAuthoringSession : IDisposable
    {
        internal readonly AnimationWindow Window;
        internal SyncedClipResolution Resolution { get; private set; } = new SyncedClipResolution();
        internal SyncedClipPair Pair { get; private set; }
        internal AnimationClip SelectedClip { get; private set; }
        internal GameObject Root { get; private set; }
        internal string Message { get; private set; }
        internal bool Failed => fault != null;
        internal Exception Failure { get; private set; }
        internal string PreviewError => preview?.Error;

        private readonly AnimationWindowAdapter adapter;
        private SyncedClipPreview helper;
        private SyncedClipGraph preview;
        private string chosenKey, fault;
        private bool dirty = true, disposed;
        private double nextRefresh;

        internal SyncedClipAuthoringSession(AnimationWindow window, AnimationWindowAdapter adapter)
        {
            Window = window;
            this.adapter = adapter;
            EditorApplication.projectChanged += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            AnimationUtility.onCurveWasModified += OnCurveModified;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        internal void ChoosePair(string key)
        {
            chosenKey = key;
            dirty = true;
            Refresh(true);
        }

        internal void Refresh(bool force = false)
        {
            if (disposed || fault != null) return;
            if (Window == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Dispose();
                Message = "Authoring stopped because the Animation window closed, scripts are compiling, or Play Mode is starting.";
                return;
            }

            try
            {
                if (helper != null && preview?.Error != null) throw new InvalidOperationException(preview.Error);
                if (!adapter.TryGetTarget(Window, out var root, out var animator, out var reason))
                {
                    RemoveHelper(false);
                    Pair = null; Root = null; SelectedClip = null;
                    Resolution = new SyncedClipResolution();
                    Message = reason;
                    return;
                }

                bool targetChanged = root != Root || Window.animationClip != SelectedClip;
                if (!force && !dirty && !targetChanged && EditorApplication.timeSinceStartup < nextRefresh) return;
                nextRefresh = EditorApplication.timeSinceStartup + 0.5;
                if (root != Root) RemoveHelper(false);
                Root = root;
                SelectedClip = Window.animationClip;
                Resolution = SyncedClipResolver.Resolve(animator.runtimeAnimatorController, SelectedClip);
                var nextPair = Resolution.Pairs.Count == 1 ? Resolution.Pairs[0] :
                    Resolution.Pairs.FirstOrDefault(candidate => candidate.Key == chosenKey);
                if (nextPair == null)
                {
                    RemoveHelper(false);
                    Pair = null;
                    Message = Resolution.Pairs.Count > 1 ? "This clip belongs to several synced pairs. Choose the state/layer pair to author." : Resolution.Message;
                    dirty = false;
                    return;
                }

                bool rebuild = force || dirty || targetChanged || !nextPair.HasSameGraphSettings(Pair);
                Pair = nextPair;
                chosenKey = Pair.Key;
                if (helper == null)
                {
                    if (Root.GetComponent<SyncedClipPreview>() != null)
                        throw new InvalidOperationException("This object already has an active Synced Clip Authoring session.");
                    helper = Root.AddComponent<SyncedClipPreview>();
                    if (helper == null) throw new InvalidOperationException("Unity could not attach the temporary preview bridge.");
                    helper.hideFlags = HideFlags.HideInInspector | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    preview = new SyncedClipGraph();
                    helper.Preview = preview;
                    rebuild = true;
                }
                preview.Pair = Pair;
                preview.SelectedClip = SelectedClip;
                preview.Adapter = adapter;
                if (rebuild) adapter.Rebuild(Window);
                Message = null;
                dirty = false;
            }
            catch (Exception exception)
            {
                Failure = exception;
                fault = exception.GetBaseException().Message;
                Message = fault + " Disable and re-enable Synced Clip Authoring after resolving this issue.";
                RemoveHelper(true);
            }
        }

        private void Invalidate() { dirty = true; }

        private void OnCurveModified(AnimationClip clip, EditorCurveBinding binding, AnimationUtility.CurveModifiedType modification)
        {
            if (clip == SelectedClip || (Pair != null && clip == Pair.Sister(SelectedClip))) dirty = true;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Dispose();
        }

        private void RemoveHelper(bool stopPreview)
        {
            if (helper == null) return;
            try
            {
                if (Window != null)
                {
                    if (stopPreview) Window.previewing = false;
                    else
                    {
                        // A rebuild with the helper still present restores the old sampled properties
                        // and preserves Unity's candidate clip. Then return to its ordinary graph.
                        preview.Pair = null;
                        adapter.Rebuild(Window);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(helper);
                helper = null;
                preview = null;
            }
            if (!stopPreview && Window != null) adapter.Rebuild(Window);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.hierarchyChanged -= Invalidate;
            Undo.undoRedoPerformed -= Invalidate;
            AnimationUtility.onCurveWasModified -= OnCurveModified;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            RemoveHelper(true);
        }
    }
}
