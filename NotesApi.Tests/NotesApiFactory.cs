using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace NotesApi.Tests;

public sealed class PostgresContainer : IAsyncDisposable
{
    private const string DatabaseName = "ci_test";

    public PostgreSqlContainer Container { get; }

    public string ConnectionString { get; }

    public PostgresContainer()
    {
        Container = new PostgreSqlBuilder("postgres:17")
            .WithDatabase(DatabaseName)
            .Build();
        Container.StartAsync().GetAwaiter().GetResult();
        ConnectionString = Container.GetConnectionString();
    }

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}

public sealed class NotesApiFactory : WebApplicationFactory<Program>
{
    public PostgresContainer Postgres { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // アプリ本体の NotesDbContext 登録（開発 DB `lab` 向け）を完全に除去する。
            // ConfigureAppConfiguration による接続文字列上書きでは本番登録に勝てない実績あり（TASKS.md:226）。
            services.RemoveAll<NotesDbContext>();
            services.RemoveAll<DbContextOptions<NotesDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<NotesDbContext>>();

            services.AddDbContext<NotesDbContext>(options =>
                options.UseNpgsql(Postgres.ConnectionString));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}