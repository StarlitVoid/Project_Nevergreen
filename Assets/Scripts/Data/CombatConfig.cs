using UnityEngine;

namespace Nevergreen.Data
{
    [System.Serializable]
    public class TierRewardProfile
    {
        public EnemyEncounterTier tier;
        [Tooltip("Minimum Parts awarded upon victory.")]
        public int minParts = 10;
        [Tooltip("Maximum Parts awarded upon victory.")]
        public int maxParts = 50;
        [Tooltip("Minimum Scraps awarded upon victory.")]
        public int minScraps = 5;
        [Tooltip("Maximum Scraps awarded upon victory.")]
        public int maxScraps = 25;
    }

    [System.Serializable]
    public struct RoomTierMapping
    {
        [Tooltip("The room progression count at which this tier begins (e.g., 1, 2, 4, 6).")]
        public int roomCount;

        [Tooltip("The encounter tier for this room count and beyond (until the next mapping).")]
        public EnemyEncounterTier tier;
    }

    [System.Serializable]
    public struct StatusIconMapping
    {
        public StatusType statusType;
        [Tooltip("If checked, this mapping will only match if the status has the specified stat target.")]
        public bool specifyStatTarget;
        public StatTarget targetStat;
        public Sprite icon;
    }

    /// <summary>
    /// Global combat tuning variables. Single instance used by BattleSystem.
    /// Values derived from GDD Combat/Stats sections.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatConfig", menuName = "Nevergreen/Data/Combat Config")]
    public class CombatConfig : ScriptableObject
    {
        [Header("Attack Roll")]
        [Tooltip("Minimum multiplier for attack roll (GDD: 0.8).")]
        public float attackRollMin = 0.8f;

        [Tooltip("Maximum multiplier for attack roll (GDD: 1.2).")]
        public float attackRollMax = 1.2f;

        [Header("Accuracy & Defense")]
        [Tooltip("Maximum accuracy cap in percent (GDD: 95).")]
        [Range(0, 100)]
        public int accuracyCap = 95;

        [Tooltip("Maximum defense cap in percent (User Request: 95).")]
        [Range(0, 100)]
        public int defenseCap = 95;

        [Tooltip("Maximum dodge cap in percent (User Request: 95).")]
        [Range(0, 100)]
        public int dodgeCap = 95;

        [Header("Critical")]
        [Tooltip("Damage multiplier on critical hit (GDD: 1.5).")]
        public float critDamageMultiplier = 1.5f;

        [Header("Stun")]
        [Tooltip("Stun resistance bonus after stun expires (GDD: +300%).")]
        public int stunRecoveryResistBonus = 300;

        [Header("Speed Roll")]
        [Tooltip("Minimum random speed bonus roll (inclusive).")]
        public int speedRollMin = 1;

        [Tooltip("Maximum random speed bonus roll (inclusive).")]
        public int speedRollMax = 4;

        [Header("Team")]
        [Tooltip("Maximum party size per team (GDD: 4).")]
        [Range(1, 4)]
        public int maxPartySize = 4;

        [Tooltip("Number of ranks per team (GDD: 4).")]
        public int rankCount = 4;

        [Header("Pile")]
        [Tooltip("Duration in rounds before a Pile decays and transitions to Destroyed state.")]
        public int pileDuration = 4;

        [Header("Leveling")]
        [Tooltip("Global maximum character level.")]
        public int globalMaxLevel = 10;

        [Tooltip("The cost of leveling up. Index 0 corresponds to level 1 -> 2, etc.")]
        public System.Collections.Generic.List<int> levelUpCostCurve = new System.Collections.Generic.List<int>() { 10, 20, 30, 40, 50, 60, 70, 80, 90 };

        /// <summary>
        /// Gets the cost to level up from the current level.
        /// Returns -1 if the character is already at or above the globalMaxLevel.
        /// </summary>
        public int GetLevelUpCost(int currentLevel)
        {
            if (currentLevel >= globalMaxLevel) return -1;
            
            int index = currentLevel - 1;
            if (levelUpCostCurve == null || levelUpCostCurve.Count == 0) return 0;
            
            if (index < 0) return levelUpCostCurve[0];
            if (index >= levelUpCostCurve.Count) return levelUpCostCurve[levelUpCostCurve.Count - 1];
            
            return levelUpCostCurve[index];
        }

        [Header("Rewards")]
        [Tooltip("Minimum Parts awarded upon battle victory (global default).")]
        public int minPartsPerBattle = 10;

