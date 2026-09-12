using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Nevergreen.UI
{
    /// <summary>
    /// Controller for the dedicated Scraps Reward popup UI.
    /// </summary>
    public class ScrapsRewardUIController : MonoBehaviour
    {
        [Header("UI Elements")]
        public GameObject panel;
        public TextMeshProUGUI rewardText;
        public Button claimButton;

        private System.Action _onClaimCallback;

        /// <summary>
        /// Initializes and shows the Scraps reward popup.
        /// </summary>
        /// <param name="scrapsAmount">Amount of Scraps rewarded.</param>
        /// <param name="onClaim">Callback invoked when the player claims the reward.</param>
        public void Initialize(int scrapsAmount, System.Action onClaim)
        {
            _onClaimCallback = onClaim;
            
            if (panel != null) 
            {
                panel.SetActive(true);
            }
            
            if (rewardText != null)
            {
                rewardText.text = $"You found {scrapsAmount} Scraps!";
            }

            if (claimButton != null)
            {
                claimButton.onClick.RemoveAllListeners();
                claimButton.onClick.AddListener(OnClaimClicked);
            }
        }

        private void OnClaimClicked()
        {
            if (panel != null) 
            {
                panel.SetActive(false);
            }
            
            _onClaimCallback?.Invoke();
            
            // Destroy this UI instance after claiming
            Destroy(gameObject);
        }
    }
}
