using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FNARTS.Core.Config
{
    /// <summary>
    /// Complete game data configuration loaded from JSON files.
    /// </summary>
    public class GameConfig
    {
        public Dictionary<string, UnitDef> UnitDefs { get; } = new();
        public Dictionary<string, BuildingDef> BuildingDefs { get; } = new();
        /// <summary>Building IDs available in placement mode, in display order.</summary>
        public List<string> PlacementOrder { get; } = new();

        public UnitDef GetUnit(string id) =>
            UnitDefs.TryGetValue(id, out var u) ? u : null;

        public BuildingDef GetBuilding(string id) =>
            BuildingDefs.TryGetValue(id, out var b) ? b : null;
    }

    /// <summary>
    /// Loads unit and building definitions from a data/ directory.
    /// Expects:
    ///   data/units/*.json      — one UnitDef per file
    ///   data/buildings/*.json  — one BuildingDef per file
    ///   data/config.json       — top-level settings (placement order, etc.)
    /// </summary>
    public static class ConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// Load all game data from the given directory (e.g. "data/").
        /// </summary>
        public static GameConfig Load(string dataDir)
        {
            var config = new GameConfig();

            // ── Unit definitions ───────────────────────────────────────
            string unitsDir = Path.Combine(dataDir, "units");
            if (Directory.Exists(unitsDir))
            {
                foreach (var file in Directory.GetFiles(unitsDir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var unit = JsonSerializer.Deserialize<UnitDef>(json, JsonOptions);
                        if (unit != null && !string.IsNullOrEmpty(unit.Id))
                            config.UnitDefs[unit.Id] = unit;
                    }
                    catch (Exception ex)
                    {
                        GameLogger.Warn($"Failed to load unit config {file}: {ex.Message}");
                    }
                }
            }

            // ── Building definitions ───────────────────────────────────
            string buildingsDir = Path.Combine(dataDir, "buildings");
            if (Directory.Exists(buildingsDir))
            {
                foreach (var file in Directory.GetFiles(buildingsDir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var building = JsonSerializer.Deserialize<BuildingDef>(json, JsonOptions);
                        if (building != null && !string.IsNullOrEmpty(building.Id))
                            config.BuildingDefs[building.Id] = building;
                    }
                    catch (Exception ex)
                    {
                        GameLogger.Warn($"Failed to load building config {file}: {ex.Message}");
                    }
                }
            }

            // ── Top-level config (placement order, etc.) ───────────────
            string configFile = Path.Combine(dataDir, "config.json");
            if (File.Exists(configFile))
            {
                try
                {
                    var json = File.ReadAllText(configFile);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("placementOrder", out var placementEl))
                    {
                        foreach (var item in placementEl.EnumerateArray())
                            config.PlacementOrder.Add(item.GetString()!);
                    }
                }
                catch (Exception ex)
                {
                    GameLogger.Warn($"Failed to load config.json: {ex.Message}");
                }
            }

            GameLogger.Info($"Loaded {config.UnitDefs.Count} unit(s), " +
                $"{config.BuildingDefs.Count} building(s) from {dataDir}");
            return config;
        }
    }
}
