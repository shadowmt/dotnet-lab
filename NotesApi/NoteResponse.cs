namespace NotesApi
{
    public record NoteResponse(
        int id,
        string station,
        string? type,
        string body,
        int severity,
        DateTime createdAt
    );
}
