using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;

namespace Brass {
    public partial class Program {

        public static bool Page0Defined = false;

        // Binary types:
        public enum Binary {
            Raw,                                    // Plain raw COM binary
            TI8X, TI83, TI82, TI86, TI85, TI73,     // TI headered binary
            TI8XApp, TI73App,                       // TI-83+/73 app
            Intel, IntelWord, MOS, Motorola,        // Hex file format
            SegaMS, SegaGG,                         // Master System / Game Gear
        }

        public class BinaryRecord {
            public List<byte> Data = new List<byte>();
            public uint StartAddress = 0x0000;
        }

        public static Binary BinaryType = Binary.Raw;   // Current output (defaults to Raw).

        // Variable name (if applicable)
        public static string VariableName = "";

        // Variable type for TI binaries.
        public static int TIVariableType = 0;
        public static bool TIVariableTypeSet = false;
        public static bool TIVariableArchived = false;  



        public static bool CanStillDefinePage0;

        public class OutputByte {
            public byte Data = 0;
            public int WriteCount = 0;
            public bool Squished = true;
            public byte EmptyFill = 0xFF;

            public OutputByte(byte Data) {
                this.Data = Data;
                this.Squished = SquishedData;
                this.EmptyFill = BinaryFillChar;
            }
            public OutputByte(byte Data, bool Squished) {
                this.Data = Data;
                this.Squished = Squished;
                this.EmptyFill = BinaryFillChar;
            }
        }
        public class BinaryPage {
            public OutputByte[] OutputBinary; // Stores the output stream of bytes.
            public uint ProgramCounter;       // Program counter for this page (each page has a different one)
            public uint StartAddress;         // Position inside output file
            public uint BinaryStartLocation;
            public uint BinaryEndLocation;
            public uint Size;
            public uint Page;
            public uint InitialProgramCounter;

            void StartInit() {
                BinaryEndLocation = 0x0000;
                BinaryStartLocation = 0xFFFF;
                StartAddress = 0x0000;
                ProgramCounter = 0x0000;
                InitialProgramCounter = 0x0000;
                Size = 0x10000;
                Page = 0;
            }

            void FinishInit() {
                for (int i = 0; i < OutputBinary.Length; ++i) {
                    OutputBinary[i] = new OutputByte(BinaryFillChar);
                }
            }

            public void FixEmptyFill() {
                foreach (OutputByte b in OutputBinary) if (b.WriteCount == 0)  b.Data = b.EmptyFill;
            }

            public BinaryPage() {
                StartInit();
                this.OutputBinary = new OutputByte[0x10000];
                FinishInit();
            }
            public BinaryPage(uint Page, uint Size, uint ProgramCounter) {
                StartInit();
                this.Page = Page;
                this.StartAddress = ProgramCounter;
                this.Size = Size;
                this.OutputBinary = new OutputByte[Size];
                this.BinaryStartLocation = Size - 1;
                this.ProgramCounter = ProgramCounter;
                this.InitialProgramCounter = ProgramCounter;
                FinishInit();
            }
        }

        public static Dictionary<uint, BinaryPage> Pages;

        // Page information
        public static BinaryPage CurrentPage;

        public static bool WriteToBinary(byte[] bytes) {

            //OutputAddresses.Add(new OutputAddress((uint)(CurrentPage.ProgramCounter + RelocationOffset), CurrentPage.Page, CurrentFilename, CurrentLineNumber));

            foreach (byte B in bytes) {
                if (!WriteToBinary(B)) return false;                
            }
            
            return true;
        }

        /// <summary>
        /// Write a byte to the output binary and flag it as written to
        /// </summary>
        /// <param name="ByteToWrite">Byte value to output</param>
        /// <returns>Success</returns>

        public static bool WriteToBinary(byte ByteToWrite) {


            if (WriteListFile) OutputAddresses.Add(new OutputAddress((uint)(CurrentPage.ProgramCounter + RelocationOffset), CurrentPage.Page, CurrentFilename, CurrentLineNumber, ByteToWrite));

            if (CurrentPage.ProgramCounter < 0) {
                DisplayError(ErrorType.Error, "Brass cannot assemble binaries which start before memory address 0: Data truncated.");
                ++CurrentPage.ProgramCounter;
                return false;
            }
            if (CurrentPage.ProgramCounter > 0xFFFF) {
                DisplayError(ErrorType.Warning, "Data overflows 64KB page limit.");
                CurrentPage.ProgramCounter &= 0xFFFF;
            }
            if (CurrentPage.Page == 0) CanStillDefinePage0 = false;

            uint AddressInPage = CurrentPage.ProgramCounter - CurrentPage.InitialProgramCounter;


            if (AddressInPage < 0 || AddressInPage >= CurrentPage.OutputBinary.Length) {
                DisplayError(ErrorType.Error, "Attempting to output beyond page boundaries.");
                return false;
            }

            if (AddressInPage < CurrentPage.BinaryStartLocation) CurrentPage.BinaryStartLocation = AddressInPage;
            if (AddressInPage > CurrentPage.BinaryEndLocation) CurrentPage.BinaryEndLocation = AddressInPage;
            
            CurrentPage.OutputBinary[AddressInPage].Data = ByteToWrite;
            ++CurrentPage.OutputBinary[AddressInPage].WriteCount;
            ++CurrentPage.ProgramCounter;
            return true;
        }

