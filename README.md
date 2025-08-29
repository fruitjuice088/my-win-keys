# MyWinKeys

Windows向けのキーボードリマッピングツール。
キーの単押しと長押し（Tap-Hold）の使い分け、複数キーの同時押し（コンボ）による入力、単純なキーの置き換えなど、自作キーボード的なカスタマイズ機能を提供する。

## 主な機能

### 1. 単純なキー置き換え

- `CapsLock` → `半角/全角` (IME切り替え)
- `無変換` → `Backspace`
- `変換` → `Enter`

### 2. Tap-Hold機能

- **`Space`キー**
  - **Tap (単押し):** 通常の `Space`
  - **Hold (長押し):** `Shift` として機能。`Space`キーを押しながら他のキーを入力すると、`Shift`+そのキーとして入力される。
- **`D`キー**
  - **Tap:** `d`
  - **Hold:** `左Ctrl`
- **`K`キー**
  - **Tap:** `k`
  - **Hold:** `右Ctrl`
- **`Tab`キー**
  - **Tap:** `Tab`
  - **Hold:** 矢印キーレイヤー。`Tab`を押しながら以下のキーを押すと、矢印キーとして機能する (リピートに対応)。
    - `H`: `←` (Left)
    - `J`: `↓` (Down)
    - `K`: `↑` (Up)
    - `L`: `→` (Right)

### 3. キーコンボ機能 (複数キーの同時押し)

- `J` + `K` → `Esc`
- `W` + `E` → `Q`
- `I` + `O` → `P`
- `,` + `.` → `_`
- `D` + `C` → `P`
- `K` + `M` → `P`
- `A` + `Z` → `"exit"`という文字列を入力
- `1` + `2` → アクティブウィンドウの左上にカーソルを移動
- `3` + `4` → アクティブウィンドウの右上にカーソルを移動
- `1` + `3` → アクティブウィンドウの左下にカーソルを移動
- `2` + `4` → アクティブウィンドウの左下にカーソルを移動
- `2` + `3` → アクティブウィンドウのタイトルバー中央にカーソルを移動
- `1` + `4` → アクティブウィンドウの中央にカーソルを移動

## 使い方

`MyWinKeys.exe`を実行するとタスクトレイに常駐する。トレイアイコンの右クリックメニューから以下を操作できる。

- 設定フォルダを開く（`appsettings.json` があるフォルダ）
- アプリの終了

## 設定

`appsettings.json`で主にタイミングを調整。

```json
{
  "Debug": false,
  "HoldThresholdMs": 175,
  "TapGraceMs": 150,
  "ComboWindowMs": 50
}
```

- `Debug`: `true`にすると、デバッグ用のログファイルが生成される。
- `HoldThresholdMs`: キーを「長押し(Hold)」と判定するまでの時間（ミリ秒）。
- `TapGraceMs`: Tap-Holdキーと他のキーの組み合わせを認識する猶予時間（ミリ秒）。
- `ComboWindowMs`: キーコンボを認識する猶予時間（ミリ秒）。

## 開発

### 必要環境

- .NET SDK

### ビルド

プロジェクトのルートディレクトリで以下のコマンドを実行。

```powershell
dotnet build -c Debug
```

#### 単一 exe 配布

配布は「実行ファイル（EXE）+ appsettings.json」のみで可能。

```powershell
# 例: フレームワーク依存の単一ファイル
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:DebugType=none /p:DebugSymbols=false
```

出力（publish フォルダ）

- `MyWinKeys.exe`（単一ファイル）
- `appsettings.json`（設定ファイル）

この2ファイルを配布すれば動作する。

### GitHub Actions でのリリース配布

タグ作成（例: v0.1.0）をプッシュすると、自動で以下2ファイルが Release にアップロードされる。

- MyWinKeys.exe
- appsettings.json
