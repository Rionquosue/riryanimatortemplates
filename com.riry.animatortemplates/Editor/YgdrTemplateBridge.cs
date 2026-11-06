/*
    Riry's Animator Templates: YGDR Animator Editor template bridge
    Copyright (C) 2026  Riry

    This file is free software: you can redistribute it and/or modify it under the terms
    of the GNU General Public License as published by the Free Software Foundation,
    either version 3 of the License, or (at your option) any later version.

    This file is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
    without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR
    PURPOSE. See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this
    file (GPL-3.0.md beside it). If not, see <https://www.gnu.org/licenses/>.

    The templates themselves, and the rest of this package, are under the MIT licence
    (LICENSE at the repository root).

    It patches YGDR Animator Editor (GPL-3.0), whose source is at
    https://github.com/YerGodDamnRight/YGDR-Animator-Editor
    (Packages/com.ygdr.animator/Editor/Patches/Window/AnimatorLayerTemplatePatch.cs).
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Riry.AnimatorTemplates.Editor
{
    /// <summary>
    /// Adds this package's templates to YGDR Animator Editor's template menus: both kinds it has (1.2.1), layer
    /// templates and blend tree templates. The editor only
    /// looks in its own package and in Assets/YGDR Animator/User Templates, so its two loaders
    /// get a Harmony postfix that appends every animator controller (layer templates) and every
    /// blend tree saved as its own asset (blend tree templates) under Templates/YGDR here. They show
    /// as package templates: among YGDR's own, never under User/ or Delete User Template.
    /// Compiled only when YGDR Animator Editor and the VRChat SDK base (which brings Harmony)
    /// are installed (see the assembly definition). If an update of the editor renames what is
    /// patched, nothing is added and a warning is logged once per session.
    /// </summary>
    [InitializeOnLoad]
    internal static class YgdrTemplateBridge
    {
        internal const string TemplatesFolder = "Packages/com.riry.animatortemplates/Templates/YGDR";
        private const string HarmonyId = "com.riry.animatortemplates.ygdr";
        private const string LogPrefix = "[Riry's Animator Templates] ";
        private const string ToolbarType = "YGDR.Editor.Animation.PatchLayerToolbar";
        private const string LayerLoader = "LoadTemplateControllers";
        private const string BlendTreeLoader = "LoadBlendTreeTemplateAssets";
        private const string LayerCache = "_templateCache";
        private const string BlendTreeCache = "_blendTreeTemplateCache";

        private static readonly Type Toolbar = AccessTools.TypeByName(ToolbarType);

        static YgdrTemplateBridge()
        {
            if (Toolbar == null)
            {
                Unavailable(ToolbarType + " was not found");
                return;
            }
            var harmony = new Harmony(HarmonyId);
            Patch(harmony, LayerLoader, nameof(AddLayerTemplates));
            Patch(harmony, BlendTreeLoader, nameof(AddBlendTreeTemplates));
            // Loaded before this ran (the editor caches its lists): read again with ours.
            ClearCaches();
        }

        private static void Patch(Harmony harmony, string method, string postfix)
        {
            MethodInfo target = AccessTools.Method(Toolbar, method);
            if (target == null)
            {
                Unavailable(ToolbarType + "." + method + " was not found");
                return;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(YgdrTemplateBridge), postfix));
            }
            catch (Exception exception)
            {
                // A changed return type (the list's shape) makes Harmony refuse the postfix.
                Unavailable(ToolbarType + "." + method + " could not be patched (" + exception.Message + ")");
            }
        }

        /// <summary>
        /// Layer templates: the editor's list (its cache) gets every controller under Templates/YGDR once. The postfix runs
        /// inside the editor's menu code, so any error is caught and logged here, leaving the editor's list as it was.
        /// </summary>
        private static void AddLayerTemplates(List<(string, AnimatorController, bool)> __result)
        {
            try
            {
                if (__result == null || !AssetDatabase.IsValidFolder(TemplatesFolder)) return;
                var added = new List<(string, AnimatorController, bool)>();
                foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { TemplatesFolder }))
                {
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                    if (controller == null || __result.Exists(entry => entry.Item2 == controller)) continue;
                    if (__result.Exists(entry => entry.Item1 == controller.name)) NameClash("layer", controller.name, controller);
                    added.Add((controller.name, controller, false));
                }
                __result.AddRange(added);
            }
            catch (Exception exception)
            {
                Failed("Adding the layer templates", exception);
            }
        }

        /// <summary>Blend tree templates: every blend tree saved as its own asset under Templates/YGDR, once (errors as above).</summary>
        private static void AddBlendTreeTemplates(List<(string, BlendTree)> __result)
        {
            try
            {
                if (__result == null || !AssetDatabase.IsValidFolder(TemplatesFolder)) return;
                var added = new List<(string, BlendTree)>();
                foreach (string guid in AssetDatabase.FindAssets("t:BlendTree", new[] { TemplatesFolder }))
                {
                    var blendTree = AssetDatabase.LoadAssetAtPath<BlendTree>(AssetDatabase.GUIDToAssetPath(guid));
                    // As the editor does: blend trees inside controllers are not templates of their own.
                    if (blendTree == null || !AssetDatabase.IsMainAsset(blendTree) || __result.Exists(entry => entry.Item2 == blendTree)) continue;
                    if (__result.Exists(entry => entry.Item1 == blendTree.name)) NameClash("blend tree", blendTree.name, blendTree);
                    added.Add((blendTree.name, blendTree));
                }
                __result.AddRange(added);
            }
            catch (Exception exception)
            {
                Failed("Adding the blend tree templates", exception);
            }
        }

        // A template named like one already in the editor's list shows twice in its menu: said once per session and name.
        private static void NameClash(string kind, string name, UnityEngine.Object template)
        {
            string key = HarmonyId + ".clash." + kind + "." + name;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogWarning(LogPrefix + "The " + kind + " template " + name + " has the same name as one YGDR Animator Editor already " +
                "lists, so the menu shows both. Rename this package's template.", template);
        }

        // An error inside a postfix: logged once per session and message, with its stack; the editor's list is left alone.
        private static void Failed(string what, Exception exception)
        {
            string key = HarmonyId + ".failed." + what + "." + exception.Message;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogError(LogPrefix + what + " to YGDR Animator Editor's menu failed, so they are left out: " + exception);
        }

        /// <summary>Makes the editor build its template lists again on next use (its own clearing only watches its folders).</summary>
        internal static void ClearCaches()
        {
            if (Toolbar == null) return;
            foreach (string name in new[] { LayerCache, BlendTreeCache })
            {
                FieldInfo field = AccessTools.Field(Toolbar, name);
                if (field != null) field.SetValue(null, null);
                // Without it, template changes only show after a script reload.
                else Unavailable(ToolbarType + "." + name + " was not found", ", so new or renamed templates only show after a script reload.");
            }
        }

        // Once per editor session; by default the templates still install, they just aren't in the editor's menus.
        private static void Unavailable(string why, string effect = ", so this package's templates are not in YGDR Animator Editor's template menus.")
        {
            string key = HarmonyId + ".unavailable." + why;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogWarning(LogPrefix + why + effect + " The editor may have changed; update this package.");
        }
    }

    /// <summary>
    /// YGDR Animator Editor lists every blend tree template under Delete Template, and deleting one deletes the folder it
    /// is in: for this package's templates, that is this package's own Templates/YGDR folder. A deletion inside this package
    /// that comes from the editor's code does nothing but log RefusedMessage as an error; deleting by hand is left alone.
    /// </summary>
    internal sealed class YgdrTemplateDeleteGuard : AssetModificationProcessor
    {
        private const string PackageRoot = "Packages/com.riry.animatortemplates";
        internal const string RefusedMessage = "please remove the templates vpm to delete templates";

        private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            if (normalized != PackageRoot && !normalized.StartsWith(PackageRoot + "/", StringComparison.Ordinal)) return AssetDeleteResult.DidNotDelete;
            if (!FromYgdr()) return AssetDeleteResult.DidNotDelete;
            Debug.LogError(RefusedMessage);
            return AssetDeleteResult.FailedDelete;
        }

        // Whether YGDR Animator Editor's code (namespace YGDR...) is on the call stack.
        private static bool FromYgdr()
        {
            System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(false).GetFrames();
            if (frames == null) return false;
            foreach (System.Diagnostics.StackFrame frame in frames)
            {
                Type type = frame.GetMethod() != null ? frame.GetMethod().DeclaringType : null;
                if (type != null && type.Namespace != null && type.Namespace.StartsWith("YGDR", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>When a file under Templates/YGDR is added, changed, moved or removed, the editor's template lists are built again.</summary>
    internal sealed class YgdrTemplateCacheClearer : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string[] paths in new[] { imported, deleted, moved, movedFrom })
            {
                foreach (string path in paths)
                {
                    if (!path.Replace('\\', '/').StartsWith(YgdrTemplateBridge.TemplatesFolder + "/", StringComparison.Ordinal)) continue;
                    YgdrTemplateBridge.ClearCaches();
                    return;
                }
            }
        }
    }
}

// ============================================================================
// GLOSSARY: YGDR Animator Editor template bridge (GPL-3.0; the rest of the package is MIT)
// ============================================================================
//
// YgdrTemplateBridge (this file)
//   TemplatesFolder          Packages/com.riry.animatortemplates/Templates/YGDR: animator controllers there are layer
//                            templates, blend trees saved as their own asset are blend tree templates. A name with "."
//                            shows as a submenu (Riry.Toggle.Simple: Riry > Toggle > Simple).
//   static constructor       [InitializeOnLoad]: Harmony (HarmonyId) postfixes on YGDR's PatchLayerToolbar
//                            (ToolbarType) LoadTemplateControllers and LoadBlendTreeTemplateAssets, then ClearCaches.
//   AddLayerTemplates        Postfix: adds each controller once to the editor's list (its _templateCache) as
//                            (name, controller, isUser false), so it is a package template, not a deletable user one.
//   AddBlendTreeTemplates    Postfix: adds each main-asset blend tree once to its blend tree list.
//   ClearCaches()            Sets the editor's _templateCache and _blendTreeTemplateCache to null.
//   Unavailable(why, effect) The patched type, method or a cache field was not found, or a method refused the postfix:
//                            warned once per session (LogPrefix).
//   NameClash / Failed       A template named like one YGDR lists (warned once per name); an error inside a postfix
//                            (logged once with its stack; the editor's list left unchanged, so its menu still builds).
// YgdrTemplateCacheClearer   AssetPostprocessor: a change under TemplatesFolder clears the caches.
// YgdrTemplateDeleteGuard    AssetModificationProcessor: refuses (FailedDelete; only RefusedMessage logged as an error) a deletion
//                            inside this package made by YGDR's code (FromYgdr: a YGDR.* type on the call stack), as its
//                            blend tree Delete Template deletes the template's whole folder. Deleting by hand still works.
// Template kinds             YGDR Animator Editor 1.2.1 has two: layer templates (LoadTemplateControllers) and blend
//                            tree templates (LoadBlendTreeTemplateAssets); both are covered.
// Riry.AnimatorTemplates.Editor.asmdef  Editor only; compiled only with com.ygdr.animator (RIRY_YGDR) and
//                            com.vrchat.base (RIRY_HARMONY, its 0Harmony.dll) installed.
// YGDR source: https://github.com/YerGodDamnRight/YGDR-Animator-Editor (also in the YGDR_ANIMATOR_EDITOR_REPO
// environment variable of the development environment).
