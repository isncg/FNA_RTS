using System;

namespace FNARTS.Core
{
    /// <summary>
    /// Global monotonic entity ID generator.
    /// Phase 1: simple counter. Phase 3: faction-prefixed format.
    /// </summary>
    public static class EntityIdGenerator
    {
        private static uint _nextId = 1;

        /// <summary>Generate the next entity ID.</summary>
        public static uint Next() => _nextId++;

        /// <summary>
        /// Generate a faction-scoped entity ID (Phase 3 network compatible).
        /// Upper 8 bits = faction index, lower 24 bits = sequence.
        /// </summary>
        public static uint NextForFaction(int factionIndex)
            => ((uint)factionIndex << 24) | (_nextId++ & 0xFFFFFF);

        /// <summary>Reset the sequence (for testing).</summary>
        public static void Reset(uint start = 1) => _nextId = start;
    }
}
