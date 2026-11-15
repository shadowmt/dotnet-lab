namespace NotesApi
{
    public interface INoteService
    {
        Task<NoteResponse> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken);
        Task<IReadOnlyList<NoteResponse>> GetPageAsync(int skip, int take, CancellationToken cancellationToken);
        Task<NoteResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<NoteResponse?> PatchAsync(int id, PatchNoteRequest request, CancellationToken cancellationToken);
    }
}