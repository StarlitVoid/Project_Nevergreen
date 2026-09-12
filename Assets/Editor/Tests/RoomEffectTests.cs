using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Nevergreen.Combat;
using Nevergreen.Data;

namespace Nevergreen.Tests
{
    [TestFixture]
    public class RoomEffectTests
    {
        // --- Test double for RoomEffectStrategy ---
        [Serializable]
        private class TestRoomEffectStrategy : RoomEffectStrategy
        {
            public static int ExecutionCount;
            public override void ExecuteRoomEffect()
            {
                ExecutionCount++;
            }
        }

        private string _testRunPath; private string _testProfilePath;

        [SetUp]
        public void SetUp()
        {
            RunSessionManager.Clear();
            RunSessionManager.IsResumingRun = false;
            TestRoomEffectStrategy.ExecutionCount = 0;

            // Redirect save operations to a temp file so tests never touch production save.dat
            _testRunPath = Path.Combine(Application.temporaryCachePath, "room_effect_test_run.dat"); _testProfilePath = Path.Combine(Application.temporaryCachePath, "room_effect_test_profile.dat");
            SaveManager.SetSavePathsForTesting(_testRunPath, _testProfilePath);
        }

        [TearDown]
        public void TearDown()
        {
            RunSessionManager.Clear();
            RunSessionManager.IsResumingRun = false;

            if (!string.IsNullOrEmpty(_testRunPath) && File.Exists(_testRunPath)) File.Delete(_testRunPath); if (!string.IsNullOrEmpty(_testProfilePath) && File.Exists(_testProfilePath))
            {
                File.Delete(_testProfilePath);
            }
            SaveManager.SetSavePathsForTesting(null, null);
        }

        // ============================================================
        // RoomData & Strategy Tests
        // ============================================================

        [Test]
        public void RoomData_ActivateEffect_InvokesStrategy()
        {
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.roomName = "Test Room";
            roomData.strategy = new TestRoomEffectStrategy();

            roomData.ActivateEffect();

            Assert.AreEqual(1, TestRoomEffectStrategy.ExecutionCount);
            UnityEngine.Object.DestroyImmediate(roomData);
        }

        [Test]
        public void RoomData_ActivateEffect_NullStrategy_DoesNotThrow()
        {
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.roomName = "Empty Room";
            roomData.strategy = null;

            Assert.DoesNotThrow(() => roomData.ActivateEffect());
            UnityEngine.Object.DestroyImmediate(roomData);
        }

        // ============================================================
        // RunSessionManager NextRoomData Tests
        // ============================================================

        [Test]
        public void RunSessionManager_NextRoomData_DefaultIsNull()
        {
            Assert.IsNull(RunSessionManager.NextRoomData);
        }

        [Test]
        public void RunSessionManager_NextRoomData_SetAndGet()
        {
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.roomName = "Test";

            RunSessionManager.NextRoomData = roomData;
            Assert.AreSame(roomData, RunSessionManager.NextRoomData);

            UnityEngine.Object.DestroyImmediate(roomData);
        }

        [Test]
        public void RunSessionManager_Clear_ResetsNextRoomData()
        {
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            RunSessionManager.NextRoomData = roomData;

            RunSessionManager.Clear();

            Assert.IsNull(RunSessionManager.NextRoomData);
            UnityEngine.Object.DestroyImmediate(roomData);
        }

        [Test]
        public void RunSessionManager_ActivateCurrentRoomEffect_InvokesStrategy()
        {
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.strategy = new TestRoomEffectStrategy();
            RunSessionManager.NextRoomData = roomData;

            RunSessionManager.ActivateCurrentRoomEffect();

            Assert.AreEqual(1, TestRoomEffectStrategy.ExecutionCount);
            UnityEngine.Object.DestroyImmediate(roomData);
        }

        [Test]
        public void RunSessionManager_ActivateCurrentRoomEffect_NullNextRoom_DoesNotThrow()
        {
            RunSessionManager.NextRoomData = null;
            Assert.DoesNotThrow(() => RunSessionManager.ActivateCurrentRoomEffect());
        }

