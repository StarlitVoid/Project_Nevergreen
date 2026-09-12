using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Nevergreen.Data;
using Nevergreen.Combat;
using Nevergreen.UI;

namespace Nevergreen.Prototype
{
    /// <summary>
    /// Prototype combat UI. Manages skill buttons, HP bars, stats panel,
    /// target selection, and the player input flow.
    /// </summary>
    public class CombatUI : MonoBehaviour
    {
        [Header("UI Panels")]
        public GameObject skillPanel;
        public GameObject statsPanel;
        public TextMeshProUGUI roundText;
        public TextMeshProUGUI turnText;
        public TextMeshProUGUI battleLogText;
        public TextMeshProUGUI statsDisplayText;
        public GameObject battleEndPanel;
        public TextMeshProUGUI battleEndText;

        [Header("Rewards UI")]
        [Tooltip("The popup to show rewards after a battle.")]
        public CombatRewardUI rewardUI;

        [Tooltip("Button shown after victory to proceed to the next room (fallback).")]
        public Button nextRoomButton;

        [Header("Party Management UI")]
        [Tooltip("Button to open the party management panel, visible only when a room is completed.")]
        public Button partyManagementButton;

        [Header("Room Choice")]
        [Tooltip("Prefab for a single room choice button. Must have a Button and TextMeshProUGUI child.")]
        public GameObject roomChoiceButtonPrefab;

        [Tooltip("Container where room choice buttons are instantiated.")]
        public Transform roomChoiceButtonsContainer;

        private System.Random _rng = new System.Random();

        [Header("Skill Buttons")]
        public Button[] skillButtons = new Button[4];
        public TextMeshProUGUI[] skillButtonLabels = new TextMeshProUGUI[4];
        public Button moveButton;
        public Button passButton;

        [Header("HP Bar Prefab")]
        public GameObject hpBarPrefab;
        public Canvas worldSpaceCanvas;

        private BattleSystem _battleSystem;
        private List<CombatCharacter> _playerTeam;
        private List<CombatCharacter> _enemyTeam;
        private Dictionary<CombatCharacter, HPBar> _hpBars = new Dictionary<CombatCharacter, HPBar>();
        private AnimationQueueProcessor _animationQueue;

        // Input state
        private SkillData _selectedAction;
        private List<CombatCharacter> _validTargets = new List<CombatCharacter>();
        private bool _selecting = false;
        private bool _isSelectingMove = false;

        // Log
        private List<string> _logLines = new List<string>();
        private const int MAX_LOG_LINES = 8;

        public void Initialize(BattleSystem system, List<CombatCharacter> playerTeam,
                                List<CombatCharacter> enemyTeam)
        {
            _battleSystem = system;
            _playerTeam = playerTeam;
            _enemyTeam = enemyTeam;

            // Cache animation queue reference
            _animationQueue = system.animationQueue;

            // Subscribe to events
            _battleSystem.OnBattleStarted += HandleBattleStarted;
            _battleSystem.OnRoundStarted += HandleRoundStarted;
            _battleSystem.OnTurnStarted += HandleTurnStarted;
            _battleSystem.OnWaitingForPlayerInput += HandleWaitingForInput;
            _battleSystem.OnActionResolved += HandleActionResolved;
            _battleSystem.OnBattleEnded += HandleBattleEnded;
            _battleSystem.OnCharacterDefeated += HandleCharacterDefeated;
            _battleSystem.OnCharacterSpawned += HandleCharacterSpawned;

            // Subscribe to animation queue lock state
            if (_animationQueue != null)
            {
                _animationQueue.OnInputLockChanged += HandleAnimationLockChanged;
            }

            RunSessionManager.RoomComplete += HandleRoomComplete;
            if (partyManagementButton != null)
            {
                partyManagementButton.gameObject.SetActive(RunSessionManager.RoomCompleted);
            }

            // Create HP bars
            CreateHPBars();

            // Setup skill buttons
            for (int i = 0; i < skillButtons.Length; i++)
            {
                int idx = i;
                if (skillButtons[i] != null)
                {
                    skillButtons[i].onClick.AddListener(() => OnSkillButtonClicked(idx));
                    skillButtons[i].gameObject.SetActive(true);
                    skillButtons[i].interactable = false;
                }
            }

            if (moveButton != null)
            {
                moveButton.onClick.AddListener(OnMoveButtonClicked);
                moveButton.gameObject.SetActive(true);
                moveButton.interactable = false;
            }

            if (passButton != null)
            {
                passButton.onClick.AddListener(OnPassButtonClicked);
                passButton.gameObject.SetActive(true);
                passButton.interactable = false;
            }

            if (battleEndPanel != null)
                battleEndPanel.SetActive(false);

            if (nextRoomButton != null)
            {
                nextRoomButton.gameObject.SetActive(false);
                nextRoomButton.onClick.RemoveAllListeners();
                nextRoomButton.onClick.AddListener(OnNextRoomClicked);
            }

            ClearLog();
        }

