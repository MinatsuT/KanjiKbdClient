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
            Console.WriteLine("{0}を見つけた。", e.FriendlyName);
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
            Console.WriteLine("{0}に接続した。", e.FriendlyName);
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
        /// 入力されたキーを送信する。
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
            Key key = e.Key;
            Key systemKey = e.SystemKey;
            KeyStates keyStates = e.KeyStates;
            bool isRepeat = e.IsRepeat;

            // IMEの処理中でなければ送信する。
            if (key != Key.ImeProcessed) {
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
                Task t = kbd.SendAsync(mod, code);
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
            if (len <= 0) {
                return;
            }

            // F11を送信してスマイルツールを起動
            await kbd.SendAsync(Key.F11);
            await Task.Delay(50);

            foreach (char ch in kanji) {
                UInt16 c = Convert.ToUInt16(ch);

                // UTF16コードを、上位から1ニブルずつ16進数の文字(0～F)に変換して送信
                for (int i = 0; i < 4; i++) {
                    string s = Convert.ToString((c >> 12) & 0xf, 16);
                    await kbd.SendAsync(s[0]);
                    c <<= 4;
                }
                await Task.Delay(5);
            }

            // 最後に、終了を示すスペースを送信。この時点でスマイルツールが終了する。
            await kbd.SendAsync(Key.Space);
            //await Task.Delay(100 + (len - 1) * 30);
            await Task.Delay(200 + (len - 1) * 100);

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
        /// デバイスを探索し、１秒待って、最後に見つかったデバイスに自動接続する。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            Task findKeyboard = kbd.FindKeyboardDeviceAsync();
            Console.WriteLine("デバイスさがすーヾ(*´∀｀*)ﾉ");
            await Task.Delay(1000);
            Console.WriteLine("デバイスに接続する～(ﾟ∀ﾟ)");
            kbd.OpenLatest();
            await findKeyboard;
            Console.WriteLine("デバイスさがすのしゅーりょー(´･ω･`)");
        }

        private async void TB_Drop(object sender, DragEventArgs e) {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) {
                foreach (var fname in files) {
                    await kbd.SendFileAsync(fname);
                }
            }
        }

        /// <summary>
        /// ドラッグされたファイルを送信する。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_PreviewDragOver(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true)) {
                e.Effects = DragDropEffects.Copy;
            } else {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
    }
}
