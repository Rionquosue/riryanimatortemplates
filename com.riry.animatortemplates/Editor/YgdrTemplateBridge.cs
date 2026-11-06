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
    /// Adds this package's templates to YGDR Animator Editor's template menus. The editor only
    /// looks in its own package and in Assets/YGDR Animator/User Templates, so its two loaders
    /// get a Harmony postfix that appends every animator controller (layer templates) and every
    /// blend tree saved as its own asset (blend tree templates) under Templates/ here. They show
    /// as package templates: among YGDR's own, never under User/ or Delete User Template.
    /// Compiled only when YGDR Animator Editor and the VRChat SDK base (which brings Harmony)
    /// are installed (see the assembly definition). If an update of the editor renames what is
    /// patched, nothing is added and a warning is logged once per session.
    /// </summary>
    [InitializeOnLoad]
    internal static class YgdrTemplateBridge
    {
        internal const string TemplatesFolder = "Packages/com.riry.animatortemplates/Templates";
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

        /// <summary>Layer templates: the editor's list (its cache) gets every controller under Templates/ once.</summary>
        private static void AddLayerTemplates(List<(string, AnimatorController, bool)> __result)
        {
            if (__result == null || !AssetDatabase.IsValidFolder(TemplatesFolder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { TemplatesFolder }))
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                if (controller != null && !__result.Exists(entry => entry.Item2 == controller))
                    __result.Add((controller.name, controller, false));
            }
        }

        /// <summary>Blend tree templates: every blend tree saved as its own asset under Templates/, once.</summary>
        private static void AddBlendTreeTemplates(List<(string, BlendTree)> __result)
        {
            if (__result == null || !AssetDatabase.IsValidFolder(TemplatesFolder)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:BlendTree", new[] { TemplatesFolder }))
            {
                var blendTree = AssetDatabase.LoadAssetAtPath<BlendTree>(AssetDatabase.GUIDToAssetPath(guid));
                // As the editor does: blend trees inside controllers are not templates of their own.
                if (blendTree != null && AssetDatabase.IsMainAsset(blendTree) && !__result.Exists(entry => entry.Item2 == blendTree))
                    __result.Add((blendTree.name, blendTree));
            }
        }

        /// <summary>Makes the editor build its template lists again on next use (its own clearing only watches its folders).</summary>
        internal static void ClearCaches()
        {
            if (Toolbar == null) return;
            foreach (string name in new[] { LayerCache, BlendTreeCache })
            {
                FieldInfo field = AccessTools.Field(Toolbar, name);
                if (field != null) field.SetValue(null, null);
            }
        }

        // Once per editor session: the templates still install, they just aren't in the editor's menus.
        private static void Unavailable(string why)
        {
            string key = HarmonyId + ".unavailable." + why;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogWarning(LogPrefix + why + ", so this package's templates are not in YGDR Animator Editor's template menus. " +
                "The editor may have changed; update this package.");
        }
    }

    /// <summary>When a file under Templates/ is added, changed, moved or removed, the editor's template lists are built again.</summary>
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
//   TemplatesFolder          Packages/com.riry.animatortemplates/Templates: animator controllers there are layer
//                            templates, blend trees saved as their own asset are blend tree templates. A name with "."
//                            shows as a submenu (Riry.Toggle.Simple: Riry > Toggle > Simple).
//   static constructor       [InitializeOnLoad]: Harmony (HarmonyId) postfixes on YGDR's PatchLayerToolbar
//                            (ToolbarType) LoadTemplateControllers and LoadBlendTreeTemplateAssets, then ClearCaches.
//   AddLayerTemplates        Postfix: adds each controller once to the editor's list (its _templateCache) as
//                            (name, controller, isUser false), so it is a package template, not a deletable user one.
//   AddBlendTreeTemplates    Postfix: adds each main-asset blend tree once to its blend tree list.
//   ClearCaches()            Sets the editor's _templateCache and _blendTreeTemplateCache to null.
//   Unavailable(why)         The patched type or method was not found or refused the postfix: warned once per session.
// YgdrTemplateCacheClearer   AssetPostprocessor: a change under TemplatesFolder clears the caches.
// Riry.AnimatorTemplates.Editor.asmdef  Editor only; compiled only with com.ygdr.animator (RIRY_YGDR) and
//                            com.vrchat.base (RIRY_HARMONY, its 0Harmony.dll) installed.
// YGDR source: https://github.com/YerGodDamnRight/YGDR-Animator-Editor (also in the YGDR_ANIMATOR_EDITOR_REPO
// environment variable of the development environment).
