using System;

namespace FNARTS.Core
{
    /// <summary>
    /// Core-layer logging abstraction. Inject delegates at startup
    /// to bridge to a concrete logger (Console, file, etc.).
    /// Zero FNA dependency — safe to use from Core.
    /// </summary>
    public static class GameLogger
    {
        public static Action<string> Info  { get; set; } = _ => { };
        public static Action<string> Warn  { get; set; } = _ => { };
        public static Action<string> Error { get; set; } = _ => { };
        public static Action<string> Debug { get; set; } = _ => { };

        public static void LogInfo(string msg) => Info(msg);
        public static void LogWarn(string msg) => Warn(msg);
        public static void LogError(string msg) => Error(msg);
        public static void LogDebug(string msg) => Debug(msg);
    }
}
