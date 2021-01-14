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
                Console.WriteLine(e);
            }
        }

        private static async Task Run(string[] args)
        {
            var wikidataSite = await WikidataTools.Init();

            var credentials = JsonConvert.DeserializeAnonymousType(await File.ReadAllTextAsync("credentials.json"), new {username = "", password = ""});

            await wikidataSite.LoginAsync(credentials.username, Encoding.UTF8.GetString(Convert.FromBase64String(credentials.password)));

            await ImportDsPerIco.Run(wikidataSite);

            // await FixReferenceAccessDate.Run(wikidataSite);
            // await DrobnePamatkyDeprecated.Run(wikidataSite);
            // await CzechStationsPolishAccuracy.Run(wikidataSite);
            // await ListSparqlQuery.Run(wikidataSite);
        }
    }
}