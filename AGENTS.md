# PROJECT KNOWLEDGE BASE

**Generated:** 2026-11-17 09:03 JST
**Commit:** 077b504
**Branch:** main

## OVERVIEW
.NET 10 / ASP.NET Core Minimal API の学習ラボ（Notes API = 「駅メモ」CRUD + EF Core/Npgsql + PostgreSQL 17 + xUnit 統合テスト + podman Compose）。FastAPI からの移行学習用に、`TASKS.md` がタスクリート＝**仕様の正本**。

## STRUCTURE
```
dotnet-lab/
├── TASKS.md                  # 課題・合格条件・実施メモ（日本語）。契約と検証手順の唯一の正本
├── compose.yml               # db(podman 使用可) + api。127.0.0.1 のみにポート公開
├── NotesApi/                 # Web プロジェクト（source は直下にフラット、レイヤディレクトリなし）→ NotesApi/AGENTS.md
│   ├── Program.cs            # 全エンドポイント + DI 登録がここだけ（top-level statements）
│   ├── Migrations/           # EF Core 生成物・コミット済み（手編集禁止）
│   └── NotesApi.slnx         # ソリューションはルートでなく NotesApi/ 配下
└── NotesApi.Tests/           # xUnit 統合テスト（WebApplicationFactory）→ NotesApi.Tests/AGENTS.md
```
`NotesApi.Tests` は**ルート直下**に置く。`NotesApi/` の内側に置くな（Web SDK の既定 glob がテストコードをアプリ側に取り込む）。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| API 契約・合格条件 | `TASKS.md`（20-39 行） | 実装前に契約を決める側。`/openapi/v1.json` は実装後の成果物 |
| エンドポイント追加 | `NotesApi/Program.cs` | コントローラなし。Minimal API のみ |
| ビジネス処理 | `NotesApi/NoteService.cs` + `INoteService.cs` | Service 層が DbContext を持つ構成が確定形 |
| 検証規則（Pydantic 相当） | DTO の DataAnnotations | `CreateNoteRequest.cs` / `PatchNoteRequest.cs` |
| DB 接続先 | dev: `appsettings.Development.json` / compose: `CONNECTIONSTRINGS__NOTESDB` env / test: `NotesApi.Tests/NotesApiFactory.cs`（Testcontainers） | 3 経路で接続先が別れる。test の接続文字列はコード直書きでなくテスト用コンテナ起動時に採番 |
| 手元 DB 確認 | `podman exec dotnet-lab-db psql -U app -d lab -c '\dt'` | docker でなく **podman**（`docker` は未インストール） |
| 学習状況の確認 | `TASKS.md` の `[x]` | Task 0-3,5,6,7,8 完了済み（未: Task 4 の合格判定・Task 9 任意） |

## CODE MAP
| Symbol | Type | Location | Refs | Role |
|--------|------|----------|------|------|
| `Program` | class (partial) | `NotesApi/Program.cs:86` | 2 | 起動 + 全 route 定義。`partial class Program {}` は `WebApplicationFactory<Program>` 用。**削除禁止** |
| `INoteService` | interface | `NotesApi/INoteService.cs:3` | 7 | Service 抽象。エンドポイントは型に依存 |
| `NoteService` | sealed class | `NotesApi/NoteService.cs:5` | 2 | EF 実装（primary ctor で `NotesDbContext db`） |
| `NotesDbContext` | class | `NotesApi/NotesDbContext.cs:5` | 9 | `DbSet<Note> Notes => Set<Note>()`。dev/test/compose 共通 |
| `Note` | class | `NotesApi/Note.cs:3` | 3 | EF エンティティ。`required` + `string?` で null 可能性を明示 |
| `CreateNoteRequest` / `PatchNoteRequest` / `NoteResponse` | record | `NotesApi/*.cs` | 4/4/15 | 入出力 DTO。JSON 契約のためパラメータ名が camelCase |
| `NotesApiFactory` / `PostgresContainer` | sealed class | `NotesApi.Tests/NotesApiFactory.cs:32` / `:12` | 2 | `WebApplicationFactory<Program>` + Testcontainers 使い捨て `postgres:17`（DB 名 `ci_test`）供給 |

エンドポイント一覧（DELETE は存在しない）: `GET /health` / `GET /notes?skip=&take=` / `GET /notes/{id:int}` / `POST /notes` / `PATCH /notes/{id:int}`

