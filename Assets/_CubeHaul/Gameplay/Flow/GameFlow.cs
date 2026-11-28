using System;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Session;

namespace CubeHaul.Gameplay.Flow
{
    /// <summary>The screen over the game (Task 17).</summary>
    public enum FlowScreen
    {
        /// <summary>The level is shown, nothing moves until Play.</summary>
        Start,
        /// <summary>Playing (also while the last trucks drive home after a win or a jam).</summary>
        Playing,
        /// <summary>"Picture complete!": Next level (or Back to level 1 after the last).</summary>
        Won,
        /// <summary>"Out of moves": +1 Slot while it is left, Retry.</summary>
        Stuck,
    }

    /// <summary>
    /// The level flow around one game (Task 17, docs/GAME_FLOW.md): the start screen, playing, and the end panels. The
    /// rules decide when a game is won or stuck (GameEngine status); this decides when the panel may say so. Not
    /// before everything the player is watching has finished: the trucks still out, including those driving home
    /// after the last pickup, and any crate still flying into its slot (the visual pending state). Then main.ts's
    /// 0.4 s pause (overlayDelay), and the panel opens. +1 Slot un-sticks a game (GameEngine.AddSlot): the panel
    /// closes and play goes on. Real time, engine-free.
    /// </summary>
    public sealed class GameFlow
    {
        /// <summary>Seconds after everything has settled before the panel opens (main.ts: overlayDelay ≥ 0.4).</summary>
        public const double PanelDelay = SimConstants.EndPanelDelay;

        public FlowScreen Screen { get; private set; } = FlowScreen.Playing;
        /// <summary>Seconds the game has been over with nothing left moving.</summary>
        public double Settled { get; private set; }

        public event Action<FlowScreen> ScreenChanged;

        /// <summary>
        /// What the player still sees moving that belongs to the game: trucks on the road or waiting in the ramp, crates
        /// flying into their slots. The end panel waits for 0.
        /// </summary>
        public static int VisualPending(GameSession session) =>
            session == null ? 0 : session.TrucksOut + session.Scheduler.FlightCount;

        /// <summary>A new level: on the start screen, or straight into play.</summary>
        public void Begin(bool showStart)
        {
            Settled = 0;
            Set(showStart ? FlowScreen.Start : FlowScreen.Playing);
        }

        /// <summary>The start screen's Play.</summary>
        public void Play()
        {
            if (Screen == FlowScreen.Start) Set(FlowScreen.Playing);
        }

        /// <summary>After the session's frame: opens the end panel when the game is over and everything is home, or closes it when +1 Slot un-stuck the game.</summary>
        public void Update(GameSession session, double realSeconds)
        {
            if (session == null || Screen == FlowScreen.Start) return;
            GameStatus status = session.Game.Status;
            if (status == GameStatus.Playing)
            {
                Settled = 0;
                if (Screen != FlowScreen.Playing) Set(FlowScreen.Playing);
                return;
            }
            if (Screen != FlowScreen.Playing) return;
            if (VisualPending(session) > 0)
            {
                Settled = 0;
                return;
            }
            Settled += Math.Max(0, realSeconds);
            if (Settled >= PanelDelay) Set(status == GameStatus.Won ? FlowScreen.Won : FlowScreen.Stuck);
        }

        /// <summary>The level after <paramref name="index"/>; after the last one, the first again (main.ts goToLevel).</summary>
        public static int NextIndex(int index, int count) => count <= 0 ? 0 : ((index + 1) % count + count) % count;

        public static int PreviousIndex(int index, int count) => count <= 0 ? 0 : ((index - 1) % count + count) % count;

        /// <summary>The won panel says "Back to level 1" after the last level, "Next level" before.</summary>
        public static bool IsLast(int index, int count) => count > 0 && index == count - 1;

        void Set(FlowScreen screen)
        {
            if (Screen == screen) return;
            Screen = screen;
            ScreenChanged?.Invoke(screen);
        }
    }
}
