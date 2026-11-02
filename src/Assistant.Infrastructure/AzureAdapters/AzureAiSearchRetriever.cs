using System.Globalization;
using Assistant.Application.Abstractions;
using Assistant.Domain.Answers;
using Assistant.Domain.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>Field names of the knowledge index. See <see cref="SearchIndexFactory"/> for the full schema.</summary>
internal static class IndexFields
{
    public const string Id = "id";
    public const string SourceId = "sourceId";
    public const string Title = "title";
    public const string Content = "content";
    public const string Uri = "uri";
    public const string IsPublic = "isPublic";
    public const string AclGroups = "aclGroups";
    public const string ContentVector = "contentVector";
    public const string SemanticConfiguration = "default";
}

/// <summary>Builds the OData security-trimming filter from the caller's validated identity (FR-B1).</summary>
public static class AclFilter
{
    /// <summary>
    /// A document is visible if it is public or shares a group with the caller. The filter is applied by the
    /// search service before ranking, and group ids come from the validated token, never from model output.
    /// </summary>
    public static string Build(UserContext user)
    {
        // search.in uses ',' as the delimiter, so ids containing it (or control characters) are dropped, not escaped.
        var groups = user.Groups
            .Where(g => g.Length > 0 && g.All(c => c != ',' && !char.IsControl(c)))
            .Select(g => g.Replace("'", "''", StringComparison.Ordinal))
            .ToList();

        return groups.Count == 0
            ? $"{IndexFields.IsPublic} eq true"
            : $"{IndexFields.IsPublic} eq true or {IndexFields.AclGroups}/any(g: search.in(g, '{string.Join(",", groups)}', ','))";
    }
}

/// <summary>Azure AI Search hybrid query: keyword + semantic ranker, with an optional vector query.</summary>
public sealed class AzureAiSearchRetriever(SearchClient client, bool useVectors) : IRetriever
{
    private const int MaxPassageChars = 4000;

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string query, UserContext user, int top, CancellationToken ct)
    {
        var options = new SearchOptions
        {
            Size = top,
            Filter = AclFilter.Build(user),
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = IndexFields.SemanticConfiguration },
        };
        foreach (var field in new[] { IndexFields.Id, IndexFields.SourceId, IndexFields.Title, IndexFields.Content, IndexFields.Uri, IndexFields.IsPublic })
        {
            options.Select.Add(field);
        }

        if (useVectors)
        {
            options.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizableTextQuery(query) { KNearestNeighborsCount = 50, Fields = { IndexFields.ContentVector } },
                },
            };
        }

        var response = await client.SearchAsync<SearchDocument>(query, options, ct).ConfigureAwait(false);

        var passages = new List<RetrievedPassage>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            var doc = result.Document;
            var content = Convert.ToString(doc[IndexFields.Content], CultureInfo.InvariantCulture) ?? string.Empty;
            passages.Add(new RetrievedPassage(
                SourceId: Convert.ToString(doc[IndexFields.SourceId], CultureInfo.InvariantCulture) ?? Convert.ToString(doc[IndexFields.Id], CultureInfo.InvariantCulture)!,
                Title: Convert.ToString(doc[IndexFields.Title], CultureInfo.InvariantCulture) ?? string.Empty,
                Text: content.Length > MaxPassageChars ? content[..MaxPassageChars] : content,
                IsPublic: doc.TryGetValue(IndexFields.IsPublic, out var isPublic) && isPublic is true,
                Uri: doc.TryGetValue(IndexFields.Uri, out var uri) ? uri as string : null));
        }

        return passages;
    }
}
