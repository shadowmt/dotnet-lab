# NotesApi（Web プロジェクト）

ASP.NET Core 10 Minimal API のアプリ本体。コントローラ・レイヤ分割なし。source はこの直下にフラット配置。

## STRUCTURE
```
NotesApi/
├── Program.cs            # DI 登録（8-14 行）+ 全 5 endpoint + pipeline（top-level statements, 86 行）
├── INoteService.cs       # Service 抽象（4 メソッド、全て CancellationToken 末尾）
├── NoteService.cs        # EF 実装。読み取りは AsNoTracking、Patch のみトラッキング更新
├── Note.cs / NotesDbContext.cs
├── CreateNoteRequest.cs / PatchNoteRequest.cs / NoteResponse.cs   # record DTO + DataAnnotations
├── Migrations/           # InitialCreate 1 本 + Snapshot。dotnet ef 生成物・手編集禁止
├── Dockerfile            # sdk:10.0 build → aspnet:10.0 run。ポート 8080
├── NotesApi.http         # 全 operation + 負ケース 3 件（VS Code REST Client 形式）
└── NotesApi.slnx         # 2 プロジェクト（本体 + ../NotesApi.Tests）を含む唯一のソリューション
```
`.vs/` と `NotesApi.csproj.user` は VS 生成物（gitignore 済み）。触らない。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| endpoint 追加・変更 | `Program.cs:26-82` | `app.MapXxx` の lambda 直呼び。Service 経由が確定形 |
| クエリパラメータ検証 | `Program.cs:28-49`（GET /notes） | DTO 検証が効かない skip/take はハンドラ内手動 → `Results.ValidationProblem` |
| 空 PATCH 防止 | `Program.cs:69-75` | 全フィールド null なら Service を呼ばず 400 |
| 検証属性の調整 | `CreateNoteRequest.cs` | station 1-40 / type ≤20 / body 1-500 / severity Range(1,5) |
| Nuget 追加 | csproj | 常に `--version 10.x.x` 付き（省略時 11 系が選ばれ失敗した実績） |

## CONVENTIONS
- DI 登録順・内容は `Program.cs:8-14` の 4 種のみ（AddDbContext/UseNpgsql, AddValidation, AddScoped<INoteService,NoteService>, AddProblemDetails, AddOpenApi）。追加は理由を書いてから。
- パイプラインは `app.UseStatusCodePages()` のみ。`UseHttpsRedirection` は学習判断で意図的になし（ルートの ANTI-PATTERNS 参照）。
- 書き込み系（CreateAsync/PatchAsync）はエンティティを `db.Add`/トラッキング更新 → `SaveChangesAsync(cancellationToken)` → 戻り値で `NoteResponse` を明示構築。DTO への写像は Service 側で行う（endpoint ではない）。
- `CreatedAt` は Service 側 `DateTime.UtcNow` で採番（DB デフォルトではない）。
- マイグレーションコマンドは `NotesApi/` ディレクトリで実行する運用（`TASKS.md` の手順もこの形）。

## ANTI-PATTERNS
- `public partial class Program { }`（`Program.cs:86`）を消すな。削除すると `NotesApiFactory : WebApplicationFactory<Program>` がビルドできない。
- DTO record の引数名を PascalCase に勝手にリネームするな（JSON キーが変わりテスト・.http・TASKS.md 契約が壊れる）。
- `appsettings.json`（本番側）に接続文字列を足すな。dev は Development 側、compose は env 注入のみ。
