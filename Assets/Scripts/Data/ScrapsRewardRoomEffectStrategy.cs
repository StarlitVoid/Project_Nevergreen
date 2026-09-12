using System;
using UnityEngine;
using Nevergreen.UI;

namespace Nevergreen.Data
{
    /// <summary>
    /// Room effect strategy that grants Scraps to the player.
    /// Typically used in a Scraps/Salvage room with OnCombatVictory activation type.
    /// Displays a dedicated Scraps Reward popup UI.
    /// </summary>
    [Serializable]
    public class ScrapsRewardRoomEffectStrategy : RoomEffectStrategy
    {
        [Tooltip("Minimum amount of Scraps awarded.")]
        [SerializeField] private int minScraps = 10;
        
        [Tooltip("Maximum amount of Scraps awarded.")]
        [SerializeField] private int maxScraps = 25;

        [Tooltip("Prefab for the dedicated Scraps Reward popup UI. Must contain ScrapsRewardUIController.")]
        [SerializeField] private GameObject scrapsRewardUiPrefab;

        public override void ExecuteRoomEffect()
        {
            var combatUI = UnityEngine.Object.FindFirstObjectByType<Nevergreen.Prototype.CombatUI>();
            if (combatUI == null)
            {
                Debug.LogWarning("[ScrapsRewardRoomEffectStrategy] Could not find CombatUI. Executing silently.");
                GrantScrapsAndComplete(GetRandomScrapsAmount(), null);
                return;
            }

            if (scrapsRewardUiPrefab == null)
            {
                Debug.LogError("[ScrapsRewardRoomEffectStrategy] scrapsRewardUiPrefab is not assigned! Executing silently.");
                GrantScrapsAndComplete(GetRandomScrapsAmount(), combatUI);
                return;
            }

            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            Canvas canvas = null;

            // 1. Prefer "UICanvas"
            foreach (var c in canvases)
            {
                if (c.name == "UICanvas" && (c.renderMode == RenderMode.ScreenSpaceOverlay || c.renderMode == RenderMode.ScreenSpaceCamera))
                {
                    canvas = c;
                    break;
                }
            }

            // 2. Fallback to any Screen-Space canvas if "UICanvas" is missing
            if (canvas == null)
            {
                foreach (var c in canvases)
                {
                    if (c.renderMode == RenderMode.ScreenSpaceOverlay || c.renderMode == RenderMode.ScreenSpaceCamera)
                    {
                        canvas = c;
                        break;
                    }
                }
            }

            if (canvas == null)
            {
                Debug.LogError("[ScrapsRewardRoomEffectStrategy] No Screen-Space Canvas found in the scene! Executing silently.");
                GrantScrapsAndComplete(GetRandomScrapsAmount(), combatUI);
                return;
            }

            // Calculate the scraps amount
            int amount = GetRandomScrapsAmount();

            // Instantiate and initialize the dedicated popup
            var uiInstance = UnityEngine.Object.Instantiate(scrapsRewardUiPrefab, canvas.transform);
            RectTransform rt = uiInstance.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            var controller = uiInstance.GetComponent<ScrapsRewardUIController>();

            if (controller != null)
            {
                controller.Initialize(amount, () => 
                {
                    GrantScrapsAndComplete(amount, combatUI);
                });
            }
            else
            {
                Debug.LogError("[ScrapsRewardRoomEffectStrategy] Instantiated prefab does not have ScrapsRewardUIController! Executing silently.");
                UnityEngine.Object.Destroy(uiInstance);
                GrantScrapsAndComplete(amount, combatUI);
            }
        }

        private int GetRandomScrapsAmount()
        {
            var rng = new System.Random();
            return rng.Next(minScraps, maxScraps + 1);
        }

        private void GrantScrapsAndComplete(int amount, Nevergreen.Prototype.CombatUI combatUI)
        {
            RunSessionManager.GrantScraps(amount);
            Debug.Log($"[ScrapsRewardRoomEffectStrategy] Awarded {amount} Scraps. Total: {RunSessionManager.Scraps}");

            if (combatUI != null)
            {
                combatUI.ShowRoomSelectionImmediately();
            }
            else
            {
                if (!RunSessionManager.RoomCompleted)
                {
                    RunSessionManager.CompleteRoom(new System.Collections.Generic.List<RoomData>());
                }
            }
        }
    }
}
