# LEARNING.md — やりたいこと逆引きコマンド集（k8s + ArgoCD 編）

この環境（minikube×podman rootless + ArgoCD + .NET NotesApi）で「これをやりたい」時に叩くコマンドを、逆引きでまとめるリファレンス。ここに載っているコマンドはすべて 2026-11-17 の構築セッションで実際に叩いて成功（または挙動を確認）したものだけ。契約・合格条件の正本は `TASKS.md`。

## 0. アーキテクチャ一眼図

```
git push (main) ──► GitHub Actions CI ──► ghcr.io/shadowmt/notes-api:<sha7>
                          │
                          └► bot が k8s/base/kustomization.yaml の newTag を書き戻し push
                                     │
                    ArgoCD(auto-sync: prune+selfHeal, poll ~3min)
                                     │
                PreSync Job(migrate) 完了 ──► Deployment rollout ──► Service
                                                                  │
ブラウザ/カール ◄── kubectl port-forward（この環境での唯一の安定到達経路）
```

- push から Pod 切り替わりまで **実測 3 分半〜4 分**（CI 約 1 分半＋ArgoCD poll 最大約 3 分＋PreSync 約 20 秒）
- クラスタのどこにも **NodePort/LoadBalancer は実在しない**。`minikube ip`(192.168.49.2) もホストから直接は叩けない（rootless kic の仕様）。アクセスは port-forward
- Compose 環境（`compose.yml`・`:8080`）とは別物。テスト DB（Testcontainers の `ci_test`）とも別物

| 名前 | 種別 | 内容 |
|---|---|---|
| `notes` | Namespace | api(`notes-api`) / postgres(`notes-db` `postgres`) / Ingress(`notes`) |
| `notes-api-config` | Secret | `CONNECTIONSTRINGS__NOTESDB` と `POSTGRES_PASSWORD`（キーは大文字 env 型） |
| `notes-db-data` | PVC | PGDATA は `pgdata` サブディレクトリ（直マウントだと lost+found で初期化失敗） |
| `notes` | ArgoCD Application | `k8s/argocd/application.yaml`。auto-sync（prune+selfHeal）有効 |

## 1. まず全体を見たい（スモーク確認）

| やりたいこと | コマンド | メモ |
|---|---|---|
| クラスタが生きてるか | `minikube status` | `host/kubelet/apiserver: Running` が健康。**DOCKER_HOST export 必須**（下記 2 節） |
| ノード/ノード IP | `kubectl get nodes -o wide` | InternalIP=192.168.49.2。**この IP 直叩きは不可**（仕様） |
| アプリ全体 healthy か | `kubectl -n notes get pods` | `notes-api-*` `postgres-*` が Running、migrate Job は Completed で残り続ける |
| GitOps の同期状態 | `kubectl -n argocd get application notes` | `SYNC=Synced HEALTH=Healthy` が正常着地 |
| API 疎通 | port-forward 後 `curl -s localhost:8080/healthz/ready` | `Healthy` |

## 2. minikube / podman（rootless）

前提の鉄則：`minikube image load` などのイメージ系コマンドは **DOCKER_HOST の export がないと exit 0 で黙って失敗**する（「image not found」）。テスト（Testcontainers）と同じ前置き。

```bash
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
```

| やりたいこと | コマンド | メモ |
|---|---|---|
| クラスタ起動/停止 | `minikube start` / `minikube stop` | rootless でも起動・停止できた（sudoers 不要） |
| カスタムイメージ注入 | `minikube image load localhost/notes-api:local-dev` | 2 節の export 必須。Phase 0 では OCI→Docker 変換なしで成功 |
| ノード上のイメージ確認 | `minikube image ls` | `localhost/...` の供給元キャッシュが見える |
| クラスタコンテナの実体 | `podman ps` | minikube=単一の podman コンテナ（kic）。NAME が minikube- で始まるものを探す |
| ノード側ログ | `minikube logs` | kubelet 等のホスト側ログ。長いので tail を併用（例: 20 行なら `minikube logs` の出力を tail する） |
| addons 一覧/有効化 | `minikube addons list` / `minikube addons enable ingress` | ingress は enabled。`--timeout` は addons enable には存在しない |
| addons 状態詳細 | `minikube addons list` | ingress の状態確認はこれで十分 |

