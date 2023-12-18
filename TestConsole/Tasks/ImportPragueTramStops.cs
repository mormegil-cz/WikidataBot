using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WikiClientLibrary;
using WikiClientLibrary.Sites;
using WikiClientLibrary.Wikibase;
using WikiClientLibrary.Wikibase.DataTypes;

namespace TestConsole.Tasks;

using static WikidataTools;

public class ImportPragueTramStops
{
    private static readonly string EditGroupId = GenerateRandomEditGroupId();
    private static readonly string EditSummary = MakeEditSummary("Prague tram stops import", EditGroupId);
    private static readonly string[] Languages = { "cs", "en" };

    private const string gtfsUrl = "http://data.pid.cz/PID_GTFS.zip";
    private static readonly DateOnly gtfsDownloadDate = new(2023, 12, 15);
    private static readonly string gtfsFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PID_GTFS.zip");
    private static ClaimReference? referenceGtfs;

    private const string stopsUrl = "https://data.pid.cz/stops/json/stops.json";
    private static readonly DateOnly jsonDownloadDate = new(2023, 12, 15);
    private static readonly string jsonFilename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "stops.json");
    private static ClaimReference? referenceStops;

    public static async Task Run(WikiSite wikidataSite)
    {
        Console.WriteLine("Loading stop data from JSON...");
        // name -> stop data
        var (jsonFileDate, stopsData) = LoadStops(jsonFilename);
        Console.WriteLine($"Done, {stopsData.Count} stops loaded");
        referenceStops = new ClaimReference(
            new Snak(WikidataProperties.StatedIn, "Q97095774", BuiltInDataTypes.WikibaseItem),
            new Snak(WikidataProperties.PublishDate, new WbTime(jsonFileDate.Year, jsonFileDate.Month, jsonFileDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time),
            new Snak(WikidataProperties.ReferenceUrl, stopsUrl, BuiltInDataTypes.Url),
            new Snak(WikidataProperties.AccessDate, new WbTime(jsonDownloadDate.Year, jsonDownloadDate.Month, jsonDownloadDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time)
        );

        // GTFS ID -> name
        var stopPerGtfsId = new Dictionary<string, string>(stopsData.Values.SelectMany(stop => stop.GtfsIds.Select(id => new KeyValuePair<string, string>(id, stop.Name))));

        Console.WriteLine("Loading route data from GTFS...");
        // (route ID, direction) -> route data
        var (gtfsFileDate, routeData) = await LoadGtfsRoutes(gtfsFilename, stopPerGtfsId);
        Console.WriteLine($"Done, {routeData.Count} routes loaded");
        referenceGtfs = new ClaimReference(
            new Snak(WikidataProperties.StatedIn, "Q97095774", BuiltInDataTypes.WikibaseItem),
            new Snak(WikidataProperties.PublishDate, new WbTime(gtfsFileDate.Year, gtfsFileDate.Month, gtfsFileDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time),
            new Snak(WikidataProperties.ReferenceUrl, gtfsUrl, BuiltInDataTypes.Url),
            new Snak(WikidataProperties.AccessDate, new WbTime(gtfsDownloadDate.Year, gtfsDownloadDate.Month, gtfsDownloadDate.Day, 0, 0, 0, 0, 0, 0, WikibaseTimePrecision.Day, WbTime.GregorianCalendar), BuiltInDataTypes.Time)
        );

        // name -> set of neighbor names
        var neighbors = ComputeNeighbors(routeData, stopPerGtfsId, stopsData);

        Console.WriteLine("Loading stop data from Wikidata...");
        // name -> QID
        var stopsInWikidata = await FindStopsAtWikidata();
        Console.WriteLine($"Done, {stopsInWikidata.Count} stops loaded");

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
        // P131 Q1085 (or more precise?)
        // P625 coords
        // P5817 Q55654238 (?)

        // P197: next stop
        //  +P5051: direction

        Console.Write("Importing");
        foreach (var (stopName, stopData) in stopsData)
        {
            var qid = stopsInWikidata[stopName];
            var entity = new Entity(wikidataSite, qid);
            await entity.RefreshAsync(EntityQueryOptions.FetchLabels | EntityQueryOptions.FetchDescriptions | EntityQueryOptions.FetchAliases | EntityQueryOptions.FetchClaims, Languages);

            if (entity.Claims == null)
            {
                Console.Error.WriteLine($"\nWARNING! Unable to read entity {qid}!");
                continue;
            }

            var aliases = new HashSet<string>(5) { stopData.Name, stopData.FullName, stopData.IdosName, stopData.IdosName, stopData.UniqueName };
            aliases.Remove(stopName);

            var edits = new List<EntityEditEntry>();

            CheckLabel(entity, stopName, "cs", edits);
            CheckLabel(entity, stopName, "en", edits);
            CheckDescription(entity, "tramvajová zastávka v Praze", "cs", edits);
            CheckDescription(entity, "tram stop in Prague", "en", edits);
            CheckAliases(entity, aliases, "cs", edits);
            CheckAliases(entity, aliases, "en", edits);

            CheckClaim(entity, WikidataProperties.Country, "Q213", edits);
            CheckClaim(entity, WikidataProperties.LocatedInAdminEntity, "Q1085", edits);
            CheckClaim(entity, WikidataProperties.UsageState, "Q55654238", edits);
            CheckCoordClaim(entity, WikidataProperties.Coordinates, (double) stopData.Lat, (double) stopData.Lon, edits);
            CheckNeighborClaims(entity, neighbors[stopName].Select(x => (stopsInWikidata[x.Item1], stopsInWikidata[x.Item2])).ToHashSet(), edits);

            if (edits.Count > 0)
            {
                Console.Write("*");
                await entity.EditAsync(edits, EditSummary, EntityEditOptions.Bot);
            }
            else
            {
                Console.Write(".");
            }
        }
    }

    private static void CheckLabel(Entity entity, string stopName, string language, List<EntityEditEntry> edits)
    {
        if (entity.Labels.ContainsLanguage(language))
        {
            var currentLabel = entity.Labels[language];
            if (currentLabel != stopName) Console.Error.WriteLine($"\nWarning: {entity.Id} has {language} label '{currentLabel}' instead of '{stopName}'");
        }
        else
        {
            edits.Add(new EntityEditEntry(nameof(entity.Labels), new WbMonolingualText(language, stopName)));
        }
    }

    private static void CheckDescription(Entity entity, string description, string language, List<EntityEditEntry> edits)
    {
        if (entity.Descriptions.ContainsLanguage(language))
        {
            var currentDesc = entity.Descriptions[language];
            if (currentDesc.Replace(' ', ' ') != description.Replace(' ', ' ')) Console.Error.WriteLine($"\nNote: {entity.Id} has {language} description '{currentDesc}' instead of '{description}'");
        }
        else
        {
            edits.Add(new EntityEditEntry(nameof(entity.Descriptions), new WbMonolingualText(language, description)));
        }
    }

    private static void CheckAliases(Entity entity, ISet<string> aliases, string language, List<EntityEditEntry> edits)
    {
        var currentAliases = new HashSet<string>(entity.Aliases[language]);

        var aliasesToAdd = new HashSet<string>(aliases);
        aliasesToAdd.ExceptWith(currentAliases);

        var extraAliases = new HashSet<string>(currentAliases);
        extraAliases.ExceptWith(aliases);
        if (extraAliases.Count > 0)
        {
            Console.Error.WriteLine($"\nNote: {entity.Id} has {extraAliases.Count} extra alias(es): {String.Join(", ", extraAliases)}");
        }

        if (aliasesToAdd.Count > 0)
        {
            edits.AddRange( aliasesToAdd.Select(a => new EntityEditEntry(nameof(entity.Aliases), new WbMonolingualText(language, a))));
        }
    }

    private static void CheckClaim(Entity entity, string property, string value, List<EntityEditEntry> edits)
    {
        if (entity.Claims.ContainsKey(property))
        {
            var claimValues = entity.Claims[property];
            if (claimValues.Any(c => value.Equals(c.MainSnak.DataValue)))
            {
                if (claimValues.Count > 1) Console.Error.WriteLine($"\nWarning: {entity.Id} has {claimValues.Count - 1} additional {property} values");
            }
            else
            {
                if (claimValues.Count == 0) throw new UnexpectedDataException($"Empty {property} claims in {entity.Id}?!");
                Console.Error.WriteLine($"\nWarning: {entity.Id} has {claimValues.Count} {property} value(s) but not the expected one");
            }
        }
        else
        {
            var addedClaim = new Claim(new Snak(property, value, BuiltInDataTypes.WikibaseItem));
            addedClaim.References.Add(referenceStops);
            edits.Add(new EntityEditEntry(nameof(entity.Claims), addedClaim));
        }
    }

    private static void CheckCoordClaim(Entity entity, string property, double lat, double lon, List<EntityEditEntry> edits)
    {
        if (entity.Claims.ContainsKey(property))
        {
            var claimValues = entity.Claims[property];
            if (claimValues.Count != 1)
            {
                Console.Error.WriteLine($"\nWarning: {entity.Id} has {claimValues.Count} {property} values");
            }
            else
            {
                var claim = claimValues.Single();
                var claimValue = (WbGlobeCoordinate) claim.MainSnak.DataValue;

                // heh! :-)
                var dist = Math.Abs(lat - claimValue.Latitude) + Math.Abs(lon - claimValue.Longitude);
                if (dist > 0.0007)
                {
                    Console.Error.WriteLine($"\nWarning: {entity.Id} has suspicious coordinates ({lat}, {lon} expected; distance {dist * 40000 / 360.0f:F5})");

                    edits.Add(new EntityEditEntry(nameof(entity.Claims), claim, EntityEditEntryState.Removed));
                    var addedCoordClaim = new Claim(new Snak(WikidataProperties.Coordinates, new WbGlobeCoordinate(lat, lon, 0.0001, WikidataProperties.GlobeEarth), BuiltInDataTypes.GlobeCoordinate));
                    addedCoordClaim.References.Add(referenceStops);
                    edits.Add(new EntityEditEntry(nameof(entity.Claims), addedCoordClaim));
                }
            }
        }
        else
        {
            var addedCoordClaim = new Claim(new Snak(WikidataProperties.Coordinates, new WbGlobeCoordinate(lat, lon, 0.0001, WikidataProperties.GlobeEarth), BuiltInDataTypes.GlobeCoordinate));
            addedCoordClaim.References.Add(referenceStops);
            edits.Add(new EntityEditEntry(nameof(entity.Claims), addedCoordClaim));
        }
    }

    private static void CheckNeighborClaims(Entity entity, HashSet<(string, string)> neighborQids, List<EntityEditEntry> edits)
    {
        var missingNeighbors = new HashSet<(string, string)>(neighborQids);
        if (entity.Claims.ContainsKey(WikidataProperties.NeighboringStop))
        {
            var neighborSet = neighborQids.Select(n => n.Item1).ToHashSet();

            var claims = entity.Claims[WikidataProperties.NeighboringStop];
            foreach (var claim in claims)
            {
                var neighborQid = (string) claim.MainSnak.DataValue;
                var directionQualifiers = claim.Qualifiers.Where(q => q.PropertyId == WikidataProperties.TerminusDirection).ToList();
                bool ok;
                if (directionQualifiers.Count > 1)
                {
                    Console.Error.WriteLine($"\nError: {entity.Id} has {directionQualifiers.Count} qualifiers for neighboring {neighborQid}");
                    return;
                }
                if (directionQualifiers.Count == 0)
                {
                    Console.Error.WriteLine($"\nNote: {entity.Id} has no qualifiers for neighboring {neighborQid}");
                    if (neighborSet.Contains(neighborQid))
                    {
                        ok = false;
                    }
                    else
                    {
                        Console.Error.WriteLine($"\nWarning: That is an unknown neighbor {neighborQid} on {entity.Id}, keeping untouched");
                        ok = true;
                    }
                }
                else
                {
                    var directionQid = (string) directionQualifiers.Single().DataValue;
                    var key = (neighborQid, directionQid);
                    if (neighborQids.Contains(key))
                    {
                        ok = true;
                        missingNeighbors.Remove(key);
                    }
                    else
                    {
                        if (neighborSet.Contains(neighborQid))
                        {
                            Console.Error.WriteLine($"\nNote: {entity.Id} has a different destination {directionQid} for neighboring {neighborQid}");
                            ok = false;
                        }
                        else
                        {
                            Console.Error.WriteLine($"\nWarning: That is an unknown neighbor {neighborQid} on {entity.Id}, keeping untouched");
                            ok = true;
                        }
                    }
                }
                if (!ok)
                {
                    if (claim.References.Count > 0)
                    {
                        Console.Error.WriteLine($"\nError: {entity.Id} has {claim.References.Count} references, not processing further");
                        return;
                    }
                    edits.Add(new EntityEditEntry(nameof(entity.Claims), claim, EntityEditEntryState.Removed));
                }
            }
        }

        foreach (var missingNeighbor in missingNeighbors)
        {
            var addedClaim = new Claim(new Snak(WikidataProperties.NeighboringStop, missingNeighbor.Item1, BuiltInDataTypes.WikibaseItem));
            addedClaim.Qualifiers.Add(new Snak(WikidataProperties.TerminusDirection, missingNeighbor.Item2, BuiltInDataTypes.WikibaseItem));
            addedClaim.References.Add(referenceGtfs);
            edits.Add(new EntityEditEntry(nameof(entity.Claims), addedClaim));
        }
    }

    private static Dictionary<string, HashSet<(string, string)>> ComputeNeighbors(Dictionary<(string, string), GtfsRoute> routes, Dictionary<string, string> stopPerGtfsId, Dictionary<string, StopData> stopData)
    {
        // first, for each route, record the set of neighboring GTFS stops
        var gtfsNeighbors = new Dictionary<string, HashSet<string>>(stopData.Count);
        foreach (var route in routes.Values)
        {
            var routeStops = route.Stops;
            var routeLength = routeStops.Count;
            var currentStopGtfs = routeStops[0];
            for (var routePos = 1; routePos < routeLength; ++routePos)
            {
                var nextStopGtfs = routeStops[routePos];

                if (!gtfsNeighbors.TryGetValue(currentStopGtfs, out var currentStopData)) gtfsNeighbors.Add(currentStopGtfs, currentStopData = new HashSet<string>(5));
                currentStopData.Add(nextStopGtfs);

                currentStopGtfs = nextStopGtfs;
            }
        }

        // from that, compute also the aggregated neighbors for each stop name
        var nameNeighbors = new Dictionary<string, HashSet<string>>();
        foreach (var (stopGtfsName, stopGtfsNeighbors) in gtfsNeighbors)
        {
            var stopName = stopPerGtfsId[stopGtfsName];
            if (!nameNeighbors.TryGetValue(stopName, out var stopNameNeighbors)) nameNeighbors.Add(stopName, stopNameNeighbors = new HashSet<string>(5));
            stopNameNeighbors.UnionWith(stopGtfsNeighbors.Select(n => stopPerGtfsId[n]));
        }

        // now from those results, compute for each GTFS stop and its neighbor the furthest common destination
        var result = new Dictionary<string, HashSet<(string, string)>>(gtfsNeighbors.Count);
        foreach (var (stopGtfsName, stopNeighbors) in gtfsNeighbors)
        {
            var stopName = stopPerGtfsId[stopGtfsName];
            if (!result.TryGetValue(stopName, out var resultSet)) result.Add(stopName, resultSet = new HashSet<(string, string)>(stopNeighbors.Count));

            foreach (var neighborGtfs in stopNeighbors)
            {
                var neighborName = stopPerGtfsId[neighborGtfs];
                if (neighborName == stopName)
                {
                    Console.Error.WriteLine("Note: Multi-stop in one route at " + neighborName);
                    continue;
                }

                var currName = neighborName;
                var comingFromName = stopName;
                var comingFromNeighbors = nameNeighbors[stopName];
                while (true)
                {
                    var currNeighbors = nameNeighbors[currName];
                    var possibleNext = currNeighbors.Where(n => n != comingFromName && !comingFromNeighbors.Contains(n)).ToHashSet();
                    if (possibleNext.Count != 1) break;
                    var next = possibleNext.Single();
                    comingFromName = currName;
                    comingFromNeighbors = currNeighbors;
                    currName = next;
                }
                resultSet.Add((neighborName, currName));
            }
        }
        return result;
    }

    private static async Task<(DateOnly gtfsFileDate, Dictionary<(string, string), GtfsRoute> routeData)> LoadGtfsRoutes(string zipFilename, Dictionary<string, string> knownGtfsIds)
    {
        await using var stream = new FileStream(zipFilename, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var tramRoutes = await LoadTramGtfsRoutes(zipArchive);

        var zipArchiveEntry = zipArchive.GetEntry("route_stops.txt") ?? throw new FormatException("Invalid GTFS data");
        await using var entryStream = zipArchiveEntry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);

        if (await reader.ReadLineAsync() != "route_id,direction_id,stop_id,stop_sequence") throw new FormatException("Unexpected GTFS format");

        var result = new Dictionary<(string, string), GtfsRoute>();
        string? line;
        List<string> currentRouteStops = new List<string>();
        string currentRoute = "";
        string currentDirection = "";
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var parts = line.Split(',');
            var route = parts[0];
            if (!tramRoutes.Contains(route))
            {
                // ignore non-tram routes
                continue;
            }
            var direction = parts[1];
            var stop = parts[2];
            var sequence = Int32.Parse(parts[3], CultureInfo.InvariantCulture);
            if (route != currentRoute || direction != currentDirection)
            {
                currentRouteStops = new List<string>();
                result.Add((route, direction), new GtfsRoute(currentRouteStops));
                currentRoute = route;
                currentDirection = direction;
            }
            if (!knownGtfsIds.ContainsKey(stop))
            {
                // contains non-relevant stops, remove route
                result.Remove((currentRoute, currentDirection));
            }
            currentRouteStops.Add(stop);
            if (sequence != currentRouteStops.Count) throw new FormatException($"Unexpected sequence at {route}/{direction}/{sequence}");
        }
        return (ToDateOnly(zipArchiveEntry.LastWriteTime), result);
    }

    private static async Task<HashSet<string>> LoadTramGtfsRoutes(ZipArchive zipArchive)
    {
        var zipArchiveEntry = zipArchive.GetEntry("routes.txt") ?? throw new FormatException("Invalid GTFS data");
        await using var entryStream = zipArchiveEntry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);

        var result = new HashSet<string>();
        if (await reader.ReadLineAsync() != "route_id,agency_id,route_short_name,route_long_name,route_type,route_url,route_color,route_text_color,is_night,is_regional,is_substitute_transport") throw new FormatException("Unexpected GTFS format");
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var parts = line.Split(',');
            var route = parts[0];
            var type = parts[4];
            // 0=tram, 1=metro, 2=train, 3=bus
            if (type == "0") result.Add(route);
        }
        return result;
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

    private static (DateOnly, Dictionary<string, StopData>) LoadStops(string filename)
    {
        using var reader = File.OpenText(filename);
        using var jsonReader = new JsonTextReader(reader);

        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartObject) throw new FormatException("Invalid JSON data");

        DateOnly? generatedAt = null;
        Dictionary<string, StopData>? stops = null;

        while (jsonReader.Read() && jsonReader.TokenType == JsonToken.PropertyName)
        {
            switch (jsonReader.Value)
            {
                case "generatedAt":
                    generatedAt = ReadDateValue(jsonReader);
                    break;

                case "dataFormatVersion":
                    var version = ReadStringValue(jsonReader);
                    if (version != "3") throw new FormatException("Unsupported version " + version);
                    break;

                case "stopGroups":
                    stops = ReadTramStopGroups(jsonReader);
                    break;

                default:
                    throw new FormatException("Unsupported field " + jsonReader.Value);
            }
        }

        if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

        if (generatedAt == null || stops == null) throw new FormatException("Missing data in JSON");

        return (generatedAt.GetValueOrDefault(), stops);
    }

    private static long ReadIntegerValue(JsonTextReader jsonReader)
    {
        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.Integer) throw new FormatException("Invalid JSON data");
        return (long) jsonReader.Value!;
    }

    private static decimal ReadDecimalValue(JsonTextReader jsonReader)
    {
        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.Float) throw new FormatException("Invalid JSON data");
        return (decimal) (double) jsonReader.Value!;
    }

    private static string ReadStringValue(JsonTextReader jsonReader)
    {
        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.String) throw new FormatException("Invalid JSON data");
        return (string) jsonReader.Value!;
    }

    private static DateOnly ReadDateValue(JsonTextReader jsonReader)
    {
        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.Date) throw new FormatException("Invalid JSON data");
        var dateTime = (DateTime) jsonReader.Value!;
        return new DateOnly(dateTime.Year, dateTime.Month, dateTime.Day);
    }

    private static HashSet<string> ReadStringSet(JsonTextReader jsonReader)
    {
        var result = new HashSet<string>();

        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");
        while (jsonReader.Read() && jsonReader.TokenType == JsonToken.String)
        {
            if (!result.Add((string) jsonReader.Value!)) throw new ArgumentException("Duplicate item in set");
        }
        if (jsonReader.TokenType != JsonToken.EndArray) throw new FormatException("Invalid JSON data");

        return result;
    }

    private static Dictionary<string, StopData> ReadTramStopGroups(JsonTextReader jsonReader)
    {
        var result = new Dictionary<string, StopData>();

        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");
        while (jsonReader.Read() && jsonReader.TokenType == JsonToken.StartObject)
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

            while (jsonReader.Read() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "name":
                        name = ReadStringValue(jsonReader);
                        break;
                    case "districtCode":
                        districtCode = ReadStringValue(jsonReader);
                        break;
                    case "idosName":
                        idosName = ReadStringValue(jsonReader);
                        break;
                    case "fullName":
                        fullName = ReadStringValue(jsonReader);
                        break;
                    case "uniqueName":
                        uniqueName = ReadStringValue(jsonReader);
                        break;
                    case "node":
                        node = ReadIntegerValue(jsonReader);
                        break;
                    case "cis":
                        number = ReadIntegerValue(jsonReader);
                        break;
                    case "municipality":
                        municipality = ReadStringValue(jsonReader);
                        break;
                    case "stops":
                        var stopsInGroup = ReadTramStopsInGroup(jsonReader);
                        noTramStops = stopsInGroup == null;
                        if (stopsInGroup != null)
                        {
                            (lat, lon, lines, var stopGtfsIds) = stopsInGroup.GetValueOrDefault();
                            gtfsIds ??= new HashSet<string>();
                            gtfsIds.UnionWith(stopGtfsIds);
                        }
                        break;
                    default:
                        jsonReader.Skip();
                        break;
                }
            }

            if (jsonReader.TokenType != JsonToken.EndObject) throw new FormatException("Invalid JSON data");

            if (name == null || districtCode == null || idosName == null || fullName == null || uniqueName == null || node == null || number == null || municipality == null || noTramStops == null) throw new FormatException("Incomplete JSON data");

            if (noTramStops.GetValueOrDefault()) continue;

            if (gtfsIds == null) throw new FormatException("Incomplete JSON data");

            if (municipality != "Praha") throw new FormatException("Unexpected tram municipality " + municipality);
            if (districtCode != "AB") throw new FormatException("Unexpected tram district code " + districtCode);

            if (name != uniqueName) Console.Error.WriteLine($"Note: Tram station name mismatch: {name} vs {uniqueName}");

            result.Add(name, new StopData(name, idosName, fullName, uniqueName, number.GetValueOrDefault(), node.GetValueOrDefault(), lat.GetValueOrDefault(), lon.GetValueOrDefault(), gtfsIds, lines!));
        }
        if (jsonReader.TokenType != JsonToken.EndArray) throw new FormatException("Invalid JSON data");

        return result;
    }

    private static (decimal, decimal, HashSet<LineAtStop>, HashSet<string>)? ReadTramStopsInGroup(JsonTextReader jsonReader)
    {
        var stopCount = 0;
        var sumLat = 0.0m;
        var sumLon = 0.0m;
        var linesInGroup = new HashSet<LineAtStop>();
        var gtfsIds = new HashSet<string>();

        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");

        while (jsonReader.Read() && jsonReader.TokenType == JsonToken.StartObject)
        {
            decimal? lat = null;
            decimal? lon = null;
            HashSet<LineAtStop>? lines = null;
            bool? hasTramLine = null;

            while (jsonReader.Read() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "lat":
                        lat = ReadDecimalValue(jsonReader);
                        break;
                    case "lon":
                        lon = ReadDecimalValue(jsonReader);
                        break;
                    case "lines":
                        lines = ReadTramLinesAtStop(jsonReader);
                        hasTramLine = lines != null;
                        break;
                    case "gtfsIds":
                        var stopGtfsIds = ReadStringSet(jsonReader);
                        gtfsIds.UnionWith(stopGtfsIds);
                        break;
                    default:
                        jsonReader.Skip();
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

        return stopCount == 0 ? null : (sumLat / stopCount, sumLon / stopCount, linesInGroup, gtfsIds);
    }

    private static HashSet<LineAtStop>? ReadTramLinesAtStop(JsonTextReader jsonReader)
    {
        var result = new HashSet<LineAtStop>();

        if (!jsonReader.Read() || jsonReader.TokenType != JsonToken.StartArray) throw new FormatException("Invalid JSON data");

        while (jsonReader.Read() && jsonReader.TokenType == JsonToken.StartObject)
        {
            string? name = null;
            string? type = null;
            string? direction = null;
            string? direction2 = null;

            while (jsonReader.Read() && jsonReader.TokenType == JsonToken.PropertyName)
            {
                switch (jsonReader.Value)
                {
                    case "id":
                        ReadIntegerValue(jsonReader);
                        break;
                    case "name":
                        name = ReadStringValue(jsonReader);
                        break;
                    case "type":
                        type = ReadStringValue(jsonReader);
                        break;
                    case "direction":
                        direction = ReadStringValue(jsonReader);
                        break;
                    case "direction2":
                        direction2 = ReadStringValue(jsonReader);
                        break;
                    case "isNight":
                        jsonReader.Skip();
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

    private static DateOnly ToDateOnly(DateTimeOffset dateTimeOffset)
    {
        var utc = dateTimeOffset.UtcDateTime;
        return new DateOnly(utc.Year, utc.Month, utc.Day);
    }

    private record StopData(string Name, string IdosName, string FullName, string UniqueName, long Number, long Node, decimal Lat, decimal Lon, HashSet<string> GtfsIds, HashSet<LineAtStop> Lines);

    private record struct LineAtStop(string LineName, string Direction);

    private record GtfsRoute(List<String> Stops);

    private record struct PositionInGtfsRoute(GtfsRoute GtfsRoute, int Position);
}