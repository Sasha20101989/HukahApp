using Microsoft.AspNetCore.Http;

namespace HookahPlatform.BuildingBlocks;

public static class HttpResults
{
    public static IResult NotFound(string resource, Guid id)
    {
        return Results.NotFound(new ProblemDetailsDto("not_found", $"{resource} '{id}' was not found."));
    }

    public static IResult Validation(string message)
    {
        return Results.BadRequest(new ProblemDetailsDto("validation_error", message));
    }

    public static IResult Conflict(string message)
    {
        return Results.Conflict(new ProblemDetailsDto("conflict", message));
    }
}

public sealed record ProblemDetailsDto(string Code, string Message);
