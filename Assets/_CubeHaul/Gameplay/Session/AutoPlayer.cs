using System;
using CubeHaul.Domain;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>What <see cref="AutoPlayer.Act"/> found.</summary>
    public enum AutoPlayOutcome
    {
        /// <summary>The level goes on.</summary>
        Playing,
        /// <summary>Won, and every truck is home.</summary>
        Won,
        /// <summary>Stuck with the +1 Slot booster used up.</summary>
        Stuck,
    }

    /// <summary>
    /// A simple, deterministic player (Task 15, docs/SESSION.md), the C# twin of the `play` policy in
    /// Tools/ts-reference/tests/appModel.ts that the golden sessions are recorded with. Before each frame it taps at most
    /// one crate: the first column whose front color can eat now or will be uncovered by a truck already out; failing
    /// that, when nothing is moving and no slot can eat, the first non-empty column. When stuck it uses +1 Slot once.
    /// It stands in for the player until the tray takes touches; it plays every built-in level but Rocket to the win.
    /// </summary>
    public static class AutoPlayer
    {
        /// <summary>One decision, made before a frame's <see cref="GameSession.Advance"/>.</summary>
        public static AutoPlayOutcome Act(GameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            GameEngine g = session.Game;
            GameState s = g.State;
            if (g.Status == GameStatus.Won && session.TrucksOut == 0) return AutoPlayOutcome.Won;
            if (g.Status == GameStatus.Stuck && !session.AddSlot()) return AutoPlayOutcome.Stuck;
            if (g.Status == GameStatus.Playing && s.HasFreeSlot())
            {
                int col = -1;
                for (int c = 0; c < s.ColumnCount && col < 0; c++)
                {
                    Crate front = s.Front(c);
                    if (front != null && (g.Board.ReachableCount(front.Color) > 0 || g.Board.OpensUp(front.Color))) col = c;
                }
                if (col < 0 && session.TrucksOut == 0 && !AnySlotCanEat(s))
                    for (int c = 0; c < s.ColumnCount && col < 0; c++)
                        if (s.ColumnLength(c) > 0) col = c;
                if (col >= 0) session.Tap(col);
            }
            return AutoPlayOutcome.Playing;
        }

        /// <summary>
        /// Plays to the end with frames of <paramref name="frameSeconds"/>: Act, then Advance, until won or stuck (or
        /// <paramref name="maxFrames"/>, then Playing). <paramref name="afterFrame"/> runs after every frame.
        /// </summary>
        public static AutoPlayOutcome Play(GameSession session, double frameSeconds, int maxFrames = 400_000, Action<GameSession> afterFrame = null)
        {
            for (int f = 0; f < maxFrames; f++)
            {
                AutoPlayOutcome o = Act(session);
                if (o != AutoPlayOutcome.Playing) return o;
                session.Advance(frameSeconds);
                afterFrame?.Invoke(session);
            }
            return AutoPlayOutcome.Playing;
        }

        static bool AnySlotCanEat(GameState s)
        {
            for (int k = 0; k < s.SlotCount; k++)
                if (s.SlotCanEat(k)) return true;
            return false;
        }
    }
}
