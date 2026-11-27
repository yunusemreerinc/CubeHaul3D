using System;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>
    /// What a frame of the game does (Task 15, docs/SESSION.md): the device's frame time goes into a
    /// <see cref="FixedStepClock"/>, and the session advances by whole 1/60 s steps; before each step the
    /// <see cref="AutoPlayer"/> may tap (while there is no touch input, or for demos and tests). The view draws
    /// <see cref="Alpha"/> of the way into the next step. Engine-free, so the frame-rate independence is tested in EditMode.
    /// </summary>
    public sealed class SessionLoop
    {
        public SessionLoop(GameSession session, FixedStepClock clock = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Clock = clock ?? new FixedStepClock();
        }

        public GameSession Session { get; }
        public FixedStepClock Clock { get; }

        /// <summary>Let the auto player tap the crates.</summary>
        public bool AutoPlay { get; set; }

        /// <summary>What the auto player found last (Playing while it is off).</summary>
        public AutoPlayOutcome Outcome { get; private set; }

        /// <summary>Share of the next step already passed, for drawing between steps.</summary>
        public double Alpha => Clock.Alpha;

        /// <summary>One device frame of <paramref name="frameSeconds"/>; returns how many steps ran.</summary>
        public int Frame(double frameSeconds)
        {
            int steps = Clock.Advance(frameSeconds);
            for (int k = 0; k < steps; k++) Step();
            return steps;
        }

        /// <summary>Exactly one step (the auto player first, if on).</summary>
        public void Step()
        {
            if (AutoPlay)
            {
                Outcome = AutoPlayer.Act(Session);
                // Like appModel.play, it stops when the level is over; the session still runs, e.g. for the end panel.
            }
            Session.Advance(Clock.Step);
        }
    }
}
