using BepInEx;
using BepInEx.Configuration;
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
using GTFO.API.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Unity.Collections;
using System.Linq;

namespace InformativeDoorIcons
{
    [BepInPlugin("informativedooricons.HazardousMonkey", "InformativeDoorIcons", "1.1.0")]
    [BepInDependency("dev.gtfomodding.gtfo-api")]
    public class InformativeDoorIconsPlugin : BasePlugin
    {
        public static InformativeDoorIconsPlugin Instance { get; private set; }

        // Toggles
        public static ConfigEntry<bool> ChangeApexToAlarm;
        public static ConfigEntry<bool> StyleFreeSecurityDoors;
        public static ConfigEntry<bool> FixDoorNamePositions;
        public static ConfigEntry<bool> SecurityDoorKeycardMatchColor;
        public static ConfigEntry<bool> ChangeWeakDoorColors;

        // Colors
        public static ConfigEntry<string> MeleeClosedColorHex;
        public static ConfigEntry<float>  MeleeClosedAlpha;
        public static ConfigEntry<string> MeleeOpenColorHex;
        public static ConfigEntry<float>  MeleeOpenAlpha;
        public static ConfigEntry<string> HackClosedColorHex;
        public static ConfigEntry<float>  HackClosedAlpha;
        public static ConfigEntry<string> HackOpenColorHex;
        public static ConfigEntry<float>  HackOpenAlpha;

        public override void Load()
        {
            Instance = this;

            // General Toggles
            ChangeApexToAlarm             = Config.Bind("Settings", "Change Apex label to Alarm", true, "Because Apex doors are always alarmed, this change makes that more obvious at a glance.");
            FixDoorNamePositions          = Config.Bind("Settings", "ImproveDoorNamePositions", true, "Bump the pixle hight on doors a bit so their titles aren't inside the door sprite.");

            // Color stuff
            StyleFreeSecurityDoors        = Config.Bind("Settings", "Change the color for non-alarm Security Doors", true, "Add green coloration to non-alarmed \"free\" Security Door map icons.");
            SecurityDoorKeycardMatchColor = Config.Bind("Settings", "- Security Doors Match Keycard Color", true, "If a door is locked via Keycard, the interior sprite of the door will now match the Keycard color until unlocked.");
            ChangeWeakDoorColors          = Config.Bind("Settings", "- Change Locked Weak Door Colors", true, "If a Weak Door (a breackable doors) is locked, their sprite color reflects that.");

            MeleeClosedColorHex           = Config.Bind("Settings", "MeleeClosedColor", "#FFFF00", "Color of a melee-locked Weak Door icon when closed. #rrggbb");
            MeleeClosedAlpha              = Config.Bind("Settings", "MeleeClosedAlpha", 0.8f,
                new ConfigDescription("Opacity of the melee-locked icon (closed state), 0.0-1.0.",
                    new AcceptableValueRange<float>(0f, 1f)));

            MeleeOpenColorHex = Config.Bind("Settings", "MeleeOpenColor", "#FFFF00", "Color of a melee-locked Weak Door icon when open. #rrggbb");
            MeleeOpenAlpha = Config.Bind("Settings", "MeleeOpenAlpha", 0.1f,
                new ConfigDescription("Opacity of the melee-locked icon (open state), 0.0-1.0.",
                    new AcceptableValueRange<float>(0f, 1f)));

            HackClosedColorHex = Config.Bind("Settings", "HackClosedColor", "#00FFFF", "Color of a hackable Weak Door icon when closed. #rrggbb");
            HackClosedAlpha = Config.Bind("Settings", "HackClosedAlpha", 0.6f,
                new ConfigDescription("Opacity of the hackable icon (closed state), 0.0-1.0.",
                    new AcceptableValueRange<float>(0f, 1f)));
                    
            HackOpenColorHex = Config.Bind("Settings", "HackOpenColor", "#00FFFF", "Color of a hackable Weak Door icon when open. #rrggbb");
            HackOpenAlpha = Config.Bind("Settings", "HackOpenAlpha", 0.1f,
                new ConfigDescription("Opacity of the hackable icon (open state), 0.0-1.0.",
                    new AcceptableValueRange<float>(0f, 1f)));

            // ---- Register DoorIconsUpdater with IL2CPP's type system ----
            ClassInjector.RegisterTypeInIl2Cpp<DoorIconsUpdater>();

            new Harmony("informativedooricons.HazardousMonkey").PatchAll();

            // ---- Load the custom KEY_BLACK outline sprite ----
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // KEY_BLACK sec door outline
                byte[] pngOutlineBytes        = File.ReadAllBytes(Path.Combine(pluginDir, "symbol_door_security_map_inner_outline.png"));

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                tex.filterMode = FilterMode.Point;
                tex.LoadImage(pngOutlineBytes);
                DoorManager.BlackKeyOutlineSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 64f);
            }
            catch (Exception e)
            {
                Debug.LogError($"[InformativeDoorIcons] Failed to load outline sprite: {e.Message}");
            }

