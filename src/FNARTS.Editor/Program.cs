using System;
using System.IO;

namespace FNARTS.Editor
{
    public static class Program
    {
        public static string MapPath { get; private set; } = "";

        static void Main(string[] args)
        {
            // Parse --map <path> argument
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--map" && i + 1 < args.Length)
                {
                    MapPath = Path.GetFullPath(args[i + 1]);
                    i++;
                }
            }

            using var game = new EditorGame();
            game.Run();
        }
    }
}