namespace NotesApi
{
    public class Note
    {
        public int Id { get; set; }
        public required string Station { get; set; }
        public string? Type { get; set; }
        public required string Body { get; set; }
        public int Severity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}