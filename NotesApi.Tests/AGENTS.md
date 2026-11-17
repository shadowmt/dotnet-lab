# NotesApi.Tests（統合テスト）

xUnit + `WebApplicationFactory<Program>` による API ハーネス。モック不使用、全て HTTP 実往復＋実 DB の統合テストのみ。DB はテスト実行ごとに Testcontainers が起動する使い捨て PostgreSQL（DB 名 `ci_test`、run 終わりにコンテナごと削除）。共有 `lab_test` は使わない。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| テスト追加 | `UnitTest1.cs` | ファイル名は無視。実体は `NotesApiIntegrationTests`（`IClassFixture<NotesApiFactory>`） |
| テスト用 DB コンテナ | `NotesApiFactory.cs`（`PostgresContainer`） | `postgres:17` を起動し接続文字列を供給。CI ではテストが自分で DB をたてる |
| 接続先の差し替え | `NotesApiFactory.cs`（`ConfigureWebHost`） | 本番 DbContext 登録を RemoveAll → コンテナ向けに再登録する唯一の場所 |
| CI 実行 | `.github/workflows/test.yml` | service container なし。Testcontainers がコンテナ内で docker を使う |

## CONVENTIONS
- **本番 DbContext 登録の除去は 4 種揃って必要**（`NotesApiFactory.cs` の RemoveAll 群）: `NotesDbContext`, `DbContextOptions<NotesDbContext>`, `DbContextOptions`, `IDbContextOptionsConfiguration<NotesDbContext>`。`ConfigureAppConfiguration` で接続文字列を上書きするだけ案は、この構成では本番 DB `lab` に繋がり失敗した実績あり（`TASKS.md:226`）。
- DB に触る前に fixture の接続文字列とアプリの Host/Port を `Assert.Equal` で一致検証 → `MigrateAsync()` → `ExecuteDeleteAsync()` がテスト先頭の定型。コンテナのホスト側ポートは毎回ランダム（5432 固定ではない）。
- テストは「GET→POST→Location ヘッダ経由 GET」のような**エンドポイント往復**で書く。Service を直接呼ぶ単体テストは作らない（このラボの設計方針）。
- ローカル実行には Docker API アクセスが必要（CI の ubuntu ランナーは同梱で追加設定不要）。podman 環境: `systemctl --user enable --now podman.socket` 済み。手動実行時は `export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock`。

## ANTI-PATTERNS
- テスト・fixture から dev DB（`lab`）へ接続しない。アプリ本体の DbContext 登録を RemoveAll して再登録した構成がコード経路として保証しているが、fixture の接続文字列を上書きする実験でこの保証を壊さない。
- `PostgreSqlBuilder()` をイメージ名なしで書かない（4.10.0 以降デフォルトイメージ廃止でエラー）。イメージは `compose.yml` と同じ `postgres:17` を明示する。
- ファイル名 `UnitTest1.cs` を真似てテストごとに別ファイルへ分割する際、`NotesApi/` 配下に作らない（Web SDK の glob でアプリ側ビルド対象に入る。ルート直下の本ディレクトリのみ）。
- `dotnet test` を root の slnx に対して実行するな。`dotnet test NotesApi.Tests/NotesApi.Tests.csproj` と明示する（TASKS.md の合格手順もこの形）。
