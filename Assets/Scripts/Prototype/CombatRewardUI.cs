using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Nevergreen.Prototype
{
    /// <summary>
    /// Displays battle rewards to the player before allowing them to proceed.
    /// </summary>
    public class CombatRewardUI : MonoBehaviour
    {
        [Header("UI Elements")]
        public GameObject panel;
        public TextMeshProUGUI rewardText;
        public Button closeButton;

        private System.Action _onClosedCallback;

        /// <summary>
        /// Shows the reward popup and waits for the player to close it.
        /// </summary>
        /// <param name="parts">Amount of Parts rewarded.</param>
        /// <param name="scraps">Amount of Scraps rewarded.</param>
        /// <param name="onClosed">Callback invoked when the player closes the popup.</param>
        public void ShowReward(int parts, int scraps, System.Action onClosed)
        {
            _onClosedCallback = onClosed;
            
            if (panel != null) 
            {
                panel.SetActive(true);
            }
            
            if (rewardText != null)
            {
                rewardText.text = $"You found {parts} Parts!";
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        private void OnCloseClicked()
        {
            if (panel != null) 
            {
                panel.SetActive(false);
            }
            
            _onClosedCallback?.Invoke();
        }
    }
}
