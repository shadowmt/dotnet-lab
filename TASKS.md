# dotnet-lab タスクリート（.NET 10 / Minimal API 主軸）

題材: **Notes API**（通勤・通学系の「駅メモ」CRUD）
- Entity: `Note(Id, Station, Type, Body, Severity, CreatedAt)`
- DB: PostgreSQL 17（podman / 127.0.0.1:5432 / db=lab / user=app / **パスワードなし trust 認証**）
- コードは全部あなたが書く。私は合格条件の検証とレビューのみ

FastAPI からの対応表:

| FastAPI / Python | ここ |
|---|---|
| uvicorn --reload | `dotnet watch run` |
| Pydantic | record + DataAnnotations |
| Depends | DI コンテナ（`AddScoped`） |
| SQLAlchemy | EF Core (Npgsql) |
| Alembic | `dotnet ef migrations` |
| /docs | `/openapi/v1.json`（+ Scalar で UI） |
| pytest + httpx | xUnit + WebApplicationFactory |

## API 契約（合否判定はこの契約が正）

データモデル `Note`: `id` int（自動連番） / `station` string / `type` string（nullable） / `body` string / `severity` int / `createdAt` datetime（UTC, ISO8601）

| Method | Path | リクエスト本文 | 成功時 | エラー時 |
|---|---|---|---|---|
| POST | `/notes` | CreateNoteRequest | 201 + `Created: /notes/{id}` ヘッダ + NoteResponse | 400 problem+json |
| GET | `/notes?skip=&take=` | - | 200 `NoteResponse[]`（既定 take=20、上限 100、上限超は 400） | 400 |
| GET | `/notes/{id}` | - | 200 NoteResponse | 404 |
| PATCH | `/notes/{id}` | 部分更新（null は変更しない） | 200 NoteResponse | 404 / 400 |

CreateNoteRequest の検証規則（DataAnnotations ＝ Pydantic の Field 相当）:

- `station`: 必須・1〜40 字
- `type`: 任意・20 字以下
- `body`: 必須・1〜500 字
- `severity`: 必須・整数 1〜5

エラー規約: 400 は `application/problem+json`（ProblemDetails）で返す。404 も ProblemDetails 推奨。
NoteResponse: `{ id, station, type, body, severity, createdAt }`（createdAt は ISO8601 UTC）

補足: 契約は実装前に決める側になる。`/openapi/v1.json` は実装**後に自動生成される成果物**（FastAPI で実装前に /docs が見えないのと同じ）

---

## Task 0 環境（完了済み・検証済み）

- [x] SDK 10.0.404（`dotnet --version`）
- [x] DB healthy / 127.0.0.1:5432 LISTEN / pg_isready accepting

## Task 1 スキャフォールド + /health

**WSL 開発の鉄則（実測で学んだ落とし穴）**: プロジェクトは WSL 側に置き、build / run は**必ず WSL 側**。Windows 側の .NET で \\wsl.localhost 越しに build すると、bin/obj の静的資産マニフェストに UNC パスが書かれ、WSL 側での起動がクラッシュする（`ArgumentException: The path must be absolute (Parameter 'root')`）。汚染時の復旧: bin/obj の staticwebassets 系を削除して WSL 側で再 build。Windows 側での閲覧は `--urls http://0.0.0.0:<port>` で `http://<WSLホストのIP>:<port>` にて可。

```
cd ~/dotnet-lab && mkdir NotesApi && cd NotesApi
dotnet new webapi -n NotesApi -f net10.0 -o .
```
（`-o .` を付けないとサブディレクトリが二重になる。注意）

実装要件: `GET /health` が `{"status":"ok"}` を返す
起動: `dotnet watch run`（FastAPI の reload 相当）

はまりポイント: テンプレートに `UseHttpsRedirection` が入っている。http の curl で 307 が返ったらこの行をコメントアウト（学習用に http のみで動かす判断を明記すること）

合格条件: `curl localhost:<port>/health` → 200 JSON / `curl .../openapi/v1.json` → 200 で openapi フィールドあり

## Task 2 DTO・バリデーション・DI

契約: 上記「API 契約」の POST /notes と NoteResponse（CreateNoteRequest / NoteResponse はこのために作る）

