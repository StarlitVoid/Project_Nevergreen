# Pile (Corpse) Mechanic

Owner: Combat Engineering Team
Status: active
Last verified: 2026-05-08
Verified commit: HEAD
Target build: Unity 2022.3 + Windows

## Purpose
The Pile mechanic (also referred to as Corpses) prevents immediate formation collapse when a character
is defeated. By occupying a rank, a Pile maintains the spatial integrity of the remaining team, forcing
the opposition to either destroy the obstacle or use skills capable of targeting deeper ranks.

## Scope
- In scope: Creation conditions, rank occupancy, move resistance, HP properties, and removal rules.
- Out of scope: Specific visual art for piles, interaction with loot/rewards, or non-combat behaviors.

## Source of Truth
- Design: [Google Doc](https://docs.google.com/document/d/1DN-fIr9PG38hDRrMWJ5NrbWfTY-V7gf5Dz2cwSw3qUo/edit?usp=sharing) (Sections: Combat, Pile)
- Code: 
  - `Assets/Scripts/Combat/BattleSystem.cs`: Expiry (`EndRound`), and Targeting (`GetValidTargets`).
  - `Assets/Scripts/Combat/CharacterLifecycleManager.cs`: Creation (`FinalizeCharacterDefeat`).
  - `Assets/Scripts/Combat/CombatCharacter.cs`: State management (`LifeState`), Healing refusal (`Heal`), and Innate Move Resist (`GetEffectiveStats`).
  - `Assets/Scripts/Prototype/HPBar.cs`: Visual state representation (Gray color, scaled max HP).
- Tests: 
  - `Assets/Editor/Tests/PileMechanicTests.cs`: Core state transitions, healing refusal, and AOE interaction.
  - `Assets/Editor/Tests/MoveTests.cs`: Innate 300% Move Resistance.
  - `Assets/Editor/Tests/GuardTests.cs`: Guard status exclusions.
  - `Assets/Editor/Tests/BattleEndTests.cs`: Lose conditions involving Cecilia as a Pile.

## Inputs
- Input action: A combatant's HP reaching 0.
- Input conditions: The killing blow must NOT be a Critical Hit AND `CharacterData.leavesPileOnDeath` must be true.

## State Model
States:
- `Alive`: Character is active and participating in combat.
- `Dying`: Intermediate state during death animation; non-interactable.
- `Pile`: Character is defeated (`HP <= 0`) but still occupies a rank. 
- `Destroyed`: Character/Pile is removed from the team list and their GameObject is destroyed.
  Triggers immediate **Rank Shifting** for all characters in higher ranks.

Transitions:
1. `Alive` -> `Dying` immediately when `HP` reaches 0.
2. `Dying` -> `Pile` when death animation finishes AND `isCritical == false` AND `leavesPileOnDeath == true`.
3. `Dying` -> `Destroyed` when death animation finishes AND (`isCritical == true` OR `leavesPileOnDeath == false`).
4. `Pile` -> `Destroyed` when `HP` reaches 0 (as a Pile) OR after its duration (in rounds) expires.
5. Entering `Destroyed` state triggers `BattleSystem.HandleCharacterDestroyed` which removes the
   character from the `_playerTeam` or `_enemyTeam` list and initiates rank-shifting tweens.

## Timing Model
- Update domain: Round-based.
- Expiry: Piles decay by 1 duration unit at the end of **every round** in battle.
- Order dependencies: Pile expiry is checked in `BattleSystem.EndRound` before `OnRoundEnded`.

## Determinism
- Deterministic across clients: Yes.
- Sources of nondeterminism: Critical hit roll (determines if a Pile is created).
- Mitigation: Seeded RNG in `SkillContext`.

## Formulas
```txt
# Pile Health (Initialized upon creation)
PileMaxHP = OriginalCharacterMaxHP * 0.50

# Pile Move Resistance
EffectiveMoveResist = BaseMoveResist + 300
```

## Tuning Variables
| Variable | Default | Unit | Source |
| --- | --- | --- | --- |
| `Pile Move Resist` | 300 | % | `CombatCharacter.cs` |
| `Pile HP Multiplier` | 0.5 | factor | `BattleSystem.cs` |
| `Pile Duration` | 4 | rounds | `CombatConfig.cs` |

## Interaction Rules
- **Healing Refusal**: Piles reject all healing. `CombatCharacter.Heal` returns immediately if state is `Pile`.
- **Targeting**: 
  - Damage skills can target Piles.
  - Healing skills automatically exclude Piles from `GetValidTargets`.
- **Status Effects**: 
  - All status effects are cleared upon transition to `Pile`.
  - Piles cannot receive new status effects (e.g., Guard, Buffs).
- **Guard Restrictions**: 
  - A character cannot Guard a Pile.
  - A Pile cannot act as a Guardian.
- **UI representation**:
  - HP Bar fill color changes to Gray (`#808080`).
  - HP Bar `maxValue` is updated to reflect the 50% HP capacity.

## Edge Cases
- **Cecilia Defeat**: If Cecilia (CharacterId `ceci`) becomes a Pile, the battle end check triggers a Loss immediately.
- **Critical Finish**: Critical hits bypass the Pile state entirely, resulting in immediate destruction and formation shift.
- **AOE Healing**: Skills with `HealEffect` target Piles within their AOE propagation range, but the Piles refuse/ignore the healing effect (they do not receive HP).

## Acceptance Tests
- **Automated**:
  - `Pile_RefusesHealing`: Confirms HP does not change when healed.
  - `AOE_Healing_SkipsPiles`: Confirms Piles are ignored by party-wide heals.
  - `HealingSkill_CannotTargetPile`: Confirms Piles are filtered out of selection.
  - `AoeTargetingTests.GetAOETargets_DamagingSkill_IncludesPiles`: Confirms Piles are included in AOE damage targeting.
  - `AoeTargetingTests.GetAOETargets_HealingSkill_IncludesPilesButDoesNotHeal`: Confirms Piles are included in AOE healing targeting but do not receive heal effects.
  - `Pile_InnateMoveResist_Is300`: Confirms move resistance bonus.
  - `CheckBattleEnd_CeciliaIsPile_TriggersDefeat`: Confirms lose condition.
- **Playtest**: 
  - Verify HP bar turns gray and rescales correctly.
  - Verify Piles disappear after configured rounds (default: 4).

## Validation
- [x] Facts match current code/content
- [x] Timing and determinism assumptions are explicit
- [x] Tuning variables map to actual data/config
- [x] Unknowns are explicitly labeled
- [x] Acceptance tests are defined
