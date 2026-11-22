namespace CubeHaul.Domain
{
    /// <summary>What a tray crate starts as: an id, a color and how many trucks it holds (cube-hunt Block, as data).</summary>
    public readonly struct CrateSpec
    {
        public int Id { get; }
        public int Color { get; }
        public int Count { get; }

        public CrateSpec(int id, int color, int count)
        {
            Id = id;
            Color = color;
            Count = count;
        }
    }

    /// <summary>
    /// A crate in play (cube-hunt Block): <see cref="Count"/> trucks of one color, each hauling away one cube.
    /// Only the engine spends trucks; outside code can read but not change it.
    /// </summary>
    public sealed class Crate
    {
        public int Id { get; }
        public int Color { get; }
        public int Count { get; internal set; }

        internal Crate(CrateSpec spec)
        {
            Id = spec.Id;
            Color = spec.Color;
            Count = spec.Count;
        }

        public override string ToString() => $"crate {Id}: color {Color} x {Count}";
    }

    /// <summary>One truck sent from a slot to haul away a cell (cube-hunt Dispatch).</summary>
    public readonly struct Dispatch
    {
        public int Slot { get; }
        public int Cell { get; }
        public int Color { get; }

        public Dispatch(int slot, int cell, int color)
        {
            Slot = slot;
            Cell = cell;
            Color = color;
        }

        public override string ToString() => $"slot {Slot} -> cell {Cell} (color {Color})";
    }
}
