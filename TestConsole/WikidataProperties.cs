using System;

namespace TestConsole;

public static class WikidataProperties
{
    public const string InstanceOf = "P31";

    public const string StartTime = "P580";
    public const string EndTime = "P582";
    public const string OfficialName = "P1448";
    public const string LexemeSense = "P7018";

    public const string HqLocation = "P159";

    public const string Country = "P17";
    public const string Street = "P669";
    public const string ZipCode = "P281";
    public const string ConscriptionNumber = "P4856";
    public const string StreetNumber = "P670";
    public const string LocatedInAdminEntity = "P131";
    public const string Coordinates = "P625";

    public const string FieldOfWork = "P101";
    
    public const string UsageState = "P5817";
    public const string NeighboringStop = "P197";
    public const string TerminusDirection = "P5051";
    
    public const string Creator = "P170";
    public const string AuthorText = "P2093";
    public const string StatedIn = "P248";
    public const string Title = "P1476";
    public const string Publisher = "P123";
    public const string ReferenceUrl = "P854";
    public const string AccessDate = "P813";
    public const string PublishDate = "P577";
    
    public const string UdcOfConcept = "P1190";
    public const string UdcOfTopic = "P8361";

    public const string ReferenceRate = "P2661";
    
    public const string Url = "P2699";
    public const string Mastodon = "P4033";
    public const string FlickrId = "P3267";
    public const string WikimediaUsername = "P4174";

    public const string FotbalIdnesId = "P3663";
    public const string CzechTheatreEncyclopedia = "P9391";
    public const string Oid = "P3743";

    public static readonly Uri GlobeEarth = new("http://www.wikidata.org/entity/Q2");

    public static readonly Uri UnitPercent = new("http://www.wikidata.org/entity/Q11229");
    public static readonly Uri UnitCentimetre = new("http://www.wikidata.org/entity/Q174728");
}