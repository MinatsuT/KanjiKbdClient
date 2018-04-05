using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KanjiKbd {
    class ImageReader {
        public int width { get; internal set; }
        public int height { get; internal set; }

        private byte[] dat;

        public ImageReader(string fname) {
            //追記：元画像のフォーマット
            ImageFormat format;

            //１．指定したパスから画像を読み込む
            using (Bitmap img = new Bitmap(Image.FromFile(fname))) {
                //画像サイズを取得
                width = Math.Min(img.Width, 1024);
                height = Math.Min(img.Height, 1024);

                //追記：元画像のフォーマットを保持
                format = img.RawFormat;

                //ピクセルデータを取得
                dat = new byte[width * height * 2];

                long ptr = 0;
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        Color c = img.GetPixel(x, y);
                        int col = (c.R >> 3);
                        col = (col << 5) | (c.G >> 3);
                        col = (col << 5) | (c.B >> 3);
                        col = (col << 1) | (c.A >= 128 ? 1 : 0);
                        dat[ptr++] = (byte)(col >> 8);
                        dat[ptr++] = (byte)(col & 0xff);
                    }
                }

            }

        }

        public byte[] GetBytes() {
            return dat;
        }

    }
}
