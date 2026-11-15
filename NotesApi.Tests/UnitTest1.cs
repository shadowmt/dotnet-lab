using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NotesApi.Tests;

public class NotesApiIntegrationTests(NotesApiFactory factory): IClassFixture<NotesApiFactory>
{
    [Fact]
    public async Task GetPostGet_RoundTropsNoteThroughApi()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            Assert.Equal("lab_test", db.Database.GetDbConnection().Database);
            await db.Database.MigrateAsync();
            await db.Notes.ExecuteDeleteAsync();
        }

        using var client = factory.CreateClient();

        var initialResponse = await client.GetAsync("/notes");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        var initialNotes = await initialResponse.Content.ReadFromJsonAsync<NoteResponse[]>();
        Assert.NotNull(initialNotes);
        Assert.Empty(initialNotes);

        var postResponse = await client.PostAsJsonAsync("/notes", new 
        {
            station = "テスト駅",
            type = (string?)null,
            body = "統合テスト",
            severity = 3
        });

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        Assert.NotNull(postResponse.Headers.Location);

        var getResponse = await client.GetAsync(postResponse.Headers.Location!);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var note = await getResponse.Content.ReadFromJsonAsync<NoteResponse>();
        Assert.NotNull(note);
        Assert.Equal("テスト駅", note.station);
        Assert.Equal("統合テスト", note.body);
        Assert.Equal(3, note.severity);
    }
}
