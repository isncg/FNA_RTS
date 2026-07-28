using System;
using System.Collections.Generic;
using System.Numerics;

namespace FNARTS.Core
{
    /// <summary>
    /// Handles unit selection via single click and drag rectangle.
    /// Core-only: no rendering or input hardware dependency.
    /// </summary>
    public class SelectionSystem
    {
        private readonly HashSet<uint> _selectedIds = new();
        public IReadOnlySet<uint> SelectedEntityIds => _selectedIds;
        public int SelectedCount => _selectedIds.Count;

        // Drag state
        public bool IsDragging { get; private set; }
        public Vector2 DragStart { get; private set; }
        public Vector2 DragEnd { get; private set; }
        public bool DragActive => IsDragging;

        /// <summary>Begin a drag selection.</summary>
        public void BeginDrag(Vector2 screenPos)
        {
            IsDragging = true;
            DragStart = screenPos;
            DragEnd = screenPos;
        }

        /// <summary>Update drag endpoint.</summary>
        public void UpdateDrag(Vector2 screenPos)
        {
            DragEnd = screenPos;
        }

        /// <summary>
        /// Finalize drag selection. Returns entities inside the drag rectangle.
        /// Caller is responsible for converting screen coords.
        /// </summary>
        public IEnumerable<Entity> EndDrag(Vector2 screenPos, EntityManager entities,
            Func<Vector2, Vector2> screenToWorld)
        {
            IsDragging = false;
            DragEnd = screenPos;

            // Build world-space rectangle from screen drag rect
            Vector2 min = screenToWorld(new Vector2(
                Math.Min(DragStart.X, DragEnd.X),
                Math.Min(DragStart.Y, DragEnd.Y)));
            Vector2 max = screenToWorld(new Vector2(
                Math.Max(DragStart.X, DragEnd.X),
                Math.Max(DragStart.Y, DragEnd.Y)));

            // Swap if min > max after transform
            if (min.X > max.X) (min.X, max.X) = (max.X, min.X);
            if (min.Y > max.Y) (min.Y, max.Y) = (max.Y, min.Y);

            return entities.QueryRect(min, max);
        }

        /// <summary>Select a single entity.</summary>
        public void Select(Entity entity, bool additive = false)
        {
            if (!additive) ClearSelection();
            if (entity != null)
            {
                _selectedIds.Add(entity.Id);
                entity.IsSelected = true;
            }
        }

        /// <summary>Select multiple entities.</summary>
        public void SelectMultiple(IEnumerable<Entity> entities, bool additive = false)
        {
            if (!additive) ClearSelection();
            foreach (var e in entities)
            {
                if (e == null) continue;
                _selectedIds.Add(e.Id);
                e.IsSelected = true;
            }
        }

        /// <summary>Deselect a single entity.</summary>
        public void Deselect(Entity entity)
        {
            if (entity == null) return;
            _selectedIds.Remove(entity.Id);
            entity.IsSelected = false;
        }

        /// <summary>Clear all selections.</summary>
        public void ClearSelection()
        {
            // Note: caller must have access to entities to clear IsSelected flags.
            // For now we just clear the set; EntityRenderer handles the visual.
            _selectedIds.Clear();
        }
    }
}
