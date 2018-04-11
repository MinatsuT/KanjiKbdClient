using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace KanjiKbd {
    /// <summary>
    /// TransferStatusDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class TransferStatusDialog : Window {
        private CancellationTokenSource tokenSource = null;

        public TransferStatusDialog() {
            InitializeComponent();
        }

        public TransferStatusDialog(CancellationTokenSource tokenSource) : this() {
            this.tokenSource = tokenSource;
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e) {
            Console.WriteLine("TransferStatusDialog: キャンセルボタンおされた");
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            Console.WriteLine("TransferStatusDialog: Closingイベント処理します");
            Cancel();
        }

        private void Cancel() {
            if (ProgressBar.Value != ProgressBar.Maximum) {
                Console.WriteLine("TransferStatusDialog: キャンセル処理します");
                tokenSource.Cancel();
            } else {
                Console.WriteLine("TransferStatusDialog: 処理が終わっているので、キャンセルしません");
            }
        }

        public void OnUpadte(TransferStatusInfo transInfo) {
            Title = string.Format("ファイル転送 {0}", transInfo.Fname);
            ProgressBar.Minimum = transInfo.Min;
            ProgressBar.Maximum = transInfo.Max;
            ProgressBar.Value = transInfo.Value;
            Status.Text = string.Format("{0}... {1:#,0}/{2:#,0}", transInfo.Mode, transInfo.Value, transInfo.Max);
            //Label.Content = "ほげぷー";

            if (transInfo.finished) {
                Close();
            }
        }
    }
}
