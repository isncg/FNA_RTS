using System.Linq;
using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Selection
{
    public class SelectionSystemTests
    {
        private static Unit MakeUnit(float x, float y)
        {
            var def = new UnitDef { Id = "test", MoveSpeed = 100f };
            return new Unit(def) { WorldPosition = new Vector2(x, y) };
        }

        [Fact]
        public void NewSelection_IsEmpty()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            Assert.Empty(sel.SelectedEntityIds);
            Assert.Equal(0, sel.SelectedCount);
        }

        [Fact]
        public void Select_SingleEntity_IsSelected()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var unit = MakeUnit(50, 50);
            sel.Select(unit);

            Assert.Single(sel.SelectedEntityIds);
            Assert.Contains(unit.Id, sel.SelectedEntityIds);
            Assert.True(unit.IsSelected);
        }

        [Fact]
        public void Select_Null_DoesNotThrow()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            sel.Select(null);
            Assert.Empty(sel.SelectedEntityIds);
        }

        [Fact]
        public void Select_Additive_KeepsPrevious()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var u1 = MakeUnit(50, 50);
            var u2 = MakeUnit(100, 100);

            sel.Select(u1);
            sel.Select(u2, additive: true);

            Assert.Equal(2, sel.SelectedCount);
            Assert.Contains(u1.Id, sel.SelectedEntityIds);
            Assert.Contains(u2.Id, sel.SelectedEntityIds);
        }

        [Fact]
        public void Select_NonAdditive_ReplacesPrevious()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var u1 = MakeUnit(50, 50);
            var u2 = MakeUnit(100, 100);

            sel.Select(u1);
            sel.Select(u2, additive: false);

            Assert.Single(sel.SelectedEntityIds);
            Assert.Contains(u2.Id, sel.SelectedEntityIds);
            Assert.DoesNotContain(u1.Id, sel.SelectedEntityIds);
        }

        [Fact]
        public void SelectMultiple_AddsAllEntities()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var units = new[] { MakeUnit(0, 0), MakeUnit(10, 10), MakeUnit(20, 20) };
            sel.SelectMultiple(units);

            Assert.Equal(3, sel.SelectedCount);
        }

        [Fact]
        public void SelectMultiple_Additive_KeepsExisting()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var existing = MakeUnit(0, 0);
            sel.Select(existing);

            var units = new[] { MakeUnit(10, 10), MakeUnit(20, 20) };
            sel.SelectMultiple(units, additive: true);

            Assert.Equal(3, sel.SelectedCount);
        }

        [Fact]
        public void SelectMultiple_WithNulls_SkipsThem()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var units = new FNARTS.Core.Entity[] { MakeUnit(0, 0), null, MakeUnit(10, 10) };
            sel.SelectMultiple(units);

            Assert.Equal(2, sel.SelectedCount);
        }

        [Fact]
        public void Deselect_RemovesEntity()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var unit = MakeUnit(50, 50);
            sel.Select(unit);
            sel.Deselect(unit);

            Assert.Empty(sel.SelectedEntityIds);
            Assert.False(unit.IsSelected);
        }

        [Fact]
        public void Deselect_Null_DoesNotThrow()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            sel.Deselect(null);
        }

        [Fact]
        public void ClearSelection_EmptiesSet()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            sel.Select(MakeUnit(0, 0));
            sel.Select(MakeUnit(10, 10), additive: true);
            sel.ClearSelection();

            Assert.Empty(sel.SelectedEntityIds);
            Assert.Equal(0, sel.SelectedCount);
        }

        [Fact]
        public void Drag_InitialState_IsInactive()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            Assert.False(sel.DragActive);
            Assert.False(sel.IsDragging);
            Assert.Equal(Vector2.Zero, sel.DragStart);
            Assert.Equal(Vector2.Zero, sel.DragEnd);
        }

        [Fact]
        public void BeginDrag_SetsStartPoint()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            sel.BeginDrag(new Vector2(100, 200));

            Assert.True(sel.DragActive);
            Assert.Equal(new Vector2(100, 200), sel.DragStart);
            Assert.Equal(new Vector2(100, 200), sel.DragEnd);
        }

        [Fact]
        public void UpdateDrag_UpdatesEndPoint()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            sel.BeginDrag(new Vector2(100, 200));
            sel.UpdateDrag(new Vector2(300, 400));

            Assert.Equal(new Vector2(100, 200), sel.DragStart);
            Assert.Equal(new Vector2(300, 400), sel.DragEnd);
        }

        [Fact]
        public void EndDrag_CompletesAndFindsEntities()
        {
            var sel = new FNARTS.Core.SelectionSystem();
            var mgr = new FNARTS.Core.EntityManager();
            var inside = MakeUnit(200, 200);
            var outside = MakeUnit(500, 500);
            mgr.AddEntity(inside);
            mgr.AddEntity(outside);

            // Simulate drag from (100,100) to (300,300) in screen space
            // Simple identity transform for screen→world
            sel.BeginDrag(new Vector2(100, 100));
            var found = sel.EndDrag(new Vector2(300, 300), mgr, p => p).ToList();

            Assert.False(sel.DragActive);
            Assert.Single(found);
            Assert.Same(inside, found[0]);
        }
    }
}