## CONVENTIONS
- **WSL 開発の鉄則**: プロジェクトは WSL 側に置き、build / run も必ず WSL 側。Windows 側の .NET で `\\wsl.localhost` 越しに build すると `bin/obj` の静的資産マニフェストに UNC パスが書かれ、WSL 側の起動が `ArgumentException: The path must be absolute (Parameter 'root')` でクラッシュする。汚染時は `bin/obj` の staticwebassets 系を削除して WSL 側で再 build（`TASKS.md:52`）。
- **DB は使い分け、削除対象は厳格**: dev = `lab` / compose = `lab`。テスト DB は Testcontainers の使い捨てコンテナ（名 `ci_test`・ホスト側ポートランダム）で、`lab_test` 作成/削除の運用は廃止。テストは `lab` に接続しない（アプリ側 DbContext 登録を RemoveAll → 再登録する設計自体が保証、`NotesApi.Tests/AGENTS.md`）。
- **検証は `AddValidation()` + `AddProblemDetails()` + `UseStatusCodePages()`** の 3 点セットで成立させる。このプロジェクトの .NET 10 参照では `.WithValidation()` と `app.UseProblemDetails()` はビルドエラーになる（`TASKS.md:77`, `TASKS.md:117`）。
- **DTO の record パラメータは camelCase が既定**。C# 慣習は PascalCase だが JSON 契約を壊さないため意図的に据え置き（`TASKS.md:162`）。リネームは判断を明示してから。
- **パッケージのバージョンは 10 系にピン留め**。`dotnet add package` でバージョンを省略すると `Microsoft.EntityFrameworkCore.Design` に `net10.0` 非対応の 11.0.0 が選ばれ失敗した実績（`TASKS.md:121-125`）。
- **コンテナは podman**。DB のポート公開は `127.0.0.1:5432` のみ・trust 認証を維持するため、API からも DB に `Host=db`（Compose 内 DNS）でつなぐ。`host.containers.internal` 経由（`169.254.1.2`）は loopback 限定公開ゆえ接続拒否になる（`TASKS.md:254-255`）。
- `bin/ obj/ .vs/ *.user appsettings.*.local.json` はコミットしない（`.gitignore`）。

## ANTI-PATTERNS (THIS PROJECT)
- **HTTPS リダイレクトを戻さない**: テンプレートの `UseHttpsRedirection` は学習用に意図的コメントアウト済み。http のみで動かす判断が `TASKS.md:63` に明記されている。
- **OpenApi / Scalar は Development のみ**: `if (app.Environment.IsDevelopment())` の外に出すな。Compose の API は Production なので `/scalar` は意図的に 404。
- **`appsettings.Development.json` をイメージに載せない**: `.dockerignore` で除外済み。接続先は実行時 env で渡す。
- **マイグレーションの手編集・リネームは禁止**: `Migrations/` は `dotnet ef` の生成物。
- **検索や推測で DB を触らない**: 実測確認なしに「成功した」と判定するな（初回 `database update` では `__EFMigrationsHistory` への未作成 SELECT エラーがログに出たが適用自体は成功していた → `TASKS.md:152`）。
- **未検証の API 名を書くな**: `.WithValidation()` / `UseProblemDetails()` のように、この構成で存在しないメソッドを提案しない。

## UNIQUE STYLES
- 学習ラボ＝**実装はエージェント、合格判定の実測検証とレビューは人間**。「Task N 完了」と投げてレビューを受ける運用（`TASKS.md:289-293`）。
- `TASKS.md` に実施メモ（日付入り）を追記してゆく。タスクを進めたら結果をそこに書き残す。
- C# の同期リークを避けるため、全 Service メソッドは `CancellationToken cancellationToken` を末尾引数に取る。
- 各タスクに「合格条件」があり、curl / psql / `dotnet test` の実測で PASS 判定する（再現手順なしの自己申告は不可）。

## COMMANDS
```bash
# ビルド（ルートから。slnx は NotesApi/ 配下）
dotnet build NotesApi/NotesApi.slnx

# 開発起動（http プロファイル = :5245 / ASPNETCORE_ENVIRONMENT=Development）
cd NotesApi && dotnet run --launch-profile http     # 差分監視は dotnet watch run

# OpenAPI / Scalar UI（Development のみ）
curl -s localhost:5245/openapi/v1.json | head   ;   # http://localhost:5245/scalar

# DB（podman）
podman compose up -d db                              # 既存が停止中の場合: podman start dotnet-lab-db
podman exec dotnet-lab-db pg_isready -U app -d lab   # accepting connections を待ってから次へ

# EF Core マイグレーション（グローバルツール 10.0.4 / NotesApi/ で実行）
cd NotesApi && dotnet ef migrations add <Name> && dotnet ef database update

# 統合テスト（DB は Testcontainers が自動起動。事前の createdb 不要）
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock   # podman 利用時（docker バイナリなしで可）
dotnet test NotesApi.Tests/NotesApi.Tests.csproj     # CI と同一手順（.github/workflows/test.yml）

# コンテナ一式（API は http://127.0.0.1:8080 / Production なので /scalar なし）
podman compose up -d --build
curl -s http://127.0.0.1:8080/health
```

## NOTES
- SDK 10.0.404。実行時 EF Core は 10.0.4 に解決されるため、`dotnet-ef` ツールも **10.0.4 に揃える**（10.0.3 だと警告）。インストール済みなら `dotnet tool update --global dotnet-ef --version 10.0.4`。
- `dotnet new webapi -o .` を付け忘れるとサブディレクトリが二重になる（`TASKS.md:58`）。
- `int severity` は JSON 未指定時 `0` になり `[Range]` で 400 になる。値型への `[Required]` は null 判定にならないので実質不要（`TASKS.md:101`）。
- `dotnet ef database update` 初回、`__EFMigrationsHistory` への SELECT エラーがログに出るが適用は成功する。**ログだけで決めず DB を確認**する。
- `.csproj` の CRLF 改行は Linux 側ビルドに支障なし → 変更しない（`TASKS.md:161`）。
- dev DB `lab` には過去の実測用レコードが残っている。テストデータと混同するな。
- テスト DB イメージは `postgres:17`（compose と同一 tag へ統一）。Testcontainers 4.10.0 以降はイメージ名指定が必須のため。手元 podman ソケット（user unit）は起動済み・手動実行時は `DOCKER_HOST` の export が必要。
