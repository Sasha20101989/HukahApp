using HookahPlatform.BuildingBlocks;
using HookahPlatform.BuildingBlocks.Persistence;
using HookahPlatform.ReviewService.Persistence;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("review-service");
builder.AddPostgresDbContext<ReviewDbContext>();

var app = builder.Build();
app.UseHookahServiceDefaults();
app.MapPersistenceHealth<ReviewDbContext>("review-service");

var reviews = new Dictionary<Guid, Review>();
SeedReviews(reviews);

app.MapPost("/api/reviews", async (CreateReviewRequest request, IEventPublisher events) =>
{
    if (request.Rating is < 1 or > 5)
    {
        return HttpResults.Validation("Rating must be from 1 to 5.");
    }

    var review = new Review(Guid.NewGuid(), request.ClientId, request.MixId, request.OrderId, request.Rating, request.Text, DateTimeOffset.UtcNow);
    reviews[review.Id] = review;
    await events.PublishAsync(new ReviewCreated(review.Id, review.ClientId, review.MixId, review.Rating, DateTimeOffset.UtcNow));

    return Results.Created($"/api/reviews/{review.Id}", review);
});

app.MapGet("/api/reviews", (Guid? mixId, Guid? orderId, int? rating) =>
{
    var query = reviews.Values.AsEnumerable();
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

    return Results.Ok(query.OrderByDescending(review => review.CreatedAt));
});

app.MapGet("/api/reviews/mixes/{mixId:guid}/summary", (Guid mixId) =>
{
    var scoped = reviews.Values.Where(review => review.MixId == mixId).ToArray();
    if (scoped.Length == 0)
    {
        return Results.Ok(new ReviewSummary(mixId, 0, 0, []));
    }

    var distribution = scoped
        .GroupBy(review => review.Rating)
        .OrderBy(group => group.Key)
        .Select(group => new RatingBucket(group.Key, group.Count()))
        .ToArray();

    return Results.Ok(new ReviewSummary(mixId, scoped.Length, Math.Round(scoped.Average(review => review.Rating), 2), distribution));
});

app.MapGet("/api/reviews/clients/{clientId:guid}", (Guid clientId) =>
    Results.Ok(reviews.Values.Where(review => review.ClientId == clientId).OrderByDescending(review => review.CreatedAt)));

app.Run();

static void SeedReviews(IDictionary<Guid, Review> reviews)
{
    var review = new Review(
        Guid.Parse("b0000000-0000-0000-0000-000000000001"),
        Guid.Parse("90000000-0000-0000-0000-000000000001"),
        Guid.Parse("70000000-0000-0000-0000-000000000001"),
        null,
        5,
        "Отличный микс",
        DateTimeOffset.UtcNow.AddDays(-1));

    reviews[review.Id] = review;
}

public sealed record Review(Guid Id, Guid ClientId, Guid? MixId, Guid? OrderId, int Rating, string? Text, DateTimeOffset CreatedAt);
public sealed record CreateReviewRequest(Guid ClientId, Guid? MixId, Guid? OrderId, int Rating, string? Text);
public sealed record ReviewSummary(Guid MixId, int ReviewsCount, double AverageRating, IReadOnlyCollection<RatingBucket> Distribution);
public sealed record RatingBucket(int Rating, int Count);
