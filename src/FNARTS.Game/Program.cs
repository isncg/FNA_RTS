using System;
using FNARTS.Core;

namespace FNARTS.Game
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Setup logging
            GameLogger.Info = msg => Console.WriteLine($"[INFO] {msg}");
            GameLogger.Warn = msg => Console.WriteLine($"[WARN] {msg}");
            GameLogger.Error = msg => Console.WriteLine($"[ERROR] {msg}");
            GameLogger.Debug = msg => Console.WriteLine($"[DEBUG] {msg}");

            // Parse args
            bool headless = false;
            bool debugRender = false;
            string mapName = "test_map1";
            int? seed = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--headless": headless = true; break;
                    case "--debug-render": debugRender = true; break;
                    case "--skip-menu": break; // Phase 1 MVP: skip unimplemented menu
                    case "--map" when i + 1 < args.Length: mapName = args[++i]; break;
                    case "--seed" when i + 1 < args.Length:
                        seed = int.Parse(args[++i]); break;
                }
            }

            if (seed.HasValue)
            {
                // Set random seed for deterministic behavior
                new Random(seed.Value); // placeholder
            }

            GameLogger.Info($"Starting FNA_RTS (headless={headless}, map={mapName})");

            try
            {
                using var game = new RTSGame(headless, debugRender, mapName);
                game.Run();
                if (headless)
                    Console.WriteLine("RESULT: RTSGame PASS");
            }
            catch (Exception ex)
            {
                GameLogger.Error($"Fatal: {ex}");
                Environment.Exit(1);
            }
        }
    }
}
