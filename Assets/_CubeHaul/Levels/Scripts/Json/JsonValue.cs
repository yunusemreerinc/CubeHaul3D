using System;
using System.Collections.Generic;

namespace CubeHaul.Levels.Json
{
    public enum JsonKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>
    /// A parsed JSON value. Numbers are IEEE doubles, as in JavaScript, so both languages see the same values.
    /// Object members keep the order of their first appearance; a repeated key keeps that place and takes the
    /// last value, which is what JavaScript's JSON.parse does.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind { get; private set; }
        public bool Boolean { get; private set; }
        public double Number { get; private set; }
        public string String { get; private set; }

        readonly List<JsonValue> items;
        readonly List<KeyValuePair<string, JsonValue>> members;

        JsonValue(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Array) items = new List<JsonValue>();
            if (kind == JsonKind.Object) members = new List<KeyValuePair<string, JsonValue>>();
        }

        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);

        public static JsonValue FromBoolean(bool value) => new JsonValue(JsonKind.Boolean) { Boolean = value };
        public static JsonValue FromNumber(double value) => new JsonValue(JsonKind.Number) { Number = value };
        public static JsonValue FromString(string value) => new JsonValue(JsonKind.String) { String = value };
        public static JsonValue NewArray() => new JsonValue(JsonKind.Array);
        public static JsonValue NewObject() => new JsonValue(JsonKind.Object);

        public bool IsObject => Kind == JsonKind.Object;
        public bool IsArray => Kind == JsonKind.Array;

        /// <summary>A finite number with no fractional part (1, 1.0 and 1e0 all count), like Number.isInteger.</summary>
        public bool IsInteger => Kind == JsonKind.Number && !double.IsNaN(Number) && !double.IsInfinity(Number) && Math.Floor(Number) == Number;

        public IReadOnlyList<JsonValue> Items => items ?? throw new InvalidOperationException("not an array");
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => members ?? throw new InvalidOperationException("not an object");

        public bool Has(string key) => IndexOf(key) >= 0;

        public JsonValue Get(string key)
        {
            int i = IndexOf(key);
            return i < 0 ? null : members[i].Value;
        }

        internal void Add(JsonValue item) => items.Add(item);

        internal void Set(string key, JsonValue value)
        {
            int i = IndexOf(key);
            if (i >= 0) members[i] = new KeyValuePair<string, JsonValue>(key, value);
            else members.Add(new KeyValuePair<string, JsonValue>(key, value));
        }

        int IndexOf(string key)
        {
            if (members == null) return -1;
            for (int i = 0; i < members.Count; i++)
                if (string.Equals(members[i].Key, key, StringComparison.Ordinal)) return i;
            return -1;
        }

        public string TypeName
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.Null: return "null";
                    case JsonKind.Boolean: return "boolean";
                    case JsonKind.Number: return "number";
                    case JsonKind.String: return "string";
                    case JsonKind.Array: return "array";
                    default: return "object";
                }
            }
        }
    }
}