        private void CreateHPBars()
        {
            if (hpBarPrefab == null || worldSpaceCanvas == null) return;

            foreach (var c in _playerTeam.Concat(_enemyTeam))
            {
                GameObject barGo = Instantiate(hpBarPrefab, worldSpaceCanvas.transform);
                HPBar bar = barGo.GetComponent<HPBar>();
                if (bar != null)
                {
                    bar.Initialize(c, _animationQueue);
                    _hpBars[c] = bar;
                }

                // Subscribe to status events for logging
                c.OnStatusApplied += HandleStatusApplied;
                c.OnPeriodicEffectApplied += HandlePeriodicEffectApplied;
            }
        }

        private void Update()
        {
            // Update HP bar positions
            foreach (var kvp in _hpBars)
            {
                if (kvp.Key != null && kvp.Value != null)
                {
                    kvp.Value.UpdatePosition();
                }
            }

            // Handle target click during selection
            if (_selecting && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                TrySelectTarget();
            }

            // Handle hover for stats display
            UpdateStatsHover();

            // Handle target selection visual preview
            UpdateTargetHoverHighlight();
        }

        private void HandleBattleStarted()
        {
            AddLog("Battle started!");
        }

        private void HandleRoundStarted(int round)
        {
            if (roundText != null)
                roundText.text = $"Round {round}";
        }

        private void HandleTurnStarted(CombatCharacter actor)
        {
            if (turnText != null)
                turnText.text = $"{actor.DisplayName}'s Turn";

            // Failsafe: Refresh all HP bars at start of turn
            foreach (var bar in _hpBars.Values)
            {
                if (bar != null) bar.Refresh();
            }
        }

        private void HandleWaitingForInput()
        {
            CombatCharacter actor = _battleSystem.CurrentActor;
            ShowSkillButtons(actor);
        }

        private void HandleActionResolved(CombatCharacter actor, SkillData skill, SkillContext context)
        {
            var target = context.primaryTarget;
            if (target == null) return;

            if (skill.modifier.IsHeal)
            {
                AddLog($"{actor.DisplayName} heals {target.DisplayName} for {context.calculatedValue} HP");
            }
            else if (context.didHit)
            {
                string critStr = context.isCritical ? " (CRIT!)" : "";
                AddLog($"{actor.DisplayName} -> {target.DisplayName}: {context.calculatedValue} dmg{critStr}");
            }
            else
            {
                AddLog($"{actor.DisplayName} -> {target.DisplayName}: MISS!");
            }
        }

        private void HandleCharacterDefeated(CombatCharacter character)
        {
            AddLog($"{character.DisplayName} defeated!");

            // Grey out defeated character
            var sr = character.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        }

        private void HandleCharacterSpawned(CombatCharacter character)
        {
            if (hpBarPrefab == null || worldSpaceCanvas == null) return;

            GameObject barGo = Instantiate(hpBarPrefab, worldSpaceCanvas.transform);
            HPBar bar = barGo.GetComponent<HPBar>();
            if (bar != null)
            {
                bar.Initialize(character, _animationQueue);
                _hpBars[character] = bar;
            }

            // Subscribe to status events for logging
            character.OnStatusApplied += HandleStatusApplied;
            character.OnPeriodicEffectApplied += HandlePeriodicEffectApplied;
        }