## 3. Pod / ワークロード

| やりたいこと | コマンド | メモ |
|---|---|---|
| Pod をライブ監視 | `kubectl -n notes get pods -w` | rollout 中は `ContainerTerminating`→`ContainerCreating`→`Running` の順 |
| ログ/前回クラッシュのログ | `kubectl -n notes logs deploy/notes-api` / `--previous` | 直前クラッシュは `--previous` |
| Pod 詳細（イベント含む） | `kubectl -n notes describe pod <pod>` | ImagePullBackOff の理由もここ |
| Namespace イベント系列 | `kubectl -n notes get events --sort-by=.lastTimestamp` | 何を待たされたか特定 |
| Pod を落としてデータ残存確認 | `kubectl -n notes delete pod -l app=postgres` | PVC の効き確認。`strategy: Recreate` なので即再取得 |
| デプロイ完了待ち | `kubectl -n notes rollout status deploy/notes-api --timeout=150s` | 循環待ちの定番 |
| ロールアウト履歴 | `kubectl -n notes rollout history deploy/notes-api` | ReplicaSet が履歴の実体 |
| ロールバック | `kubectl -n notes rollout undo deploy/notes-api` | 旧 ReplicaSet を保持している間だけ可（既定 10 世代） |
| 手動再起動 | `kubectl -n notes rollout restart deploy/notes-api` | Git の tag 書き換えと勘ぐられるので学習では避ける（selfHeal は変えない） |
| Job 完了判定 | `kubectl -n notes wait --for=condition=complete job/migrate` | **Pod に対する `wait --for=condition=Ready pod --all` は完了済み Job Pod で永久 timeout**。Job は `--for=condition=complete job/...` |
| Pod リソース使用量 | `kubectl -n notes top pods` | metrics-server 内蔵 addon |

## 4. ArgoCD（ダッシュボードの見方＋CLI 代用）

### ダッシュボード

```bash
kubectl -n argocd port-forward svc/argocd-server 8443:443
# → https://localhost:8443（自己署名なので警告を無視して進む）
# ユーザー: admin
# パスワード: kubectl -n argocd get secret argocd-initial-admin-secret -o jsonpath='{.data.password}' | base64 -d
```

見たことのある画面の見方：

| 画面要素 | 読み方 |
|---|---|
| アプリカードの `notes` | SYNC STATUS＝Git とクラスタの実物の一致、HEALTH STATUS＝リソースの健全性、OPERATION＝最後の同期の実行結果 |
| `OutOfSync` | Git の宣言と実物がズレた。**クラスタ側を直したつもりでも直さない**。Git を直す |
| `Synced` / `Healthy` | 正常着地。この 2 語が揃えば PASS 判定可 |
| `Succeeded` | 同期**操作**のフェーズ。`SYNC STATUS=Synced` とは**別の列**（`Succeeded` だけ見て安心する罠） |
| リソースツリー | `PRESYNC` フック Job（migrate）→ Deployment→ReplicaSet→Pod の順。**古い ReplicaSet もツリーに出る**（DESIRED=0＝ロールバック用履歴。Git 管理外） |
| Manifest タブ | そのリソースの Git 由来 YAML と conditions が見られる |
| Events タブ / Logs タブ | Pod 内のイベントと各コンテナのログ |
| HARD REFRESH | Git を見に行って diff 再計算（自動 poll は約 3 分おき） |

port-forward は websocket と相性が悪く UI が時々切れる。切れても再実行で復帰。

### ターミナル側（CLI 未インストール、kubectl で全部できる）

```bash
# 4 項目をライブで見る（デプロイ観察の本命）
kubectl -n argocd get application notes -w
# 1 行サマリ（監視スクリプトの定番）
kubectl -n argocd get application notes -o jsonpath='{.status.sync.status}/{.status.health.status} op={.status.operationState.phase} rev={.status.sync.revision}{"\n"}'
# Application CR 自体と同期ポリシー
kubectl -n argocd get application notes -o yaml | head -40
```

