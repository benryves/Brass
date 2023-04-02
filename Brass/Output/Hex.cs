using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Brass {
    
    public class HexFileRecord {

        public enum RecordType {
            Data = 0x00,
            EndOfFile = 0x01,
            ExtendedSegmentAddress = 0x02,
            StartSegmentAddress = 0x03,
            ExtendedLinearAddress = 0x04,
            StartLinearAddress = 0x05,
        }

        public RecordType Type = RecordType.Data;

        public readonly string RecordStart = ":";
        
        public readonly byte[] Data;

        public readonly uint Address;

        public readonly Program.Binary Format;

        public override string ToString() {
            string DataString = "";
            byte Checksum = (byte)(Data.Length + (Address & 0xFF) + ((Address >> 8) & 0xFF) + (long)Type);
            foreach (byte b in Data) {
                DataString += b.ToString("X2");
                Checksum += b;
            }
            Checksum = (byte)(-Checksum);
            return RecordStart + Data.Length.ToString("X2") + ((uint)(Address & 0xFFFF)).ToString("X4") + ((int)Type).ToString("X2") + DataString + Checksum.ToString("X2");
        }

        public HexFileRecord(Program.Binary type, byte[] data, uint address) {
            this.Data = data == null ? new byte[0] : data;
            this.Address = address;
            this.Format = type;
        }

        public HexFileRecord(Program.Binary format, byte[] data, uint address, RecordType type) {
            this.Data = data == null ? new byte[0] : data;
            this.Address = address;
            this.Type = type;
            this.Format = format;
        }
    }

    public class HexFileWriter {

        private readonly Program.Binary HexFileFormat;
        private readonly bool WritePageInfo = false;
        public HexFileWriter(Program.Binary format, bool writePageInfo) {
            this.HexFileFormat = format;
            this.WritePageInfo = writePageInfo;            
        }

        public void WritePage(Program.BinaryPage page, TextWriter writer) {
            if (this.WritePageInfo) {
                writer.WriteLine(new HexFileRecord(this.HexFileFormat, new byte[] { (byte)(page.Page >> 8), (byte)(page.Page) }, 0x0000, HexFileRecord.RecordType.ExtendedSegmentAddress));
            }
            List<byte> Data = new List<byte>();
            uint StartAddress = page.BinaryStartLocation;
            for (uint i = page.BinaryStartLocation; i <= page.BinaryEndLocation; ++i) {
                if (page.OutputBinary[i].WriteCount > 0) Data.Add(page.OutputBinary[i].Data);
                if (Data.Count > 0 && (i == page.BinaryEndLocation || page.OutputBinary[i].WriteCount == 0 || Data.Count >= 0x20)) {
                    writer.WriteLine(new HexFileRecord(this.HexFileFormat, Data.ToArray(), StartAddress + page.StartAddress));
                    StartAddress = i + 1;
                    Data.Clear();
                }
            }
        }
        public void WriteEndOfFile(TextWriter writer) {

            writer.WriteLine(new HexFileRecord(this.HexFileFormat, null, 0x0000, HexFileRecord.RecordType.EndOfFile));
        }
    }
}
