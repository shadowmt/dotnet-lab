# NotesApi.Tests（統合テスト）

xUnit + `WebApplicationFactory<Program>` による API ハーネス。モック不使用、全て HTTP 実往復＋実 DB（`lab_test`）の統合テストのみ。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| テスト追加 | `UnitTest1.cs` | ファイル名は無視。実体は `NotesApiIntegrationTests`（`IClassFixture<NotesApiFactory>`） |
| 接続先の差し替え | `NotesApiFactory.cs:15-25` | `ConfigureTestServices` で本番 DbContext 登録を RemoveAll → `lab_test` 用に再登録する唯一の場所 |
| 実行前の DB 準備 | ルート AGENTS.md の COMMANDS | `createdb -U app lab_test` が無いとテストは接続失敗する |

## CONVENTIONS
- **本番 DbContext 登録の除去は 4 種揃って必要**（`NotesApiFactory.cs:18-21`）: `NotesDbContext`, `DbContextOptions<NotesDbContext>`, `DbContextOptions`, `IDbContextOptionsConfiguration<NotesDbContext>`。`ConfigureAppConfiguration` で接続文字列を上書きするだけ案は、この構成では本番 DB `lab` に繋がり失敗した実績あり（`TASKS.md:226`）。
- DB に触る前に必ず `Assert.Equal("lab_test", db.Database.GetDbConnection().Database)` で接続先を検証してから `MigrateAsync()` → `ExecuteDeleteAsync()`（テスト先頭の定型、`UnitTest1.cs:13-19`）。
- テストは「GET→POST→Location ヘッダ経由 GET」のような**エンドポイント往復**で書く。Service を直接呼ぶ単体テストは作らない（このラボの設計方針）。
- 実行に podman の `dotnet-lab-db` コンテナの起動状態が必要（停止中は `container state improper` で失敗）。

## ANTI-PATTERNS
- テストから DB `lab`（dev 用）に**接続・削除しない**。`ExecuteDeleteAsync` 等で消してよいのは `lab_test` の Notes のみ。
- ファイル名 `UnitTest1.cs` を真似てテストごとに別ファイルへ分割する際、`NotesApi/` 配下に作らない（Web SDK の glob でアプリ側ビルド対象に入る。ルート直下の本ディレクトリのみ）。
- `dotnet test` を root の slnx に対して実行するな。`dotnet test NotesApi.Tests/NotesApi.Tests.csproj` と明示する（TASKS.md の合格手順もこの形）。
