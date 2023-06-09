using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {

        private static byte BCD(byte Value) {
            int Tens = Value / 10;
            Value -= (byte)(Tens * 10);
            return (byte)((Tens << 4) + Value);
        }
        private static ushort BCD(ushort Value) {
            int Thousands = Value / 1000;
            Value -= (ushort)(Thousands * 1000);
            int Hundreds = Value / 100;
            Value -= (ushort)(Hundreds * 100);
            int Tens = Value / 10;
            Value -= (ushort)(Tens * 10);
            return (ushort)((Thousands << 12) + (Hundreds << 8) + (Tens << 4) + Value);
        }

        private static bool HasSdscTag = false;

        private static byte SdscMajorVersionNumber = 0;
        private static byte SdscMinorVersionNumber = 0;

        private enum Region { Japan, Export, International };
        private static Region SegaRegion = Region.Export;

        private static int SegaPartNumber = 0;
        private static int SegaVersion = 0;

        private class SdscString {
            public string Value = "";
            public ushort Pointer = 0x0000;
            public bool PointerCreated = false; 
        }

        private static SdscString SdscTitle = new SdscString();
        private static SdscString SdscDescription = new SdscString();
        private static SdscString SdscAuthor = new SdscString();



        private static void GenerateSegaHeaders(ref OutputByte[] OutputBinary) {

            // SDSC Header

            int CheckHeaderOverwritten = 0;

            if (HasSdscTag) {                
                for (int i = 0x7FE0; i < 0x7FF0; ++i) {
                    CheckHeaderOverwritten += OutputBinary[i].WriteCount;
                    ++OutputBinary[i].WriteCount;
                }
                if (CheckHeaderOverwritten != 0) DisplayError(ErrorType.Warning, "SDSC tag overwrites existing code (keep code out of $7FE0-$7FEF region or disable SDSC tag).");

                OutputBinary[0x7FE0].Data = (byte)'S';
                OutputBinary[0x7FE1].Data = (byte)'D';
                OutputBinary[0x7FE2].Data = (byte)'S';
                OutputBinary[0x7FE3].Data = (byte)'C';

                OutputBinary[0x7FE4].Data = BCD(SdscMajorVersionNumber);
                OutputBinary[0x7FE5].Data = BCD(SdscMinorVersionNumber);

                DateTime Timestamp = DateTime.Now;
                ushort Year = BCD((ushort)Timestamp.Year);

                OutputBinary[0x7FE6].Data = BCD((byte)Timestamp.Day);
                OutputBinary[0x7FE7].Data = BCD((byte)Timestamp.Month);
                OutputBinary[0x7FE8].Data = (byte)(Year & 0xFF);
                OutputBinary[0x7FE9].Data = (byte)(Year >> 8);

                // Now we need to find free ROM areas for the tags

                for (int i = 0; i < 3; i++) {
                    SdscString ToPlace = null;
                    switch (i) {
                        case 0: ToPlace = SdscAuthor; break;
                        case 1: ToPlace = SdscTitle; break;
                        case 2: ToPlace = SdscDescription; break;
                    }
                    if (!ToPlace.PointerCreated) {
                        // We need to find a place to slip the tag.
                        ToPlace.Pointer = 0xFFFF;
                        bool CountingEmpties = false;
                        for (int j = 0; !ToPlace.PointerCreated && j < 0xFFFF - (ToPlace.Value.Length + 1); ++j) {
                            if (OutputBinary[j].WriteCount == 0) {
                                if (!CountingEmpties) {
                                    CountingEmpties = true;
                                    ToPlace.Pointer = (ushort)j;
                                } else {
                                    if (j - ToPlace.Pointer == ToPlace.Value.Length + 1) {
                                        for (int k = 0; k < ToPlace.Value.Length; k++) {
                                            OutputBinary[ToPlace.Pointer + k].Data = (byte)ToPlace.Value[k];
                                            ++OutputBinary[ToPlace.Pointer + k].WriteCount;
                                        }
                                        OutputBinary[ToPlace.Pointer + ToPlace.Value.Length].Data = 0x00;
                                        ++OutputBinary[ToPlace.Pointer + ToPlace.Value.Length].WriteCount;
                                        ToPlace.PointerCreated = true;
                                    }
                                }
                            } else {
                                CountingEmpties = false;
                            }
                        }
                        if (ToPlace.Pointer == 0xFFFF) {
                            string TagType = "";
                            switch (i) {
                                case 0: TagType = "author"; break;
                                case 1: TagType = "title"; break;
                                case 2: TagType = "description"; break;
                            }
                            DisplayError(ErrorType.Warning, "SDSC " + TagType + " tag could not fit onto the ROM, so has been omitted.");
                        }
                    } 
                    OutputBinary[0x7FEA + i * 2].Data = (byte)(ToPlace.Pointer & 0xFF);
                    OutputBinary[0x7FEB + i * 2].Data = (byte)(ToPlace.Pointer >> 0x8);
  
                }
            }

            CheckHeaderOverwritten = 0;
            for (int i = 0x7FF0; i < 0x8000; ++i) {
                CheckHeaderOverwritten += OutputBinary[i].WriteCount;
                ++OutputBinary[i].WriteCount;
            }
            if (CheckHeaderOverwritten != 0) DisplayError(ErrorType.Warning, "Sega ROM header overwrites existing code (keep code out of $7FF0-$7FFF region!)");

            // Official SEGA ROM header

            OutputBinary[0x7FF0].Data = (byte)'T';
            OutputBinary[0x7FF1].Data = (byte)'M';
            OutputBinary[0x7FF2].Data = (byte)'R';
            OutputBinary[0x7FF3].Data = (byte)' ';
            OutputBinary[0x7FF4].Data = (byte)'S';
            OutputBinary[0x7FF5].Data = (byte)'E';
            OutputBinary[0x7FF6].Data = (byte)'G';
            OutputBinary[0x7FF7].Data = (byte)'A';

            // Unknown
            OutputBinary[0x7FF8].Data = 0xFF;
            OutputBinary[0x7FF9].Data = 0xFF;

            // Checksum

            int ChecksumRange = Math.Min(0x40000, OutputBinary.Length); // Limit to 256KB
            if (ChecksumRange < 0x8000) ChecksumRange = 0x8000;
            if (ChecksumRange > 0x8000 && ChecksumRange < 0x20000) ChecksumRange = 0x8000; // 32KB
            if (ChecksumRange > 0x20000 && ChecksumRange < 0x40000) ChecksumRange = 0x40000; // 128KB

            ushort ChecksumValue = 0x0000;
            for (int i = 0; i < ChecksumRange; ++i) {
                if (i < 0x7FF0 || i >= 0x8000) ChecksumValue += OutputBinary[i].Data;
            }

            OutputBinary[0x7FFA].Data = (byte)(ChecksumValue >> 0x8);
            OutputBinary[0x7FFB].Data = (byte)(ChecksumValue & 0xFF);

            // Part number and version

            int[] ExpandedVersionNumber = new int[6];

            if (SegaPartNumber >= 160000) {
                SegaPartNumber = 159999;
                DisplayError(ErrorType.Warning, "Sega part number must be between 0 and 159999 (inclusive).");
            } else if (SegaPartNumber < 0) {
                DisplayError(ErrorType.Warning, "Sega part number must be between 0 and 159999 (inclusive).");
                SegaPartNumber = 0;
            }

            if (SegaVersion < 0 || SegaVersion > 0xF) {
                DisplayError(ErrorType.Warning, "Sega version number must be between 0 and 15 (inclusive).");
                SegaVersion = (SegaVersion < 0) ? 0 : 15;
            }

            int Divisor = 100000;
            for (int i = 0; i < 6; ++i) {
                ExpandedVersionNumber[i] = SegaPartNumber / Divisor;
                SegaPartNumber -= ExpandedVersionNumber[i] * Divisor;
                Divisor /= 10;
            }

            OutputBinary[0x7FFC].Data = (byte)((ExpandedVersionNumber[4] << 4) | (ExpandedVersionNumber[5]));
            OutputBinary[0x7FFD].Data = (byte)((ExpandedVersionNumber[2] << 4) | (ExpandedVersionNumber[3]));
            OutputBinary[0x7FFE].Data = (byte)(((ExpandedVersionNumber[0] * 10 + ExpandedVersionNumber[1]) << 4) | (SegaVersion));

            // Checksum range
            switch (ChecksumRange) {
                case 0x8000:
                    OutputBinary[0x7FFF].Data = 0x0C;
                    break;
                case 0x20000:
                    OutputBinary[0x7FFF].Data = 0x0F;
                    break;
                case 0x40000:
                    OutputBinary[0x7FFF].Data = 0x00;
                    break;
            }

            // Model/region:

            switch (BinaryType) {
                case Binary.SegaMS:
                    switch (SegaRegion) {
                        case Region.Japan:
                            OutputBinary[0x7FFF].Data |= 0x30;
                            break;
                        case Region.Export:
                            OutputBinary[0x7FFF].Data |= 0x40;
                            break;
                        case Region.International:
                            OutputBinary[0x7FFF].Data |= 0x40;
                            DisplayError(ErrorType.Warning, "'International' region invalid for Sega Master System ROMs (amended to 'Export').");
                            break;
                    }
                    break;
                case Binary.SegaGG:
                    switch (SegaRegion) {
                        case Region.Japan:
                            OutputBinary[0x7FFF].Data |= 0x50;
                            break;
                        case Region.Export:
                            OutputBinary[0x7FFF].Data |= 0x60;
                            break;
                        case Region.International:
                            OutputBinary[0x7FFF].Data |= 0x70;
                            break;
                    }
                    break;
            }
            BinaryType = Binary.Raw;
        }
    }
}
