using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NotesApi.Tests;

public sealed class NotesApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Additional test-specific service configuration can go here
            services.RemoveAll<NotesDbContext>();
            services.RemoveAll<DbContextOptions<NotesDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<NotesDbContext>>();

            services.AddDbContext<NotesDbContext>(options =>
                options.UseNpgsql("Host=127.0.0.1;Port=5432;Database=lab_test;Username=app"));
        });
    }
}