using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace KanjiKbd {
    class KeyCode {
        public static readonly byte MOD_LCTRL = 1<<0;
        public static readonly byte MOD_LSHIFT = 1<<1;
        public static readonly byte MOD_LALT = 1<<2;
        public static readonly byte MOD_LWINDOWS = 1<<3;
        public static readonly byte MOD_RCTRL = 1 << 4;
        public static readonly byte MOD_RSHIFT = 1 << 5;
        public static readonly byte MOD_RALT = 1 << 6;
        public static readonly byte MOD_RWINDOWS = 1 << 7;

        public static readonly byte KEY_ENTER = 0x28;

        private static readonly int[] asciiToScanCode = {
            //  0      1      2      3      4      5      6      7      8      9      A      B      C      D      E      F
            0x02C, 0x11E, 0x11F, 0x120, 0x121, 0x122, 0x123, 0x124, 0x125, 0x126, 0x134, 0x133, 0x036, 0x02d, 0x037, 0x038, //0x20-0x2F
            0x027, 0x01E, 0x01F, 0x020, 0x021, 0x022, 0x023, 0x024, 0x025, 0x026, 0x034, 0x033, 0x136, 0x12d, 0x137, 0x138, //0x30-0x3F
            0x02f, 0x104, 0x105, 0x106, 0x107, 0x108, 0x109, 0x10A, 0x10B, 0x10C, 0x10D, 0x10E, 0x10F, 0x110, 0x111, 0x112, //0x40-0x4F
            0x113, 0x114, 0x115, 0x116, 0x117, 0x118, 0x119, 0x11A, 0x11B, 0x11C, 0x11D, 0x030, 0x089, 0x032, 0x02e, 0x187, //0x50-0x5F
            0x12f, 0x004, 0x005, 0x006, 0x007, 0x008, 0x009, 0x00A, 0x00B, 0x00C, 0x00D, 0x00E, 0x00F, 0x010, 0x011, 0x012, //0x60-0x6F
            0x013, 0x014, 0x015, 0x016, 0x017, 0x018, 0x019, 0x01A, 0x01B, 0x01C, 0x01D, 0x130, 0x189, 0x132, 0x12e};       //0x70-0x7E

        public static readonly byte[] keyToScanCode = new byte[256];

        public static int CharToCode(char c) {
            return KeyCode.asciiToScanCode[Convert.ToUInt16(c) - 0x20];
        }

        public static int KeyToCode(Key k) {
            return keyToScanCode[(int)k];
        }

        static KeyCode() {
            // A(0x04)-Z(0x1D)
            for (int i = (int)Key.A; i <= (int)Key.Z; i++) {
                keyToScanCode[i] = (byte)(0x04 + (i - (int)Key.A));
            }

            // 1(0x1E)-9(0x26)
            for (int i = (int)Key.D1; i <= (int)Key.D9; i++) {
                keyToScanCode[i] = (byte)(0x1E + (i - (int)Key.D1));
            }

            // 0(0x27)
            keyToScanCode[(int)Key.D0] = 0x27;

            keyToScanCode[(int)Key.Enter] = 0x28;
            keyToScanCode[(int)Key.Escape] = 0x29;
            keyToScanCode[(int)Key.Back] = 0x2A;
            keyToScanCode[(int)Key.Tab] = 0x2B;
            keyToScanCode[(int)Key.Space] = 0x2C;
            keyToScanCode[(int)Key.OemMinus] = 0x2D; // -
            keyToScanCode[(int)Key.OemQuotes] = 0x2E; // ^
            keyToScanCode[(int)Key.Oem5] = 0x89; // \
            keyToScanCode[(int)Key.Oem3] = 0x2f; // @
            keyToScanCode[(int)Key.OemOpenBrackets] = 0x30; // [
            keyToScanCode[(int)Key.OemPlus] = 0x33; // ;
            keyToScanCode[(int)Key.Oem1] = 0x34; // :
            keyToScanCode[(int)Key.Oem6] = 0x32; // ]
            keyToScanCode[(int)Key.OemComma] = 0x36; // ,
            keyToScanCode[(int)Key.OemPeriod] = 0x37; // .
            keyToScanCode[(int)Key.OemQuestion] = 0x38; // /
            keyToScanCode[(int)Key.OemBackslash] = 0x87; // _

            // F1(0x3A)-F12(0x45)
            for (int i = (int)Key.F1; i <= (int)Key.F12; i++) {
                keyToScanCode[i] = (byte)(0x3A + (i - (int)Key.F1));
            }

            keyToScanCode[(int)Key.PrintScreen] = 0x46;
            keyToScanCode[(int)Key.Scroll] = 0x47;
            keyToScanCode[(int)Key.Pause] = 0x48;

            keyToScanCode[(int)Key.Insert] = 0x49;
            keyToScanCode[(int)Key.Home] = 0x4A;
            keyToScanCode[(int)Key.PageUp] = 0x4B;
            keyToScanCode[(int)Key.Delete] = 0x4C;
            keyToScanCode[(int)Key.End] = 0x4D;
            keyToScanCode[(int)Key.PageDown] = 0x4E;

            keyToScanCode[(int)Key.Right] = 0x4F;
            keyToScanCode[(int)Key.Left] = 0x50;
            keyToScanCode[(int)Key.Down] = 0x51;
            keyToScanCode[(int)Key.Up] = 0x52;

            keyToScanCode[(int)Key.NumLock] = 0x53;
            keyToScanCode[(int)Key.Divide] = 0x54;
            keyToScanCode[(int)Key.Multiply] = 0x55;
            keyToScanCode[(int)Key.Subtract] = 0x56;
            keyToScanCode[(int)Key.Add] = 0x57;
            keyToScanCode[(int)Key.Return] = 0x58;

            // Num1(0x59)-Num9(0x61)
            for (int i = (int)Key.NumPad1; i <= (int)Key.NumPad9; i++) {
                keyToScanCode[i] = (byte)(0x59 + (i - (int)Key.NumPad1));
            }

            // Num0(0x62)
            keyToScanCode[(int)Key.NumPad0] = 0x62;

        }
    }
}
