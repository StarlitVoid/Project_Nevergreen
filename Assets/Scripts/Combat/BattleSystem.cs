using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Nevergreen.Data;

namespace Nevergreen.Combat
{
    /// <summary>
    /// Core turn-based combat state machine.
    /// Manages round/turn sequencing, action resolution, and battle lifecycle.
    /// </summary>
    public class BattleSystem : MonoBehaviour
    {
        [Header("Config")]

        [Tooltip("Animation queue processor. Auto-created at runtime if not assigned.")]
        public AnimationQueueProcessor animationQueue;

        [Tooltip("Enemy skill announcement banner. Optional — if not assigned, enemies execute skills immediately.")]
        public EnemySkillBanner enemySkillBanner;

        // --- Runtime State ---
        public BattleState CurrentState { get; private set; } = BattleState.Inactive;
        public int CurrentRound { get; private set; } = 0;
        public CombatCharacter CurrentActor { get; private set; }

        /// <summary>Parts awarded upon victory in the current battle.</summary>
        public int PartsGrantedThisBattle { get; private set; }

        /// <summary>Scraps awarded upon victory in the current battle.</summary>
        public int ScrapsGrantedThisBattle { get; private set; }

        // Layout configuration (injected by Bootstrap or set in Inspector)
        [HideInInspector] public float playerBaseX = -3f;
        [HideInInspector] public float playerSpacingX = -2f;
        [HideInInspector] public float enemyBaseX = 3f;
        [HideInInspector] public float enemySpacingX = 2f;

        private List<CombatCharacter> _playerTeam = new List<CombatCharacter>();
        private List<CombatCharacter> _enemyTeam = new List<CombatCharacter>();
        private List<CombatCharacter> _initialPlayerTeam = new List<CombatCharacter>();
        private List<TurnEntry> _turnOrder = new List<TurnEntry>();
        private int _currentTurnIndex = 0;
        private System.Random _rng = new System.Random();

        private FormationManager _formationManager = new FormationManager();
        private SkillExecutor _skillExecutor = new SkillExecutor();
        private CharacterLifecycleManager _lifecycleManager = new CharacterLifecycleManager();

        private bool _isInitialized = false;

        // Player input state
        private bool _waitingForPlayerInput = false;

        // --- Events ---
        public event Action OnBattleStarted;
        public event Action<int> OnRoundStarted; // round number
        public event Action<int> OnRoundEnded; // round number
        public event Action<CombatCharacter> OnTurnStarted;
        public event Action<SkillContext> OnBeforeDamageCalculation;
        public event Action<SkillContext, CombatCharacter> OnBeforeDamageCalculationPerTarget;
        public event Action<CombatCharacter, SkillData, SkillContext> OnActionResolved; // actor, skill, context
        public event Action<BattleOutcome> OnBattleEnded;
        public event Action OnWaitingForPlayerInput;
        public event Action<CombatCharacter> OnCharacterDefeated;
        public event Action<CombatCharacter> OnCharacterRemoved;

        public void TriggerBeforeDamageCalculationPerTarget(SkillContext ctx, CombatCharacter target)
        {
            OnBeforeDamageCalculationPerTarget?.Invoke(ctx, target);
        }

        public List<CombatCharacter> PlayerTeam => _playerTeam;
        public List<CombatCharacter> EnemyTeam => _enemyTeam;
        public bool IsWaitingForPlayerInput => _waitingForPlayerInput;

        private void Awake()
        {
            EnsureManagersInitialized();
        }

        private void EnsureManagersInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // Ensure animation queue exists early so UI can bind to it
            if (animationQueue == null)
            {
                animationQueue = GetComponent<AnimationQueueProcessor>();
                if (animationQueue == null)
                {
                    animationQueue = gameObject.AddComponent<AnimationQueueProcessor>();
                }
            }

            if (GetComponent<BattleMusicController>() == null)
            {
                gameObject.AddComponent<BattleMusicController>();
            }

            _formationManager.playerBaseX = playerBaseX;
            _formationManager.playerSpacingX = playerSpacingX;
            _formationManager.enemyBaseX = enemyBaseX;
            _formationManager.enemySpacingX = enemySpacingX;
            
            _formationManager.Initialize(animationQueue);
            _skillExecutor.Initialize(animationQueue, enemySkillBanner, this);
            _lifecycleManager.Initialize(_formationManager, animationQueue, this);

