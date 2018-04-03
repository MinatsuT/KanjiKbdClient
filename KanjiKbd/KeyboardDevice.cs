using BitStreams;
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

        private byte[] buf = new byte[1024];

        public bool FileSending { get; internal set; } = false;

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

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public KeyboardDevice() {
        }

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
            Console.Out.WriteLine(String.Format("Open [{0}].", kdi.DeviceName));
        }

        /// <summary>
        /// COMポートからの受信
        /// </summary>
        private void ReceiveCOMPort(object sender, System.IO.Ports.SerialDataReceivedEventArgs e) {
            string dat = myPort.ReadExisting();
            Console.Out.WriteLine(String.Format("COM received:[{0}]", dat));
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
        /// ファイル送信中でなければ、文字列を送信。
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public async Task SendAsync(string s) {
            if (!FileSending) await _SendAsync(s);
        }

        /// <summary>
        /// 文字列を送信。（内部用）
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private async Task _SendAsync(string s) {
            foreach (char c in s) {
                await _SendAsync(c);
            }
        }


        /// <summary>
        /// ファイル送信中でなければ、charを指定してキーを送信する。
        /// </summary>
        /// <param name="c"></param>
        public async Task SendAsync(char c) {
            if (!FileSending) await _SendAsync(c);
        }

        /// <summary>
        /// charを指定してキーを送信する。（内部用）
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        private async Task _SendAsync(char c) {
            await _SendAsync(KeyCode.CharToCode(c));
        }

        /// <summary>
        /// ファイル送信中でなければ、Keyを指定してキーを送信する。
        /// </summary>
        /// <param name="k"></param>
        public async Task SendAsync(Key k) {
            if (!FileSending) await _SendAsync(k);
        }

        /// <summary>
        /// Keyを指定してキーを送信する。（内部用）
        /// </summary>
        /// <param name="k"></param>
        /// <returns></returns>
        private async Task _SendAsync(Key k) {
            await _SendAsync(KeyCode.KeyToCode(k));
        }

        /// <summary>
        /// ファイル送信中でなければ、スキャンコードを指定してキーを送信する。
        /// 0x100以上のスキャンコードはシフトキーを押している状態とみなす。
        /// </summary>
        /// <param name="exCode"></param>
        public async Task SendAsync(int exCode) {
            if (!FileSending) await _SendAsync(exCode);
        }

        /// <summary>
        /// スキャンコードを指定してキーを送信する。
        /// 0x100以上のスキャンコードはシフトキーを押している状態とみなす。（内部用）
        /// </summary>
        /// <param name="exCode"></param>
        /// <returns></returns>
        private async Task _SendAsync(int exCode) {
            byte mod = (exCode & 0x100) == 0 ? (byte)0 : KeyCode.MOD_LSHIFT;
            byte code = (byte)(exCode & 0xff);
            await _SendAsync(mod, code);
        }

        /// <summary>
        /// ファイル送信中でなければ、モディファイアとスキャンコードを指定して送信。
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        public async Task SendAsync(byte mod, byte code) {
            if (!FileSending) await _SendAsync(mod, code);
        }

        /// <summary>
        /// ファイル送信中でなければ、モディファイアとスキャンコードを指定して送信。（内部用）
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="code"></param>
        /// <returns></returns>
        private async Task _SendAsync(byte mod, byte code) {
            buf[0] = 0;
            buf[1] = mod;
            buf[2] = code;
            SendPkt(3);
            await Task.Delay(waitPerPkt);

            buf[2] = 0;
            SendPkt(3);
            //await Task.Delay(waitPerPkt);
        }

        /// <summary>
        /// サイズを指定して送信
        /// </summary>
        /// <param name="size"></param>
        public void SendPkt(int size) {
            // COMポートへ送信
            myPort?.Write(buf, 0, size);

            // キーボードサーバへ送信
            if (myClient != null) {
                NetworkStream stream = myClient.GetStream();
                stream.Write(buf, 0, size);
                stream.Flush();
            }
        }

        //                                       0         1         2         3         4                                     
        //                                       01234567890123456789012345678901234567890123    
        private static readonly string tokens = ",-./0123456789:;@abcdefghijklmnopqrstuvwxyz[";
        private static readonly char endMark = ']';
        private bool[] usableFlag = new bool[tokens.Length];
        private List<int> usedPos = new List<int>();

        /// <summary>
        /// 使用可能トークンを初期化する。
        /// </summary>
        private void InitToken() {
            usedPos.Clear();
            for (int i = 0; i < tokens.Length; i++) {
                usableFlag[i] = true;
            }
        }

        /// <summary>
        /// 使用可能なトークンを探す。
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        private int FindUsableToken(byte b) {
            int pos = 0;
            int count = -1;
            for (pos = 0; pos < tokens.Length; pos++) {
                if (usableFlag[pos]) {
                    count++;
                }
                if (count == b) {
                    break;
                }
            }
            return pos;
        }

        /// <summary>
        /// 使用したトークンを使用不可にマークする。
        /// 12個のトークンが使用済みになったら、最初のトークンを使用可能に復帰する。
        /// </summary>
        /// <param name="pos"></param>
        private void PushUsed(int pos) {
            usableFlag[pos] = false;
            usedPos.Add(pos);
            if (usedPos.Count == 12) {
                int oldest = usedPos.First();
                usableFlag[oldest] = true;
                usedPos.RemoveAt(0);
            }
        }

        /// <summary>
        /// 指定されたビット数のデータをトークンに変換して送信。
        /// </summary>
        /// <param name="bitSize"></param>
        private async Task SendBitsAsync(int bitSize) {
            buf[0] = 0xff; // raw packet
            bs.Seek(0, 0);
            int delay = 10;

            string s = "";
            for (int bitCount = 0; bitCount < bitSize;) {
                for (int i = 0; i < 6; i++) {
                    byte b = 0;
                    if (bitCount < bitSize) {
                        b = bs.ReadByte(5);
                        bitCount += 5;
                    }
                    int pos = FindUsableToken(b);
                    //s += string.Format("{0}({1})=[{2}]", b, pos, tokens[pos]);
                    s += tokens[pos];
                    buf[1 + i] = (byte)KeyCode.CharToCode(tokens[pos]);
                    PushUsed(pos);
                }
                SendPkt(1 + 6);
                for (int i = 0; i < 6; i++) buf[1 + i] = 0;
                SendPkt(1 + 6);
                SendPkt(1 + 6);
                SendPkt(1 + 6);
                //SendPkt(1 + 6);
                //await Task.Delay(delay);
            }
            Console.WriteLine("tokens=[{0}]", s);

            // 改行送信
            for (int i = 0; i < 6; i++) buf[1 + i] = 0;
            buf[1 + 0] = KeyCode.KEY_ENTER;
            SendPkt(1 + 6);
            for (int i = 0; i < 6; i++) buf[1 + i] = 0;
            SendPkt(1 + 6);
            SendPkt(1 + 6);
            SendPkt(1 + 6);
            //Thread.Sleep(delay);
            //SendPkt(1 + 6);
            //await Task.Delay(100);
        }

        // LCM(5bit*6,8bit) = (2*15bit, 2*4bit) = 2*4*15bit = 8*15bit = 15byte
        private static readonly int maxBytesPerPkt = 15 * 20;
        private static readonly int maxBitsPerPkt = maxBytesPerPkt * 8;
        private byte[] bitBuf = new byte[maxBytesPerPkt];
        private BitStream bs;
        public static int waitPerPkt { get; set; } = 20;

        public async Task<long> SendFileAsync(string fname) {
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
                    if (c < 0x7f) {
                        sendFname += c;
                    }
                    if (sendFname.Length >= 14) break;
                }
                await _SendAsync(reverseCase(sendFname));
                await _SendAsync(0, KeyCode.KEY_ENTER);

                // サイズを送信
                fileSize = new FileInfo(fname).Length;
                await _SendAsync("" + fileSize);
                await _SendAsync(0, KeyCode.KEY_ENTER);

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

        public static string reverseCase(string s) {
            return new string(s.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c)) : c).ToArray());
        }

    }

}
