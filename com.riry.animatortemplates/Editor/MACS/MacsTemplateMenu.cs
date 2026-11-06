using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Riry.AnimatorTemplates.Editor.Macs
{
    /// <summary>
    /// Adds a Riry submenu to the right-click menu of a blend tree node in the Animator window, directly beneath MACS's New
    /// submenu. It lists every blend tree saved as its own asset under Templates/MACS; choosing one opens the window that
    /// inserts it into the clicked blend tree. Unity's HandleNodeInput gets a transpiler (its own Harmony ID, so MACS's
    /// UnpatchAll of its ID leaves it alone) that calls InjectMenu just before the menu is shown.
    /// </summary>
    [InitializeOnLoad]
    internal static class MacsTemplateMenu
    {
        internal const string TemplatesFolder = "Packages/com.riry.animatortemplates/Templates/MACS";
        internal const string HarmonyId = "com.riry.animatortemplates.macs";
        internal const string LogPrefix = "[Riry's Animator Templates] ";
        private const string MenuRoot = "Riry/";
        private const string MacsNewMenu = "New/";

        static MacsTemplateMenu()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.UnpatchAll(HarmonyId);
                harmony.CreateClassProcessor(typeof(HandleNodeInputPatch)).Patch();
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + "The blend tree node menu could not be patched, so the Riry submenu is missing: " + exception);
            }
        }

        /// <summary>Called from the patched HandleNodeInput with the menu still to be shown. Adds items only; never shows UI.</summary>
        public static void InjectMenu(GenericMenu menu, UnityEditor.Graphs.GraphGUI graphGUI, UnityEditor.Graphs.AnimationBlendTree.Node node)
        {
            try
            {
                var target = node != null ? node.motion as BlendTree : null;
                if (menu == null || target == null) return;
                int before = menu.GetItemCount();
                var host = Traverse.Create(graphGUI).Field("m_Host").GetValue<EditorWindow>();
                var controller = host != null ? Traverse.Create(host).Property("animatorController").GetValue<AnimatorController>() : null;
                List<BlendTree> templates = FindTemplates();
                if (templates.Count == 0) menu.AddDisabledItem(new GUIContent(MenuRoot + "No templates in Templates/MACS"));
                foreach (BlendTree template in templates)
                {
                    var content = new GUIContent(MenuRoot + MenuName(template));
                    if (controller == null) menu.AddDisabledItem(content);
                    else
                    {
                        // Captured now: the graph GUI can be rebuilt before the item is chosen.
                        var request = new MacsTemplateRequest(template, target, controller, host);
                        menu.AddItem(content, false, Open, request);
                    }
                }
                MoveBelowNew(menu, before);
            }
            catch (Exception exception)
            {
                Failed("Adding the Riry submenu", exception);
            }
        }

        private static void Open(object userData)
        {
            try
            {
                MacsTemplateWindow.Open((MacsTemplateRequest)userData);
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + "Opening the template window failed: " + exception);
            }
        }

        /// <summary>Every blend tree saved as its own asset under TemplatesFolder (blend trees inside other assets belong to them).</summary>
        internal static List<BlendTree> FindTemplates()
        {
            var found = new List<BlendTree>();
            if (!AssetDatabase.IsValidFolder(TemplatesFolder)) return found;
            foreach (string guid in AssetDatabase.FindAssets("t:BlendTree", new[] { TemplatesFolder }))
            {
                var tree = AssetDatabase.LoadAssetAtPath<BlendTree>(AssetDatabase.GUIDToAssetPath(guid));
                if (tree != null && AssetDatabase.IsMainAsset(tree) && !found.Contains(tree)) found.Add(tree);
            }
            found.Sort((a, b) => string.CompareOrdinal(MenuName(a), MenuName(b)));
            return found;
        }

        // The asset's path under TemplatesFolder without its extension: subfolders become submenus, and no two names clash.
        private static string MenuName(BlendTree template)
        {
            string path = AssetDatabase.GetAssetPath(template).Replace('\\', '/');
            if (path.StartsWith(TemplatesFolder + "/", StringComparison.Ordinal)) path = path.Substring(TemplatesFolder.Length + 1);
            int dot = path.LastIndexOf('.');
            int slash = path.LastIndexOf('/');
            return dot > slash ? path.Substring(0, dot) : path;
        }

        /// <summary>
        /// Moves the items added from index <paramref name="from"/> to directly after the last item of MACS's New submenu.
        /// GenericMenu has no public way to insert, so its item list (m_MenuItems, Unity 2022.3) is reordered. Without MACS's
        /// New submenu, or if that list is missing, the items stay at the bottom.
        /// </summary>
        private static void MoveBelowNew(GenericMenu menu, int from)
        {
            FieldInfo itemsField = AccessTools.Field(typeof(GenericMenu), "m_MenuItems");
            var items = itemsField != null ? itemsField.GetValue(menu) as IList : null;
            if (items == null)
            {
                Unavailable("GenericMenu.m_MenuItems was not found", ", so the Riry submenu is at the bottom of the menu instead of beneath New.");
                return;
            }
            int lastNew = -1;
            for (int i = 0; i < from && i < items.Count; i++)
            {
                if (ItemText(items[i]).StartsWith(MacsNewMenu, StringComparison.Ordinal)) lastNew = i;
            }
            if (lastNew < 0 || lastNew == from - 1) return;
            var moved = new List<object>();
            for (int i = from; i < items.Count; i++) moved.Add(items[i]);
            for (int i = items.Count - 1; i >= from; i--) items.RemoveAt(i);
            for (int i = 0; i < moved.Count; i++) items.Insert(lastNew + 1 + i, moved[i]);
        }

        private static string ItemText(object item)
        {
            var content = item != null ? Traverse.Create(item).Field("content").GetValue<GUIContent>() : null;
            return content != null && content.text != null ? content.text : "";
        }

        // An error inside the menu code: logged once per session and message with its stack; the menu is shown without Riry.
        internal static void Failed(string what, Exception exception)
        {
            string key = HarmonyId + ".failed." + what + "." + exception.Message;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogError(LogPrefix + what + " failed: " + exception);
        }

        // Once per editor session.
        internal static void Unavailable(string why, string effect)
        {
            string key = HarmonyId + ".unavailable." + why;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            Debug.LogWarning(LogPrefix + why + effect + " Unity or MACS may have changed; update this package.");
        }
    }

    /// <summary>
    /// The transpiler on UnityEditor.Graphs.AnimationBlendTree.GraphGUI.HandleNodeInput: between the load of the menu local
    /// and its ShowAsContext call it inserts dup, ldarg.0, ldarg.1, call InjectMenu, so the menu holds every item Unity and
    /// MACS added. The branch label stays on the original load.
    /// </summary>
    [HarmonyPatch]
    internal static class HandleNodeInputPatch
    {
        private const string GraphGuiType = "UnityEditor.Graphs.AnimationBlendTree.GraphGUI";
        private const string TargetName = "HandleNodeInput";

        private static MethodBase Target()
        {
            Type type = AccessTools.TypeByName(GraphGuiType);
            return type != null ? AccessTools.Method(type, TargetName) : null;
        }

        private static bool Prepare()
        {
            if (Target() != null) return true;
            MacsTemplateMenu.Unavailable(GraphGuiType + "." + TargetName + " was not found", ", so the blend tree node menu has no Riry submenu.");
            return false;
        }

        private static MethodBase TargetMethod() => Target();

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo inject = AccessTools.Method(typeof(MacsTemplateMenu), nameof(MacsTemplateMenu.InjectMenu));
            var result = new List<CodeInstruction>();
            bool injected = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!injected && instruction.opcode == OpCodes.Callvirt && instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(GenericMenu) && method.Name == nameof(GenericMenu.ShowAsContext))
                {
                    result.Add(new CodeInstruction(OpCodes.Dup));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Call, inject));
                    injected = true;
                }
                result.Add(instruction);
            }
            if (!injected) MacsTemplateMenu.Unavailable(GraphGuiType + "." + TargetName + " has no GenericMenu.ShowAsContext call", ", so the blend tree node menu has no Riry submenu.");
            return result;
        }
    }

    /// <summary>What a Riry menu item carries to the window: chosen when the menu was built.</summary>
    internal sealed class MacsTemplateRequest
    {
        internal readonly BlendTree Template;
        internal readonly BlendTree Target;
        internal readonly AnimatorController Controller;
        internal readonly EditorWindow Host;

        internal MacsTemplateRequest(BlendTree template, BlendTree target, AnimatorController controller, EditorWindow host)
        {
            Template = template;
            Target = target;
            Controller = controller;
            Host = host;
        }
    }
}

