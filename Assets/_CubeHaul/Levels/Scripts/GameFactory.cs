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
            if (level.Tray == null) throw new InvalidOperationException($"level \"{level.LevelId}\" has no static tray; generate one first");
            var columns = new List<IReadOnlyList<CrateSpec>>(level.Tray.Columns.Length);
            foreach (BlockDefinition[] col in level.Tray.Columns)
            {
                var specs = new CrateSpec[col.Length];
                for (int k = 0; k < col.Length; k++) specs[k] = new CrateSpec(col[k].Id, col[k].Color, col[k].Count);
                columns.Add(specs);
            }
            return new GameEngine(level.CreateBoard(), columns, level.SlotCount);
        }
    }
}
