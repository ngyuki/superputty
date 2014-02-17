# SuperPutty

## 概要

SuperPutty は PuTTY をタブ化するためのアプリケーションです。Google Code で公開されています。

 - http://code.google.com/p/superputty/

このリポジトリでは、オリジナルの SuperPutty に次の修正を施したものを公開しています。

 - mintty のタブを閉じたときに mintty プロセスが終了しない問題を修正
 - シングルインスタンスモードが有効でもアプリケーションが多重起動することがある問題を修正
 - コマンドラインオプションに `-mintty` を追加
 - mintty のデフォルトのシェルに `/bin/bash -l` を指定
 - アプリケーションが最小化しているときに新しいセッションを開始するとウィンドウが復元するように変更
 - コマンドラインから mintty セッションを開始したときのタブ名を変更

## ダウンロード

 - [Releases](https://github.com/ngyuki/superputty/releases)

## ライセンス

すべてのパッチにはオリジナルの SuperPutty と同じ [MIT License](http://www.opensource.org/licenses/mit-license.php) が適用されます。

## Original README.txt

This is the README for the SuperPutty Application

For License information please read the License.txt included with the download

For issue tracking, documentation and downloads please visit the Google Code Project
http://code.google.com/p/superputty/
