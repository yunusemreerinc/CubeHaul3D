using CubeHaul.Domain;

namespace CubeHaul.Levels.Generation
{
    public static class PeelLayers
    {
        /// <summary>
        /// cube-hunt peelLayers: for every cell, the "layer" in which it becomes reachable when the picture is peeled one
        /// full ring at a time; low = available early. -1 for empty cells.
        /// </summary>
        public static int[] Of(PictureDefinition picture, OpenEdges edges)
        {
            Board board = picture.CreateBoard(edges);
            var layer = new int[picture.Cells.Length];
            for (int i = 0; i < layer.Length; i++) layer[i] = -1;
            int colors = picture.Palette.Length;
            var ring = new int[picture.Cells.Length];
            for (int l = 0; board.Remaining > 0; l++)
            {
                // A snapshot of every reachable cube, then eat them all. The order within a ring does not change the layers.
                int n = 0;
                for (int c = 0; c < colors; c++)
                    foreach (int i in board.ReachableCells(c)) ring[n++] = i;
                for (int k = 0; k < n; k++)
                {
                    layer[ring[k]] = l;
                    board.Eat(ring[k]);
                }
            }
            return layer;
        }
    }
}
