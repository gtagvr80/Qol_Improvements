#region Usings
using UnityEngine;
using MelonLoader;
using Il2CppInterop.Runtime.Injection;
using Il2Cpp;
using Il2CppFishNet.Object;
using Il2CppFishNet.Transporting.Relay;
using Il2CppSystem.Collections.Generic;
using System.Collections;
using System;
using HarmonyLib;
using System.Runtime.InteropServices;
using Il2CppCodeStage.AntiCheat.Storage;
using Il2CppInControl.mod;
#endregion

[assembly: MelonInfo(typeof(QOL_Improvements.Mod), "QOL Improvements", "1.0", "Krum20")]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
namespace QOL_Improvements
{
    public class Mod : MelonMod
    {
        public static LobbyMenu menu;
        MelonPreferences_Category QOL_Improvements;
        MelonPreferences_Entry<bool> FilterRooms;

        #region Methods
        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<BetterFlyCam>();

            QOL_Improvements = MelonPreferences.CreateCategory("QOL_Improvements");
            FilterRooms = QOL_Improvements.CreateEntry("Filter Bot/Spam Rooms", true);

            QOL_Improvements.SetFilePath("QOL Improvements/Settings.cfg");
        }

        public override void OnLateUpdate()
        {
            if (menu != null && menu.FAENEDGHCPN != null && FilterRooms.Value)
            {
                List<RelayRoomInfo> filteredRooms = new();

                foreach (RelayRoomInfo room in menu.FAENEDGHCPN)
                {
                    // So currently we remove bot/spam rooms if the maxplayers is 1 or less
                    // Or the room name is longer than 20 characters (aka max characters you can type in a room name by default)
                    if (room.MaxPlayers <= 1 || room.Name.Length > 20)
                        continue;
                    
                    filteredRooms.Add(room);
                }

                menu.FAENEDGHCPN = filteredRooms;
            }
        }
        #endregion
    }

    [HarmonyPatch(typeof(FlyCamController), nameof(FlyCamController.Awake))]
    public class FlyCamPatch
    {
        static void Postfix(FlyCamController __instance)
        {
            var obj = __instance.gameObject;
            MelonLogger.Msg($"[FlyCamPatch] Awake on '{obj.name}' | hasFlyCam: {obj.TryGetComponent<FlyCamController>(out _)} | hasCamera: {obj.TryGetComponent<Camera>(out _)}");

            if (!obj.TryGetComponent<FlyCamController>(out _))
                return;

            if (!obj.TryGetComponent<BetterFlyCam>(out _))
                obj.AddComponent<BetterFlyCam>();
        }
    }

    [HarmonyPatch(typeof(LobbyMenu), "Start")]
    public class LobbyMenuPatch
    {
        static void Postfix(LobbyMenu __instance) => Mod.menu = __instance;
    }

    // fpsmouselook patch made by ai (sorry)
    [HarmonyPatch(typeof(FPSMouseLook), "Update")]
    public static class FPSMouseLookPatch
    {
        #region Variables
        const float SingleAxisScale = 1f / 60f;

        // FPSMouseLook offsets
        const int OFF_AXES = 0x20, OFF_SENS = 0x24, OFF_AIM_SENS = 0x28;
        const int OFF_SENS_X = 0x2C, OFF_SENS_Y = 0x30;
        const int OFF_MIN_Y = 0x34, OFF_MAX_Y = 0x38, OFF_ROT_Y = 0x3C;
        const int OFF_FLYCAM = 0x40, OFF_WEAPMGR = 0x48, OFF_WEAPSCRIPT = 0x50;
        const int OFF_CUR_SENS = 0x58, OFF_MULT_X = 0x5C, OFF_MULT_Y = 0x60, OFF_TIMER = 0x6C;
        // WeaponManager / WeaponScript offsets
        const int OFF_MGR_SELECTED = 0x28, OFF_AIMED = 0x31;
        #endregion

        static float ReadF(IntPtr p, int off) =>
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, off));
        static void WriteF(IntPtr p, int off, float v) =>
            Marshal.WriteInt32(p, off, BitConverter.SingleToInt32Bits(v));

        [HarmonyPrefix]
        public static bool Prefix(FPSMouseLook __instance)
        {
            IntPtr p = __instance.Pointer;

            // Flycam: let the original run (it has WASD/QE movement)
            if (Marshal.ReadByte(p, OFF_FLYCAM) != 0) return true;

            // 1. Once-per-second sensitivity refresh from ObscuredPrefs
            float timer = ReadF(p, OFF_TIMER) + Time.deltaTime;
            if (timer > 1f)
            {
                int v = ObscuredPrefs.GetInt("Mouse", 0);
                float mult = v == 0 ? 2f : (v < 0 ? 1.5f : v + 1f);
                timer = 0f;
                WriteF(p, OFF_MULT_X, mult);
                WriteF(p, OFF_MULT_Y, mult);
            }
            WriteF(p, OFF_TIMER, timer);

            // 2. Pause check, then the "Menu" tag check
            if (Time.timeScale < 0.01f) return false;
            if (GameObject.FindGameObjectWithTag("Menu") != null) return false;

            // 3. Look logic: stock FPS Kit, no deltaTime in any mode
            float mouseX = cInput.GetAxis("Mouse X", 1f);
            float mouseY = cInput.GetAxis("Mouse Y", 1f);
            int axes = Marshal.ReadInt32(p, OFF_AXES);

            float sx = ReadF(p, OFF_SENS_X);
            float sy = ReadF(p, OFF_SENS_Y);
            if (axes != 0)
            {
                sx *= SingleAxisScale;
                sy *= SingleAxisScale;
            }

            float minY = ReadF(p, OFF_MIN_Y), maxY = ReadF(p, OFF_MAX_Y);
            float rotY = ReadF(p, OFF_ROT_Y);
            Transform t = __instance.transform;

            if (axes == 0)          // MouseXAndY
            {
                float rotX = t.localEulerAngles.y + mouseX * sx;
                rotY = Mathf.Clamp(rotY + mouseY * sy, minY, maxY);
                WriteF(p, OFF_ROT_Y, rotY);
                t.localEulerAngles = new Vector3(-rotY, rotX, 0f);
            }
            else if (axes == 1)     // MouseX only
            {
                t.Rotate(0f, mouseX * sx, 0f, Space.Self);
            }
            else                    // MouseY only
            {
                rotY = Mathf.Clamp(rotY + mouseY * sy, minY, maxY);
                WriteF(p, OFF_ROT_Y, rotY);
                t.localEulerAngles = new Vector3(-rotY, t.localEulerAngles.y, 0f);
            }

            // 4. Weapon lookup (cached at +0x50 like the original)
            WeaponScript weapScript = null;
            IntPtr mgrPtr = Marshal.ReadIntPtr(p, OFF_WEAPMGR);
            if (mgrPtr != IntPtr.Zero && new WeaponManager(mgrPtr) != null)
            {
                IntPtr selPtr = Marshal.ReadIntPtr(mgrPtr, OFF_MGR_SELECTED);
                WeaponScript sel = selPtr == IntPtr.Zero ? null : new WeaponScript(selPtr);
                if (sel != null)
                {
                    IntPtr cachedPtr = Marshal.ReadIntPtr(p, OFF_WEAPSCRIPT);
                    WeaponScript cached = cachedPtr == IntPtr.Zero ? null : new WeaponScript(cachedPtr);
                    weapScript = cached != null ? cached : sel.GetComponent<WeaponScript>();
                }
            }
            Marshal.WriteIntPtr(p, OFF_WEAPSCRIPT,
                weapScript != null ? weapScript.Pointer : IntPtr.Zero);

            // 5. Aim sensitivity (writes the real, unscaled values back to the game)
            float cur = ReadF(p, OFF_SENS);
            if (weapScript != null
                && Marshal.ReadByte(weapScript.Pointer, OFF_AIMED) != 0
                && weapScript.name != "Camera")
                cur = ReadF(p, OFF_AIM_SENS);

            WriteF(p, OFF_CUR_SENS, cur);
            WriteF(p, OFF_SENS_X, ReadF(p, OFF_MULT_X) * cur);
            WriteF(p, OFF_SENS_Y, ReadF(p, OFF_MULT_Y) * cur);

            return false; // skip the original Update
        }
    }

    public class BetterFlyCam(IntPtr ptr) : MonoBehaviour(ptr)
    {
        #region Variables
        public Vector3 RotationOffset = new(-90, 90);
        public float UpOffset = 0.3f;
        public float ForwardOffset = 0;
        Transform Target;
        List<Transform> CurrentHead = new();
        Transform[] T;
        FlyCamController flyCam;
        List<Transform> Options = new();
        Vector2 scrollCreate = Vector2.zero;
        FPSMouseLook mouseLook;
        bool IsWatching = false;
        bool Open = false;
        bool UseBackup = false;
        int Backup = 0;
        bool hidden = false;
        bool IncludeFlyCam = true;
        bool IncludeEnemy = false;
        #endregion

        void Start()
        {
            mouseLook = GetComponent<FPSMouseLook>();
            flyCam = GetComponent<FlyCamController>();

            if (TryGetComponent<NetworkObject>(out var a) && !a.HasAuthority)
                Destroy(this);

            MelonCoroutines.Start(ScanForTargets());
        }

        void Update()
        {
            #region Make keys do smth
            if (Input.GetKeyDown(KeyCode.P) && Target != null)
                IsWatching = !IsWatching;
            if (Input.GetKeyDown(KeyCode.O))
                Open = !Open;

            #region Scrolling through targets
            Vector2 Mouse = Input.mouseScrollDelta;

            if (Mouse.y > 0 && Target != null && Options.IndexOf(Target) < Options.Count)   // scroll up
            {
                Target = Options[Options.IndexOf(Target) - 1];
                ClearHeads();
            }
            else if (Mouse.y > 0 && Target == null && Backup == 0)
            {
                Target = Options[0];
                ClearHeads();
            }

            if (Mouse.y < 0 && Target != null && Options.IndexOf(Target) >= 0)              // scroll down
            {
                Target = Options[Options.IndexOf(Target) + 1];
                ClearHeads();
            }
            else if (Mouse.y < 0 && Target == null && Backup == 0)
            {
                Target = Options[0];
                ClearHeads();
            }
            #endregion

            #endregion

            #region Main logic
            if (IsWatching)
            {
                if (Target != null)
                {
                    mouseLook.enabled = false;
                    flyCam.enabled = false;
                    transform.rotation = Target.root != Target || !Target.root.name.Contains("(Clone)") ? Quaternion.Euler(new(0, Target.eulerAngles.y, Target.eulerAngles.z)) * Quaternion.Euler(RotationOffset) : Target.rotation;
                    transform.position = Target.position
                        + transform.up * (Target.root != Target ? UpOffset : 0)
                        + transform.forward * (Target.root != Target ? ForwardOffset : 0);

                    if (!hidden)
                    {
                        hidden = true;
                        T = FindObjectsByType<Transform>(FindObjectsSortMode.None);

                        foreach (Transform t in T)
                        {
                            if (t.name.Contains("tubby_head") && t.parent == Target)
                            {
                                CurrentHead.Add(t);
                                t.gameObject.SetActive(false);
                            }
                        }
                    }

                }

                if (Target != null && Options.Contains(Target))
                    Backup = Options.IndexOf(Target);
                else if (!UseBackup)
                {
                    Target = Options[Backup + (Options.Count >= Backup ? 1 : 0)];
                    ClearHeads();
                }

                if (Options.Count < Backup && !UseBackup)
                {
                    Backup = Options.Count - 1;
                    Target = Options[Backup];
                    ClearHeads();
                }
            }
            else
            {
                mouseLook.enabled = true;
                flyCam.enabled = true;
                ClearHeads();
            }
            #endregion            
        }

        void OnGUI()
        {
            if (Open)
            {
                GUILayout.BeginVertical();
                GUILayout.Label("<b>Controls: </b>\nO - Toggle menu;\nP - Toggle POV through target;\nMouse Scroll - Navigate through targets");

                GUILayout.Label("<b>Settings</b>");
                if (GUILayout.Button("Watch through player POV: " + (!IsWatching ? "Disabled" : "Enabled")))
                    IsWatching = !IsWatching;
                IncludeFlyCam = GUILayout.Toggle(IncludeFlyCam, "Include other flycams in targets list");
                IncludeEnemy = GUILayout.Toggle(IncludeEnemy, "Include enemies in targets list (Experimental)");
                UseBackup = GUILayout.Toggle(UseBackup, "Stay on target index even if doesnt exist on certain index in targets list");
                GUILayout.Space(25);

                GUILayout.Label("<b>Targets: </b>");
                scrollCreate = GUILayout.BeginScrollView(scrollCreate, "box", GUILayout.Width(400), GUILayout.Height(400));
                foreach (Transform t in Options)
                {
                    if (t == Target && IsWatching)  // green when selected and watching through pov
                        GUI.color = Color.green;
                    else if (t == Target)           // red when only selected
                        GUI.color = Color.red;
                    else
                        GUI.color = Color.white;

                    if (GUILayout.Button(t.root.gameObject.name, GUILayout.Width(200)))
                    {
                        Target = t;
                        ClearHeads();
                    }
                    GUI.color = Color.white;
                }
                GUILayout.EndScrollView();
                
                GUILayout.EndVertical();
            }
        }

        void ClearHeads()
        {
            try
            {
                foreach (Transform t in CurrentHead)
                {
                    t?.gameObject.SetActive(true);
                }
            }
            finally
            {
                CurrentHead.Clear();
                hidden = false;
            }
        }

        IEnumerator ScanForTargets()
        {
            WaitForSeconds wait = new(0.25f); // wait so pc wont go kaboom

            while (true)
            {
                T = FindObjectsByType<Transform>(FindObjectsSortMode.None);

                Options.Clear(); // clear the list
                
                foreach (Transform t in T)
                {
                    // if we get the specific head bone found in the player
                    if (t.name == "Bip001 Head" && !t.TryGetComponent<Camera>(out _) && !Options.Contains(t))
                        Options.Add(t);
                    // if we get another FlyCam
                    else if (IncludeFlyCam && t.TryGetComponent<FlyCamController>(out _) && !t.TryGetComponent<Camera>(out _) && !Options.Contains(t))
                        Options.Add(t);
                    // last if its a boss enemy (AI or player controlled)
                    else if (IncludeEnemy && t.root.TryGetComponent<BossBot>(out _) && t.root.name.Contains("(Clone)") && !Options.Contains(t))
                        Options.Add(t);
                }

                yield return wait;
            }
        }
    }
}
