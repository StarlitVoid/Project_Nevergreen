using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nevergreen.Combat;
using Nevergreen.Prototype;

namespace Nevergreen.Tests
{
    [TestFixture]
    public class CombatUITests
    {
        private GameObject go;
        private CombatUI combatUI;
        private GameObject battleEndPanel;
        private GameObject nextRoomButtonGo;
        private Button nextRoomButton;

        [SetUp]
        public void Setup()
        {
            go = new GameObject("CombatUI");
            combatUI = go.AddComponent<CombatUI>();

            battleEndPanel = new GameObject("BattleEndPanel");
            combatUI.battleEndPanel = battleEndPanel;

            nextRoomButtonGo = new GameObject("NextRoomButton");
            nextRoomButton = nextRoomButtonGo.AddComponent<Button>();
            combatUI.nextRoomButton = nextRoomButton;
            
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(nextRoomButtonGo.transform, false);
            textGo.AddComponent<TextMeshProUGUI>();
        }

        [TearDown]
        public void Teardown()
        {
            if (battleEndPanel != null) Object.DestroyImmediate(battleEndPanel);
            if (nextRoomButtonGo != null) Object.DestroyImmediate(nextRoomButtonGo);
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void HandleBattleEnded_ShowsNextRoomButton_OnVictory()
        {
            // Initially, nextRoomButton should be active or inactive depending on state
            nextRoomButton.gameObject.SetActive(false);

            // Call HandleBattleEnded(Victory)
            var handleEndedMethod = typeof(CombatUI).GetMethod("HandleBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            handleEndedMethod.Invoke(combatUI, new object[] { BattleOutcome.Victory });

            // Button should be active
            Assert.IsTrue(nextRoomButton.gameObject.activeSelf, "Next Room button should be active on Victory.");
        }

        [Test]
        public void HandleBattleEnded_ShowsMainMenuButton_OnDefeat()
        {
            nextRoomButton.gameObject.SetActive(false);

            // Call HandleBattleEnded(Defeat)
            var handleEndedMethod = typeof(CombatUI).GetMethod("HandleBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            handleEndedMethod.Invoke(combatUI, new object[] { BattleOutcome.Defeat });

            // Button should be active
            Assert.IsTrue(nextRoomButton.gameObject.activeSelf, "Next Room button should be active on Defeat.");
            
            // Text should be Back to Main Menu
            var textComp = nextRoomButton.GetComponentInChildren<TextMeshProUGUI>();
            Assert.AreEqual("Back to Main Menu", textComp.text);
        }

        [Test]
        public void Initialize_HidesNextRoomButton_AndRegistersListener()
        {
            // We need a dummy BattleSystem to satisfy Initialize parameter, or pass null if safe
            // Let's create a dummy BattleSystem
            var bsGo = new GameObject("BattleSystem");
            var bs = bsGo.AddComponent<BattleSystem>();

            var playerTeam = new List<CombatCharacter>();
            var enemyTeam = new List<CombatCharacter>();

            // Setup nextRoomButton as active initially
            nextRoomButton.gameObject.SetActive(true);

            // Run Initialize
            combatUI.Initialize(bs, playerTeam, enemyTeam);

            // Verify button is deactivated during initialization
            Assert.IsFalse(nextRoomButton.gameObject.activeSelf, "Next Room button should be deactivated on Initialization.");

            // Cleanup
            Object.DestroyImmediate(bsGo);
        }

        [Test]
        public void CombatRewardUI_ShowReward_DisplaysPartsOnly()
        {
            var rewardGo = new GameObject("RewardUI");
            var rewardUI = rewardGo.AddComponent<CombatRewardUI>();
            var textGo = new GameObject("RewardText");
            var text = textGo.AddComponent<TextMeshProUGUI>();
            rewardUI.rewardText = text;

            rewardUI.ShowReward(10, 5, null);

            Assert.AreEqual("You found 10 Parts!", text.text, "Reward text should only display Parts and ignore Scraps.");
            
            Object.DestroyImmediate(rewardGo);
        }
    }
}
