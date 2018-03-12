using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
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
        private SerialPort myPort;
        private byte[] buf = new byte[1024];
        private readonly byte XOFF = 0x13; // Ctrl+S
        private readonly byte XON = 0x11; // Ctrl+Q
        private Boolean flowStop = false;

        /// <summary>
        /// IMEで変換中かのフラグ
        /// </summary>
        private bool _imeFlag = false;

        public MainWindow() {
            InitializeComponent();

            TextCompositionManager.AddPreviewTextInputHandler(TB, OnPreviewTextInput);
            TextCompositionManager.AddPreviewTextInputStartHandler(TB, OnPreviewTextInputStart);
            TextCompositionManager.AddPreviewTextInputUpdateHandler(TB, OnPreviewTextInputUpdate);

            // COMポートを列挙し、最後に見つかったポートをオープンする
            string portName = null;
            foreach (var p in SerialPort.GetPortNames()) {
                portName = p;
                MenuItem m = new MenuItem();
                m.Header = p;
                m.Click += Com_Click;
                COM.Items.Add(m);
            }
            if (portName != null && portName != "") {
                COM.Header = String.Format("{0}(_C)", portName);
                openPort(portName);
            }
        }

        // IME変換中の判定は、こちらのblogを参考にさせて頂きました。
        // https://yone64.wordpress.com/2010/10/25/ime%E3%81%A7%E5%A4%89%E6%8F%9B%E7%8A%B6%E6%85%8B%E4%B8%AD%E3%81%A7%E3%82%82textbox-textchanged%E3%81%8C%E7%99%BA%E7%94%9F%E3%81%99%E3%82%8B/
        private void TB_TextChanged(object sender, TextChangedEventArgs e) {
            if (_imeFlag) return;
            //IMEで確定した場合のみ、ここに入る
            string kanji = TB.Text;
            if (kanji != "") {
                // 文字が入力されていたら、送信する
                Task.Run(() => sendKanji(kanji));
            }
            TB.Clear();
            Console.WriteLine(kanji);
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e) {
            _imeFlag = false;
        }

        private void OnPreviewTextInputStart(object sender, TextCompositionEventArgs e) {
            _imeFlag = true;
        }

        private void OnPreviewTextInputUpdate(object sender, TextCompositionEventArgs e) {
            if (e.TextComposition.CompositionText.Length == 0)
                _imeFlag = false;
        }

        private void Com_Click(object sender, RoutedEventArgs e) {
            MenuItem menuitem = (MenuItem)sender; // オブジェクトをMenuItemクラスのインスタンスにキャストする。
            string header = menuitem.Header.ToString(); // Headerプロパティを取り出して、文字列に変換する。
            string tag = menuitem.Tag.ToString(); // Tagプロパティを取り出して、文字列に変換する。
            Console.Out.WriteLine(String.Format("{0},{1}", header, tag));
            COM.Header = String.Format("{0}(_C)", header);
            openPort(header);
        }

        /// <summary>
        /// COMポートのオープン
        /// </summary>
        private void openPort(String port) {
            String PortName = port;
            int BaudRate = 921600;
            Parity Parity = Parity.None;
            int DataBits = 8;
            StopBits StopBits = StopBits.One;

            myPort = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits);
            myPort.Handshake = Handshake.XOnXOff;
            myPort.Open();
            myPort.DataReceived += new SerialDataReceivedEventHandler(Receive);
            Console.Out.WriteLine(String.Format("Open [{0}].", PortName));
        }

        /// <summary>
        /// COMポートからの受信
        /// </summary>
        private void Receive(object sender, System.IO.Ports.SerialDataReceivedEventArgs e) {
            string dat = myPort.ReadExisting();
            //Console.Out.WriteLine(String.Format("[{0}]", dat));
        }

        /// <summary>
        /// キー入力
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
            Key key = e.Key;
            Key systemKey = e.SystemKey;
            KeyStates keyStates = e.KeyStates;
            bool isRepeat = e.IsRepeat;

            // IMEの処理中でなければ、入力されたキーを送信する。
            if (key != Key.ImeProcessed) {
                if (key==Key.System) {
                    key = systemKey;
                }
                sendKey(key);
                e.Handled = true;
            }

            String s = String.Format("  Key=[{0}]\t  KeyStates=[{1}]\t  IsRepeat=[{2}]\t", key, keyStates, isRepeat);
            ModifierKeys modifierKeys = Keyboard.Modifiers;
            if ((modifierKeys & ModifierKeys.Alt) != ModifierKeys.None)
                s += "  Alt ";
            if ((modifierKeys & ModifierKeys.Control) != ModifierKeys.None)
                s += "  Control ";
            if ((modifierKeys & ModifierKeys.Shift) != ModifierKeys.None)
                s += "  Shift ";
            if ((modifierKeys & ModifierKeys.Windows) != ModifierKeys.None)
                s += "  Windows";
            if (key == Key.System)
                s += systemKey;

            s += String.Format(" Len=[{0}] T=[{1}]", TB.GetLineLength(0), TB.GetLineText(0));
            //Console.Out.WriteLine(s);
        }

        /// <summary>
        /// 漢字を送信する
        /// </summary>
        private void sendKanji(string kanji) {
            // F11を送信してスマイルツールを起動
            sendKey(0, KeyCode.getCode(Key.F11));
            //Console.Out.WriteLine(String.Format("Send F11[{0}]",kanji));
            Thread.Sleep(50);
            int wait = 0;

            foreach (char ch in kanji) {
                UInt16 c = Convert.ToUInt16(ch);
                //Console.Out.WriteLine(String.Format("[{0}]", Convert.ToString(c, 16)));

                // UTF16コードを、上位から1ニブルずつ16進数の文字(0～F)に変換して送信
                for (int i = 0; i < 4; i++) {
                    string s = Convert.ToString((c>>12) & 0xf, 16);
                    UInt16 fig = Convert.ToUInt16(s[0]);
                    sendKey(0, (byte)KeyCode.asciiToScanCode[fig - 0x20]);
                    Thread.Sleep(1);
                    c <<= 4;
                }
                wait += 30;
            }

            // 最後に、終了を示すスペースを送信。この時点でスマイルツールが終了する。
            sendKey(0, KeyCode.getCode(Key.Space));
            Thread.Sleep(100+wait);

            // Ctrl+Vを送信して貼り付け
            sendKey(KeyCode.MOD_LCTRL, KeyCode.getCode(Key.V));
        }

        /// <summary>
        /// キーを指定して送信
        /// </summary>
        private void sendKey(Key key) {
            byte code = KeyCode.getCode(key);
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
            sendKey(mod, code);
            //Console.Out.WriteLine(String.Format("Down[{0}] Send[{1}]", key, Convert.ToString(code, 16)));
        }

        /// <summary>
        /// モディファイアとスキャンコードを指定して送信
        /// </summary>
        private void sendKey(byte mod, byte code) {
            if (myPort != null) {
                buf[0] = 0;
                buf[1] = mod;
                buf[2] = code;
                myPort.Write(buf, 0, 3);
            }
        }

        private void menuitemExit_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }
    }
}
