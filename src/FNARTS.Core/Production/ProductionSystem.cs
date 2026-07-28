using System;
using System.Collections.Generic;

namespace FNARTS.Core.Production
{
    /// <summary>
    /// Processes building production queues each frame.  When an item
    /// completes the callback is invoked with (building, unitDefId).
    /// </summary>
    public class ProductionSystem
    {
        /// <summary>Completed items queued for callback this frame.</summary>
        private readonly List<(Building building, string unitDefId)> _completed = new();

        /// <summary>
        /// Advance all building production queues by dt seconds.
        /// Calls onCompleted for each item that finishes this frame.
        /// </summary>
        public void Update(float dt, EntityManager entities,
            Action<Building, string> onCompleted)
        {
            _completed.Clear();

            foreach (var e in entities.AllEntities)
            {
                if (!e.IsAlive) continue;
                if (e is not Building b) continue;
                if (!b.IsProducing) continue;

                var item = b.CurrentProduction!;
                item.RemainingTime -= dt;

                if (item.RemainingTime <= 0f)
                {
                    b.ProductionQueue.Dequeue();
                    _completed.Add((b, item.UnitDefId));
                }
            }

            // Fire callbacks after iteration (avoids modifying
            // the entity collection while enumerating).
            foreach (var (building, unitDefId) in _completed)
                onCompleted(building, unitDefId);
        }

        /// <summary>
        /// Enqueue a unit for training.  Returns false if the building
        /// does not list unitDefId in its ProducesUnitIds.
        /// </summary>
        public bool Enqueue(Building building, string unitDefId, float buildTime)
        {
            if (building.Definition.ProducesUnitIds == null)
                return false;
            if (!building.Definition.ProducesUnitIds.Contains(unitDefId))
                return false;

            building.ProductionQueue.Enqueue(
                new ProductionItem(unitDefId, buildTime));
            return true;
        }

        /// <summary>
        /// Cancel the currently-training item at this building.
        /// Returns the cancelled ProductionItem, or null if idle.
        /// </summary>
        public ProductionItem CancelCurrent(Building building)
        {
            if (!building.IsProducing) return null;
            // Remove the head of the queue (current item).
            // We must drain-to-list, remove first, re-enqueue rest.
            var items = building.ProductionQueue.ToArray();
            building.ProductionQueue.Clear();
            var cancelled = items[0];
            for (int i = 1; i < items.Length; i++)
                building.ProductionQueue.Enqueue(items[i]);
            return cancelled;
        }
    }
}
