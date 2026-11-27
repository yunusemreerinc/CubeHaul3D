using System;
using System.Collections.Generic;
using CubeHaul.Domain;
using CubeHaul.Gameplay.Roads;
using CubeHaul.Gameplay.Trucks;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>
    /// Every truck of a level (main.ts sendTruck, the ramp lanes and the car loop; Task 15, docs/SESSION.md). A truck
    /// sent for a claimed cube takes the ramp lane with the cheapest route and waits in that lane's queue; each lane lets
    /// one truck up every CONVOY_GAP · carScale / CAR_SPEED seconds, so they leave in a spaced-out line. On the road
    /// trucks never wait for each other. When a truck reaches its cube, the game collects it right then (main.ts
    /// pickUp → Game.collect): only from that moment does the space behind it open up. Then it loads and drives home.
    /// </summary>
    public sealed class TruckFleet
    {
        readonly RoadPathPlanner roads;
        readonly Queue<TruckMotion>[] ramp;
        readonly double[] rampTimer;
        readonly List<TruckMotion> cars = new List<TruckMotion>(128);
        readonly List<TruckMotion> returned = new List<TruckMotion>(8);

        public TruckFleet(RoadPathPlanner roads)
        {
            this.roads = roads ?? throw new ArgumentNullException(nameof(roads));
            ramp = new Queue<TruckMotion>[RoadLayout.LaneCount];
            for (int k = 0; k < ramp.Length; k++) ramp[k] = new Queue<TruckMotion>();
            rampTimer = new double[RoadLayout.LaneCount];
            CarScale = TruckMotion.ScaleFor(roads.CellSize);
            // main.ts: (CONVOY_GAP * carScale()) / CAR_SPEED, in that order.
            LaneSpacing = SimConstants.ConvoyGap * CarScale / SimConstants.CarSpeed;
        }

        public RoadPathPlanner Roads => roads;
        public double CarScale { get; }
        /// <summary>Simulation seconds between two trucks leaving the same ramp lane.</summary>
        public double LaneSpacing { get; }

        /// <summary>Trucks on the road (driving, loading, driving home), in update order.</summary>
        public IReadOnlyList<TruckMotion> OnRoad => cars;
        public int OnRoadCount => cars.Count;
        public int QueuedCount
        {
            get
            {
                int n = 0;
                foreach (Queue<TruckMotion> q in ramp) n += q.Count;
                return n;
            }
        }

        /// <summary>main.ts trucksOut: on the road or still waiting in a ramp lane.</summary>
        public int Count => cars.Count + QueuedCount;

        public int QueuedIn(int lane) => ramp[lane].Count;
        public Queue<TruckMotion>.Enumerator Queue(int lane) => ramp[lane].GetEnumerator();

        /// <summary>A truck sent out: on the road this step.</summary>
        public event Action<TruckMotion> Launched;
        /// <summary>A truck reached its cube, and the game has just collected it.</summary>
        public event Action<TruckMotion> Collected;
        /// <summary>A truck finished loading and turned for home.</summary>
        public event Action<TruckMotion> Loaded;
        /// <summary>A truck is back down in the tunnel.</summary>
        public event Action<TruckMotion> Returned;

        /// <summary>main.ts sendTruck: plans the truck for this dispatch and queues it in its ramp lane.</summary>
        public TruckMotion Send(Dispatch d)
        {
            TruckRoute route = roads.PlanTruck(d.Cell);
            var truck = new TruckMotion(route, roads.Center(d.Cell), roads.CellSize, roads.Ring, d.Slot, d.Color);
            ramp[route.Lane].Enqueue(truck);
            return truck;
        }

        /// <summary>main.ts: each lane's timer counts down; a lane whose time is up lets its next truck out.</summary>
        public void AdvanceRamp(double simSeconds)
        {
            for (int k = 0; k < ramp.Length; k++)
            {
                rampTimer[k] -= simSeconds;
                if (ramp[k].Count > 0 && rampTimer[k] <= 0)
                {
                    TruckMotion t = ramp[k].Dequeue();
                    cars.Add(t);
                    rampTimer[k] = LaneSpacing;
                    Launched?.Invoke(t);
                }
            }
        }

        /// <summary>
        /// main.ts car loop: every truck on the road moves on by <paramref name="simSeconds"/>; a truck reaching its cube
        /// collects it in <paramref name="game"/> at once; the trucks back in the tunnel leave the fleet after the loop.
        /// </summary>
        public void AdvanceCars(double simSeconds, GameEngine game)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            int gone = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                TruckMotion t = cars[i];
                TruckEvents e = t.Advance(simSeconds);
                if ((e & TruckEvents.Arrived) != 0)
                {
                    game.Collect(t.Cell);
                    Collected?.Invoke(t);
                }
                if ((e & TruckEvents.Loaded) != 0) Loaded?.Invoke(t);
                if ((e & TruckEvents.Gone) != 0) gone++;
            }
            if (gone == 0) return;
            // main.ts: cars = cars.filter(c => c.state !== "gone"), keeping the order; then report them.
            returned.Clear();
            int j = 0;
            for (int i = 0; i < cars.Count; i++)
            {
                TruckMotion t = cars[i];
                if (t.State == TruckState.Gone) returned.Add(t);
                else cars[j++] = t;
            }
            cars.RemoveRange(j, cars.Count - j);
            for (int i = 0; i < returned.Count; i++) Returned?.Invoke(returned[i]);
            returned.Clear();
        }

        /// <summary>Every truck gone at once (a restart): none is reported as returned.</summary>
        public void Clear()
        {
            cars.Clear();
            foreach (Queue<TruckMotion> q in ramp) q.Clear();
            Array.Clear(rampTimer, 0, rampTimer.Length);
        }
    }
}