- `record` で DTO（CreateNoteRequest / NoteResponse）— Pydantic 相当
- POST で `Severity` が範囲外→ 400（`Results.ValidationProblem` 相当を調べる）
- ビジネス処理を `INoteService` インターフェース＋実装に切り出し `AddScoped` で注入（Depends 相当）

合格条件: 不正 Severity の POST → 400、正常 → 201 + Created ヘッダ

**Task 2 通過構成（実測）**: `AddValidation()` ＋ `AddProblemDetails()` ＋ `UseStatusCodePages()` で Severity 範囲外の 400 が `application/problem+json` になる。このプロジェクトの .NET 10 参照では `.WithValidation()` と `UseProblemDetails()` が見つからずビルドエラーになったため使用しない。本番相当の例外処理は別途 `UseExceptionHandler()` を検討する。

## Task 3 EF Core + マイグレーション（Alembic 相当）

```
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.3
# 初回インストール:
dotnet tool install --global dotnet-ef --version 10.0.4
# 既にインストール済みなら、上の代わりに実行:
# dotnet tool update --global dotnet-ef --version 10.0.4
dotnet ef --version
```
- connection string は `appsettings.Development.json` に（PW なし: `Host=127.0.0.1;Port=5432;Database=lab;Username=app`）
- `Note` エンティティ + `NotesDbContext`
- `dotnet ef migrations add InitialCreate` → `dotnet ef database update`

合格条件: `podman exec dotnet-lab-db psql -U app -d lab -c '\dt'` に `Notes` テーブル

## 実施メモ（Task 2〜3、2026-11-14）

### Task 2: DTO・検証・DI

1. `CreateNoteRequest` / `NoteResponse` を record で作成する。入力DTOは `station` 必須・1〜40字、`type` は `string?`・最大20字、`body` 必須・1〜500字、`severity` は `[Range(1, 5)]`。応答DTOには `id` を含める。
2. `int severity` は未指定時に既定値 `0` になるため `[Range]` が400にする。値型への `[Required]` は `null` 判定にならず実質不要。文字列の必須項目には `[Required]` を付ける。
3. `INoteService` と `NoteService` を作り、`builder.Services.AddScoped<INoteService, NoteService>()` で登録してPOSTから注入する。Task 2時点のIDは `Interlocked.Increment` によるプロセス内の仮採番（再起動でリセット）。DB採番に切り替える。
4. 検証サービスを登録し、Problem Detailsを設定する。
   ```csharp
   builder.Services.AddValidation();
   builder.Services.AddProblemDetails();
   // builder.Build() の後
   app.UseStatusCodePages();
   ```
5. 起動プロファイル `http` のポート（現在5245）に不正Severityと正常値をPOSTして確認する。
   ```bash
   curl -i -H 'Content-Type: application/json' \
     -d '{"station":"東京","type":null,"body":"テスト","severity":6}' \
     http://localhost:5245/notes
   ```
   期待値は `400` と `Content-Type: application/problem+json`。Severityを `3` にした正常POSTは `201 Created` と `Location: /notes/{id}`。
6. `.WithValidation()` と `app.UseProblemDetails()` はこのプロジェクトではコンパイルできなかった。`AddProblemDetails()` と `UseStatusCodePages()` の組み合わせは実測でProblem Details形式になった。

### Task 3: EF Core・PostgreSQL・マイグレーション

1. `NotesApi` ディレクトリでパッケージを追加する。バージョン省略時、`Microsoft.EntityFrameworkCore.Design` は当時の最新11.0.0が選ばれ、`net10.0` 非対応で失敗した。EF Designは10系を指定する。
   ```bash
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
   dotnet add package Microsoft.EntityFrameworkCore.Design --version 10.0.3
   ```
2. `appsettings.Development.json` のトップレベルに接続文字列を追加する（開発DBはパスワードなし）。
   ```json
   "ConnectionStrings": {
     "NotesDb": "Host=127.0.0.1;Port=5432;Database=lab;Username=app"
   }
   ```