        private void HandleStatusApplied(CombatCharacter target, StatusType type, bool succeeded, StatTarget? targetStat)
        {
            string statusName = type.ToString();
            if ((type == StatusType.Buff || type == StatusType.Debuff) && targetStat.HasValue)
            {
                statusName = $"{targetStat.Value} {type}";
            }

            if (succeeded)
            {
                AddLog($"{target.DisplayName} afflicted with {statusName}!");
            }
            else
            {
                AddLog($"{target.DisplayName} resisted {statusName}.");
            }
        }

        private void HandlePeriodicEffectApplied(CombatCharacter target, StatusType type, int amount)
        {
            string effectName = type.ToString();
            if (type == StatusType.Restore)
            {
                AddLog($"{target.DisplayName} restored {amount} HP from {effectName}");
            }
            else
            {
                AddLog($"{target.DisplayName} takes {amount} {effectName} damage");
            }
        }

        private void HandleBattleEnded(BattleOutcome outcome)
        {
            HideSkillButtons();

            bool hasOnVictoryRoomEffect = RunSessionManager.NextRoomData != null 
                && RunSessionManager.NextRoomData.activationType == RoomActivationType.OnCombatVictory;

            if (battleEndPanel != null && !hasOnVictoryRoomEffect)
            {
                battleEndPanel.SetActive(true);
                if (battleEndText != null)
                    battleEndText.text = outcome == BattleOutcome.Victory ? "VICTORY!" : "DEFEAT";
            }

            AddLog($"Battle ended: {outcome}");

            if (outcome == BattleOutcome.Victory)
            {
                if (nextRoomButton != null)
                {
                    var txt = nextRoomButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = "Next Room";
                }
                
                if (rewardUI != null)
                {
                    rewardUI.ShowReward(_battleSystem.PartsGrantedThisBattle, _battleSystem.ScrapsGrantedThisBattle, OnRewardPopupClosed);
                }
                else
                {
                    OnRewardPopupClosed();
                }
            }
            else
            {
                if (nextRoomButton != null)
                {
                    var txt = nextRoomButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = "Back to Main Menu";
                    
                    nextRoomButton.onClick.RemoveAllListeners();
                    nextRoomButton.onClick.AddListener(OnBackToMainMenuClicked);
                    nextRoomButton.gameObject.SetActive(true);
                    if (roomChoiceButtonsContainer != null)
                    {
                        roomChoiceButtonsContainer.gameObject.SetActive(false);
                    }
                }
            }
        }

        public void ShowRoomSelectionImmediately()
        {
            HideSkillButtons();

            if (battleEndPanel != null)
            {
                battleEndPanel.SetActive(true);
                if (battleEndText != null)
                    battleEndText.text = "CHOOSE NEXT ROOM";
            }

            if (nextRoomButton != null)
            {
                var txt = nextRoomButton.GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = "Next Room";
            }

            if (partyManagementButton != null)
            {
                partyManagementButton.gameObject.SetActive(true);
            }

            SpawnRoomChoiceButtons();
        }

        private void OnRewardPopupClosed()
        {
            bool hasOnVictoryRoomEffect = RunSessionManager.NextRoomData != null 
                && RunSessionManager.NextRoomData.activationType == RoomActivationType.OnCombatVictory;

            if (hasOnVictoryRoomEffect)
            {
                RunSessionManager.TriggerPendingVictoryRoomEffect();
            }
            else
            {
                SpawnRoomChoiceButtons();
            }
        }

