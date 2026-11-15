using Microsoft.EntityFrameworkCore;

namespace NotesApi
{
    public sealed class NoteService(NotesDbContext db) : INoteService
    {
        public async Task<NoteResponse> CreateAsync(CreateNoteRequest request, CancellationToken cancellationToken)
        {
            var note = new Note
            {
                Station = request.station,
                Type = request.type,
                Body = request.body,
                Severity = request.severity,
                CreatedAt = DateTime.UtcNow
            };

            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);

            return new NoteResponse(
                note.Id,
                note.Station,
                note.Type,
                note.Body,
                note.Severity,
                note.CreatedAt
            );
        }

        public async Task<IReadOnlyList<NoteResponse>> GetPageAsync(int skip, int take, CancellationToken cancellationToken)
        {
            return await db.Notes
                .AsNoTracking()
                .OrderBy(note => note.Id)
                .Skip(skip)
                .Take(take)
                .Select(note => new NoteResponse(
                    note.Id,
                    note.Station,
                    note.Type,
                    note.Body,
                    note.Severity,
                    note.CreatedAt
                ))
                .ToListAsync(cancellationToken);
        }

        public async Task<NoteResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            var note = await db.Notes
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

            return note is null
                ? null
                : new NoteResponse(
                note.Id,
                note.Station,
                note.Type,
                note.Body,
                note.Severity,
                note.CreatedAt
            );
        }

        public async Task<NoteResponse?> PatchAsync(int id, PatchNoteRequest request, CancellationToken cancellationToken)
        {
            var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (note is null)
            {
                return null;
            }

            if (request.station is not null) note.Station = request.station;
            if (request.type is not null) note.Type = request.type;
            if (request.body is not null) note.Body = request.body;
            if (request.severity is not null) note.Severity = (int)request.severity;

            await db.SaveChangesAsync(cancellationToken);

            return new NoteResponse(
                note.Id,
                note.Station,
                note.Type,
                note.Body,
                note.Severity,
                note.CreatedAt
            );
        }
    }
}