3. `Note` エンティティ（`Id`, `Station`, `Type?`, `Body`, `Severity`, `CreatedAt`）と `NotesDbContext : DbContext` を作る。`DbSet<Note> Notes => Set<Note>()` は `DbSet` の有効な公開方法。
4. `Program.cs` で接続文字列を読み、`AddDbContext` と `UseNpgsql` を登録する。設定がなければ起動時に明示的に失敗させる。
   ```csharp
   var connectionString = builder.Configuration.GetConnectionString("NotesDb")
       ?? throw new InvalidOperationException("Connection string 'NotesDb' was not found.");
   builder.Services.AddDbContext<NotesDbContext>(
       options => options.UseNpgsql(connectionString));
   ```
5. EF CLIのバージョンを確認する。実行時EF Coreは10.0.4に解決されたため、当初のCLI 10.0.3では警告が出た。CLIを10.0.4に合わせた。
   ```bash
   dotnet tool install --global dotnet-ef --version 10.0.4
   # すでにインストール済みなら:
   dotnet tool update --global dotnet-ef --version 10.0.4
   dotnet ef --version
   ```
6. マイグレーションを作成してDBへ適用する。
   ```bash
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```
   初回適用時、未作成の `__EFMigrationsHistory` へのSELECTがエラーログに出たが、その後 `Applying migration` / `Done` まで進んだ。ログだけで成功と決めず、DBを確認する。
7. Task 3の合格確認:
   ```bash
   podman exec dotnet-lab-db psql -U app -d lab -c '\dt'
   ```
   `Notes` と `__EFMigrationsHistory` の存在を実測確認した。

### 補足メモ

- `.csproj` のCRLF改行はLinux側のビルドに支障がなかったため変更しない。
- DTOのrecord引数名は現在camelCase。C#の慣習はPascalCaseだが、JSONはWeb既定でcamelCaseに変換される。必要になった時に名前変更を判断する。
- `NoteService` の仮ID採番は永続化されない。Task 4でDB保存・DB採番へ切り替える。

## Task 4 CRUD + ページング

- GET /notes?skip=&take=（デフォルト take=20, 上限 100 — クエリ検証）
- GET /notes/{id}（404）、POST（201）、PATCH（404 対応）
- リファクタリング: Service 層が DbContext を持つ構成に整える

合格条件: curl 一通りで 200/201/400/404 が全部出る。`/openapi/v1.json` に 4 つのオペレーション

## Task 5 xUnit 統合テスト（pytest+httpx 相当）

- `NotesApi.Tests` を作り、xUnit と `WebApplicationFactory` でAPIをHTTP経由でテスト
- テストDBを `lab` から分離し、接続先を `lab_test` にする
- テストは**エンドポイント往復**で書く（GET→POST→GET の1本目）

合格条件: `dotnet test` 通過（私は `dropdb lab_test && createdb lab_test` から再実行して再現確認）

### Task 5 手順メモ（2026-11-15）

#### 1. PostgreSQLコンテナとテストDBの準備

DBコンテナが停止している場合、`podman exec` は `container state improper` で失敗する。既存コンテナを起動し、DBがreadyになった後にテストDBを作る。`lab` ではなく `lab_test` のみをテストに使用する。

```bash
podman ps -a --filter name=dotnet-lab-db
# STATUS が Exited の場合
podman start dotnet-lab-db
podman exec dotnet-lab-db pg_isready -U app -d lab
# accepting connections と出てから実行
podman exec dotnet-lab-db createdb -U app lab_test
```

`createdb` が既存DBエラーになった場合は、一覧で存在を確認する。

```bash
podman exec dotnet-lab-db psql -U app -d postgres -c '\l'
```

#### 2. xUnitテストプロジェクト作成

コマンドはリポジトリルートで実行する。テストプロジェクトはWebプロジェクトの配下に置かない。Web SDKの既定globが配下のC#ファイルをアプリ側に取り込むのを避けるため。

```bash
dotnet new xunit -n NotesApi.Tests -f net10.0
dotnet add NotesApi.Tests/NotesApi.Tests.csproj reference NotesApi/NotesApi.csproj
dotnet add NotesApi.Tests/NotesApi.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.4
dotnet sln NotesApi/NotesApi.slnx add NotesApi.Tests/NotesApi.Tests.csproj
dotnet list NotesApi.Tests/NotesApi.Tests.csproj package
```

