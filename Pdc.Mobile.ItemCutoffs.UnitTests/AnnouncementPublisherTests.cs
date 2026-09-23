using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pdc.EventPro.Domain.Entities;
using Pdc.Mobile.ItemCutoffs.Models;
using Pdc.Mobile.ItemCutoffs.Repositories;
using Pdc.Mobile.ItemCutoffs.Repositories.Abstract;
using Pdc.Mobile.ItemCutoffs.Services;
using System.Text.Json;

namespace Pdc.Mobile.ItemCutoffs.UnitTests;

///////////////////////////////////////////////
/// ANNOUNCEMENT PUBLISHER TESTS
///////////////////////////////////////////////

/// <summary>
/// The announcement post is written by THIS Lambda and by the REST API, into the
/// same RTDB node, and the app reads both without knowing which wrote them.
///
/// These pin the document shape against that contract. A field that drifts here is
/// a post that renders wrongly only when it came from a sales deadline, which is
/// the kind of bug nobody reproduces.
/// </summary>
[TestClass]
public class AnnouncementPublisherTests
{
    private Mock<IFirebaseNewsFeedPostRepository> postRepository = null!;
    private Mock<INewsFeedPushEnqueuer> pushEnqueuer = null!;
    private AnnouncementPublisher publisher = null!;

    [TestInitialize]
    public void Setup()
    {
        postRepository = new Mock<IFirebaseNewsFeedPostRepository>();
        pushEnqueuer = new Mock<INewsFeedPushEnqueuer>();

        publisher = new AnnouncementPublisher(
            NullLogger.Instance,
            postRepository.Object,
            pushEnqueuer.Object);
    }

    private static EventItemCutoffGroup Group(bool notify = true, string? mediaJson = null) => new()
    {
        Id = 1,
        EventId = 500,
        EventUuid = "event-uuid",
        Name = "Saturday 1 PM",
        PostTitle = "30 Minutes: Novice J&J Closes at 1:00 PM",
        PostBody = "30 minutes left. These close at 1:00 PM:\n- Novice J&J\nGet registered.",
        PostNotify = notify,
        PostMediaJson = mediaJson,
        CreatedByName = "Jane Doe"
    };

    private NewsFeedPostDocument CapturePost()
    {
        NewsFeedPostDocument? captured = null;

        postRepository
            .Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        return captured!;
    }

    ///////////////////////////////////////////////
    /// THE DOCUMENT SHAPE
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task PostCarriesTheStoredCopyVerbatim()
    {
        // The copy was generated, previewed and approved when the deadline was saved.
        // Nothing rewrites it here.
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        var group = Group();
        await publisher.Publish(group, dryRun: false);

        Assert.AreEqual(group.PostTitle, captured!.Title);
        Assert.AreEqual(group.PostBody, captured.Body);
    }

    [TestMethod]
    public async Task PostIsGeneralAndHighPriority()
    {
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(), dryRun: false);

