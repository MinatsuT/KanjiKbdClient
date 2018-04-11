using BitStreams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KanjiKbd {
    class Compress {
        private byte[] buf = null;

        private const int minMatch = 3;
        private const int maxPos = 0xffff;
        private const int maxLen = 0xff;
        private const int memlevel = 15;
        private const int hashBits = memlevel + 7;
        private const int hashSize = 1 << hashBits;
        private const int hashMask = hashSize - 1;
        private const int hashShift = ((hashBits + minMatch - 1) / minMatch);
        private List<int>[] hashHead = null;
        private List<int> hashHist = null;
        private int curHash = 0;
        private int curPtr = 0;

        public Compress(byte[] inputBytes) {
            this.buf = inputBytes;
        }

        public byte[] GetCompressedData(IProgress<TransferStatusInfo> p, CancellationToken token, TransferStatusInfo transInfo) {
            if (buf.Length == 0) {
                return null;
            }

            hashHead = new List<int>[hashSize];
            hashHist = new List<int>();
            curHash = 0;

            var outBuf = new List<byte>();
            var compFlagByte = new byte[1];
            var bs = new BitStream(compFlagByte);
            int outBits = 0;
            int compFlagPos = 0;

            outBuf.Add(0); // ビットフラグを追加
            compFlagPos = outBuf.Count - 1;
            bs.Seek(0, 0);

            curPtr = -minMatch;
            for (int i = 0; i < minMatch; i++) {
                ForwardPtrAddHash();
            }

            transInfo.Max = buf.Length;
            while (curPtr < buf.Length) {
                int pos, len;
                if (LongestMatch(out pos, out len) && len > minMatch) {
                    len = Math.Min(len, maxLen + minMatch);
                    // minMatchバイトより長くマッチしたのでpos,lenを追加
                    bs.WriteBit(1);
                    // pos
                    var writePos = (curPtr - minMatch) - pos; // curPtr-minMatchの位置が、ポジション0
                    outBuf.Add((byte)(writePos >> 8));
                    outBuf.Add((byte)(writePos & 0xff));
                    // len
                    outBuf.Add((byte)(len - minMatch)); // 0でminMatchバイト

                    // ハッシュを更新しつつ、注目点を進める
                    for (int i = 0; i < len; i++) {
                        ForwardPtrAddHash();
                    }
                } else {
                    // 生データを追加
                    bs.WriteBit(0);
                    int ed = Math.Min(curPtr + minMatch, buf.Length);
                    while (curPtr < ed) {
                        outBuf.Add(buf[curPtr]);
                        ForwardPtrAddHash();
                    }
                }
                if (++outBits == 8) {
                    bs.Seek(0, 0);
                    outBuf[compFlagPos] = bs.ReadByte(8); // ビットフラグ書き出し
                    bs.Seek(0, 0);
                    if (curPtr < buf.Length) {
                        outBuf.Add(0); // 新しいビットフラグ追加
                        compFlagPos = outBuf.Count - 1;
                    }
                    outBits = 0;
                }
                transInfo.Value = curPtr;
                p.Report(transInfo);
                if (token.IsCancellationRequested) {
                    break;
                }
            }
            transInfo.Value = curPtr;
            p.Report(transInfo);

            if (outBits > 0) {
                bs.Seek(0, 0);
                outBuf[compFlagPos] = bs.ReadByte(8); // ビットフラグ書き出し
            }

            return outBuf.ToArray();
        }

        private void ForwardPtrAddHash() {
            curPtr++;

            if (curPtr + minMatch < 0 || curPtr + minMatch - 1 >= buf.Length) {
                return;
            }

            // ハッシュ更新
            curHash = ((curHash << hashShift) ^ buf[curPtr + minMatch - 1]) & hashMask;

            if (curPtr >= 0) {
                if (hashHead[curHash] == null) {
                    hashHead[curHash] = new List<int>();
                }
                hashHead[curHash].Insert(0, curPtr);
                hashHist.Add(curHash);
                if (hashHist.Count > maxPos + minMatch) {
                    var h = hashHist[0];
                    hashHead[h].RemoveAt(hashHead[h].Count - 1);
                    hashHist.RemoveAt(0);
                }
            }
        }

        private bool LongestMatch(out int pos, out int len) {
            pos = -1;
            len = 0;

            if (curPtr + minMatch < 0 || curPtr + minMatch >= buf.Length) {
                return false;
            }

            foreach (var tgt in hashHead[curHash]) {
                if ((curPtr - minMatch - tgt) > maxPos || tgt > curPtr - minMatch) {
                    continue;
                }
                var matchLen = MatchLen(tgt, len);
                if (matchLen > len) {
                    pos = tgt;
                    len = matchLen;
                }
            }
            return (len > 0);
        }

        private int MatchLen(int tgt, int candidate) {
            int candTgt = tgt + candidate - 1;
            int candPtr = curPtr + candidate - 1;
            while (candTgt >= tgt) {
                if (buf[candTgt--] != buf[candPtr--]) {
                    return -1;
                }
            }

            int chkPtr = curPtr + candidate;
            int chkTgt = tgt + candidate;
            int len = candidate;
            int ed = Math.Min(curPtr + maxLen, buf.Length);
            while (chkPtr < ed && buf[chkTgt++] == buf[chkPtr++]) {
                len++;
            }
            return len;
        }

    }
}
