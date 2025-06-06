// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

// Credit to Andr�s Leone G�mez from his repository at https://github.com/forestrf/UnityAnimationWindow
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditorInternal.PlayEm;

namespace UnityEditor.PlayEm 
{
    [System.Serializable]
    class AnimationWindowClipPopup
    {
        [SerializeField] public AnimationWindowState state;

        static int s_ClipPopupHash = "s_ClipPopupHash".GetHashCode();

		public string animClipName;

		private const float kMenuOffsetMac = 19;

        internal sealed class ClipPopupCallbackInfo
        {
            // The global shared popup state
            public static ClipPopupCallbackInfo instance = null;

            // Name of the command event sent from the popup menu to OnGUI when user has changed selection
            private const string kPopupMenuChangedMessage = "ClipPopupMenuChanged";

            // The control ID of the popup menu that is currently displayed.
            // Used to pass selection changes back again...
            private readonly int m_ControlID = 0;

            // Which item was selected
            private AnimationClip m_SelectedClip = null;

            // Which view should we send it to.
            private readonly GUIView m_SourceView;

            public ClipPopupCallbackInfo(int controlID)
            {
                m_ControlID = controlID;
                m_SourceView = GUIView.current;
            }

            public static AnimationClip GetSelectedClipForControl(int controlID, AnimationClip clip)
            {
                Event evt = Event.current;
                if (evt.type == EventType.ExecuteCommand && evt.commandName == kPopupMenuChangedMessage)
                {
                    if (instance == null)
                    {
                        Debug.LogError("Popup menu has no instance");
                        return clip;
                    }
                    if (instance.m_ControlID == controlID)
                    {
                        clip = instance.m_SelectedClip;
                        instance = null;
                        GUI.changed = true;
                        evt.Use();
                    }
                }
                return clip;
            }

            public static void SetSelectedClip(AnimationClip clip)
            {
                if (instance == null)
                {
                    Debug.LogError("Popup menu has no instance");
                    return;
                }

                instance.m_SelectedClip = clip;
            }

            public static void SendEvent()
            {
                if (instance == null)
                {
                    Debug.LogError("Popup menu has no instance");
                    return;
                }

                instance.m_SourceView.SendEvent(EditorGUIUtility.CommandEvent(kPopupMenuChangedMessage));
            }
        }


        private void DisplayClipMenu(Rect position, int controlID, AnimationClip clip)
        {
            AnimationClip[] clips = GetOrderedClipList();
            GUIContent[] menuContent = GetClipMenuContent(clips);
            int selected = ClipToIndex(clips, clip);

            // Center popup menu around button widget
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                position.y = position.y - selected * EditorGUI.kSingleLineHeight - kMenuOffsetMac;
            }

            ClipPopupCallbackInfo.instance = new ClipPopupCallbackInfo(controlID);

            EditorUtility.DisplayCustomMenu(position, menuContent, null, selected, (userData, options, index) =>
            {
                if (index < clips.Length)
                {
                    ClipPopupCallbackInfo.SetSelectedClip(clips[index]);
                }
                else
                {
                    AnimationClip newClip = AnimationWindowUtility.CreateNewClip(state.selection.rootGameObject.name);
                    if (newClip)
                    {
                        AnimationWindowUtility.AddClipToAnimationPlayerComponent(state.activeAnimationPlayer, newClip);
                        ClipPopupCallbackInfo.SetSelectedClip(newClip);
                    }
                }

                ClipPopupCallbackInfo.SendEvent();
            }, null);
        }

