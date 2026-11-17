using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NotesApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("NotesDb") ?? throw new InvalidOperationException("Connection string 'NotesDb' not found.");

builder.Services.AddDbContext<NotesDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddValidation();
builder.Services.AddScoped<INoteService, NoteService>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Kubernetes の単発マイグレーション Job 用。同一イメージを `--migrate` 付きで起動すると、
// HTTP サーバを立てず DB マイグレーションのみ適用して終了する（EF Core 公式パターン）。
// 通常起動（引数なし）ではこの分岐に入らず、追加動作はゼロ。
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
    await db.Database.MigrateAsync();
    return;
}

app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapGet("/notes", async (INoteService noteService, CancellationToken cancellationToken, int skip = 0, int take = 20) =>
{
    var errors = new Dictionary<string, string[]>();
    
    if (skip < 0)
    {
        errors.Add("skip", ["Must be zero or greater."]);
    }

    if (take < 1 || take > 100)
    {
        errors.Add("take", ["Must be between 1 and 100."]);
    }

    if (errors.Any())
    {
        return Results.ValidationProblem(errors);
    }

    var notes = await noteService.GetPageAsync(skip, take, cancellationToken);
    return Results.Ok(notes);
});

app.MapGet("/notes/{id:int}", async (int id, INoteService noteService, CancellationToken cancellationToken) =>
{
    var note = await noteService.GetByIdAsync(id, cancellationToken);

    return note is null
        ? Results.NotFound()
        : Results.Ok(note);
}); 

app.MapPost("/notes", async (CreateNoteRequest request, INoteService noteService, CancellationToken cancellationToken) =>
{
    var note = await noteService.CreateAsync(request, cancellationToken);

    return Results.Created($"/notes/{note.id}", note);
});

app.MapPatch("/notes/{id:int}", async (int id, PatchNoteRequest request, INoteService noteService, CancellationToken cancellationToken) =>
{
    if (request.station is null && request.type is null && request.body is null && request.severity is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            { "request", ["At least one field must be provided."] }
        });
    }

    var note = await noteService.PatchAsync(id, request, cancellationToken);

    return note is null
        ? Results.NotFound()
        : Results.Ok(note);
});

app.Run();

public partial class Program { } // Needed for integration tests