        private void SpawnRoomChoiceButtons()
        {
            // Clear any previously spawned room choice buttons
            if (roomChoiceButtonsContainer != null)
            {
                for (int i = roomChoiceButtonsContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(roomChoiceButtonsContainer.GetChild(i).gameObject);
                }
            }

            // Check if we have the config and prefab to create dynamic buttons
            var globalConfig = GameDatabase.Instance != null ? GameDatabase.Instance.GlobalConfig : null;
            var combatConfig = GameDatabase.Instance != null ? GameDatabase.Instance.CombatConfig : null;
            var roomDb = GameDatabase.Instance != null ? GameDatabase.Instance.RoomDatabase : null;
            var availableRooms = roomDb != null ? roomDb.availableRooms : null;
            
            bool hasAvailableRooms = availableRooms != null && availableRooms.Count > 0;
            bool hasBossRoom = roomDb != null && roomDb.bossRoom != null;
            bool hasHealRoom = roomDb != null && roomDb.healRoom != null;

            bool canSpawnDynamic = globalConfig != null
                && (hasAvailableRooms || hasBossRoom || hasHealRoom)
                && roomChoiceButtonPrefab != null
                && roomChoiceButtonsContainer != null;

            if (canSpawnDynamic)
            {
                // Hide fallback button
                if (nextRoomButton != null)
                    nextRoomButton.gameObject.SetActive(false);

                // Check if we already have choices saved in the session (e.g. resuming after victory)
                var choices = RunSessionManager.NextRoomChoices;
                if (choices == null || choices.Count == 0)
                {
                    bool isBossNext = false;
                    bool isTierTransition = false;

                    if (combatConfig != null)
                    {
                        var currentTier = combatConfig.GetEncounterTierForRoom(RunSessionManager.RoomProgression);
                        var nextTier = combatConfig.GetEncounterTierForRoom(RunSessionManager.RoomProgression + 1);

                        bool isCurrentHealRoom = RunSessionManager.IsHealRoom(RunSessionManager.CurrentRoomData);
                        isTierTransition = (currentTier != nextTier) && (currentTier != EnemyEncounterTier.Trivial) && !isCurrentHealRoom;

                        if (nextTier == EnemyEncounterTier.Boss && hasBossRoom)
                        {
                            isBossNext = true;
                        }
                    }

                    if (isTierTransition)
                    {
                        if (hasHealRoom)
                        {
                            choices = new List<RoomData> { roomDb.healRoom };
                        }
                        else
                        {
                            Debug.LogWarning("[CombatUI] Expected to force Heal Room on tier transition, but healRoom is not assigned in RoomDatabase. Falling back to random rooms.");
                            choices = WeightedRoomSelector.SelectRooms(availableRooms, globalConfig.roomChoiceCount, _rng);
                        }
                    }
                    else if (isBossNext)
                    {
                        choices = new List<RoomData> { roomDb.bossRoom };
                    }
                    else if (hasAvailableRooms)
                    {
                        // Pick random rooms (up to roomChoiceCount)
                        choices = WeightedRoomSelector.SelectRooms(availableRooms, globalConfig.roomChoiceCount, _rng);
                    }
                    else
                    {
                        choices = new List<RoomData>();
                    }

                    RunSessionManager.CompleteRoom(choices);
                }

                foreach (var room in choices)
                {
                    GameObject btnGo = Instantiate(roomChoiceButtonPrefab, roomChoiceButtonsContainer);
                    var btn = btnGo.GetComponent<Button>();
                    btnGo.GetComponent<Image>().enabled = true;
                    btn.enabled = true;
                    var label = btnGo.GetComponentInChildren<TextMeshProUGUI>();
                    label.enabled = true;

                    if (label != null)
                        label.text = room.roomName;

                    if (btn != null)
                    {
                        RoomData capturedRoom = room;
                        btn.onClick.AddListener(() => OnRoomChoiceClicked(capturedRoom));
                    }

                    var tooltipTrigger = btnGo.GetComponent<RoomTooltipTrigger>();
                    tooltipTrigger.enabled = true;
                    tooltipTrigger.SetRoom(room);
                }
            }
            else
            {
                // Fallback: show default next room button
                if (nextRoomButton != null)
                    nextRoomButton.gameObject.SetActive(true);
            }
        }

        private void HandleRoomComplete()
        {
            if (partyManagementButton != null)
            {
                partyManagementButton.gameObject.SetActive(true);
            }
        }

