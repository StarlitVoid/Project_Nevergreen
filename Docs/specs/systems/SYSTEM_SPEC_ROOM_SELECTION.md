# Room Selection and Effect Strategies System

> **Owner:** Gameplay Engineering Team | **Last Updated:** 2026-08-31 | **Status:** Active

## Purpose
Decouple room definition and metadata from execution behavior using ScriptableObjects and polymorphic execution strategies. Centralize room probability rules within `RoomDatabase` using `RoomPoolEntry`, and implement a deferred room choice generation timing model where post-combat room UI controllers explicitly signal completion before room choices are evaluated and rendered.

## Scope
- In scope: room metadata representation, Strategy pattern serialization via `[SerializeReference]` and `[SubclassSelector]`, centralized probability rule configuration (`RoomSelectionRule`), independent weighted random room sampling with replacement (`WeightedRoomSelector`), deferred room choice UI timing model, run session next-room persistence, bootstrap interception, and battle setup bypass.
- Out of scope: multiplayer synchronization, custom animations for selection buttons, specific combat character layout mapping.

## Source of Truth
- Code:
  - `Assets/Scripts/Data/RoomData.cs` (`Nevergreen.Data.RoomData`)
  - `Assets/Scripts/Data/RoomPoolEntry.cs` (`Nevergreen.Data.RoomPoolEntry`)
  - `Assets/Scripts/Data/RoomSelectionRule.cs` (`Nevergreen.Data.RoomSelectionRule`)
  - `Assets/Scripts/Data/RoomSelectionRules/FixedWeightRule.cs` (`Nevergreen.Data.FixedWeightRule`)
  - `Assets/Scripts/Data/RoomSelectionRules/PartyCountRule.cs` (`Nevergreen.Data.PartyCountRule`)
  - `Assets/Scripts/Data/RoomSelectionRules/ProgressionScaledRule.cs` (`Nevergreen.Data.ProgressionScaledRule`)
  - `Assets/Scripts/Data/WeightedRoomSelector.cs` (`Nevergreen.Data.WeightedRoomSelector`)
  - `Assets/Scripts/Data/RoomActivationType.cs` (`Nevergreen.Data.RoomActivationType`)
  - `Assets/Scripts/Data/RoomEffectStrategy.cs` (`Nevergreen.Data.RoomEffectStrategy`)
  - `Assets/Scripts/Data/CombatRoomEffectStrategy.cs` (`Nevergreen.Data.CombatRoomEffectStrategy`)
  - `Assets/Scripts/Data/MarionetteRoomEffectStrategy.cs` (`Nevergreen.Data.MarionetteRoomEffectStrategy`)
  - `Assets/Scripts/Data/RoomDatabase.cs` (`Nevergreen.Data.RoomDatabase`)
  - `Assets/Scripts/Data/GlobalConfig.cs` (`Nevergreen.Data.GlobalConfig`)
  - `Assets/Scripts/Data/RunSessionManager.cs` (`Nevergreen.Data.RunSessionManager`)
  - `Assets/Scripts/Prototype/CombatSceneBootstrap.cs` (`Nevergreen.Prototype.CombatSceneBootstrap`)
  - `Assets/Scripts/Prototype/CombatUI.cs` (`Nevergreen.Prototype.CombatUI`)
  - `Assets/Scripts/UI/MarionetteSelectionController.cs` (`Nevergreen.UI.MarionetteSelectionController`)
  - `Assets/Scripts/UI/TheatreUIController.cs` (`Nevergreen.UI.TheatreUIController`)
- Tests:
  - `Assets/Editor/Tests/WeightedRoomSelectorTests.cs` (`Nevergreen.Tests.WeightedRoomSelectorTests`)
  - `Assets/Editor/Tests/RoomEffectTests.cs` (`Nevergreen.Tests.RoomEffectTests`)
  - `Assets/Editor/Tests/CombatUITests.cs` (`Nevergreen.Tests.CombatUITests`)
- Design:
  - `Docs/specs/architecture/ARCHITECTURE_SPEC_COMBAT_RUNTIME.md`
  - `Docs/specs/systems/SYSTEM_SPEC_COMBAT_SCREEN.md`

