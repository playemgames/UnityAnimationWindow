// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityEditorInternal.PlayEm;
using System.Linq;
using Object = UnityEngine.Object;

namespace UnityEditor.PlayEm
{
    [System.Serializable]
    internal class AnimationEventTimeLine
    {
        internal static class Styles
        {
            public static GUIContent textAddEvent = EditorGUIUtility.TrTextContent("Add Animation Event");
            public static GUIContent textDeleteEvents = EditorGUIUtility.TrTextContent("Delete Animation Events");
            public static GUIContent textDeleteEvent = EditorGUIUtility.TrTextContent("Delete Animation Event");
            public static GUIContent textCopyEvents = EditorGUIUtility.TrTextContent("Copy Animation Events");
            public static GUIContent textPasteEvents = EditorGUIUtility.TrTextContent("Paste Animation Events");
        }

        [System.NonSerialized]
        private AnimationEvent[] m_EventsAtMouseDown;
        [System.NonSerialized]
        private float[] m_EventTimes;
        private static readonly Vector2 k_EventMarkerSize = new Vector2(16, 16);

        private bool m_DirtyTooltip = false;
        private int m_HoverEvent = -1;
        private string m_InstantTooltipText = null;
        private Vector2 m_InstantTooltipPoint = Vector2.zero;
        private bool m_HasSelectedEvents;

        public AnimationEventTimeLine(EditorWindow owner)
        {
        }

        public class EventComparer : IComparer<AnimationEvent>
        {
            public int Compare(AnimationEvent x, AnimationEvent y)
            {
                float timeX = x.time;
                float timeY = y.time;
                if (timeX != timeY)
                    return ((int)Mathf.Sign(timeX - timeY));

                int valueX = x.GetHashCode();
                int valueY = y.GetHashCode();
                return valueX - valueY;
            }
        }

        private struct EventLineContextMenuObject
        {
            public GameObject m_Animated;
            public AnimationClip m_Clip;
            public float m_Time;
            public int m_Index;
            public bool[] m_Selected;

            public EventLineContextMenuObject(GameObject animated, AnimationClip clip, float time, int index, bool[] selected)
            {
                m_Animated = animated;
                m_Clip = clip;
                m_Time = time;
                m_Index = index;
                m_Selected = selected;
            }
        }

        internal bool HasSelectedEvents => m_HasSelectedEvents;

        public void AddEvent(float time, GameObject gameObject, AnimationClip animationClip)
        {
            AnimationWindowEvent awEvent = AnimationWindowEvent.CreateAndEdit(gameObject, animationClip, time);
            Selection.activeObject = awEvent;
        }

        public void EditEvents(GameObject gameObject, AnimationClip clip, bool[] selectedIndices)
        {
            List<AnimationWindowEvent> awEvents = new List<AnimationWindowEvent>();

            for (int index = 0; index < selectedIndices.Length; ++index)
            {
                if (selectedIndices[index])
                    awEvents.Add(AnimationWindowEvent.Edit(gameObject, clip, index));
            }

            if (awEvents.Count > 0)
            {
                Selection.objects = awEvents.ToArray();
            }
            else
            {
                ClearSelection();
            }
        }

        public void EditEvent(GameObject gameObject, AnimationClip clip, int index)
        {
            AnimationWindowEvent awEvent = AnimationWindowEvent.Edit(gameObject, clip, index);
            Selection.activeObject = awEvent;
        }

        public void ClearSelection()
        {
            // Do not unecessarily clear selection.  Only clear if selection already is animation window event.
            if (Selection.activeObject is AnimationWindowEvent)
                Selection.activeObject = null;
        }

        public void DeleteEvents(AnimationClip clip, bool[] deleteIndices)
        {
            bool deletedAny = false;

            List<AnimationEvent> eventList = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
            for (int i = eventList.Count - 1; i >= 0; i--)
            {
                if (deleteIndices[i])
                {
                    eventList.RemoveAt(i);
                    deletedAny = true;
                }
            }

            if (deletedAny)
            {
                Undo.RegisterCompleteObjectUndo(clip, "Delete Event");

                AnimationUtility.SetAnimationEvents(clip, eventList.ToArray());
                Selection.objects = new AnimationWindowEvent[] {};

                m_DirtyTooltip = true;
            }
        }

        void CopyEvents(AnimationClip clip, bool[] selected, int explicitIndex = -1)
        {
            var allEvents = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
            AnimationWindowEventsClipboard.CopyEvents(allEvents, selected, explicitIndex);
        }