- auto-sync は Git の `syncPolicy.automated` が命。**ArgoCD は Application CR 自身を自己管理しない**ので、ポリシー変更後は `kubectl -n argocd apply -f k8s/argocd/application.yaml` が必要
- PreSync Job は v3.6.1 では**名前のサフィックスが変わらず同名 replace**（`hook-delete-policy: BeforeHookCreation`）
- selfHeal 有効中：**kubectl での手元直列は約 3 分で Git 状態に戻される**。検証で触ったら必ず戻す

## 5. CI（GitHub Actions）

| やりたいこと | コマンド | メモ |
|---|---|---|
| 直近の run 一覧 | `gh run list --limit 5` | push ごとに `test`→`build-push`→`update-tag` |
| 最新 run を張り付き | `gh run watch` | 約 1 分半 |
| 失敗ログを見る | `gh run view --log-failed <run_id>` | |
| bot の書き戻しコミット確認 | `git fetch && git log --oneline origin/main -3` | `ci: update notes-api image tag to <sha7> (auto) [skip ci]` |
| bot コミットが CI を再発火しない確認 | `gh run list --limit 3` で bot sha の run が無いこと | `GITHUB_TOKEN` 由来 push は再発火なし（ループ不成立の実測根拠） |

注意：**docs コミットも同じ循環を回す**（docs の sha でもイメージ build→tag 書き戻し→実デプロイ）。内容に差は無く、Rollout は「実物」に見える。異常ではない。

## 6. イメージ（ghcr）

| やりたいこと | コマンド | メモ |
|---|---|---|
| 匿名 pull できるか（公開確認） | `podman pull ghcr.io/shadowmt/notes-api:<sha7>` | リポジトリは public。Phase 2 で実証済み |
| ノードキャッシュに在るか | `minikube image ls` | 出力から notes-api を grep して確認。`imagePullPolicy: IfNotPresent` と供給経路の合わせ技 |
| tag の一覧 | https://github.com/shadowmt/dotnet-lab/pkgs/container/notes-api | Actions の書き戻し sha がそのまま tag 名 |

## 7. API にアクセスする／叩く

```bash
# 本命：Service 直（Host 変換不要、デプロイで切れる→ループで自動再接続）
while true; do kubectl -n notes port-forward svc/notes-api 8080:80 >/dev/null 2>&1; sleep 1; done
```

| 用途 | コマンド |
|---|---|
| 生存確認 | `curl -s localhost:8080/healthz/ready`（`Healthy`） |
| Scalar UI | ブラウザで http://localhost:8080/scalar （Development 稼働中のみ。302→/scalar/） |
| OpenAPI | `curl -s localhost:8080/openapi/v1.json`（head で先頭だけ） |
| 一覧取得 | `curl -s localhost:8080/notes`（jq で整形） |
| 1 件登録 | `curl -s -X POST localhost:8080/notes -H 'content-type: application/json' -d '{"station":"p8","body":"hello","severity":2}'` |
| 1 件取得 | `curl -s localhost:8080/notes/<id>` |

- POST の必須フィールドは `station` と `body`（任意 `severity`）。`text` は 400 になる
- **`kubectl port-forward` は起動時に裏の Pod を 1 本に張り付ける**。デプロイで Pod が死ぬと切断される（既知の half-close 問題もあり）→ 上記のループが定番回避
- `http://localhost/scalar` と打つとポート 80 に行き、**クラスタ外の Windows 系サービス**（wslrelay 中継）が 404 HTML を返す。pf は必ずポート指定付きで叩く
- ingress 経路（参考・実用非推奨）：pf 先 `svc/ingress-nginx-controller 18098:80`、**`Host: notes.localhost` ヘッダ必須**（無しは 404）。rootless ではデータプレーンが断続的に死ぬためブラウザ運用にはしない（Phase 6 記録）
- クラスタは Development 稼働（意図的・判断根拠は TASKS.md）。compose は Production のまま（`/scalar` は 404 が正しい）

## 8. データベース（クラスタ内 postgres）

```bash
# クエリ実行
kubectl -n notes exec deploy/postgres -- psql -U notes -d notesdb -c "SELECT id, station, body, severity FROM notes ORDER BY id DESC LIMIT 5;"
# 接続情報（パスワードは secret 内。表示したら必ず伏せる）
kubectl -n notes get secret notes-api-config -o jsonpath='{.data.CONNECTIONSTRINGS__NOTESDB}' | base64 -d | sed 's/Password=[^;]*/Password=***/'
# PVC
kubectl -n notes get pvc notes-db-data
```

