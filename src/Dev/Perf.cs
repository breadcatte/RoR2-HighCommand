using System.Diagnostics;
using UnityEngine;

namespace HighCommand.Dev
{
    /// <summary>
    /// Cheap cost counters for the mod's own work (read with the console command hc_perf). Each bucket accumulates
    /// the time spent in one part of the mod; the report converts it to milliseconds per second of real time.
    /// </summary>
    internal static class Perf
    {
        internal static long Brain;      // HeroBrain decisions
        internal static long Executor;   // HeroAIState (per-body inputs)
        internal static long Ui;         // everything the client root does per frame
        internal static long Flow;       // RunFlow ticks

        // the interface's cost split by part (the sum is roughly "ui" plus the late-update drawing)
        internal const int PartOverlay = 0, PartMinimap = 1, PartRoster = 2, PartCommand = 3, PartTopBar = 4, PartPick = 5,
            PartOrders = 6, PartOther = 7;
        internal static readonly long[] Part = new long[8];
        private static readonly string[] PartNames = { "overlay", "minimap", "roster", "command", "topbar", "pick", "orders", "other" };

        private static float windowStart = -1f;
        private static int frames;
        private static float worstFrame;
        private static int gcStart;

        internal static long Begin() => Stopwatch.GetTimestamp();

        internal static void End(ref long bucket, long started) => bucket += Stopwatch.GetTimestamp() - started;

        internal static void Frame()
        {
            if (windowStart < 0f)
            {
                windowStart = Time.unscaledTime;
                gcStart = System.GC.CollectionCount(0);
            }
            frames++;
            if (Time.unscaledDeltaTime > worstFrame) worstFrame = Time.unscaledDeltaTime;
        }

        internal static string Report()
        {
            float seconds = Mathf.Max(0.001f, Time.unscaledTime - windowStart);
            double toMs = 1000.0 / Stopwatch.Frequency / seconds;
            int gc = System.GC.CollectionCount(0) - gcStart;
            long mem = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            string s = $"hc_perf: window {seconds:0.0}s, {frames / seconds:0} fps = {1000f * seconds / Mathf.Max(1, frames):0.00} ms/frame " +
                       $"(worst frame {worstFrame * 1000f:0} ms) | gc0 x{gc}, mono {mem / 1048576f:0.0} MB | per second: " +
                       $"brains {Brain * toMs:0.00} ms, executors {Executor * toMs:0.00} ms, ui {Ui * toMs:0.00} ms, flow {Flow * toMs:0.00} ms";
            var parts = new System.Text.StringBuilder(" | parts:");
            for (int i = 0; i < Part.Length; i++)
            {
                parts.Append(' ').Append(PartNames[i]).Append(' ').Append((Part[i] * toMs).ToString("0.00"));
                Part[i] = 0;
            }
            s += parts.ToString();
            Brain = Executor = Ui = Flow = 0;
            frames = 0;
            worstFrame = 0f;
            windowStart = Time.unscaledTime;
            gcStart = System.GC.CollectionCount(0);
            return s;
        }
    }
}