        // (case 1029160) Modified version of EditorGUI.DoPopup to fit large data list query.
        private AnimationClip DoClipPopup(AnimationClip clip, GUIStyle style)
        {
            Rect position = EditorGUILayout.GetControlRect(false, EditorGUI.kSingleLineHeight, style);
            int controlID = GUIUtility.GetControlID(s_ClipPopupHash, FocusType.Keyboard, position);

            clip = ClipPopupCallbackInfo.GetSelectedClipForControl(controlID, clip);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.Repaint:
                    Font originalFont = style.font;
                    if (originalFont && EditorGUIUtility.GetBoldDefaultFont() && originalFont == EditorStyles.miniFont)
                    {
                        style.font = EditorStyles.miniBoldFont;
                    }

                    GUIContent buttonContent = EditorGUIUtility.TempContent(CurveUtility.GetClipName(clip));
                    buttonContent.tooltip = AssetDatabase.GetAssetPath(clip);

                    style.Draw(position, buttonContent, controlID, false);

                    style.font = originalFont;
                    break;
                case EventType.MouseDown:
                    if (evt.button == 0 && position.Contains(evt.mousePosition))
                    {
                        DisplayClipMenu(position, controlID, clip);
                        GUIUtility.keyboardControl = controlID;
                        evt.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if (evt.MainActionKeyForControl(controlID))
                    {
                        DisplayClipMenu(position, controlID, clip);
                        evt.Use();
                    }
                    break;
            }

            return clip;
        }

        public void OnGUI()
        {
			// Check for the animation clip name and create array of clips based on name
			animClipName = EditorGUILayout.TextField("Animation Clip Name:", animClipName, GUILayout.Width(300));
			GUILayout.FlexibleSpace();

            if (state.selection.canChangeAnimationClip)
            {
                EditorGUI.BeginChangeCheck();
                var newClip = DoClipPopup(state.activeAnimationClip, AnimationWindowStyles.animClipToolbarPopup);
                if (EditorGUI.EndChangeCheck())
                {
                    state.activeAnimationClip = newClip;

                    //  Layout has changed, bail out now.
                    EditorGUIUtility.ExitGUI();
                }
            }
            else if (state.activeAnimationClip != null)
            {
                Rect r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, AnimationWindowStyles.toolbarLabel);
                EditorGUI.LabelField(r, CurveUtility.GetClipName(state.activeAnimationClip), AnimationWindowStyles.toolbarLabel);
            }
        }

        private GUIContent[] GetClipMenuContent(AnimationClip[] clips)
        {
            int size = clips.Length;
            if (state.selection.canCreateClips)
                size += 2;

            GUIContent[] content = new GUIContent[size];
            for (int i = 0; i < clips.Length; i++)
            {
                content[i] = new GUIContent(CurveUtility.GetClipName(clips[i]));
            }

            if (state.selection.canCreateClips)
            {
                content[content.Length - 2] = GUIContent.none;
                content[content.Length - 1] = AnimationWindowStyles.createNewClip;
            }

            return content;
        }

        private AnimationClip[] GetOrderedClipList()
        {
            AnimationClip[] clips = new AnimationClip[0];
            if (state.activeRootGameObject != null)
                clips = AnimationUtility.GetAnimationClips(state.activeRootGameObject);

			// Check for clips by name in animClipName field
			if (!string.IsNullOrEmpty(animClipName)) { 
				if (clips != null && clips.Length > 0) { 
					List<AnimationClip> clipsByName = new List<AnimationClip>();
					string nameInClip = animClipName;

					for (int i = 0; i < clips.Length; i++) {
						if (clips[i].name.Contains(nameInClip)) { 
							clipsByName.Add(clips[i]);
						}
					}

					if (clipsByName.Count > 0) { 
						clips = clipsByName.ToArray();
					}
				}
			}

			#if UNITY_EDITOR_WIN
			// So we don't break in windows even though it worked before, only up to 999 because you need one more for New Clip
			System.Array.Resize(ref clips, 999);
			#endif

            Array.Sort(clips, (AnimationClip clip1, AnimationClip clip2) => CurveUtility.GetClipName(clip1).CompareTo(CurveUtility.GetClipName(clip2)));

            return clips;
        }

        private int ClipToIndex(AnimationClip[] clips, AnimationClip clip)
        {
            for (int index = 0; index < clips.Length; ++index)
            {
                if (clips[index] == clip)
                    return index;
            }

            return 0;
        }
    }
}
