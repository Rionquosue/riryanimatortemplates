using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Riry.AnimatorTemplates.Editor.Macs
{
    /// <summary>
    /// Inserts a copy of a template blend tree into a blend tree, with the template's parameters renamed. Everything it
    /// changes or creates in the controller (added parameters, the copied blend trees, the new child) is one undo step.
    /// The template itself is only read.
    /// </summary>
    internal static class MacsTemplateInsert
    {
        // MACS's preferences for where new blend trees go (read only; MACS owns them).
        private const string ExternalPref = "UM_CreateExternalBlendTrees";
        private const string OutputFolderPref = "UM_BlendTreeOutputDirectory";
        private const string OutputFolderAbsolutePref = "UM_BlendTreeOutputDirectoryIsAbsolute";
        private const string DirectWeightPref = "UM_DefaultDirectBlendTreeWeightParameter";

        /// <summary>The weight parameter MACS gives a child it adds to a Direct blend tree.</summary>
        internal static string DefaultDirectWeight() => EditorPrefs.GetString(DirectWeightPref, "Always 1");

        /// <summary>Every parameter the template's blend trees read, in the order first met.</summary>
        internal static List<string> UsedParameters(BlendTree template)
        {
            var names = new List<string>();
            var seen = new HashSet<BlendTree>();
            void Walk(BlendTree tree)
            {
                if (tree == null || !seen.Add(tree)) return;
                if (tree.blendType == BlendTreeType.Direct)
                {
                    foreach (ChildMotion child in tree.children) Add(names, child.directBlendParameter);
                }
                else
                {
                    Add(names, tree.blendParameter);
                    if (tree.blendType != BlendTreeType.Simple1D) Add(names, tree.blendParameterY);
                }
                foreach (ChildMotion child in tree.children) Walk(child.motion as BlendTree);
            }
            Walk(template);
            return names;
        }

        private static void Add(List<string> names, string name)
        {
            if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
        }

        /// <summary>The names in <paramref name="wanted"/> that the controller has no parameter for, each once.</summary>
        internal static List<string> Missing(AnimatorController controller, IEnumerable<string> wanted)
        {
            var have = new HashSet<string>(controller.parameters.Select(p => p.name));
            return wanted.Where(n => !string.IsNullOrEmpty(n) && !have.Contains(n)).Distinct().ToList();
        }

        /// <summary>
        /// Checks for parameters the controller lacks and asks whether to add them, then inserts. Returns false when nothing
        /// was changed (cancelled, or the request is no longer valid).
        /// </summary>
        internal static bool Run(MacsTemplateRequest request, IDictionary<string, string> names, string weight,
            IDictionary<Motion, Motion> replacements)
        {
            if (request.Template == null || request.Target == null || request.Controller == null)
            {
                Debug.LogWarning(MacsTemplateMenu.LogPrefix + "The template, blend tree or controller no longer exists; nothing was inserted.");
                return false;
            }
            bool direct = request.Target.blendType == BlendTreeType.Direct;
            var wanted = new List<string>(names.Values);
            if (direct) wanted.Add(weight);
            List<string> missing = Missing(request.Controller, wanted);
            bool addMissing = false;
            if (missing.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex("Insert " + request.Template.name,
                    request.Controller.name + " has no parameter named:\n\n" + string.Join("\n", missing) +
                    "\n\nAdd them to its parameter list as Float parameters?", "Add and insert", "Cancel", "Insert without adding");
                if (choice == 1) return false;
                addMissing = choice == 0;
            }
            Insert(request, names, direct ? weight : null, replacements, addMissing ? missing : new List<string>());
            return true;
        }

        /// <summary>The insertion itself, as one undo group named after the template.</summary>
        internal static BlendTree Insert(MacsTemplateRequest request, IDictionary<string, string> names, string weight,
            IDictionary<Motion, Motion> replacements, List<string> addParameters)
        {
            string label = "Insert template " + request.Template.name;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            try
            {
                if (addParameters.Count > 0)
                {
                    Undo.RegisterCompleteObjectUndo(request.Controller, label);
                    foreach (string name in addParameters) request.Controller.AddParameter(name, AnimatorControllerParameterType.Float);
                    EditorUtility.SetDirty(request.Controller);
                }

                var created = new List<BlendTree>();
                BlendTree copy = Copy(request.Template, request.Target, request.Controller, names, replacements,
                    new Dictionary<BlendTree, BlendTree>(), created, label);

                Undo.RegisterCompleteObjectUndo(request.Target, label);
                request.Target.AddChild(copy);
                ChildMotion[] children = request.Target.children;
                if (weight != null) children[children.Length - 1].directBlendParameter = weight;
                request.Target.children = children;
                SetNewThresholdOnLastChild(request.Target);
                EditorUtility.SetDirty(request.Target);
                foreach (BlendTree tree in created) EditorUtility.SetDirty(tree);

                Debug.Log(MacsTemplateMenu.LogPrefix + "Inserted template " + request.Template.name + " into " + request.Target.name +
                    " (" + created.Count + " blend tree" + (created.Count == 1 ? "" : "s") +
                    (addParameters.Count > 0 ? ", added parameters " + string.Join(", ", addParameters) : "") + ").", request.Target);
                if (request.Host != null) request.Host.Repaint();
                return copy;
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        // Copies a blend tree and the blend trees under it; clips are shared (or swapped per replacements). Each copy is
        // saved where MACS would save a new blend tree under that parent, and registered as created for the undo step.
        private static BlendTree Copy(BlendTree source, BlendTree parent, AnimatorController controller, IDictionary<string, string> names,
            IDictionary<Motion, Motion> replacements, Dictionary<BlendTree, BlendTree> copies, List<BlendTree> created, string label)
        {
            if (copies.TryGetValue(source, out BlendTree done)) return done;
            BlendTree copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            copy.blendParameter = Rename(names, source.blendParameter);
            copy.blendParameterY = Rename(names, source.blendParameterY);
            copies[source] = copy;
            Persist(copy, parent, controller);
            Undo.RegisterCreatedObjectUndo(copy, label);
            created.Add(copy);

            ChildMotion[] children = source.children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].directBlendParameter = Rename(names, children[i].directBlendParameter);
                Motion motion = children[i].motion;
                if (motion is BlendTree tree) children[i].motion = Copy(tree, copy, controller, names, replacements, copies, created, label);
                else if (motion != null && replacements != null && replacements.TryGetValue(motion, out Motion replacement)) children[i].motion = replacement;
            }
            copy.children = children;
            return copy;
        }

        private static string Rename(IDictionary<string, string> names, string name)
        {
            return name != null && names.TryGetValue(name, out string renamed) && !string.IsNullOrEmpty(renamed) ? renamed : name;
        }

        /// <summary>
        /// MACS's placement: under a blend tree saved in its own asset, into that asset; otherwise, with MACS's external blend
        /// trees on, as a new asset in its output folder; otherwise inside the controller, hidden as Unity does.
        /// </summary>
        private static void Persist(BlendTree tree, BlendTree parent, AnimatorController controller)
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            string parentPath = AssetDatabase.GetAssetPath(parent);
            if (parentPath != "" && parentPath != controllerPath)
            {
                tree.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(tree, parentPath);
                return;
            }
            if (controllerPath == "") return;
            if (EditorPrefs.GetBool(ExternalPref, false))
            {
                string folder = OutputFolder(controllerPath);
                if (EnsureFolder(folder))
                {
                    tree.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(tree, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + tree.name + ".asset"));
                    return;
                }
                Debug.LogWarning(MacsTemplateMenu.LogPrefix + "MACS's blend tree folder " + folder + " could not be made, so " + tree.name +
                    " is saved inside " + controller.name + ".", controller);
            }
            tree.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(tree, controllerPath);
        }

        private static string OutputFolder(string controllerPath)
        {
            string folder = EditorPrefs.GetString(OutputFolderPref, "BlendTrees").Replace('\\', '/').Trim('/');
            if (EditorPrefs.GetBool(OutputFolderAbsolutePref, false)) return folder;
            int slash = controllerPath.LastIndexOf('/');
            return (slash > 0 ? controllerPath.Substring(0, slash) : "Assets") + (folder == "" ? "" : "/" + folder);
        }

        private static bool EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return true;
            int slash = folder.LastIndexOf('/');
            if (slash <= 0) return false;
            string parent = folder.Substring(0, slash);
            if (!EnsureFolder(parent)) return false;
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
            return AssetDatabase.IsValidFolder(folder);
        }

        // Unity's own threshold for a child added to a 1D tree with manual thresholds (private in its blend tree graph GUI).
        private static void SetNewThresholdOnLastChild(BlendTree tree)
        {
            Type type = AccessTools.TypeByName("UnityEditor.Graphs.AnimationBlendTree.GraphGUI");
            MethodInfo method = type != null ? AccessTools.Method(type, "SetNewThresholdOnLastChild", new[] { typeof(BlendTree) }) : null;
            if (method != null) method.Invoke(null, new object[] { tree });
            else if (!tree.useAutomaticThresholds)
                MacsTemplateMenu.Unavailable("SetNewThresholdOnLastChild was not found", ", so an inserted template's threshold is left at 0.");
        }
    }
}

