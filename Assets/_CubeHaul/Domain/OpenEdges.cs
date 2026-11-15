namespace CubeHaul.Domain
{
    /// <summary>
    /// Board edges trucks can drive in from (cube-hunt core/types.ts OpenEdges). The bottom edge, where the ramp is,
    /// is always open (B-02). JSON: "all", "sides", "bottom".
    /// </summary>
    public enum OpenEdges
    {
        /// <summary>Every edge: bottom, left, right and top.</summary>
        All,
        /// <summary>Bottom, left and right; a wall along the top.</summary>
        Sides,
        /// <summary>Only the bottom; walls on the other three sides.</summary>
        Bottom,
    }
}