## Responsibilities
- Encapsulate room details (name, description, activation timing, execution strategy) inside `RoomData`.
- Centralize room selection rules and weights in `RoomDatabase` using `RoomPoolEntry` pairs (combining a `RoomData` reference with a polymorphic `RoomSelectionRule`).
- Defer room choice generation when an `OnCombatVictory` room effect is pending, allowing UI panels (e.g. Marionette selection, Theatre interaction) to complete state modifications before room choices are evaluated.
- Evaluate selection rules dynamically against current run session state (e.g. `PartyCountRule` scaling weight inversely with party size) upon explicit room completion.
- Perform independent weighted random sampling with replacement via `WeightedRoomSelector.SelectRooms()`.
- Maintain the next-room state across scene transitions in `RunSessionManager.NextRoomData`.
- Intercept scene load transitions in `CombatSceneBootstrap.Start()` to handle immediate activation (`OnRoomLoaded`) and early return (bypassing combat).
- Execute lingering strategies during continuous combat (`ContinuousCombat`) after team initialization.
- Listen to battle outcome events (`OnBattleEnded`) in `RunSessionManager` to execute victory-activated strategies (`OnCombatVictory`).

## Data Model
- `RoomPoolEntry`: Class containing `RoomData room` reference and `RoomSelectionRule selectionRule` serialized with `[SerializeReference]` and `[SubclassSelector]`.
- `RoomSelectionRule`: Abstract base class with concrete implementations:
  - `FixedWeightRule`: Returns a constant configured weight.
  - `PartyCountRule`: Scales weight inversely based on current party count vs `CombatConfig.maxPartySize`.
  - `ProgressionScaledRule`: Scales weight based on current `RunSessionManager.RoomProgression`.
- `RoomActivationType`: enum containing `OnRoomLoaded`, `ContinuousCombat`, `OnCombatVictory`.
- `RoomEffectStrategy`: Abstract base class decorated with `[Serializable]`, defining `ExecuteRoomEffect()`.
- `CombatRoomEffectStrategy`: Concrete "no-op" strategy for plain combat rooms that immediately signals room completion via `combatUI.ShowRoomSelectionImmediately()`.
- `MarionetteRoomEffectStrategy`: Concrete implementation for marionette acquisition, opening `Marionette_Selection_Screen`.
- `ScrapsRewardRoomEffectStrategy`: Concrete implementation for Scraps/Salvage rooms, executes `OnCombatVictory` to display a dedicated Scraps popup and grant currency before signaling completion.
- `RoomData`: ScriptableObject asset containing `roomId` (string), `roomName` (string), `description` (TextArea string), `activationType` (RoomActivationType), and `strategy` (RoomEffectStrategy).
- `RoomDatabase`: ScriptableObject containing `availableRooms` (`List<RoomPoolEntry>`), `bossRoom` (`RoomData`), and `healRoom` (`RoomData`).
- `GlobalConfig`: ScriptableObject containing `roomChoiceCount` (int).
- `RunSessionManager`: Static class tracking `NextRoomData` (RoomData), `NextRoomChoices` (`List<RoomData>`), and room state.

## Event Contracts
- Event: `BattleSystem.OnBattleEnded`
  - Producer: `BattleSystem`
  - Consumers: `RunSessionManager`, `CombatUI`
  - Payload schema: `BattleOutcome` (enum: `Victory`, `Defeat`)
- Event: Room Choice Button Click
  - Producer: Dynamic UI Buttons spawned in `CombatUI`
  - Consumers: `RunSessionManager` (assigns `NextRoomData`), Scene Manager (triggers reload of the current combat scene)
- Signal: `CombatUI.ShowRoomSelectionImmediately()`
  - Producer: Room UI Controllers (`MarionetteSelectionController` on Confirm/Skip, `TheatreUIController` on Complete, `CombatRoomEffectStrategy`)
  - Consumers: `CombatUI` (evaluates rules, generates room choices, spawns selection buttons)

## Timing & Execution Model

### 1. Scene Load & Battle Phase
1. `CombatSceneBootstrap.Start()` runs upon entering `"CombatPrototype"`.
2. Check `NextRoomData.activationType == OnRoomLoaded`. If true, call `RunSessionManager.ActivateCurrentRoomEffect()`, clear `NextRoomData`, and return early (bypassing battle).
3. If false, initialize teams and battle. If `activationType == ContinuousCombat`, activate strategy immediately.
4. Battle proceeds to resolution (`BattleSystem` fires `OnBattleEnded`).

