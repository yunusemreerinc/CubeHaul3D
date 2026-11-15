using CubeHaul.Levels.Json;

namespace CubeHaul.Levels
{
    /// <summary>Reads LevelDefinition JSON text: parse, validate, build. Never throws for bad input.</summary>
    public static class LevelDefinitionReader
    {
        /// <summary>Parses and validates LevelDefinition JSON (UTF-8 text; a leading BOM is ignored).</summary>
        public static LevelReadResult Read(string text)
        {
            if (text == null) text = "";
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            JsonValue root;
            try
            {
                root = JsonParser.Parse(text);
            }
            catch (JsonParseException e)
            {
                return new LevelReadResult(null, new[] { new LevelIssue(IssueCodes.JsonSyntax, "", e.Message) }, new LevelIssue[0]);
            }
            return LevelValidator.Validate(root);
        }
    }
}
