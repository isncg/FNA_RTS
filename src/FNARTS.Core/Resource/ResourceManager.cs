using System.Collections.Generic;

namespace FNARTS.Core.Resource
{
    /// <summary>
    /// Per-faction credit wallet. Tracks balance and enforces spending limits.
    /// No income/harvesting — credits are a finite resource for now.
    /// </summary>
    public class ResourceManager
    {
        private readonly Dictionary<int, int> _credits = new();

        public int GetCredits(int faction)
            => _credits.TryGetValue(faction, out var c) ? c : 0;

        public void SetCredits(int faction, int amount)
            => _credits[faction] = amount;

        public void AddCredits(int faction, int amount)
        {
            if (!_credits.ContainsKey(faction))
                _credits[faction] = 0;
            _credits[faction] += amount;
        }

        /// <summary>
        /// Attempt to spend credits. Returns true if the faction had
        /// sufficient funds and the amount was deducted.
        /// </summary>
        public bool TrySpend(int faction, int amount)
        {
            if (amount <= 0) return true;
            if (!_credits.TryGetValue(faction, out var current) || current < amount)
                return false;
            _credits[faction] = current - amount;
            return true;
        }
    }
}
