using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Riry.AnimatorTemplates.Editor.Macs
{
    /// <summary>
    /// Opened from the Riry submenu: one row per parameter the template uses, each renamable by typing or by picking from
    /// MACS's fuzzy list of the controller's parameters (shown under the focused row). Insert checks for parameters the
    /// controller lacks and asks before adding them, makes the AAP clips with MACS's generator, then inserts.
    /// </summary>
    internal sealed class MacsTemplateWindow : EditorWindow
    {
        private const string FieldPrefix = "riry.macs.parameter.";
        private const float FuzzyHeight = 140f;

        private MacsTemplateRequest _request;
        private List<string> _originals = new List<string>();
        private List<string> _chosen = new List<string>();
        private bool _direct;
        private int _weightRow = -1;
        private int _focusedRow = -1;
        private int _focusNext = -1;
        private Vector2 _scroll;
        private Vector2 _fuzzyScroll;
        private readonly Dictionary<string, string> _aapNotes = new Dictionary<string, string>();

        internal static void Open(MacsTemplateRequest request)
        {
            var window = CreateInstance<MacsTemplateWindow>();
            window._request = request;
            window.titleContent = new GUIContent("Insert " + request.Template.name);
            window._originals = MacsTemplateInsert.UsedParameters(request.Template);
            window._chosen = new List<string>(window._originals);
            window._direct = request.Target.blendType == BlendTreeType.Direct;
            if (window._direct)
            {
                window._weightRow = window._originals.Count;
                window._originals.Add(null);
                window._chosen.Add(MacsTemplateInsert.DefaultDirectWeight());
            }
            foreach (AnimationClip clip in MacsTemplateInsert.Clips(request.Template))
            {
                if (!MacsTemplateInsert.IsAap(clip, out string parameter, out float value)) continue;
                window._aapNotes[parameter] = window._aapNotes.TryGetValue(parameter, out string values) ? values + ", " + value : value.ToString();
            }
            window.minSize = new Vector2(360, 240);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (_request == null || _request.Template == null || _request.Target == null || _request.Controller == null)
            {
                // After a script reload, or the template, blend tree or controller was deleted.
                EditorGUILayout.HelpBox("The template, blend tree or controller no longer exists. Open the template again from the Riry menu.", MessageType.Info);
                if (GUILayout.Button("Close")) Close();
                return;
            }
            // Which row's fuzzy list shows is fixed for the whole GUI pass, so Layout and Repaint draw the same controls.
            if (Event.current.type == EventType.Layout)
            {
                // The last focused row keeps its list, so a click on the list still lands if the field lost focus.
                int focused = FocusedRow();
                if (focused >= 0) _focusedRow = focused;
                if (_focusNext >= 0)
                {
                    EditorGUI.FocusTextInControl(FieldPrefix + _focusNext);
                    _focusNext = -1;
                }
            }

            EditorGUILayout.LabelField("Into " + _request.Target.name + " in " + _request.Controller.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Type a name, or pick one from the list under the field. Double-click to pick and move on.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            var parameters = _request.Controller.parameters;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int row = 0; row < _chosen.Count; row++)
            {
                string label = row == _weightRow ? "Weight in " + _request.Target.name : _originals[row];
                string tooltip = row == _weightRow
                    ? "The parameter that weights the inserted template in this Direct blend tree."
                    : (_aapNotes.TryGetValue(_originals[row], out string values)
                        ? "Also animated by the template's AAP clips (values " + values + "): they are made again for this name."
                        : "Used by the template's blend trees.");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.MinWidth(0));
                GUI.SetNextControlName(FieldPrefix + row);
                _chosen[row] = EditorGUILayout.TextField(_chosen[row], GUILayout.MinWidth(0));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(Status(parameters, _chosen[row]), EditorStyles.miniLabel);

                if (row == _focusedRow)
                {
                    _fuzzyScroll = EditorGUILayout.BeginScrollView(_fuzzyScroll, GUILayout.Height(FuzzyHeight));
                    string picked = MacsFunctions.FuzzyList(_request.Controller, position.width - 30, _chosen[row], out bool accepted);
                    EditorGUILayout.EndScrollView();
                    if (picked != _chosen[row])
                    {
                        _chosen[row] = picked;
                        // The field keeps showing its own edit text while focused: refocus so it shows the pick.
                        GUI.FocusControl(null);
                        _focusNext = row;
                        Repaint();
                    }
                    if (accepted)
                    {
                        _focusNext = (row + 1) % _chosen.Count;
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_chosen.Any(string.IsNullOrEmpty)))
            {
                if (GUILayout.Button("Insert")) Insert();
            }
            if (GUILayout.Button("Cancel")) Close();
            EditorGUILayout.EndHorizontal();
        }

        private int FocusedRow()
        {
            string focused = GUI.GetNameOfFocusedControl();
            return focused != null && focused.StartsWith(FieldPrefix) && int.TryParse(focused.Substring(FieldPrefix.Length), out int row) ? row : -1;
        }

        private static string Status(AnimatorControllerParameter[] parameters, string name)
        {
            if (string.IsNullOrEmpty(name)) return "A name is needed.";
            AnimatorControllerParameter existing = parameters.FirstOrDefault(p => p.name == name);
            if (existing == null) return "Not in the controller: Insert asks to add it.";
            return existing.type == AnimatorControllerParameterType.Float
                ? "In the controller."
                : "In the controller as " + existing.type + "; blend trees need a Float.";
        }

        private void Insert()
        {
            var names = new Dictionary<string, string>();
            for (int row = 0; row < _chosen.Count; row++)
            {
                if (row != _weightRow) names[_originals[row]] = _chosen[row];
            }
            string weight = _direct ? _chosen[_weightRow] : null;
            MacsTemplateRequest request = _request;
            if (MacsTemplateInsert.Run(request, names, weight,
                () => MacsTemplateInsert.MakeAapClips(request.Template, request.Controller, names)))
                Close();
            GUIUtility.ExitGUI();
        }
    }
}

// ============================================================================
// GLOSSARY: MACS template window
// ============================================================================
//
// MacsTemplateWindow (this file)
//   Open(request)            Utility window: one row per MacsTemplateInsert.UsedParameters name, plus "Weight in <tree>"
//                            (default UM_DefaultDirectBlendTreeWeightParameter) when the clicked tree is Direct.
//   _originals / _chosen     The template's names (null for the weight row) and the names to use.
//   _focusedRow              The row whose text field last had focus, fixed per GUI pass in Layout: its MACS fuzzy list
//                            (MacsFunctions.FuzzyList) is drawn under it. A pick replaces the text; a double-click also
//                            moves focus to the next row (_focusNext), as in MACS's smoothing window.
//   Status                   In the controller / not in it (Insert asks to add it) / wrong type.
//   _aapNotes                Values of the template's AAP clips per parameter, shown in the row's tooltip.
//   Insert                   MacsTemplateInsert.Run with MakeAapClips; closes when something was inserted.
