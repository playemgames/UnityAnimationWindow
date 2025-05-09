// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityEditorInternal.PlayEm;
using Object = UnityEngine.Object;

namespace UnityEditor.PlayEm
{
    [EditorWindowTitle(title = "Animation", icon = "UnityEditor.AnimationWindow")]
    public sealed class PlayEmAnimationWindow : EditorWindow, IHasCustomMenu
    {
		[MenuItem("Window/Animation/Play-Em Animation Window %6", false, 1)]
		private static void ShowAnimationWindow() {
			EditorWindow.GetWindow<PlayEmAnimationWindow>();
		}

        // Active Animation windows
        private static List<PlayEmAnimationWindow> s_AnimationWindows = new List<PlayEmAnimationWindow>();
        internal static List<PlayEmAnimationWindow> GetAllAnimationWindows() { return s_AnimationWindows; }

        private AnimEditor m_AnimEditor;

        [SerializeField]
        EditorGUIUtility.EditorLockTracker m_LockTracker = new EditorGUIUtility.EditorLockTracker();

        [SerializeField] private int m_LastSelectedObjectID;

        private GUIStyle m_LockButtonStyle;
        private GUIContent m_DefaultTitleContent;
        private GUIContent m_RecordTitleContent;

        internal AnimEditor animEditor
        {
            get
            {
                return m_AnimEditor;
            }
        }

