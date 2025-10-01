using System;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole;

public static class LexemeBuiltInDataTypes
{
    public static WikibaseDataType WikibaseLexeme { get; }
        = new DelegatePropertyType<string>("wikibase-lexeme", "wikibase-entityid",
            EntityIdFromJson, EntityIdToJson);

    public static WikibaseDataType WikibaseForm { get; }
        = new DelegatePropertyType<string>("wikibase-form", "wikibase-entityid",
            EntityIdFromJson, EntityIdToJson);

    public static WikibaseDataType WikibaseSense { get; }
        = new DelegatePropertyType<string>("wikibase-sense", "wikibase-entityid",
            EntityIdFromJson, EntityIdToJson);

    private static string EntityIdFromJson(JsonNode value)
    {
        string? id = null;
        value["id"]?.AsValue().TryGetValue(out id);
        if (id != null) return id;
        string? type = null;
        value["entity-type"]?.AsValue().TryGetValue(out type);
        switch (type)
        {
            case "item":
                id = "Q";
                break;
            case "property":
                id = "P";
                break;
            case "lexeme":
                id = "L";
                break;
            default:
                throw new ArgumentException("Invalid entity-type: " + type + ".", nameof(value));
        }
        string? numericId = null;
        value["numeric-id"]?.AsValue().TryGetValue(out id);
        id += numericId ?? throw new ArgumentException("Missing or invalid numeric-id.", nameof(value));
        return id;
    }

    private static JsonNode EntityIdToJson(string id)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        id = id.Trim();
        if (id.Length < 2) throw new ArgumentException("Invalid entity identifier.", nameof(id));
        var hyphen = id.IndexOf('-');
        var value = new JsonObject();
        if (hyphen < 0)
        {
            if (id[0] != 'L') throw new ArgumentException("Unsupported entity identifier format: " + id, nameof(id));
            value.Add("entity-type", "lexeme");
            value.Add("numeric-id", Int32.Parse(id));
            return value;
        }

        var kind = id[hyphen + 1];
        switch (kind)
        {
            case 'S':
                value.Add("entity-type", "sense");
                value.Add("id", id);
                break;
            case 'F':
                value.Add("entity-type", "form");
                value.Add("id", id);
                break;
            default:
                throw new ArgumentException("Unsupported entity identifier format: " + id, nameof(id));
        }
        return value;
    }

    internal sealed class DelegatePropertyType<T>(string name, string valueTypeName, Func<JsonNode, T> parseHandler, Func<T, JsonNode> toJsonHandler)
        : WikibaseDataType
        where T : class
    {
        private readonly Func<JsonNode, T> parseHandler = parseHandler ?? throw new ArgumentNullException(nameof(parseHandler));
        private readonly Func<T, JsonNode> toJsonHandler = toJsonHandler ?? throw new ArgumentNullException(nameof(toJsonHandler));

        public DelegatePropertyType(string name, Func<JsonNode, T> parseHandler, Func<T, JsonNode> toJsonHandler)
            : this(name, name, parseHandler, toJsonHandler)
        {
        }

        /// <inheritdoc />
        public override string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

        /// <inheritdoc />
        public override string ValueTypeName { get; } = valueTypeName ?? throw new ArgumentNullException(nameof(valueTypeName));

        /// <inheritdoc />
        public override Type MappedType => typeof(T);

        public override object Parse(JsonNode expr) => parseHandler(expr);

        public override JsonNode ToJson(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value is T t)
                return toJsonHandler(t);
            throw new ArgumentException($"Value type is incompatible for {Name}: expected {typeof(T)}, received {value.GetType()}.", nameof(value));
        }
    }
}