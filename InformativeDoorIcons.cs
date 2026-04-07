using BepInEx;
using CellMenu;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using System.Collections.Generic;
using LevelGeneration;
using TMPro;
// using GTFO.API.Utilities;

namespace InformativeDoorIcons
{
    [BepInPlugin("informativedooricons.HazardousMonkey", "InformativeDoorIcons", "1.0.0")]
    [BepInDependency("dev.gtfomodding.gtfo-api")]
    public class InformativeDoorIconsPlugin : BasePlugin
    {
        public static InformativeDoorIconsPlugin Instance { get; private set; }
        public override void Load()
        {
            Instance = this;

            new Harmony("informativedooricons.HazardousMonkey").PatchAll();

            // Loading the custom sprite, but really I could get away with only doing this in the H-Postfix
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                byte[] pngBytes  = File.ReadAllBytes(Path.Combine(pluginDir, "symbol_door_security_map_inner_outline.png"));
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                tex.filterMode = FilterMode.Point;
                tex.LoadImage(pngBytes);
                DoorManager.BlackKeyOutlineSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), DoorManager.BlackKeyOutlinePixelsPerUnit);
                
            }
            catch (Exception e)
            {
                Debug.LogError($"[InformativeDoorIcons] Failed to load outline sprite: {e.Message}");
            }

            Log.LogInfo("[InformativeDoorIcons] Loaded.");
        }
    }

    public static class DoorManager
    {
        private static readonly Dictionary<int, CM_SyncedGUIItem> s_weakDoorGUI = new();

        public static void RegisterWeakDoor(int physicalDoorInstanceId, CM_SyncedGUIItem guiItem)
        {
            s_weakDoorGUI[physicalDoorInstanceId] = guiItem;
        }

        public static bool TryGetWeakDoorGUI(int physicalDoorInstanceId, out CM_SyncedGUIItem guiItem)
                    => s_weakDoorGUI.TryGetValue(physicalDoorInstanceId, out guiItem);

        private static readonly Dictionary<int, CM_SyncedGUIItem> s_securityDoorGUI = new();

        public static void RegisterSecurityDoor(int physicalDoorInstanceId, CM_SyncedGUIItem guiItem)
        {
            s_securityDoorGUI[physicalDoorInstanceId] = guiItem;
        }

        public static bool TryGetSecurityDoorGUI(int physicalDoorInstanceId, out CM_SyncedGUIItem guiItem)
                    => s_securityDoorGUI.TryGetValue(physicalDoorInstanceId, out guiItem);

        public static Sprite BlackKeyOutlineSprite { get; set; } = null;

        public const float BlackKeyOutlinePixelsPerUnit = 64f;

        // -------------------------------------------------------------------------
        // GIMME DEM COLORS!!!!
        // -------------------------------------------------------------------------
        public static Color GetKeyColor(string keyName)
        {
            const string prefix = "KEY_";
            if (!keyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[InformativeDoorIcons] '{keyName}' did not contain a valid prefix.");
                return new Color(0.4191f, 0.1387f, 0.1387f, 1);
            }

            string colorName = keyName.Substring(prefix.Length); // IE: "ORANGE", "PURPLE", "GREEN"

            // Manual color overrides
            switch (colorName)
            {
                case "BLUE":   return new Color(0f,      0.33f,   1f,      1f); // Because apparently Blue in-game looks like Purple
                case "GREEN":  return new Color(0f,      1f,      0.2f,    1f); // Default green is too dark
                case "PURPLE": return new Color(0.6235f, 0.1137f, 0.9372f, 1f); // Unity default #800080 is also too dark
            }

            if (ColorUtility.TryParseHtmlString(colorName, out Color result))
                return result;

            // Fallback
            Debug.LogWarning($"[InformativeDoorIcons] Could not resolve color from key name: '{keyName}'");
            return new Color(0.4191f, 0.1387f, 0.1387f, 1);
        }

        // Default inner-sprite colors restored when a weak door becomes fully unlocked.
        // Source: in-engine observations noted in the Setup patch comments.
        public static readonly Color WeakDoorDefaultClosed = new(0.3373f, 0.3529f, 0.2549f, 1f);
        public static readonly Color WeakDoorDefaultOpen   = new(0.3373f, 0.3529f, 0.2549f, 0.0549f);
    }


    // ============================================================
    //  Harmony patches
    // ============================================================