            _skillExecutor.OnBeforeDamageCalculation += (ctx) => OnBeforeDamageCalculation?.Invoke(ctx);
            _skillExecutor.OnBeforeDamageCalculationPerTarget += (ctx, target) => OnBeforeDamageCalculationPerTarget?.Invoke(ctx, target);
            _skillExecutor.OnActionResolved += (actor, skill, ctx) => OnActionResolved?.Invoke(actor, skill, ctx);
            
            _lifecycleManager.OnCharacterDefeated += (c) => OnCharacterDefeated?.Invoke(c);
            _lifecycleManager.OnCharacterRemoved += (c) => OnCharacterRemoved?.Invoke(c);
            _lifecycleManager.OnCharacterSpawned += (c) => OnCharacterSpawned?.Invoke(c);
        }

        /// <summary>
        /// Start a battle with the given teams.
        /// </summary>
        public void StartBattle(List<CombatCharacter> playerTeam, List<CombatCharacter> enemyTeam)
        {
            EnsureManagersInitialized();

            _playerTeam = playerTeam;
            _enemyTeam = enemyTeam;
            _initialPlayerTeam = new List<CombatCharacter>(playerTeam);
            _rng = new System.Random();
            CurrentRound = 0;

            // Subscribe to defeat events and inject config
            foreach (var c in _playerTeam.Concat(_enemyTeam))
            {
                _lifecycleManager.SubscribeCharacter(c);
            }

            // Activate traits now that BattleSystem reference is available
            foreach (var c in _playerTeam.Concat(_enemyTeam))
            {
                c.ActivateTraits(this);
            }

            CurrentState = BattleState.RoundStart;
            OnBattleStarted?.Invoke();

            StartCoroutine(BattleLoop());
        }

        private IEnumerator BattleLoop()
        {
            while (CurrentState != BattleState.BattleEnd)
            {
                switch (CurrentState)
                {
                    case BattleState.RoundStart:
                        yield return StartRound();
                        break;

                    case BattleState.CharacterTurn:
                        yield return ProcessTurn();
                        break;

                    case BattleState.RoundEnd:
                        yield return EndRound();
                        break;
                }

                yield return null;
            }
        }

        private IEnumerator StartRound()
        {
            CurrentRound++;
            BuildTurnOrder();
            _currentTurnIndex = 0;

            OnRoundStarted?.Invoke(CurrentRound);
            Debug.Log($"[BattleSystem] === Round {CurrentRound} Start === Turn order: " +
                      string.Join(", ", _turnOrder.Select(t => $"{t.character.DisplayName}(spd:{t.speed})")));

            // Wait for any round-start animations (e.g., boss telegraph)
            if (animationQueue != null)
            {
                while (animationQueue.IsBusy)
                    yield return null;
            }

            if (CheckBattleEnd()) yield break;

            CurrentState = BattleState.CharacterTurn;
            yield return null;
        }

        /// <summary>
        /// Build turn order by Speed. Ties: enemies before players, then front rank first.
        /// </summary>
        private void BuildTurnOrder()
        {
            _turnOrder = TurnOrderBuilder.Build(_playerTeam, _enemyTeam, GameDatabase.Instance.CombatConfig, _rng);
        }

