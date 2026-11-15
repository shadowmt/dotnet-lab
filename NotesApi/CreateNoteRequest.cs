namespace NotesApi
{
    using System.ComponentModel.DataAnnotations;

    public record CreateNoteRequest(
        [Required]
        [StringLength(40, MinimumLength = 1)]
        string station,
        
        [StringLength(20)]
        string? type,

        [Required]
        [StringLength(500, MinimumLength = 1)]
        string body,

        [Range(1, 5)]
        int severity
    );
}
