using HookahPlatform.BuildingBlocks;
using HookahPlatform.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.AddHookahServiceDefaults("api-gateway");

var app = builder.Build();
app.UseHookahServiceDefaults();

app.MapGet("/api/catalog/services", () => Results.Ok(ServiceCatalog.Services));

app.MapGet("/api/catalog/routes", () => Results.Ok(ServiceCatalog.Services.Select(service => new
{
    service.Code,
    service.BasePath,
    Upstream = $"http://{service.Code}:8080{service.BasePath}"
})));

app.Run();