/* used for debugging custom KEY_BLACK door sprites
    [HarmonyPatch(typeof(GateKeyItem), nameof(GateKeyItem.Setup))]
    public static class InformativeDoorIcons_GateKeyItem_Setup_NameChangeForDebug
    {
        public static void Postfix(GateKeyItem __instance)
        {
            __instance.m_keyName = "KEY_BLACK";
        }
    }
*/

    // Resets a weak door's custom inner-sprite color back to defaults once both locks have been unlocked. Relies on the registry populated in Setup below.
    [HarmonyPatch(typeof(LG_WeakLock), nameof(LG_WeakLock.OnSyncStatusChanged))]
    public static class InformativeDoorIcons_LG_WeakLock_OnSyncStatusChanged_DoorLockCheckForUnlocked
    {
        public static void Postfix(LG_WeakLock __instance, eWeakLockStatus status)
        {
            if (status != eWeakLockStatus.Unlocked) return;
            
            // lmfao
            // LG_WeakDoor physicalDoor_WL = __instance.gameObject.transform.parent.transform.parent.transform.parent.transform.parent.transform.parent.GetComponent<LG_WeakDoor>();
            // LG_WeakDoor physicalDoor_WL = __instance.gameObject.transform.parent.parent.parent.parent.parent.GetComponent<LG_WeakDoor>();
            LG_WeakDoor physicalDoor_WL = __instance.gameObject.GetComponentInParents<LG_WeakDoor>();

            if (physicalDoor_WL == null) return;

            // Only act once both locks are open.
            if (physicalDoor_WL.WeakLocks[0].Status != eWeakLockStatus.Unlocked || physicalDoor_WL.WeakLocks[1].Status != eWeakLockStatus.Unlocked) return;

            // Look up the map-marker GUI that was registered during Setup.
            if (!DoorManager.TryGetWeakDoorGUI(physicalDoor_WL.gameObject.GetInstanceID(), out CM_SyncedGUIItem guiItem))
            {
                Debug.LogWarning("[InformativeDoorIcons] OnSyncStatusChanged: no GUI entry found for unlocked weak door.");
                return;
            }

            static bool IsInnerSprite(string name) => name.EndsWith("_inner") || (name.Contains("_inner (") && name.EndsWith(")"));

            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakClosed.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = DoorManager.WeakDoorDefaultClosed;
            }

            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakOpen.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = DoorManager.WeakDoorDefaultOpen;
            }
        }
    }

    [HarmonyPatch(typeof(CM_SyncedGUIItem), nameof(CM_SyncedGUIItem.Setup))]
    public static class InformativeDoorIcons_CM_SyncedGUIItem_Setup_DoorColoration
    {
        public static void Postfix(CM_SyncedGUIItem __instance)
        {
            if (__instance.m_gfxSecureApex != null) // Is this a door?
            {
                // Raise the position on the door Name-text so that it's not inside the door sprite
                __instance.m_locatorTxt.gameObject.transform.localPosition = new Vector3(0, 5.5f, 0);

                static bool IsInnerSprite(string name) => name.EndsWith("_inner") || (name.Contains("_inner (") && name.EndsWith(")"));

                bool isHack        = false;
                bool isMeleeLocked = false;

                GameObject physicalDoor_GO = __instance.RevealerBase.transform.parent.gameObject.transform.parent.gameObject;

                // Weak Door
                if (physicalDoor_GO.GetComponent<LG_WeakDoor>() != null)
                {
                    LG_WeakDoor physicalDoor_WL = physicalDoor_GO.GetComponent<LG_WeakDoor>();

                    // Door has a lock
                    if (physicalDoor_WL.WeakLocks != null)
                    {
                        isMeleeLocked = physicalDoor_WL.WeakLocks[0].m_lockType == eWeakLockType.Melee    || physicalDoor_WL.WeakLocks[1].m_lockType == eWeakLockType.Melee;
                        isHack        = physicalDoor_WL.WeakLocks[0].m_lockType == eWeakLockType.Hackable || physicalDoor_WL.WeakLocks[1].m_lockType == eWeakLockType.Hackable;
                    }
                    else return;

                    // Register the physical-door -> GUI mapping so OnSyncStatusChanged can find it.
                    DoorManager.RegisterWeakDoor(physicalDoor_GO.GetInstanceID(), __instance);
                    // Debug.LogWarning($"[InformativeDoorIcons] WeakDoor registered: {physicalDoor_GO.GetInstanceID()} and GUI: {__instance}");

                    if (isMeleeLocked)
                    {
                        SpriteRenderer[] doorGFXClosed = __instance.m_gfxWeakClosed.GetComponentsInChildren<SpriteRenderer>();
                        SpriteRenderer[] doorGFXOpen   = __instance.m_gfxWeakOpen.GetComponentsInChildren<SpriteRenderer>();
                        foreach (SpriteRenderer sprite in doorGFXClosed)
                        {
                            if (!IsInnerSprite(sprite.gameObject.name)) continue;
                            sprite.color = new Color(1, 1, 0, 0.8f);
                        }
                        foreach (SpriteRenderer sprite in doorGFXOpen)
                        {
                            if (!IsInnerSprite(sprite.gameObject.name)) continue;
                            sprite.color = new Color(1, 1, 0, 0.3f);
                        }

                        return;
                    }
                    else if (isHack)
                    {
                        SpriteRenderer[] doorGFXClosed = __instance.m_gfxWeakClosed.GetComponentsInChildren<SpriteRenderer>();
                        SpriteRenderer[] doorGFXOpen   = __instance.m_gfxWeakOpen.GetComponentsInChildren<SpriteRenderer>();
                        foreach (SpriteRenderer sprite in doorGFXClosed)
                        {
                            if (!IsInnerSprite(sprite.gameObject.name)) continue;
                            sprite.color = new Color(0, 1, 1, 0.6f);
                        }
                        foreach (SpriteRenderer sprite in doorGFXOpen)
                        {
                            if (!IsInnerSprite(sprite.gameObject.name)) continue;
                            sprite.color = new Color(0, 1, 1, 0.3f);
                        }

                        return;
                    }
                }

                // Security Door
                if (physicalDoor_GO.GetComponent<LG_SecurityDoor>() != null)
                {
                    DoorManager.RegisterSecurityDoor(physicalDoor_GO.GetInstanceID(), __instance);
                    // Debug.LogWarning($"[InformativeDoorIcons] SecurityDoor registered: {physicalDoor_GO.GetInstanceID()} and GUI: {__instance}");

                    LG_SecurityDoor physicalDoor_SL = physicalDoor_GO.GetComponent<LG_SecurityDoor>();

                    static bool IsClampSprite(string name) => name.EndsWith("_clamp") || (name.Contains("_clamp (") && name.EndsWith(")"));

                    static bool IsInnerBulkheadSymbolSprite(string name) => name.ContainsIgnoreCase("symbol_bulkhead");

                    // Fix Bulkhead door symbol
                    foreach (SpriteRenderer sprite in __instance.m_gfxBulkheadClosed.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                        sprite.sortingOrder = 5;
                        sprite.color = Color.white;
                    }

                    // Fix "APEX" text z-fighting  TextMeshPro
                    foreach (MeshRenderer mesh in __instance.m_gfxSecureApex.GetComponentsInChildren<MeshRenderer>())
                    {
                        mesh.sortingOrder = 5;
                    }

                    // Replace "APEX" text with "ALARM"   
                    foreach (TextMeshPro tmp in __instance.m_gfxSecureApex.GetComponentsInChildren<TextMeshPro>())
                    {
                        if (tmp.m_text.ContainsIgnoreCase("APEX"))
                        {
                            tmp.text = "ALARM";
                            tmp.characterSpacing = 8;
                            tmp.ForceMeshUpdate();
                        }
                    }

                    // Mark "free" Security doors by making the clamp sprites green
                    foreach (SpriteRenderer sprite in __instance.m_gfxSecureClosed.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsClampSprite(sprite.gameObject.name)) continue;
                        sprite.color = new Color(0, 1, .5f, 1);
                    }

                    // "Locked by key" check
                    if (physicalDoor_SL.m_keyItem != null)
                    {
                        string key     = physicalDoor_SL.m_keyItem.m_keyName;
                        Color keyColor = DoorManager.GetKeyColor(key);

                        SpriteRenderer[] doorGFX = __instance.m_gfxSecureKeycard.GetComponentsInChildren<SpriteRenderer>();
                        foreach (SpriteRenderer sprite in doorGFX)
                        {
                            if (!IsInnerSprite(sprite.gameObject.name)) continue;
                            sprite.color = keyColor;
                        }

                        // Key color is black, making it hard to see.
                        // Duplicate the "inner" sprite, then change its style to create an outlien of sorts.
                        if (key == "KEY_BLACK")
                        {
                            if(DoorManager.BlackKeyOutlineSprite == null)
                            {
                                try
                                {
                                    string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                                    byte[] pngBytes  = File.ReadAllBytes(Path.Combine(pluginDir, "symbol_door_security_map_inner_outline.png"));
                                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                                    tex.filterMode = FilterMode.Point;
                                    tex.LoadImage(pngBytes);
                                    DoorManager.BlackKeyOutlineSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), DoorManager.BlackKeyOutlinePixelsPerUnit);
                                    Debug.LogWarning("[InformativeDoorIcons] BlackKeyOutlineSprite was Null, new one created");
                                    
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError($"[InformativeDoorIcons] Failed to load outline sprite: {e.Message}");
                                }
                            }

                            if (DoorManager.BlackKeyOutlineSprite != null)
                            {
                                // Re-query after coloring so we only touch inner sprites.
                                foreach (SpriteRenderer sprite in __instance.m_gfxSecureKeycard.GetComponentsInChildren<SpriteRenderer>())
                                {
                                    if (!IsInnerSprite(sprite.gameObject.name)) continue;

                                    GameObject duplicate         = GameObject.Instantiate(sprite.gameObject, sprite.gameObject.transform.parent);
                                    SpriteRenderer duplicateSR   = duplicate.GetComponent<SpriteRenderer>();
                                    duplicateSR.sprite           = DoorManager.BlackKeyOutlineSprite;
                                    duplicateSR.color            = Color.white;
                                    duplicateSR.sortingOrder    -= 1;

                                    // We've overdrawn our sprite a little to avoid any pixel clipping issues, but we now manually adjust its lPos.x
                                    if (duplicateSR.gameObject.transform.localPosition.x > 0)
                                    {
                                        duplicateSR.gameObject.transform.localPosition = new Vector3(0.176f, duplicateSR.gameObject.transform.localPosition.y, duplicateSR.gameObject.transform.localPosition.z);
                                    }
                                    else
                                    {
                                        duplicateSR.gameObject.transform.localPosition = new Vector3(-0.176f, duplicateSR.gameObject.transform.localPosition.y, duplicateSR.gameObject.transform.localPosition.z);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

/* Doesn't happen at all?
    [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.SetNavInfo))]
    public static class InformativeDoorIcons_LG_SecurityDoor_SetNavInfo_DetectProgression
    {
        public static void Postfix(LG_SecurityDoor __instance, LG_PowerGenerator_Core powerGenerator)
        {
            Debug.LogWarning($"[InformativeDoorIcons] SetNavInfo ran");
            if (!DoorManager.TryGetWeakDoorGUI(__instance.gameObject.GetInstanceID(), out CM_SyncedGUIItem guiItem))
            {
                Debug.LogWarning($"[InformativeDoorIcons] SetupPowerGeneratorLock: no GUI entry found for {__instance.gameObject.GetInstanceID()}");
                return;
            }
            Debug.LogWarning($"[InformativeDoorIcons] {guiItem.m_additionalTxt.text}");
            Debug.LogWarning($"[InformativeDoorIcons] {__instance.m_terminalNavInfoForward}");
            Debug.LogWarning($"[InformativeDoorIcons] {__instance.m_terminalNavInfoForward[0]}");
            Debug.LogWarning($"[InformativeDoorIcons] {__instance.m_terminalNavInfoForward[1]}");

            guiItem.m_additionalTxt.text = __instance.m_terminalNavInfoForward[0];
            guiItem.m_additionalTxt.ForceMeshUpdate();
        }
    }
*/

    /*  happens too early
    [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.SetupPowerGeneratorLock))]
    public static class InformativeDoorIcons_LG_SecurityDoor_SetupPowerGeneratorLock_ChangeReqText
    {
        public static void Postfix(LG_SecurityDoor __instance, LG_PowerGenerator_Core powerGenerator)
        {
            Debug.LogWarning("[InformativeDoorIcons] SetupPowerGeneratorLock ran");
            if (!DoorManager.TryGetWeakDoorGUI(__instance.gameObject.GetInstanceID(), out CM_SyncedGUIItem guiItem))
            {
                Debug.LogWarning($"[InformativeDoorIcons] SetupPowerGeneratorLock: no GUI entry found for {__instance.gameObject.GetInstanceID()}");
                return;
            }

            foreach (TextMeshPro tmp in guiItem.m_additionalTxt.GetComponentsInChildren<TextMeshPro>())
            {
                // turning it off and on is required, otherwise the text doesn't update
                tmp.gameObject.SetActive(false);
                tmp.m_text = $"REQ: CELL -> {powerGenerator.m_itemKey}";
                tmp.characterSpacing = 8;
                tmp.gameObject.SetActive(true);
            }
        }
    }
    */
}