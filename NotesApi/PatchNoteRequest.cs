namespace NotesApi
{
    using System.ComponentModel.DataAnnotations;

    public record PatchNoteRequest(
        [StringLength(40, MinimumLength = 1)]
        string? station = null,
        [StringLength(20)]
        string? type = null,
        [StringLength(500, MinimumLength = 1)]
        string? body = null,
        [Range(1, 5)]
        int? severity = null
    );
}