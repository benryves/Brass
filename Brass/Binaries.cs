using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Reflection;

namespace Brass {
    public partial class Program {

        // Binary types:
        public enum Binary {
            Raw,                                    // Plain raw COM binary
            TI8X, TI83, TI82, TI86, TI85, TI73,     // TI headered binary
            Intel, IntelWord, MOS, Motorola,        // Hex file format
        }

        public class BinaryRecord {
            public ArrayList Data = new ArrayList();
            public int StartAddress = 0x0000;
        }

        public static Binary BinaryType = Binary.Raw;   // Current output (defaults to Raw).

        // Variable name (if applicable)
        public static string VariableName = "";

        // Variable type for TI binaries.
        public static int TIVariableType = 0;
        public static bool TIVariableTypeSet = false;


        public static byte[] OutputBinary;      // Stores the output stream of bytes.
        public static int[] HasBeenOutput;      // Have we written to this byte?
        public static int BinaryStartLocation;  // Start location of the binary file.
        public static int BinaryEndLocation;    // End location of the binary file.


        /// <summary>
        /// Write a byte to the output binary and flag it as written to
        /// </summary>
        /// <param name="ByteToWrite">Byte value to output</param>
        /// <returns>Success</returns>
        public static bool WriteToBinary(byte ByteToWrite) {
            if (ProgramCounter < 0) {
                DisplayError(ErrorType.Error, "Brass cannot assemble binaries which start before memory address 0: Data truncated.");
                ++ProgramCounter;
                return false;
            }
            // Allocate more binary space, if need be.
            if (ProgramCounter >= OutputBinary.Length) {
                int NewSize = OutputBinary.Length + 0x10000;
                byte[] NewOutputBinary = new byte[NewSize];
                int[] NewHasBeenOutput = new int[NewSize];
                for (int i = 0; i < OutputBinary.Length; ++i) {
                    NewOutputBinary[i] = OutputBinary[i];
                    NewHasBeenOutput[i] = HasBeenOutput[i];
                }
                OutputBinary = NewOutputBinary;
                HasBeenOutput = NewHasBeenOutput;
            }

            if (ProgramCounter < BinaryStartLocation) BinaryStartLocation = ProgramCounter;
            if (ProgramCounter > BinaryEndLocation) BinaryEndLocation = ProgramCounter;
            ++HasBeenOutput[ProgramCounter];
            OutputBinary[ProgramCounter++] = ByteToWrite;
            return true;
        }

