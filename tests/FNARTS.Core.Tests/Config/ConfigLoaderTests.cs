using System.IO;
using System.Text.Json;
using FNARTS.Core.Config;
using Xunit;

namespace FNARTS.Core.Tests.Config
{
    public class ConfigLoaderTests
    {
        [Fact]
        public void Load_ValidDataDir_ReturnsPopulatedConfig()
        {
            // Arrange: create a temp directory with sample JSON files
            var tmp = Path.Combine(Path.GetTempPath(), $"fna_test_{System.Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, "units"));
                Directory.CreateDirectory(Path.Combine(tmp, "buildings"));

                File.WriteAllText(Path.Combine(tmp, "units", "worker.json"), @"
                {
                    ""id"": ""worker"",
                    ""name"": ""Worker"",
                    ""moveSpeed"": 120.0,
                    ""buildTime"": 5.0,
                    ""textureId"": ""worker""
                }");
                File.WriteAllText(Path.Combine(tmp, "units", "soldier.json"), @"
                {
                    ""id"": ""soldier"",
                    ""name"": ""Soldier"",
                    ""moveSpeed"": 90.0,
                    ""buildTime"": 8.0,
                    ""textureId"": ""infantry""
                }");
                File.WriteAllText(Path.Combine(tmp, "buildings", "barracks.json"), @"
                {
                    ""id"": ""barracks"",
                    ""name"": ""Barracks"",
                    ""sizeX"": 3,
                    ""sizeY"": 2,
                    ""height"": 2,
                    ""textureId"": ""gen_3_2_2"",
                    ""producesUnitIds"": [""worker"", ""soldier""]
                }");
                File.WriteAllText(Path.Combine(tmp, "config.json"), @"
                {
                    ""placementOrder"": [""barracks""]
                }");

                // Act
                var config = ConfigLoader.Load(tmp);

                // Assert
                Assert.Equal(2, config.UnitDefs.Count);
                Assert.Single(config.BuildingDefs);
                Assert.Single(config.PlacementOrder);

                var worker = config.GetUnit("worker");
                Assert.NotNull(worker);
                Assert.Equal("Worker", worker.Name);
                Assert.Equal(120f, worker.MoveSpeed);
                Assert.Equal(5f, worker.BuildTime);
                Assert.Equal("worker", worker.TextureId);

                var soldier = config.GetUnit("soldier");
                Assert.NotNull(soldier);
                Assert.Equal(90f, soldier.MoveSpeed);
                Assert.Equal(8f, soldier.BuildTime);

                var barracks = config.GetBuilding("barracks");
                Assert.NotNull(barracks);
                Assert.Equal("Barracks", barracks.Name);
                Assert.Equal(3, barracks.SizeX);
                Assert.Equal(2, barracks.SizeY);
                Assert.Equal(2, barracks.Height);
                Assert.Equal(2, barracks.ProducesUnitIds.Count);
                Assert.Contains("worker", barracks.ProducesUnitIds);
                Assert.Contains("soldier", barracks.ProducesUnitIds);

                Assert.Equal("barracks", config.PlacementOrder[0]);
            }
            finally
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void Load_EmptyDir_ReturnsEmptyConfig()
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"fna_empty_{System.Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(tmp);

                var config = ConfigLoader.Load(tmp);

                Assert.Empty(config.UnitDefs);
                Assert.Empty(config.BuildingDefs);
                Assert.Empty(config.PlacementOrder);
            }
            finally
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void Load_MissingDir_ReturnsEmptyConfig()
        {
            var config = ConfigLoader.Load("/nonexistent/path/xyz_test");
            Assert.Empty(config.UnitDefs);
            Assert.Empty(config.BuildingDefs);
        }

        [Fact]
        public void Load_MalformedJson_SkipsAndContinues()
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"fna_bad_{System.Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, "units"));
                File.WriteAllText(Path.Combine(tmp, "units", "good.json"), @"
                {
                    ""id"": ""good"",
                    ""name"": ""Good Unit"",
                    ""moveSpeed"": 100.0,
                    ""buildTime"": 3.0,
                    ""textureId"": ""good""
                }");
                File.WriteAllText(Path.Combine(tmp, "units", "bad.json"), "not valid json {{{");

                var config = ConfigLoader.Load(tmp);

                // Should still load the valid file
                Assert.Single(config.UnitDefs);
                Assert.NotNull(config.GetUnit("good"));
            }
            finally
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void GetUnit_UnknownId_ReturnsNull()
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"fna_get_{System.Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(tmp);
                var config = ConfigLoader.Load(tmp);
                Assert.Null(config.GetUnit("nonexistent"));
                Assert.Null(config.GetBuilding("nonexistent"));
            }
            finally
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, recursive: true);
            }
        }
    }
}
