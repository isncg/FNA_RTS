using Xunit;

namespace FNARTS.Core.Tests.Util
{
    public class EntityIdGeneratorTests
    {
        [Fact]
        public void Next_ReturnsMonotonicValues()
        {
            EntityIdGenerator.Reset(1); // Ensure known starting point
            uint id1 = EntityIdGenerator.Next();
            uint id2 = EntityIdGenerator.Next();
            uint id3 = EntityIdGenerator.Next();

            Assert.True(id1 < id2);
            Assert.True(id2 < id3);
            Assert.Equal(1u, id1);
            Assert.Equal(2u, id2);
            Assert.Equal(3u, id3);
        }

        [Fact]
        public void Reset_StartsFromSpecifiedValue()
        {
            EntityIdGenerator.Reset(100);
            uint id = EntityIdGenerator.Next();
            Assert.Equal(100u, id);
            EntityIdGenerator.Reset(1); // Reset back for other tests
        }

        [Fact]
        public void NextForFaction_EncodesFactionInHighByte()
        {
            EntityIdGenerator.Reset(1);
            uint id = EntityIdGenerator.NextForFaction(5);

            // Faction 5 in high byte: 0x05_000000
            uint factionPart = (id >> 24) & 0xFF;
            Assert.Equal(5u, factionPart);

            EntityIdGenerator.Reset(1);
        }

        [Fact]
        public void NextForFaction_DifferentFactions_DifferentHighBytes()
        {
            var id1 = EntityIdGenerator.NextForFaction(0);
            var id2 = EntityIdGenerator.NextForFaction(1);

            uint faction1 = (id1 >> 24) & 0xFF;
            uint faction2 = (id2 >> 24) & 0xFF;
            Assert.Equal(0u, faction1);
            Assert.Equal(1u, faction2);
            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void NextForFaction_Lower24BitsMonotonic()
        {
            EntityIdGenerator.Reset(1);
            uint id1 = EntityIdGenerator.NextForFaction(0); // seq 1
            uint id2 = EntityIdGenerator.NextForFaction(0); // seq 2

            uint seq1 = id1 & 0xFFFFFF;
            uint seq2 = id2 & 0xFFFFFF;
            Assert.Equal(1u, seq1);
            Assert.Equal(2u, seq2);

            EntityIdGenerator.Reset(1);
        }
    }
}
