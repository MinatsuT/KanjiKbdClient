using BitStreams;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KanjiKbd {
    public class KeyboardDeviceEventArgs : EventArgs {
        public string FriendlyName { get; }

        public KeyboardDeviceEventArgs(string FriendlyName) : base() {
            this.FriendlyName = FriendlyName;
        }
    }

    public class NoKeyboardDeviceException : Exception {
        public NoKeyboardDeviceException() {
        }

        public NoKeyboardDeviceException(string message)
            : base(message) {
        }

        public NoKeyboardDeviceException(string message, Exception inner)
            : base(message, inner) {
        }
    }

    public class TransferStatusInfo {
        public string Mode { get; internal set; }
        public string Fname { get; internal set; }
        public int Min { get; internal set; }
        public int Max { get; internal set; }
        public int Value { get; internal set; }
        public bool finished { get; internal set; } = false;
    }

    class KeyboardDevice {
        private class KeyboardDeviceInfo {
            public enum KeyboardDeviceType { COM, SERVER };

            public string FriendlyName { get; }

            public string DeviceName { get; }

            public KeyboardDeviceType DeviceType { get; }

            public bool IsCom => DeviceType == KeyboardDeviceType.COM ? true : false;

            public bool IsServer => DeviceType == KeyboardDeviceType.SERVER ? true : false;

            public KeyboardDeviceInfo(string friendlyName, string deviceName, KeyboardDeviceType deviceType) {
                this.FriendlyName = friendlyName ?? throw new ArgumentNullException(nameof(friendlyName));
                this.DeviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
                this.DeviceType = deviceType;
            }
        }

        private int serverPort = 3720;

        private List<KeyboardDeviceInfo> devices = new List<KeyboardDeviceInfo>();
        private SerialPort myPort = null;
        private TcpClient myClient = null;

        private readonly byte XOFF = 0x13; //Ctrl+S
        private readonly byte XON = 0x11; //Ctrl+Q
        private bool COMSendEnable = true;

        /// <summary>
        /// COMポートからの受信バッファ
        /// </summary>
        private string COMbuf = "";

        /// <summary>
        /// HIDレポートパケット(モードバイト+mod+code*6)
        /// </summary>
        private byte[] pkt = new byte[8];

        /// <summary>
        /// ファイル送信中フラグ
        /// </summary>
        public bool FileSending { get; internal set; } = false;

        /// <summary>
        /// 転送ステータス
        /// </summary>
        private TransferStatusInfo transInfo = new TransferStatusInfo();

        /// <summary>
        /// 送信ファイル名
        /// </summary>
        private string fileName;

        /// <summary>
        /// 送信ファイル種別
        /// </summary>
        private string fileType;

        /// <summary>
        /// ファイル送信データ
        /// </summary>
        private byte[] fileData;

        /// <summary>
        /// 実データサイズ
        /// </summary>
        private int actualFileSize;

        /// <summary>
        /// 圧縮データサイズ
        /// </summary>
        private int compFileSize;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public KeyboardDevice() {
        }

        /*****************************************************************
         * イベント
         *****************************************************************/
        /// <summary>
        /// キーボードデバイスデリゲート
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public delegate void KeyboardDeviceEventHandler(object sender, KeyboardDeviceEventArgs e);

        /// <summary>
        /// キーボードデバイス発見イベントハンドラ
        /// </summary>
        public event KeyboardDeviceEventHandler KeyboardDeviceFound;

        /// <summary>
        /// キーボードデバイス発見イベント
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnKeyboardDeviceFound(KeyboardDeviceEventArgs e) => KeyboardDeviceFound?.Invoke(this, e);

        /// <summary>
        /// キーボードデバイス接続イベントハンドラ
        /// </summary>
        public event KeyboardDeviceEventHandler KeyboardDeviceConnected;

        /// <summary>
        /// キーボードデバイス接続イベント
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnKeyboardDeviceConnected(KeyboardDeviceEventArgs e) => KeyboardDeviceConnected?.Invoke(this, e);


        /*****************************************************************
         * デバイス
         *****************************************************************/
        /// <summary>
        /// キーボードデバイスを探索する。
        /// </summary>
        public async Task FindKeyboardDeviceAsync() {
            Console.WriteLine("キーボードを探す！");
            FindCOMPort();
            await FindKeyboardServerAsync();
        }

        /// <summary>
        /// 最後に追加されたデバイスをオープンする。
        /// </summary>
        public void OpenLatest() {
            Console.WriteLine("OpenLatestが呼ばれた！");
            Open(devices.Last().FriendlyName);
        }

        /// <summary>
        /// キーボードデバイス名を指定してオープンする
        /// </summary>
        /// <param name="friendlyName">キーボードデバイス名</param>
        public void Open(string friendlyName) {
            KeyboardDeviceInfo kdi = devices.Find(x => x.FriendlyName == friendlyName) ?? throw new NoKeyboardDeviceException();

            // サーバを閉じる
            myClient?.Close();
            myClient = null;

            // COMポートを閉じる
            myPort?.Close();
            myPort = null;

            switch (kdi.DeviceType) {
                case KeyboardDeviceInfo.KeyboardDeviceType.COM:
                    OpenCOMPort(kdi);
                    break;
                case KeyboardDeviceInfo.KeyboardDeviceType.SERVER:
                    OpenServer(kdi);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 発見したキーボードデバイスを追加する。
        /// </summary>
        /// <param name="deviceName">キーボードデバイス名</param>
        private void AddDevice(KeyboardDeviceInfo kdi) {
            if (devices.Exists(x => x.FriendlyName == kdi.FriendlyName)) {
                return;
            }
            devices.Add(kdi);
            OnKeyboardDeviceFound(new KeyboardDeviceEventArgs(kdi.FriendlyName));
        }

        /// <summary>
        /// COMポートを列挙する。
        /// </summary>
        private void FindCOMPort() {
            foreach (var portName in SerialPort.GetPortNames()) {
                AddDevice(new KeyboardDeviceInfo(portName, portName, KeyboardDeviceInfo.KeyboardDeviceType.COM));
            }
        }

        /// <summary>
        /// COMポートのオープン
        /// </summary>
        private void OpenCOMPort(KeyboardDeviceInfo kdi) {
            int BaudRate = 921600;
            Parity Parity = Parity.None;
            int DataBits = 8;
            StopBits StopBits = StopBits.One;

            myPort = new SerialPort(kdi.DeviceName, BaudRate, Parity, DataBits, StopBits) {
                Handshake = Handshake.XOnXOff
            };
            myPort.Open();
            myPort.DataReceived += new SerialDataReceivedEventHandler(ReceiveCOMPort);
            OnKeyboardDeviceConnected(new KeyboardDeviceEventArgs(kdi.FriendlyName));

            COMSendEnable = true;
            Console.Out.WriteLine(String.Format("Open [{0}].", kdi.DeviceName));
        }

        /// <summary>
        /// COMポートからの受信
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReceiveCOMPort(object sender, System.IO.Ports.SerialDataReceivedEventArgs e) {
            string s = "";
            foreach (char c in myPort.ReadExisting()) {
                if (c == XOFF) {
                    COMSendEnable = false;
                    //Console.WriteLine("XOFF");
                } else if (c == XON) {
                    COMSendEnable = true;
                    //Console.WriteLine("XON");
                } else {
                    s += c;
                }
            }

            COMbuf += s;
            var delCount = 0;
            var len = COMbuf.Length;
            if (len > 0 && COMbuf[len - 1] == '\n') {
                delCount++;
                if (len > 1 && COMbuf[len - 2] == '\r') {
                    delCount++;
                }
                Console.Out.WriteLine(String.Format("COM received:[{0}]", COMbuf.Remove(len - delCount)));
                COMbuf = "";
            }
        }


        /// <summary>
        /// キーボードサーバを探す  
        /// </summary>
        private async Task FindKeyboardServerAsync() {
            // UDPクライアントを作成
            var local = new IPEndPoint(IPAddress.Any, 0);
            var client = new UdpClient(local) {
                EnableBroadcast = true
            };

            // メッセージ受信開始
            Task listenMessage = ListenMessageAsync(client);

            // 全てのブロードキャストアドレスにメッセージを送信
            var buf = Encoding.ASCII.GetBytes("");
            foreach (IPAddress broadcast in GetAllBroadcastAddresses()) {
                await client.SendAsync(buf, buf.Length, new IPEndPoint(broadcast, this.serverPort));
            }

            // メッセージ受信タスクの終了待ち
            // ５秒間だけ待ってやる！
            await Task.Delay(5000);
            client.Close();
            await listenMessage;
            Console.WriteLine("UDP受信タスクが終了したはず。");
        }

        /// <summary>
        /// ブロードキャストアドレスを列挙する。
        /// </summary>
        /// <returns></returns>
        private List<IPAddress> GetAllBroadcastAddresses() {
            List<IPAddress> broadcasts = new List<IPAddress>();

            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces()) {
                if (netInterface.OperationalStatus != OperationalStatus.Up) {
                    continue;
                }

                //Console.WriteLine("Name: " + netInterface.Name);
                IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                IPAddress nullAddress = new IPAddress(0);

                uint multi = BitConverter.ToUInt32(IPAddress.Parse("169.254.0.0").GetAddressBytes(), 0);

                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses) {
                    if (!IPAddress.IsLoopback(addr.Address) && !addr.IPv4Mask.Equals(nullAddress)) {
                        uint adr = BitConverter.ToUInt32(addr.Address.GetAddressBytes(), 0);
                        uint msk = BitConverter.ToUInt32(addr.IPv4Mask.GetAddressBytes(), 0);
                        uint net = adr & msk;
                        if (net != multi) {
                            IPAddress broadcast = new IPAddress(adr | ~msk);
                            Console.WriteLine("{0}/{1} {2}", addr.Address.ToString(), addr.IPv4Mask.ToString(), broadcast.ToString());
                            broadcasts.Add(broadcast);
                        }
                    }
                }
            }

            Console.WriteLine("");
            return broadcasts;
        }

        /// <summary>
        /// キーボードサーバからの応答を受信し、キーボードサーバ情報を追加する。
        /// </summary>
        /// <param name="client"></param>
        private async Task ListenMessageAsync(UdpClient client) {
            try {
                while (true) {
                    // データ受信待機
                    var result = await client.ReceiveAsync();

                    // キーボードサーバのIPアドレスを取得
                    var address = result.RemoteEndPoint.Address.ToString();

                    // 受信したデータを変換
                    var hostname = Encoding.ASCII.GetString(result.Buffer);

                    // Receive イベント を実行
                    AddDevice(new KeyboardDeviceInfo(string.Format("{0}({1})", address.ToString(), hostname), address, KeyboardDeviceInfo.KeyboardDeviceType.SERVER));
                    Console.WriteLine("Keyboard Server found: {0}({1})", address, hostname);
                }
            } catch (Exception e) {
                Console.WriteLine("ListenMessageAync 終了");
            }
        }

        /// <summary>
        /// キーボードサーバに接続する
        /// </summary>
        /// <param name="kdi"></param>
        private void OpenServer(KeyboardDeviceInfo kdi) {
            myClient?.Close();
            myClient = new TcpClient(kdi.DeviceName, this.serverPort);
            Task receive = ReceiveServer();

            OnKeyboardDeviceConnected(new KeyboardDeviceEventArgs(kdi.FriendlyName));
            Console.Out.WriteLine(String.Format("Open [{0}].", kdi.DeviceName));
        }

        /// <summary>
        /// キーボードサーバからの受信
        /// </summary>
        /// <returns></returns>
        private async Task ReceiveServer() {
            NetworkStream stream = myClient.GetStream();
            byte[] buf = new byte[256];
            while (true) {
                Int32 bytes = await stream.ReadAsync(buf, 0, buf.Length);
                if (bytes == 0) {
                    break;
                }
                string dat = System.Text.Encoding.ASCII.GetString(buf, 0, bytes);
                Console.Out.WriteLine(String.Format("Server received:[{0}]", dat));
            }
        }

        /// <summary>
        /// デバイスへ送信
        /// </summary>
        /// <param name="size"></param>
        private async Task SendPktToDeviceAsync(int size) {
            // COMポートへ送信
            if (myPort != null) {
                while (!COMSendEnable) {
                    await Task.Delay(1);
                }
                myPort?.Write(pkt, 0, size);
            }

            // キーボードサーバへ送信
            if (myClient != null) {
                NetworkStream stream = myClient.GetStream();
                stream.Write(pkt, 0, size);
                stream.Flush();
            }
        }

        /*****************************************************************
         * キー送信
         *****************************************************************/
        private const int singleWait = 1;

        /// <summary>
        /// ファイル送信中でなければ、文字列を送信。
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public async Task SendAsync(string s, int delay = singleWait) {
            if (!FileSending) await _SendAsync(s, delay);
        }

        /// <summary>
        /// 文字列を送信。（内部用）
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private async Task _SendAsync(string s, int delay = singleWait) {
            foreach (char c in s) {
                await _SendAsync(c, delay);
            }
        }

        /// <summary>
        /// ファイル送信中でなければ、charを指定してキーを送信する。
        /// </summary>
        /// <param name="c"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        public async Task SendAsync(char c, int delay = singleWait) {
            if (!FileSending) await _SendAsync(c, delay);
        }

        /// <summary>
        /// charを指定してキーを送信する。（内部用）
        /// </summary>
        /// <param name="c"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        private async Task _SendAsync(char c, int delay = singleWait) {
            await _SendAsync(KeyCode.CharToCode(c), delay);
        }

        /// <summary>
        /// ファイル送信中でなければ、Keyを指定してキーを送信する。
        /// </summary>
        /// <param name="k"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        public async Task SendAsync(Key k, int delay = singleWait) {
            if (!FileSending) await _SendAsync(k, delay);
        }

        /// <summary>
        /// Keyを指定してキーを送信する。（内部用）
        /// </summary>
        /// <param name="k"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        private async Task _SendAsync(Key k, int delay = singleWait) {
            await _SendAsync(KeyCode.KeyToCode(k), delay);
        }

        /// <summary>
        /// ファイル送信中でなければ、スキャンコードを指定してキーを送信する。
        /// 0x100以上のスキャンコードはシフトキーを押している状態とみなす。
        /// </summary>
        /// <param name="exCode"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        public async Task SendAsync(int exCode, int delay = singleWait) {
            if (!FileSending) await _SendAsync(exCode, delay);
        }

        /// <summary>
        /// スキャンコードを指定してキーを送信する。
        /// 0x100以上のスキャンコードはシフトキーを押している状態とみなす。（内部用）
        /// </summary>
        /// <param name="exCode"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        private async Task _SendAsync(int exCode, int delay = singleWait) {
            byte mod = (exCode & 0x100) == 0 ? (byte)0 : KeyCode.MOD_LSHIFT;
            byte code = (byte)(exCode & 0xff);
            await _SendAsync(mod, code, delay);
        }

        /// <summary>
        /// ファイル送信中でなければ、モディファイアとスキャンコードを指定して送信。
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="code"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        public async Task SendAsync(byte mod, byte code, int delay = singleWait) {
            if (!FileSending) await _SendAsync(mod, code, delay);
        }

        /// <summary>
        /// ファイル送信中でなければ、モディファイアとスキャンコードを指定して送信。（内部用）
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="code"></param>
        /// <param name="delay"></param>
        /// <returns></returns>
        private async Task _SendAsync(byte mod, byte code, int delay = singleWait) {
            pkt[0] = 0x00; // raw packet indicator
            pkt[1] = mod;
            pkt[2] = code;
            await SendPktToDeviceAsync(1 + 2);

            await Task.Delay(delay);

            pkt[2] = 0;
            await SendPktToDeviceAsync(1 + 2);
        }

        /// <summary>
        /// ファイル送信中でなければ、最大6個のスキャンコードをまとめて送信。
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="codes"></param>
        /// <returns></returns>
        public async Task SendAsync(byte mod, byte[] codes, int delay = singleWait) {
            if (!FileSending) await _SendAsync(mod, codes, delay);
        }

        /// <summary>
        /// 最大6個のスキャンコードをまとめて送信。（内部用）
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="codes"></param>
        /// <returns></returns>
        private async Task _SendAsync(byte mod, byte[] codes, int delay = singleWait) {
            pkt[1] = mod;
            int len = codes.Length;
            for (int i = 0; i < 6; i++) {
                byte b = 0;
                if (i < len) {
                    b = codes[i];
                }
                pkt[2 + i] = b;
            }
            await SendPktAsync(delay);
        }

        /// <summary>
        /// HIDレポートパケット(モードバイト+mod+code*6)を送信する。
        /// </summary>
        /// <returns></returns>
        private async Task SendPktAsync(int delay = singleWait) {
            pkt[0] = 0xff; // stream packet indicator
            await SendPktToDeviceAsync(2 + 6);

            //if (delay > 0) {
            await Task.Delay(delay);
            //}

            for (int i = 2; i < 8; i++) {
                pkt[i] = 0;
            }
            await SendPktToDeviceAsync(2 + 6);
        }


        /*****************************************************************
         * ファイル送信
         *****************************************************************/
        private static readonly int bitsPerCode = 6;
        // LCM(6bit*6,8bit) = (2*2*3*3bit, 2*2*2bit) = 2*2*2*3*3bit = 72bit = 9byte
        private static readonly int maxBytesPerPkt = 9 * 40;
        private static readonly int maxBitsPerPkt = maxBytesPerPkt * 8;
        private byte[] bitBuf = new byte[maxBytesPerPkt];
        private BitStream bs;

        //                                         0         1         2          3         4         5         6
        //                                         012345678901234567890123456 78901234567890123456789012345678901 2   3
        private static readonly string tokenStr = "ABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()=~|`{+*}<>?_0123456789/- .\t\x1b\b";
        private static byte[] tokens = new byte[64];
        private static readonly char endMark = ']';

        /// <summary>
        /// トークンを初期化する。
        /// </summary>
        private void InitToken() {
            string a = "0";
            for (int i = 0; i < 64; i++) {
                char ch = tokenStr[i];
                var code = KeyCode.CharToCode(ch);
                if ((code & 0x100) == 0) {
                    switch (ch) {
                        case '-':
                            code = 0x56;
                            break;
                        case '.':
                            code = 0x63;
                            break;
                        case '/':
                            code = 0x54;
                            break;
                        default:
                            if (code >= 0x1e && code <= 0x27) { // 1-0
                                code = (byte)(0x59 + (code - 0x1e)); // 0x59=Keypad 1
                            }
                            break;
                    }
                }
                tokens[i] = (byte)(code & 0xff);
            }
        }

        /// <summary>
        /// 指定されたビット数のデータをトークンに変換して送信。
        /// </summary>
        /// <param name="bitSize"></param>
        private async Task SendBitsAsync(int bitSize) {
            bs.Seek(0, 0);

            string s = "";
            for (int bitCount = 0; bitCount < bitSize;) {
                for (int i = 0; i < 6; i++) {
                    byte b = 0;
                    if (bitCount < bitSize) {
                        b = bs.ReadByte(bitsPerCode);
                        bitCount += bitsPerCode;
                    }
                    pkt[2 + i] = tokens[b];
                    //s += string.Format("{0}({1})[{2}]", tokenStr[b], Convert.ToString(b, 16), Convert.ToString(tokens[b], 16));
                    s += tokenStr[b];
                }
                pkt[1] = KeyCode.MOD_LSHIFT;
                await SendPktAsync(0);
            }
            //Console.Write("\ntokens=[{0}]\n", s);

            // 改行送信
            //pkt[1] = 0;
            pkt[2] = KeyCode.KEY_ENTER;
            await SendPktAsync(0);
        }


        /// <summary>
        /// 指定されたファイルを送信する。
        /// </summary>
        /// <param name="fname"></param>
        /// <returns></returns>
        public async Task<long> SendFileAsync_old(string fname) {
            FileSending = true;
            BinaryReader br = null;
            long fileSize = 0;

            try {
                br = new BinaryReader(File.Open(fname, FileMode.Open), Encoding.ASCII);

                if (bs == null) {
                    bs = new BitStream(bitBuf);
                }
                InitToken();

                // ファイル名を送信
                string sendFname = "";
                foreach (char c in Path.GetFileName(fname)) {
                    // 多バイトコードは含めない
                    if (c < 0x7f) {
                        sendFname += c;
                    }
                    if (sendFname.Length >= 14) break;
                }
                await _SendAsync(reverseCase(sendFname));
                await _SendAsync(KeyCode.KEY_ENTER);

                // サイズを送信
                fileSize = new FileInfo(fname).Length;
                await _SendAsync("" + fileSize);
                Console.WriteLine("size=" + fileSize);
                await _SendAsync(KeyCode.KEY_ENTER);

                // ファイルの中身を送信
                try {
                    long readSize = 0;
                    while (readSize < fileSize) {
                        bs.Seek(0, 0);
                        for (int i = 0; i < maxBytesPerPkt; i++) {
                            byte b = 0;
                            if (readSize < fileSize) {
                                b = br.ReadByte();
                                readSize++;
                            }
                            bs.WriteByte(b);
                        }
                        Console.Write("sendbits");
                        await SendBitsAsync(maxBitsPerPkt);
                    }
                } catch (EndOfStreamException e) {
                    Debug.WriteLine("File read finised.");
                } catch (Exception e) {
                    Debug.WriteLine(e.ToString());
                    //throw;
                }

                // 終了マークを送信
                await _SendAsync(endMark);
                await _SendAsync(KeyCode.KEY_ENTER);
            } finally {
                br?.Close();
                FileSending = false;
            }
            return fileSize;
        }


        /// <summary>
        /// ファイル転送の準備をする。
        /// </summary>
        /// <param name="fname"></param>
        /// <param name="p"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<int> PrepareSendFileAsync(string fname, IProgress<TransferStatusInfo> p, CancellationToken token) {
            byte[] dat = System.IO.File.ReadAllBytes(fname);

            FileInfo fi = new FileInfo(fname);

            // ファイル名
            fileName = "";
            foreach (char c in fi.Name) {
                // 多バイトコードは含めない
                if (c < 0x7f) {
                    fileName += c;
                }
                if (fileName.Length >= 14) break;
            }

            // ファイルの種別
            var key = Registry.ClassesRoot.OpenSubKey(fi.Extension);
            var mimeType = key?.GetValue("Content Type") as string;

            fileType = "dat";
            if (mimeType != null) {
                if (mimeType.StartsWith("text")) {
                    // テキストファイル
                    fileType = "txt";
                    dat = UTF16Reader.GetBytes(fname);
                } else if (mimeType.StartsWith("image")) {
                    // 画像ファイル
                    ImageReader ir = new ImageReader(fname);
                    dat = ir.GetBytes();
                    fileType = string.Format("grp{0}x{1}", ir.width, ir.height);
                }
            }

            if (fileType == "dat") {
                dat = System.IO.File.ReadAllBytes(fname);
            }
            actualFileSize = dat.Length;

            transInfo.Mode = "圧縮中";
            transInfo.Fname = fileName;
            transInfo.Min = 0;
            transInfo.Max = actualFileSize;
            transInfo.Value = 0;
            transInfo.finished = false;

            Compress comp = new Compress(dat);
            fileData = await Task.Run(() => comp.GetCompressedData(p, token, transInfo));
            compFileSize = fileData.Length;

            Console.WriteLine("File=[{0}] Type={1} compSize={2} actualSize={3}", fileName, fileType, compFileSize, actualFileSize);

            return compFileSize;
        }

        /// <summary>
        /// 大文字小文字を逆転させる
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string reverseCase(string s) {
            return new string(s.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c)) : c).ToArray());
        }

        /// <summary>
        /// バイトデータを送信する
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataName"></param>
        /// <param name="dataType"></param>
        /// <param name="compSize"></param>
        /// <param name="actualSize"></param>
        /// <returns></returns>
        public async Task<int> SendDataAsync(IProgress<TransferStatusInfo> p, CancellationToken token) {
            FileSending = true;

            transInfo.Mode = "転送中";
            transInfo.Max = fileData.Length;
            transInfo.Value = 0;
            p.Report(transInfo);


            int sentSize = 0;
            int dataSize = Math.Min(fileData.Length, compFileSize);

            if (bs == null) {
                bs = new BitStream(bitBuf);
            }
            InitToken();

            // ファイル名名を送信
            await _SendAsync(fileName);
            await _SendAsync(KeyCode.KEY_ENTER);

            // ファイルタイプを送信
            await _SendAsync(fileType);
            await _SendAsync(KeyCode.KEY_ENTER);

            // 圧縮データの送信サイズを送信
            await _SendAsync(dataSize.ToString());
            await _SendAsync(KeyCode.KEY_ENTER);

            // 圧縮前のデータのサイズを送信
            await _SendAsync(actualFileSize.ToString());
            await _SendAsync(KeyCode.KEY_ENTER);

            // データを送信
            while (sentSize < dataSize) {
                bs.Seek(0, 0);
                for (int i = 0; i < maxBytesPerPkt; i++) {
                    byte b = 0;
                    if (sentSize < dataSize) {
                        b = fileData[sentSize++];
                    }
                    bs.WriteByte(b);
                }
                await SendBitsAsync(maxBitsPerPkt);
                transInfo.Value = sentSize;
                p.Report(transInfo);
                if (token.IsCancellationRequested) {
                    Console.WriteLine("DummyWork: キャンセルリクエスト受信");
                    break;
                }
            }

            // 終了マークを送信
            await _SendAsync(endMark);
            await _SendAsync(KeyCode.KEY_ENTER);

            transInfo.finished = true;
            p.Report(transInfo);

            FileSending = false;
            return sentSize;
        }

    }
}
