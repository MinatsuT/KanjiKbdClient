using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace KanjiKbd {
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window {
        private KeyboardDevice kbd = null;

        /// <summary>
        /// IMEで変換中かのフラグ
        /// </summary>
        private bool _imeFlag = false;

        public MainWindow() {
            InitializeComponent();

            TextCompositionManager.AddPreviewTextInputHandler(TB, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(TB, OnPreviewTextInputStart);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(TB, OnPreviewTextInputUpdate);

            kbd = new KeyboardDevice();
            kbd.KeyboardDeviceFound += OnKeyboardDeviceFound;
            kbd.KeyboardDeviceConnected += OnKeyboardDeviceConnected;

        }

        /// <summary>
        /// キーボードデバイス名を追加
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnKeyboardDeviceFound(object sender, KeyboardDeviceEventArgs e) {
            Console.WriteLine("キーボードデバイス発見: {0}", e.FriendlyName);
            MenuItem m = new MenuItem {
                Header = e.FriendlyName
            };
            KbdDev.Items.Add(m);
            m.Click += KbdDev_Click;
        }

        /// <summary>
        /// キーボードデバイス名をメニューのヘッダに表示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnKeyboardDeviceConnected(object sender, KeyboardDeviceEventArgs e) {
            if (KbdDev.Dispatcher.CheckAccess()) {
                KbdDev.Header = e.FriendlyName;
            } else {
                KbdDev.Dispatcher.Invoke(() => {
                    KbdDev.Header = e.FriendlyName;
                });
            }
        }

        /// <summary>
        /// キーボードデバイスの選択
        /// </summary>
        private void KbdDev_Click(object sender, RoutedEventArgs e) {
            MenuItem menuitem = (MenuItem)sender;
            string friendlyName = menuitem.Header.ToString();
            KbdDev.Header = friendlyName; // COMメニューの文字列を更新
            kbd.Open(friendlyName);
        }

        /// <summary>
        /// KeyUpハンドラ(PrintScreen用)
        /// </summary>
        private void Window_KeyUp(object sender, KeyEventArgs e) {
            Key key = e.Key;
            if (key==Key.Snapshot) {
                Window_PreviewKeyDown(sender, e);
            }
        }

        /// <summary>
        /// 入力されたキーを送信する。
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
            Key key = e.Key;
            Key systemKey = e.SystemKey;
            KeyStates keyStates = e.KeyStates;
            bool isRepeat = e.IsRepeat;

            // IMEの処理中でなければ送信する。
            if (key != Key.ImeProcessed) {
                if (key == Key.A && false) {
                    for (int i = 0; i < 0x86; i++) {
                        if (i >= 0x3a && i <= 0x48) continue;
                        Task t = kbd.SendAsync(KeyCode.MOD_LSHIFT, (byte)i, 20);
                    }
                } else {
                    if (key == Key.System) {
                        key = systemKey;
                    }

                    byte code = (byte)KeyCode.KeyToCode(key);
                    ModifierKeys modKey = Keyboard.Modifiers;
                    byte mod = 0;
                    if ((modKey & ModifierKeys.Alt) != ModifierKeys.None) {
                        mod |= KeyCode.MOD_LALT;
                    }
                    if ((modKey & ModifierKeys.Control) != ModifierKeys.None) {
                        mod |= KeyCode.MOD_LCTRL;
                    }
                    if ((modKey & ModifierKeys.Shift) != ModifierKeys.None) {
                        mod |= KeyCode.MOD_LSHIFT;
                    }
                    if ((modKey & ModifierKeys.Windows) != ModifierKeys.None) {
                        mod |= KeyCode.MOD_LWINDOWS;
                    }
                    Task t = kbd.SendAsync(mod, code, 20);
                }
                e.Handled = true;
            }
        }

        // IME変換中の判定は、こちらのblogを参考にさせて頂きました。
        // https://yone64.wordpress.com/2010/10/25/ime%E3%81%A7%E5%A4%89%E6%8F%9B%E7%8A%B6%E6%85%8B%E4%B8%AD%E3%81%A7%E3%82%82textbox-textchanged%E3%81%8C%E7%99%BA%E7%94%9F%E3%81%99%E3%82%8B/
        private async void TB_TextChanged(object sender, TextChangedEventArgs e) {
            if (_imeFlag) return;
            //IMEで確定した場合のみ、ここに入る
            string kanji = string.Copy(TB.Text); // TBをクリアするので、コピーしておく。
            if (kanji != "") {
                // 文字が入力されていたら、送信する
                await SendKanjiAsync(kanji);
            }
            TB.Clear();
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) => _imeFlag = false;

        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e) => _imeFlag = true;

        private void OnPreviewTextInputUpdate(object sender, TextCompositionEventArgs e) {
            if (e.TextComposition.CompositionText.Length == 0) {
                _imeFlag = false;
            }
        }

        /// <summary>
        /// 漢字を送信する
        /// </summary>
        private async Task SendKanjiAsync(string kanji) {
            int len = kanji.Length;

            // 空文字なら何もしない
            if (len <= 0) {
                return;
            }

            // F11を送信してスマイルツールを起動
            await kbd.SendAsync(Key.F11);
            await Task.Delay(50);

            byte[] codes = new byte[4];

            foreach (char ch in kanji) {
                UInt16 c = Convert.ToUInt16(ch);
                if (c == 0x0d) {
                    // CRは送信しない
                    continue;
                }

                // UTF16コードを、上位から1ニブルずつ16進数の文字(0～F)に変換して送信
                for (int i = 0; i < 4; i++) {
                    string s = Convert.ToString((c >> 12) & 0xf, 16);
                    codes[i] = (byte)KeyCode.CharToCode(s[0]);
                    c <<= 4;
                }
                await kbd.SendAsync(0, codes, 1);
            }

            // 最後に、終了を示すスペースを送信。この時点でスマイルツールが終了する。
            await kbd.SendAsync(Key.Space);

            // スマイルツールが終了するのを待つ。
            //await Task.Delay(200 + (len - 1) * 100);
            await Task.Delay(100);

            // Ctrl+Vを送信して貼り付け
            await kbd.SendAsync(KeyCode.MOD_LCTRL, (byte)KeyCode.KeyToCode(Key.V));
        }

        /// <summary>
        /// 終了
        /// </summary>
        private void MenuitemExit_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        /// <summary>
        /// デバイスを検索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Loaded(object sender, RoutedEventArgs e) {
            Task findKeyboard = kbd.FindAndAttach();
        }

        /// <summary>
        /// デバイスを再検索
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void menuitemReScan_Click(object sender, RoutedEventArgs e) {
            Task findKeyboard = kbd.FindAndAttach();
        }

        /// <summary>
        /// ドロップされたファイルを送信する。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Drop(object sender, DragEventArgs e) {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            Task.Run(() => {
                Dispatcher.Invoke(async () => {
                    if (files != null) {
                        if (files.Length == 1) {
                            await FileSendCommand(files[0]);
                        } else {
                            Topmost = true;
                            MessageBox.Show(this, "複数のファイルを同時に送信することはできません。", this.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                            Topmost = false;
                        }
                    }
                });
            });
        }

        /// <summary>
        /// ファイル送信コマンド
        /// </summary>
        /// <param name="fname"></param>
        /// <returns></returns>
        private async Task FileSendCommand(string fname) {
            // キャンセルトークン生成
            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            // ダイアログ生成
            TransferStatusDialog transDialog = new TransferStatusDialog(tokenSource);
            Progress<TransferStatusInfo> p = new Progress<TransferStatusInfo>(transDialog.OnUpadte);

            // ファイル送信タスクを作成・開始
            var fileSendTask = FileSend(fname, p, token);

            // ダイアログ表示
            Console.WriteLine("MainWindow: ダイアログ表示");
            transDialog.ShowDialog();
            Console.WriteLine("MainWindow: ダイアログ終了");

            //// ファイル転送タスク終了待ち
            var ret = await fileSendTask;
            Console.WriteLine("MainWindow: ファイル転送タスク終了（戻り値={0}）", ret);

            tokenSource.Dispose();

            // 画面が切り替わるのを少し待つ
            await Task.Delay(100);
        }

        /// <summary>
        /// ファイル送信タスク
        /// </summary>
        /// <param name="fname"></param>
        /// <param name="p"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<int> FileSend(string fname, Progress<TransferStatusInfo> p, CancellationToken token) {
            // ファイル転送準備（圧縮）タスクを生成・開始
            var prepareTask = kbd.PrepareSendFileAsync(fname, p, token);

            // F11を送信してスマイルツールを起動
            await kbd.SendAsync(Key.F11);
            Console.WriteLine("スマイルツール起動");

            // 初回の起動に時間がかかる場合があるので、長めに待つ
            await Task.Delay(2000);

            // ファイル転送準備が終わるのを待つ
            var prepareRet = await prepareTask;
            Console.WriteLine("MainWindow: ファイル圧縮タスク終了（戻り値={0}）", prepareRet);

            if (token.IsCancellationRequested) {
                // スペースを送信してSmileToolを終了
                await kbd.SendAsync(Key.Space);
                return -1;
            }

            // "0000"を送信して、ファイル転送モードへ移行
            byte[] codes = new byte[4];
            for (int i = 0; i < 4; i++) codes[i] = (byte)KeyCode.CharToCode('0');
            await kbd.SendAsync(0, codes, 1);

            //// ファイル転送タスクを生成・開始
            Console.WriteLine("MainWindow: ファイル転送スタート");
            var sendSize = await kbd.SendDataAsync(p, token);

            return sendSize;
        }

        /// <summary>
        /// ドラッグされたファイルを受け入れる。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_DragOver(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true)) {
                e.Effects = DragDropEffects.Copy;
            } else {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// クリップボードから貼り付け
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void menuitemPaste_Click(object sender, RoutedEventArgs e) {
            TB.IsEnabled = false;
            await SendKanjiAsync(Clipboard.GetText());
            TB.IsEnabled = true;
        }

        /// <summary>
        /// ファイルオープン
        /// </summary>
        /// <param name="target"></param>
        /// <param name="e"></param>
        private async void OpenCmdExecuted(object target, ExecutedRoutedEventArgs e) {
            var dialog = new OpenFileDialog();
            dialog.Title = "ファイルを開く";
            dialog.Filter = "全てのファイル(*.*)|*.*";
            if (dialog.ShowDialog() == true) {
                await FileSendCommand(dialog.FileName);
            }
        }

    }
}
