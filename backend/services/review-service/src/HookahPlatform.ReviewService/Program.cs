using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.ReviewService.Persistence;
using HookahPlatform.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("review-service");
builder.AddPostgresDbContext<ReviewDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<ReviewDbContext>("review-service");

app.MapPost("/api/reviews", async (CreateReviewRequest request, ReviewDbContext db, IEventPublisher events, CancellationToken cancellationToken) =>
{
    if (request.Rating is < 1 or > 5)
    {
        return HttpResults.Validation("Rating must be from 1 to 5.");
    }

    var review = new ReviewEntity
    {
        Id = Guid.NewGuid(),
        ClientId = request.ClientId,
        MixId = request.MixId,
        OrderId = request.OrderId,
        Rating = request.Rating,
        Text = request.Text,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Reviews.Add(review);
    var created = new ReviewCreated(review.Id, review.ClientId, review.MixId, review.Rating, DateTimeOffset.UtcNow);
    var outboxMessage = db.AddOutboxMessage(created);
    await db.SaveChangesAsync(cancellationToken);
    await db.ForwardAndMarkOutboxAsync(events, created, outboxMessage, cancellationToken);

    return Results.Created($"/api/reviews/{review.Id}", review);
});

app.MapGet("/api/reviews", async (Guid? mixId, Guid? orderId, int? rating, ReviewDbContext db, CancellationToken cancellationToken) =>
{
    var query = db.Reviews.AsNoTracking();
    if (mixId is not null)
    {
        query = query.Where(review => review.MixId == mixId);
    }
    if (orderId is not null)
    {
        query = query.Where(review => review.OrderId == orderId);
    }
    if (rating is not null)
    {
        query = query.Where(review => review.Rating == rating);
    }

    return Results.Ok(await query.OrderByDescending(review => review.CreatedAt).ToListAsync(cancellationToken));
});

app.MapGet("/api/reviews/mixes/{mixId:guid}/summary", async (Guid mixId, ReviewDbContext db, CancellationToken cancellationToken) =>
{
    var scoped = await db.Reviews.AsNoTracking().Where(review => review.MixId == mixId).ToListAsync(cancellationToken);
    if (scoped.Count == 0)
    {
        return Results.Ok(new ReviewSummary(mixId, 0, 0, []));
    }

    var distribution = scoped
        .GroupBy(review => review.Rating)
        .OrderBy(group => group.Key)
        .Select(group => new RatingBucket(group.Key, group.Count()))
        .ToArray();

    return Results.Ok(new ReviewSummary(mixId, scoped.Count, Math.Round(scoped.Average(review => review.Rating), 2), distribution));
});

app.MapGet("/api/reviews/clients/{clientId:guid}", async (Guid clientId, ReviewDbContext db, CancellationToken cancellationToken) =>
    Results.Ok(await db.Reviews.AsNoTracking().Where(review => review.ClientId == clientId).OrderByDescending(review => review.CreatedAt).ToListAsync(cancellationToken)));

app.Run();

public sealed record CreateReviewRequest(Guid ClientId, Guid? MixId, Guid? OrderId, int Rating, string? Text);
public sealed record ReviewSummary(Guid MixId, int ReviewsCount, double AverageRating, IReadOnlyCollection<RatingBucket> Distribution);
public sealed record RatingBucket(int Rating, int Count);