        private void OnRoomChoiceClicked(RoomData selectedRoom)
        {
            RunSessionManager.NextRoomData = selectedRoom;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>
        /// Handles the animation queue locking/unlocking input.
        /// When locked: disable all combat buttons.
        /// When unlocked: re-show buttons if we're still waiting for player input.
        /// </summary>
        private void HandleAnimationLockChanged(AnimationQueueState state)
        {
            if (state.isInputLocked)
            {
                HideSkillButtons();
            }
            else if (_battleSystem.IsWaitingForPlayerInput)
            {
                ShowSkillButtons(_battleSystem.CurrentActor);
            }
        }

        private void ShowSkillButtons(CombatCharacter actor)
        {
            for (int i = 0; i < skillButtons.Length; i++)
            {
                if (skillButtons[i] == null) continue;

                skillButtons[i].gameObject.SetActive(true);

                var tooltipTrigger = skillButtons[i].gameObject.GetComponent<SkillTooltipTrigger>();
                if (tooltipTrigger == null)
                {
                    tooltipTrigger = skillButtons[i].gameObject.AddComponent<SkillTooltipTrigger>();
                }

                if (i < actor.equippedSkills.Count)
                {
                    var skill = actor.equippedSkills[i];
                    tooltipTrigger.SetSkill(skill);

                    skillButtons[i].interactable =
                        actor.CanUseSkillFromRank(skill) && actor.HasRemainingUses(skill);

                    if (skillButtonLabels[i] != null)
                    {
                        string dmgInfo = "";
                        if (skill.modifier.IsDamage)
                            dmgInfo = $" ({skill.modifier.damagePercent * 100f:F0}% ATK)";
                        else if (skill.modifier.IsHeal)
                            dmgInfo = $" (Heal {skill.modifier.healPercent * 100f:F0}%)";

                        skillButtonLabels[i].text = $"{skill.displayName}{dmgInfo}";
                    }
                }
                else
                {
                    tooltipTrigger.SetSkill(null);
                    skillButtons[i].interactable = false;
                    if (skillButtonLabels[i] != null)
                    {
                        skillButtonLabels[i].text = "";
                    }
                }
            }

            if (moveButton != null)
                moveButton.interactable = true;
            if (passButton != null)
                passButton.interactable = true;
        }

        private void HideSkillButtons()
        {
            TooltipEvents.HideTooltip();
            foreach (var btn in skillButtons)
            {
                if (btn != null) btn.interactable = false;
            }
            if (moveButton != null) moveButton.interactable = false;
            if (passButton != null) passButton.interactable = false;
        }

        private void OnSkillButtonClicked(int index)
        {
            if (_selecting) HighlightValidTargets(false);
            _isSelectingMove = false;

            CombatCharacter actor = _battleSystem.CurrentActor;
            if (index >= actor.equippedSkills.Count) return;

            _selectedAction = actor.equippedSkills[index];
            _validTargets = _battleSystem.GetValidTargets(actor, _selectedAction);

            if (_validTargets.Count == 0)
            {
                AddLog("No valid targets!");
                return;
            }

            // Need target selection
            _selecting = true;
            HighlightValidTargets(true);
            AddLog($"Select target for {_selectedAction.displayName}...");
        }

        private void OnMoveButtonClicked()
        {
            if (_selecting) HighlightValidTargets(false);
            
            CombatCharacter actor = _battleSystem.CurrentActor;

            // Find adjacent ally to swap with
            var allies = actor.IsPlayerTeam ? _playerTeam : _enemyTeam;
            _validTargets = allies
                .Where(a => a.IsAlive && a != actor &&
                            Mathf.Abs(a.rank - actor.rank) == 1)
                .ToList();

            if (_validTargets.Count > 0)
            {
                _selecting = true;
                _isSelectingMove = true;
                HighlightValidTargets(true);
                AddLog($"Select adjacent ally to swap with...");
            }
            else
            {
                AddLog("No adjacent ally to swap with!");
            }
        }

        private void OnPassButtonClicked()
        {
            if (_selecting) HighlightValidTargets(false);
            _selecting = false;
            _isSelectingMove = false;
            
            _battleSystem.SubmitPassAction();
            HideSkillButtons();
            AddLog($"{_battleSystem.CurrentActor.DisplayName} passes.");
        }

        private void TrySelectTarget()
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

            if (hit.collider != null)
            {
                CombatCharacter clicked = hit.collider.GetComponent<CombatCharacter>();
                if (clicked != null && _validTargets.Contains(clicked))
                {
                    if (_isSelectingMove)
                    {
                        _battleSystem.SubmitMoveAction(clicked);
                    }
                    else
                    {
                        List<CombatCharacter> selected = _battleSystem.GetAOETargets(_selectedAction, clicked);
                        _battleSystem.SubmitPlayerAction(_selectedAction, selected);
                    }
                    
                    _selecting = false;
                    _isSelectingMove = false;
                    HighlightValidTargets(false);
                    HideSkillButtons();
                }
            }
        }