        internal AnimationWindowState state
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state;
                }
                return null;
            }
        }

        public AnimationClip animationClip
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.activeAnimationClip;
                }
                return null;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    m_AnimEditor.state.activeAnimationClip = value;
                }
            }
        }

        public bool previewing
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.previewing;
                }
                return false;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    if (value)
                        m_AnimEditor.state.StartPreview();
                    else
                        m_AnimEditor.state.StopPreview();
                }
            }
        }

        public bool canPreview
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.canPreview;
                }

                return false;
            }
        }

        public bool recording
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.recording;
                }
                return false;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    if (value)
                        m_AnimEditor.state.StartRecording();
                    else
                        m_AnimEditor.state.StopRecording();
                }
            }
        }

        public bool canRecord
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.canRecord;
                }

                return false;
            }
        }

        public bool playing
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.playing;
                }
                return false;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    if (value)
                        m_AnimEditor.state.StartPlayback();
                    else
                        m_AnimEditor.state.StopPlayback();
                }
            }
        }

        public float time
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.currentTime;
                }
                return 0.0f;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    m_AnimEditor.state.currentTime = value;
                }
            }
        }

        public int frame
        {
            get
            {
                if (m_AnimEditor != null)
                {
                    return m_AnimEditor.state.currentFrame;
                }
                return 0;
            }
            set
            {
                if (m_AnimEditor != null)
                {
                    m_AnimEditor.state.currentFrame = value;
                }
            }
        }

        internal void ForceRefresh()
        {
            if (m_AnimEditor != null)
            {
                m_AnimEditor.state.ForceRefresh();
            }
        }

        void OnEnable()
        {
            if (m_AnimEditor == null)
            {
                m_AnimEditor = CreateInstance(typeof(AnimEditor)) as AnimEditor;
                m_AnimEditor.hideFlags = HideFlags.HideAndDontSave;
            }

            s_AnimationWindows.Add(this);
            titleContent = GetLocalizedTitleContent();

            m_DefaultTitleContent = titleContent;
            m_RecordTitleContent = EditorGUIUtility.TextContentWithIcon(titleContent.text, "Animation.Record");

            OnSelectionChange();

            Undo.undoRedoPerformed += UndoRedoPerformed;
        }

        void OnDisable()
        {
            s_AnimationWindows.Remove(this);
            m_AnimEditor.OnDisable();

            Undo.undoRedoPerformed -= UndoRedoPerformed;
        }

        void OnDestroy()
        {
            DestroyImmediate(m_AnimEditor);
        }

        void Update()
        {
            if (m_AnimEditor == null)
                return;

            m_AnimEditor.Update();
        }

        void OnGUI()
        {
            if (m_AnimEditor == null)
                return;

            titleContent = m_AnimEditor.state.recording ? m_RecordTitleContent : m_DefaultTitleContent;
            m_AnimEditor.OnAnimEditorGUI(this, position);
        }

        internal void OnSelectionChange()
        {
            if (m_AnimEditor == null)
                return;

            Object activeObject = Selection.activeObject;

            bool restoringLockedSelection = false;
            if (m_LockTracker.isLocked && m_AnimEditor.stateDisabled)
            {
                activeObject = EditorUtility.InstanceIDToObject(m_LastSelectedObjectID);
                restoringLockedSelection = true;
                m_LockTracker.isLocked = false;
            }

            GameObject activeGameObject = activeObject as GameObject;
            if (activeGameObject != null)
            {
                EditGameObject(activeGameObject);
            }
            else
            {
                Transform activeTransform = activeObject as Transform;
                if (activeTransform != null)
                {
                    EditGameObject(activeTransform.gameObject);
                }
                else
                {
                    AnimationClip activeAnimationClip = activeObject as AnimationClip;
                    if (activeAnimationClip != null)
                        EditAnimationClip(activeAnimationClip);
                }
            }

            if (restoringLockedSelection && !m_AnimEditor.stateDisabled)
            {
                m_LockTracker.isLocked = true;
            }
        }

        void OnFocus()
        {
            OnSelectionChange();
        }

        internal void OnControllerChange()
        {
            // Refresh selectedItem to update selected clips.
            OnSelectionChange();
        }

        void OnLostFocus()
        {
            if (m_AnimEditor != null)
                m_AnimEditor.OnLostFocus();
        }

        [Callbacks.OnOpenAsset]
        static bool OnOpenAsset(int instanceID, int line)
        {
            var clip = EditorUtility.InstanceIDToObject(instanceID) as AnimationClip;
            if (clip)
            {
                EditorWindow.GetWindow<AnimationWindow>();
                return true;
            }
            return false;
        }

        internal bool EditGameObject(GameObject gameObject)
        {
            return EditGameObjectInternal(gameObject, (AnimationWindowControl)null);
        }

        internal bool EditAnimationClip(AnimationClip animationClip)
        {
            if (state.linkedWithSequencer == true)
                return false;

            EditAnimationClipInternal(animationClip, (Object)null, (AnimationWindowControl)null);
            return true;
        }

        internal bool EditSequencerClip(AnimationClip animationClip, Object sourceObject, AnimationWindowControl controlInterface)
        {
            EditAnimationClipInternal(animationClip, sourceObject, controlInterface);
            state.linkedWithSequencer = true;
            return true;
        }

        internal void UnlinkSequencer()
        {
            if (state.linkedWithSequencer)
            {
                state.linkedWithSequencer = false;

                // Selected object could have been changed when unlocking the animation window
                EditAnimationClip(null);
                OnSelectionChange();
            }
        }

        private bool EditGameObjectInternal(GameObject gameObject, AnimationWindowControl controlInterface)
        {
            if (EditorUtility.IsPersistent(gameObject))
                return false;

            if ((gameObject.hideFlags & HideFlags.NotEditable) != 0)
                return false;

            var newSelection = GameObjectSelectionItem.Create(gameObject);
            if (ShouldUpdateGameObjectSelection(newSelection))
            {
                m_AnimEditor.selection = newSelection;
                m_AnimEditor.overrideControlInterface = controlInterface;

                m_LastSelectedObjectID = gameObject != null ? gameObject.GetInstanceID() : 0;
            }
            else
                m_AnimEditor.OnSelectionUpdated();

            return true;
        }

        private void EditAnimationClipInternal(AnimationClip animationClip, Object sourceObject, AnimationWindowControl controlInterface)
        {
            var newSelection = AnimationClipSelectionItem.Create(animationClip, sourceObject);
            if (ShouldUpdateSelection(newSelection))
            {
                m_AnimEditor.selection = newSelection;
                m_AnimEditor.overrideControlInterface = controlInterface;

                m_LastSelectedObjectID = animationClip != null ? animationClip.GetInstanceID() : 0;
            }
            else
                m_AnimEditor.OnSelectionUpdated();
        }

        void ShowButton(Rect r)
        {
            if (m_LockButtonStyle == null)
                m_LockButtonStyle = "IN LockButton";

            EditorGUI.BeginChangeCheck();

            m_LockTracker.ShowButton(r, m_LockButtonStyle, m_AnimEditor.stateDisabled);

            // Selected object could have been changed when unlocking the animation window
            if (EditorGUI.EndChangeCheck())
                OnSelectionChange();
        }

        private bool ShouldUpdateGameObjectSelection(GameObjectSelectionItem selectedItem)
        {
            if (m_LockTracker.isLocked)
                return false;

            if (state.linkedWithSequencer)
                return false;

            // Selected game object with no animation player.
            if (selectedItem.rootGameObject == null)
                return true;

            AnimationWindowSelectionItem currentSelection = m_AnimEditor.selection;

            // Game object holding animation player has changed.  Update selection.
            if (selectedItem.rootGameObject != currentSelection.rootGameObject)
                return true;

            // No clip in current selection, favour new selection.
            if (currentSelection.animationClip == null)
                return true;

            // Make sure that animation clip is still referenced in animation player.
            if (currentSelection.rootGameObject != null)
            {
                AnimationClip[] allClips = AnimationUtility.GetAnimationClips(currentSelection.rootGameObject);
                if (!Array.Exists(allClips, x => x == currentSelection.animationClip))
                    return true;
            }

            return false;
        }

        private bool ShouldUpdateSelection(AnimationWindowSelectionItem selectedItem)
        {
            if (m_LockTracker.isLocked)
                return false;

            AnimationWindowSelectionItem currentSelection = m_AnimEditor.selection;
            return (selectedItem.GetRefreshHash() != currentSelection.GetRefreshHash());
        }

        private void UndoRedoPerformed()
        {
            Repaint();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            m_LockTracker.AddItemsToMenu(menu, m_AnimEditor.stateDisabled);
        }
    }
}
