# Lessons Learned

## Communication & Workflow

### 1. Investigation vs. Implementation (CRITICAL)
- **Pattern**: User explicitly requests "investigation" and "proposal" without "implementation".
- **Rule**: STOP after providing the analysis and list of steps. DO NOT call any file-modifying tools (`replace_file_content`, `write_to_file`, etc.) targeting source code until the user gives the "Go ahead."
- **Failure Analysis**: I ignored a pre-existing lesson on this topic. I must re-read `lessons.md` at the start of EVERY new task to internalize current project-specific failures.

### 2. Pre-Edit Scope Check
- **Rule**: Before every `replace_file_content` or `write_to_file` call, verify: "Did the user ask me to make this change, or am I assuming they want it now?"

## Combat & Formation Mechanics

### 3. Implicit Anchoring (Pile Mechanic)
- **Pattern**: Assuming dead characters should be ignored by spatial systems (ranks, formations).
- **Rule**: In a formation-based game, corpses often occupy slots and should be subject to displacement. Don't assume `!IsAlive` means "ignore for movement logic" unless explicitly required.
- **Verification**: When implementing formation-related mechanics, always test with a mix of alive and dead characters to ensure the formation remains coherent.

### 4. IsAlive Over-usage
- **Rule**: Avoid using `!character.IsAlive` as a blanket filter for turn logic, status logic, AND spatial logic. Spatial logic should usually consider all entities in the formation to maintain spatial integrity.

## Technical & Project Structure

### 5. C# Namespace Visibility in Subfolders
- **Pattern**: Creating new sub-namespaces (e.g., `Nevergreen.Combat.AI`) inside a parent folder (`Nevergreen.Combat`).
- **Rule**: Even if a namespace is a sub-namespace of another, you must explicitly include `using` directives for the parent namespace if they reside in different files or folders. Don't assume visibility based on folder structure alone.
- **Verification**: Always check for `CS0246` (type or namespace not found) immediately after creating new files in new directories.

### 6. Reuse Calculator Functions
- **Pattern**: Duplicating calculation logic (such as hit resolution) in multiple helper methods.
- **Rule**: Always prioritize reusing functions from centralized calculation classes (like `CombatCalculator`). If a calculation class function assumes non-null configurations or parameters that might be null in tests, refactor that function to gracefully support null/default fallbacks rather than copying the logic elsewhere.

### 7. AOE Healing Target Rules for Piles
- **Pattern**: Interpreting "piles are not counted as targets when it comes to AOE healing skills" as "piles should be skipped (propagation bypasses the pile to find the next alive target)".
- **Rule**: "Not counted as targets when it comes to AOE healing skills" actually means: the pile *is* included in the targets returned by AOE target selection (absorbing a target slot and blocking propagation further back), but no healing effect is applied to it (i.e. the heal effect is ignored/refused). It does *not* mean the targeting logic should skip the pile to target an alive character behind it.
- **Verification**: Always clarify targeting rules regarding whether "exclude" or "ignore" means *skip during selection* or *select but apply no effect*.

### 8. Object Reference Identity across Scene Boundaries
- **Pattern**: Using `==` checks on `ScriptableObject` references (like `characterData == RunSessionManager.CurrentParty[0].character`) to link data loaded from a Main Menu UI with data on instantiated prefabs in a Combat Scene.
- **Rule**: Unity does not guarantee that a `ScriptableObject` loaded directly by UI logic shares the same memory instance (InstanceID) as a `ScriptableObject` embedded in a prefab instantiated in a different scene. This causes equality checks to silently fail. 
- **Verification**: Always prefer explicit data injection (e.g., passing `PartyMemberInfo` directly into `InitializeForCombat`) over static array lookups based on object reference equality when crossing scene or instantiation boundaries.

### 9. Simplify API Modifications (Direct Refactoring)
- **Pattern**: Creating a new method and routing the old method to it.
- **Rule**: If the call sites are limited and the refactoring achieves the same functionality, directly modify the existing method signature/arguments instead of introducing wrappers or forwarding logic. This keeps the API clean and avoids duplicate logic pathways.

### 10. Clean List Initialization
- **Pattern**: Creating a temporary variable, performing a null check, and calling `AddRange` to populate a list field that was cleared right before.
- **Rule**: Prefer direct assignment with a null-coalescing operator (`?? new List<T>()`) to handle possible null returns. This is cleaner, avoids temporary variables, and prevents potential `NullReferenceException` values down the line.

### Test Teardown Safety and Assets
- **Pattern**: Using Object.DestroyImmediate(obj, true) in test teardowns is extremely dangerous if the object reference could potentially point to a real project asset loaded via AssetDatabase or Resources.Load. The 	rue parameter stands for  llowDestroyingAssets and will literally wipe the file contents from the disk.
- **Solution**: Always guard asset destruction in test teardowns with if (!UnityEditor.EditorUtility.IsPersistent(obj)) to guarantee you are only ever destroying transient in-memory mocks, never real files.

