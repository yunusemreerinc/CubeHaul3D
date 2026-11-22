using CubeHaul.Domain;

namespace CubeHaul.Levels
{
    public static class BoardFactory
    {
        /// <summary>A fresh Board for this level's picture and edges (cube-hunt: new Board(level.picture, level.edges)).</summary>
        public static Board CreateBoard(this LevelDefinition level) => level.Picture.CreateBoard(level.Edges);

        /// <summary>A fresh Board for this picture (cube-hunt: new Board(picture, edges)).</summary>
        public static Board CreateBoard(this PictureDefinition p, OpenEdges edges) => new Board(p.Width, p.Height, p.Cells, p.Palette.Length, edges);
    }
}