- DB 名=`notesdb`、ユーザー=`notes`（`k8s/base/postgres-deployment.yaml` の env と secret `POSTGRES_PASSWORD`）
- dev（`podman exec dotnet-lab-db psql -U app -d lab ...`）・テスト（Testcontainers `ci_test`）と**この DB は全部別**。混同するとデータを探し間違える

## 9. GitOps 運用（このリポジトリの push 手順）

```bash
git add -A && git commit -m "feat: ..."     # ← 注意 2 あり
git pull --rebase origin main               # ← 注意 3 あり（bot と競合するため毎回やる）
git push origin main
```

1. 変更は Git 経由だけ。手元 `kubectl` 直編集は selfHeal に負ける
2. commit message は**バッククォート禁止**（フックで弾かれる）+ `check-chinese` フックが簡体字混入を弾く（日本語の新字体と旧字体、紛らわしい漢字。弾かれたら指摘された行の字体だけ日本語表記に直して再 commit）
3. 循環で bot が先に進むので push は `pull --rebase` が定番。衝突した経験はない（bot は k8s/base の kustomization.yaml だけ触る）
4. `k8s/base/kustomization.yaml` の `images.newTag` は CI の専有物。**手で触らない**
5. 循環を張って見守るなら（監視スクリプトの最小例）：

```bash
MY=$(git rev-parse --short HEAD)   # push した sha
while :; do
  IMG=$(kubectl -n notes get deploy notes-api -o jsonpath='{.spec.template.spec.containers[0].image}')
  echo "$(date +%T) ${IMG##*:}"; [[ "$IMG" == *:"$MY" ]] && break; sleep 12
done
kubectl -n notes rollout status deploy/notes-api --timeout=150s
```

## 10. 「あれ？」集（実測済み症状 → 正体 → 対処）

| 症状 | 正体 | 対処 |
|---|---|---|
| デプロイ中に port-forward 切断 | kubectl pf は特定 Pod に張り付き | 再接続ループ（7 節）。恒久はクラスタ再作成＋`--ports` か rootful/docker（PVC 失う、未実施） |
| `minikube image load` が exit 0 で効かない | DOCKER_HOST 未 export | export してから再実行（2 節） |
| `wait --for=condition=Ready pod --all` が永久待ち | 完了済み Job Pod は Ready=False のまま | `complete job/...` にする（3 節） |
| ingress なのにデプロイ後に API が 404/ハング | rootless×ingress-nginx の worker 断死（Phase 6 記録） | API は Service 直 pf を使う |
| `Synced` なのに `Succeeded` と別物が欲しくなる | enum 混同 | `sync.status` は Synced、`Succeeded` は `operationState.phase` |
| `kubectl` で直した設定が戻る | selfHeal | Git を直す |
| ログに EF の `fail: ... 20102 __EFMigrationsHistory` | 履歴テーブル未作成時の初回プローブ | 直後に `Applying migration` があれば無害（ログで判断せず DB で確認） |
| ポート 80 がなんか返す | WSL の :80 を Windows 側サービスが握っている |クラスタとは無関係。pf のポートを指定する |
| `pkill -f` で自分が死んでハーネスが壊れる | bash -c ウラッパの cmdline にパターンが一致 | 掃除は起動スクリプト内の `kill $!`（明示 PID）だけ |

## 付録：この環境のバージョンと名前

- minikube v1.39.0（driver=podman、rootless）/ Kubernetes ノード v1.37.0 / ArgoCD v3.6.1（StatefulSet: `quay.io/argoproj/argocd:v3.6.1`）/ podman 5.7.0 / .NET SDK 10.0.404 / postgres:17 / ingress-nginx v1.15.1（addon・不安定記録は Phase 6）
- `docker` バイナリは無い。podman のみ。`sudo` 不要
- イメージ: `ghcr.io/shadowmt/notes-api:<sha7>`（public、CI による tag 書き戻しが tag 管理の唯一の経路）
- ソフトウェアの契約（DTO・検証規則）と合格条件の履歴＝実施メモは `TASKS.md`
