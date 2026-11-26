using System;
using System.Collections.Generic;
using CubeHaul.Gameplay.Roads;

namespace CubeHaul.Gameplay.Trucks
{
    /// <summary>Where a truck is in its trip (main.ts Car.state, plus the time before it leaves the tunnel).</summary>
    public enum TruckState
    {
        /// <summary>Driving out along its route to the cube.</summary>
        Out,
        /// <summary>Parked next to its cube, loading it (LOAD_TIME), turning to face it.</summary>
        Load,
        /// <summary>Driving back the way it came, then down its ramp lane.</summary>
        Home,
        /// <summary>Back down in the tunnel: done.</summary>
        Gone,
    }

    /// <summary>What happened during one <see cref="TruckMotion.Advance"/>.</summary>
    [Flags]
    public enum TruckEvents
    {
        None = 0,
        /// <summary>It reached its cube: the cube is collected now (main.ts pickUp → Game.collect).</summary>
        Arrived = 1,
        /// <summary>It finished loading and turned for home.</summary>
        Loaded = 2,
        /// <summary>It is back in the tunnel.</summary>
        Gone = 4,
    }

    /// <summary>
    /// One truck's movement, engine-free (Task 14, docs/TRUCKS.md): a port of main.ts's car update (drive, turnToward,
    /// load, drive home, carPose, the ramp fade). It drives at CAR_SPEED along its route's waypoints, eases its heading
    /// toward the next waypoint (at most 14 rad/s, 10 while loading) so turns look natural, keeps to the right on the
    /// road, and climbs out of / sinks into the tunnel. Positions are design units; the view maps them to the world.
    /// Allocation-free after construction.
    /// </summary>
    public sealed class TruckMotion
    {
        /// <summary>Truck speed, design units per second at 1x (main.ts CAR_SPEED).</summary>
        public const double Speed = SimConstants.CarSpeed;
        public const double LoadTime = SimConstants.LoadTime;
        /// <summary>Steering while driving and while loading, rad per second (main.ts update: 14·dt, 10·dt).</summary>
        public const double DriveTurnRate = 14, LoadTurnRate = 10;
        /// <summary>On the road a truck keeps this far right of the route, at scale 1 (main.ts LANE).</summary>
        public const double Lane = 5.8;
        /// <summary>A truck fades in over this distance out of the tunnel (main.ts RAMP_FADE).</summary>
        public const double RampFade = 28;
        /// <summary>Share of the load after which the cube is in the bed (main.ts LAND_AT).</summary>
        public const double LandAt = 0.78;
        /// <summary>A truck is this long and wide at scale 1, design units (main.ts truckSprite).</summary>
        public const double Length = 21.4, Width = 11.4;

        readonly TruckRoute route;
        readonly RoadRing ring;
        IReadOnlyList<PointD> path;

        /// <param name="route">The planned trip (RoadPathPlanner.PlanTruck).</param>
        /// <param name="cube">Center of the cube it goes for.</param>
        /// <param name="cellSize">The level's cell size: trucks shrink with the cubes (main.ts carScale).</param>
        /// <param name="ring">The road, for keeping right on it.</param>
        public TruckMotion(TruckRoute route, PointD cube, double cellSize, RoadRing ring)
        {
            this.route = route ?? throw new ArgumentNullException(nameof(route));
            this.ring = ring;
            Cube = cube;
            CarScale = ScaleFor(cellSize);
            X = route.Start.X;
            Y = route.Start.Y;
            Angle = -Math.PI / 2;
            path = route.Out;
            State = TruckState.Out;
        }

        public TruckRoute Route => route;
        public int Cell => route.Target;
        public PointD Cube { get; }
        public double CarScale { get; }

        public TruckState State { get; private set; }
        /// <summary>Position on the route (the route's center line), design units.</summary>
        public double X { get; private set; }
        public double Y { get; private set; }
        /// <summary>Heading, radians, in design space (0 = +x, π/2 = +y, i.e. down the screen).</summary>
        public double Angle { get; private set; }
        /// <summary>Index of the waypoint it is heading for.</summary>
        public int Step { get; private set; }
        /// <summary>Seconds of loading left.</summary>
        public double Wait { get; private set; }
        /// <summary>It has its cube (from arrival on).</summary>
        public bool HasCargo { get; private set; }
        /// <summary>Distance driven so far, design units (for wheel spin).</summary>
        public double Odometer { get; private set; }

        public IReadOnlyList<PointD> Path => path;

        /// <summary>main.ts carScale: trucks shrink with the cubes so one still fits beside each cube (11 wide).</summary>
        public static double ScaleFor(double cellSize) => Math.Min(1, Math.Max(0.5, cellSize / 13));

