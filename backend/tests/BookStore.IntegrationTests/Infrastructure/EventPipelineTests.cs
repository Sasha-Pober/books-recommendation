using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Domain.Services;
using BookStore.Infrastructure.Events;
using BookStore.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookStore.IntegrationTests.Infrastructure;

public class EventPipelineTests(PostgresFixture fixture) : PostgresTest(fixture)
{
    [Fact]
    public async Task Store_SavesValidEvents_AndDropsUnknownBooksAndUsers()
    {
        await using var db = Fixture.CreateDbContext();
        var book = TestData.Book("Дюна", new Author { FullName = "Френк Герберт" });
        db.Books.Add(book);
        var user = await TestData.AddUserAsync(db);

        var saved = await CreateStore(db).SaveAsync(
            [
                View(user.Id, book.Id),
                View(null, book.Id),
                View(null, Guid.NewGuid()),     // unknown book
                View(Guid.NewGuid(), book.Id),  // deleted account
            ],
            CancellationToken.None);

        Assert.Equal(2, saved);
        await using var check = Fixture.CreateDbContext();
        Assert.Equal(2, check.Interactions.Count());
    }

    [Fact]
    public async Task Store_MarksRecommendationClicked_OnlyForTheSameUserAndBook()
    {
        await using var db = Fixture.CreateDbContext();
        var author = new Author { FullName = "Френк Герберт" };
        var dune = TestData.Book("Дюна", author);
        var messiah = TestData.Book("Месія Дюни", author);
        db.Books.AddRange(dune, messiah);
        var user = await TestData.AddUserAsync(db);
        var shownToUser = new RecommendationLog { UserId = user.Id, BookId = dune.Id, Placement = RecommendationPlacement.Home };
        var shownToGuest = new RecommendationLog { BookId = dune.Id, Placement = RecommendationPlacement.Home };
        var otherBook = new RecommendationLog { UserId = user.Id, BookId = messiah.Id, Placement = RecommendationPlacement.Home };
        db.RecommendationLogs.AddRange(shownToUser, shownToGuest, otherBook);
        await db.SaveChangesAsync();

        await CreateStore(db).SaveAsync(
            [
                View(user.Id, dune.Id, shownToUser.Id),   // valid click
                View(user.Id, dune.Id, shownToGuest.Id),  // log of another viewer
                View(user.Id, dune.Id, otherBook.Id),     // log of another book
            ],
            CancellationToken.None);

        await using var check = Fixture.CreateDbContext();
        Assert.True(check.RecommendationLogs.Single(l => l.Id == shownToUser.Id).Clicked);
        Assert.False(check.RecommendationLogs.Single(l => l.Id == shownToGuest.Id).Clicked);
        Assert.False(check.RecommendationLogs.Single(l => l.Id == otherBook.Id).Clicked);
        Assert.Equal(3, check.Interactions.Count());
    }

    [Fact]
    public async Task Writer_FlushesPartialBatch_AfterFlushInterval()
    {
        var bookId = await SeedBookAsync();
        var (queue, writer, provider) = CreatePipeline(
            new EventQueueOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMilliseconds(50) });
        await using var _ = provider;
        await writer.StartAsync(CancellationToken.None);

        queue.TryEnqueue(Views(bookId, 3));

        await WaitForAsync(() => CountInteractions() == 3);
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Writer_WritesFullBatches_AndDrainsTheQueueOnShutdown()
    {
        var bookId = await SeedBookAsync();
        // A long interval: only full batches are written while running.
        var (queue, writer, provider) = CreatePipeline(
            new EventQueueOptions { BatchSize = 10, FlushInterval = TimeSpan.FromMinutes(1) });
        await using var _ = provider;
        await writer.StartAsync(CancellationToken.None);

        queue.TryEnqueue(Views(bookId, 25));
        await WaitForAsync(() => CountInteractions() == 20);

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(25, CountInteractions());
        Assert.False(queue.TryEnqueue(Views(bookId, 1)));
    }

    private static InteractionBatchStore CreateStore(BookStoreDbContext db) =>
        new(db, NullLogger<InteractionBatchStore>.Instance);

    private (ChannelInteractionQueue Queue, InteractionBatchWriter Writer, ServiceProvider Provider) CreatePipeline(
        EventQueueOptions options)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<BookStoreDbContext>(builder => builder.UseBookStoreDatabase(Fixture.ConnectionString))
            .AddScoped<InteractionBatchStore>()
            .BuildServiceProvider();
        var queue = new ChannelInteractionQueue(Options.Create(options));
        var writer = new InteractionBatchWriter(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<InteractionBatchWriter>>());
        return (queue, writer, provider);
    }

    private async Task<Guid> SeedBookAsync()
    {
        await using var db = Fixture.CreateDbContext();
        var book = TestData.Book("Дюна", new Author { FullName = "Френк Герберт" });
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private int CountInteractions()
    {
        using var db = Fixture.CreateDbContext();
        return db.Interactions.Count();
    }

    private static List<QueuedView> Views(Guid bookId, int count) =>
        Enumerable.Range(0, count).Select(_ => View(null, bookId)).ToList();

    private static QueuedView View(Guid? userId, Guid bookId, long? recommendationLogId = null) =>
        new(Interaction.Create(userId, "s", bookId, InteractionType.View), recommendationLogId);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the event writer.");
            await Task.Delay(50);
        }
    }
}
