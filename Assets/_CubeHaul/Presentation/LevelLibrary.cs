using System.Collections.Generic;
using CubeHaul.Levels;
using UnityEngine;

namespace CubeHaul.Presentation
{
    /// <summary>
    /// The game's level files as TextAssets, in play order (Assets ▸ Create ▸ CubeHaul ▸ Level Library). Unity-side
    /// adapter for <see cref="LevelRepository"/>, which does the reading and validation without the engine.
    /// </summary>
    [CreateAssetMenu(menuName = "CubeHaul/Level Library", fileName = "LevelLibrary")]
    public sealed class LevelLibrary : ScriptableObject
    {
        [SerializeField] TextAsset[] levels = new TextAsset[0];

        /// <summary>Reads and validates every level. Check <see cref="LevelRepository.Errors"/>, or call ThrowIfErrors().</summary>
        public LevelRepository Load()
        {
            var sources = new List<LevelSource>();
            foreach (TextAsset asset in levels)
                if (asset != null) sources.Add(new LevelSource(asset.name, asset.text));
            return LevelRepository.Load(sources);
        }
    }
}