        private IEnumerator ProcessTurn()
        {
            if (CheckBattleEnd()) yield break;

            if (_currentTurnIndex >= _turnOrder.Count)
            {
                CurrentState = BattleState.RoundEnd;
                yield break;
            }

            TurnEntry entry = _turnOrder[_currentTurnIndex];
            CurrentActor = entry.character;

            // Skip dead characters
            if (!CurrentActor.IsAlive)
            {
                _currentTurnIndex++;
                yield break;
            }

            OnTurnStarted?.Invoke(CurrentActor);
            Debug.Log($"[BattleSystem] Turn: {CurrentActor.DisplayName} (Rank {CurrentActor.rank})");

            // Phase 1: Apply DOT/HOT effects (bleed/blight still hurt stunned characters)
            StatusProcessor.ProcessPeriodicEffects(CurrentActor);

            // Wait for DOT/HOT animations to finish before proceeding
            if (animationQueue != null)
            {
                while (animationQueue.IsBusy) yield return null;
            }

            // Check if died from DOT or if battle ended during animations
            if (CheckBattleEnd()) yield break;

            // Check death from DOT (redundant with CheckBattleEnd but kept for turn index increment)
            if (!CurrentActor.IsAlive)
            {
                StatusProcessor.TickDurations(CurrentActor, GameDatabase.Instance.CombatConfig.stunRecoveryResistBonus);
                _currentTurnIndex++;
                yield break;
            }

            // Check stun (before ticking durations so stun correctly skips the turn)
            if (CurrentActor.isStunned)
            {
                Debug.Log($"[BattleSystem] {CurrentActor.DisplayName} is stunned! Skipping turn.");
                StatusProcessor.TickDurations(CurrentActor, GameDatabase.Instance.CombatConfig.stunRecoveryResistBonus);
                _currentTurnIndex++;
                yield break;
            }

            // Phase 2: Tick status durations and remove expired
            StatusProcessor.TickDurations(CurrentActor, GameDatabase.Instance.CombatConfig.stunRecoveryResistBonus);

            // Player or Enemy action
            if (CurrentActor.IsPlayerTeam)
            {
                yield return WaitForPlayerAction();
            }
            else
            {
                yield return ExecuteEnemyAction();
            }

            // Wait for all queued animations to finish before advancing
            if (animationQueue != null)
            {
                while (animationQueue.IsBusy)
                {
                    yield return null;
                }
            }


            // Check if battle ended during the action
            if (CheckBattleEnd())
            {
                yield break;
            }

            _currentTurnIndex++;
        }

