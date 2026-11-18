using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var games = new ConcurrentDictionary<Guid, ChessGame>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/games", () =>
{
    var id = Guid.NewGuid();
    var game = new ChessGame();
    games[id] = game;
    return Results.Ok(new { gameId = id, state = game.GetSnapshot() });
});

app.MapGet("/api/games/{id:guid}", (Guid id) =>
    games.TryGetValue(id, out var game)
        ? Results.Ok(game.GetSnapshot())
        : Results.NotFound(new { error = "Game not found. Start a new game." }));

app.MapPost("/api/games/{id:guid}/moves", (Guid id, MoveRequest? request) =>
{
    if (!games.TryGetValue(id, out var game))
        return Results.NotFound(new { error = "Game not found. Start a new game." });

    if (request?.From is null || request.To is null)
        return Results.BadRequest(new { error = "A starting square and destination square are required." });

    if (!game.TryMove(request.From, request.To, out var error))
        return Results.BadRequest(new { error });

    return Results.Ok(game.GetSnapshot());
});

app.Run();

sealed record MoveRequest(string? From, string? To);