        internal void PasteEvents(GameObject animated, AnimationClip clip, float time)
        {
            var oldEvents = AnimationUtility.GetAnimationEvents(clip);
            var newEvents = AnimationWindowEventsClipboard.AddPastedEvents(oldEvents, time, out var selected);
            if (newEvents == null)
                return;

            Undo.RegisterCompleteObjectUndo(clip, "Paste Events");
            EditEvents(animated, clip, selected);
            AnimationUtility.SetAnimationEvents(clip, newEvents);
            m_DirtyTooltip = true;
        }

        public void EventLineGUI(Rect rect, AnimationWindowState state)
        {
            //  We only display and manipulate animation events from the main
            //  game object in selection.  If we ever want to update to handle
            //  a multiple selection, a single timeline might not be sufficient...
            AnimationClip clip = state.activeAnimationClip;
            GameObject animated = state.activeRootGameObject;

            GUI.BeginGroup(rect);
            Color backupCol = GUI.color;

            Rect eventLineRect = new Rect(0, 0, rect.width, rect.height);

            float mousePosTime = Mathf.Max(Mathf.RoundToInt(state.PixelToTime(Event.current.mousePosition.x, rect) * state.frameRate) / state.frameRate, 0.0f);

            // Draw events
            if (clip != null)
            {
                AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
                Texture eventMarker = EditorGUIUtility.IconContent("Animation.EventMarker").image;

                // Calculate rects
                Rect[] hitRects = new Rect[events.Length];
                Rect[] drawRects = new Rect[events.Length];
                int shared = 1;
                int sharedLeft = 0;
                for (int i = 0; i < events.Length; i++)
                {
                    AnimationEvent evt = events[i];

                    if (sharedLeft == 0)
                    {
                        shared = 1;
                        while (i + shared < events.Length && events[i + shared].time == evt.time)
                            shared++;
                        sharedLeft = shared;
                    }
                    sharedLeft--;

                    // Important to take floor of positions of GUI stuff to get pixel correct alignment of
                    // stuff drawn with both GUI and Handles/GL. Otherwise things are off by one pixel half the time.
                    float keypos = Mathf.Floor(state.FrameToPixel(evt.time * clip.frameRate, rect));
                    int sharedOffset = 0;
                    if (shared > 1)
                    {
                        float spread = Mathf.Min((shared - 1) * (k_EventMarkerSize.x - 1), (int)(state.FrameDeltaToPixel(rect) - k_EventMarkerSize.x * 2));
                        sharedOffset = Mathf.FloorToInt(Mathf.Max(0, spread - (k_EventMarkerSize.x - 1) * (sharedLeft)));
                    }

                    Rect r = new Rect(
                        keypos + sharedOffset - k_EventMarkerSize.x / 2,
                        (rect.height - 10) * (float)(sharedLeft - shared + 1) / Mathf.Max(1, shared - 1),
                        k_EventMarkerSize.x,
                        k_EventMarkerSize.y);

                    hitRects[i] = r;
                    drawRects[i] = r;
                }

                // Store tooptip info
                if (m_DirtyTooltip)
                {
                    if (m_HoverEvent >= 0 && m_HoverEvent < hitRects.Length)
                    {
                        m_InstantTooltipText = AnimationWindowEventInspector.FormatEvent(animated, events[m_HoverEvent]);
                        m_InstantTooltipPoint = new Vector2(hitRects[m_HoverEvent].xMin + (int)(hitRects[m_HoverEvent].width / 2) + rect.x - 30, rect.yMax);
                    }
                    m_DirtyTooltip = false;
                }

                bool[] selectedEvents = new bool[events.Length];
                m_HasSelectedEvents = false;

                Object[] selectedObjects = Selection.objects;
                foreach (Object selectedObject in selectedObjects)
                {
                    AnimationWindowEvent awe = selectedObject as AnimationWindowEvent;
                    if (awe != null)
                    {
                        if (awe.eventIndex >= 0 && awe.eventIndex < selectedEvents.Length)
                        {
                            selectedEvents[awe.eventIndex] = true;
                            m_HasSelectedEvents = true;
                        }
                    }
                }

                Vector2 offset = Vector2.zero;
                int clickedIndex;
                float startSelection, endSelection;

                // TODO: GUIStyle.none has hopping margins that need to be fixed
                HighLevelEvent hEvent = EditorGUIExt.MultiSelection(
                    rect,
                    drawRects,
                    new GUIContent(eventMarker),
                    hitRects,
                    ref selectedEvents,
                    null,
                    out clickedIndex,
                    out offset,
                    out startSelection,
                    out endSelection,
                    GUIStyle.none
                );

                if (hEvent != HighLevelEvent.None)
                {
                    switch (hEvent)
                    {
                        case HighLevelEvent.BeginDrag:
                            m_EventsAtMouseDown = events;
                            m_EventTimes = new float[events.Length];
                            for (int i = 0; i < events.Length; i++)
                                m_EventTimes[i] = events[i].time;
                            break;
                        case HighLevelEvent.SelectionChanged:
                            state.ClearKeySelections();
                            EditEvents(animated, clip, selectedEvents);
                            break;
                        case HighLevelEvent.Delete:
                            DeleteEvents(clip, selectedEvents);
                            break;
                        case HighLevelEvent.Copy:
                            CopyEvents(clip, selectedEvents);
                            break;
                        case HighLevelEvent.Paste:
                            PasteEvents(animated, clip, state.currentTime);
                            break;

                        case HighLevelEvent.DoubleClick:

                            if (clickedIndex != -1)
                                EditEvents(animated, clip, selectedEvents);
                            else
                                EventLineContextMenuAdd(new EventLineContextMenuObject(animated, clip, mousePosTime, -1, selectedEvents));
                            break;
                        case HighLevelEvent.Drag:
                            for (int i = events.Length - 1; i >= 0; i--)
                            {
                                if (selectedEvents[i])
                                {
                                    AnimationEvent evt = m_EventsAtMouseDown[i];
                                    evt.time = m_EventTimes[i] + offset.x * state.PixelDeltaToTime(rect);
                                    evt.time = Mathf.Max(0.0F, evt.time);
                                    evt.time = Mathf.RoundToInt(evt.time * clip.frameRate) / clip.frameRate;
                                }
                            }
                            int[] order = new int[selectedEvents.Length];
                            for (int i = 0; i < order.Length; i++)
                            {
                                order[i] = i;
                            }
                            System.Array.Sort(m_EventsAtMouseDown, order, new EventComparer());
                            bool[] selectedOld = (bool[])selectedEvents.Clone();
                            float[] timesOld = (float[])m_EventTimes.Clone();
                            for (int i = 0; i < order.Length; i++)
                            {
                                selectedEvents[i] = selectedOld[order[i]];
                                m_EventTimes[i] = timesOld[order[i]];
                            }

                            // Update selection to reflect new order.
                            EditEvents(animated, clip, selectedEvents);

                            Undo.RegisterCompleteObjectUndo(clip, "Move Event");
                            AnimationUtility.SetAnimationEvents(clip, m_EventsAtMouseDown);
                            m_DirtyTooltip = true;
                            break;
                        case HighLevelEvent.ContextClick:
                            CreateContextMenu(animated, clip, events[clickedIndex].time, clickedIndex, selectedEvents);

                            // Mouse may move while context menu is open - make sure instant tooltip is handled
                            m_InstantTooltipText = null;
                            m_DirtyTooltip = true;
                            state.Repaint();
                            break;
                    }
                }

                CheckRectsOnMouseMove(rect, events, hitRects);

                // Bring up menu when context-clicking on an empty timeline area (context-clicking on events is handled above)
                if (Event.current.type == EventType.ContextClick && eventLineRect.Contains(Event.current.mousePosition))
                {
                    Event.current.Use();
                    CreateContextMenu(animated, clip, mousePosTime, -1, selectedEvents);
                }
            }

            GUI.color = backupCol;
            GUI.EndGroup();
        }