        // ============================================================
        // Victory Subscription Tests
        // ============================================================

        [Test]
        public void SubscribeToBattle_Victory_ActivatesOnCombatVictoryRoom()
        {
            // Arrange
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.roomName = "Victory Room";
            roomData.activationType = RoomActivationType.OnCombatVictory;
            roomData.strategy = new TestRoomEffectStrategy();

            RunSessionManager.NextRoomData = roomData;
            RunSessionManager.SubscribeToBattle(battleSystem);

            // Act - fire OnBattleEnded(Victory) via reflection
            var onBattleEnded = typeof(BattleSystem)
                .GetField("OnBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = onBattleEnded.GetValue(battleSystem) as Action<BattleOutcome>;
            del?.Invoke(BattleOutcome.Victory);

            // Assert - Should NOT trigger immediately (deferred)
            Assert.AreEqual(0, TestRoomEffectStrategy.ExecutionCount,
                "Strategy should NOT be executed immediately on Victory (deferred to UI).");
            Assert.IsNotNull(RunSessionManager.NextRoomData,
                "NextRoomData should not be cleared yet.");

            // Act - Trigger manually
            RunSessionManager.TriggerPendingVictoryRoomEffect();

            // Assert - Should trigger now
            Assert.AreEqual(1, TestRoomEffectStrategy.ExecutionCount,
                "Strategy should have been executed after trigger.");
            Assert.IsNull(RunSessionManager.NextRoomData,
                "NextRoomData should be cleared after victory activation.");

            UnityEngine.Object.DestroyImmediate(roomData);
            UnityEngine.Object.DestroyImmediate(battleGO);
        }

        [Test]
        public void SubscribeToBattle_Defeat_DoesNotActivateRoom()
        {
            // Arrange
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.roomName = "Victory Room";
            roomData.activationType = RoomActivationType.OnCombatVictory;
            roomData.strategy = new TestRoomEffectStrategy();

            RunSessionManager.NextRoomData = roomData;
            RunSessionManager.SubscribeToBattle(battleSystem);

            // Act - fire Defeat
            var onBattleEnded = typeof(BattleSystem)
                .GetField("OnBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = onBattleEnded.GetValue(battleSystem) as Action<BattleOutcome>;
            del?.Invoke(BattleOutcome.Defeat);

            // Assert
            Assert.AreEqual(0, TestRoomEffectStrategy.ExecutionCount,
                "Strategy should NOT execute on Defeat.");
            Assert.IsNull(RunSessionManager.NextRoomData,
                "NextRoomData should be cleared on Defeat because the run is wiped.");

            UnityEngine.Object.DestroyImmediate(roomData);
            UnityEngine.Object.DestroyImmediate(battleGO);
        }

        [Test]
        public void SubscribeToBattle_Victory_OnRoomLoadedType_DoesNotActivate()
        {
            // Arrange - room type is OnRoomLoaded, not OnCombatVictory
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.activationType = RoomActivationType.OnRoomLoaded;
            roomData.strategy = new TestRoomEffectStrategy();

            RunSessionManager.NextRoomData = roomData;
            RunSessionManager.SubscribeToBattle(battleSystem);

            // Act
            var onBattleEnded = typeof(BattleSystem)
                .GetField("OnBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = onBattleEnded.GetValue(battleSystem) as Action<BattleOutcome>;
            del?.Invoke(BattleOutcome.Victory);

            // Assert
            Assert.AreEqual(0, TestRoomEffectStrategy.ExecutionCount,
                "OnRoomLoaded strategy should NOT activate on Victory.");
            Assert.AreSame(roomData, RunSessionManager.NextRoomData,
                "NextRoomData should remain set (wrong activation type).");

            UnityEngine.Object.DestroyImmediate(roomData);
            UnityEngine.Object.DestroyImmediate(battleGO);
        }

        [Test]
        public void SubscribeToBattle_Unsubscribes_AfterBattleEnded()
        {
            // Arrange
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            RunSessionManager.SubscribeToBattle(battleSystem);

            // Act - fire event once
            var onBattleEnded = typeof(BattleSystem)
                .GetField("OnBattleEnded", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = onBattleEnded.GetValue(battleSystem) as Action<BattleOutcome>;
            del?.Invoke(BattleOutcome.Victory);

            // Now set a new room and fire again — should NOT execute since unsubscribed
            var roomData = ScriptableObject.CreateInstance<RoomData>();
            roomData.activationType = RoomActivationType.OnCombatVictory;
            roomData.strategy = new TestRoomEffectStrategy();
            RunSessionManager.NextRoomData = roomData;

            // Re-read the delegate — it should have been unsubscribed
            del = onBattleEnded.GetValue(battleSystem) as Action<BattleOutcome>;
            del?.Invoke(BattleOutcome.Victory);

            // Assert — strategy should NOT have fired from the second invocation
            // because RunSessionManager unsubscribed after the first event
            Assert.AreEqual(0, TestRoomEffectStrategy.ExecutionCount,
                "Strategy should not execute after unsubscription.");

            UnityEngine.Object.DestroyImmediate(roomData);
            UnityEngine.Object.DestroyImmediate(battleGO);
        }

        // ============================================================
        // Room Progression Tests
        // ============================================================

        [Test]
        public void RoomProgression_StartsAtZero()
        {
            Assert.AreEqual(0, RunSessionManager.RoomProgression);
        }

        [Test]
        public void RoomProgression_ClearResetsToZero()
        {
            RunSessionManager.RoomProgression = 5;
            RunSessionManager.Clear();
            Assert.AreEqual(0, RunSessionManager.RoomProgression);
        }

        [Test]
        public void RoomProgression_InitializeResetsToZero()
        {
            RunSessionManager.RoomProgression = 3;
            RunSessionManager.Initialize();
            Assert.AreEqual(0, RunSessionManager.RoomProgression);
        }

        [Test]
        public void Initialize_AssignsMarionetteRoomToNextRoomData()
        {
            // Arrange
            var roomDb = ScriptableObject.CreateInstance<RoomDatabase>();
            var marionetteRoom = ScriptableObject.CreateInstance<RoomData>();
            marionetteRoom.roomId = "RD_MarionetteRoom";
            marionetteRoom.roomName = "Marionette Room";
            roomDb.availableRooms.Add(new RoomPoolEntry { room = marionetteRoom });

            var mockGameDb = GameDatabase.CreateForTesting(rooms: roomDb);
            GameDatabase.SetInstanceForTesting(mockGameDb);

            // Act
            RunSessionManager.Initialize();

            // Assert
            Assert.AreSame(marionetteRoom, RunSessionManager.NextRoomData);

            // Cleanup
            GameDatabase.SetInstanceForTesting(null);
            UnityEngine.Object.DestroyImmediate(mockGameDb);
            UnityEngine.Object.DestroyImmediate(marionetteRoom);
            UnityEngine.Object.DestroyImmediate(roomDb);
        }

        [Test]
        public void RoomProgression_OnSceneLoaded_IncrementsWhenCombatSceneAndPartyExists()
        {
            RunSessionManager.CurrentParty.Add(new PartyMemberInfo());
            RunSessionManager.OnSceneLoaded("CombatPrototype");
            
            Assert.AreEqual(1, RunSessionManager.RoomProgression);
        }

        [Test]
        public void RoomProgression_OnSceneLoaded_DoesNotIncrementWhenNotCombatScene()
        {
            RunSessionManager.CurrentParty.Add(new PartyMemberInfo());
            RunSessionManager.OnSceneLoaded("MainMenu");
            
            Assert.AreEqual(0, RunSessionManager.RoomProgression);
        }

        [Test]
        public void RoomProgression_OnSceneLoaded_DoesNotIncrementWhenPartyEmpty()
        {
            // CurrentParty is empty by default after Setup/Clear
            RunSessionManager.OnSceneLoaded("CombatPrototype");
            
            Assert.AreEqual(0, RunSessionManager.RoomProgression);
        }

        [Test]
        public void RoomProgression_OnSceneLoaded_SkipsIncrementWhenIsResumingRun()
        {
            RunSessionManager.CurrentParty.Add(new PartyMemberInfo());
            RunSessionManager.RoomProgression = 3;
            RunSessionManager.IsResumingRun = true;

            RunSessionManager.OnSceneLoaded("CombatPrototype");

            Assert.AreEqual(3, RunSessionManager.RoomProgression, "RoomProgression should not increment when resuming.");
            Assert.IsFalse(RunSessionManager.IsResumingRun, "IsResumingRun should be reset to false after scene load.");
        }

        [Test]
        public void RoomProgression_OnSceneLoaded_DoesNotIncrementWhenNextRoomIsHealRoom()
        {
            RunSessionManager.CurrentParty.Add(new PartyMemberInfo());
            RunSessionManager.RoomProgression = 3;
            
            var healRoom = ScriptableObject.CreateInstance<RoomData>();
            healRoom.roomId = "RD_HealRoom";
            RunSessionManager.NextRoomData = healRoom;

            RunSessionManager.OnSceneLoaded("CombatPrototype");

            Assert.AreEqual(3, RunSessionManager.RoomProgression, "RoomProgression should not increment when entering a Heal Room.");
            UnityEngine.Object.DestroyImmediate(healRoom);
        }

        [Test]
        public void RoomProgression_ResumeInHealRoom_PreservesProgressionCount()
        {
            RunSessionManager.CurrentParty.Add(new PartyMemberInfo());
            RunSessionManager.RoomProgression = 7;
            
            var healRoom = ScriptableObject.CreateInstance<RoomData>();
            healRoom.roomId = "RD_HealRoom";
            RunSessionManager.NextRoomData = healRoom;

            // 1. Initial entry into Heal Room
            RunSessionManager.OnSceneLoaded("CombatPrototype");
            Assert.AreEqual(7, RunSessionManager.RoomProgression, "RoomProgression should stay at 7 upon entering Heal Room.");

            // 2. Simulate Quit and Resume
            RunSessionManager.IsResumingRun = true;
            RunSessionManager.NextRoomData = healRoom;
            RunSessionManager.OnSceneLoaded("CombatPrototype");

            Assert.AreEqual(7, RunSessionManager.RoomProgression, "RoomProgression should remain 7 upon resuming inside Heal Room.");
            UnityEngine.Object.DestroyImmediate(healRoom);
        }

        // ============================================================
        // CombatConfig Room Selection Tests
        // ============================================================

        [Test]
        public void GlobalConfig_RoomChoiceCount_DefaultIs3()
        {
            var config = ScriptableObject.CreateInstance<GlobalConfig>();
            Assert.AreEqual(3, config.roomChoiceCount);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void RoomDatabase_AvailableRooms_DefaultIsEmpty()
        {
            var roomDb = ScriptableObject.CreateInstance<RoomDatabase>();
            Assert.IsNotNull(roomDb.availableRooms);
            Assert.AreEqual(0, roomDb.availableRooms.Count);
            UnityEngine.Object.DestroyImmediate(roomDb);
        }
        // ============================================================
        // Team Formation Update Tests
        // ============================================================

        [Test]
        public void BattleVictory_UpdatesRosterFormationOrder()
        {
            // Arrange
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            var p1 = new PartyMemberInfo();
            var p2 = new PartyMemberInfo();
            var p3 = new PartyMemberInfo();

            RunSessionManager.CurrentParty.Add(p1);
            RunSessionManager.CurrentParty.Add(p2);
            RunSessionManager.CurrentParty.Add(p3);

            var c1GO = new GameObject("C1");
            var c1 = c1GO.AddComponent<CombatCharacter>();
            c1.partyInfo = p1;
            c1.rank = 3;
            c1.state = LifeState.Alive;

            var c2GO = new GameObject("C2");
            var c2 = c2GO.AddComponent<CombatCharacter>();
            c2.partyInfo = p2;
            c2.rank = 1;
            c2.state = LifeState.Alive;

            var c3GO = new GameObject("C3");
            var c3 = c3GO.AddComponent<CombatCharacter>();
            c3.partyInfo = p3;
            c3.rank = 2;
            c3.state = LifeState.Alive;

            var playerTeam = new List<CombatCharacter> { c1, c2, c3 };
            typeof(BattleSystem).GetField("_playerTeam", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(battleSystem, playerTeam);
            typeof(BattleSystem).GetField("_enemyTeam", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(battleSystem, new List<CombatCharacter>());

            // Act - invoke CheckBattleEnd
            var isEnd = battleSystem.CheckBattleEnd();

            // Assert
            Assert.IsTrue(isEnd, "Battle should end in victory since enemy team is empty.");
            Assert.AreEqual(3, RunSessionManager.CurrentParty.Count);
            
            // Expected order based on ranks: c2 (rank 1) -> c3 (rank 2) -> c1 (rank 3)
            // Which maps to: p2, p3, p1
            Assert.AreSame(p2, RunSessionManager.CurrentParty[0]);
            Assert.AreSame(p3, RunSessionManager.CurrentParty[1]);
            Assert.AreSame(p1, RunSessionManager.CurrentParty[2]);

            // Cleanup
            UnityEngine.Object.DestroyImmediate(battleGO);
            UnityEngine.Object.DestroyImmediate(c1GO);
            UnityEngine.Object.DestroyImmediate(c2GO);
            UnityEngine.Object.DestroyImmediate(c3GO);
        }

        [Test]
        public void BattleVictory_RemovesDeadAndPilesAndMaintainsFormation()
        {
            // Arrange
            var battleGO = new GameObject("BattleSystem");
            var battleSystem = battleGO.AddComponent<BattleSystem>();

            var p1 = new PartyMemberInfo();
            var p2 = new PartyMemberInfo();
            var p3 = new PartyMemberInfo();

            RunSessionManager.CurrentParty.Add(p1);
            RunSessionManager.CurrentParty.Add(p2);
            RunSessionManager.CurrentParty.Add(p3);

            var c1GO = new GameObject("C1");
            var c1 = c1GO.AddComponent<CombatCharacter>();
            c1.partyInfo = p1;
            c1.rank = 1;
            c1.state = LifeState.Pile; // Should be removed

            var c2GO = new GameObject("C2");
            var c2 = c2GO.AddComponent<CombatCharacter>();
            c2.partyInfo = p2;
            c2.rank = 3;
            c2.state = LifeState.Alive;

            var c3GO = new GameObject("C3");
            var c3 = c3GO.AddComponent<CombatCharacter>();
            c3.partyInfo = p3;
            c3.rank = 2;
            c3.state = LifeState.Alive;

            var playerTeam = new List<CombatCharacter> { c1, c2, c3 };
            typeof(BattleSystem).GetField("_playerTeam", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(battleSystem, playerTeam);
            typeof(BattleSystem).GetField("_enemyTeam", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(battleSystem, new List<CombatCharacter>());

            // Act
            var isEnd = battleSystem.CheckBattleEnd();

            // Assert
            Assert.IsTrue(isEnd);
            Assert.AreEqual(2, RunSessionManager.CurrentParty.Count);
            
            // Expected order based on remaining ranks: c3 (rank 2) -> c2 (rank 3)
            // Which maps to: p3, p2
            Assert.AreSame(p3, RunSessionManager.CurrentParty[0]);
            Assert.AreSame(p2, RunSessionManager.CurrentParty[1]);

            // Cleanup
            UnityEngine.Object.DestroyImmediate(battleGO);
            UnityEngine.Object.DestroyImmediate(c1GO);
            UnityEngine.Object.DestroyImmediate(c2GO);
            UnityEngine.Object.DestroyImmediate(c3GO);
        }
        // ============================================================
        // BossRoomEffectStrategy Tests
        // ============================================================

        [Test]
        public void BossRoomEffectStrategy_Execute_CreatesUIWithEndRunButton()
        {
            // Arrange
            var canvasGO = new GameObject("TestCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var endRunPrefab = new GameObject("EndRunPrefab");
            endRunPrefab.AddComponent<RectTransform>(); // Need RectTransform to avoid null ref in strategy
            
            var strategy = new BossRoomEffectStrategy();
            strategy.endRunPrefab = endRunPrefab;

            // Act
            strategy.ExecuteRoomEffect();

            // Assert
            var victoryPanel = canvasGO.transform.Find("BossRoomVictoryPanel");
            Assert.IsNotNull(victoryPanel, "BossRoomVictoryPanel should be created under Canvas.");

            var textGo = victoryPanel.Find("RunCompletedText");
            Assert.IsNotNull(textGo, "RunCompletedText should be created under the panel.");
            var textComp = textGo.GetComponent<TMPro.TextMeshProUGUI>();
            Assert.IsNotNull(textComp, "TextMeshProUGUI component should be attached.");
            Assert.AreEqual("Run Completed", textComp.text);

            var endRunClone = victoryPanel.Find("EndRunPrefab(Clone)");
            Assert.IsNotNull(endRunClone, "EndRun prefab should be instantiated under the panel.");

            // Cleanup
            UnityEngine.Object.DestroyImmediate(canvasGO);
            UnityEngine.Object.DestroyImmediate(endRunPrefab);
        }
        // ============================================================
        // HealRoomEffectStrategy & MarionetteHealChoiceController Tests
        // ============================================================

        [Test]
        public void HealRoomEffect_SingleHeal_Restores999HP_CappedAtMaxHP()
        {
            // Arrange
            var member = new PartyMemberInfo { currentLevel = 1 };
            var characterAsset = ScriptableObject.CreateInstance<CharacterData>();
            characterAsset.statPerLevel = new List<StatBlockData> { new StatBlockData { maxHP = 100 } };
            member.character = characterAsset;
            member.currentHP = 10; // Needs healing

            var party = new List<PartyMemberInfo> { member };

            var go = new GameObject("HealChoice");
            var controller = go.AddComponent<Nevergreen.UI.MarionetteHealChoiceController>();
            var btn1Go = new GameObject("MarionetteButton1");
            var btn1 = btn1Go.AddComponent<UnityEngine.UI.Button>();
            var tmpro1 = btn1Go.AddComponent<TMPro.TextMeshProUGUI>();
            
            var buttonsField = typeof(Nevergreen.UI.MarionetteHealChoiceController).GetField("marionetteButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            buttonsField.SetValue(controller, new UnityEngine.UI.Button[] { btn1, null, null, null });

            // Act
            controller.Initialize(party);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[MarionetteHealChoiceController] CombatUI not found!");
            btn1.onClick.Invoke();

            // Assert
            Assert.IsNull(member.currentHP, "HP should be null (max) because 10 + 999 > 100.");
            
            UnityEngine.Object.DestroyImmediate(characterAsset);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void HealRoomEffect_HealAll_RestoresExactly25PercentMaxHP()
        {
            // Arrange
            var member1 = new PartyMemberInfo { currentLevel = 1 };
            var member2 = new PartyMemberInfo { currentLevel = 1 };
            
            var characterAsset = ScriptableObject.CreateInstance<CharacterData>();
            characterAsset.statPerLevel = new List<StatBlockData> { new StatBlockData { maxHP = 100 } };
            
            member1.character = characterAsset;
            member1.currentHP = 50;
            
            member2.character = characterAsset;
            member2.currentHP = null; // Already full
            
            var party = new List<PartyMemberInfo> { member1, member2 };

            var go = new GameObject("HealChoice");
            var controller = go.AddComponent<Nevergreen.UI.MarionetteHealChoiceController>();
            
            var healAllGo = new GameObject("HealAllButton");
            var healAllBtn = healAllGo.AddComponent<UnityEngine.UI.Button>();
            var tmpro = healAllGo.AddComponent<TMPro.TextMeshProUGUI>();
            
            var healBtnField = typeof(Nevergreen.UI.MarionetteHealChoiceController).GetField("healAllButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            healBtnField.SetValue(controller, healAllBtn);

            // Act
            controller.Initialize(party);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[MarionetteHealChoiceController] CombatUI not found!");
            healAllBtn.onClick.Invoke();

            // Assert
            Assert.AreEqual(75, member1.currentHP, "50 + 25% of 100 = 75");
            Assert.IsNull(member2.currentHP, "Already full HP should remain null");

            UnityEngine.Object.DestroyImmediate(characterAsset);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void HealRoomEffect_CompletesRoomAndSaves()
        {
            // Arrange
            var combatUIGo = new GameObject("CombatUI");
            var combatUI = combatUIGo.AddComponent<Nevergreen.Prototype.CombatUI>();

            var member = new PartyMemberInfo { currentLevel = 1 };
            var characterAsset = ScriptableObject.CreateInstance<CharacterData>();
            characterAsset.statPerLevel = new List<StatBlockData> { new StatBlockData { maxHP = 100 } };
            member.character = characterAsset;
            member.currentHP = 10;

            var party = new List<PartyMemberInfo> { member };

            var go = new GameObject("HealChoice");
            var controller = go.AddComponent<Nevergreen.UI.MarionetteHealChoiceController>();
            var btn1Go = new GameObject("MarionetteButton1");
            var btn1 = btn1Go.AddComponent<UnityEngine.UI.Button>();
            var tmpro1 = btn1Go.AddComponent<TMPro.TextMeshProUGUI>();
            
            var buttonsField = typeof(Nevergreen.UI.MarionetteHealChoiceController).GetField("marionetteButtons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            buttonsField.SetValue(controller, new UnityEngine.UI.Button[] { btn1, null, null, null });

            // Act
            controller.Initialize(party);
            btn1.onClick.Invoke();

            // Assert
            Assert.IsFalse(go.activeSelf, "UI should be deactivated after choice");

            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(combatUIGo);
        }

        // ============================================================
        // ScrapsRewardRoomEffectStrategy Tests
        // ============================================================

        [Test]
        public void ScrapsRewardRoomEffectStrategy_Execute_CreatesUIPopup()
        {
            // Arrange
            var canvasGO = new GameObject("TestCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var combatUIGO = new GameObject("CombatUI");
            var combatUI = combatUIGO.AddComponent<Nevergreen.Prototype.CombatUI>();

            var popupPrefab = new GameObject("ScrapsPopupPrefab");
            popupPrefab.AddComponent<RectTransform>();
            var controller = popupPrefab.AddComponent<Nevergreen.UI.ScrapsRewardUIController>();
            
            var textGo = new GameObject("RewardText");
            controller.rewardText = textGo.AddComponent<TMPro.TextMeshProUGUI>();

            var btnGo = new GameObject("ClaimButton");
            controller.claimButton = btnGo.AddComponent<UnityEngine.UI.Button>();

            var panelGo = new GameObject("Panel");
            controller.panel = panelGo;

            var strategy = new ScrapsRewardRoomEffectStrategy();
            var field = typeof(ScrapsRewardRoomEffectStrategy).GetField("scrapsRewardUiPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(strategy, popupPrefab);

            // Set fixed scraps amount for testing predictability if needed, or let it random roll
            
            // Act
            strategy.ExecuteRoomEffect();

            // Assert
            var spawnedPopup = canvasGO.transform.Find("ScrapsPopupPrefab(Clone)");
            Assert.IsNotNull(spawnedPopup, "Scraps popup prefab should be instantiated under the canvas.");
            
            // Click Claim button to grant scraps
            var spawnedController = spawnedPopup.GetComponent<Nevergreen.UI.ScrapsRewardUIController>();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");
            spawnedController.claimButton.onClick.Invoke();

            // Verify RunSessionManager Scraps updated
            // It randomly rolls between 30 and 70 (default min/max) so > 0
            Assert.Greater(RunSessionManager.Scraps, 0, "Scraps should be awarded to the run session.");

            // Cleanup
            UnityEngine.Object.DestroyImmediate(canvasGO);
            UnityEngine.Object.DestroyImmediate(popupPrefab);
            UnityEngine.Object.DestroyImmediate(combatUIGO);
        }
    }
}
