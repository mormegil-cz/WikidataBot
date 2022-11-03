using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WikiClientLibrary.Sites;

namespace TestConsole;

public class WikidataCache<TKey, TData>
{
    private readonly Func<TKey, Task<TData>> retrieveFunc;
    private readonly Dictionary<TKey, TData> cache = new();

    public WikidataCache(Func<TKey, Task<TData>> retrieveFunc)
    {
        this.retrieveFunc = retrieveFunc;
    }

    public async Task<TData> Get(TKey key)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;

        var retrieved = await retrieveFunc(key);
        cache.Add(key, retrieved);
        return retrieved;
    }
}

public static class WikidataCache
{
    public static WikidataCache<string, string> CreateLabelCache(WikiSite wikidataSite, string language) =>
        new(async qid => await WikidataTools.GetLabel(wikidataSite, qid, language) ?? qid);

    public static WikidataCache<string, IList<IList<string?>>> CreateSparqlCache(string query, IDictionary<string, string> fields) =>
        new(param => ExecuteParameterizedSparql(query, param, fields, results => (IList<IList<string?>>)results.ToList()));

    public static WikidataCache<string, IList<string?>> CreateSparqlCache(string query, string fieldName, string fieldType)
    {
        var fields = new Dictionary<string, string> { { fieldName, fieldType } };
        return new(
            param => ExecuteParameterizedSparql(
                query, param, fields,
                results => (IList<string?>)results.Select(columns => columns[0]).ToList()
            )
        );
    }

    private static async Task<TResult> ExecuteParameterizedSparql<TResult>(string query, string param, IDictionary<string, string> fields, Func<IEnumerable<IList<string?>>, TResult> resultSelector) =>
        resultSelector(
            WikidataTools.GetEntities(
                // TODO: SPARQL parameter escaping
                await WikidataTools.GetSparqlResults(query.Replace("$PARAM$", param)),
                fields
            )
        );
}