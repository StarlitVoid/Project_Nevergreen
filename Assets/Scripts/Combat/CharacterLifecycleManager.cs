using System;
using System.Collections.Generic;
using UnityEngine;
using Nevergreen.Data;

namespace Nevergreen.Combat
{
    /// <summary>
    /// Manages the lifecycle of combat characters, including death, pile conversion, destruction, and dynamic spawning.
    /// </summary>
    public class CharacterLifecycleManager
    {
        public event Action<CombatCharacter> OnCharacterDefeated;
        public event Action<CombatCharacter> OnCharacterRemoved;
        public event Action<CombatCharacter> OnCharacterSpawned;

        private FormationManager _formationManager;
        private AnimationQueueProcessor _animationQueue;
        private BattleSystem _battleSystem; // To trigger CheckBattleEnd

        public void Initialize(FormationManager formationManager, AnimationQueueProcessor animationQueue, BattleSystem battleSystem)
        {
            _formationManager = formationManager;
            _animationQueue = animationQueue;
            _battleSystem = battleSystem;
        }

        public void SubscribeCharacter(CombatCharacter character)
        {
            character.OnDefeated += HandleCharacterDefeated;
            character.OnStateChanged += HandleCharacterStateChanged;
        }

        public void UnsubscribeCharacter(CombatCharacter character)
        {
            character.OnDefeated -= HandleCharacterDefeated;
            character.OnStateChanged -= HandleCharacterStateChanged;
        }

        public void RegisterSpawnedCharacter(CombatCharacter character)
        {
            if (character == null) return;

            var team = character.IsPlayerTeam ? _battleSystem.PlayerTeam : _battleSystem.EnemyTeam;
            if (!team.Contains(character))
            {
                team.Add(character);
            }

            SubscribeCharacter(character);
            character.ActivateTraits(_battleSystem);

            OnCharacterSpawned?.Invoke(character);
        }

        private void HandleCharacterDefeated(CombatCharacter character, bool wasCritical)
        {
            Debug.Log($"[CharacterLifecycleManager] {character.DisplayName} has been defeated!{(wasCritical ? " (CRITICAL KILL)" : "")}");

            // 1. Enqueue the death animation and sound
            if (_animationQueue != null && Application.isPlaying)
            {
                var deathParallel = new ParallelStep($"{character.DisplayName} Die Parallel");

                if (character.animator != null)
                {
                    deathParallel.AddStep(new AnimatorStep($"{character.DisplayName} Die", character.animator, "Die", 1.5f));
                }

                if (character.characterData != null && character.characterData.deathSFX != null)
                {
                    deathParallel.AddStep(new PlaySoundStep(character.characterData.deathSFX));
                }

                if (character.animator != null || (character.characterData != null && character.characterData.deathSFX != null))
                {
                    _animationQueue.Enqueue(deathParallel);
                }
            }

            // 3. Enqueue deferred transition (runs right after death animation finishes)
            if (_animationQueue != null && Application.isPlaying)
            {
                _animationQueue.Enqueue(new ActionStep($"{character.DisplayName} Spawn Pile", () =>
                {
                    FinalizeCharacterDefeat(character, wasCritical);
                }));
            }
            else
            {
                FinalizeCharacterDefeat(character, wasCritical);
            }

            OnCharacterDefeated?.Invoke(character);
        }

        private void FinalizeCharacterDefeat(CombatCharacter character, bool wasCritical)
        {
            bool canFormPile = character.characterData != null && character.characterData.leavesPileOnDeath;
            string displayName = character.DisplayName;

            if (wasCritical || !canFormPile)
            {
                character.state = LifeState.Destroyed;
                Debug.Log($"[CharacterLifecycleManager] {displayName} is destroyed (no Pile formed).");
            }
            else
            {
                character.state = LifeState.Pile;
                character.currentHP = character.baseStats.maxHP / 2;
                
                int defaultDuration = 4;
                var config = GameDatabase.Instance != null ? GameDatabase.Instance.CombatConfig : null;
                if (config != null)
                {
                    defaultDuration = config.pileDuration;
                }
                character.pileDuration = defaultDuration;

                // Clear all previous status effects (Bleeds, Buffs, etc.)
                character.statusEffects.Clear();

                Debug.Log($"[CharacterLifecycleManager] {character.DisplayName} has become a Pile. HP: {character.currentHP}, Duration: {character.pileDuration}");
            }
        }

        private void HandleCharacterStateChanged(CombatCharacter character, LifeState newState)
        {
            if (newState == LifeState.Destroyed)
            {
                HandleCharacterDestroyed(character);
            }
        }

        private void HandleCharacterDestroyed(CombatCharacter character)
        {
            Debug.Log($"[CharacterLifecycleManager] {character.DisplayName} has been destroyed and removed from battle.");

            var team = character.IsPlayerTeam ? _battleSystem.PlayerTeam : _battleSystem.EnemyTeam;

            // 1. Remove from team list
            team.Remove(character);

            // 2. Compact formation (handles any gap size from multi-rank characters)
            _formationManager.CompactFormation(team);

            // 3. Enqueue physical destruction (after small delay for any lingering effects/animations)
            if (_animationQueue != null && Application.isPlaying)
            {
                _animationQueue.Enqueue(new ActionStep($"{character.DisplayName} Cleanup", () =>
                {
                    UnityEngine.Object.Destroy(character.gameObject);
                }));
            }
            else
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(character.gameObject);
                else UnityEngine.Object.DestroyImmediate(character.gameObject);
            }

            // 4. Notify external systems
            OnCharacterRemoved?.Invoke(character);

            // 5. Check battle end (in case this was the last character)
            _battleSystem.CheckBattleEnd();
        }
    }
}