        /// <summary>
        /// Moves the truck on by <paramref name="dt"/> seconds of simulation time (main.ts: the frame time times the
        /// speed). Returns what happened, in order: it may arrive, load and leave within one long step.
        /// </summary>
        public TruckEvents Advance(double dt)
        {
            if (dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            var events = TruckEvents.None;
            if (State == TruckState.Gone) return events;
            if (State == TruckState.Load)
            {
                Angle = TurnToward(Angle, Math.Atan2(Cube.Y - Y, Cube.X - X), LoadTurnRate * dt);
                Wait -= dt;
                if (Wait <= 0)
                {
                    // Drive back out the way it came, then down the ramp.
                    State = TruckState.Home;
                    path = route.Home;
                    Step = 0;
                    events |= TruckEvents.Loaded;
                }
                return events;
            }
            events |= Drive(Speed * dt);
            if (State == TruckState.Out || State == TruckState.Home)
            {
                PointD next = path[Math.Min(Step, path.Count - 1)];
                if (Step < path.Count && Hypot(next.X - X, next.Y - Y) > 0.5)
                    Angle = TurnToward(Angle, Math.Atan2(next.Y - Y, next.X - X), DriveTurnRate * dt);
            }
            return events;
        }

        TruckEvents Drive(double dist)
        {
            while (dist > 0 && Step < path.Count)
            {
                PointD t = path[Step];
                double d = Hypot(t.X - X, t.Y - Y);
                if (d > dist)
                {
                    X += (t.X - X) / d * dist;
                    Y += (t.Y - Y) / d * dist;
                    Odometer += dist;
                    return TruckEvents.None;
                }
                X = t.X;
                Y = t.Y;
                Odometer += d;
                dist -= d;
                Step++;
            }
            if (Step < path.Count) return TruckEvents.None;
            if (State == TruckState.Out)
            {
                State = TruckState.Load;
                Wait = LoadTime;
                HasCargo = true;
                return TruckEvents.Arrived;
            }
            State = TruckState.Gone;
            return TruckEvents.Gone;
        }

        /// <summary>main.ts turnToward: from one heading toward another by at most maxStep, the short way round.</summary>
        public static double TurnToward(double from, double to, double maxStep)
        {
            double diff = to - from;
            while (diff > Math.PI) diff -= Math.PI * 2;
            while (diff < -Math.PI) diff += Math.PI * 2;
            return from + Math.Max(-maxStep, Math.Min(maxStep, diff));
        }

        /// <summary>main.ts laneShare: how far to keep right here, full on the road and the driveway, none in the field.</summary>
        public double LaneShare(double x, double y)
        {
            double driveway = SimConstants.RampLanes[SimConstants.RampLanes.Count - 1] + 14;
            if (y > ring.Y1 && Math.Abs(x - SimConstants.TunnelX) <= driveway) return 1;
            double d = double.PositiveInfinity;
            if (x >= ring.X0 && x <= ring.X1) d = Math.Min(d, Math.Min(Math.Abs(y - ring.Y0), Math.Abs(y - ring.Y1)));
            if (y >= ring.Y0 && y <= ring.Y1) d = Math.Min(d, Math.Min(Math.Abs(x - ring.X0), Math.Abs(x - ring.X1)));
            return Math.Max(0, Math.Min(1, (RoadLayout.RoadWidth / 2 + 2 - d) / 4));
        }

        /// <summary>main.ts carPose: where the truck is drawn, shifted to its own (right-hand) lane on the road.</summary>
        public PointD Pose()
        {
            double lane = Lane * CarScale * LaneShare(X, Y);
            return new PointD(X - Math.Sin(Angle) * lane, Y + Math.Cos(Angle) * lane);
        }

        /// <summary>0 down in the tunnel, 1 fully out (main.ts: it grows climbing out of the ramp, shrinks rolling back down).</summary>
        public double Fade
        {
            get
            {
                double fade = 1;
                if (State == TruckState.Out && Step == 0) fade = Hypot(X - route.Start.X, Y - route.Start.Y) / RampFade;
                if (State == TruckState.Home && Step == path.Count - 1)
                {
                    PointD end = path[path.Count - 1];
                    fade = Hypot(end.X - X, end.Y - Y) / RampFade;
                }
                if (State == TruckState.Gone) fade = 0;
                return Math.Min(1, fade);
            }
        }

        /// <summary>Hidden while still in the tunnel (main.ts: fade ≤ 0.02 is not drawn).</summary>
        public bool Visible => Fade > 0.02;

        /// <summary>Drawn size relative to a scale-1 truck (main.ts: carScale · (0.55 + 0.45 · fade)).</summary>
        public double DrawScale => CarScale * (0.55 + 0.45 * Fade);

        /// <summary>How far through loading (0..1); 1 outside the Load state.</summary>
        public double LoadProgress => State == TruckState.Load ? 1 - Wait / LoadTime : 1;

        /// <summary>The cube shows in the bed: from LAND_AT through the load on.</summary>
        public bool CargoVisible => HasCargo && (State != TruckState.Load || LoadProgress >= LandAt);

        static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
    }
}
