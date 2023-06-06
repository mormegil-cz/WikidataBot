using System;
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

    private static string EntityIdFromJson(JToken value)
    {
        var id = (string)value["id"];
        if (id != null) return id;
        var type = (string)value["entity-type"];
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
        id += (string)value["numeric-id"];
        return id;
    }

    private static JToken EntityIdToJson(string id)
    {
        if (id == null) throw new ArgumentNullException(nameof(id));
        id = id.Trim();
        if (id.Length < 2) throw new ArgumentException("Invalid entity identifier.", nameof(id));
        var hyphen = id.IndexOf('-');
        var value = new JObject();
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

    internal sealed class DelegatePropertyType<T> : WikibaseDataType
    {
        private readonly Func<JToken, T> parseHandler;
        private readonly Func<T, JToken> toJsonHandler;

        public DelegatePropertyType(string name, Func<JToken, T> parseHandler, Func<T, JToken> toJsonHandler)
            : this(name, name, parseHandler, toJsonHandler)
        {
        }

        public DelegatePropertyType(string name, string valueTypeName, Func<JToken, T> parseHandler, Func<T, JToken> toJsonHandler)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ValueTypeName = valueTypeName ?? throw new ArgumentNullException(nameof(valueTypeName));
            this.parseHandler = parseHandler ?? throw new ArgumentNullException(nameof(parseHandler));
            this.toJsonHandler = toJsonHandler ?? throw new ArgumentNullException(nameof(toJsonHandler));
        }

        /// <inheritdoc />
        public override string Name { get; }

        /// <inheritdoc />
        public override string ValueTypeName { get; }

        /// <inheritdoc />
        public override Type MappedType => typeof(T);

        public override object Parse(JToken expr)
        {
            return parseHandler(expr);
        }

        public override JToken ToJson(object value)
        {
            if (value == null) return null;
            if (value is T t)
                return toJsonHandler(t);
            throw new ArgumentException($"Value type is incompatible for {Name}: expected {typeof(T)}, received {value.GetType()}.", nameof(value));
        }
    }
}