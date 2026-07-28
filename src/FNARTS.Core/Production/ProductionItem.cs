namespace FNARTS.Core
{
    /// <summary>One item in a building's production queue.</summary>
    public class ProductionItem
    {
        /// <summary>UnitDef.Id of the unit being trained.</summary>
        public string UnitDefId { get; }

        /// <summary>Total time to train (seconds).</summary>
        public float TotalTime { get; }

        /// <summary>Time remaining (seconds). Decremented each frame.</summary>
        public float RemainingTime { get; set; }

        /// <summary>Fraction complete [0, 1].</summary>
        public float Progress => 1f - (RemainingTime / TotalTime);

        public ProductionItem(string unitDefId, float totalTime)
        {
            UnitDefId = unitDefId;
            TotalTime = totalTime;
            RemainingTime = totalTime;
        }
    }
}
