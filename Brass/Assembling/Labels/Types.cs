using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {

        public interface IType {

            DataType Type { get; }
            
            string OutputName { get; }

            int Size { get; }

            double Cast(double Value);

            byte[] ByteRepresentation(double Value);

        }

        
        public class IntegerType : IType {
            #region Integer type

            public readonly bool Unsigned;

            public IntegerType(int Size, bool Unsigned) {
                this.size = Size;
                this.Unsigned = Unsigned;
            }

            int size;
            public int Size {
                get { return this.size; }
            }

            public DataType Type { get { return DataType.Int; } }

            public double Cast(double Value) {
                double MaxSize = Math.Pow(2.0d, this.size * 8.0d);
                while (Value < 0.0d) Value += MaxSize;
                if (!Unsigned && Value >= MaxSize / 2.0d) {
                    return Math.Ceiling(Value - MaxSize);
                } else {
                    return Math.Floor(Value);
                }
            }

            public byte[] ByteRepresentation(double Value) {
                byte[] Output = new byte[this.size];
                double RV = Value;
                double MaxSize = Math.Pow(2.0d, this.size * 8.0d);
                while (Value < 0.0d) Value += MaxSize;
                for (int i = 0; i < this.size; ++i) {
                    Output[i] = (byte)Value;
                    Value /= 256.0d;
                }
                return Output;
            }

            public string OutputName {
                get {
                    string SignedOrNot = this.Unsigned ? "" : "u";
                    switch (Size) {
                        case 1:
                            return SignedOrNot + "byte";
                        case 2:
                            return SignedOrNot + "word";
                        case 4:
                            return SignedOrNot + "int";
                        default:
                            return SignedOrNot + (1 << Size);
                    }
                }
            }
            #endregion
        }

        public class StructureType : IType {
            #region Structure
            public Struct Structure;
            public StructureType(Struct Structure) {
                this.Structure = Structure;
            }
            public int Size {
                get { return Structure.Size; }
            }
            public double Cast(double Value) {
                throw new Exception("Invalid cast.");
            }

            public DataType Type {
                get { return DataType.Structure; }
            }

            public string OutputName {
                get { return Structure.Name; }
            }

            public byte[] ByteRepresentation(double Value) {
                throw new Exception("You may not inline structures.");
            }
            #endregion
        }

        public class FixedPointValue : IType {
            #region Fixed-point
            public FixedPointInfo Information;
            public int Size {
                get {
                    return Information.SizeInBytes;
                }
            }
            public FixedPointValue(FixedPointInfo Information) {
                this.Information = Information;
            }
            public double Cast(double Value) {
                double Scale = Math.Pow(2.0, Information.FractionSize);
                double FixedPointValue = Value * Scale;
                IntegerType I = new IntegerType(this.Information.SizeInBytes, this.Information.Unsigned);
                return I.Cast(FixedPointValue);

            }
            public string OutputName {
                get {
                    return (Information.Unsigned ? "u" : "") + "fp" + Information.ValueSize + "." + Information.FractionSize;
                }
            }

            public DataType Type {
                get { return DataType.FixedPoint; }
            }

            public byte[] ByteRepresentation(double Value) {
                byte[] Output = new byte[this.Size];
                double RV = Cast(Value);
                double MaxSize = Math.Pow(2.0d, this.Size * 8.0d);
                while (RV < 0.0d) RV += MaxSize;
                for (int i = 0; i < this.Size; ++i) {
                    Output[i] = (byte)RV;
                    RV /= 256.0d;
                }
                return Output;
            }
            #endregion
        }

        public class ByteBlock : IType {
            #region Byte block
            public int Size {
                get { return size; }
            }
            int size;
            public ByteBlock(int Size) {
                this.size = Size;
            }
            public string OutputName {
                get {
                    return Size.ToString();
                }
            }

            public DataType Type {
                get { return DataType.ByteBlock; }
            }

            public double Cast(double Value) {
                throw new Exception("Invalid cast.");
            }

            public byte[] ByteRepresentation(double Value) {
                throw new Exception("You may not use byte blocks in this manner (use a sequence of bytes instead).");
            }
            #endregion
        }

        public class AsciiChar : IType {
            #region ASCII character

            public DataType Type {
                get { return DataType.Ascii; }
            }

            public string OutputName {
                get { return "asc"; }
            }

            public int Size {
                get { return 1; }
            }

            public double Cast(double Value) {
                return TranslateChar((char)Value);
            }

            public byte[] ByteRepresentation(double Value) {
                return new byte[] { TranslateChar((char)Value) };
            }


            private byte TranslateChar(char toTranslate) {
                byte TryTranslate;
                if (ASCIITable.TryGetValue(toTranslate, out TryTranslate)) {
                    return TryTranslate;
                } else {
                    return (byte)toTranslate;
                }
            }

            #endregion
        }
        
        public class TiFloat : IType {
            #region TI floating-point value
            public DataType Type {
                get { return DataType.TiFloat; }
            }

            public string OutputName {
                get { return "tifloat"; }
            }

            public int Size {
                get { return 9; }
            }

            public double Cast(double Value) {
                throw new Exception("Invalid cast.");
            }

            public byte[] ByteRepresentation(double Value) {
                byte[] Return = new byte[Size];

                string[] SplitParts = Value.ToString("." + "".PadRight((Size - 2) * 2, '#') + "E+00", InvariantCulture).TrimStart('-').Split('E');
                if (SplitParts.Length!=2) throw new Exception("Could not convert " + Value + " to a TI floating-point value.");

                Return[0] = (Value < 0.0d) ? (byte)0x80 : (byte)0x00;

                string Mantissa = SplitParts[0].TrimStart('.').PadRight((Size - 2) * 2, '0');
                Return[1] = (byte)(0x80 + int.Parse(SplitParts[1], InvariantCulture) - 1);

                for (int i = 2; i < Size; ++i) {
                    Return[i] = (byte)((Mantissa[(i - 2) * 2 + 1] - '0') + (Mantissa[(i - 2) * 2 + 0] - '0') * 16);
                }
                
                return Return;
            }
            #endregion
        }

        public enum DataType {
            Constant,
            Unspecified,
            ByteBlock,
            Ascii,
            Int,
            FixedPoint,
            Structure,
            TiFloat
        }

        public class FixedPointInfo {
            public readonly int ValueSize;
            public readonly int FractionSize;
            public readonly bool Unsigned;
            
            public int SizeInBytes {
                get {
                    return (ValueSize + FractionSize - 1) / 8 + 1;
                }
            }

            public FixedPointInfo(int ValueSize, int FractionSize, bool Unsigned) {
                this.ValueSize = ValueSize;
                this.FractionSize = FractionSize;
                this.Unsigned = Unsigned;
            }

        }

        public static double Cast(double Original, IType Format) {
            return Format.Cast(Original);
        }


        public static bool TryGetTypeInformation(string TypeDeclaration, out IType Type) {
            return TryGetTypeInformation(TypeDeclaration, out Type, false);
        }
        public static bool TryGetTypeInformation(string TypeDeclaration, out IType Type, bool CheckingExisting) {
            switch (TypeDeclaration.ToLower()) {
                case "db":
                case "byte":
                case "1":
                    Type = new IntegerType(1, true);
                    return true;
                case "dw":
                case "word":
                case "2":
                    Type = new IntegerType(2, true);
                    return true;
                case "asc":
                    Type = new AsciiChar();
                    return true;
                case "ubyte":
                    Type = new IntegerType(1, false);
                    return true;
                case "uword":
                    Type = new IntegerType(2, false);
                    return true;
                case "int":
                case "4":
                    Type = new IntegerType(4, false);
                    return true;
                case "uint":
                    Type = new IntegerType(4, true);
                    return true;
                case "tifloat":
                    Type = new TiFloat();
                    return true;
            }

            FixedPointInfo Fp;
            if (TryGetFixedPointData(TypeDeclaration, out Fp)) {
                Type = new FixedPointValue(Fp);
                return true;
            }

            Struct St;
            if (Structs.TryGetValue(IsCaseSensitive ? TypeDeclaration : TypeDeclaration.ToLower(), out St)) {
                Type = new StructureType(St);
                return true;
            }
            if (CheckingExisting) {
                Type = null;
                return false;
            }
            try {
                Type = new ByteBlock(IntEvaluate(TypeDeclaration));
                return true;
            } catch {
                Type = null;
                return false;
            }

        }


        public static bool TryGetFixedPointData(string TypeDeclaration, out FixedPointInfo Info) {
            
            Info = null;

            if (TypeDeclaration.Length == 0) return false;
            
            bool Unsigned = false;
            
            if (TypeDeclaration[0] == 'u') {
                Unsigned = true;
                TypeDeclaration = TypeDeclaration.Substring(1);
            }

            if (TypeDeclaration.Length < 2 || TypeDeclaration[0] != 'f' || TypeDeclaration[1] != 'p') return false;

            TypeDeclaration = TypeDeclaration.Substring(2);

            string[] Sizes = TypeDeclaration.Split('.');

            if (Sizes.Length != 2) return false;

            int ValueSize, FractionSize;

            if (!int.TryParse(Sizes[0], out ValueSize) || !int.TryParse(Sizes[1], out FractionSize)) return false;

            Info = new FixedPointInfo(ValueSize, FractionSize, Unsigned);

            return true;
        }

    }
}