### 2. Post-Combat & Deferred Room Choice Generation
1. `RunSessionManager.OnBattleEnded` handles victory outcome and saves run state, but defers room effect execution.
2. `CombatUI.HandleBattleEnded` displays the `CombatRewardUI` popup showing earned Parts and Scraps.
3. When the player clicks **Close** on the `CombatRewardUI` popup, `CombatUI.OnRewardPopupClosed()` checks if `NextRoomData.activationType == RoomActivationType.OnCombatVictory`.
   - **If `OnCombatVictory` is true**: `CombatUI` calls `RunSessionManager.TriggerPendingVictoryRoomEffect()`.
     - The room's effect strategy executes (e.g. opening Marionette selection panel or Theatre panel) and `NextRoomData` is set to `null`.
     - The player interacts with the room UI panel.
     - Upon completion (or skip), the room's UI controller calls `combatUI.ShowRoomSelectionImmediately()`.
   - **If `OnCombatVictory` is false**: `CombatUI` calls `SpawnRoomChoiceButtons()` directly.
4. `SpawnRoomChoiceButtons()` calls `WeightedRoomSelector.SelectRooms(availableRooms, roomChoiceCount, _rng)`.
   - Selection rules evaluate against current up-to-date session state (e.g., party size *after* marionette acquisition).
   - `WeightedRoomSelector` performs **independent weighted random sampling with replacement**.
   - Choice buttons are rendered in `CombatUI`.

## Determinism & Random Sampling
- `WeightedRoomSelector.SelectRooms()` uses weighted random sampling **with replacement** (independent rolls).
- Each choice button is rolled independently based on the candidate entries' evaluated weights.
- Candidates with weight <= 0 are excluded from selection.
- Duplicate room choices can appear if rolled multiple times.
- Uses `System.Random` instance seeded for reproducibility.

## Authority Model
- Single-player/offline: All room selection state, strategy executions, and UI prefab instantiations occur locally on the client machine.

## Performance Budget
- CPU: Strategy execution and room choice generation execute within a single frame (< 16ms).
- Memory: UI Prefab instantiation avoids allocations exceeding 1MB per room choice generation.

## Error Handling and Recovery
- Strategy is null: Logged as warning inside `RoomData.ActivateEffect()`.
- Missing UI Controller / CombatUI reference: `MarionetteSelectionController` and `CombatRoomEffectStrategy` fall back to `RunSessionManager.CompleteRoom()` if `CombatUI` is missing.
- Missing Config/Rooms: `CombatUI` falls back to default `nextRoomButton`.

## Observability
- Logs: Warns on missing strategy reference (`[RoomData] Room '{roomName}' has no strategy assigned`), logs room completion and selection button generation.

## Acceptance Tests
- Automated:
  - `Assets/Editor/Tests/WeightedRoomSelectorTests.cs`:
    - `SelectRooms_UniformWeights_ReturnsRequestedCount`
    - `SelectRooms_ZeroWeight_ExcludesRoom`
    - `SelectRooms_NullRule_DefaultsToWeightOne`
    - `SelectRooms_HigherWeight_IsSelectedMoreOften`
    - `SelectRooms_CanReturnDuplicates`
    - `SelectRooms_PoolSmallerThanCount_ReturnsRequestedCount`
    - `SelectRooms_EmptyPool_ReturnsEmpty`
    - `PartyCountRule_FewMarionettes_HigherWeight`
    - `PartyCountRule_FullParty_BaseWeight`
    - `ProgressionScaledRule_IncreasesWithProgression`
    - `FixedWeightRule_ReturnsConfiguredValue`
  - `Assets/Editor/Tests/RoomEffectTests.cs`:
    - `Initialize_AssignsMarionetteRoomToNextRoomData`
    - `RoomDatabase_AvailableRooms_DefaultIsEmpty`
    - `RoomEffect_OnCombatVictory_TriggersEffect`
  - `Assets/Editor/Tests/SaveManagerTests.cs`:
    - `SaveRun_RoomCompleted_PersistsSelectionAndState`
- Playtest:
  - Open `RoomDatabase.asset` in Unity Inspector. Configure `availableRooms` entries with `RoomData` references and `RoomSelectionRule` instances.
  - Complete a combat encounter in an `OnCombatVictory` room (e.g. Marionette Room). Confirm room selection buttons do not spawn under the panel.
  - Complete or skip the Marionette selection screen. Confirm room choice buttons spawn immediately after panel closes.

## Validation
- [x] Facts match current code/content
- [x] Timing and determinism assumptions are explicit
- [x] Unknowns are explicitly labeled
- [x] Links and paths resolve
- [x] Acceptance tests are defined

