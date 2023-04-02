using System;
using System.Collections;
using System.Text;
using System.IO;

namespace Brass {
    public partial class Program {

        public static int ProgramCounter = 0;

        public static Hashtable Labels;         // Stores label names -> address mapping.
        public static Hashtable Instructions;   // Stores instructions -> Instruction class mapping (eg 'LD').
        public static Hashtable Macros;         // Stores macros.


        public static ArrayList ExportTable;    // Stores the export table.


        public static Hashtable SourceFiles;    // Loaded source files.
        public static Hashtable BinaryFiles;    // Loaded binary files.

        public static bool IsCaseSensitive = false; // Case sensitive or not?

        public static string CurrentModule;     // Current module's name.
        public static string CurrentLocalLabel; // Current local label definition.

        public static bool DebugMode = false;   // Debugmode dumps all sorts of stuff to the console.

        public enum Pass { Labels, Assembling } // Which pass are we on?

        public static Hashtable ConditionalHasBeenTrue; // Remembering if a conditional on this level has ever been true.
        public static Stack ConditionalStack;   // Stack of conditionals keeping track of true/false/FileNotFound

        public static Macro LastMacro;          // What was the last macro (so #defcont can do some magic).

        public static Hashtable ReusableLabels; // This is pure magic ;)

        public static byte[] ASCIITable;        // ASCII mapping table

        public static ArrayList AllInstructions;

        public static int VariableTable = 0;        // Variable table location
        public static int VariableTableOff = 0;     // Offset in variable table
        public static int VariableTableSize = 0;    // Size of the variable table size

        public static bool WriteListFile = false;
        public static ArrayList ListFile = new ArrayList();

        /// <summary>
        /// Adjust a label name, fixing for case and local label stuff.
        /// </summary>
        /// <param name="Name">Label name</param>
        /// <returns>Fixed label name</returns>
        public static string FixLabelName(string Name) {
            Name = IsCaseSensitive ? Name.Trim() : Name.Trim().ToLower();
            if (Name.StartsWith(CurrentLocalLabel)) Name = CurrentModule + "." + Name;
            if (Name.Length == 0) {
                throw new Exception("Nonexistant label name.");
            } else if (Name[0] >= '0' && Name[0] <= '9') {
                throw new Exception("Labels names may not start with a number ('" + Name + "').");
            }
            return Name;
        }


        public class ListFileEntry {
            public string Source;
            public int Line;
            public int Address;
            public string File;
            public byte[] Data;
            public ListFileEntry(string Source, int Line, int Address, string File, byte[] Data) {
                this.Source = Source;
                this.Line = Line;
                this.Address = Address;
                this.File = File;
                this.Data = Data;
            }

        }


        /// <summary>
        /// Adds a new label (tries to)
        /// </summary>
        /// <param name="Name">Name of the label (is adjusted)</param>
        /// <param name="Value">Value to assign</param>
        /// <param name="ForceNewLabel">Force a new label definition, even if an old one exists</param>
        /// <param name="SourceFile">Current source file</param>
        /// <param name="Line">Line number</param>
        /// <returns>True on success, false on failure.</returns>
        public static bool AddNewLabel(string Name, int Value, bool ForceNewLabel, string SourceFile, int Line) {
            try {
                Name = FixLabelName(Name);

                if (Name != "") {
                    if (Name.Replace("+", "") == "" || Name.Replace("-", "") == "") {
                        if (ReusableLabels[ProgramCounter] == null) {
                            ReusableLabels[ProgramCounter] = new Hashtable();
                        }
                        ((Hashtable)ReusableLabels[ProgramCounter])[Name] = Value;
                    } else {
                        if (!CheckLabelName(Name)) DisplayError(ErrorType.Warning, "Potentially confusing label name '" + Name + "'.", SourceFile, Line);
                        if (Labels[Name] != null && !ForceNewLabel) {
                            return false;
                        } else {
                            Labels[Name] = Value;
                        }
                    }
                    return true;
                } else {
                    DisplayError(ErrorType.Error, "Invalid label name.", SourceFile, Line);
                    return false;
                }
            } catch (Exception ex) {
                DisplayError(ErrorType.Error, ex.Message, SourceFile, Line);
                return false;
            }
        }

        /// <summary>
        /// Reset the state of the assembler (needs doing at the start of each pass).
        /// </summary>
        public static void ResetStateOnPass() {
            WriteListFile = false;
            ConditionalStack = new Stack();
            ConditionalStack.Push(true);
            ConditionalHasBeenTrue = new Hashtable();
            ConditionalHasBeenTrue[(int)0] = true;
            RLE_Flag = 0x91;
            RLE_ValueFirst = true;
            Macros = new Hashtable();
            CurrentLocalLabel = "_";
            CurrentModule = "noname";
            ProgramCounter = 0;

        }

        /// <summary>
        /// Start assembling a source file
        /// </summary>
        /// <param name="Filename">Filename to start from</param>
        /// <returns>True on success, false on errors</returns>
        public static bool AssembleFile(string Filename) {
            ExportTable = new ArrayList();
            Labels = new Hashtable();
            SourceFiles = new Hashtable();
            BinaryFiles = new Hashtable();
            Macros = new Hashtable();
            ReusableLabels = new Hashtable();
            ResetStateOnPass();
            if (AssembleFile(Filename, Pass.Labels)) {
                Console.WriteLine("Pass 1 complete.");
                if (DebugMode) {
                    foreach (string K in Labels.Keys) {
                        Console.WriteLine(K + "\t" + ((int)Labels[K]).ToString("X4"));
                    }                  
                }

                ASCIITable = new byte[256];
                for (int i = 0; i < 256; ++i) {
                    ASCIITable[i] = (byte)i;
                }

                ResetStateOnPass();
                OutputBinary = new byte[0x10000];
                HasBeenOutput = new int[0x10000];
                BinaryStartLocation = OutputBinary.Length;
                BinaryEndLocation = 0;
                ExportTable = new ArrayList();
                bool Ret = AssembleFile(Filename, Pass.Assembling);
                Console.WriteLine("Pass 2 complete.");
                return Ret;
            } else {
                return false;
            }
        }

