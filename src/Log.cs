using System;
using System.Collections.Generic;
using MelonLoader;

namespace WestlandRacerMod
{
    /// <summary>Trim logging helpers (mirrors the other IGG doomsday mods).</summary>
    internal static class Log
    {
        private static readonly HashSet<string> _warnedOnce = new HashSet<string>();
        private static readonly object _sync = new object();

        public static void Info(string message)
        {
            try { MelonLogger.Msg("[WestlandRacer] " + message); } catch { }
        }

        public static void Warn(string message)
        {
            try { MelonLogger.Warning("[WestlandRacer] " + message); } catch { }
        }

        public static void WarnOnce(string message)
        {
            lock (_sync)
            {
                if (!_warnedOnce.Add(message)) return;
            }
            Warn(message);
        }

        public static void Exception(string context, Exception ex)
        {
            try { MelonLogger.Error("[WestlandRacer] " + context + ": " + ex.Message); } catch { }
        }
    }
}