        private IEnumerator WaitForPlayerAction()
        {
            _waitingForPlayerInput = true;
            OnWaitingForPlayerInput?.Invoke();

            // Wait until player submits an action
            while (_waitingForPlayerInput)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Called by UI when player selects a skill and targets.
        /// </summary>
        public void SubmitPlayerAction(SkillData skill, List<CombatCharacter> targets)
        {
            if (!_waitingForPlayerInput) return;

            ExecuteSkill(CurrentActor, skill, targets);
            _waitingForPlayerInput = false;
        }

        /// <summary>
        /// Called by UI when player uses the Move action. Selects target rank to move to.
        /// </summary>
        public void SubmitMoveAction(CombatCharacter target)
        {
            if (!_waitingForPlayerInput) return;

            ExecuteMoveAndShift(CurrentActor, target.rank);
            _waitingForPlayerInput = false;
        }

        public void ExecuteMoveAndShift(CombatCharacter mover, int targetRank)
        {
            _formationManager.ExecuteMoveAndShift(mover, targetRank, mover.IsPlayerTeam ? _playerTeam : _enemyTeam);
        }

        public float GetXPositionForRank(Team team, int rank)
        {
            return _formationManager.GetXPositionForRank(team, rank);
        }

        public float GetXPositionForCharacter(CombatCharacter character)
        {
            return _formationManager.GetXPositionForCharacter(character);
        }

        /// <summary>
        /// Called by UI when player chooses to pass their turn.
        /// </summary>
        public void SubmitPassAction()
        {
            if (!_waitingForPlayerInput) return;

            Debug.Log($"[BattleSystem] {CurrentActor.DisplayName} passes their turn.");

            // Enqueue visual pass animation length
            if (animationQueue != null)
            {
                animationQueue.Enqueue(new WaitTimerStep(
                    $"{CurrentActor.DisplayName} Pass",
                    0.3f));
            }

            _waitingForPlayerInput = false;
        }

        private IEnumerator ExecuteEnemyAction()
        {
            yield return new WaitForSeconds(0.5f); // Brief delay for readability

            var brain = CurrentActor.GetComponent<Nevergreen.Combat.AI.AIBrain>();
            if (brain == null)
            {
                Debug.LogError($"[BattleSystem] {CurrentActor.DisplayName} is missing an AIBrain component. Passing.");
                yield break;
            }

            var decision = brain.EvaluateTurn(this);

            // Record the decision in the character's history
            brain.RecordDecision(decision);

            if (decision.isPass)
            {
                Debug.Log($"[BattleSystem] {CurrentActor.DisplayName} AI decided to pass.");

                // Enqueue visual pass animation length
                if (animationQueue != null)
                {
                    animationQueue.Enqueue(new WaitTimerStep($"{CurrentActor.DisplayName} Pass", 0.3f));
                }
            }
            else
            {
                Debug.Log($"[BattleSystem] Enemy {CurrentActor.DisplayName} executes {decision.skill.displayName}");
                QueueEnemySkill(CurrentActor, decision.skill, decision.targets);
            }
        }

        public void QueueEnemySkill(CombatCharacter user, SkillData skill, List<CombatCharacter> targets)
        {
            EnsureManagersInitialized();
            _skillExecutor.QueueEnemySkill(user, skill, targets, _rng);
        }

        public void ExecuteSkill(CombatCharacter user, SkillData skill, List<CombatCharacter> targets)
        {
            EnsureManagersInitialized();
            _skillExecutor.Execute(user, skill, targets, _rng);
        }



        public List<CombatCharacter> GetValidTargets(CombatCharacter user, SkillData skill)
        {
            EnsureManagersInitialized();
            return TargetResolver.GetValidTargets(user, skill, _playerTeam, _enemyTeam);
        }

        public List<CombatCharacter> GetAOETargets(SkillData skill, CombatCharacter primaryTarget)
        {
            EnsureManagersInitialized();
            return TargetResolver.GetAOETargets(skill, primaryTarget, _playerTeam, _enemyTeam);
        }

        public bool CheckBattleEnd()
        {
            EnsureManagersInitialized();
            
            if (CurrentState == BattleState.BattleEnd) return true;

            var outcome = BattleOutcomeEvaluator.Evaluate(_playerTeam, _enemyTeam, _initialPlayerTeam, out string reason);
            if (outcome == BattleOutcome.Defeat)
            {
                CurrentState = BattleState.BattleEnd;
                Debug.Log($"[BattleSystem] === DEFEAT ({reason}) ===");
                OnBattleEnded?.Invoke(BattleOutcome.Defeat);
                return true;
            }
            else if (outcome == BattleOutcome.Victory)
            {
                CurrentState = BattleState.BattleEnd;
                Debug.Log("[BattleSystem] === VICTORY ===");

                var config = GameDatabase.Instance.CombatConfig;
                var tier = config != null ? config.GetEncounterTierForRoom(Nevergreen.RunSessionManager.RoomProgression) : EnemyEncounterTier.Trivial;

                BattleRewardHandler.ApplyVictoryRewards(_playerTeam, config, tier, _rng, out int partsGranted, out int scrapsGranted);
                PartsGrantedThisBattle = partsGranted;
                ScrapsGrantedThisBattle = scrapsGranted;
                
                if (partsGranted > 0 || scrapsGranted > 0)
                {
                    Debug.Log($"[BattleSystem] Awarded {PartsGrantedThisBattle} Parts and {ScrapsGrantedThisBattle} Scraps. Total: {Nevergreen.RunSessionManager.Parts} Parts, {Nevergreen.RunSessionManager.Scraps} Scraps.");
                }

                OnBattleEnded?.Invoke(BattleOutcome.Victory);
                return true;
            }

            return false;
        }

        private IEnumerator EndRound()
        {
            Debug.Log($"[BattleSystem] === Round {CurrentRound} End ===");

            // Tick durations for all Piles at the end of each round
            foreach (var c in _playerTeam.Concat(_enemyTeam).Where(c => c.IsPile).ToList())
            {
                c.pileDuration--;

                // If the Pile has decayed, move to Destroyed state
                if (c.pileDuration <= 0)
                {
                    c.state = LifeState.Destroyed;
                    Debug.Log($"[BattleSystem] {c.DisplayName} Pile has decayed and is now Destroyed.");
                }
            }

            OnRoundEnded?.Invoke(CurrentRound);

            // Wait for any round-end animations (e.g., boss strike)
            if (animationQueue != null)
            {
                while (animationQueue.IsBusy)
                    yield return null;
            }

            if (CheckBattleEnd()) yield break;

            CurrentState = BattleState.RoundStart;
            yield return null;
        }

        public void CompactFormation(List<CombatCharacter> team)
        {
            EnsureManagersInitialized();
            _formationManager.CompactFormation(team);
        }

        public event Action<CombatCharacter> OnCharacterSpawned;

        public void RegisterSpawnedCharacter(CombatCharacter character)
        {
            EnsureManagersInitialized();
            _lifecycleManager.RegisterSpawnedCharacter(character);
        }

        private void OnDestroy()
        {
            foreach (var c in _playerTeam.Concat(_enemyTeam))
            {
                _lifecycleManager.UnsubscribeCharacter(c);
                c.DeactivateAllTraits();
            }
        }
    }

    public enum BattleState
    {
        Inactive,
        RoundStart,
        CharacterTurn,
        RoundEnd,
        BattleEnd
    }

    public enum BattleOutcome
    {
        Victory,
        Defeat
    }
}