        private void HighlightValidTargets(bool highlight)
        {
            foreach (var t in _validTargets)
            {
                var sr = t.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    if (highlight)
                    {
                        sr.color = Color.yellow;
                    }
                    else
                    {
                        // Reset to base color: gray for piles, white for alive
                        sr.color = (t.state == LifeState.Pile)
                            ? new Color(0.3f, 0.3f, 0.3f, 0.5f)
                            : Color.white;
                    }
                }
            }
        }

        private void UpdateTargetHoverHighlight()
        {
            if (!_selecting || _isSelectingMove) return;

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

            CombatCharacter hovered = null;
            if (hit.collider != null)
            {
                hovered = hit.collider.GetComponent<CombatCharacter>();
            }

            // Determine preview target set
            List<CombatCharacter> previewTargets = new List<CombatCharacter>();
            if (hovered != null && _validTargets.Contains(hovered))
            {
                previewTargets = _battleSystem.GetAOETargets(_selectedAction, hovered);
            }

            // Apply color highlighting
            foreach (var t in _validTargets)
            {
                var sr = t.GetComponentInChildren<SpriteRenderer>();
                if (sr != null)
                {
                    if (previewTargets.Contains(t))
                    {
                        sr.color = Color.green; // Preview selection
                    }
                    else
                    {
                        sr.color = Color.yellow; // Baseline valid target
                    }
                }
            }
        }

        private void UpdateStatsHover()
        {
            if (statsDisplayText == null) return;

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero);

            if (hit.collider != null)
            {
                CombatCharacter hovered = hit.collider.GetComponent<CombatCharacter>();
                if (hovered != null)
                {
                    CombatStats stats = hovered.GetEffectiveStats();
                    statsDisplayText.text =
                        $"{hovered.DisplayName} (Lv{hovered.currentLevel})\n" +
                        $"HP: {hovered.currentHP}/{stats.maxHP}\n" +
                        $"ATK: {stats.attack}  DEF: {stats.defense}%\n" +
                        $"ACC: {stats.accuracy}%  DOD: {stats.dodge}%\n" +
                        $"CRIT: {stats.critChance}%  SPD: {stats.speed}\n" +
                        $"Rank: {hovered.rank}  Team: {hovered.team}\n" +
                        $"BLEED RES: {stats.bleedResist}%  BLIGHT RES: {stats.blightResist}%  STUN RES: {stats.stunResist}%\n" +
                        $"DEBUFF RES: {stats.debuffResist}%  MOVE RES: {stats.moveResist}%";
                    return;
                }
            }

            statsDisplayText.text = "Hover over a character to see stats";
        }

        private void AddLog(string message)
        {
            _logLines.Add(message);
            while (_logLines.Count > MAX_LOG_LINES)
                _logLines.RemoveAt(0);

            if (battleLogText != null)
                battleLogText.text = string.Join("\n", _logLines);
        }

        private void ClearLog()
        {
            _logLines.Clear();
            if (battleLogText != null)
                battleLogText.text = "";
        }

        private void OnNextRoomClicked()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void OnBackToMainMenuClicked()
        {
            SceneManager.LoadScene("MainMenu");
        }

        private void OnDestroy()
        {
            if (_battleSystem != null)
            {
                _battleSystem.OnBattleStarted -= HandleBattleStarted;
                _battleSystem.OnRoundStarted -= HandleRoundStarted;
                _battleSystem.OnTurnStarted -= HandleTurnStarted;
                _battleSystem.OnWaitingForPlayerInput -= HandleWaitingForInput;
                _battleSystem.OnActionResolved -= HandleActionResolved;
                _battleSystem.OnBattleEnded -= HandleBattleEnded;
                _battleSystem.OnCharacterDefeated -= HandleCharacterDefeated;
                _battleSystem.OnCharacterSpawned -= HandleCharacterSpawned;
            }

            foreach (var c in _playerTeam.Concat(_enemyTeam))
            {
                if (c != null)
                {
                    c.OnStatusApplied -= HandleStatusApplied;
                    c.OnPeriodicEffectApplied -= HandlePeriodicEffectApplied;
                }
            }

            if (_animationQueue != null)
            {
                _animationQueue.OnInputLockChanged -= HandleAnimationLockChanged;
            }

            RunSessionManager.RoomComplete -= HandleRoomComplete;
        }
    }
}