        void CreateContextMenu(GameObject animatedGo, AnimationClip clip, float time, int eventIndex, bool[] selectedEvents)
        {
            GenericMenu menu = new GenericMenu();
            var contextData = new EventLineContextMenuObject(animatedGo, clip, time, eventIndex, selectedEvents);
            var selectedCount = selectedEvents.Count(selected => selected);

            menu.AddItem(Styles.textAddEvent, false, EventLineContextMenuAdd, contextData);
            if (selectedCount > 0 || eventIndex != -1)
            {
                menu.AddItem(selectedCount > 1 ? Styles.textDeleteEvents : Styles.textDeleteEvent, false, EventLineContextMenuDelete, contextData);
                menu.AddItem(Styles.textCopyEvents, false, EventLineContextMenuCopy, contextData);
            }
            else
            {
                menu.AddDisabledItem(Styles.textDeleteEvents);
                menu.AddDisabledItem(Styles.textCopyEvents);
            }
            if (AnimationWindowEventsClipboard.CanPaste())
                menu.AddItem(Styles.textPasteEvents, false, EventLineContextMenuPaste, contextData);
            else
                menu.AddDisabledItem(Styles.textPasteEvents);
            menu.ShowAsContext();
        }

        public void DrawInstantTooltip(Rect position)
        {
            if (!string.IsNullOrEmpty(m_InstantTooltipText))
            {
                // Draw body of tooltip
                GUIStyle style = (GUIStyle)"AnimationEventTooltip";

                // TODO: Move to editor_resources
                style.contentOffset = new Vector2(0f, 0f);
                style.overflow = new RectOffset(10, 10, 0, 0);

                Vector2 size = style.CalcSize(new GUIContent(m_InstantTooltipText));
                Rect rect = new Rect(m_InstantTooltipPoint.x - size.x * .5f, m_InstantTooltipPoint.y + 24, size.x, size.y);

                // Right align tooltip rect if it would otherwise exceed the bounds of the window
                if (rect.xMax > position.width)
                    rect.x = position.width - rect.width;

                GUI.Label(rect, m_InstantTooltipText, style);

                // Draw arrow of tooltip
                rect = new Rect(m_InstantTooltipPoint.x - 33, m_InstantTooltipPoint.y, 7, 25);
                GUI.Label(rect, "", "AnimationEventTooltipArrow");
            }
        }

