using System;
using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;
using CubeHaul.Levels;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>
    /// One level being played with trucks (Task 15, docs/SESSION.md): the rules (<see cref="GameEngine"/>) joined to
    /// main.ts's clock, engine-free. <see cref="Advance"/> is one frame of main.ts App.update, in the same order:
    /// <list type="number">
    /// <item>slot cooldowns count down (simulation time = real time × speed);</item>
    /// <item>while Playing, GameEngine.Tick with the ready slots: each claimed cube gets a truck (cheapest ramp lane), the
    /// slot waits POP_INTERVAL, an emptied crate leaves;</item>
    /// <item>each ramp lane lets its next truck out when its spacing is up;</item>
    /// <item>the trucks move; one reaching its cube collects it (GameEngine.Collect) right then, loads, drives home;</item>
    /// <item>trucks back in the tunnel leave; tapped crates fly on in real time and land.</item>
    /// </list>
    /// So a cube is claimed by exactly one truck and leaves the board only when that truck picks it up; the cubes
    /// behind it stay closed until then. The speed (1x, 2x, 3x) scales driving, loading, cooldowns and lane spacing,
    /// but not the crate flight (B-31, B-32). The same calls with the same frame times give the same game as
    /// cube-hunt (golden sessions/*.json).
    /// </summary>
    public sealed class GameSession
    {
        readonly List<Dispatch> sent = new List<Dispatch>(SimConstants.MaxSlots);
        readonly List<int> landed = new List<int>(SimConstants.MaxSlots);

        public GameSession(GameEngine game)
        {
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Roads = RoadPathPlanner.ForBoard(game.Board);
            Scheduler = new DispatchScheduler();
            Fleet = new TruckFleet(Roads);
        }

        /// <summary>A fresh game of this level.</summary>
        public static GameSession For(LevelDefinition level) => new GameSession(level.CreateGame());

        public GameEngine Game { get; }
        public RoadPathPlanner Roads { get; }
        public DispatchScheduler Scheduler { get; }
        public TruckFleet Fleet { get; }

        /// <summary>1, 2 or 3 (main.ts speed button).</summary>
        public int Speed { get; private set; } = 1;
        /// <summary>Real seconds since the level started.</summary>
        public double RealTime { get; private set; }
        /// <summary>Simulation seconds (each frame's real time × the speed).</summary>
        public double SimTime { get; private set; }
        public long Frames { get; private set; }
        /// <summary>The +1 Slot booster was used (once per level, B-30).</summary>
        public bool SlotBoostUsed { get; private set; }
        /// <summary>Real seconds since the game ended and the last truck came home (main.ts overlayDelay, B-36).</summary>
        public double OverlayDelay { get; private set; }

        /// <summary>Trucks on the road or waiting in the ramp (main.ts trucksOut).</summary>
        public int TrucksOut => Fleet.Count;
        /// <summary>Won or stuck, and every truck is home.</summary>
        public bool IsOver => Game.Status != GameStatus.Playing && Fleet.Count == 0;
        /// <summary>The end panel would be showing (main.ts: overlayDelay has reached 0.4 s).</summary>
        public bool ShowsEndPanel => OverlayDelay >= SimConstants.EndPanelDelay;
        /// <summary>
        /// The +1 Slot booster is still there: once per level, up to MAX_SLOTS, not after a win (main.ts canSlot, B-30).
        /// Free for now: no ad or purchase stands in front of it (Task 17).
        /// </summary>
        public bool CanAddSlot => !SlotBoostUsed && Game.State.SlotCount < SimConstants.MaxSlots && Game.Status != GameStatus.Won;

        /// <summary>A crate was tapped from a column into a slot (it now flies there).</summary>
        public event Action<int, int> CrateTapped;
        /// <summary>A tap was refused because every slot is full (main.ts shakes the tray).</summary>
        public event Action<int> TapRefused;
        /// <summary>A crate landed in its slot; it may send trucks from now on.</summary>
        public event Action<int> CrateLanded;
        /// <summary>A crate's last truck left: the slot (and the crate's color) is free again.</summary>
        public event Action<int, int> CrateEmptied;
        /// <summary>A truck was sent for a claimed cube; it waits in its ramp lane.</summary>
        public event Action<TruckMotion> TruckSent;
        public event Action SlotAdded;
        public event Action<int> SpeedChanged;

        /// <summary>Trucks leaving the ramp, collecting, loading, returning: see <see cref="TruckFleet"/>.</summary>
        public event Action<TruckMotion> TruckLaunched
        {
            add => Fleet.Launched += value;
            remove => Fleet.Launched -= value;
        }

        public event Action<TruckMotion> CubeCollected
        {
            add => Fleet.Collected += value;
            remove => Fleet.Collected -= value;
        }

        public event Action<TruckMotion> TruckLoaded
        {
            add => Fleet.Loaded += value;
            remove => Fleet.Loaded -= value;
        }

        public event Action<TruckMotion> TruckReturned
        {
            add => Fleet.Returned += value;
            remove => Fleet.Returned -= value;
        }

        public void SetSpeed(int speed)
        {
            if (speed < 1 || speed > 3) throw new ArgumentOutOfRangeException(nameof(speed), "1, 2 or 3");
            if (speed == Speed) return;
            Speed = speed;
            SpeedChanged?.Invoke(speed);
        }

        /// <summary>main.ts speed button: 1x → 2x → 3x → 1x.</summary>
        public int CycleSpeed()
        {
            SetSpeed(Speed % 3 + 1);
            return Speed;
        }

        /// <summary>
        /// main.ts tapColumn: the column's front crate goes to the first free slot and starts flying there. Refused
        /// (-1) for an empty column or when the game is over; with every slot full it is refused and reported.
        /// </summary>
        public int Tap(int col)
        {
            if (Game.State.Front(col) == null || Game.Status != GameStatus.Playing) return -1;
            if (!Game.State.HasFreeSlot())
            {
                TapRefused?.Invoke(col);
                return -1;
            }
            int slot = Game.Tap(col);
            if (slot < 0) return -1;
            Scheduler.CrateTapped(slot);
            CrateTapped?.Invoke(col, slot);
            return slot;
        }

        /// <summary>main.ts addSlot: the +1 Slot booster, once per level and up to MAX_SLOTS; un-sticks the game.</summary>
        public bool AddSlot()
        {
            if (!CanAddSlot) return false;
            SlotBoostUsed = true;
            Game.AddSlot();
            OverlayDelay = 0;
            SlotAdded?.Invoke();
            return true;
        }

        /// <summary>One frame of main.ts update(<paramref name="realSeconds"/>); see the class notes for the order.</summary>
        public void Advance(double realSeconds)
        {
            if (realSeconds < 0 || double.IsNaN(realSeconds)) throw new ArgumentOutOfRangeException(nameof(realSeconds));
            double sdt = realSeconds * Speed;
            RealTime += realSeconds;
            SimTime += sdt;
            Frames++;

            Scheduler.AdvanceCooldowns(sdt);
            if (Game.Status == GameStatus.Playing)
            {
                Game.Tick(Scheduler.Ready, sent);
                for (int k = 0; k < sent.Count; k++)
                {
                    Dispatch d = sent[k];
                    TruckMotion truck = Fleet.Send(d);
                    Scheduler.TruckSent(d.Slot);
                    TruckSent?.Invoke(truck);
                    // That was the crate's last truck: it leaves the dock.
                    if (Game.State.SlotAt(d.Slot) == null) CrateEmptied?.Invoke(d.Slot, d.Color);
                }
            }

            Fleet.AdvanceRamp(sdt);
            Fleet.AdvanceCars(sdt, Game);

            landed.Clear();
            Scheduler.AdvanceFlights(realSeconds, landed);
            for (int k = 0; k < landed.Count; k++) CrateLanded?.Invoke(landed[k]);

            if (Game.Status != GameStatus.Playing && Fleet.Count == 0) OverlayDelay += realSeconds;
        }
    }
}
