using CubeHaul.Domain;

namespace CubeHaul.Levels
{
    public static class BoardFactory
    {
        /// <summary>A fresh Board for this level's picture and edges (cube-hunt: new Board(level.picture, level.edges)).</summary>
        public static Board CreateBoard(this LevelDefinition level)
        {
            PictureDefinition p = level.Picture;
            return new Board(p.Width, p.Height, p.Cells, p.Palette.Length, level.Edges);
        }
    }
}
