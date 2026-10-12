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
        private static float windowStart = -1f;
        private static int frames;
        private static float worstFrame;

        internal static long Begin() => Stopwatch.GetTimestamp();

        internal static void End(ref long bucket, long started) => bucket += Stopwatch.GetTimestamp() - started;

        internal static void Frame()
        {
            if (windowStart < 0f) windowStart = Time.unscaledTime;
            frames++;
            if (Time.unscaledDeltaTime > worstFrame) worstFrame = Time.unscaledDeltaTime;
        }

        internal static string Report()
        {
            float seconds = Mathf.Max(0.001f, Time.unscaledTime - windowStart);
            double toMs = 1000.0 / Stopwatch.Frequency / seconds;
            string s = $"hc_perf: window {seconds:0.0}s, {frames / seconds:0} fps (worst frame {worstFrame * 1000f:0} ms) | per second: " +
                       $"brains {Brain * toMs:0.00} ms, executors {Executor * toMs:0.00} ms, ui {Ui * toMs:0.00} ms, flow {Flow * toMs:0.00} ms";
            Brain = Executor = Ui = Flow = 0;
            frames = 0;
            worstFrame = 0f;
            windowStart = Time.unscaledTime;
            return s;
        }
    }
}
