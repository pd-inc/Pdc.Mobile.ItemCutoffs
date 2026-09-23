namespace Pdc.Mobile.ItemCutoffs.Models;

///////////////////////////////////////////////
/// NEWS FEED POST DOCUMENT
///////////////////////////////////////////////

/// <summary>
/// The RTDB document for a news feed post.
///
/// THIS IS A COPY OF Pdc.Mobile's NewsFeedPostDocument AND MUST MATCH IT FIELD FOR
/// FIELD. This Lambda is the SECOND writer of events/{uuid}/news_feed/posts/{id};
/// the REST API is the first. The app reads both without knowing which wrote them,
/// so a field that drifts here shows up as a post that renders wrongly only when it
/// came from a sales deadline.
///
/// It is copied rather than shared because Pdc.Mobile is a deployable, not a
/// package. FirebaseNewsFeedPostRepositoryTests pins the serialized shape.
/// </summary>
public class NewsFeedPostDocument
{
    /// <summary>Unique post identifier, format post-{guid:N}.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Always "general" for a sales deadline announcement.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Always "high": a deadline matters, but it is not an emergency.</summary>
    public string Priority { get; set; } = string.Empty;

    /// <summary>The admin who set the deadline up.</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string UpdatedAt { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public string? DeletedAt { get; set; }

    /// <summary>
    /// Ordered media, already validated and server-stamped by the REST API when the
    /// deadline was saved. Null when the announcement carries none.
    /// </summary>
    public List<NewsFeedMediaItemDocument>? Media { get; set; }
}

/// <summary>
/// A media attachment. Copy of Pdc.Mobile's NewsFeedMediaItemDocument.
///
/// Provider and ThumbnailUrl are server-owned and were stamped at save time by
/// INewsFeedMediaResolver, so this Lambda never parses a video URL.
/// </summary>
public class NewsFeedMediaItemDocument
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Provider { get; set; }

    public string? ThumbnailUrl { get; set; }
}