生成された構成は `xunit 2.9.3` と `Microsoft.NET.Test.Sdk 17.14.1` のVSTest。`Microsoft.AspNetCore.Mvc.Testing` はEF Core実行時依存関係と合わせて10.0.4を指定した。プロジェクト名は `NotesApi.Tests`（Testsは複数形）なので、パスも `NotesApi.Tests/NotesApi.Tests.csproj` とする。

#### 3. APIエントリーポイントをテストから参照

`NotesApi/Program.cs` のトップレベル文の後（`app.Run();` の次）に追加する。`WebApplicationFactory<Program>` がアプリを起動するために必要。

```csharp
public partial class Program { }
```

#### 4. テストホストと統合テスト

- `WebApplicationFactory<Program>` を継承した `NotesApiFactory` を用意し、`ConfigureTestServices` で `NotesDbContext` の本番登録とオプション構成を `RemoveAll` してから `lab_test` 用に再登録する。`ConfigureAppConfiguration` に接続文字列を足すだけの初回案は、この構成では `Program` が使う接続先を上書きできず、開発DBの `lab` に接続してしまった。
- テストコードはDBを操作する前に `Assert.Equal("lab_test", db.Database.GetDbConnection().Database)` で接続先を確認する。確認後にテストDBへマイグレーションを適用し、テストデータを消去する。
- xUnitの統合テストで `HttpClient` から GET→POST→Locationを使ったGET を実行する。201、200と返却内容を検証する。
- 実行コマンド（VSTest）:
  ```bash
  dotnet test NotesApi.Tests/NotesApi.Tests.csproj
  ```
- 2026-11-15にテスト1件が成功。`lab_test` にNotesが作成されることを確認した。初回の誤接続で `lab` に残ったテストレコードは削除せず、そのまま残している。今後もテストでは `lab` に接続・削除しない。
- DBを初期状態から再現確認する場合に限り、`lab_test` のみを削除・再作成してからテストする。`lab` は削除しない。

## Task 6 .http 収集

テンプレートの NotesApi.http を全エンドポイント分だけ記入してください。VS Code REST Client か curl で実行できること

合格条件: .http に全オペレーション + 負ケース 2 件以上

## Task 7 Dockerfile 化

multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` で publish → `mcr.microsoft.com/dotnet/aspnet:10.0` で実行
`podman build` またはComposeからbuildし、APIコンテナの `/health` と GET `/notes` を確認。DBへの接続方法は、コンテナのネットワーク構成とDBポート公開範囲を確認して選ぶ。

合格条件: コンテナ化されたAPIの `/health` と GET `/notes` が成功

### Task 7 実施メモ（2026-11-15）

1. `NotesApi/Dockerfile` はSDK 10.0でRelease publishし、ASP.NET 10.0ランタイムイメージで実行するmulti-stage構成。コンテナのHTTPポートは8080。
2. `NotesApi/.dockerignore` で `bin/`, `obj/`, userファイル、`appsettings.Development.json` をビルドコンテキストから除外。接続先はイメージに埋め込まず実行時環境変数で渡す。
3. ComposeのPostgreSQLは `db` サービスとして既に存在し、名前付きvolumeでデータを永続化。DBポートは `127.0.0.1:5432` のみ公開、認証は開発用のパスワードなしtrust。
4. APIを別の `podman run` で起動して `Host=host.containers.internal` を使うと、名前は `169.254.1.2` に解決するが、DBのポートがホストのloopbackだけにbindされているため接続拒否になった。trust認証のDBを `0.0.0.0` に公開するのは避ける。
5. 安全な開発構成としてAPIもComposeの `api` サービスにし、DBと同じComposeネットワークに接続。接続先は `Host=db;Port=5432;Database=lab;Username=app`（Composeのサービス名 `db`）。DBのhealthcheck後にAPIを起動し、APIポートも `127.0.0.1:8080` のみ公開する。
   ```yaml
   environment:
     CONNECTIONSTRINGS__NOTESDB: "Host=db;Port=5432;Database=lab;Username=app"
   ```
   `__` は .NET設定キーの `:` に対応し、設定キーは大文字小文字を区別しない。`ASPNETCORE_ENVIRONMENT` や `DOTNET_*` はこの公開アプリの実行には不要。
6. 実測でコンテナから `/health` は200、GET `/notes` も200。DB接続とTask 7の合格条件を確認した。`host.containers.internal` の挙動も調査したが、loopback限定公開とtrust認証を維持するためCompose内DNS方式を採用。

## Task 8（選択）Scalar UI で /docs 相当

```
dotnet add package Scalar.AspNetCore
```
テンプレートはUIを含まず、標準では `/openapi/v1.json` のみ。ScalarはサードパーティのOpenAPI UI。

### Task 8 実施メモ（2026-11-15）

1. `NotesApi` ディレクトリで `dotnet add package Scalar.AspNetCore` を実行。`Scalar.AspNetCore 2.21.0` が追加された。
2. `Program.cs` に `using Scalar.AspNetCore;` を追加し、Development環境の条件内で `app.MapScalarApiReference();` を呼び出す。`app.MapOpenApi()` と同じく開発時だけUIを公開する。
3. Development環境で確認。`GET /scalar` は `/scalar/` にリダイレクトされ、`/scalar/` は200 HTML、`/openapi/v1.json` は200 JSONを返した。ビルドも0警告・0エラー。
4. 開発環境で起動する場合は、NotesApiディレクトリから実行する。
   ```bash
   cd <リポジトリルート>/NotesApi
   dotnet run --launch-profile http
   ```
   `http` launch profileが `ASPNETCORE_ENVIRONMENT=Development` と `http://localhost:5245` を設定する。起動後、ブラウザーで `http://localhost:5245/scalar` を開く。停止は起動したターミナルで `Ctrl+C`。
