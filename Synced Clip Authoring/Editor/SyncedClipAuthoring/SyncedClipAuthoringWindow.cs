using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VB.SyncedClipAuthoring.Editor
{
    public sealed class SyncedClipAuthoringWindow : EditorWindow
    {
        private AnimationWindow animationWindow;
        private SyncedClipAuthoringSession session;
        private string error;

        [MenuItem("Tools/Animation/Synced Clip Authoring")]
        public static void Open()
        {
            GetWindow<SyncedClipAuthoringWindow>("Synced Clip Authoring");
        }

        private void OnEnable()
        {
            minSize = new Vector2(360, 240);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopSession();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) StopSession();
        }

        private void Tick()
        {
            if (session == null) return;
            if (animationWindow == null || EditorApplication.isCompiling) StopSession();
            else session.Refresh();
            Repaint();
        }

        private void StopSession()
        {
            session?.Dispose();
            session = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Author with synced clips", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scrub and key in the Animation window. The controller supplies the sister clip.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            if (animationWindow == null && session == null)
                animationWindow = Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();
            using (new EditorGUI.DisabledScope(session != null))
                animationWindow = (AnimationWindow)EditorGUILayout.ObjectField("Animation window", animationWindow, typeof(AnimationWindow), true);
            if (animationWindow == null && GUILayout.Button("Open Animation Window"))
                animationWindow = GetWindow<AnimationWindow>();

            using (new EditorGUI.DisabledScope(animationWindow == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                bool enable = EditorGUILayout.ToggleLeft("Synced Clip Authoring", session != null);
                if (enable != (session != null))
                {
                    if (!enable) StopSession();
                    else if (AnimationWindowAdapter.TryCreate(out var adapter, out error))
                    {
                        session = new SyncedClipAuthoringSession(animationWindow, adapter);
                        session.Refresh(true);
                    }
                }
            }

            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (session == null)
            {
                EditorGUILayout.HelpBox("Select an animated object and its Unity clip, then enable authorship here. The FBX stays linked through the Animator Controller.", MessageType.Info);
                return;
            }

            var pairs = session.Resolution.Pairs;
            if (pairs.Count > 1)
            {
                string[] labels = new[] { "Choose a state/layer pair…" }.Concat(pairs.Select(pair => pair.Label)).ToArray();
                int current = session.Pair == null ? 0 : pairs.FindIndex(pair => pair.Key == session.Pair.Key) + 1;
                int chosen = EditorGUILayout.Popup("Pair", current, labels);
                if (chosen != current) session.ChoosePair(chosen == 0 ? null : pairs[chosen - 1].Key);
            }
            if (session.Message != null)
                EditorGUILayout.HelpBox(session.Message, session.Failed ? MessageType.Error : MessageType.Info);
            if (session.Pair == null) return;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Target", session.Root, typeof(GameObject), true);
                EditorGUILayout.ObjectField("Editing", session.SelectedClip, typeof(AnimationClip), false);
                EditorGUILayout.ObjectField("Sister", session.Pair.Sister(session.SelectedClip), typeof(AnimationClip), false);
            }
            EditorGUILayout.LabelField(session.Pair.Label, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Both layers: weight 1 · Same time in seconds", EditorStyles.miniLabel);
            if (!session.Pair.DurationsMatch)
            {
                var sister = session.Pair.Sister(session.SelectedClip);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.HelpBox($"Clip durations differ: editing {session.SelectedClip.length:0.####}s, sister {sister.length:0.####}s. " +
                        "The shorter clip holds its final value while scrubbing.", MessageType.Warning);
                    EditorGUILayout.LabelField("Match scales all keys and events in the editing clip proportionally. Supports Undo.", EditorStyles.wordWrappedMiniLabel);
                    bool canMatch = SyncedClipDuration.CanMatch(session.SelectedClip, sister, out var reason);
                    using (new EditorGUI.DisabledScope(!canMatch))
                    {
                        if (GUILayout.Button(new GUIContent($"Match Sister Length ({sister.length:0.####}s)",
                                "Scale the selected clip's keys and events to fit its sister. The sister clip is unchanged.")))
                        {
                            if (SyncedClipDuration.TryMatch(session.SelectedClip, sister, out error))
                                session.Refresh(true);
                        }
                    }
                    if (!canMatch) EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (EditorUtility.IsPersistent(session.SelectedClip) && AssetDatabase.IsSubAsset(session.SelectedClip))
                EditorGUILayout.HelpBox("The selected clip is imported. Select the editable Unity sister in the Animation window to add keys.", MessageType.Info);
            if (GUILayout.Button("Refresh Pair and Sampling")) session.Refresh(true);
        }
    }
}