        /// <summary>
        /// Write the binary out.
        /// </summary>
        /// <param name="BinaryFile">Filename of the binary to write to.</param>
        /// <returns>Success</returns>
        public static bool WriteBinary(string BinaryFile) {

            Binary OldType = BinaryType;

            try {

                string HexChars = "0123456789ABCDEF";

                if (File.Exists(BinaryFile)) File.Delete(BinaryFile);

                foreach (KeyValuePair<uint, BinaryPage> KVP in Pages) {
                    KVP.Value.FixEmptyFill();
                    if (!(KVP.Key == 0 && !Page0Defined)) {
                        KVP.Value.BinaryStartLocation = 0;
                        KVP.Value.BinaryEndLocation = KVP.Value.Size - 1;
                    }
                }


                // Add the app header to page 0 for TI apps
                if (BinaryType == Binary.TI8XApp || BinaryType == Binary.TI73App) {
                    bool Page0Exists = false;
                    bool PageError = false;
                    foreach (KeyValuePair<uint, BinaryPage> P in Pages) {
                        if (P.Key == 0) Page0Exists = true;
                        if (P.Value.Size != 0x4000) {
                            DisplayError(ErrorType.Error, "Page " + P.Key + " needs to be 16KB.");
                            PageError = true;
                        }
                    }

                    uint PageOrder = 0;
                    foreach (KeyValuePair<uint, BinaryPage> KVP in Pages) {
                        if (KVP.Key != KVP.Value.Page || KVP.Key != PageOrder++) {
                            DisplayError(ErrorType.Error, "Pages are out of order - you must define pages in order (0,1,2,3,...)");
                            PageError = true;
                            break;
                        }
                    }

                    if (PageError) return false;

                    if (!Page0Exists) {
                        DisplayError(ErrorType.Error, "Page 0 missing - header can't be added.");
                        return false;
                    }
                    byte[] Header = CreateAppHeader();
                    BinaryPage Page0 = Pages[0];
                    
                    if (Page0.OutputBinary.Length < 128) {
                        DisplayError(ErrorType.Error, "Page 0 smaller than 128 bytes: header can't be added.");
                        return false;
                    }
                    bool OverwrittenPage0 = false;
                    for (int i = 0; i < 128; ++i) {
                        if (Page0.OutputBinary[i].WriteCount != 0) {
                            OverwrittenPage0 = true;
                        }
                        Page0.OutputBinary[i].Squished = true;
                        Page0.OutputBinary[i].Data = Header[i];
                        Page0.OutputBinary[i].WriteCount++;
                    }
                    if (OverwrittenPage0) {
                        DisplayError(ErrorType.Error, "Application header overwrites part of your code on page 0 - try .org $4000+128.");
                    }
                    StartOutputAddress = 0;
                    EndOutputAddress = (uint)(Pages.Count * 0x4000 - 1);
                    BinaryType = Binary.Intel;
                }


                // Calculate the TOTAL SIZE.
                uint TotalBinarySize = 0;
                foreach (KeyValuePair<uint, BinaryPage> KVP in Pages) {
                    BinaryPage ToAdd = KVP.Value;
                    if (ToAdd.StartAddress + ToAdd.Size > TotalBinarySize) {
                        TotalBinarySize = ToAdd.StartAddress + ToAdd.Size;
                    }
                }
                TotalBinarySize = (uint)Math.Max(TotalBinarySize, 1 + EndOutputAddress - StartOutputAddress);

                OutputByte[] DecodeOutputBinary = new OutputByte[TotalBinarySize];

                for (int i = 0; i < TotalBinarySize; ++i) {
                    DecodeOutputBinary[i] = new OutputByte(BinaryFillChar);                    
                }

                uint BinaryStartLocation = TotalBinarySize;
                uint BinaryEndLocation = 0;
                
                
                foreach (KeyValuePair<uint, BinaryPage> KVP in Pages) {
                    BinaryPage ToAdd = KVP.Value;
                    for (uint i = ToAdd.BinaryStartLocation; i <= ToAdd.BinaryEndLocation; i++) {
                        uint j = i + ToAdd.StartAddress;
                        if (j < BinaryStartLocation) BinaryStartLocation = j;
                        if (j > BinaryEndLocation) BinaryEndLocation = j;
                        DecodeOutputBinary[j] = ToAdd.OutputBinary[i];
                    }
                }


                if (BinaryType == Binary.SegaMS || BinaryType == Binary.SegaGG) GenerateSegaHeaders(ref DecodeOutputBinary);

                List<byte> ExpandOutputBinary = new List<byte>(DecodeOutputBinary.Length);
                List<bool> ExpandHasBeenOutput = new List<bool>(DecodeOutputBinary.Length);
                for (uint i = BinaryStartLocation; i <= BinaryEndLocation; ++i) {
                    OutputByte o = DecodeOutputBinary[i];
                    if (o.Squished) {
                        ExpandOutputBinary.Add(o.Data);
                        ExpandHasBeenOutput.Add(o.WriteCount > 0);
                    } else {
                        ExpandOutputBinary.Add((byte)HexChars[o.Data >> 04]);
                        ExpandOutputBinary.Add((byte)HexChars[o.Data & 0xF]);
                        ExpandHasBeenOutput.Add(o.WriteCount > 0);
                        ExpandHasBeenOutput.Add(o.WriteCount > 0);
                    }
                }
                byte[] OutputBinary = ExpandOutputBinary.ToArray();
                bool[] HasBeenOutput = ExpandHasBeenOutput.ToArray();

                switch (BinaryType) {
                    case Binary.Raw:
                        // Just dump a plain binary:
                        using (BinaryWriter BW = new BinaryWriter(new FileStream(BinaryFile, FileMode.OpenOrCreate), Encoding.ASCII)) {
                            BW.Write(OutputBinary);
                        }
                        break;
                    case Binary.TI83:
                    case Binary.TI8X:
                    case Binary.TI82:
                    case Binary.TI86:
                    case Binary.TI85:
                    case Binary.TI73:
                        #region TI variable wrapping
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

                            List<byte> FormattedVariable = new List<byte>();

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
                            if (VariableName.ToUpper() != VariableName) {
                                DisplayError(ErrorType.Warning, "The TIOS gets confused with variable names containing lowercase characters ('" + VariableName + "').");
                            }

                            // Total size of the data
                            int TotalSize = OutputBinary.Length; // (BinaryEndLocation - BinaryStartLocation) + 1;
                           
                            int HeaderSize = 2;

                            FormattedVariable.Add((byte)((TotalSize + HeaderSize) & 0xFF));
                            FormattedVariable.Add((byte)((TotalSize + HeaderSize) >> 8));

                            // Type ID byte
                            FormattedVariable.Add((byte)TIVariableType);

                            // Format variable name

                            if (BinaryType == Binary.TI86 || BinaryType == Binary.TI85) {
                                FormattedVariable.Add((byte)(Math.Min(VariableNameLength, VariableName.Length)));
                                for (int i = 0; i < Math.Min(VariableNameLength, VariableName.Length); i++) {
                                    FormattedVariable.Add((byte)VariableName[i]);
                                }
                                if (BinaryType == Binary.TI86) {
                                    for (int i = VariableName.Length; i < VariableNameLength; ++i) {
                                        FormattedVariable.Add((byte)' ');
                                    }
                                }

                            } else {
                                VariableName = VariableName.PadRight(VariableNameLength, (char)0x00);
                                for (int i = 0; i < VariableNameLength; i++) {
                                    FormattedVariable.Add((byte)VariableName[i]);
                                }
                            }

                            if (BinaryType == Binary.TI8X) {
                                FormattedVariable.Add((byte)(TIVariableArchived ? 0x80 : 0x00));
                                FormattedVariable.Add(0x00);
                            }

                            // Size (again)
                            FormattedVariable.Add((byte)((TotalSize + HeaderSize) & 0xFF));
                            FormattedVariable.Add((byte)((TotalSize + HeaderSize) >> 8));

                            // Program header (2 bytes for size):
                            FormattedVariable.Add((byte)(TotalSize & 0xFF));
                            FormattedVariable.Add((byte)(TotalSize >> 8));

                            // Write the binary itself

                            for (int i = 0; i < OutputBinary.Length; ++i) {
                                FormattedVariable.Add(OutputBinary[i]);
                            }
                
                            #endregion

                            // Write size

                            BW.Write((byte)(FormattedVariable.Count & 0xFF));
                            BW.Write((byte)(FormattedVariable.Count >> 8));

                            ushort CheckSum = 0;
                            for (int i = 0; i < FormattedVariable.Count; i++) {
                                byte b = FormattedVariable[i];
                                CheckSum += b;
                            }
                            BW.Write(FormattedVariable.ToArray());

                            BW.Write((byte)(CheckSum & 0xFF));
                            BW.Write((byte)(CheckSum >> 8));
                        }
                        break;
                        #endregion
                    case Binary.Intel:
                    case Binary.IntelWord:
                    case Binary.MOS:
                    case Binary.Motorola:

                        using (TextWriter T = new StreamWriter(BinaryFile, false)) {
                            HexFileWriter H = new HexFileWriter(BinaryType, Pages.Count != 1);
                            foreach (KeyValuePair<uint, BinaryPage> p in Pages) {
                                H.WritePage(p.Value, T);
                            }
                            H.WriteEndOfFile(T);
                        }

                        /*List<BinaryRecord> FileChunks = new List<BinaryRecord>();
                        BinaryRecord B = new BinaryRecord();
                        B.StartAddress = BinaryStartLocation;
                        bool LastRecordHasBeenFlushed = true;

                        uint HasBeenOutputPointer = BinaryStartLocation;

                        for (uint i = 0; i < OutputBinary.Length; ++i) {
                            
                            LastRecordHasBeenFlushed = false;
                            B.Data.Add(OutputBinary[i]);

                            if (B.Data.Count == 0x20) {
                                FileChunks.Add(B);
                                B = new BinaryRecord();
                                B.StartAddress = i + 1 + BinaryStartLocation;
                                LastRecordHasBeenFlushed = true;
                            } else if (!HasBeenOutput[i]) {
                                FileChunks.Add(B);
                                while (i < OutputBinary.Length && !HasBeenOutput[i]) ++i;
                                B = new BinaryRecord();
                                B.StartAddress = i + BinaryStartLocation;                              
                                LastRecordHasBeenFlushed = true;
                            }
                            HasBeenOutputPointer += (uint)(DecodeOutputBinary[HasBeenOutputPointer].Squished ? 1 : 2);
                        }
                        if (!LastRecordHasBeenFlushed) FileChunks.Add(B);

                        using (TextWriter T = new StreamWriter(BinaryFile, false)) {
                            foreach (BinaryRecord BR in FileChunks) {
                                uint Checksum = 0;
                                string DataBytes = "";
                                foreach (byte DB in BR.Data) {
                                    DataBytes += DB.ToString("X2");
                                    Checksum += (uint)DB;
                                }
                                Checksum += (uint)(BR.Data.Count + (BR.StartAddress & 0xFF) + (BR.StartAddress >> 8));

                                switch (BinaryType) {
                                    case Binary.Intel:
                                    case Binary.IntelWord:
                                        Checksum = (uint)((-Checksum) & 0xFF);
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
                        break;*/



                        break;
                }

                bool CorruptedSection = false;
                uint CorruptedSectionStart = 0;
                for (uint i = 0; i < DecodeOutputBinary.Length; ++i) {
                    if (DecodeOutputBinary[i] != null && DecodeOutputBinary[i].WriteCount > 1) {
                        if (!CorruptedSection) {
                            CorruptedSectionStart = i + BinaryStartLocation;
                            CorruptedSection = true;
                        }
                    } else {
                        if (CorruptedSection) {
                            DisplayError(ErrorType.Warning, "Data overlap between $" + CorruptedSectionStart.ToString("X4") + "-$" + ((int)i - 1 + BinaryStartLocation).ToString("X4") + ".");
                        }
                        CorruptedSection = false;
                    }
                }
                if (CorruptedSection) {
                    DisplayError(ErrorType.Warning, "Data overlap between $" + CorruptedSectionStart.ToString("X4") + "-$" + BinaryEndLocation.ToString("X4") + ".");
                }

            } catch (Exception ex) {
                DisplayError(ErrorType.Error, "Could not write output file: " + ex.Message);
                return false;
            }

            if (OldType == Binary.TI73App || OldType == Binary.TI8XApp) {
                Console.WriteLine("Signing application...");
                
                try {
                    // Sign the app (where BinaryFile is the filename of the .hex file to sign)
                    AppSigner Signer = new AppSigner(AppSigner.Mode.DetectType);

                    // Get the key file
                    string KeyFile = Signer.GetKeyFile(BinaryFile);
                    if (KeyFile == "") throw new Exception("Couldn't find the key file.");

                    // Get the output filename
                    string SignedAppFilename = Signer.FormatOutput(BinaryFile);
                    if (SignedAppFilename == "") throw new Exception("Couldn't establish output filename.");

                    // Sign the bugger
                    int Signed = Signer.Sign(BinaryFile, KeyFile, SignedAppFilename);
                    if (Signed != 0) throw new Exception(Signer.GetErrorMessage(Signed));

                } catch (Exception ex) {
                    // Didn't work as it should
                    DisplayError(ErrorType.Error, "Could not sign application: " + ex.Message);
                }
            }

            return true;
        }
    }
}
