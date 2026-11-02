using Azure.Search.Documents.Indexes.Models;

namespace Assistant.Infrastructure.AzureAdapters;

/// <summary>Embedding settings for the index vectorizer (integrated vectorization at query time).</summary>
public sealed record VectorizerSettings(Uri OpenAiEndpoint, string EmbeddingDeployment, string EmbeddingModel, int Dimensions);

/// <summary>
/// Defines the knowledge index used by <see cref="AzureAiSearchRetriever"/>. Documents are chunks of catalog, FAQ
/// and policy content. <c>aclGroups</c> carries the Entra group ids allowed to see a chunk; <c>isPublic</c> marks
/// chunks visible to everyone and eligible for the semantic cache.
/// To use vector search, pass <paramref name="vectors"/>, grant the search service identity the
/// "Cognitive Services OpenAI User" role on the embedding account, populate <c>contentVector</c> at ingestion,
/// and set <c>Assistant:Azure:SearchUseVectors</c> to true.
/// </summary>
public static class SearchIndexFactory
{
    public static SearchIndex Create(string name, VectorizerSettings? vectors = null)
    {
        var fields = new List<SearchField>
        {
            new SimpleField(IndexFields.Id, SearchFieldDataType.String) { IsKey = true },
            new SimpleField(IndexFields.SourceId, SearchFieldDataType.String) { IsFilterable = true },
            new SearchableField(IndexFields.Title),
            new SearchableField(IndexFields.Content),
            new SimpleField(IndexFields.Uri, SearchFieldDataType.String),
            new SimpleField(IndexFields.IsPublic, SearchFieldDataType.Boolean) { IsFilterable = true },
            new SearchField(IndexFields.AclGroups, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            new SimpleField("sourceVersion", SearchFieldDataType.String) { IsFilterable = true },
        };

        var index = new SearchIndex(name)
        {
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(
                        IndexFields.SemanticConfiguration,
                        new SemanticPrioritizedFields
                        {
                            TitleField = new SemanticField(IndexFields.Title),
                            ContentFields = { new SemanticField(IndexFields.Content) },
                        }),
                },
            },
        };

        if (vectors is not null)
        {
            fields.Add(new VectorSearchField(IndexFields.ContentVector, vectors.Dimensions, "vector-profile"));
            index.VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("hnsw") },
                Profiles = { new VectorSearchProfile("vector-profile", "hnsw") { VectorizerName = "aoai" } },
                Vectorizers =
                {
                    new AzureOpenAIVectorizer("aoai")
                    {
                        Parameters = new AzureOpenAIVectorizerParameters
                        {
                            ResourceUri = vectors.OpenAiEndpoint,
                            DeploymentName = vectors.EmbeddingDeployment,
                            ModelName = vectors.EmbeddingModel,
                        },
                    },
                },
            };
        }

        foreach (var field in fields)
        {
            index.Fields.Add(field);
        }

        return index;
    }
}
