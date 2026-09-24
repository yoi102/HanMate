<p align="center"><img src="docs/assets/hanmate-icon.svg" width="112" height="112" alt="HanMate アイコン" /></p>

# HanMate · 汉语小伴

[简体中文](README.md) · [English](README.en.md) · **日本語**

**[0.1.3 プレビュー版をダウンロード](https://github.com/yoi102/HanMate/releases/tag/v0.1.3)** · [リリースノート](docs/releases/0.1.3.md) · [プライバシーポリシー](PRIVACY.md)

無料・アプリ内課金なし。Windows / Android のプレビュー版を GitHub で公開しています。Microsoft Store と Google Play ではまだ配信していません。

HanMate は **.NET 10 / .NET MAUI** で開発している、オフラインで使える中国語学習アプリです。ピンイン、単語、課文、文法、詩、辞書、読み上げを一つのアプリにまとめています。画面の言語は簡体字中国語・日本語・英語に対応し、切り替えても学習用の中国語本文とピンインは変わりません。

現在の主な開発・検証対象は **Windows と Android** です。プロジェクトには iOS / Mac Catalyst のターゲットも残っていますが、検証済みの提供対象ではありません。

## 主な機能

| タブ | 内容 |
| --- | --- |
| ピンイン | 声母・韻母・整体認読音節・声調を、例字・例語と対応する録音で練習できます。 |
| 学習 | カテゴリ別の単語、課文、文法、詩を、ピンインと収録済みの訳文とともに学べます。 |
| 検索 | ローカル辞書で意味・ピンイン・収録済みの例文を確認できます。手書き入力と辞書項目のお気に入り登録にも対応しています。 |
| お気に入り | フォルダーで教材を整理し、学習タブと同じ詳細画面を開けます。 |
| 設定 | 表示言語、読みやすさ、音声を設定し、教材の管理、インポート・エクスポート、バックアップを行えます。 |

### 学習と読み上げ

- 単語には、日常語、呼び方、あいさつ、数字、重さ、量詞、長さ、調味料、調理器具、生活用品などのカテゴリがあります。数字などは学習しやすい順に並びます。
- 単語をタップすると読み上げ、もう一度タップすると停止します。長押しすると既存の語釈と、収録されている用法・例文を表示します。
- 課文と詩のタイトル、作者情報、本文にピンインを表示し、それぞれタップして読み上げられます。全文の読み上げは別の操作で開始します。
- 文法画面では説明・文型・例文をまとめて確認できます。
- 内容に変更がなければ、お気に入りへ戻った際に一覧の位置を維持します。下部タブの繰り返しタップ、または長押しで、そのタブの最初の画面に戻れます。

個人教材の編集、ピンインの修正、録音、教材の共有にも対応しています。教材データとお気に入りは別々に管理され、お気に入りを解除しても本文や音声は削除されません。

ローカルネットワークで共有するには、単語・課文・文法・詩の詳細画面で「その他 → 共有」を選ぶか、各一覧のメニューから複数の項目、または単語カテゴリーを選びます。送信側が 07 のような 2 桁のルーム番号を入力して作成し、受信側は学習トップの「Wi-Fi で受信」から同じ番号で検索を始めます。ルームが見つかるまで自動で探し、見つかると参加します。ブロードキャストで見つからない場合は送信側に表示されたローカル IP を入力して再検索できます。同じ番号のルームが複数ある場合は送信端末を選びます。送信側は送信先を選び、端末ごとの受信・取り込み状況を確認できます。同じローカルネットワークが必要で、パッケージの上限は 32 MiB です。単語には元のカテゴリー情報が付き、受信側で保存先を選べます。本人の録音と優先録音設定を含めることができます。音声パックのファイルと合成音声は送信されず、受信側の読み上げエンジン設定も変更されません。2 桁の番号は検索用の目印なので、送信前に端末を確認してください。共有が制限された内蔵教材は送信できません。

各教材の一覧では、右上の「その他」に「内容を追加」と「共有する項目を選択」があります。選択モードではページをまたいで最大 100 件を選べ、浮いている共有アイコンから選択項目のルームを開けます。詳細画面から共有する場合は現在の項目だけを送信します。

### 音声

**設定 → 読み上げ音声** で音声を選択できます。

- **MeloTTS**：内蔵の中国語女性音声。端末内で合成します。
- **Kokoro**：内蔵の中国語男性・女性音声。端末内で合成します。
- **システム音声**：端末にインストールされた音声を使用します。利用できる音声は OS によって異なります。
- **既存の録音**：対応する教材では、内容に合った録音を優先します。

オフライン AI 読み上げでは装飾記号を区切りとして処理し、表示・保存されている原文は変更しません。初回はモデルの読み込みに時間がかかる場合があります。発音の品質と速度は端末、文章、選択した音声によって変わります。

## ソースコードの取得

音声モデルと圧縮辞書は **Git LFS** で管理しています。[Git](https://git-scm.com/downloads) と [Git LFS](https://git-lfs.com/) をインストールしてから実行してください。

```powershell
git lfs install
git clone https://github.com/yoi102/HanMate.git
cd HanMate
git lfs pull
git lfs ls-files
```

初回は数百 MB のリソースをダウンロードします。Web サイトから取得したソース ZIP には実ファイルではなく LFS ポインターが含まれる場合があるため、Git での取得を推奨します。モデルが小さなテキストファイルになっている場合は、ビルド前に `git lfs pull` を実行してください。

## 開発環境

- Windows 開発機と [.NET SDK 10.0.401](https://dotnet.microsoft.com/download/dotnet/10.0)。SDK の選択ルールは [global.json](global.json) を参照してください。
- .NET MAUI ワークロード。対応する Visual Studio または .NET CLI を使用します。
- Windows 向けには Windows SDK、Android 向けには Android SDK と JDK が必要です。Visual Studio の MAUI 開発環境から導入できます。
- Python は一部の教材生成・監査ツールで使用します。通常のアプリのビルドには不要です。

```powershell
dotnet --version
dotnet workload install maui
```

NuGet のバージョンは [Directory.Packages.props](Directory.Packages.props) で一元管理し、各プロジェクトにロックファイルを用意しています。初回の依存関係の復元にはネット接続が必要です。内蔵モデルは Git LFS で取得し、通常のビルドでは別途ダウンロードしません。

## ビルドとテスト

リポジトリのルートで実行してください。未検証のプラットフォームをビルドしないよう、対象ごとに復元します。

### Windows

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-windows10.0.19041.0
dotnet build HanMate.App/HanMate.App.csproj -c Release -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 --no-restore
```

Windows 版は非パッケージ形式です。デバッグ時は Visual Studio で `HanMate.App` と Windows ターゲットを選択してください。

### Android

```powershell
dotnet restore HanMate.App/HanMate.App.csproj --locked-mode -p:TargetFrameworks=net10.0-android
dotnet build HanMate.App/HanMate.App.csproj -c Debug -f net10.0-android -p:TargetFrameworks=net10.0-android -p:EmbedAssembliesIntoApk=true --no-restore
```

APK は `HanMate.App/bin/Debug/net10.0-android/` に生成されます。内蔵音声モデルのため容量が大きく、初回のビルドやインストールには時間がかかります。Debug APK は開発用の署名であり、ストア公開用の署名ではありません。

### 自動テスト

```powershell
dotnet test HanMate.Core.Tests/HanMate.Core.Tests.csproj -p:RestoreLockedMode=true
dotnet test HanMate.Infrastructure.Tests/HanMate.Infrastructure.Tests.csproj -p:RestoreLockedMode=true
```

自動テストはコアロジック、ピンイン、音声入力の前処理、お気に入り、教材リソース、データ復旧などを検証します。端末での画面操作、マイク、発音の試聴、アクセシビリティの確認を代替するものではありません。

## ディレクトリ構成

| パス | 内容 |
| --- | --- |
| [HanMate.App](HanMate.App) | MAUI 画面、操作、プラットフォーム対応、音声サービス。 |
| [HanMate.Core](HanMate.Core) | 教材モデル、ピンイン、読解、音声、業務ルール。 |
| [HanMate.Infrastructure](HanMate.Infrastructure) | SQLite、辞書、リソース、お気に入り、入出力、復旧。 |
| [HanMate.Core.Tests](HanMate.Core.Tests) | コアロジックのテスト。 |
| [HanMate.Infrastructure.Tests](HanMate.Infrastructure.Tests) | 永続化、リソース、復旧などのテスト。 |
| [tools](tools/README.md) | 教材生成、リソース監査、開発用検証ツール。 |
| [計画・開発資料](HanMate_Planning_Pack_v3.0/README.md) | 仕様、設計、進捗、検証記録、配布資料。主に中国語で記載しています。 |

## データとプライバシー

本文、お気に入り、録音、設定は端末内に保存します。ローカル AI 合成で読み上げる本文をアップロードすることはありません。追加音声の取得は、ユーザーの操作時に指定された配布元へ接続します。システム音声の動作は端末と音声サービスに依存します。

共有ファイルやバックアップには個人の文章・録音が含まれる場合があります。個人のデータベース、バックアップ、録音、認証情報、署名鍵をリポジトリへ登録しないでください。ビルド成果物やローカル検証ログは、Git の対象外である `artifacts/` などに保存します。

## 開発状況と資料

現在も開発を継続しています。2026-09-21 のお気に入り画面の遷移修正は、関連する自動テスト 16 件、Windows Release ビルド、Android Debug ビルドに合格しました。この修正の端末操作検証は未実施です。ビルドの成功は製品全体の受け入れ完了や正式公開の承認を意味しません。

- [開発状況](HanMate_Planning_Pack_v3.0/tracking/STATUS.md)
- [引き継ぎ](HanMate_Planning_Pack_v3.0/tracking/HANDOFF.md)
- [残作業](HanMate_Planning_Pack_v3.0/tracking/REMAINING_WORK.md)
- [内部配布](HanMate_Planning_Pack_v3.0/delivery/README.md)
- [ユーザーガイドと FAQ](HanMate_Planning_Pack_v3.0/docs/24_User_Guide_and_FAQ.md)

資料には過去の記録も含まれます。日付と検証範囲を確認してください。教材の校閲、発音の試聴、再配布権の確認、正式署名、ストア公開には、それぞれ未完了の作業があります。

## ライセンスと第三者のリソース

HanMate の独自ソースコードとオリジナル文書には [MIT ライセンス](LICENSE)を適用します。第三者のコード、辞書、録音、モデル、フォントおよび派生データには、それぞれのライセンスとクレジット表記の条件が適用されます。出典や許諾が未確認の素材に MIT による再配布許諾を与えるものではありません。[第三者ライセンスの確認記録](docs/THIRD_PARTY_LICENSES.md)を参照してください。

- [音声モデルとランタイム](HanMate.App/Resources/Raw/Voices/NOTICE.txt)
- [ピンイン録音](HanMate.App/Resources/Raw/Pinyin/NOTICE.txt)
- [単語録音](HanMate.App/Resources/Raw/WordAudio/NOTICE.txt)
- [辞書の出典](HanMate.Infrastructure/Dictionary/NOTICE.json)
- [標準辞書の出典と審査状況](HanMate.Infrastructure/Dictionary/XINHUA-NOTICE.json)
- [手書きリソース](HanMate.App/Resources/Raw/Handwriting/NOTICE.json)

元のライセンスは各リソースとともに保存しています。組み込みや自動検証の成功は、内容、発音品質、再配布条件の最終確認が完了したことを意味しません。