        // general, not scheduleChange: a deadline is news, not something that moved.
        Assert.AreEqual("general", captured!.Type);
        // high, never urgent: a weekend where every post is urgent has no way left to
        // say something actually is.
        Assert.AreEqual("high", captured.Priority);
    }

    [TestMethod]
    public async Task PostIsBylinedToTheAdminWhoSetTheDeadlineUp()
    {
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(), dryRun: false);

        Assert.AreEqual("Jane Doe", captured!.AuthorName);
    }

    [TestMethod]
    public async Task PostIdUsesTheSamePrefixAsAHandWrittenPost()
    {
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(), dryRun: false);

        StringAssert.StartsWith(captured!.Id, "post-");
        Assert.AreEqual("post-".Length + 32, captured.Id.Length);
    }

    [TestMethod]
    public async Task PostIsNotDeleted()
    {
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(), dryRun: false);

        Assert.IsFalse(captured!.IsDeleted);
        Assert.IsNull(captured.DeletedAt);
    }

    ///////////////////////////////////////////////
    /// SERIALIZED SHAPE
    ///////////////////////////////////////////////

    [TestMethod]
    public void SerializedPostIsCamelCaseAndOmitsNulls()
    {
        // Mirrors Pdc.Mobile FirebaseNewsFeedRepository.JsonOptions. If these two
        // drift, a sales deadline post is shaped differently from a typed one.
        var json = JsonSerializer.Serialize(
            new NewsFeedPostDocument
            {
                Id = "post-abc",
                Title = "T",
                Body = "B",
                Type = "general",
                Priority = "high",
                AuthorName = "Jane Doe",
                CreatedAt = "2026-09-05T17:00:00Z",
                UpdatedAt = "2026-09-05T17:00:00Z",
                IsDeleted = false,
                DeletedAt = null,
                Media = null
            },
            FirebaseNewsFeedPostRepository.JsonOptions);

        StringAssert.Contains(json, "\"authorName\":");
        StringAssert.Contains(json, "\"isDeleted\":");
        Assert.IsFalse(json.Contains("deletedAt"), "A null must be omitted, not written as null.");
        Assert.IsFalse(json.Contains("\"media\""), "Absent media must be omitted entirely.");
    }

    [TestMethod]
    public void PostPathMatchesTheApisPathBuilder()
    {
        Assert.AreEqual(
            "events/abc-123/news_feed/posts/post-xyz.json",
            FirebaseNewsFeedPostRepository.BuildPostPath("abc-123", "post-xyz"));
    }

    ///////////////////////////////////////////////
    /// MEDIA
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task StoredMediaIsAttachedAsServerStamped()
    {
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        var mediaJson = "[{\"id\":\"media-1\",\"type\":\"video\",\"url\":\"https://youtu.be/x\",\"provider\":\"youtube\",\"thumbnailUrl\":\"https://img/x.jpg\"}]";

        await publisher.Publish(Group(mediaJson: mediaJson), dryRun: false);

        Assert.AreEqual(1, captured!.Media!.Count);
        // Provider and thumbnail were stamped by the API at save time; this Lambda
        // never parses a video URL.
        Assert.AreEqual("youtube", captured.Media[0].Provider);
    }

    [TestMethod]
    public async Task CorruptMediaCostsTheAttachmentsNotThePost()
    {
        // The words carry the deadline. An announcement with no image still works.
        NewsFeedPostDocument? captured = null;
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback<string, NewsFeedPostDocument>((_, post) => captured = post)
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(mediaJson: "{not json"), dryRun: false);

        Assert.IsNotNull(captured);
        Assert.IsNull(captured!.Media);
    }

    ///////////////////////////////////////////////
    /// PUSH
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task PushIsEnqueuedAfterThePostIsWritten()
    {
        // A push that arrives before the post exists deep-links to nothing.
        var order = new List<string>();

        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback(() => order.Add("post"))
            .Returns(Task.CompletedTask);

        pushEnqueuer.Setup(x => x.Enqueue(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .Callback(() => order.Add("push"))
            .Returns(Task.CompletedTask);

        await publisher.Publish(Group(notify: true), dryRun: false);

        CollectionAssert.AreEqual(new[] { "post", "push" }, order);
    }

    [TestMethod]
    public async Task PushIsSkippedWhenTheDeadlineAsksForNoNotification()
    {
        await publisher.Publish(Group(notify: false), dryRun: false);

        pushEnqueuer.Verify(x => x.Enqueue(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()), Times.Never);
    }

    [TestMethod]
    public async Task AFailedPushDoesNotUndoASuccessfulPost()
    {
        // Retrying the announcement would write a SECOND post. A missing push is a
        // quiet feed entry; a duplicated post is visible to every attendee.
        pushEnqueuer.Setup(x => x.Enqueue(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .ThrowsAsync(new InvalidOperationException("queue down"));

        var postId = await publisher.Publish(Group(notify: true), dryRun: false);

        Assert.IsFalse(string.IsNullOrWhiteSpace(postId));
        postRepository.Verify(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()), Times.Once);
    }

    [TestMethod]
    public async Task AFailedPostPropagates_SoTheTickCanRetry()
    {
        // Swallowing this would mark the announcement sent when nothing was written.
        postRepository.Setup(x => x.WritePost(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()))
            .ThrowsAsync(new HttpRequestException("firebase down"));

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => publisher.Publish(Group(), dryRun: false));

        pushEnqueuer.Verify(x => x.Enqueue(It.IsAny<string>(), It.IsAny<NewsFeedPostDocument>()), Times.Never);
    }

    ///////////////////////////////////////////////
    /// DRY RUN
    ///////////////////////////////////////////////

    [TestMethod]
    public async Task DryRun_WritesNothingAndQueuesNothing()
    {
        await publisher.Publish(Group(), dryRun: true);

        postRepository.VerifyNoOtherCalls();
        pushEnqueuer.VerifyNoOtherCalls();
    }
}
