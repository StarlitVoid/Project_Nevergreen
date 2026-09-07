  #if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using Nevergreen;
using Nevergreen.Data;

namespace Nevergreen.Prototype
{
    /// <summary>
    /// Debug utility for the Unity Editor.
    /// Pressing 'R' immediately ends the current run and returns to the main menu.
    /// </summary>
    public static class EditorDebugUtility
    {
        private static readonly string[] MarionettePaths = new string[]
        {
            "Assets/Data/Characters/Marionettes/CD_Alchemist.asset",   //1
            "Assets/Data/Characters/Marionettes/CD_Assassin.asset",    //2
            "Assets/Data/Characters/Marionettes/CD_Butler.asset",      //3
            "Assets/Data/Characters/Marionettes/CD_Violinist.asset",   //4
            "Assets/Data/Characters/Marionettes/CD_Commander.asset",   //5
            "Assets/Data/Characters/Marionettes/CD_Enforcer.asset",    //6
            "Assets/Data/Characters/Marionettes/CD_Knight.asset",      //7
            "Assets/Data/Characters/Marionettes/CD_Maid.asset",        //8
            "Assets/Data/Characters/Marionettes/CD_Princess.asset",    //9
            "Assets/Data/Characters/Marionettes/CD_Sharpshooter.asset" //0
        };

