using System;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>
    /// Turns uneven frame times into whole simulation steps (Task 15, docs/SESSION.md). cube-hunt runs one update per
    /// browser frame, which on a 60 Hz screen is 1/60 s; stepping the session by exactly that, whatever the device's
    /// frame rate, gives the same game as cube-hunt at 60 Hz, the same on every phone. A long frame is cut to
    /// MAX_FRAME_TIME first, as in main.ts. <see cref="Alpha"/> is how far the next step has come, for drawing in between.
    /// </summary>
    public sealed class FixedStepClock
    {
        /// <summary>Tolerance for frame times that are a whole number of steps up to rounding (60 Hz frames of 1/60 s).</summary>
        const double Slack = 1e-9;

        double accumulator;

        public FixedStepClock(double step = 1.0 / 60)
        {
            if (!(step > 0) || double.IsInfinity(step)) throw new ArgumentOutOfRangeException(nameof(step));
            Step = step;
        }

        /// <summary>Real seconds per simulation step.</summary>
        public double Step { get; }

        /// <summary>Longest frame counted, seconds (main.ts: min(0.05, Δt)).</summary>
        public double MaxFrameTime { get; set; } = SimConstants.MaxFrameTime;

        /// <summary>Share of a step since the last one, 0..1.</summary>
        public double Alpha => Math.Max(0, Math.Min(1, accumulator / Step));

        /// <summary>Adds a frame's real time; returns how many steps to run now.</summary>
        public int Advance(double frameSeconds)
        {
            if (double.IsNaN(frameSeconds) || frameSeconds < 0) frameSeconds = 0;
            accumulator += Math.Min(frameSeconds, MaxFrameTime);
            int steps = (int)Math.Floor((accumulator + Slack) / Step);
            accumulator = Math.Max(0, accumulator - steps * Step);
            return steps;
        }

        public void Reset() => accumulator = 0;
    }
}
