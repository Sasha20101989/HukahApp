using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("review-service");

var app = builder.Build();
app.UseHookahServiceDefaults();

var reviews = new Dictionary<Guid, Review>();

app.MapPost("/api/reviews", async (CreateReviewRequest request, IEventPublisher events) =>
{
    if (request.Rating is < 1 or > 5)
    {
        return HttpResults.Validation("Rating must be from 1 to 5.");
    }

    var review = new Review(Guid.NewGuid(), request.ClientId, request.MixId, request.OrderId, request.Rating, request.Text, DateTimeOffset.UtcNow);
    reviews[review.Id] = review;
    await events.PublishAsync(new ReviewCreated(review.Id, review.ClientId, review.Rating, DateTimeOffset.UtcNow));

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

app.Run();

public sealed record Review(Guid Id, Guid ClientId, Guid? MixId, Guid? OrderId, int Rating, string? Text, DateTimeOffset CreatedAt);
public sealed record CreateReviewRequest(Guid ClientId, Guid? MixId, Guid? OrderId, int Rating, string? Text);