// ============================================================================
// GLOSSARY: MACS template insertion
// ============================================================================
//
// MacsTemplateInsert (this file)
//   UsedParameters(template) Every parameter the template's blend trees read: blendParameter (1D and 2D),
//                            blendParameterY (2D), each child's directBlendParameter (Direct).
//   Missing(controller, ...) Names the controller has no parameter for.
//   Run                      Asks (Add and insert / Cancel / Insert without adding) when parameters are missing, then
//                            Insert. weight: the new child's directBlendParameter when the clicked tree is Direct.
//   Insert                   One undo group (CollapseUndoOperations): adds the parameters (Float), copies the template
//                            (Copy) and adds the copy as the clicked tree's last child. A single Ctrl+Z removes all of it.
//   Copy                     Instantiate per blend tree, parameters renamed (names: template name -> chosen name),
//                            clips shared or swapped (replacements), RegisterCreatedObjectUndo each.
//   Persist                  MACS's placement for a new blend tree (UM_CreateExternalBlendTrees, UM_BlendTreeOutputDirectory,
//                            UM_BlendTreeOutputDirectoryIsAbsolute): parent's own asset, MACS's output folder, or the
//                            controller (HideInHierarchy).
//   DefaultDirectWeight      UM_DefaultDirectBlendTreeWeightParameter (MACS default "Always 1").
//   SetNewThresholdOnLastChild  Unity's private threshold rule for manual 1D thresholds, by reflection.
