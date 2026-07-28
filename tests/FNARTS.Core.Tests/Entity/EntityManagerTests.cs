using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FNARTS.Core.Tests.Entity
{
    public class EntityManagerTests
    {
        private static Unit MakeUnit(float x, float y)
        {
            var def = new UnitDef { Id = "test", MoveSpeed = 100f };
            return new Unit(def) { WorldPosition = new Vector2(x, y) };
        }

        [Fact]
        public void AddEntity_AppearsInAllEntities()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(100, 200);
            mgr.AddEntity(unit);
            Assert.Contains(unit, mgr.AllEntities);
            Assert.Single(mgr.AllEntities);
        }

        [Fact]
        public void GetEntity_ByExistingId_ReturnsEntity()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(50, 50);
            mgr.AddEntity(unit);
            var found = mgr.GetEntity(unit.Id);
            Assert.Same(unit, found);
        }

        [Fact]
        public void GetEntity_ByNonexistentId_ReturnsNull()
        {
            var mgr = new EntityManager();
            Assert.Null(mgr.GetEntity(99999));
        }

        [Fact]
        public void RemoveEntity_RemovesFromCollection()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(50, 50);
            mgr.AddEntity(unit);
            mgr.RemoveEntity(unit.Id);
            Assert.Empty(mgr.AllEntities);
            Assert.Null(mgr.GetEntity(unit.Id));
        }

        [Fact]
        public void RemoveEntity_Nonexistent_DoesNotThrow()
        {
            var mgr = new EntityManager();
            mgr.RemoveEntity(99999);
            // Should not throw
        }

        [Fact]
        public void QueryPoint_EntityAtPosition_Found()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(100, 100);
            mgr.AddEntity(unit);

            var found = mgr.QueryPoint(new Vector2(100, 100));
            Assert.Same(unit, found);
        }

        [Fact]
        public void QueryPoint_EntityFar_NotFound()
        {
            var mgr = new EntityManager();
            var unit = MakeUnit(100, 100);
            mgr.AddEntity(unit);

            var found = mgr.QueryPoint(new Vector2(1000, 1000));
            Assert.Null(found);
        }

        [Fact]
        public void QueryPoint_MultipleEntities_ReturnsHighestY()
        {
            var mgr = new EntityManager();
            var back = MakeUnit(100, 90);   // lower Y = further back
            var front = MakeUnit(100, 110); // higher Y = closer to camera
            mgr.AddEntity(back);
            mgr.AddEntity(front);

            // Query at center — should pick front (highest Y)
            var found = mgr.QueryPoint(new Vector2(100, 100));
            Assert.Same(front, found);
        }

        [Fact]
        public void QueryPoint_DeadEntity_Skipped()
        {
            var mgr = new EntityManager();
            var dead = MakeUnit(100, 100);
            dead.IsAlive = false;
            mgr.AddEntity(dead);

            Assert.Null(mgr.QueryPoint(new Vector2(100, 100)));
        }

        [Fact]
        public void QueryRect_FindsEntitiesInRectangle()
        {
            var mgr = new EntityManager();
            var inside = MakeUnit(50, 50);
            var outside = MakeUnit(200, 200);
            mgr.AddEntity(inside);
            mgr.AddEntity(outside);

            var found = mgr.QueryRect(new Vector2(0, 0), new Vector2(100, 100)).ToList();
            Assert.Single(found);
            Assert.Same(inside, found[0]);
        }

        [Fact]
        public void QueryRect_DeadEntities_Excluded()
        {
            var mgr = new EntityManager();
            var dead = MakeUnit(50, 50);
            dead.IsAlive = false;
            mgr.AddEntity(dead);

            var found = mgr.QueryRect(new Vector2(0, 0), new Vector2(100, 100)).ToList();
            Assert.Empty(found);
        }

        [Fact]
        public void AllEntities_IsReadOnly()
        {
            var mgr = new EntityManager();
            Assert.IsAssignableFrom<IReadOnlyList<FNARTS.Core.Entity>>(mgr.AllEntities);
        }
    }
}
