using System;
using System.Collections;
using System.Text;

namespace Brass {
    public partial class Program {

        public static byte RLE_Flag; // = 0x91;
        public static bool RLE_ValueFirst;

        /// <summary>
        /// Compress a block of data using RLE (run-length encoding)
        /// </summary>
        /// <param name="data">Data to compress</param>
        /// <returns>Compressed data</returns>
        private static byte[] RLE(byte[] data) {
            ArrayList Return = new ArrayList();

            for (int i = 0; i < data.Length; ++i) {
                if (data[i] == RLE_Flag || (i < data.Length - 3 && data[i] == data[i + 1] && data[i] == data[i + 2] && data[i] == data[i + 3])) {
                    // We have a run!
                    Return.Add(RLE_Flag);
                    
                    int DataLengthCount = 0;
                    byte CurrentByte = data[i];
                    int FinalPosition = Math.Min(i + 0xFF, data.Length);

                    for (; i < FinalPosition; ++i) {
                        if (data[i] == CurrentByte) {
                            ++DataLengthCount;
                        } else {
                            break;
                        }
                    }
                    --i;
                    
                    if (RLE_ValueFirst) Return.Add(CurrentByte);
                    Return.Add((byte)DataLengthCount);
                    if (!RLE_ValueFirst) Return.Add(CurrentByte);
                } else {
                    Return.Add(data[i]);
                }
            }

            return (byte[])Return.ToArray(typeof(byte));
        }
    }
}
