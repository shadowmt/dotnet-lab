using Microsoft.EntityFrameworkCore;

namespace NotesApi
{
    public class NotesDbContext : DbContext
    {
        public NotesDbContext(DbContextOptions<NotesDbContext> options) : base(options)
        {
        }

        public DbSet<Note> Notes => Set<Note>();
    }
}