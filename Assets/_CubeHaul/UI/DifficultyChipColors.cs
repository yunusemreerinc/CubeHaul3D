using CubeHaul.Levels;
using UnityEngine;

namespace CubeHaul.UI
{
    /// <summary>The difficulty chip colors of cube-hunt's top bar (main.ts drawTopBar, B-25).</summary>
    public static class DifficultyChipColors
    {
        public static Color For(DifficultyLabel label)
        {
            switch (label)
            {
                case DifficultyLabel.Easy: return new Color32(0x5c, 0xc4, 0x64, 0xff);
                case DifficultyLabel.Medium: return new Color32(0xf2, 0xb4, 0x3c, 0xff);
                case DifficultyLabel.Hard: return new Color32(0xf0, 0x80, 0x3c, 0xff);
                default: return new Color32(0xe8, 0x42, 0x4f, 0xff);
            }
        }
    }
}