// ============================================================================
// GLOSSARY: MACS template menu
// ============================================================================
//
// MacsTemplateMenu (this file)
//   TemplatesFolder          Packages/com.riry.animatortemplates/Templates/MACS: every blend tree saved as its own asset
//                            there (subfolders included) is a template in the Riry submenu.
//   HarmonyId                com.riry.animatortemplates.macs: this patch's owner. Only this ID is ever unpatched, so MACS's
//                            patches (com.mcardellje.unitymodding) and the YGDR bridge's are left alone.
//   static constructor       [InitializeOnLoad]: UnpatchAll(HarmonyId), then applies HandleNodeInputPatch. Re-run after
//                            every domain reload, as patches do not survive one.
//   InjectMenu               Adds Riry/<template path> per template (disabled without a controller; "No templates" when
//                            there are none) for blend tree nodes only, then MoveBelowNew.
//   MoveBelowNew             Moves the added items directly after MACS's last New/ item by reordering GenericMenu's
//                            m_MenuItems. Without New/ items they stay at the bottom.
//   Failed / Unavailable     An error while adding the items (logged once with its stack); a missing Unity member
//                            (warned once per session).
// HandleNodeInputPatch       Transpiler on UnityEditor.Graphs.AnimationBlendTree.GraphGUI.HandleNodeInput: inserts
//                            dup, ldarg.0, ldarg.1, call InjectMenu before the first GenericMenu.ShowAsContext.
//   Prepare / TargetMethod   Resolve the internal type by name; Prepare skips the patch when it is missing.
// MacsTemplateRequest        The template, clicked blend tree, controller and Animator window, captured with the menu.
// Placement                  Without MACS the Riry submenu is last; with it, directly beneath New.