5. ComposeのAPIサービスは既定のProduction環境なので、Scalar UIは公開されない。必要な場合だけ上記の開発環境で起動して確認する。

## Task 9（任意）1 エンドポイントを Controller 方式で書き換え

`--use-controllers` とMinimal の差分（モデルバインディング・フィルター・テスト容易性）を 1 文で言えるように

---

## レビュー手順（毎回）

1. あなた: 「Task N 完了」と投げる
2. 私: 実測検証（build / test / curl / psql）→ PASS 又は 再現手順付きの返却


## メモ

dotnet add <プロジェクト> package <パッケージ> --version <版>：パッケージを追加
dotnet add <プロジェクト> reference <参照先>：プロジェクト参照を追加
dotnet list <プロジェクト> package：追加済みパッケージを確認

podman ps -a --filter name=<コンテナ名>：停止中も含めてコンテナ状態を確認
podman start <コンテナ名>：停止中の既存コンテナを起動
podman exec <コンテナ名> <コマンド>：起動中コンテナ内でコマンドを実行
podman exec dotnet-lab-db pg_isready -U app -d lab：PostgreSQLの接続受付状態を確認
podman exec dotnet-lab-db createdb -U app lab_test：テスト用DBを作成
podman exec dotnet-lab-db psql -U app -d postgres -c '\l'：DB一覧を表示
podman exec dotnet-lab-db psql -U app -d lab -c '\dt'：lab内のテーブル一覧を表示

---

## 実施メモ（Testcontainers 化 / GitHub Actions CI、2026-11-17）

