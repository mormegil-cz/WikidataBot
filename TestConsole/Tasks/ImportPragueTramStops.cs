using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiClientLibrary.Sites;

namespace TestConsole.Tasks;

using static WikidataTools;

public class ImportPragueTramStops
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Prague tram stops import", EditGroupId);
    private static readonly string[] Languages = { };

    // http://data.pid.cz/PID_GTFS.zip
    private static readonly DateOnly gtfsDownloadDate = new DateOnly(2023, 12, 5);
    private static readonly string gtfsFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PID_GTFS.zip");

    // https://data.pid.cz/stops/json/stops.json
    private static readonly DateOnly jsonDownloadDate = new DateOnly(2023, 12, 5);
    private static readonly string jsonFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "stops.json");

    public static async Task Run(WikiSite wikidataSite)
    {
        await Console.Out.WriteLineAsync("Loading stop data from JSON...");
        var (jsonFileDate, stopsData) = await LoadStops(jsonFilename);
        await Console.Out.WriteLineAsync($"Done, {stopsData.Count} stops loaded");

        var stopPerGtfsId = new Dictionary<string, string>(stopsData.Values.SelectMany(stop => stop.GtfsIds.Select(id => new KeyValuePair<string, string>(id, stop.Name))));

        await Console.Out.WriteLineAsync("Loading route data from GTFS...");
        var (gtfsFileDate, routeData) = await LoadGtfsRoutes(gtfsFilename);
        await Console.Out.WriteLineAsync($"Done, {routeData.Count} routes loaded");

        var neighbors = ComputeNeighbors(routeData, stopPerGtfsId, stopsData);

        await Console.Out.WriteLineAsync("Loading stop data from Wikidata...");
        var stopsInWikidata = await FindStopsAtWikidata();
        await Console.Out.WriteLineAsync($"Done, {stopsInWikidata.Count} stops loaded");

        var jsonNameSet = stopsData.Keys.ToHashSet();
        var wikidataNameSet = stopsInWikidata.Keys.ToHashSet();

        var missingInWikidata = new HashSet<string>(jsonNameSet);
        missingInWikidata.ExceptWith(wikidataNameSet);
        foreach (var missing in missingInWikidata) await Console.Error.WriteLineAsync("Missing in Wikidata: " + missing);

        var unknownInWikidata = new HashSet<string>(wikidataNameSet);
        unknownInWikidata.ExceptWith(jsonNameSet);
        foreach (var unknown in unknownInWikidata) await Console.Error.WriteLineAsync($"Unknown in Wikidata: {unknown} ({stopsInWikidata[unknown]})");

        if (missingInWikidata.Count > 0 || unknownInWikidata.Count > 0)
        {
            await Console.Error.WriteLineAsync("Errors found, exiting");
            return;
        }

        // P31 Q2175765 [ensured by query]
        // P16 Q1420119 [ensured by query]
        // P1448 name [ensured by query]

        // label.cs: name
        // label.en: name
        // description.cs: Tramvajová zastávka v Praze
        // description.en: Tram stop in Prague
        // aliases.cs: {names}
        // P17 Q213
        // P625 coords
        // P5817 Q55654238 (?)
    }

    private static Dictionary<string, HashSet<(string, string)>> ComputeNeighbors(Dictionary<string, GtfsRoute> routes, Dictionary<string, string> stopPerGtfsId, Dictionary<string, StopData> stopData)
    {
        // first, for each route, record the neighboring stations with the set of route positions
        var neighbors = new Dictionary<string, Dictionary<string, HashSet<PositionInGtfsRoute>>>(stopData.Count);
        foreach (var route in routes.Values)
        {
            var routeStops = route.Stops;
            var routeLength = routeStops.Count;
            var currentStopGtfs = routeStops[0];
            for (var routePos = 1; routePos < routeLength; ++routePos)
            {
                var nextStopGtfs = routeStops[routePos];

                var currentStopName = stopPerGtfsId[currentStopGtfs];
                if (!neighbors.TryGetValue(currentStopName, out var currentStopData)) neighbors.Add(currentStopName, currentStopData = new Dictionary<string, HashSet<PositionInGtfsRoute>>(5));

                if (!currentStopData.TryGetValue(nextStopGtfs, out var neighborStopData)) currentStopData.Add(nextStopGtfs, neighborStopData = new HashSet<PositionInGtfsRoute>(5));
                neighborStopData.Add(new PositionInGtfsRoute(route, routePos));

                currentStopGtfs = nextStopGtfs;
            }
        }

        // now from that result, compute for each stop its neighbors with the furthest common destination
        var result = new Dictionary<string, HashSet<(string, string)>>(neighbors.Count);
        foreach (var (stopName, stopRoutes) in neighbors)
        {
            var stopNeighbors = new HashSet<(string, string)>(stopRoutes.Count);
            result.Add(stopName, stopNeighbors);

            foreach (var (neighborGtfs, neighborRoutes) in stopRoutes)
            {
                var neighborStopName = stopPerGtfsId[neighborGtfs];
                var minRemainingRouteLength = neighborRoutes.Min(route => route.GtfsRoute.Stops.Count - route.Position);

                string? lastCommonName = null;
                for (var commonOffset = 0; commonOffset < minRemainingRouteLength; ++commonOffset)
                {
                    var firstRoute = neighborRoutes.First();
                    var firstStopName = stopPerGtfsId[firstRoute.GtfsRoute.Stops[firstRoute.Position + commonOffset]];
                    var isCommon = neighborRoutes.All(route =>
                        stopPerGtfsId[route.GtfsRoute.Stops[route.Position + commonOffset]] == firstStopName
                    );
                    if (isCommon) lastCommonName = firstStopName;
                    else break;
                }

                Debug.Assert(lastCommonName != null);

                stopNeighbors.Add((neighborStopName, lastCommonName));
            }
        }

        return result;
    }

    private static async Task<(DateOnly gtfsFileDate, Dictionary<string, GtfsRoute> routeData)> LoadGtfsRoutes(string zipFilename)
    {
    }

    private static async Task<Dictionary<string, string>> FindStopsAtWikidata()
    {
        var stops = GetEntities(await GetSparqlResults(@"
SELECT ?item ?name WHERE {
  ?item wdt:P16 wd:Q1420119;
        wdt:P31 wd:Q2175765;
        wdt:P1448 ?name.
}
"), new Dictionary<string, string> { { "item", "uri" }, { "name", "literal" } }).ToDictionary(row => row[1]!, row => GetEntityIdFromUri(row[0]!));
        return stops;
    }

    private static async Task<(DateOnly, Dictionary<string, StopData>)> LoadStops(string filename)
    {
        using var reader = File.OpenText(filename);
        await using var jsonReader = new JsonTextReader(reader);

        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.StartObject) throw new FormatException("Invalid JSON data");

        DateOnly? generatedAt = null;
        Dictionary<string, StopData>? stops = null;

        while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.PropertyName)
        {
            switch (jsonReader.Value)
            {
                case "generatedAt":
                    generatedAt = await ReadDateValue(jsonReader);
                    break;

                case "dataFormatVersion":
                    var version = await ReadStringValue(jsonReader);
                    if (version != "3") throw new FormatException("Unsupported version " + version);
                    break;

                case "stopGroups":
                    stops = await ReadTramStopGroups(jsonReader);
                    break;

                default:
                    throw new FormatException("Unsupported field " + jsonReader.Value);
            }
        }

        if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

        if (generatedAt == null || stops == null) throw new FormatException("Missing data in JSON");

        return (generatedAt.GetValueOrDefault(), stops);
    }

    private static async Task<long> ReadIntegerValue(JsonTextReader jsonReader)
    {
        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.Integer) throw new FormatException("Invalid JSON data");
        return (long) jsonReader.Value!;
    }

    private static async Task<decimal> ReadDecimalValue(JsonTextReader jsonReader)
    {
        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.Float) throw new FormatException("Invalid JSON data");
        return (decimal) (double) jsonReader.Value!;
    }

    private static async Task<string> ReadStringValue(JsonTextReader jsonReader)
    {
        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.String) throw new FormatException("Invalid JSON data");
        return (string) jsonReader.Value!;
    }

    private static async Task<DateOnly> ReadDateValue(JsonTextReader jsonReader)
    {
        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.Date) throw new FormatException("Invalid JSON data");
        var dateTime = (DateTime) jsonReader.Value!;
        return new DateOnly(dateTime.Year, dateTime.Month, dateTime.Day);
    }

    private static async Task<HashSet<string>> ReadStringSet(JsonTextReader jsonReader)
    {
        var result = new HashSet<string>();

        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");
        while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.String)
        {
            if (!result.Add((string) jsonReader.Value!)) throw new ArgumentException("Item already exists");
        }
        if (jsonReader.TokenType != JsonToken.EndArray) throw new FormatException("Invalid JSON data");

        return result;
    }

    private static async Task<Dictionary<string, StopData>> ReadTramStopGroups(JsonTextReader jsonReader)
    {
        var result = new Dictionary<string, StopData>();

        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");
        while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.StartObject)
        {
            string? name = null;
            string? districtCode = null;
            string? idosName = null;
            string? fullName = null;
            string? uniqueName = null;
            long? node = null;
            long? number = null;
            string? municipality = null;
            decimal? lat = null;
            decimal? lon = null;
            HashSet<LineAtStop>? lines = null;
            HashSet<string>? gtfsIds = null;
            bool? noTramStops = null;

            while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "name":
                        name = await ReadStringValue(jsonReader);
                        break;
                    case "districtCode":
                        districtCode = await ReadStringValue(jsonReader);
                        break;
                    case "idosName":
                        idosName = await ReadStringValue(jsonReader);
                        break;
                    case "fullName":
                        fullName = await ReadStringValue(jsonReader);
                        break;
                    case "uniqueName":
                        uniqueName = await ReadStringValue(jsonReader);
                        break;
                    case "node":
                        node = await ReadIntegerValue(jsonReader);
                        break;
                    case "cis":
                        number = await ReadIntegerValue(jsonReader);
                        break;
                    case "municipality":
                        municipality = await ReadStringValue(jsonReader);
                        break;
                    case "gtfsIds":
                        gtfsIds = await ReadStringSet(jsonReader);
                        break;
                    case "stops":
                        var stopsInGroup = await ReadTramStopsInGroup(jsonReader);
                        noTramStops = stopsInGroup == null;
                        if (stopsInGroup != null) (lat, lon, lines) = stopsInGroup.GetValueOrDefault();
                        break;
                    default:
                        await jsonReader.SkipAsync();
                        break;
                }
            }

            if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

            if (name == null || districtCode == null || idosName == null || fullName == null || uniqueName == null || node == null || number == null || municipality == null || gtfsIds == null || noTramStops == null) throw new FormatException("Incomplete JSON data");

            if (noTramStops.GetValueOrDefault()) continue;

            if (municipality != "Praha") throw new FormatException("Unexpected tram municipality " + municipality);
            if (districtCode != "AB") throw new FormatException("Unexpected tram district code " + districtCode);

            if (name != uniqueName) await Console.Out.WriteLineAsync($"Warning: Tram station name mismatch: {name} vs {uniqueName}");

            result.Add(name, new StopData(name, idosName, fullName, uniqueName, number.GetValueOrDefault(), node.GetValueOrDefault(), lat.GetValueOrDefault(), lon.GetValueOrDefault(), gtfsIds, lines!));
        }
        if (jsonReader.TokenType != JsonToken.EndArray) throw new FormatException("Invalid JSON data");

        return result;
    }

    private static async Task<(decimal, decimal, HashSet<LineAtStop>)?> ReadTramStopsInGroup(JsonTextReader jsonReader)
    {
        var stopCount = 0;
        var sumLat = 0.0m;
        var sumLon = 0.0m;
        var linesInGroup = new HashSet<LineAtStop>();

        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");

        while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.StartObject)
        {
            decimal? lat = null;
            decimal? lon = null;
            HashSet<LineAtStop>? lines = null;
            bool? hasTramLine = null;

            while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "lat":
                        lat = await ReadDecimalValue(jsonReader);
                        break;
                    case "lon":
                        lon = await ReadDecimalValue(jsonReader);
                        break;
                    case "lines":
                        lines = await ReadTramLinesAtStop(jsonReader);
                        hasTramLine = lines != null;
                        break;
                    default:
                        await jsonReader.SkipAsync();
                        break;
                }
            }

            if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

            if (lat == null || lon == null || hasTramLine == null) throw new FormatException("Incomplete JSON data");

            if (hasTramLine.GetValueOrDefault())
            {
                sumLat += lat.GetValueOrDefault();
                sumLon += lon.GetValueOrDefault();
                ++stopCount;

                linesInGroup.UnionWith(lines!);
            }
        }

        return stopCount == 0 ? null : (sumLat / stopCount, sumLon / stopCount, linesInGroup);
    }

    private static async Task<HashSet<LineAtStop>?> ReadTramLinesAtStop(JsonTextReader jsonReader)
    {
        var result = new HashSet<LineAtStop>();

        if (!await jsonReader.ReadAsync() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");

        while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.StartObject)
        {
            string? name = null;
            string? type = null;
            string? direction = null;
            string? direction2 = null;

            while (await jsonReader.ReadAsync() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "id":
                        await ReadIntegerValue(jsonReader);
                        break;
                    case "name":
                        name = await ReadStringValue(jsonReader);
                        break;
                    case "type":
                        type = await ReadStringValue(jsonReader);
                        break;
                    case "direction":
                        direction = await ReadStringValue(jsonReader);
                        break;
                    case "direction2":
                        direction2 = await ReadStringValue(jsonReader);
                        break;
                    case "isNight":
                        await jsonReader.SkipAsync();
                        break;
                    default:
                        throw new FormatException("Unsupported property " + jsonReader.Value);
                }
            }

            if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

            if (name == null || type == null || direction == null) throw new FormatException("Incomplete JSON data");

            if (type == "tram") result.Add(new LineAtStop(name, direction));
        }

        return result.Count == 0 ? null : result;
    }

    private record StopData(string Name, string IdosName, string FullName, string UniqueName, long Number, long Node, decimal Lat, decimal Lon, HashSet<string> GtfsIds, HashSet<LineAtStop> Lines);

    private record struct LineAtStop(string LineName, string Direction);

    private record GtfsRoute(List<String> Stops);

    private record struct PositionInGtfsRoute(GtfsRoute GtfsRoute, int Position);
}