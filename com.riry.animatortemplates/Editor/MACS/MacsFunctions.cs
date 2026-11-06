using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Riry.AnimatorTemplates.Editor.Macs
{
    /// <summary>
    /// MACS's AAP clip generator and fuzzy parameter list. MACS 1.7.10 keeps them internal under obfuscated names (class
    /// A.B in assembly MACS), so they are found by exact signature. When a MACS update renames them, a copy of the same
    /// behaviour here is used instead and a warning says so once per session.
    /// </summary>
    internal static class MacsFunctions
    {
        private const string MacsAssembly = "MACS";
        private const string UtilityType = "A.B";
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _resolved;
        private static MethodInfo _aapClip;     // A.B.A(AnimatorController, string, float): AnimationClip, reuse or create
        private static MethodInfo _fuzzyList;   // A.B.A(AnimatorController, float, string): A.B.q
        private static FieldInfo _fuzzyText;    // A.B.q.Bn: the query, or the clicked parameter
        private static FieldInfo _fuzzyAccepted; // A.B.q.BO: the click was a double click

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            Assembly macs = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == MacsAssembly);
            Type utility = macs != null ? macs.GetType(UtilityType, false) : null;
            if (utility == null)
            {
                Fallback(MacsAssembly + " " + UtilityType + " was not found");
                return;
            }
            _aapClip = utility.GetMethod("A", Statics, null, new[] { typeof(AnimatorController), typeof(string), typeof(float) }, null);
            if (_aapClip != null && _aapClip.ReturnType != typeof(AnimationClip)) _aapClip = null;
            if (_aapClip == null) Fallback("MACS's AAP clip generator (A.B.A) was not found");

            _fuzzyList = utility.GetMethod("A", Statics, null, new[] { typeof(AnimatorController), typeof(float), typeof(string) }, null);
            if (_fuzzyList != null)
            {
                _fuzzyText = _fuzzyList.ReturnType.GetField("Bn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _fuzzyAccepted = _fuzzyList.ReturnType.GetField("BO", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_fuzzyText == null || _fuzzyText.FieldType != typeof(string) || _fuzzyAccepted == null || _fuzzyAccepted.FieldType != typeof(bool))
                    _fuzzyList = null;
            }
            if (_fuzzyList == null) Fallback("MACS's fuzzy parameter list (A.B.A) was not found");
        }

        /// <summary>
        /// The AAP clip for a parameter and value, as MACS's Create AAP Clip makes it: the existing
        /// &lt;controller folder&gt;/Animations/AAP {parameter} {value}.anim, or a new one saved there. Never part of an undo step.
        /// </summary>
        internal static AnimationClip AapClip(AnimatorController controller, string parameter, float value)
        {
            Resolve();
            if (_aapClip != null)
            {
                try
                {
                    var clip = _aapClip.Invoke(null, new object[] { controller, parameter, value }) as AnimationClip;
                    if (clip != null) return clip;
                    Fallback("MACS's AAP clip generator returned no clip");
                }
                catch (Exception exception)
                {
                    _aapClip = null;
                    Fallback("MACS's AAP clip generator failed (" + (exception.InnerException ?? exception).Message + ")");
                }
            }
            return CopyAapClip(controller, parameter, value);
        }

        /// <summary>
        /// Draws the fuzzy list of the controller's parameters under a text field, as MACS's AAP window does. Returns the
        /// field's new text (a clicked parameter replaces the typed text) and whether a parameter was double-clicked.
        /// </summary>
        internal static string FuzzyList(AnimatorController controller, float width, string query, out bool accepted)
        {
            Resolve();
            if (_fuzzyList != null)
            {
                try
                {
                    object result = _fuzzyList.Invoke(null, new object[] { controller, width, query ?? "" });
                    accepted = (bool)_fuzzyAccepted.GetValue(result);
                    return (string)_fuzzyText.GetValue(result);
                }
                catch (Exception exception)
                {
                    _fuzzyList = null;
                    Fallback("MACS's fuzzy parameter list failed (" + (exception.InnerException ?? exception).Message + ")");
                    // Its layout calls were cut short: stop this GUI pass, the next one uses the copy.
                    GUIUtility.ExitGUI();
                }
            }
            return CopyFuzzyList(controller.parameters.Select(p => p.name), query ?? "", out accepted);
        }

        private static void Fallback(string why)
        {
            string key = MacsTemplateMenu.HarmonyId + ".macsfunctions." + why;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogWarning(MacsTemplateMenu.LogPrefix + why + ", so this package's copy of it is used. MACS may have changed; update this package.");
        }

        // ---- Copies of MACS 1.7.10's behaviour ----

        private static AnimationClip CopyAapClip(AnimatorController controller, string parameter, float value)
        {
            string name = string.Format("AAP {0} {1}", parameter, value);
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string folder = Path.GetDirectoryName(controllerPath).Replace('\\', '/') + "/Animations";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(folder + "/" + name + ".anim");
            if (existing != null) return existing;

            var clip = new AnimationClip { name = name };
            clip.SetCurve("", typeof(Animator), parameter, AnimationCurve.Constant(0, 0, value));
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), "Animations");
                if (!AssetDatabase.IsValidFolder(folder)) throw new IOException("Failed to create folder " + folder);
                Debug.Log(MacsTemplateMenu.LogPrefix + "Created folder at " + folder);
            }
            string file = name;
            foreach (char invalid in Path.GetInvalidFileNameChars()) file = file.Replace(invalid, '_');
            string path = folder + "/" + file + ".anim";
            for (int n = 1; File.Exists(path); n++) path = folder + "/" + file + " (" + n + ").anim";
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static GUIStyle _resultStyle;

        private static string CopyFuzzyList(IEnumerable<string> candidates, string query, out bool accepted)
        {
            accepted = false;
            if (_resultStyle == null) _resultStyle = new GUIStyle(EditorStyles.label) { richText = true };
            string text = query;
            foreach (string candidate in candidates)
            {
                if (!Match(query, candidate, out int start1, out int length1, out int start2, out int length2)) continue;
                Rect rect = GUILayoutUtility.GetRect(new GUIContent(candidate), _resultStyle);
                Event current = Event.current;
                if (current.type == EventType.Repaint)
                    _resultStyle.Draw(rect, new GUIContent(Highlight(candidate, start1, length1, start2, length2)), false, false, false, false);
                else if (current.type == EventType.MouseUp && rect.Contains(current.mousePosition))
                {
                    text = candidate;
                    accepted = current.clickCount == 2;
                    current.Use();
                }
            }
            return text;
        }

        /// <summary>
        /// MACS's matching: the longest in-order run of the query's start inside the candidate (ignoring case), and, when that
        /// is not the whole query, the rest of the query after that run or else before it. At most two segments.
        /// </summary>
        internal static bool Match(string query, string candidate, out int start1, out int length1, out int start2, out int length2)
        {
            start1 = length1 = start2 = length2 = 0;
            if (string.IsNullOrEmpty(query)) return true;
            if (string.IsNullOrEmpty(candidate)) return false;
            TextInfo lower = CultureInfo.CurrentCulture.TextInfo;
            string q = lower.ToLower(query), c = lower.ToLower(candidate);
            int best = 0;
            var starts = new List<int>();
            for (int s = 0; s < c.Length; s++)
            {
                int length = 0;
                while (length < q.Length && s + length < c.Length && c[s + length] == q[length]) length++;
                if (length == 0) continue;
                if (length > best) { best = length; starts.Clear(); }
                if (length == best) starts.Add(s);
            }
            if (best == 0) return false;
            if (best == q.Length)
            {
                start1 = starts[0];
                length1 = best;
                return true;
            }
            string rest = query.Substring(best);
            foreach (int s in starts)
            {
                int after = candidate.IndexOf(rest, s + best, StringComparison.OrdinalIgnoreCase);
                int before = after < 0 ? candidate.Substring(0, s).IndexOf(rest, StringComparison.OrdinalIgnoreCase) : -1;
                int at = after >= 0 ? after : before;
                if (at < 0) continue;
                start1 = s;
                length1 = best;
                start2 = at;
                length2 = rest.Length;
                return true;
            }
            return false;
        }

        private static string Highlight(string candidate, int start1, int length1, int start2, int length2)
        {
            var segments = new List<(int start, int length)> { (start1, length1), (start2, length2) }
                .Where(s => s.length > 0).OrderBy(s => s.start).ToList();
            var result = new System.Text.StringBuilder();
            int at = 0;
            foreach (var (start, length) in segments)
            {
                if (start < at) continue;
                result.Append(candidate, at, start - at).Append("<color=cyan>").Append(candidate, start, length).Append("</color>");
                at = start + length;
            }
            return result.Append(candidate.Substring(at)).ToString();
        }
    }
}

// ============================================================================
// GLOSSARY: MACS functions
// ============================================================================
//
// MacsFunctions (this file)
//   Resolve                  Finds MACS 1.7.10's internal class A.B (assembly MACS) and, by exact signature, its
//                            reuse-or-create AAP clip generator A(AnimatorController, string, float): AnimationClip and
//                            its fuzzy parameter list A(AnimatorController, float, string), whose result has Bn (text)
//                            and BO (double-clicked). The names are the obfuscator's.
//   AapClip                  MACS's AAP clip: "AAP {parameter} {value}", one constant curve on the Animator property named
//                            after the parameter, saved in <controller folder>/Animations; an existing one is reused.
//                            Created outside any undo step, so Ctrl+Z never deletes it.
//   FuzzyList                MACS's fuzzy list of the controller's parameters, drawn under a text field: a click
//                            replaces the text, a double click also accepts it. Typed names that are not parameters stay.
//   Fallback                 A MACS member missing or failing: warned once per session; the copies below are used.
//   CopyAapClip / CopyFuzzyList / Match / Highlight
//                            This package's copies of that MACS behaviour (same name, curve, folder, " (n)" suffix,
//                            matching and cyan highlight).
