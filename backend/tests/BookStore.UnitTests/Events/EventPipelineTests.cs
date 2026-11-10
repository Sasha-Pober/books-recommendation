using BookStore.Domain.Entities;
using BookStore.Domain.Enums;
using BookStore.Infrastructure.Events;
using BookStore.Infrastructure.Identity;
using BookStore.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookStore.UnitTests.Events;

public class EventPipelineTests
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public void Queue_RejectsWholeRequest_WhenItDoesNotFit()
    {
        var queue = new ChannelInteractionQueue(Options.Create(new EventQueueOptions { Capacity = 3 }));

        Assert.True(queue.TryEnqueue(Views(Guid.NewGuid(), 2)));
        Assert.False(queue.TryEnqueue(Views(Guid.NewGuid(), 2)));
        Assert.Equal(2, queue.Reader.Count);
        Assert.True(queue.TryEnqueue(Views(Guid.NewGuid(), 1)));
    }

    [Fact]
    public async Task Store_SavesValidEvents_AndDropsUnknownBooksAndUsers()
    {
        await using var db = TestData.CreateDbContext(_databaseName);
        var book = TestData.Book("Дюна", new Author { FullName = "Френк Герберт" });
        var user = new AppUser { UserName = "a@b.c", Email = "a@b.c", FullName = "Reader" };
        db.Books.Add(book);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var batch = new[]
        {
            Interaction.Create(user.Id, "s", book.Id, InteractionType.View),
            Interaction.Create(null, "s", book.Id, InteractionType.View),
            Interaction.Create(null, "s", Guid.NewGuid(), InteractionType.View),     // unknown book
            Interaction.Create(Guid.NewGuid(), "s", book.Id, InteractionType.View),  // deleted account
        };

        var saved = await new InteractionBatchStore(db, NullLogger<InteractionBatchStore>.Instance)
            .SaveAsync(batch, CancellationToken.None);

        Assert.Equal(2, saved);
        await using var check = TestData.CreateDbContext(_databaseName);
        Assert.Equal(2, check.Interactions.Count());
    }

    [Fact]
    public async Task Writer_FlushesPartialBatch_AfterFlushInterval()
    {
        var bookId = await SeedBookAsync();
        var (queue, writer) = CreatePipeline(new EventQueueOptions { BatchSize = 100, FlushInterval = TimeSpan.FromMilliseconds(50) });
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
        var (queue, writer) = CreatePipeline(new EventQueueOptions { BatchSize = 10, FlushInterval = TimeSpan.FromMinutes(1) });
        await writer.StartAsync(CancellationToken.None);

        queue.TryEnqueue(Views(bookId, 25));
        await WaitForAsync(() => CountInteractions() == 20);

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(25, CountInteractions());
        Assert.False(queue.TryEnqueue(Views(bookId, 1)));
    }

    private (ChannelInteractionQueue Queue, InteractionBatchWriter Writer) CreatePipeline(EventQueueOptions options)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<BookStoreDbContext>(builder => TestData.UseInMemory(builder, _databaseName))
            .AddScoped<InteractionBatchStore>()
            .BuildServiceProvider();
        var queue = new ChannelInteractionQueue(Options.Create(options));
        var writer = new InteractionBatchWriter(
            queue,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            TimeProvider.System,
            services.GetRequiredService<ILogger<InteractionBatchWriter>>());
        return (queue, writer);
    }

    private async Task<Guid> SeedBookAsync()
    {
        await using var db = TestData.CreateDbContext(_databaseName);
        var book = TestData.Book("Дюна", new Author { FullName = "Френк Герберт" });
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private int CountInteractions()
    {
        using var db = TestData.CreateDbContext(_databaseName);
        return db.Interactions.Count();
    }

    private static List<Interaction> Views(Guid bookId, int count) =>
        Enumerable.Range(0, count).Select(_ => Interaction.Create(null, "s", bookId, InteractionType.View)).ToList();

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the event writer.");
            await Task.Delay(20);
        }
    }
}