        public void EventLineContextMenuAdd(object obj)
        {
            EventLineContextMenuObject eventObj = (EventLineContextMenuObject)obj;
            AddEvent(eventObj.m_Time, eventObj.m_Animated, eventObj.m_Clip);
        }

        public void EventLineContextMenuEdit(object obj)
        {
            EventLineContextMenuObject eventObj = (EventLineContextMenuObject)obj;

            if (Array.Exists(eventObj.m_Selected, selected => selected))
            {
                EditEvents(eventObj.m_Animated, eventObj.m_Clip, eventObj.m_Selected);
            }
            else if (eventObj.m_Index >= 0)
            {
                EditEvent(eventObj.m_Animated, eventObj.m_Clip, eventObj.m_Index);
            }
        }

        public void EventLineContextMenuDelete(object obj)
        {
            EventLineContextMenuObject eventObj = (EventLineContextMenuObject)obj;
            AnimationClip clip = eventObj.m_Clip;
            if (clip == null)
                return;

            int clickedIndex = eventObj.m_Index;

            // If a selection already exists, delete selection instead of clicked index
            if (Array.Exists(eventObj.m_Selected, selected => selected))
            {
                DeleteEvents(clip, eventObj.m_Selected);
            }
            // Else, only delete the clicked animation event
            else if (clickedIndex >= 0)
            {
                bool[] deleteIndices = new bool[eventObj.m_Selected.Length];
                deleteIndices[clickedIndex] = true;
                DeleteEvents(clip, deleteIndices);
            }
        }

        void EventLineContextMenuCopy(object obj)
        {
            var ctx = (EventLineContextMenuObject)obj;
            var clip = ctx.m_Clip;
            if (clip != null)
                CopyEvents(clip, ctx.m_Selected, ctx.m_Index);
        }

        void EventLineContextMenuPaste(object obj)
        {
            var ctx = (EventLineContextMenuObject)obj;
            AnimationClip clip = ctx.m_Clip;
            if (clip != null)
                PasteEvents(ctx.m_Animated, clip, ctx.m_Time);
        }

        private void CheckRectsOnMouseMove(Rect eventLineRect, AnimationEvent[] events, Rect[] hitRects)
        {
            Vector2 mouse = Event.current.mousePosition;
            bool hasFound = false;

            if (events.Length == hitRects.Length)
            {
                for (int i = hitRects.Length - 1; i >= 0; i--)
                {
                    if (hitRects[i].Contains(mouse))
                    {
                        hasFound = true;
                        if (m_HoverEvent != i)
                        {
                            m_HoverEvent = i;
                            m_InstantTooltipText = events[m_HoverEvent].functionName;
                            m_InstantTooltipPoint = new Vector2(hitRects[m_HoverEvent].xMin + (int)(hitRects[m_HoverEvent].width / 2) + eventLineRect.x, eventLineRect.yMax);
                            m_DirtyTooltip = true;
                        }
                    }
                }
            }
            if (!hasFound)
            {
                m_HoverEvent = -1;
                m_InstantTooltipText = "";
            }
        }
    }
} // namespace
