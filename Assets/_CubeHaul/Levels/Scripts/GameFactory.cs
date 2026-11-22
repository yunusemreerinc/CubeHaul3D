using System;
using System.Collections.Generic;
using CubeHaul.Domain;

namespace CubeHaul.Levels
{
    public static class GameFactory
    {
        /// <summary>
        /// A new game of this level (cube-hunt: new Game(level)): a fresh board and a copy of the static tray. The level
        /// itself is never changed by playing.
        /// </summary>
        public static GameEngine CreateGame(this LevelDefinition level)
        {
            if (level.Tray == null) throw new InvalidOperationException($"level \"{level.LevelId}\" has no static tray; bake one first (LevelBaker)");
            return CreateGame(level.Picture, level.Edges, level.SlotCount, level.Tray);
        }

        /// <summary>A new game of this picture and tray.</summary>
        public static GameEngine CreateGame(PictureDefinition picture, OpenEdges edges, int slotCount, TrayDefinition tray)
        {
            if (picture == null) throw new ArgumentNullException(nameof(picture));
            if (tray == null) throw new ArgumentNullException(nameof(tray));
            return new GameEngine(picture.CreateBoard(edges), Specs(tray), slotCount);
        }

        /// <summary>The tray as engine input.</summary>
        public static List<IReadOnlyList<CrateSpec>> Specs(TrayDefinition tray)
        {
            var columns = new List<IReadOnlyList<CrateSpec>>(tray.Columns.Length);
            foreach (BlockDefinition[] col in tray.Columns)
            {
                var specs = new CrateSpec[col.Length];
                for (int k = 0; k < col.Length; k++) specs[k] = new CrateSpec(col[k].Id, col[k].Color, col[k].Count);
                columns.Add(specs);
            }
            return columns;
        }
    }
}
