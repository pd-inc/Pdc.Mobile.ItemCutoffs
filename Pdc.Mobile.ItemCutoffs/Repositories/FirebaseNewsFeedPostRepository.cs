using Microsoft.Extensions.Logging;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pdc.Mobile.ItemCutoffs.Repositories;

///////////////////////////////////////////////
/// FIREBASE NEWS FEED POST REPOSITORY
///////////////////////////////////////////////

/// <summary>
/// Writes the announcement post directly to the Realtime Database with the service
/// account credential, the same way the Contests and Notifications Lambdas write
/// their projections.
///
/// This makes the function the SECOND writer of the post node. The serialization
/// options below are not incidental: camelCase naming and
/// WhenWritingNull both mirror Pdc.Mobile's FirebaseNewsFeedRepository exactly, so a
/// post from a sales deadline is byte-identical in shape to one an admin typed. The
/// app cannot tell them apart, and must not be able to.
/// </summary>
public class FirebaseNewsFeedPostRepository : IFirebaseNewsFeedPostRepository
{
    private const string FeatureKeyWriteFailed = "##_ITEMCUTOFF_FIREBASE_WRITEFAILED";
    private const string JsonContentType = "application/json";

    /// <summary>
    /// Mirrors Pdc.Mobile FirebaseNewsFeedRepository.JsonOptions. Changing either
    /// without the other is what a drifted post looks like.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger logger;
    private readonly HttpClient httpClient;
    private readonly IFirebaseAccessTokenProvider accessTokenProvider;
    private readonly string databaseUrl;

    public FirebaseNewsFeedPostRepository(
        ILogger logger,
        HttpClient httpClient,
        IFirebaseAccessTokenProvider accessTokenProvider,
        string databaseUrl)
    {
        this.logger = logger;
        this.httpClient = httpClient;
        this.accessTokenProvider = accessTokenProvider;
        this.databaseUrl = databaseUrl;
    }

    /// <summary>
    /// The post's path. Identical to Pdc.Mobile's NewsFeedFirebasePathBuilder.
    /// </summary>
    internal static string BuildPostPath(string eventUuid, string postId)
        => $"events/{eventUuid}/news_feed/posts/{postId}.json";

    /// <summary>
    /// PUTs the post. A PUT rather than a PATCH because the id is a fresh guid, so
    /// there is nothing to merge with and a partial write would be worse than none.
    /// </summary>
    public async Task WritePost(string eventUuid, NewsFeedPostDocument post)
    {
        var accessToken = await accessTokenProvider.GetAccessTokenAsync();
        var requestUri = $"{databaseUrl.TrimEnd('/')}/{BuildPostPath(eventUuid, post.Id)}";

        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(post, JsonOptions),
            Encoding.UTF8,
            JsonContentType);

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();

            logger.LogError("{FeatureKey} Failed to write announcement post {PostId} for EventUuid {EventUuid}. Status {StatusCode}: {Body}",
                FeatureKeyWriteFailed, post.Id, eventUuid, (int)response.StatusCode, body);

            // Thrown so the caller records a retryable outcome rather than marking
            // the announcement sent when nothing was written.
            throw new HttpRequestException(
                $"Firebase rejected the announcement post write with status {(int)response.StatusCode}.");
        }
    }
}
