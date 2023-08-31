using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TestConsole.Tasks;

namespace TestConsole
{
    internal static class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                await Run(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }
        }

        private static async Task Run(string[] args)
        {
            // await MapCsfdToImdb.Run(@"c:\Temp\csfd\csfd-list.csv", @"c:\Temp\csfd\csfd-to-imdb.csv");

            var wikidataSite = await WikidataTools.Init();

            var credentials = JsonConvert.DeserializeAnonymousType(await File.ReadAllTextAsync("credentials.json"), new { username = "", password = "" }) ?? throw new FormatException("Missing configuration");

            // await Console.Error.WriteLineAsync("Logging in...");
            // await wikidataSite.LoginAsync(credentials.username, Encoding.UTF8.GetString(Convert.FromBase64String(credentials.password)));

            // await Experiments.Run(wikidataSite);
            // await ImportDsPerIco.Run(wikidataSite);
            // await FixReferenceAccessDate.Run(wikidataSite);
            // await DrobnePamatkyDeprecated.Run(wikidataSite);
            // await CzechStationsPolishAccuracy.Run(wikidataSite);
            // await ListSparqlQuery.Run(wikidataSite);
            // await ExportPropertyHistory.Run(wikidataSite);
            // await FixMonumentCatalogueUrl.Run(wikidataSite);
            // await UpdateDisambigDescription.Run(wikidataSite);
            // await CovIdFixImport.RunDeprecationImport(wikidataSite);
            // await ImportOpenCorporatesIdPerIco.Run(wikidataSite);
            // await IihfWcNormalization.Run(wikidataSite);
            // await UpdateZipFromRuian.Run(wikidataSite);
            // await FixHqFromAres.Run(wikidataSite);
            // await RemapFotbalIdnesReferences.Run(wikidataSite);
            // await MastodonAddFromDate.Run(wikidataSite);
            // await LinkMunicipalityNameToLexeme.Run(wikidataSite);
            // await SwitchTopicClassification.Run(wikidataSite);
            await WikidataTreeBuilder.Run();
        }
    }
}