        /// <summary>
        /// Assemble a source file
        /// </summary>
        /// <param name="Filename">Filename of the source file to assemble</param>
        /// <returns>True on success, False on any failure</returns>
        public static bool AssembleFile(string Filename, Pass PassNumber) {

            string ActualFilename = Path.GetFullPath(Filename).ToLower();
            if (SourceFiles[ActualFilename] == null) {
                TextReader SourceFile;
                try {
                    SourceFile = new StreamReader(ActualFilename);
                } catch (Exception) {
                    DisplayError(ErrorType.Error, "Could not open file " + ActualFilename);
                    return false;
                }
                SourceFiles[ActualFilename] = SourceFile.ReadToEnd().Replace("\r", "").Split('\n');
                SourceFile.Close();
            }

            string[] RealSourceLines = (string[])SourceFiles[ActualFilename];
            if (RealSourceLines.Length == 0) return true;

            string RealSourceLine = RealSourceLines[0];
            int CurrentLineNumber = 0;
            bool JustHitALabel = false;
            string JustHitLabelName = "";
            while (CurrentLineNumber++ < RealSourceLines.Length) {
                RealSourceLine = RealSourceLines[CurrentLineNumber - 1];
                string SourceLine = "";
                #region Macro preprocessor
                if (PassNumber == Pass.Labels) {

                    // Replace environment variables:

                    int ReplaceEnvVars = RealSourceLine.IndexOf("[%");
                    while (ReplaceEnvVars != -1) {
                        int EndOfEnvVar = RealSourceLine.IndexOf("%]");
                        if (EndOfEnvVar == -1 || (EndOfEnvVar - ReplaceEnvVars <= 1)) break;

                        string Before = RealSourceLine.Remove(ReplaceEnvVars);
                        string After = RealSourceLine.Substring(EndOfEnvVar + 2);

                        string EnvVarName = RealSourceLine.Substring(ReplaceEnvVars, EndOfEnvVar - ReplaceEnvVars).Substring(2);

                        bool IsString = false;
                        if (EnvVarName.Length > 1 && EnvVarName.StartsWith("$") && EnvVarName.EndsWith("$")) {
                            EnvVarName = EnvVarName.Substring(1, EnvVarName.Length - 2);
                            IsString = true;
                        }

                        RealSourceLine = Before + (IsString ? EscapeString(EnvironmentVariables[EnvVarName]) : EnvironmentVariables[EnvVarName]) + After;
                        ReplaceEnvVars = RealSourceLine.IndexOf("[%");
                    }
                    // Carry on

                    string MacroLine = SafeStripWhitespace(RealSourceLine, true) + " ";
                    string CurrentToken = "";
                    string Preceding = "";
                    for (int i = 0; i < MacroLine.Length; ++i) {

                        if ((MacroLine[i] == '\'' || MacroLine[i] == '"')) {
                            SourceLine += ApplyMacros(CurrentToken);
                            char Matching = MacroLine[i];
                            int Start = i;
                            ++i;
                            while (i < MacroLine.Length && MacroLine[i] != Matching && MacroLine[i - 1] != '\\') {
                                ++i;
                            }

                            int EndOfLine = Math.Min(MacroLine.Length - 1, i);
                            
                            SourceLine += MacroLine.Substring(Start, EndOfLine - Start + 1);

                        } else if (i == MacroLine.Length - 1 || "\t\\ +-*/,(){}|&^%;:?'\"=".IndexOf(MacroLine[i]) != -1) {
                            Macro Check = (Macro)Macros[IsCaseSensitive ? CurrentToken : CurrentToken.ToLower()];
                            if (MacroLine[i] == '(' && Check != null) {
                                CurrentToken += '(';
                                ++i;
                                int BracketCount = 1;
                                while (i < MacroLine.Length && BracketCount > 0) {
                                    CurrentToken += MacroLine[i];
                                    if (MacroLine[i] == '(') ++BracketCount;
                                    if (MacroLine[i] == ')') --BracketCount;
                                    ++i;
                                }
                            }
                            if (i == MacroLine.Length - 1) CurrentToken += MacroLine[i];

                            if (Preceding == "ifdef" || Preceding == "ifndef") {
                                SourceLine += CurrentToken;
                            } else {
                                SourceLine += ApplyMacros(CurrentToken);
                            }

                            Preceding = ((string)(CurrentToken + " ")).Substring(1).ToLower().Trim();

                            CurrentToken = "";
                            if (i != MacroLine.Length - 1 && i < MacroLine.Length) SourceLine += MacroLine[i];
                            
                        } else {
                            CurrentToken += MacroLine[i];
                        }
                    }
                    if (DebugMode) Console.WriteLine("PRE:>{0} [->] {1}", ((string[])SourceFiles[ActualFilename])[CurrentLineNumber - 1], SourceLine);
                    ((string[])SourceFiles[ActualFilename])[CurrentLineNumber - 1] = SourceLine;
                } else {
                    SourceLine = RealSourceLine;
                }
                #endregion
            // Now we get to do all sorts of assembling fun.
            CarryOnAssembling:

                if (DebugMode) Console.WriteLine("ASM:>{0}", SourceLine);

                if (SourceLine.StartsWith("\\")) SourceLine = SourceLine.Substring(1);

                // Fix up the conditionals:
                if ((bool)ConditionalStack.Peek()) {
                    ConditionalHasBeenTrue[(int)ConditionalStack.Count - 1] = true;
                }

                if (SourceLine.Trim().Length == 0) continue; // Nothing left on this line

                int FindFirstChar = 0;
                while (FindFirstChar < SourceLine.Length && (SourceLine[FindFirstChar] == ' ' || SourceLine[FindFirstChar] == '\t')) {
                    ++FindFirstChar;
                }

                // Is it a command?

                if (SourceLine[FindFirstChar] == '.' || SourceLine[FindFirstChar] == '#' || SourceLine[FindFirstChar] == '=') { // = for .equ alias
                    #region Assembler Directives
                    // Command
                    string Command = "";
                    if (SourceLine[FindFirstChar] != '=') {
                        while (FindFirstChar < SourceLine.Length && SourceLine[FindFirstChar] != ' ' && SourceLine[FindFirstChar] != '\t') {
                            Command += SourceLine[FindFirstChar];
                            ++FindFirstChar;
                        }
                    } else {
                        Command = "=";
                        ++FindFirstChar;
                    }
                    string FullRestOfLine = SourceLine.Substring(FindFirstChar).Trim();
                    int SplitLine = GetSafeIndexOf(FullRestOfLine, '\\');
                    string RestOfLine = FullRestOfLine;

                    Command = Command.ToLower();

                    if (Command != ".define" && Command != "#define" && Command != ".defcont" && Command != "#defcont") {
                        if (SplitLine != -1) {
                            // Trim everything after the \ away.
                            RestOfLine = FullRestOfLine.Remove(SplitLine);
                            SourceLine = FullRestOfLine.Substring(SplitLine + 1);
                        } else {
                            // Flush the source line, no more to go.
                            SourceLine = "";
                        }
                    } else {
                        SourceLine = "";
                    }
                    switch (Command) {
                        case ".org":
                            #region Origin
                            if (!(bool)ConditionalStack.Peek()) break;
                            try {
                                ProgramCounter = TranslateArgument(RestOfLine);
                            } catch (Exception ex) {
                                DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            }
                            if (DebugMode) Console.WriteLine("Instruction counter moved to " + ProgramCounter.ToString("X4"));
                            break;
                            #endregion
                        case "#include":
                        case ".include":
                            #region Include
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (DebugMode) Console.WriteLine("Moving to " + RestOfLine);
                            string NewFileName = RestOfLine.Replace("\"", "");
                            if (!AssembleFile(NewFileName, PassNumber)) {
                                DisplayError(ErrorType.Error, "Error in file '" + NewFileName + "'", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            }
                            if (DebugMode) Console.WriteLine("Done " + RestOfLine);
                            break;
                            #endregion
                        case "#incbin":
                        case ".incbin":
                            #region Include (Binaries)
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (DebugMode) Console.WriteLine("Loading " + RestOfLine);
                            
                            try {
                                string[] BinaryArguments = SafeSplit(RestOfLine, ',');

                                bool UseRLE = false;
                                string SizeLabel = "";

                                byte[] TranslationTable = new byte[256];
                                for (int i = 0; i < 256; ++i) {
                                    TranslationTable[i] = (byte)i;                                    
                                }

                                string Rule = "";
                                string BinStart = "";
                                string BinEnd = "";

                                for (int i = 1; i < BinaryArguments.Length; ++i) {

                                    BinaryArguments[i] = SafeStripWhitespace(BinaryArguments[i]);

                                    switch (BinaryArguments[i].ToLower().Trim()) {
                                        case "rle":
                                            UseRLE = true; break;
                                        default:
                                            if (BinaryArguments[i].ToLower().EndsWith("=size")) {
                                                SizeLabel = BinaryArguments[i].Trim().Remove(BinaryArguments[i].Trim().Length - 5);
                                            } else if (BinaryArguments[i].ToLower().StartsWith("rule=")) {
                                                Rule = BinaryArguments[i].Substring(5);
                                            } else if (BinaryArguments[i].ToLower().StartsWith("start=")) {
                                                BinStart = BinaryArguments[i].Substring(6);
                                            } else if (BinaryArguments[i].ToLower().StartsWith("end=")) {
                                                BinEnd = BinaryArguments[i].Substring(4);
                                            } else {
                                                DisplayError(ErrorType.Warning, "Unsupported argument '" + BinaryArguments[i].Trim() + "' on binary inclusion.", Filename, CurrentLineNumber);
                                            }
                                            break;
                                    }

                                }

                                string FullFilename = Path.GetFullPath(BinaryArguments[0].Replace("\"", "")).ToLower();


                                if (BinaryFiles[FullFilename] == null && Rule != "") {
                                    for (int j = 0; j < 256; ++j) {
                                        try {
                                            TranslationTable[j] = (byte)TranslateArgument(Rule.Replace("{*}", "(" + j.ToString() + ")"));
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not apply rule to binary: " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                    }
                                }


                                if (BinaryFiles[FullFilename] == null) {
                                    using (BinaryReader BR = new BinaryReader(new FileStream(FullFilename, FileMode.Open))) {

                                        if (SizeLabel != "") {
                                            if (!AddNewLabel(SizeLabel, (int)BR.BaseStream.Length, false, Filename, CurrentLineNumber)) {
                                                DisplayError(ErrorType.Warning, "Could not create file size label '" + SizeLabel + "'.", Filename, CurrentLineNumber);
                                            }
                                        }

                                        int BinStartIx = 0;
                                        if (BinStart != "") {
                                            try {
                                                BinStartIx = TranslateArgument(BinStart);
                                                if (BinStartIx < 0 || BinStartIx >= BR.BaseStream.Length) throw new Exception("Address $" + BinStartIx.ToString("X4") + " is out of the bounds of the binary file.");
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not use start location '" + BinStart + "' - " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                        }

                                        int BinEndIx = (int)BR.BaseStream.Length - 1;
                                        if (BinEnd != "") {
                                            try {
                                                BinEndIx = TranslateArgument(BinEnd);
                                                if (BinEndIx < BinStartIx) throw new Exception("End location $" + BinEndIx.ToString("X4") + " is before start location $" + BinStartIx.ToString("X4") + "!");
                                                if (BinEndIx < 0 || BinEndIx >= BR.BaseStream.Length) throw new Exception("Address $" + BinStartIx.ToString("X4") + " is out of the bounds of the binary file.");
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not use end location '" + BinEnd + "' - " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                        }

                                        byte[] BinaryFile = new byte[BinEndIx - BinStartIx + 1];

                                        BR.BaseStream.Seek(BinStartIx, SeekOrigin.Begin);

                                        for (int i = 0; i <= BinEndIx - BinStartIx; i++) {
                                            BinaryFile[i] = TranslationTable[BR.ReadByte()];
                                        }
                                        if (UseRLE) {
                                            BinaryFile = RLE(BinaryFile);
                                        }
                                        BinaryFiles[FullFilename] = BinaryFile;
                                    }
                                }

                                byte[] BinaryData = (byte[])BinaryFiles[FullFilename];

                                switch (PassNumber) {
                                    case Pass.Labels:
                                        ProgramCounter += BinaryData.Length;
                                        break;
                                    case Pass.Assembling:
                                        ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, ProgramCounter, Filename, BinaryData));
                                        for (int i = 0; i < BinaryData.Length; i++) {
                                            WriteToBinary(BinaryData[i]);
                                        }
                                        
                                        break;
                                    default:
                                        break;
                                }
                            } catch (Exception ex) {
                                DisplayError(ErrorType.Error, "Could not include binary data: " + ex.Message, Filename, CurrentLineNumber);
                            }
                            break;
                            #endregion
                        case ".locallabelchar":
                            #region Local label character
                            if (!(bool)ConditionalStack.Peek()) break;
                            string NewLocalLabelChar = RestOfLine.Trim('"');
                            if (NewLocalLabelChar.Length != 1) {
                                DisplayError(ErrorType.Warning, "'" + NewLocalLabelChar + "' is not a valid local label. (Not set!)", Filename, CurrentLineNumber);
                            } else {
                                CurrentLocalLabel = NewLocalLabelChar;
                            }
                            break;
                            #endregion
                        case ".module":
                            #region Module
                            if (!(bool)ConditionalStack.Peek()) break;
                            string NewModuleName = RestOfLine.Trim('"');
                            if (!IsCaseSensitive) NewModuleName = NewModuleName.ToLower();
                            if (NewModuleName.Length == 0) {
                                DisplayError(ErrorType.Warning, "Module name not specified.", Filename, CurrentLineNumber);
                                CurrentModule = "noname";
                            } else {
                                CurrentModule = NewModuleName;
                            }
                            break;
                            #endregion
                        case ".db":
                        case ".dw":
                        case ".text":
                        case ".byte":
                        case ".word":
                        case ".asc":
                            #region Define data
                            if (!(bool)ConditionalStack.Peek()) break;
                            int DataSize = (Command.ToLower() == ".dw" || Command.ToLower() == ".word") ? 2 : 1;
                            string Data = SafeStripWhitespace(RestOfLine) + "\n";
                            int DataPointer = 0;
                            string CurrentData = "";
                            ArrayList DefinedData = new ArrayList();
                            while (DataPointer < Data.Length) {
                                if (Data[DataPointer] == ',' || Data[DataPointer] == '\n') {
                                    // Flush current data

                                    if (CurrentData.Trim() != "") {

                                        if (PassNumber == Pass.Assembling) {
                                            int DataValue = 0;
                                            try {
                                                DataValue = TranslateArgument(CurrentData);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }

                                            if (PassNumber == Pass.Assembling && WriteListFile) DefinedData.Add(Command == ".asc" ? ASCIITable[DataValue & 0xFF] : (byte)(DataValue & 0xFF));
                                            WriteToBinary(Command == ".asc" ? ASCIITable[DataValue & 0xFF] : (byte)(DataValue & 0xFF));
                                            
                                            if (DataSize == 2) {
                                                DefinedData.Add((byte)(DataValue >> 8));
                                                WriteToBinary((byte)(DataValue >> 8));
                                            }
                                        } else {
                                            ProgramCounter += DataSize;
                                        }
                                    }

                                    CurrentData = "";
                                } else if (Data[DataPointer] == '"') {
                                    // String
                                    ++DataPointer;
                                    string StringToInclude = "";
                                    while (DataPointer < Data.Length && (Data[DataPointer] != '"' || Data[DataPointer - 1] == '\\')) {
                                        StringToInclude += Data[DataPointer];
                                        ++DataPointer;
                                    }
                                    ++DataPointer;
                                    string UnescapedString = UnescapeString(StringToInclude);

                                    if (PassNumber == Pass.Assembling) {
                                        foreach (char C in UnescapedString) {
                                            if (PassNumber == Pass.Assembling && WriteListFile) DefinedData.Add(Command == ".asc" ? ASCIITable[C & 0xFF] : (byte)(C & 0xFF));
                                            WriteToBinary(Command == ".asc" ? ASCIITable[C & 0xFF] : (byte)(C & 0xFF));
                                            if (DataSize == 2) {
                                                DefinedData.Add((byte)(C >> 8));
                                                WriteToBinary((byte)(C >> 8));
                                            }
                                        }
                                    } else {
                                        ProgramCounter += UnescapedString.Length * DataSize;
                                    }
                                } else {
                                    CurrentData += Data[DataPointer];
                                }
                                
                                ++DataPointer;
                            }
                            if (WriteListFile && PassNumber == Pass.Assembling) {
                                ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, ProgramCounter - DefinedData.Count, Filename, (byte[])DefinedData.ToArray(typeof(byte))));
                            }
                            // We have performed the translation:
                            /*byte[] ActualDefinedData;
                            if (Command.EndsWith("rle")) {
                                ActualDefinedData = RLE((byte[])DefinedData.ToArray(typeof(byte)));
                            } else {
                                ActualDefinedData = 
                            }
                            if (PassNumber == Pass.Labels) {
                                ProgramCounter += ActualDefinedData.Length;
                            } else {
                                foreach (byte DB in ActualDefinedData) {
                                    WriteToBinary(DB);                                    
                                }
                            }*/
                            break;
                            #endregion
                        case ".block":
                            #region Block
                            if (!(bool)ConditionalStack.Peek()) break;
                            try {
                                ProgramCounter += TranslateArgument(RestOfLine);
                            } catch {
                                DisplayError(ErrorType.Error, "'" + RestOfLine + "' is not a valid number.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            }
                            break;
                            #endregion
                        case ".chk":
                            #region Checksum
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Assembling) {

                                // Get the line:
                                int StartAddress = 0;
                                try {
                                    StartAddress = TranslateArgument(RestOfLine);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                }
                                byte Checksum = 0;
                                for (int i = StartAddress; i < ProgramCounter; ++i) {
                                    Checksum += (byte)OutputBinary[i];
                                }
                                if (WriteListFile) ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, ProgramCounter, Filename, new byte[] { (byte)Checksum }));
                                WriteToBinary(Checksum);
                            } else {
                                ++ProgramCounter;
                            }
                            break;
                            #endregion
                        case ".echo":
                            #region Messages
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Assembling) {
                                string[] Messages = SafeSplit(RestOfLine, ',');
                                for (int i = 0; i < Messages.Length; ++i) {
                                    Messages[i] = Messages[i].Trim();
                                    
                                }
                                foreach (string EchoMessage in Messages) {
                                    try {
                                        if (EchoMessage.StartsWith("\"") && EchoMessage.EndsWith("\"") && EchoMessage.Length >= 2) {
                                            DisplayError(ErrorType.Message, UnescapeString(EchoMessage.Substring(1, EchoMessage.Length - 2)));
                                        } else {
                                            DisplayError(ErrorType.Message, TranslateArgument(EchoMessage).ToString());
                                        }
                                    } catch {
                                        DisplayError(ErrorType.Warning, ".echo directive argument malformed: '" + EchoMessage + "'", Filename, CurrentLineNumber);
                                    }
                                }

                            }
                            break;
                            #endregion
                        case ".equ":
                        case "=":
                            #region Label assignment
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Labels) {
                                if (JustHitALabel == false) {
                                    DisplayError(ErrorType.Error, Command + " directive is invalid unless you have just declared a label.", Filename, CurrentLineNumber);
                                } else {
                                    try {
                                        AddNewLabel(JustHitLabelName, TranslateArgument(RestOfLine), true, Filename, CurrentLineNumber);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not assign value '" + RestOfLine + "' to label '" + JustHitLabelName + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                    }
                                }
                            }
                            break;
                            #endregion
                        case ".export":
                            #region Label exporting
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Assembling) {
                                string[] ExportVars = SafeSplit(RestOfLine, ',');
                                foreach (string S in ExportVars) {
                                    string LabelName = S.Trim();
                                    if (LabelName.StartsWith(CurrentLocalLabel)) LabelName = CurrentModule + "." + LabelName;
                                    if (Labels[IsCaseSensitive ? LabelName : LabelName.ToLower()] != null) {
                                        ExportTable.Add(LabelName);
                                    } else {
                                        DisplayError(ErrorType.Warning, "Could not find label '" + LabelName + "' to export.");
                                    }
                                }
                            }
                            break;
                            #endregion
                        case ".fill":
                        case ".fillw":
                            #region Fill data
                            if (!(bool)ConditionalStack.Peek()) break;
                            string[] FillArgs = SafeSplit(RestOfLine, ',');
                            if (FillArgs.Length < 1 || FillArgs.Length > 2) {
                                DisplayError(ErrorType.Error, ".fill syntax invalid.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            } else {
                                int FillValue = Command == ".fill" ? 0xFF : 0xFFFF;
                                int FillSize = 0;
                                int Progress = 0;
                                try {
                                    FillSize = TranslateArgument(FillArgs[0]);
                                    if (FillArgs.Length == 2) {
                                        Progress = 1;
                                        FillValue = TranslateArgument(FillArgs[1]);
                                    }

                                    if (PassNumber == Pass.Assembling) {
                                        ArrayList FilledData = new ArrayList();
                                        for (int i = 0; i < FillSize; ++i) {
                                            WriteToBinary((byte)(FillValue & 0xFF));
                                            FilledData.Add((byte)(FillValue & 0xFF));
                                            if (Command == ".fillw") {
                                                WriteToBinary((byte)(FillValue >> 8));
                                                FilledData.Add((byte)(FillValue >> 8));
                                            }
                                        }
                                        if (WriteListFile)
                                            ListFile.Add(new ListFileEntry(Command + " " + RestOfLine,
                                                CurrentLineNumber,
                                                ProgramCounter - FilledData.Count,
                                                Filename,
                                                (byte[])FilledData.ToArray(typeof(byte))));
                                    } else {
                                        ProgramCounter += FillSize * (Command == ".fill" ? 1 : 2);
                                    }
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Error, "Could not evaluate '" + FillArgs[Progress] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                }
                            }
                            break;
                            #endregion
                        case ".end":
                            #region End assembling
                            if (!(bool)ConditionalStack.Peek()) break;
                            return true;
                            #endregion
                        case ".define":
                        case "#define":
                            #region TASM macros
                            if (!(bool)ConditionalStack.Peek()) break;

                            // TASM macro
                            int ScanMacro = 0;
                            string MacroName = "";
                            string MacroArgs = "";
                            bool AmReadingArgs = false;
                            while (ScanMacro < RestOfLine.Length && ((RestOfLine[ScanMacro] != ' ' && RestOfLine[ScanMacro] != '\t') || AmReadingArgs) && RestOfLine[ScanMacro] != '\\') {
                                if (RestOfLine[ScanMacro] == '(') {
                                    AmReadingArgs = true;
                                    ++ScanMacro;
                                    continue;
                                }
                                if (RestOfLine[ScanMacro] == ')') break;
                                if (AmReadingArgs) {
                                    MacroArgs += RestOfLine[ScanMacro];
                                } else {
                                    MacroName += RestOfLine[ScanMacro];
                                }
                                ++ScanMacro;
                            }
                            string MacroSubstitution = IsCaseSensitive ? MacroName : MacroName.ToLower();
                            if (ScanMacro < RestOfLine.Length) {
                                MacroSubstitution = RestOfLine.Substring(ScanMacro + 1);
                            }
                            LastMacro = new Macro();
                            LastMacro.Name = IsCaseSensitive ? MacroName : MacroName.ToLower();
                            if (MacroArgs.Trim() == "") {
                                LastMacro.Args = new string[0];
                            } else {
                                LastMacro.Args = SafeSplit(MacroArgs, ',');
                            }
                            for (int i = 0; i < LastMacro.Args.Length; ++i) {
                                LastMacro.Args[i] = LastMacro.Args[i].ToLower().Trim();
                            }
                            LastMacro.Replacement = MacroSubstitution.Trim();
                            Macros[LastMacro.Name] = LastMacro;
                            if (!CheckLabelName(LastMacro.Name)) DisplayError(ErrorType.Warning, "Potentially confusing macro name '" + LastMacro.Name + "'.", Filename, CurrentLineNumber);
                            break;
                            #endregion
                        case ".defcont":
                        case "#defcont":
                            #region Continue macros
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (LastMacro == null) {
                                DisplayError(ErrorType.Error, "No macro to continue from!");
                            } else {
                                LastMacro.Replacement += RestOfLine;
                                Macros[LastMacro.Name] = LastMacro;
                            }
                            break;
                            #endregion
                        case ".ifdef":
                        case "#ifdef":
                        case ".ifndef":
                        case "#ifndef":
                            #region Conditional defines
                            if ((bool)ConditionalStack.Peek() == true) {
                                if (DebugMode) Console.WriteLine("Checking " + RestOfLine);
                                bool CheckDefine = Macros[IsCaseSensitive ? RestOfLine : RestOfLine.ToLower()] != null;
                                ConditionalStack.Push((Command.IndexOf('n') == -1) ? CheckDefine : !CheckDefine);
                            } else {
                                ConditionalStack.Push(false); // If the parent is FALSE, this too is FALSE.
                            }
                            break;
                            #endregion
                        case ".elseifdef":
                        case "#elseifdef":
                        case ".elseifndef":
                        case "#elseifndef":
                            #region Conditional defines
                            ConditionalStack.Pop(); // Discard current state.
                            if ((bool)ConditionalStack.Peek() == true) {
                                object CheckConditions = ConditionalHasBeenTrue[(int)ConditionalStack.Count];
                                if (CheckConditions != null && (bool)CheckConditions == true) {
                                    ConditionalStack.Push(false);
                                } else {
                                    bool CheckDefine = Macros[IsCaseSensitive ? RestOfLine : RestOfLine.ToLower()] != null;
                                    ConditionalStack.Push((Command.IndexOf('n') == -1) ? CheckDefine : !CheckDefine);
                                }

                            } else {
                                ConditionalStack.Push(false); // If the parent is FALSE, this too is FALSE.
                            }
                            break;
                            #endregion
                        case ".if":
                        case "#if":
                            #region Conditionals
                            if ((bool)ConditionalStack.Peek() == true) {
                                if (DebugMode) Console.WriteLine("Checking " + RestOfLine);
                                int Result = 0;
                                try {
                                    Result = TranslateArgument(RestOfLine);
                                    if (DebugMode) Console.WriteLine(RestOfLine + "=" + Result);
                                    ConditionalStack.Push(Result != 0);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Error, "Could not evaluate " + RestOfLine + " (" + ex.Message + ") - possible errors between pass 1 and 2.", Filename, CurrentLineNumber);
                                    ConditionalStack.Push(false);
                                }
                            } else {
                                ConditionalStack.Push(false); // If the parent is FALSE, this too is FALSE.
                            }
                            break;
                            #endregion
                        case ".elseif":
                        case "#elseif":
                            #region Conditionals (else)
                            ConditionalStack.Pop(); // Discard current state.
                            if ((bool)ConditionalStack.Peek() == true) {
                                try {
                                    object CheckConditions = ConditionalHasBeenTrue[(int)ConditionalStack.Count];
                                    if (CheckConditions != null && (bool)CheckConditions == true) {
                                        ConditionalStack.Push(false);
                                    } else {
                                        int Result = TranslateArgument(RestOfLine);
                                        ConditionalStack.Push(Result != 0);
                                    }

                                } catch {
                                    DisplayError(ErrorType.Error, "Could not evaluate " + RestOfLine + " (possible errors between pass 1 and 2).", Filename, CurrentLineNumber);
                                    ConditionalStack.Push(false);
                                }
                            } else {
                                ConditionalStack.Push(false); // If the parent is FALSE, this too is FALSE.
                            }
                            break;
                            #endregion
                        case ".endif":
                        case "#endif":
                            #region Conditionals (end)
                            if (ConditionalStack.Count == 1) {
                                DisplayError(ErrorType.Error, "Unmatched " + Command + " directive.", Filename, CurrentLineNumber);
                            } else {
                                ConditionalStack.Pop();
                                ConditionalHasBeenTrue[(int)ConditionalStack.Count] = null;
                            }
                            break;
                            #endregion
                        case ".else":
                        case "#else":
                            #region Conditionals (basic else)
                            bool CurrentLevel = (bool)ConditionalStack.Pop();
                            if ((bool)ConditionalStack.Peek() == true) {
                                object CheckConditions = ConditionalHasBeenTrue[(int)ConditionalStack.Count];
                                if (CheckConditions != null && (bool)CheckConditions == true) {
                                    ConditionalStack.Push(false);
                                } else {
                                    ConditionalStack.Push(!CurrentLevel);
                                }
                            } else {
                                ConditionalStack.Push(false); // If the parent is FALSE, this too is FALSE.
                            }
                            break;
                            #endregion
                        case ".list":
                        case ".nolist":
                            #region Listing
                            if (!(bool)ConditionalStack.Peek()) break;
                            WriteListFile = (Command == ".list");
                            break;
                            #endregion
                        case ".addinstr":
                            #region Add instruction
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (AddInstructionLine(RestOfLine)) {
                                RehashInstructionTable();
                            } else {
                                DisplayError(ErrorType.Error, "Could not add instruction.", Filename, CurrentLineNumber);
                            }
                            break;
                            #endregion
                        case ".variablename":
                            #region Set variable name
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Labels) {
                                VariableName = RestOfLine.Trim().Trim('"');
                            }
                            break;
                            #endregion
                        case ".binarymode":
                            #region Binary mode
                            if (!(bool)ConditionalStack.Peek()) break;
                            if (PassNumber == Pass.Labels) {
                                switch (RestOfLine.ToLower()) {
                                    case "raw":
                                        BinaryType = Binary.Raw; break;
                                    case "ti8x":
                                        BinaryType = Binary.TI8X; break;
                                    case "ti83":
                                        BinaryType = Binary.TI83; break;
                                    case "ti82":
                                        BinaryType = Binary.TI82; break;
                                    case "ti85":
                                        BinaryType = Binary.TI85; break;
                                    case "ti86":
                                        BinaryType = Binary.TI86; break;
                                    case "ti73":
                                        BinaryType = Binary.TI73; break;
                                    case "intel":
                                        BinaryType = Binary.Intel; break;
                                    case "intelword":
                                        BinaryType = Binary.IntelWord; break;
                                    case "mos":
                                        BinaryType = Binary.MOS; break;
                                    case "motorola":
                                        BinaryType = Binary.Motorola; break;
                                    default:
                                        DisplayError(ErrorType.Error, "Invalid binary mode '" + RestOfLine + "'", Filename, CurrentLineNumber);
                                        break;
                                }
                            }
                            break;
                            #endregion
                        case ".tivariabletype":
                            #region TI variable type
                            if (!(bool)ConditionalStack.Peek()) break;
                            try {
                                TIVariableType = TranslateArgument(RestOfLine);
                            } catch (Exception ex) {
                                DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                            }
                            break;
                            #endregion
                        case ".dbrnd":
                        case ".dwrnd":
                            #region Random data
                            if (!(bool)ConditionalStack.Peek()) break;
                            string[] RndArgs = SafeSplit(RestOfLine, ',');
                            if (RndArgs.Length != 3) {
                                DisplayError(ErrorType.Error, Command + " requires 3 arguments.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            } else {
                                int[] RealRndArgs = new int[3];
                                bool RndArgsValid = true;
                                for (int i = 0; i < 3; ++i) {
                                    RndArgs[i] = RndArgs[i].Trim();
                                    try {
                                        RealRndArgs[i] = TranslateArgument(RndArgs[i]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not evaluate '" + RndArgs[i] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        RndArgsValid = false;
                                        break;
                                    }
                                }
                                if (!RndArgsValid) break;
                                int RndDataSize = Command.IndexOf('w') == -1 ? 1 : 2;
                                if (PassNumber == Pass.Assembling) {
                                    ArrayList RandomData = new ArrayList();
                                    Random R = new Random();
                                    try {
                                        if (RealRndArgs[1] > RealRndArgs[2]) throw new Exception("Minimum must be less than maximum");
                                        for (int i = 0; i < RealRndArgs[0]; ++i) {
                                            int RN = R.Next(RealRndArgs[1], RealRndArgs[2]);
                                            WriteToBinary((byte)(RN & 0xFF));
                                            RandomData.Add(((byte)(RN & 0xFF)));
                                            if (RndDataSize == 2) {
                                                WriteToBinary((byte)(RN >> 8));
                                                RandomData.Add((byte)(RN >> 8));
                                            }
                                        }
                                        if (WriteListFile)
                                            ListFile.Add(new ListFileEntry(Command + " " + RestOfLine,
                                                CurrentLineNumber,
                                                ProgramCounter - RandomData.Count,
                                                Filename,
                                                (byte[])RandomData.ToArray(typeof(byte))));
         
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not generate random data: '" + ex.Message + "'.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }

                                } else {
                                    ProgramCounter += RndDataSize * RealRndArgs[0];
                                }

                            }

                            break;
                            #endregion
                        case ".var":
                            #region Variable
                            if (!(bool)ConditionalStack.Peek() || PassNumber != Pass.Labels) break;
                            string[] VarArgs = SafeSplit(RestOfLine, ',');
                            if (VarArgs.Length != 2) {
                                DisplayError(ErrorType.Error, "Variables must have a name and size definition.", Filename, CurrentLineNumber);
                            } else {
                                string VariableName = VarArgs[1].Trim();
                                if (VariableName == "") {
                                    DisplayError(ErrorType.Error, "No variable name specified.", Filename, CurrentLineNumber);
                                    break;
                                }
                                int VariableSize = 1;
                                try {
                                    VariableSize = TranslateArgument(VarArgs[0]);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Error, "Invalid variable size '" + VarArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                    break;
                                }
                                // So, we have a variable name and a size :)
                                if (AddNewLabel(VariableName, VariableTableOff + VariableTable, false, Filename, CurrentLineNumber)) {
                                    if (VariableTableSize != 0 && VariableTableOff >= VariableTableSize) DisplayError(ErrorType.Warning, "Variable '" + VariableName + "' leaks out of allocated variable table space.", Filename, CurrentLineNumber);
                                    VariableTableOff += VariableSize;

                                } else {
                                    DisplayError(ErrorType.Error, "Could not add variable '" + VariableName + "' (previously defined label).", Filename, CurrentLineNumber);
                                }
                            }
                            break;
                            #endregion
                        case ".varloc":
                            #region Variable table location
                            if (!(bool)ConditionalStack.Peek() || PassNumber != Pass.Labels) break;
                            string[] VarLocArgs = SafeSplit(RestOfLine, ',');
                            if (VarLocArgs.Length > 2 || VarLocArgs.Length < 1) {
                                DisplayError(ErrorType.Error, "Variable table location definition must specify the location (and, optionally, a maximum size).", Filename, CurrentLineNumber);
                            } else {
                                try {
                                    VariableTable = TranslateArgument(VarLocArgs[0]);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Error, "Invalid variable table location '" + VarLocArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                    break;
                                }
                                VariableTableOff = 0;
                                VariableTableSize = 0;
                                if (VarLocArgs.Length == 2) {
                                    try {
                                        VariableTableSize = TranslateArgument(VarLocArgs[1]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Warning, "Invalid variable table size '" + VarLocArgs[1] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        break;

                                    }
                                }
                            }
                            break;
                            #endregion
                        case ".asciimap":
                            #region ASCII mapping
                            if (!(bool)ConditionalStack.Peek() || PassNumber != Pass.Assembling) break;
                            string[] ASCIIArgs = SafeSplit(RestOfLine, ',');
                            if (ASCIIArgs.Length < 2 || ASCIIArgs.Length > 3) {
                                DisplayError(ErrorType.Warning, "Invalid ASCII mapping definition.", Filename, CurrentLineNumber);
                            } else {
                                int MinChar = 0;
                                try {
                                    MinChar = TranslateArgument(ASCIIArgs[0]);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Warning, "Could not parse argument for " + ((ASCIIArgs.Length == 3) ? "minimum" : "character") + ": " + ex.Message);
                                    break;
                                }
                                int MaxChar = MinChar;
                                if (ASCIIArgs.Length == 3) {
                                    try {
                                        MaxChar = TranslateArgument(ASCIIArgs[1]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Warning, "Could not parse argument for maximum: " + ex.Message);
                                        break;
                                    }
                                }
                                string Rule = ASCIIArgs[ASCIIArgs.Length - 1];
                                if (MinChar < 0x00) { DisplayError(ErrorType.Warning, "Minimum too small: clipping to $00.", Filename, CurrentLineNumber); MinChar = 0x00; }
                                if (MaxChar < 0x00) { DisplayError(ErrorType.Warning, "Maximum too small: clipping to $00.", Filename, CurrentLineNumber); MaxChar = 0x00; }
                                if (MinChar > 0xFF) { DisplayError(ErrorType.Warning, "Minimum too large: clipping to $FF.", Filename, CurrentLineNumber); MinChar = 0xFF; }
                                if (MaxChar > 0xFF) { DisplayError(ErrorType.Warning, "Maximum too large: clipping to $FF.", Filename, CurrentLineNumber); MaxChar = 0xFF; }
                                for (int i = MinChar; i <= MaxChar; ++i) {
                                    try {
                                        ASCIITable[i] = (byte)TranslateArgument(Rule.Replace("{*}", "(" + i.ToString() + ")"));
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Warning, "Invalid ASCII mapping: could not parse remapping of character " + i + "(" + ex.Message + ").", Filename, CurrentLineNumber);
                                        break;
                                    }
                                }
                            }
                            break;
                            #endregion
                        case ".dbsin":
                        case ".dbcos":
                        case ".dwsin":
                        case ".dwcos":
                            #region Trig tables
                            if (!(bool)ConditionalStack.Peek()) break;
                            string[] TrigArgs = SafeSplit(RestOfLine, ',');
                            if (TrigArgs.Length != 6) {
                                DisplayError(ErrorType.Error, "Trigonometric table directives require 6 arguments.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                                break;
                            }
                            int TrigScale = 0;
                            int TrigMag = 0;
                            int TrigStart = 0;
                            int TrigEnd = 0;
                            int TrigStep = 0;
                            int TrigOffset = 0;
                            try {
                                TrigScale = TranslateArgument(TrigArgs[0]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table scale: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                            try {
                                TrigMag = TranslateArgument(TrigArgs[1]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table amplitude: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                            try {
                                TrigStart = TranslateArgument(TrigArgs[2]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table start: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                            try {
                                TrigEnd = TranslateArgument(TrigArgs[3]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table end: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                            try {
                                TrigStep = TranslateArgument(TrigArgs[4]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table step: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                            try {
                                TrigOffset = TranslateArgument(TrigArgs[5]);
                            } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table offset: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }

                            if (TrigStep == 0) {
                                DisplayError(ErrorType.Error, "Table step must be nonzero.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                                break;
                            }
                            if (TrigScale == 0) {
                                DisplayError(ErrorType.Error, "Table scale must be nonzero.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                                break;
                            }

                            if (TrigStep < 0 && (TrigEnd > TrigStart) || TrigStep > 0 && (TrigEnd < TrigStart)) {
                                DisplayError(ErrorType.Error, "Step must cycle through angle range in order (going from " + TrigStart + " to " + TrigEnd + " with a step of " + TrigStep + " will not work).", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                                break;
                            }

                            ArrayList AnglesToUse = new ArrayList();
                            if (TrigStep > 0) {
                                for (int i = TrigStart; i <= TrigEnd; i += TrigStep) {
                                    AnglesToUse.Add(i);
                                }
                            } else {
                                for (int i = TrigStart; i >= TrigEnd; i += TrigStep) {
                                    AnglesToUse.Add(i);
                                }
                            }
                            int TrigElementSize = (Command.IndexOf('w') == -1) ? 1 : 2;
                            ArrayList TrigData = new ArrayList();
                            if (PassNumber == Pass.Labels) {
                                ProgramCounter += AnglesToUse.Count * TrigElementSize;
                            } else {
                                foreach (int I in AnglesToUse) {
                                    double RealAngle = ((double)I / (double)TrigScale) * Math.PI * 2;
                                    int PlainTrigValue = TrigOffset + (int)Math.Round(((Command.IndexOf('c') == -1) ? Math.Sin(RealAngle) : Math.Cos(RealAngle)) * (double)TrigMag);
                                    WriteToBinary((byte)(PlainTrigValue & 0xFF));
                                    TrigData.Add((byte)(PlainTrigValue & 0xFF));
                                    if (TrigElementSize == 2) {
                                        WriteToBinary((byte)(PlainTrigValue >> 8));
                                        TrigData.Add((byte)(PlainTrigValue >> 8));
                                    }
                                }

                                if (WriteListFile)
                                    ListFile.Add(new ListFileEntry(Command + " " + RestOfLine,
                                        CurrentLineNumber,
                                        ProgramCounter - TrigData.Count,
                                        Filename,
                                        (byte[])TrigData.ToArray(typeof(byte))));
                            }                            
                            break;
                            #endregion
                        case ".rlemode":
                            #region RLE mode
                            if (!(bool)ConditionalStack.Peek()) break;
                            string[] RLE_Args = SafeSplit(RestOfLine, ',');
                            try {
                                RLE_Flag = (byte)(TranslateArgument(RLE_Args[0]));
                            } catch (Exception ex) {
                                DisplayError(ErrorType.Warning, "Could not set new RLE run character - " + ex.Message, Filename, CurrentLineNumber);
                                break;
                            }
                            if (RLE_Args.Length > 1) {
                                try {
                                    RLE_ValueFirst = (TranslateArgument(RLE_Args[1]) != 0);
                                } catch (Exception ex) {
                                    DisplayError(ErrorType.Warning, "Could not set new RLE ordering mode - " + ex.Message, Filename, CurrentLineNumber);
                                    break;
                                }                                
                            }
                            break;
                            #endregion
                        default:
                            DisplayError(ErrorType.Error, "Unsupported directive '" + Command + "'.", Filename, CurrentLineNumber);
                            if (StrictMode) return false;
                            break;
                    }
                    if (SourceLine != "") goto CarryOnAssembling;
                    #endregion
                } else if ((bool)ConditionalStack.Peek()) {
                    if (FindFirstChar == 0) {
                        #region Label Detection
                        // Label
                        int EndOfLabel = SourceLine.IndexOfAny("\t .#".ToCharArray());
                        if (EndOfLabel == -1) {
                            if (PassNumber == Pass.Labels) {
                                JustHitALabel = true;
                                JustHitLabelName = SourceLine.Trim().Replace(":", "");
                                if (!AddNewLabel(JustHitLabelName, ProgramCounter, false, Filename, CurrentLineNumber)) {
                                    DisplayError(ErrorType.Error, "Could not create new label '" + JustHitLabelName + "' (previously defined).", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                }
                            }
                        } else {
                            JustHitALabel = true;
                            JustHitLabelName = SourceLine.Remove(EndOfLabel).Trim().Replace(":", "");
                            if (!IsCaseSensitive) JustHitLabelName = JustHitLabelName.ToLower();
                            if (PassNumber == Pass.Labels && !AddNewLabel(JustHitLabelName, ProgramCounter, false, Filename, CurrentLineNumber) && StrictMode) return false;

                            SourceLine = SourceLine.Substring(EndOfLabel);
                            goto CarryOnAssembling; // You love teh goto.
                        }
                        #endregion
                    } else {
                        JustHitALabel = false;  // Clear that
                        #region Assembly Code
                        // Assembly
                        string Instr = "";
                        while (FindFirstChar < SourceLine.Length && SourceLine[FindFirstChar] != ' ' && SourceLine[FindFirstChar] != '\t') {
                            Instr += SourceLine[FindFirstChar];
                            ++FindFirstChar;
                        }
                        Instr = Instr.ToLower();
                        if (Instructions[Instr] == null) {
                            DisplayError(ErrorType.Error, "Instruction '" + Instr + "' not understood.", Filename, CurrentLineNumber);
                            if (StrictMode) return false;
                        } else {
                            string Args = "";

                            ArrayList H = (ArrayList)Instructions[Instr];

                            bool HasArgs = false;
                            Instruction I = null;
                            foreach (Instruction FindI in H) {
                                string Arg = FindI.Arguments;
                                if (Arg!="\"\"") {
                                    HasArgs = true;
                                    break;
                                }
                            }
                            if (HasArgs) {
                                while (FindFirstChar < SourceLine.Length && (SourceLine[FindFirstChar] == ' ' || SourceLine[FindFirstChar] == '\t' || SourceLine[FindFirstChar] == '\\')) {
                                    ++FindFirstChar;
                                }

                            }
                            while (FindFirstChar < SourceLine.Length && SourceLine[FindFirstChar] != '\\') {
                                Args += SourceLine[FindFirstChar];
                                ++FindFirstChar;
                            }
    
                            // Strip out whitespace
                            Args = SafeStripWhitespace(Args);
                            
                            foreach (Instruction FindI in H) {
                                string Arg = FindI.Arguments;
                                // || Args.StartsWith(Arg)
                                if (MatchWildcards(Arg, Args)) {
                                    I = FindI;
                                    break;
                                }
                            }
                            if (I == null) {
                                DisplayError(ErrorType.Error, "Argument '" + Args + "' (for '" + Instr + "') not understood.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            } else {

                                if (PassNumber == Pass.Assembling) {

                                    if (DebugMode) Console.WriteLine(ProgramCounter.ToString("X4") + ":" + ((int)(ProgramCounter - BinaryStartLocation)).ToString("X4") + "\t" + I.Name + "\t" + I.Arguments + "\t" + SafeStripWhitespace(Args));

                                    byte[] InstructionBytes = new byte[I.Size];
                                    for (int i = 0; i < I.Opcodes.Length; ++i) {
                                        InstructionBytes[i] = I.Opcodes[i];
                                    }
                                    ArrayList SourceArgs = ExtractArguments(I.Arguments, Args);
                                    ArrayList AdjustedArgs = new ArrayList();

                                    int RealArgument = 0;
                                    try {
                                        RealArgument = ((SourceArgs.Count == 0) ? 0 : TranslateArgument((string)SourceArgs[0]));
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not parse expression '" + SourceArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    switch (I.Rule) {

                                        case Instruction.InstructionRule.NoTouch:
                                            int ExtraSize = I.Size - I.Opcodes.Length;
                                            if (ExtraSize > 0) {
                                                if (ExtraSize == 1) {
                                                    InstructionBytes[InstructionBytes.Length - 1] = (byte)(RealArgument & 0xFF);
                                                } else if (ExtraSize == 2) {
                                                    InstructionBytes[InstructionBytes.Length - 2] = (byte)(RealArgument & 0xFF);
                                                    InstructionBytes[InstructionBytes.Length - 1] = (byte)(RealArgument >> 8);
                                                } else {
                                                    DisplayError(ErrorType.Error, "NOP type with multiple arguments not supported.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                }
                                            }
                                            break;
                                        case Instruction.InstructionRule.R1:
                                            RealArgument -= (ProgramCounter + I.Size);
                                            if (RealArgument > 127 || RealArgument < -128) {
                                                DisplayError(ErrorType.Error, "Range of relative jump exceeded. (" + RealArgument + " bytes)", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            InstructionBytes[InstructionBytes.Length - 1] = (byte)RealArgument;
                                            break;
                                        case Instruction.InstructionRule.R2:
                                            RealArgument -= (ProgramCounter + I.Size);
                                            if (RealArgument > 32767 || RealArgument < -32768) {
                                                DisplayError(ErrorType.Error, "Range of relative jump exceeded. (" + RealArgument + " bytes)", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            InstructionBytes[InstructionBytes.Length - 2] = (byte)(RealArgument & 0xFF);
                                            InstructionBytes[InstructionBytes.Length - 1] = (byte)(RealArgument >> 8);
                                            break;
                                        case Instruction.InstructionRule.ZIdX:
                                            for (int i = I.Opcodes.Length; i < I.Size; i++) {
                                                int Arg = 0;
                                                if (SourceArgs.Count == 2) {
                                                    string ToUse = (string)SourceArgs[i - I.Opcodes.Length];
                                                    try {
                                                        Arg = TranslateArgument(ToUse);
                                                        InstructionBytes[i] = (byte)Arg;
                                                    } catch {
                                                        DisplayError(ErrorType.Error, "Could not understand argument '" + ToUse + "'.", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false;
                                                    }
                                                } else {
                                                    if (I.Size - I.Opcodes.Length == 2) {
                                                        InstructionBytes[I.Size - 2] = (byte)((RealArgument | I.Or) & 0xFF);
                                                        InstructionBytes[I.Size - 1] = (byte)((RealArgument | I.Or) >> 8);
                                                    } else {
                                                        InstructionBytes[I.Size - 1] = (byte)RealArgument;
                                                    }
                                                }
                                            }
                                            break;
                                        case Instruction.InstructionRule.ZBit:
                                            if (RealArgument < 0 || RealArgument > 7) {
                                                DisplayError(ErrorType.Error, "Bit index must be in the range 0-7 (not " + RealArgument + ").", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            RealArgument *= 8;

                                            if (I.Size == 4) {
                                                int SecondArgument = 0;
                                                try {
                                                    SecondArgument = TranslateArgument((string)SourceArgs[1]);
                                                } catch {
                                                    DisplayError(ErrorType.Error, "Could not understand argument '" + SourceArgs[1] + "'.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                }
                                                if (SecondArgument > 127 || SecondArgument < -128) {
                                                    DisplayError(ErrorType.Error, "Range of IX must be between -128 and 127 (not " + SecondArgument + ").", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                }
                                                InstructionBytes[2] = (byte)((SecondArgument | (I.Or & 0xFF)) & 0xFF);
                                                InstructionBytes[3] = (byte)(RealArgument | (I.Or >> 8));
                                            } else if (I.Size == 2) {
                                                InstructionBytes[1] += (byte)(RealArgument);
                                            } else {
                                                DisplayError(ErrorType.Error, "ZBIT instruction not supported.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            break;
                                        case Instruction.InstructionRule.RST:
                                            InstructionBytes[0] = (byte)((int)InstructionBytes[0] + RealArgument);
                                            break;
                                        default:
                                            DisplayError(ErrorType.Error, "This instruction is not yet supported.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                    }
                                    if (WriteListFile) {
                                        ListFile.Add(new ListFileEntry(FindFirstChar == SourceLine.Length ? SourceLine : SourceLine.Remove(FindFirstChar), CurrentLineNumber, ProgramCounter, Filename, InstructionBytes));
                                    }
                                    foreach (byte B in InstructionBytes) {
                                        WriteToBinary(B);
                                    }

                                } else {
                                    ProgramCounter += I.Size;
                                }

                                SourceLine = SourceLine.Substring(FindFirstChar);
                                goto CarryOnAssembling;
                            }

                        }
                        #endregion
                    }
                }
            }

            return true;
        }
    }
}