        private class DebugBehaviour : MonoBehaviour
        {
            private void Update()
            {
                if (Keyboard.current == null) return;

                if (Keyboard.current.rKey.wasPressedThisFrame)
                {
                    // Check if we are not already in MainMenu to avoid redundant transitions
                    if (SceneManager.GetActiveScene().name == "MainMenu")
                    {
                        return;
                    }

                    Debug.Log("[EditorDebugUtility] 'R' pressed. Immediately ending the run and returning to Main Menu...");
                    
                    // Clear the active run save state (deletes/resets save file)
                    SaveManager.ClearActiveRun();

                    // Clear the static run session data
                    RunSessionManager.Clear();

                    // Return to Main Menu
                    SceneManager.LoadScene("MainMenu");
                }

                if (Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    var battleSystem = Object.FindAnyObjectByType<Nevergreen.Combat.BattleSystem>();
                    if (battleSystem != null && battleSystem.CurrentState != Nevergreen.Combat.BattleState.Inactive && battleSystem.CurrentState != Nevergreen.Combat.BattleState.BattleEnd)
                    {
                        Debug.Log("[EditorDebugUtility] Spacebar pressed. Dealing 999999 damage to all active enemies...");
                        var enemies = new System.Collections.Generic.List<Nevergreen.Combat.CombatCharacter>(battleSystem.EnemyTeam);
                        foreach (var enemy in enemies)
                        {
                            if (enemy != null && enemy.IsAlive)
                            {
                                enemy.TakeDamage(999999);
                            }
                        }
                    }
                }

                if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                {
                    RunSessionManager.GrantParts(100);
                    RunSessionManager.GrantScraps(100);
                    Debug.Log($"[EditorDebugUtility] Up Arrow pressed. Granted 100 Parts. Total Parts: {RunSessionManager.Parts}");
                    Debug.Log($"[EditorDebugUtility] Up Arrow pressed. Granted 100 Scraps. Total Scraps: {RunSessionManager.Scraps}");

                }

                if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                {
                    RunSessionManager.RoomProgression += 1;
                    Debug.Log($"[EditorDebugUtility] Right Arrow pressed. Increased Room Progression by 1. New Room Progression: {RunSessionManager.RoomProgression}");
                }

                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                {
                    RunSessionManager.RoomProgression = Mathf.Max(0, RunSessionManager.RoomProgression - 1);
                    Debug.Log($"[EditorDebugUtility] Left Arrow pressed. Decreased Room Progression by 1. New Room Progression: {RunSessionManager.RoomProgression}");
                }

                if (Keyboard.current.hKey.wasPressedThisFrame)
                {
                    Debug.Log("[EditorDebugUtility] 'H' pressed. Instantly healing all player marionettes for 999999 HP...");
                    
                    var battleSystem = Object.FindAnyObjectByType<Nevergreen.Combat.BattleSystem>();
                    if (battleSystem != null && battleSystem.CurrentState != Nevergreen.Combat.BattleState.Inactive && battleSystem.CurrentState != Nevergreen.Combat.BattleState.BattleEnd)
                    {
                        var players = new System.Collections.Generic.List<Nevergreen.Combat.CombatCharacter>(battleSystem.PlayerTeam);
                        foreach (var player in players)
                        {
                            if (player != null)
                            {
                                if (!player.IsAlive)
                                {
                                    player.state = Nevergreen.Combat.LifeState.Alive;
                                }
                                player.Heal(999999);
                            }
                        }
                    }
                    else
                    {
                        if (RunSessionManager.CurrentParty != null)
                        {
                            foreach (var member in RunSessionManager.CurrentParty)
                            {
                                if (member != null && member.character != null)
                                {
                                    var stats = member.character.GetStatsForLevel(member.currentLevel);
                                    if (stats != null)
                                    {
                                        member.currentHP = stats.maxHP;
                                    }
                                }
                            }
                            SaveManager.SaveRun();
                        }
                    }
                }

                if (Keyboard.current.tKey.wasPressedThisFrame)
                {
                    Debug.Log("[EditorDebugUtility] 'T' pressed. Spawning Test_Trinket_Dropper and TrinketUIItemTest...");
                    
                    var dropperPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Test_Trinket_Dropper.prefab");
                    var trinketPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TrinketUIItemTest.prefab");
                    var trinketData = UnityEditor.AssetDatabase.LoadAssetAtPath<Nevergreen.Data.TrinketData>("Assets/Data/Trinket/TD_fine_blade.asset");

                    if (dropperPrefab != null && trinketPrefab != null && trinketData != null)
                    {
                        Canvas targetCanvas = null;
                        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                        foreach (var c in canvases)
                        {
                            if (c.name == "UICanvas")
                            {
                                targetCanvas = c;
                                break;
                            }
                        }
                        if (targetCanvas == null && canvases.Length > 0)
                        {
                            targetCanvas = canvases[0];
                        }

                        GameObject dropper = targetCanvas != null ? Object.Instantiate(dropperPrefab, targetCanvas.transform) : Object.Instantiate(dropperPrefab);
                        
                        GameObject trinketObj = Object.Instantiate(trinketPrefab, dropper.transform);
                        var uiItem = trinketObj.GetComponent<Nevergreen.UI.TrinketUIItem>();
                        if (uiItem != null)
                        {
                            uiItem.Initialize(trinketData, null, -1);
                        }
                        
                        Debug.Log("[EditorDebugUtility] Successfully spawned test dropper and trinket.");
                    }
                    else
                    {
                        Debug.LogError("[EditorDebugUtility] Failed to load one or more assets for the 'T' hotkey.");
                    }
                }

                if (Keyboard.current.mKey.wasPressedThisFrame)
                {
                    var audioManager = Nevergreen.Audio.AudioManager.Instance;
                    if (audioManager != null)
                    {
                        AudioSource bgmSource = null;
                        var field = typeof(Nevergreen.Audio.AudioManager).GetField("_bgmSourceMain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            bgmSource = field.GetValue(audioManager) as AudioSource;
                        }

                        if (bgmSource == null)
                        {
                            var sources = audioManager.GetComponents<AudioSource>();
                            foreach (var src in sources)
                            {
                                if (src != null && src.isPlaying && src.clip != null)
                                {
                                    bgmSource = src;
                                    break;
                                }
                            }
                        }

                        if (bgmSource != null && bgmSource.clip != null && bgmSource.isPlaying)
                        {
                            float targetTime = Mathf.Max(0f, bgmSource.clip.length - 3f);
                            bgmSource.time = targetTime;
                            Debug.Log($"[EditorDebugUtility] 'M' pressed. Skipped BGM '{bgmSource.clip.name}' to {targetTime:F2}s (last 3s).");
                        }
                        else
                        {
                            Debug.Log("[EditorDebugUtility] 'M' pressed. No BGM is currently playing.");
                        }
                    }
                }

                int digitIndex = -1;
                if (Keyboard.current.digit1Key.wasPressedThisFrame) digitIndex = 0;
                else if (Keyboard.current.digit2Key.wasPressedThisFrame) digitIndex = 1;
                else if (Keyboard.current.digit3Key.wasPressedThisFrame) digitIndex = 2;
                else if (Keyboard.current.digit4Key.wasPressedThisFrame) digitIndex = 3;
                else if (Keyboard.current.digit5Key.wasPressedThisFrame) digitIndex = 4;
                else if (Keyboard.current.digit6Key.wasPressedThisFrame) digitIndex = 5;
                else if (Keyboard.current.digit7Key.wasPressedThisFrame) digitIndex = 6;
                else if (Keyboard.current.digit8Key.wasPressedThisFrame) digitIndex = 7;
                else if (Keyboard.current.digit9Key.wasPressedThisFrame) digitIndex = 8;
                else if (Keyboard.current.digit0Key.wasPressedThisFrame) digitIndex = 9;

                if (digitIndex != -1)
                {
                    var template = UnityEditor.AssetDatabase.LoadAssetAtPath<Nevergreen.Data.CharacterData>(MarionettePaths[digitIndex]);
                    if (template != null)
                    {
                        var gameDb = Nevergreen.Data.GameDatabase.Instance;
                        var traitDb = gameDb != null ? gameDb.TraitDatabase : null;
                        var member = Nevergreen.Data.MarionetteGenerator.GenerateMarionetteFromTemplate(template, traitDb);
                        
                        if (member != null)
                        {
                            if (RunSessionManager.CurrentParty == null)
                            {
                                RunSessionManager.CurrentParty = new System.Collections.Generic.List<PartyMemberInfo>();
                            }
                            RunSessionManager.CurrentParty.Add(member);
                            SaveManager.SaveRun();
                            Debug.Log($"[EditorDebugUtility] Added generated {template.displayName} to RunSessionManager.CurrentParty.");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[EditorDebugUtility] Failed to load CharacterData template at {MarionettePaths[digitIndex]}");
                    }
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            var go = new GameObject("EditorDebugUtility_Helper");
            go.AddComponent<DebugBehaviour>();
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
        }
    }
}
#endif