1. `NotesApi.Tests` の DB を手動 `createdb lab_test` 運用から、Testcontainers（`Testcontainers.PostgreSql 4.17.0` + 明示 `Npgsql 10.0.3`）による使い捨て `postgres:17` コンテナへ移行。DB 名は `WithDatabase("ci_test")`。ホスト側ポートは毎回ランダム採番で、5432 を他のローカルコンテナに占有されている環境でも実測で同居できた。
2. 接続文字列はもうコードに直書きしない。`PostgreSqlContainer.StartAsync()` → `GetConnectionString()` を fixture が採番し、`ConfigureTestServices` の 4 RemoveAll（Task 5 通過構成は維持）で DbContext を丸ごと差し替える。誤って dev DB `lab` へ繋がる経路がコード上無くなる。
3. 従来の `Assert.Equal("lab_test", ...)` は、アプリ接続文字列（`db.Database.GetConnectionString()`）と fixture（コンテナ）接続文字列の Host/Port 一致検証に置き換えた。dev DB 誤接続時はこの assert で落ちる。
4. CI を `.github/workflows/test.yml`（checkout / setup-dotnet@v6 / dotnet test の 3 ステップ、service container なし）に追加。GitHub-hosted ubuntu ランナーは docker 同梱のため追加設定は不要（Testcontainers 公式 docs/cicd 準拠）。
5. ローカル実行（podman 5.7 / WSL）: `systemctl --user enable --now podman.socket` 済み。手で叩くときだけ `DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock` を export。`createdb` 手順・`lab_test` 削除運用は不要になった。
6. 実測: 初回 `dotnet test` 12 秒で PASS（イメージ pull + コンテナ起動込み）、2 回目 1 秒台 PASS、2 回実行後も残余コンテナなし。
7. 2026-11-17 実施: remote へ push（main を 077b504..84adb93、3 コミット）し GitHub Actions 初回実測済み。ワークフロー「test」run 44004309677（headSha 84adb93）が conclusion=success。https://github.com/shadowmt/dotnet-lab/actions/runs/44004309677 。CI ランナーは docker 同梱のため `DOCKER_HOST` 不要で、Testcontainers が CI 上でも postgres:17 を起動して統合テストが green になった（ローカルと同コードで通ることを実証）。
8. 2026-11-17 再検証: Task 5 の教訓（ConfigureAppConfiguration 案は Program が使う接続先を上書きできない）は Testcontainers 化後も有効なまま。`ConfigureWebHost` 内の `builder.ConfigureAppConfiguration` で `ConnectionStrings:NotesDb` をコンテナ値へ差し替える変種をスクラッチ probe で実測し、登録済み DbContext の接続文字列は依然 `Database=lab` に解決された（probe は接続文字列文字列の読み取りのみで DB には一切接続していない）。なお Mvc.Testing 10.0.4 では `WebApplicationFactory.ConfigureAppConfiguration(IConfigurationBuilder)` の単一引数オーバーライドは廃止されており、オーバーライドはコンパイル不可。config 系の実験は必ず `ConfigureWebHost` 経由で書くこと。

## Task 10（追加）: k8s + GitOps 実践 — minikube + ArgoCD でアプリ+DB を駆動

### 目的
このマシン（WSL2・docker バイナリ無し・podman 5.7 rootless）で minikube を podman ドライバで動かし、notes-api + PostgreSQL 17 のマニフェストをクラスタ上で駆動、さらに ArgoCD で main への push が自動デプロイされる GitOps の流れまで作る。公式ドキュメントで挙動が未記載の点は、計画に「実測ゲート」として明示的に組み込む（本ラボの実測主義と同じ扱い）。

### 合格条件（実測）
- [ ] Phase 0: `kubectl get nodes` が Ready。podman build したイメージをクラスタへ供給し smoke Pod が Succeeded（①②は実施メモ参照）
- [x] Phase 1: k8s マニフェストで api + postgres が起動。`/healthz/ready` 200、POST/GET ラウンドトリップ、Pod 再作成後もデータ残存（PVC 実証）
- [ ] Phase 2: CI がビルドして ghcr.io へ push（公開パッケージ。手元 `podman pull` が匿名で成功すること）
- [ ] Phase 3: マイグレーションが単発 PreSync Job 化。Job Completed → API Pod Healthy の順が守られる
- [ ] Phase 4: ArgoCD（v3.6.1）で Application が Synced/Healthy（手動 sync）
- [ ] Phase 5: auto-sync 有効化。main へ 1 push → 約 5 分以内にクラスタの Deployment image が新しい sha へ一致＝自動デプロイ完成
- [ ] Phase 6: 公式未記載 3 点（プロファイルのアップグレード挙動／image load の実効性／ingress×podman）の実測結果をこの節に記録