            // ---- Hot-reload watcher ----
            // LiveEdit fires whenever the config file is saved.
            // We only set the dirty flag here, while the actual work happens inside DoorIconsUpdater.Update()
            LiveEditListener cfgListener = LiveEdit.CreateListener(Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath), false); cfgListener.FileChanged += _ =>
            {
                Config.Reload();
                DoorManager.MarkConfigDirty();
            };

            DoorManager.RefreshConfig();

            Log.LogInfo("[InformativeDoorIcons] Locked and Loaded.");
        }
    }


    // ============================================================
    // Created on (DontDestroyOnLoad) from the first CM_SyncedGUIItem.Setup a session.
    // Checks the dirty flag every frame. When set, it refreshes the config cache and re-applies all configurable visuals to every registered door.
    // ============================================================
    public class DoorIconsUpdater : MonoBehaviour
    {
        // Required IL2CPP constructor.
        public DoorIconsUpdater(IntPtr ptr) : base(ptr) { }

        private static GameObject _updaterHost;

        public static void EnsureCreated()
        {
            if (_updaterHost != null && _updaterHost.Pointer != IntPtr.Zero) return;
            _updaterHost = new GameObject("[InformativeDoorIcons] UpdaterHost");
            GameObject.DontDestroyOnLoad(_updaterHost);
            _updaterHost.AddComponent(Il2CppType.Of<DoorIconsUpdater>()).Cast<DoorIconsUpdater>();

            Debug.Log("[InformativeDoorIcons] UpdaterHost host created.");
        }

        void Update()
        {
            if (DoorManager.ConsumeConfigDirty())
            {
                DoorManager.RefreshConfig();
                DoorManager.ApplyConfigToAllDoors();
            }
        }
    }


    // ============================================================
    // DoorManager:
    // - Keeps registries of every door's CM_SyncedGUIItem + physical-door component, keyed by GetInstanceID() of the physical door GameObject.
    // - Cache hot-reload config values, which are refreshed on each save.
    // - ApplyConfigToAllDoors(): iterates both registries and re-applies all config-driven visuals in-session.
    // - Shared sprite-name helpers used by both the Setup patch and hot-reload.
    // ============================================================
    public static class DoorManager
    {
        // ---- Registry entry types ----
        // Store the door reference and metadata so we don't need to re-traverse the hierarchy.
        internal struct WeakDoorEntry
        {
            public CM_SyncedGUIItem GuiItem;
            public LG_WeakDoor      PhysicalDoor;
            public bool             IsMeleeLocked;
            public bool             IsHack;
            public Vector3          OriginalLocatorPos; // Captured BEFORE the Setup patch moves the text for the first time, so the FixDoorNamePositions revert path has the true original position.
        }

        internal struct SecurityDoorEntry
        {
            public CM_SyncedGUIItem  GuiItem;
            public LG_SecurityDoor   PhysicalDoor;
            public Vector3           OriginalLocatorPos;      // Captured BEFORE the Setup patch moves the text.
            public float             OriginalApexCharSpacing; // Captured BEFORE the Setup patch calls ForceMeshUpdate() on the APEX TextMeshPro so the ChangeApexToAlarm revert path is correct.
            public List<GameObject>  BlackKeyOutlineClones;   // KEY_BLACK outline duplicates; toggled active/inactive with CfgSecurityDoorKeycardMatchColor.
        }

        // ---- Registries ----
        internal static readonly Dictionary<int, WeakDoorEntry>     s_weakDoor     = new();
        internal static readonly Dictionary<int, SecurityDoorEntry> s_securityDoor = new();

        // Registers a Weak Door. Must be called BEFORE ApplyConfigToWeakDoor so that OriginalLocatorPos is captured from the unmodified transform.
        public static void RegisterWeakDoor(
            int              physicalDoorInstanceId,
            CM_SyncedGUIItem guiItem,
            LG_WeakDoor      physicalDoor,
            bool             isMeleeLocked,
            bool             isHack)
        {
            s_weakDoor[physicalDoorInstanceId] = new WeakDoorEntry
            {
                GuiItem            = guiItem,
                PhysicalDoor       = physicalDoor,
                IsMeleeLocked      = isMeleeLocked,
                IsHack             = isHack,
                OriginalLocatorPos = guiItem.m_locatorTxt.gameObject.transform.localPosition,
            };
        }

        // Must be called BEFORE ApplyConfigToSecurityDoor.
        // Registers a Security Door. Captures OriginalLocatorPos and OriginalApexCharSpacing from the live objects BEFORE they're adjusted.
        public static void RegisterSecurityDoor(int physicalDoorInstanceId, CM_SyncedGUIItem guiItem, LG_SecurityDoor  physicalDoor)
        {
            // Capture the APEX characterSpacing before the Setup patch changes it.
            // 0f is the correct fallback — it is Unity's TextMeshPro default.
            float origApexSpacing = 0f;
            foreach (TextMeshPro tmp in guiItem.m_gfxSecureApex.GetComponentsInChildren<TextMeshPro>())
            {
                if (tmp.m_text.ContainsIgnoreCase("APEX"))
                {
                    origApexSpacing = tmp.characterSpacing; // reminder: get the real values
                    break;
                }
            }

            s_securityDoor[physicalDoorInstanceId] = new SecurityDoorEntry
            {
                GuiItem                 = guiItem,
                PhysicalDoor            = physicalDoor,
                OriginalLocatorPos      = guiItem.m_locatorTxt.gameObject.transform.localPosition,
                OriginalApexCharSpacing = origApexSpacing,
            };
        }

        // ---- Entry read accessors ----
        // Used by the Setup patch to retrieve the just-registered entry (complete with its captured originals) and pass it straight into the Apply helpers.
        internal static bool TryGetWeakDoorEntry(int id, out WeakDoorEntry doorEntry)
            => s_weakDoor.TryGetValue(id, out doorEntry);

        internal static bool TryGetSecurityDoorEntry(int id, out SecurityDoorEntry doorEntry)
            => s_securityDoor.TryGetValue(id, out doorEntry);

        // Writes a (modified) SecurityDoorEntry back into the registry.
        // Required because SecurityDoorEntry is a struct — TryGet returns a copy, so mutations must be stored explicitly.
        internal static void StoreSecurityDoorEntry(int id, SecurityDoorEntry entry)
            => s_securityDoor[id] = entry;

        // ---- Backward-compatible CM_SyncedGUIItem accessors ----
        // Preserves the signatures used by LG_WeakLock.OnSyncStatusChanged.

        public static bool TryGetWeakDoorGUI(int id, out CM_SyncedGUIItem guiItem)
        {
            if (s_weakDoor.TryGetValue(id, out WeakDoorEntry doorEntry)) { guiItem = doorEntry.GuiItem; return true; }
            guiItem = null;
            return false;
        }

        public static bool TryGetSecurityDoorGUI(int id, out CM_SyncedGUIItem guiItem)
        {
            if (s_securityDoor.TryGetValue(id, out SecurityDoorEntry doorEntry)) { guiItem = doorEntry.GuiItem; return true; }
            guiItem = null;
            return false;
        }

        // ---- Sprite assets ----
        public static Sprite BlackKeyOutlineSprite { get; set; } = null;
        public static Sprite BulkMainSprite { get; set; } = null;
        public static Sprite BulkSecondarySprite { get; set; } = null;
        public static Sprite BulkOverloadSprite { get; set; } = null;
        public static Sprite BulkSecondaryOpenSprite { get; set; } = null;
        public static Sprite BulkOverloadOpenSprite { get; set; } = null;

        // ---- Cached config values ----
        // All reads during Setup and hot-reload go through these fields.
        // Toggles:
        public static bool CfgFixDoorNamePositions          = true;
        public static bool CfgChangeWeakDoorColors          = true;
        public static bool CfgChangeApexToAlarm             = true;
        public static bool CfgStyleFreeSecurityDoors        = true;
        public static bool CfgSecurityDoorKeycardMatchColor = true;

        // Weak door lock colors.
        public static Color CfgMeleeClosedColor = new Color(1f, 1f, 0f, 0.8f);  // default: yellow-ish
        public static Color CfgMeleeOpenColor   = new Color(1f, 1f, 0f, 0.3f);
        public static Color CfgHackClosedColor  = new Color(0f, 1f, 1f, 0.6f);  // default: cyan
        public static Color CfgHackOpenColor    = new Color(0f, 1f, 1f, 0.3f);

        // ---- Live-config dirty flag ----
        private static volatile bool _configDirty = false;
        public static void MarkConfigDirty() => _configDirty = true;
        public static bool ConsumeConfigDirty()
        {
            if (!_configDirty) return false;
            _configDirty = false;
            return true;
        }

        // ---- Config refresh ----
        // Reads all ConfigEntry<T>.Value fields and updates the cached statics above.
        // Contains no Unity API calls, but is always invoked on the main thread in practice (from Load() at startup and from DoorIconsUpdater.Update() at runtime).
        public static void RefreshConfig()
        {
            CfgFixDoorNamePositions          = InformativeDoorIconsPlugin.FixDoorNamePositions.Value;
            CfgChangeWeakDoorColors          = InformativeDoorIconsPlugin.ChangeWeakDoorColors.Value;
            CfgChangeApexToAlarm             = InformativeDoorIconsPlugin.ChangeApexToAlarm.Value;
            CfgStyleFreeSecurityDoors        = InformativeDoorIconsPlugin.StyleFreeSecurityDoors.Value;
            CfgSecurityDoorKeycardMatchColor = InformativeDoorIconsPlugin.SecurityDoorKeycardMatchColor.Value;

            CfgMeleeClosedColor = ParseColorWithAlpha(InformativeDoorIconsPlugin.MeleeClosedColorHex.Value, new Color(1f, 1f, 0f),InformativeDoorIconsPlugin.MeleeClosedAlpha.Value);
            CfgMeleeOpenColor   = ParseColorWithAlpha(InformativeDoorIconsPlugin.MeleeOpenColorHex.Value,   new Color(1f, 1f, 0f),InformativeDoorIconsPlugin.MeleeOpenAlpha.Value);
            CfgHackClosedColor  = ParseColorWithAlpha(InformativeDoorIconsPlugin.HackClosedColorHex.Value,  new Color(0f, 1f, 1f),InformativeDoorIconsPlugin.HackClosedAlpha.Value);
            CfgHackOpenColor    = ParseColorWithAlpha(InformativeDoorIconsPlugin.HackOpenColorHex.Value,    new Color(0f, 1f, 1f),InformativeDoorIconsPlugin.HackOpenAlpha.Value);

            // juicy logs
            Debug.Log($"[InformativeDoorIcons] Config refreshed -> " +
                      $"fixPosNames={CfgFixDoorNamePositions} "      +
                      $"weakColors={CfgChangeWeakDoorColors} "       +
                      $"apex={CfgChangeApexToAlarm} "                +
                      $"freeStyle={CfgStyleFreeSecurityDoors} "      + 
                      $"keycard={CfgSecurityDoorKeycardMatchColor} " +
                      $"meleeC={CfgMeleeClosedColor} "               + 
                      $"meleeO={CfgMeleeOpenColor} "                 +
                      $"hackC={CfgHackClosedColor} "                 +
                      $"hackO={CfgHackOpenColor}");
        }

        // Parses a #rrggbb hex string and combines it with a separate alpha float.
        private static Color ParseColorWithAlpha(string hex, Color fallbackRGB, float alpha)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return new Color(c.r, c.g, c.b, alpha);

            Debug.LogWarning($"[InformativeDoorIcons] Could not parse color '{hex}' — using fallback.");
            return new Color(fallbackRGB.r, fallbackRGB.g, fallbackRGB.b, alpha);
        }

        // ---- Hot-reload re-apply ----
        // Re-applies all config-driven visuals to every registered door.
        // Called from DoorIconsUpdater.Update() on ConsumeConfigDirty()
        public static void ApplyConfigToAllDoors()
        {
            foreach (WeakDoorEntry entry in s_weakDoor.Values)
                ApplyConfigToWeakDoor(entry);

            foreach (KeyValuePair<int, SecurityDoorEntry> kvp in s_securityDoor)
            {
                ApplyConfigToSecurityDoor(kvp.Value);

                // If the toggle just came back on and this is a KEY_BLACK door whose outline clones
                // were never created (because the toggle was off during Setup), create them now.
                if (CfgSecurityDoorKeycardMatchColor
                    && kvp.Value.PhysicalDoor.m_keyItem != null
                    && kvp.Value.PhysicalDoor.m_keyItem.m_keyName == "KEY_BLACK"
                    && (kvp.Value.BlackKeyOutlineClones == null || kvp.Value.BlackKeyOutlineClones.Count == 0))
                {
                    CreateBlackKeyOutlineClones(kvp.Key, kvp.Value);
                }
            }

            Debug.Log($"[InformativeDoorIcons] Hot-reload applied to {s_weakDoor.Count} weak door(s) and {s_securityDoor.Count} security door(s).");
        }

        // Applies (or reverts) all config-driven changes for a single Weak Door entry.
        // Called from both the Setup patch (initial run, using the freshly-registered entry) and from ApplyConfigToAllDoors()
        internal static void ApplyConfigToWeakDoor(in WeakDoorEntry doorEntry)
        {
            if (doorEntry.GuiItem == null) return;

            // ---- Fix door name position ----
            doorEntry.GuiItem.m_locatorTxt.gameObject.transform.localPosition = CfgFixDoorNamePositions
                ? new Vector3(0f, 5.5f, 0f)
                : doorEntry.OriginalLocatorPos;

            // ---- Weak door color ----
            if (!CfgChangeWeakDoorColors)
            {
                // Revert to the game's default inner-sprite colors.
                // On an initial Setup run with the feature off since the sprites are already at these values.
                SetWeakDoorInnerColors(doorEntry.GuiItem, WeakDoorDefaultClosed, WeakDoorDefaultOpen);
                return;
            }

            // Bit dumb, but we need to check this for hot-loading stuff, otherwise toggling on & off will cause previously unlocked doors to appear locked again.
            LG_WeakDoor physicalDoor_WL = doorEntry.PhysicalDoor.GetComponent<LG_WeakDoor>();
            if (physicalDoor_WL != null)
            {
                if (physicalDoor_WL.WeakLocks[0].Status == eWeakLockStatus.Unlocked && physicalDoor_WL.WeakLocks[1].Status == eWeakLockStatus.Unlocked) return;
            }

            if (doorEntry.IsMeleeLocked)
                SetWeakDoorInnerColors(doorEntry.GuiItem, CfgMeleeClosedColor, CfgMeleeOpenColor);
            else if (doorEntry.IsHack)
                SetWeakDoorInnerColors(doorEntry.GuiItem, CfgHackClosedColor, CfgHackOpenColor);
        }

        // Applies (or reverts) all config-driven changes for a single Security Door entry.
        // Called from both the Setup patch and ApplyConfigToAllDoors()
        internal static void ApplyConfigToSecurityDoor(in SecurityDoorEntry doorEntry)
        {
            if (doorEntry.GuiItem == null) return;

            // ---- Fix door name position ----
            doorEntry.GuiItem.m_locatorTxt.gameObject.transform.localPosition = CfgFixDoorNamePositions ? new Vector3(0f, 5.5f, 0f) : doorEntry.OriginalLocatorPos;

            // ---- Fix "APEX" text z-fighting ----
            foreach (MeshRenderer mesh in doorEntry.GuiItem.m_gfxSecureApex.GetComponentsInChildren<MeshRenderer>())
                mesh.sortingOrder = 2;

            // ---- Replace "APEX" label with "ALARM" (or revert) ----
            // Combined both states so the check works on any re-apply regardless of what a previous run may have written.
            foreach (TextMeshPro tmp in doorEntry.GuiItem.m_gfxSecureApex.GetComponentsInChildren<TextMeshPro>())
            {
                bool isApex  = tmp.m_text.ContainsIgnoreCase("APEX");
                bool isAlarm = tmp.m_text.ContainsIgnoreCase("ALARM");
                if (!isApex && !isAlarm) continue; // if neither, gtfo

                if (CfgChangeApexToAlarm)
                {
                    tmp.text             = "ALARM";
                    tmp.characterSpacing = 8f;
                }
                else
                {
                    tmp.text             = "APEX";
                    tmp.characterSpacing = doorEntry.OriginalApexCharSpacing;
                }
                tmp.ForceMeshUpdate();
            }

            // ---- Style "free" (non-alarm) Security Doors ----
            // Green if True, Sec-Red if False
            foreach (SpriteRenderer sprite in doorEntry.GuiItem.m_gfxSecureClosed.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsClampSprite(sprite.gameObject.name)) continue;
                sprite.color = CfgStyleFreeSecurityDoors ? new Color(0f, 1f, 0.5f, 1f) : new Color(0.7647f, 0.2745f, 0.2745f, 1); // not the same as Bulkhead symbol's "red"
            }

            // ---- Security Door keycard color matching ----
            // keyColor if True, ~red if False
            if (doorEntry.PhysicalDoor.m_keyItem != null)
            {
                string key      = doorEntry.PhysicalDoor.m_keyItem.m_keyName;
                Color  keyColor = CfgSecurityDoorKeycardMatchColor ? GetKeyColor(key) : new Color(0.7647f, 0.2745f, 0.2745f, 1); // not the same as Bulkhead symbol's "red"

                foreach (SpriteRenderer sprite in doorEntry.GuiItem.m_gfxSecureKeycard.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!IsInnerSprite(sprite.gameObject.name)) continue;
                    sprite.color = keyColor;
                }
            }

            // ---- Toggle KEY_BLACK outline clones ----
            // The clones are part of the keycard coloring system, so they follow the same toggle.
            if (doorEntry.BlackKeyOutlineClones != null)
            {
                foreach (GameObject clone in doorEntry.BlackKeyOutlineClones)
                {
                    if (clone != null)
                        clone.SetActive(CfgSecurityDoorKeycardMatchColor);
                }
            }
        }

        // ---- KEY_BLACK outline clone creation ----
        // Creates the outline clones for a KEY_BLACK Security Door and stores them in the registry entry.
        // Called from the Setup patch (initial creation) and from ApplyConfigToAllDoors (late creation when
        // the toggle was off at Setup time and is later turned back on mid-session).
        internal static void CreateBlackKeyOutlineClones(int instanceId, SecurityDoorEntry entry)
        {
            // Fallback sprite load if the plugin Load() attempt failed.
            if (BlackKeyOutlineSprite == null)
            {
                try
                {
                    string pluginDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    byte[] pngBytes  = System.IO.File.ReadAllBytes(System.IO.Path.Combine(pluginDir, "symbol_door_security_map_inner_outline.png"));
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                    tex.filterMode = FilterMode.Point;
                    tex.LoadImage(pngBytes);
                    BlackKeyOutlineSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 64f);
                    Debug.LogWarning("[InformativeDoorIcons] BlackKeyOutlineSprite was null; created fallback in CreateBlackKeyOutlineClones.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[InformativeDoorIcons] CreateBlackKeyOutlineClones: failed to load outline sprite: {e.Message}");
                    return;
                }
            }

            List<GameObject> createdClones = new();

            foreach (SpriteRenderer sprite in entry.GuiItem.m_gfxSecureKeycard.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsInnerSprite(sprite.gameObject.name)) continue;

                GameObject     duplicate   = GameObject.Instantiate(sprite.gameObject, sprite.gameObject.transform.parent);
                SpriteRenderer duplicateSR = duplicate.GetComponent<SpriteRenderer>();
                duplicateSR.sprite         = BlackKeyOutlineSprite;
                duplicateSR.color          = Color.white;
                duplicateSR.sortingOrder  -= 1;

                duplicateSR.gameObject.transform.localPosition = duplicateSR.gameObject.transform.localPosition.x > 0
                    ? new Vector3( 0.176f, duplicateSR.gameObject.transform.localPosition.y, duplicateSR.gameObject.transform.localPosition.z)
                    : new Vector3(-0.176f, duplicateSR.gameObject.transform.localPosition.y, duplicateSR.gameObject.transform.localPosition.z);

                createdClones.Add(duplicate);
            }

            entry.BlackKeyOutlineClones = createdClones;
            StoreSecurityDoorEntry(instanceId, entry);
        }

        // ---- Weak door inner-sprite color helper ----
        // Moved all the stuff from the harmony patch to static functions so the .Setup() and hot-reloading path share a single definition.
        internal static bool IsInnerSprite(string name) => !name.Contains("(Clone)") && (name.EndsWith("_inner") || (name.Contains("_inner (") && name.EndsWith(")")));
        internal static bool IsClampSprite(string name) => !name.Contains("(Clone)") && (name.EndsWith("_clamp") || (name.Contains("_clamp (") && name.EndsWith(")")));
        internal static bool IsInnerBulkheadSymbolSprite(string name) => name.ContainsIgnoreCase("symbol_bulkhead");
        private static void SetWeakDoorInnerColors(CM_SyncedGUIItem guiItem, Color closedColor, Color openColor)
        {
            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakClosed.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = closedColor;
            }
            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakOpen.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = openColor;
            }
        }

        // -------------------------------------------------------------------------
        // GIMME DEM COLORS!!!!
        // -------------------------------------------------------------------------
        // Default inner-sprite colors restored when a weak door becomes fully unlocked
        public static readonly Color WeakDoorDefaultClosed = new(0.3373f, 0.3529f, 0.2549f, 1f);
        public static readonly Color WeakDoorDefaultOpen   = new(0.3373f, 0.3529f, 0.2549f, 0.0549f);
        private static Color GetKeyColor(string keyName)
        {
            const string prefix = "KEY_";
            if (!keyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[InformativeDoorIcons] '{keyName}' did not contain a valid prefix.");
                return new Color(0.4191f, 0.1387f, 0.1387f, 1);
            }

            string colorName = keyName.Substring(prefix.Length); // IE: "ORANGE", "PURPLE", "GREEN"

            // Manual color overrides for hues that Unity/HTML names render poorly in-game.
            switch (colorName)
            {
                case "BLUE":   return new Color(0f,      0.33f,   1f,      1f); // HTML blue looks purple in-game
                case "GREEN":  return new Color(0f,      1f,      0.2f,    1f); // Default green is too dark
                case "PURPLE": return new Color(0.6235f, 0.1137f, 0.9372f, 1f); // #800080 is too dark
            }

            if (ColorUtility.TryParseHtmlString(colorName, out Color result))
                return result;

            // Fallback
            Debug.LogWarning($"[InformativeDoorIcons] Could not resolve color from key name: '{keyName}'");
            return new Color(0.4191f, 0.1387f, 0.1387f, 1);
        }

        // ============================================================
        // Called from CM_SyncedGUIItem.SetVisible(), with the objective of improving and fixing them.
        // Icons now match their objective via sprite replacement, they are on a non-broken sortOrder, and are rotated to face "north" for a better glance value.
        // ============================================================
        internal static void BulkheadDoorIconSwap(LG_SecurityDoor doorSL, CM_SyncedGUIItem GUI)
        {
            static bool IsInnerBulkheadSymbolSprite(string name) => name.ContainsIgnoreCase("symbol_bulkhead");

            // make a list, and apply stuff there, so we don't have to duplicate code several times
            // Applied at the bottom if count > 0
            List<SpriteRenderer> bulkSymbols = new();

            if (doorSL.LinksToLayerType == LG_LayerType.MainLayer)
            {
                foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadClosed.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                    if (BulkMainSprite != null) sprite.sprite = BulkMainSprite;
                    bulkSymbols.Add(sprite);
                }
                
                foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadOpen.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                    if (BulkMainSprite != null) sprite.sprite = BulkMainSprite;
                    bulkSymbols.Add(sprite);
                }
            }
            else if (doorSL.LinksToLayerType == LG_LayerType.SecondaryLayer)
            {
                    foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadClosed.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                        if (BulkSecondarySprite != null) sprite.sprite = BulkSecondarySprite;
                        bulkSymbols.Add(sprite);
                    }

                    foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadOpen.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                        if (BulkSecondarySprite != null) sprite.sprite = BulkSecondarySprite;
                        bulkSymbols.Add(sprite);
                    }
            }
            else if (doorSL.LinksToLayerType == LG_LayerType.ThirdLayer)
            {
                    foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadClosed.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                        if (BulkOverloadSprite != null)  sprite.sprite = BulkOverloadSprite;
                        bulkSymbols.Add(sprite);
                    }

                    foreach (SpriteRenderer sprite in GUI.m_gfxBulkheadOpen.GetComponentsInChildren<SpriteRenderer>())
                    {
                        if (!IsInnerBulkheadSymbolSprite(sprite.gameObject.name)) continue;
                        if (BulkOverloadSprite != null)  sprite.sprite = BulkOverloadSprite;
                        bulkSymbols.Add(sprite);
                    }
            }

            // Make sure there's no color tint and that we're max Alpha
            // Put symbol on top of other door sprites
            // Rotate to match expected north
            if (bulkSymbols.Count > 0)
            {
                foreach (SpriteRenderer sprite in bulkSymbols)
                {
                    sprite.color = new Color(1, 1, 1, 1);
                    sprite.sortingOrder = 2;
                    sprite.transform.localRotation = Quaternion.Euler(0, 0, 360 - GUI.transform.localRotation.eulerAngles.z);
                }
            }
        }
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

    // ============================================================
    //  Reset our door registers every mission.
    // ============================================================
    [HarmonyPatch(typeof(GS_Lobby), nameof(GS_Lobby.TryStartLevelTrigger))]
    public static class InformativeDoorIcons_GS_Lobby_TryStartLevelTrigger_NameChangeForDebug
    {
        public static void Postfix(GS_Lobby __instance)
        {
            if (DoorManager.s_weakDoor.Count > 0) DoorManager.s_weakDoor.Clear();
            if (DoorManager.s_securityDoor.Count > 0) DoorManager.s_securityDoor.Clear();
        }
    }
    // ============================================================
    //  Resets a weak door's custom inner-sprite color back to the game defaults once both locks have been unlocked.
    // ============================================================
    [HarmonyPatch(typeof(LG_WeakLock), nameof(LG_WeakLock.OnSyncStatusChanged))]
    public static class InformativeDoorIcons_LG_WeakLock_OnSyncStatusChanged_DoorLockCheckForUnlocked
    {
        public static void Postfix(LG_WeakLock __instance, eWeakLockStatus status)
        {
            if (status != eWeakLockStatus.Unlocked) return;

            // If we never applied custom colors, there is nothing to reset.
            if (!DoorManager.CfgChangeWeakDoorColors) return;

            // lmfao
            // LG_WeakDoor physicalDoor_WL = __instance.gameObject.transform.parent.transform.parent.transform.parent.transform.parent.transform.parent.GetComponent<LG_WeakDoor>();
            LG_WeakDoor physicalDoor_WL = __instance.gameObject.GetComponentInParents<LG_WeakDoor>();

            if (physicalDoor_WL == null) return;

            // If either of the locks != Unlocked, don't act. We need both to be Unlocked.
            if (physicalDoor_WL.WeakLocks[0].Status != eWeakLockStatus.Unlocked || physicalDoor_WL.WeakLocks[1].Status != eWeakLockStatus.Unlocked) return;

            // Look up the map-marker GUI that was registered during Setup.
            if (!DoorManager.TryGetWeakDoorGUI(physicalDoor_WL.gameObject.GetInstanceID(), out CM_SyncedGUIItem guiItem))
            {
                Debug.LogWarning("[InformativeDoorIcons] OnSyncStatusChanged: no GUI entry found for unlocked weak door.");
                return;
            }

            // closed sprite
            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakClosed.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!DoorManager.IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = DoorManager.WeakDoorDefaultClosed;
            }
            // open sprite
            foreach (SpriteRenderer sprite in guiItem.m_gfxWeakOpen.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!DoorManager.IsInnerSprite(sprite.gameObject.name)) continue;
                sprite.color = DoorManager.WeakDoorDefaultOpen;
            }
        }
    }


    // ============================================================
    // The hotness. Runs once per door per map-load as each door GUI is created.
    // - Guard on m_gfxSecureApex to confirm this is a door GUI. I'm pretty sure all non-door GUI have null door sprites, so any of them would work.
    // - Ensure DoorIconsUpdater is alive (hot-reload requires it).
    // - Figure out what kind of door it is.
    // - Register the door.
    // - Push the door entry to the external function so that I don't have to do this twice with the hot-loader.
    // - Make sure the KEY_BLACK custom sprite is loaded if needed.
    // ============================================================
    [HarmonyPatch(typeof(CM_SyncedGUIItem), nameof(CM_SyncedGUIItem.Setup))]
    public static class InformativeDoorIcons_CM_SyncedGUIItem_Setup_DoorColoration
    {
        public static void Postfix(CM_SyncedGUIItem __instance)
        {
            if (__instance.m_gfxSecureApex == null) return; // null = Not a door GUI.

            // Ensure the per-frame updater exists (created once, persists for the session).
            DoorIconsUpdater.EnsureCreated();

            GameObject physicalDoor_GO = __instance.RevealerBase.transform.parent.parent.gameObject;
            int        instanceId      = physicalDoor_GO.GetInstanceID();

            __instance.m_locatorTxt.GetComponent<MeshRenderer>().sortingOrder = 10;

            // ---- Weak Door ----
            LG_WeakDoor physicalDoor_WL = physicalDoor_GO.GetComponent<LG_WeakDoor>();
            if (physicalDoor_WL != null)
            {
                // Make sure it has locks
                if (physicalDoor_WL.WeakLocks == null) return;
                // Used for late-join protection
                if (physicalDoor_WL.WeakLocks[0].Status == eWeakLockStatus.Unlocked && physicalDoor_WL.WeakLocks[1].Status == eWeakLockStatus.Unlocked) return;

                bool isMeleeLocked = physicalDoor_WL.WeakLocks[0].m_lockType == eWeakLockType.Melee    || physicalDoor_WL.WeakLocks[1].m_lockType == eWeakLockType.Melee;
                bool isHack        = physicalDoor_WL.WeakLocks[0].m_lockType == eWeakLockType.Hackable || physicalDoor_WL.WeakLocks[1].m_lockType == eWeakLockType.Hackable;

                // Register: captures OriginalLocatorPos from the unmodified transform.
                DoorManager.RegisterWeakDoor(instanceId, __instance, physicalDoor_WL, isMeleeLocked, isHack);
                // Debug.LogWarning($"[InformativeDoorIcons] WeakDoor registered: {instanceId}");

                // Retrieve the stored entry w/ orig values, then push to color logic. Done to avoid duplicate re-color logic for hot-loading.
                if (DoorManager.TryGetWeakDoorEntry(instanceId, out DoorManager.WeakDoorEntry wEntry))
                    DoorManager.ApplyConfigToWeakDoor(wEntry);

                return;
            }


            // ---- Security Door ----
            LG_SecurityDoor physicalDoor_SL = physicalDoor_GO.GetComponent<LG_SecurityDoor>();
            if (physicalDoor_SL != null)
            {

                // Register: captures OriginalLocatorPos and OriginalApexCharSpacing.
                DoorManager.RegisterSecurityDoor(instanceId, __instance, physicalDoor_SL);
                // Debug.LogWarning($"[InformativeDoorIcons] SecurityDoor registered: {instanceId}");

                // Retrieve the stored entry w/ orig values, then push to color logic. Done to avoid duplicate re-color logic for hot-loading.
                if (DoorManager.TryGetSecurityDoorEntry(instanceId, out DoorManager.SecurityDoorEntry sEntry))
                    DoorManager.ApplyConfigToSecurityDoor(sEntry);

                // ---- KEY_BLACK outline sprite (Setup-only) ----
                // ApplyConfigToSecurityDoor re-applies the inner-sprite color on hot-reload.
                if (DoorManager.CfgSecurityDoorKeycardMatchColor && physicalDoor_SL.m_keyItem != null && physicalDoor_SL.m_keyItem.m_keyName == "KEY_BLACK")
                {
                    if (DoorManager.TryGetSecurityDoorEntry(instanceId, out DoorManager.SecurityDoorEntry sEntryForClones))
                        DoorManager.CreateBlackKeyOutlineClones(instanceId, sEntryForClones);
                }

                // We're prepping the custom sprites here, which SHOULD make them ready to be set when needed on Visible() call
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                if (physicalDoor_SL.LinksToLayerType == LG_LayerType.MainLayer) // Main
                {
                    if (DoorManager.BulkMainSprite == null)
                    {
                        byte[] pngBytes  = File.ReadAllBytes(Path.Combine(pluginDir, "bulkMain.png"));
                        var tex          = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                        tex.filterMode   = FilterMode.Point;
                        tex.LoadImage(pngBytes);
                        DoorManager.BulkMainSprite  = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
                    }
                }
                else if (physicalDoor_SL.LinksToLayerType == LG_LayerType.SecondaryLayer) // Secondary "Extreme"
                {
                    if (DoorManager.BulkSecondarySprite == null)
                    {
                        byte[] pngBytes = File.ReadAllBytes(Path.Combine(pluginDir, "bulkSecondary.png"));
                        var tex         = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                        tex.filterMode  = FilterMode.Point;
                        tex.LoadImage(pngBytes);
                        DoorManager.BulkSecondarySprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
                    }
                }
                else if (physicalDoor_SL.LinksToLayerType == LG_LayerType.ThirdLayer) // Overload
                {
                    if (DoorManager.BulkOverloadSprite == null)
                    {
                        byte[] pngBytes = File.ReadAllBytes(Path.Combine(pluginDir, "bulkOverload.png"));
                        var tex         = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                        tex.filterMode  = FilterMode.Point;
                        tex.LoadImage(pngBytes);
                        DoorManager.BulkOverloadSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
                    }
                }
            }
        }
    }

    // ============================================================
    // Because CM_SyncedGUIItem sets its own rotation at some dump point, I've chosen to target its
    // visibility state, the best possible chance to have a set rotation, to make rotation changes.
    // Because I'm really lazy right now, I also tied in the icon swap in the same hook.
    //
    // To avoid clutter in this area, I've sent the references off to a dedicated class.
    // ============================================================
    [HarmonyPatch(typeof(CM_SyncedGUIItem), nameof(CM_SyncedGUIItem.SetVisible))]
    public static class InformativeDoorIcons_CM_SyncedGUIItem_SetVisible_BulkheadSymbolChangeAndRotation
    {
        public static void Prefix(CM_SyncedGUIItem __instance, bool visible)
        {
            if (__instance.m_gfxSecureApex == null || visible == false) return; // null = Not a door GUI.

            LG_SecurityDoor physicalDoor_SL = __instance.RevealerBase.transform.parent.parent.gameObject.GetComponent<LG_SecurityDoor>();

            if (physicalDoor_SL != null)
            {
                DoorManager.BulkheadDoorIconSwap(physicalDoor_SL, __instance);
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
