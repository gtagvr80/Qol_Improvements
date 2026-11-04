#region Usings
using UnityEngine;
using MelonLoader;
using Il2CppInterop.Runtime.Injection;
using Il2Cpp;
using System.Collections.Generic;
using System.Collections;
using System;
using HarmonyLib;
#endregion

[assembly: MelonInfo(typeof(QOL_Improvements.Mod), "QOL Improvements", "1.0", "Krum20")]
[assembly: MelonGame("ZeoWorks", "Slendytubbies 3")]
namespace QOL_Improvements
{
    public class Mod : MelonMod
    {
        #region Methods
        public override void OnInitializeMelon() => ClassInjector.RegisterTypeInIl2Cpp<BetterFlyCam>();
        #endregion
    }

    [HarmonyPatch(typeof(FlyCamController), "Start")]
    public class FlyCamPatch
    {
        static void Postfix(FlyCamController __instance)
        {
            string GM = UnityEngine.Object.FindFirstObjectByType<RoomMultiplayerMenu>().KCIGKBNBPNN; // It references an obfuscated variable so it needs its reference to be updated on every update of the game

            if (GM == "THR" && __instance.TryGetComponent<Camera>(out _) && !__instance.TryGetComponent<BetterFlyCam>(out _))
                __instance.gameObject.AddComponent<BetterFlyCam>();
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
        #endregion

        void Start()
        {
            mouseLook = GetComponent<FPSMouseLook>();
            flyCam = GetComponent<FlyCamController>();

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
            else
            {
                mouseLook.enabled = true;
                flyCam.enabled = true;
                ClearHeads();
            }
            #endregion            

            #region Checks for pointing to a null transform
            if (IsWatching)
            {
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
                UseBackup = GUILayout.Toggle(UseBackup, "Stay on target index even if doesnt exist on certain index in targets list");
                GUILayout.Space(25);

                GUILayout.Label("<b>Targets: </b>");
                scrollCreate = GUILayout.BeginScrollView(scrollCreate, "box", GUILayout.Width(300), GUILayout.Height(400));
                foreach (Transform t in Options)
                {
                    if (t == Target && IsWatching)  // green when selected and watching through pov
                        GUI.color = Color.green;
                    else if (t == Target)           // red when only selected
                        GUI.color = Color.red;
                    else
                        GUI.color = Color.white;

                    if (GUILayout.Button(t.root.gameObject.name))
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
                    if (t != null)
                        t.gameObject.SetActive(true);
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
                    else if (t.TryGetComponent<FlyCamController>(out _) && !t.TryGetComponent<Camera>(out _) && !Options.Contains(t))
                        Options.Add(t);
                    // last if its a boss enemy (AI or player controlled)
                    else if (t.root.TryGetComponent<BossBot>(out _) && t.root.name.Contains("(Clone)") && !Options.Contains(t))
                        Options.Add(t);
                }

                yield return wait;
            }
        }
    }
}