### 採用決定（2026-11-17・すべて推奨案で承認済み）
- 露出: port-forward 先行（ingress アドオンは Phase 4 以降の実測枠）
- DB: Deployment+PVC（k8s 公式の単一 stateful アプリ手順と同形）
- マイグレーション: 同一イメージの `--migrate` 引数単発 Job（ArgoCD フック `PreSync` + `sync-wave: "-1"` + `hook-delete-policy: BeforeHookCreation`）
- タグ供給: CI が kustomize のイメージ行を書き戻して push（`GITHUB_TOKEN`。同トークンの push がワークフローを再発火しないことは GitHub 公式記載＝ループ不成立）
- マニフェスト: 同一リポジトリ `/k8s`（kustomize）。レジストリ: ghcr.io 公開。ArgoCD 操作: Application CR の宣言的 kubectl apply のみ（argocd CLI は導入しない）

### 実施メモ

2026-11-17 Phase 0 実施（PASS。公式未記載だった ①② がここで確定）:
- ① プロファイルのアップグレード: minikube v1.39.0（現行安定）で v1.37.0 作成のプロファイルを `minikube start` すると、そのまま再利用できた（`minikube delete` 不要・ノードコンテナは 6d19h のまま健在）。起動ログに `MINIKUBE_ROOTLESS=true` と出るとおり rootless podman が自動判定され、**パスワードなし sudo／sudoers 変更は一切不要**（`/etc/subuid` の 100000:65536 設定は済んでいた）。`kubectl get nodes` Ready（k8s v1.37.0 / containerd 2.3.4 / 192.168.49.2）。なお `Error downloading kic artifacts: not yet implemented` は既知の无害ログノイズ（kubernetes/minikube#8426）で、base image pull 自体は成功している。ログで決めない教訓（Task 5）の k8s 版。
- ② イメージ供給: `minikube image load` に `DOCKER_HOST` を渡さないと、参照の `localhost/` を**レジストリホスト名として解釈**し `https://localhost/v2/` へタイムアウト到達後に「not found」と化ける（しかも exit 0。失敗の検知は `minikube image ls` の実査で行う）。手順は `systemctl --user start podman`（socket は idle 時 inactive になり得る）→ `DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock minikube image load localhost/<tag>` → `minikube image ls | grep <tag>` で載ったことを確認。smoke Pod は imagePullPolicy=IfNotPresent で Succeeded、logs に hello、imageID は podman build のイメージ ID と一致。**podman build 既定の OCI 形式のまま変換不要**（docs が警戒した `--format docker` は不要と実証）。
- ③ ingress アドオン×podman ドライバは未実測のまま Phase 4 ゲートへ持ち越し（公式に可否の記載なし）。

2026-11-17 Phase 1 実施（PASS）:
- `k8s/base/` に kustomize 一式（namespace/secret/PVC/postgres+api の Deployment/Service）を新設し、namespace=notes へ `kubectl -n notes apply -k k8s/base` で適用。api Service は 80→targetPort 8080（Dockerfile の ASPNETCORE_HTTP_PORTS=8080 と一致）。実測: `/healthz/ready`・`/healthz/live` とも 200（既存 `/health` も 200 のまま）、POST→GET で応答データ確認、`kubectl delete pod -l app=postgres` からの再作成後も POST データが残存し ID シーケンスも続いていた（PVC 永続化の実証）。`dotnet test` は変更後も green。
- Program.cs の追加は MapHealthChecks の 2 エンドポイントのみ。はまった点: `HealthCheckOptions` は `Microsoft.AspNetCore.Diagnostics.HealthChecks` 側の型で、`Microsoft.Extensions.Diagnostics.HealthChecks` だけではビルドエラー（CS0246）。
- postgres は RWO PVC のため `strategy: Recreate`（新 Pod が先に上がってボリュームを掴みにいくのを防ぐ）と PVC の `subPath` マウント（lost+found による PGDATA 汚染回避）が必須だった。
- 判明した残件: API 初回は EF マイグレーション未適用のため DB 参照が 500 になる。この場はホスト側の dotnet ef database update（port-forward 経由）で切り抜けたが、これは恒久手順ではない → Phase 3 の単発 PreSync Job で自動化する（実測で消えるまで Phase 3 完了とみなさない）。
- Secret（notes-api-config）は学習用として平文のまま Git 管理（ファイル内に注記）。ローカル専用クラスタとダミーパスワード前提の割り切り。本番なら ExternalSecrets 等へ。
