using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TestConsole.Integration.Mastodon;

// see https://www.rfc-editor.org/rfc/rfc7033#section-4.4
public record class JsonResourceDescriptor(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("aliases")] string[]? Aliases,
    [property: JsonPropertyName("properties")] Dictionary<string, string?>? Properties,
    [property: JsonPropertyName("links")] JsonRdLink[]? Links
);

// see https://www.rfc-editor.org/rfc/rfc7033#section-4.4.4
public record class JsonRdLink(
    [property: JsonPropertyName("rel")] string? Rel,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("href")] string? Href,
    [property: JsonPropertyName("titles")] Dictionary<string, string>? Titles,
    [property: JsonPropertyName("properties")] Dictionary<string, string?>? Properties
);