        [Tooltip("Maximum Parts awarded upon battle victory (global default).")]
        public int maxPartsPerBattle = 50;

        [Tooltip("Minimum Scraps awarded upon battle victory (global default).")]
        public int minScrapsPerBattle = 5;

        [Tooltip("Maximum Scraps awarded upon battle victory (global default).")]
        public int maxScrapsPerBattle = 25;

        [Tooltip("Reward profiles configured for each enemy encounter tier.")]
        public System.Collections.Generic.List<TierRewardProfile> tierRewardProfiles = new System.Collections.Generic.List<TierRewardProfile>();

        /// <summary>
        /// Gets the configured min/max reward ranges for a specific encounter tier.
        /// Falls back to global defaults if the tier does not have a specific profile configured.
        /// </summary>
        public void GetRewardRanges(EnemyEncounterTier tier, out int minParts, out int maxParts, out int minScraps, out int maxScraps)
        {
            if (tierRewardProfiles != null)
            {
                var profile = tierRewardProfiles.Find(p => p != null && p.tier == tier);
                if (profile != null)
                {
                    minParts = profile.minParts;
                    maxParts = profile.maxParts;
                    minScraps = profile.minScraps;
                    maxScraps = profile.maxScraps;
                    return;
                }
            }

            // Fallbacks
            minParts = minPartsPerBattle;
            maxParts = maxPartsPerBattle;
            minScraps = minScrapsPerBattle;
            maxScraps = maxScrapsPerBattle;
        }

        [Header("Enemy Encounter Tiers")]
        [Tooltip("Mappings of room count progression to enemy encounter tiers. Sorted by roomCount ascending automatically.")]
        public System.Collections.Generic.List<RoomTierMapping> roomTierMappings = new System.Collections.Generic.List<RoomTierMapping>();

        /// <summary>
        /// Retrieves the appropriate encounter tier for the given room count.
        /// Falls back to Trivial if no mappings exist.
        /// </summary>
        public EnemyEncounterTier GetEncounterTierForRoom(int roomCount)
        {
            if (roomTierMappings == null || roomTierMappings.Count == 0)
            {
                // Fallback defaults if not configured
                if (roomCount >= 8) return EnemyEncounterTier.Boss;
                if (roomCount >= 6) return EnemyEncounterTier.LateGame;
                if (roomCount >= 4) return EnemyEncounterTier.MidGame;
                if (roomCount >= 2) return EnemyEncounterTier.EarlyGame;
                return EnemyEncounterTier.Trivial;
            }

            EnemyEncounterTier selectedTier = EnemyEncounterTier.Trivial;
            int highestMatchingRoomCount = -1;

            foreach (var mapping in roomTierMappings)
            {
                if (roomCount >= mapping.roomCount && mapping.roomCount > highestMatchingRoomCount)
                {
                    highestMatchingRoomCount = mapping.roomCount;
                    selectedTier = mapping.tier;
                }
            }

            // If roomCount is smaller than all configured mappings, return the tier of the lowest configured mapping.
            if (highestMatchingRoomCount == -1)
            {
                int minRoomCount = int.MaxValue;
                foreach (var mapping in roomTierMappings)
                {
                    if (mapping.roomCount < minRoomCount)
                    {
                        minRoomCount = mapping.roomCount;
                        selectedTier = mapping.tier;
                    }
                }
            }

            return selectedTier;
        }

        [Header("Status Icons")]
        [Tooltip("Configure icons for status effects. Can specify general status types or target specific stats for buff/debuff.")]
        public System.Collections.Generic.List<StatusIconMapping> statusIcons = new System.Collections.Generic.List<StatusIconMapping>();

        /// <summary>
        /// Retrieves the configured status icon for the given type and stat target.
        /// Prioritizes mappings with specific stat targets (for buff/debuff) before falling back to generic mappings.
        /// </summary>
        public Sprite GetStatusIcon(StatusType type, StatTarget targetStat)
        {
            if (statusIcons == null || statusIcons.Count == 0) return null;

            // 1. Search for specific match first (if it's Buff/Debuff and specifyStatTarget is true)
            foreach (var mapping in statusIcons)
            {
                if (mapping.statusType == type && mapping.specifyStatTarget && mapping.targetStat == targetStat)
                {
                    return mapping.icon;
                }
            }

            // 2. Fallback to generic match (where specifyStatTarget is false)
            foreach (var mapping in statusIcons)
            {
                if (mapping.statusType == type && !mapping.specifyStatTarget)
                {
                    return mapping.icon;
                }
            }

            return null;
        }
    }
}