### Test Runner Class Name Filter
- **Pattern**: When using the Unity Test Runner (via the MCP `tests-run` tool), specify the **short class name** (e.g., `PartyManagementPanelTests`) rather than the fully qualified name (e.g., `Nevergreen.Tests.PartyManagementPanelTests`) for the `testClass` parameter.
- **Rule**: Using the fully qualified name may result in a "No tests found" error.

### GameDatabase.Initialize vs SetInstanceForTesting
- **Pattern**: GameDatabase.Initialize() sets _bypassAutoDiscovery = false, meaning the next Instance access after cleanup will auto-load the real persistent asset from disk. Always use SetInstanceForTesting() in tests, which properly sets the bypass flag.
- **Rule**: NEVER use GameDatabase.Initialize() in test code. Always use GameDatabase.SetInstanceForTesting().

### Reflection for Property Setters is Fragile
- **Pattern**: Using typeof(X).GetProperty(...).SetValue() to set private setters is fragile and silently fails on rename.
- **Solution**: Change private set to internal set when the setter needs to be accessed by same-assembly code (e.g., SaveManager setting RunSessionManager.LastSelectedFormation).

### 11. Avoid LaTeX Notation in Markdown Artifacts
- **Pattern**: Using LaTeX mathematical formatting (such as `$$formula$$` or `$\text{formula}$`) in markdown documents.
- **Rule**: Avoid LaTeX equations in walkthroughs and documentation artifacts as the default markdown renderers (including the local UI) may not support LaTeX rendering and will display the raw code literally, rendering it unreadable.
- **Solution**: Use standard inline code blocks (e.g., `a * b = c`) or code block quotes to display formulas clearly.

### 12. Ground-Telegraphed Hazard Targeting vs. Unit-Targeted AOE
- **Pattern**: Converting location-targeted hazards or ground telegraph skills to use the game's standard unit-targeted AOE system (`GetAOETargets`).
- **Rule**: Do NOT use standard unit-targeted AOE expansion (e.g., `GetAOETargets`) for ground hazards or telegraphed rank mechanics. Because unit-targeted AOE propagates dynamically from a primary target to adjacent ranks up to `maxTargets`, it can hit characters in non-telegraphed safe ranks, violating the visual boundaries shown to the player. Ground hazards must always target and affect only the exact telegraphed ranks/locations.

### 13. System and Mechanic Spec Placement and Naming Conventions
- **Pattern**: Creating new design specification documents in the root of the `Docs/specs/` directory or with arbitrary file names.
- **Rule**: All specification documents MUST follow the project's folder layout and naming conventions:
  - Place specifications in the correct category folders (e.g., `Docs/specs/systems/` for systems, `Docs/specs/mechanics/` for mechanics, `Docs/specs/architecture/` for architecture).
  - Follow the exact filename prefix matching the template used (e.g., `SYSTEM_SPEC_<NAME>.md`, `MECHANIC_SPEC_<NAME>.md`, `ARCHITECTURE_SPEC_<NAME>.md` in all caps).

### 14. CreateAssetMenu Submenu Consistency for Databases
- **Pattern**: Placing registry database assets (e.g., `TrinketDatabase`) under the generic `"Nevergreen/Data/"` menu folder.
- **Rule**: All database registry ScriptableObjects (such as `TrinketDatabase`, `RoomDatabase`, `GameDatabase`) should be placed under the `"Nevergreen/Databases/"` submenu to ensure a consistent, clean asset creation menu in the Unity Editor.
- **Verification**: Check the `[CreateAssetMenu]` attribute's `menuName` field to ensure database-related assets use `"Nevergreen/Databases/"` as the parent path rather than `"Nevergreen/Data/"`.

### 15. Skill Animation Reference and Fallback Design
- **Pattern**: Choosing string-based state names over direct asset references (`AnimationClip`) for ScriptableObjects when authoring animation data.
- **Rule**: When adding animation fields to ScriptableObjects (like `SkillData`), prefer direct `AnimationClip` object references in the Inspector over raw string state names for stronger type safety and drag-and-drop authoring. Ensure missing asset references log clear diagnostic errors (`Debug.LogError`) while retaining graceful fallback behavior to generic animations.

### 16. Avoid Adding Unnecessary UI Elements Beyond Specific Requirements
- **Pattern**: Adding optional header/title fields (e.g. room name) to a UI tooltip when the requirement specifically requested displaying a single field (e.g. `description`).
- **Rule**: Stick strictly to requested data fields for UI components. Do not add optional extra text fields or header components unless explicitly requested or clearly specified in the design spec.

### 17. UI Visibility Bugs — Trace Parent Panel Activation, Not Just Children
- **Pattern**: Focusing on a placeholder child element inside a container when the real issue is the parent panel being activated too early.
- **Rule**: When investigating "panel appears too early" bugs, always trace the **full panel hierarchy activation chain** first. The fix is almost always about *when the parent container is activated*, not about hiding individual children. Ask: "What calls `SetActive(true)` on this panel, and should that call be conditional?"