        /// <summary>
        /// Write the binary out.
        /// </summary>
        /// <param name="BinaryFile">Filename of the binary to write to.</param>
        /// <returns>Success</returns>
        public static bool WriteBinary(string BinaryFile) {
            try {
                if (File.Exists(BinaryFile)) File.Delete(BinaryFile);

                switch (BinaryType) {
                    case Binary.Raw:
                        // Just dump a plain binary:
                        using (BinaryWriter BW = new BinaryWriter(new FileStream(BinaryFile, FileMode.OpenOrCreate), Encoding.ASCII)) {
                            for (int i = BinaryStartLocation; i <= BinaryEndLocation; ++i) {
                                BW.Write(OutputBinary[i]);
                            }
                        }
                        break;
                    case Binary.TI83:
                    case Binary.TI8X:
                    case Binary.TI82:
                    case Binary.TI86:
                    case Binary.TI85:
                    case Binary.TI73:
                        // Set TI variable type (if applicable).
                        if (!TIVariableTypeSet) {
                            TIVariableType = (BinaryType == Binary.TI86 || BinaryType == Binary.TI85) ? 0x12 : 0x06;
                        }
                        // Write a TI-compatible binary:
                        using (BinaryWriter BW = new BinaryWriter(new FileStream(BinaryFile, FileMode.OpenOrCreate), Encoding.ASCII)) {

                            // 8 byte type signature

                            switch (BinaryType) {
                                case Binary.TI8X:
                                    BW.Write("**TI83F*".ToCharArray()); break;
                                case Binary.TI83:
                                    BW.Write("**TI83**".ToCharArray()); break;
                                case Binary.TI82:
                                    BW.Write("**TI82**".ToCharArray()); break;
                                case Binary.TI86:
                                    BW.Write("**TI86**".ToCharArray()); break;
                                case Binary.TI85:
                                    BW.Write("**TI85**".ToCharArray()); break;
                                case Binary.TI73:
                                    BW.Write("**TI73**".ToCharArray()); break;

                            }

                            // 3 byte signature

                            BW.Write((byte)0x1A);
                            BW.Write(BinaryType == Binary.TI85 ? (byte)0x0C : (byte)0x0A);
                            BW.Write((byte)0x00);

                            // 42-byte comment

                            BW.Write(("Generated by Brass " + Assembly.GetExecutingAssembly().GetName().Version.ToString()).PadRight(42, (char)0x00).ToCharArray());

                            #region Forming variable entry

                            // Build up the variable entry:

                            ArrayList FormattedVariable = new ArrayList();

                            int VariableNameLength = 8;

                            switch (BinaryType) {
                                case Binary.TI8X:
                                    FormattedVariable.Add(0x0D);
                                    FormattedVariable.Add(0x00);
                                    break;
                                case Binary.TI82:
                                case Binary.TI83:
                                case Binary.TI73:
                                    FormattedVariable.Add(0x0B);
                                    FormattedVariable.Add(0x00);
                                    break;
                                case Binary.TI86:
                                    FormattedVariable.Add(0x0C);
                                    FormattedVariable.Add(0x00);
                                    break;
                                case Binary.TI85:
                                    FormattedVariable.Add((byte)(Math.Min(VariableNameLength, VariableName.Length) + 4));
                                    FormattedVariable.Add(0x00);
                                    break;
                            }


                            if (VariableName.Length > VariableNameLength) {
                                DisplayError(ErrorType.Warning, "Variable name '" + VariableName + "' has been truncated to " + VariableNameLength + " characters.");
                            }

                            // Total size of the data
                            int TotalSize = (BinaryEndLocation - BinaryStartLocation) + 1;

                            int HeaderSize = 2;

                            FormattedVariable.Add((TotalSize + HeaderSize) & 0xFF);
                            FormattedVariable.Add((TotalSize + HeaderSize) >> 8);

                            // Type ID byte

                            FormattedVariable.Add(TIVariableType);

                            // Format variable name

                            if (BinaryType == Binary.TI86 || BinaryType == Binary.TI85) {
                                FormattedVariable.Add((byte)(Math.Min(VariableNameLength, VariableName.Length)));
                                for (int i = 0; i < Math.Min(VariableNameLength, VariableName.Length); i++) {
                                    FormattedVariable.Add(VariableName[i]);
                                }
                                if (BinaryType == Binary.TI86) {
                                    for (int i = VariableName.Length; i < VariableNameLength; ++i) {
                                        FormattedVariable.Add((byte)' ');
                                    }
                                }

                            } else {
                                VariableName = VariableName.PadRight(VariableNameLength, (char)0x00);
                                for (int i = 0; i < VariableNameLength; i++) {
                                    FormattedVariable.Add(VariableName[i]);
                                }
                            }

                            if (BinaryType == Binary.TI8X) {
                                FormattedVariable.Add(0x00);
                                FormattedVariable.Add(0x00);
                            }
                            // Size (again)
                            FormattedVariable.Add((TotalSize + HeaderSize) & 0xFF);
                            FormattedVariable.Add((TotalSize + HeaderSize) >> 8);

                                // Program header (2 bytes for size):
                                FormattedVariable.Add(TotalSize & 0xFF);
                                FormattedVariable.Add(TotalSize >> 8);

                            // Write the binary itself
                            for (int i = BinaryStartLocation; i <= BinaryEndLocation; ++i) {
                                FormattedVariable.Add(OutputBinary[i]);
                            }

                            #endregion

                            // Write size

                            BW.Write((byte)(FormattedVariable.Count & 0xFF));
                            BW.Write((byte)(FormattedVariable.Count >> 8));

                            ushort CheckSum = 0;
                            for (int i = 0; i < FormattedVariable.Count; i++) {
                                byte b = (byte)(Convert.ToByte(FormattedVariable[i]));
                                CheckSum += b;
                                BW.Write(b);
                            }

                            BW.Write((byte)(CheckSum & 0xFF));
                            BW.Write((byte)(CheckSum >> 8));
                        }
                        break;
                    case Binary.Intel:
                    case Binary.IntelWord:
                    case Binary.MOS:
                    case Binary.Motorola:

                        ArrayList FileChunks = new ArrayList();
                        BinaryRecord B = new BinaryRecord();
                        B.StartAddress = BinaryStartLocation;
                        bool LastRecordHasBeenFlushed = true;
                        for (int i = BinaryStartLocation; i <= BinaryEndLocation; ++i) {
                            if (HasBeenOutput[i] != 0) {
                                LastRecordHasBeenFlushed = false;
                                B.Data.Add(OutputBinary[i]);
                            }

                            if (B.Data.Count == 0x18) {
                                FileChunks.Add(B);
                                B = new BinaryRecord();
                                B.StartAddress = i + 1;
                                LastRecordHasBeenFlushed = true;
                            } else if (HasBeenOutput[i] == 0) {
                                FileChunks.Add(B);
                                while (i <= BinaryEndLocation && HasBeenOutput[i] == 0) ++i;
                                B = new BinaryRecord();
                                B.StartAddress = i;                              
                                LastRecordHasBeenFlushed = true;
                            }
                        }
                        if (!LastRecordHasBeenFlushed) FileChunks.Add(B);
                        


                        using (TextWriter T = new StreamWriter(BinaryFile, false)) {
                            foreach (BinaryRecord BR in FileChunks) {
                                int Checksum = 0;
                                string DataBytes = "";
                                foreach (byte DB in BR.Data) {
                                    DataBytes += DB.ToString("X2");
                                    Checksum += (int)DB;
                                }
                                Checksum += BR.Data.Count + (BR.StartAddress & 0xFF) + (BR.StartAddress >> 8);

                                switch (BinaryType) {
                                    case Binary.Intel:
                                    case Binary.IntelWord:
                                        Checksum = (-Checksum) & 0xFF;
                                        T.WriteLine(":{0}{1}00{2}{3}", BR.Data.Count.ToString("X2"), ((BinaryType == Binary.Intel ? BR.StartAddress : BR.StartAddress >> 1) & 0xFFFF).ToString("X4"), DataBytes, Checksum.ToString("X2"));
                                        break;
                                    case Binary.MOS:
                                        Checksum &= 0xFFFF;
                                        T.WriteLine(";{0}{1}{2}{3}", BR.Data.Count.ToString("X2"), (BR.StartAddress & 0xFFFF).ToString("X4"), DataBytes, Checksum.ToString("X4"));
                                        break;
                                    case Binary.Motorola:
                                        Checksum = ~(Checksum + 3) & 0xFF;
                                        T.WriteLine("S1{0}{1}{2}{3}", ((int)(BR.Data.Count+3)).ToString("X2"), (BR.StartAddress & 0xFFFF).ToString("X4"), DataBytes, Checksum.ToString("X2"));
                                        break;
                                }
                            }
                            switch (BinaryType) {
                                case Binary.Intel:
                                case Binary.IntelWord:
                                    T.WriteLine(":00000001FF");
                                    break;
                                case Binary.MOS:
                                    T.WriteLine(";00");
                                    break;
                                case Binary.Motorola:
                                    T.WriteLine("S9030000FC");
                                    break;
                            }
                            
                                                        
                        }
                        break;
                }
            } catch (Exception ex) {
                DisplayError(ErrorType.Error, "Could not write output file: " + ex.Message);
                return false;
            }
            return true;
        }
    }
}
