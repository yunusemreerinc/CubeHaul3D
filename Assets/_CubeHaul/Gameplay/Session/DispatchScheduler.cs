using System;
using System.Collections.Generic;

namespace CubeHaul.Gameplay.Session
{
    /// <summary>
    /// When each slot may send its next truck (main.ts update, Task 15, docs/SESSION.md): a slot is ready once its
    /// cooldown has run out and its crate has landed. After a truck leaves, the slot waits POP_INTERVAL (0.15 s) of
    /// simulation time, so the cooldown shrinks with the speed (B-33). A tapped crate flies into its slot for
    /// 1 / FLY_SPEED seconds of <b>real</b> time, whatever the speed (B-32). Allocation-free after construction.
    /// </summary>
    public sealed class DispatchScheduler
    {
        /// <summary>main.ts keeps one timer per possible slot (MAX_SLOTS); slots past them are never ready.</summary>
        readonly double[] slotTimer = new double[SimConstants.MaxSlots];
        /// <summary>Crates flying into their slots: slot and progress (0..1), in tap order.</summary>
        readonly List<(int slot, double t)> flights = new List<(int, double)>(SimConstants.MaxSlots);

        /// <summary>The cached readiness test for GameEngine.Tick.</summary>
        public Func<int, bool> Ready { get; }

        public DispatchScheduler()
        {
            Ready = IsReady;
        }

        public int FlightCount => flights.Count;

        /// <summary>Seconds (simulation time) until this slot may send again; ≤ 0 when it may.</summary>
        public double CooldownOf(int slot) => slot >= 0 && slot < slotTimer.Length ? slotTimer[slot] : double.PositiveInfinity;

        /// <summary>main.ts ready(s): cooldown over and no crate still flying into it.</summary>
        public bool IsReady(int slot) => slot >= 0 && slot < slotTimer.Length && slotTimer[slot] <= 0 && !IsFlying(slot);

        public bool IsFlying(int slot)
        {
            for (int k = 0; k < flights.Count; k++)
                if (flights[k].slot == slot) return true;
            return false;
        }

        /// <summary>How far this slot's crate is into its flight (0..1), or 1 when none is flying.</summary>
        public double FlightOf(int slot)
        {
            for (int k = 0; k < flights.Count; k++)
                if (flights[k].slot == slot) return flights[k].t;
            return 1;
        }

        /// <summary>A crate was tapped into this slot: it starts flying.</summary>
        public void CrateTapped(int slot) => flights.Add((slot, 0));

        /// <summary>This slot just sent a truck: it waits POP_INTERVAL.</summary>
        public void TruckSent(int slot)
        {
            if (slot >= 0 && slot < slotTimer.Length) slotTimer[slot] = SimConstants.PopInterval;
        }

        /// <summary>Counts every slot's cooldown down by <paramref name="simSeconds"/> (main.ts: slotTimer[k] −= sdt).</summary>
        public void AdvanceCooldowns(double simSeconds)
        {
            for (int k = 0; k < slotTimer.Length; k++) slotTimer[k] -= simSeconds;
        }

        /// <summary>
        /// Moves the flights on by <paramref name="realSeconds"/>; the crates that land are written to
        /// <paramref name="landed"/> (slots, in tap order; not cleared first). Returns how many landed.
        /// </summary>
        public int AdvanceFlights(double realSeconds, List<int> landed)
        {
            int n = 0, kept = 0;
            for (int k = 0; k < flights.Count; k++)
            {
                (int slot, double t) = flights[k];
                t += realSeconds * SimConstants.FlySpeed;
                if (t >= 1)
                {
                    landed?.Add(slot);
                    n++;
                }
                else flights[kept++] = (slot, t);
            }
            flights.RemoveRange(kept, flights.Count - kept);
            return n;
        }

        /// <summary>Back to the start of a level: no cooldowns, nothing flying.</summary>
        public void Reset()
        {
            Array.Clear(slotTimer, 0, slotTimer.Length);
            flights.Clear();
        }
    }
}
