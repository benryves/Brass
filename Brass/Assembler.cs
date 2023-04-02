using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Drawing;

namespace Brass {
    public partial class Program {

        public static Hashtable Labels;         // Stores label names -> address mapping.
        public static Hashtable Instructions;   // Stores instructions -> Instruction class mapping (eg 'LD').

        public static ArrayList ExportTable;    // Stores the export table.


        public static Hashtable SourceFiles;    // Loaded source files.
        public static Hashtable BinaryFiles;    // Loaded binary files.

        public static bool IsCaseSensitive = false; // Case sensitive or not?

        public static string CurrentModule;     // Current module's name.
        public static string CurrentLocalLabel; // Current local label definition.


        public enum Pass { Labels, Assembling } // Which pass are we on?

        public static Hashtable ConditionalHasBeenTrue; // Remembering if a conditional on this level has ever been true.
        public static Stack ConditionalStack;   // Stack of conditionals keeping track of true/false/FileNotFound

        public static Macro LastMacro;
        public static MacroReplacement LastReplacement;

        public static Hashtable[] ReusableLabels; // + or - : This is pure magic ;)

        public static ArrayList BookmarkLabels; // @
        public static int BookmarkIndex;

        public static Hashtable ForLoops;       // Hold for-loop label names->for-loop data

        public static byte[] ASCIITable;        // ASCII mapping table

        public static ArrayList AllInstructions;

        public static int VariableTable = 0;        // Variable table location
        public static int VariableTableOff = 0;     // Offset in variable table
        public static int VariableTableSize = 0;    // Size of the variable table size

        public static Stack LastForLoop;

        public static bool WriteListFile = false;
        public static ArrayList ListFile = new ArrayList();

        public static Hashtable FileHandles;

        public static Queue MatchedAssembly;

        public static bool OnlyOutputMinimalData;

        public static int StartOutputAddress = 0;
        public static int EndOutputAddress = 0;

        public static byte BinaryFillChar = 0xFF;

        public static int RelocationOffset = 0x0000;

        public static ArrayList Namespaces = new ArrayList();

        public class LabelDetails {
            public int Value = 0;
            public string File = "";
            public int Line = 0;
            public string Name = "";
            public int Page = 0;
            public LabelDetails(string Name, int Value, string File, int Line) {
                this.Name = Name;
                this.Value = Value;
                this.File = File;
                this.Line = Line;
                this.Page = CurrentPage.Page;
            }
        }

        private static string ResolvePath(string CurrentFile, string RemoteFile) {
            string Closest = Path.Combine(Path.GetDirectoryName(CurrentFile), RemoteFile);
            if (File.Exists(Closest)) return Closest;
            return RemoteFile;
        }

        /// <summary>
        /// Adjust a label name, fixing for case and local label stuff.
        /// </summary>
        /// <param name="Name">Label name</param>
        /// <returns>Fixed label name</returns>
        public static string FixLabelName(string Name) { return FixLabelName(Name, false); }
        public static string FixLabelName(string Name, bool IgnoreErrors) {
            Name = IsCaseSensitive ? Name.Trim() : Name.Trim().ToLower();
            int ReplaceVariables = Name.IndexOf("{");
            while (ReplaceVariables != -1) {
                int EndOfVariable = Name.IndexOf("}");
                if (EndOfVariable == -1 || (EndOfVariable - ReplaceVariables <= 1)) break;

                string Before = Name.Remove(ReplaceVariables);
                string After = Name.Substring(EndOfVariable + 1);

                string ReplaceVariableName = Name.Substring(ReplaceVariables, EndOfVariable - ReplaceVariables).Substring(1);

                Name = Before + TranslateArgument(ReplaceVariableName) + After;
                ReplaceVariables = Name.IndexOf("{");
            }
            if (Name.StartsWith(CurrentLocalLabel)) Name = CurrentModule + "." + Name;
            if (!IgnoreErrors) {
                if (Name.Length == 0) {
                    throw new Exception("Nonexistant label name.");
                } else if (Name[0] >= '0' && Name[0] <= '9') {
                    throw new Exception("Labels names may not start with a number ('" + Name + "').");
                }
            }
            return Name;
        }

        private class ReusableLabelTracker {
            public int Index = 0;
            public ArrayList AllLabels = new ArrayList(1024);

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

        public class ForLoop {
            //public int Value = 0;
            public int Step = 0;
            public int Start = 0;
            public int End = 0;
            public string Filename = "";
            public int LineNumber = 0;
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
        public static char[] Reusables = { '+', '-' };
        public static bool AddNewLabel(string Name, int Value, bool ForceNewLabel, string SourceFile, int Line, Pass PassNumber) {
            try {
                Name = FixLabelName(Name);

                if (Name != "") {
                    if (Name == "@") {
                        if (PassNumber == Pass.Labels) {
                            BookmarkLabels.Add(Value);
                        }
                        ++BookmarkIndex;
                    } else if (Name.IndexOfAny(Reusables) != -1 && Name.Replace("+", "") == "" || Name.Replace("-", "") == "") {
                        int Mode = Name[0] == '+' ? 1 : 0;
                        if (PassNumber == Pass.Labels) {
                            if (ReusableLabels[Mode][Name.Length] == null) ReusableLabels[Mode][Name.Length] = new ReusableLabelTracker();
                            ((ReusableLabelTracker)ReusableLabels[Mode][Name.Length]).AllLabels.Add(Value);
                        }
                        ++((ReusableLabelTracker)ReusableLabels[Mode][Name.Length]).Index;
                    } else {
                        //if (!CheckLabelName(Name)) DisplayError(ErrorType.Warning, "Potentially confusing label name '" + Name + "'.", SourceFile, Line);
                        if (Labels[Name] != null && !ForceNewLabel) {
                            return false;
                        } else {
                            Labels[Name] = new LabelDetails(Name, Value, SourceFile, Line);
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
            WriteListFile = true;
            ConditionalStack = new Stack(128);
            ConditionalStack.Push(true);
            ConditionalHasBeenTrue = new Hashtable(128);
            ConditionalHasBeenTrue[(int)0] = true;
            ForLoops = new Hashtable(64);
            RLE_Flag = 0x91;
            RLE_ValueFirst = true;
            LastReplacement = null;
            LastMacro = null;
            AvailableMacros = new Hashtable(512);
            CurrentLocalLabel = "_";
            CurrentModule = "noname";
            Namespaces = new ArrayList();

            CurrentPage = new BinaryPage();
            Pages = new Hashtable();
            Pages[0] = CurrentPage;
            CanStillDefinePage0 = true;

            LastForLoop = new Stack(64);
            CloseFileHandles();

            BookmarkIndex = 0;

            for (int i = 0; i < 2; i++) {
                foreach (object O in ReusableLabels[i].Keys) {
                    ReusableLabelTracker R = (ReusableLabelTracker)ReusableLabels[i][O];
                    R.Index = 0;
                }
            }

            OnlyOutputMinimalData = true;

            RelocationOffset = 0x0000;

        }

        public static void CloseFileHandles() {
            if (FileHandles != null) {
                foreach (object V in FileHandles.Keys) {
                    BinaryReader ToClose = (BinaryReader)FileHandles[V];
                    ToClose.Close();
                }
            }
            FileHandles = new Hashtable();
        }

        /// <summary>
        /// Start assembling a source file
        /// </summary>
        /// <param name="Filename">Filename to start from</param>
        /// <returns>True on success, false on errors</returns>
        public static bool AssembleFile(string Filename) {
            ExportTable = new ArrayList(512);
            Labels = new Hashtable(32000);
            SourceFiles = new Hashtable(64);
            BinaryFiles = new Hashtable(64);
            ReusableLabels = new Hashtable[2] { new Hashtable(16), new Hashtable(16) };
            BookmarkLabels = new ArrayList(32000);
            MatchedAssembly = new Queue(64000);
            ResetStateOnPass();


            DateTime StartTime = DateTime.Now;

            if (AssembleFile(Filename, Pass.Labels)) {
                TimeSpan PassTime = DateTime.Now - StartTime;
                Console.WriteLine("Pass 1 complete. ({0}ms).", (int)(PassTime.Seconds * 1000) + (int)PassTime.Milliseconds);

                //*
                foreach (Error E in ErrorLog) {
                    if (E.E == ErrorType.Error) {
                        DisplayError(ErrorType.Message, "Pass 1 failed, pass 2 therefore skipped.\n");
                        return false;
                    }
                }
                //*/

                StartTime = DateTime.Now;

                ASCIITable = new byte[256];
                for (int i = 0; i < 256; ++i) {
                    ASCIITable[i] = (byte)i;
                }

                ResetStateOnPass();
                ExportTable = new ArrayList();
                bool Ret = AssembleFile(Filename, Pass.Assembling);

                PassTime = DateTime.Now - StartTime;
                Console.WriteLine("Pass 2 complete. ({0}ms).", (int)(PassTime.Seconds * 1000) + (int)PassTime.Milliseconds);
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
                    try {
                        SourceLine = PreprocessMacros(RealSourceLine);
                    } catch (Exception ex) {
                        DisplayError(ErrorType.Error, "Internal preprocessor error: " + ex.Message, Filename, CurrentLineNumber);
                        return false;
                    }
                    ((string[])SourceFiles[ActualFilename])[CurrentLineNumber - 1] = SourceLine;
                } else {
                    SourceLine = RealSourceLine;
                }
                #endregion
            // Now we get to do all sorts of assembling fun.
            CarryOnAssembling:
                //if (DebugMode) Console.WriteLine("ASM:>{0}", SourceLine);

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
                    string UnalteredRestOfLine = SourceLine.Substring(FindFirstChar);
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

                        case ".ifdef":
                        case "#ifdef":
                        case ".ifndef":
                        case "#ifndef":
                            #region Conditional defines
                            if ((bool)ConditionalStack.Peek() == true) {
                                //if (DebugMode) Console.WriteLine("Checking " + RestOfLine);
                                bool CheckDefine = AvailableMacros[IsCaseSensitive ? RestOfLine : RestOfLine.ToLower()] != null;
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
                                    bool CheckDefine = AvailableMacros[IsCaseSensitive ? RestOfLine : RestOfLine.ToLower()] != null;
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
                                //if (DebugMode) Console.WriteLine("Checking " + RestOfLine);
                                int Result = 0;
                                try {
                                    Result = TranslateArgument(RestOfLine);
                                    //if (DebugMode) Console.WriteLine(RestOfLine + "=" + Result);
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

                        default:
                            if (!(bool)ConditionalStack.Peek()) break;
                            switch (Command) {
                                case ".org":
                                    #region Origin
                                    try {
                                        CurrentPage.ProgramCounter = TranslateArgument(RestOfLine);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    //if (DebugMode) Console.WriteLine("Instruction counter moved to " + ProgramCounter.ToString("X4"));
                                    break;
                                    #endregion
                                case "#include":
                                case ".include":
                                    #region Include
                                    //if (DebugMode) Console.WriteLine("Moving to " + RestOfLine);
                                    string NewFileName = RestOfLine.Replace("\"", "");
                                    if (!AssembleFile(ResolvePath(Filename, NewFileName), PassNumber)) {
                                        DisplayError(ErrorType.Error, "Error in file '" + NewFileName + "'", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    //if (DebugMode) Console.WriteLine("Done " + RestOfLine);
                                    break;
                                    #endregion
                                case "#incbin":
                                case ".incbin":
                                    #region Include (Binaries)
                                    //if (DebugMode) Console.WriteLine("Loading " + RestOfLine);

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

                                        string FullFilename = Path.GetFullPath(ResolvePath(Filename, BinaryArguments[0].Replace("\"", "")).ToLower());


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
                                                    if (!AddNewLabel(SizeLabel, (int)BR.BaseStream.Length, false, Filename, CurrentLineNumber, PassNumber)) {
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
                                                CurrentPage.ProgramCounter += BinaryData.Length;
                                                break;
                                            case Pass.Assembling:
                                                ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, CurrentPage.ProgramCounter, Filename, BinaryData));
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
                                    string NewModuleName = RestOfLine.Trim('"');
                                    if (!IsCaseSensitive) NewModuleName = NewModuleName.ToLower();
                                    if (NewModuleName.Length == 0) {
                                        DisplayError(ErrorType.Warning, "Module name not specified (reverting to noname).", Filename, CurrentLineNumber);
                                        CurrentModule = "noname";
                                    } else {
                                        CurrentModule = NewModuleName;
                                    }
                                    Namespaces = new ArrayList();
                                    break;
                                    #endregion
                                case ".db":
                                case ".dw":
                                case ".text":
                                case ".byte":
                                case ".word":
                                case ".asc":
                                    #region Define data
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
                                                    CurrentPage.ProgramCounter += DataSize;
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
                                                CurrentPage.ProgramCounter += UnescapedString.Length * DataSize;
                                            }
                                        } else {
                                            CurrentData += Data[DataPointer];
                                        }

                                        ++DataPointer;
                                    }
                                    if (WriteListFile && PassNumber == Pass.Assembling) {
                                        ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, CurrentPage.ProgramCounter - DefinedData.Count, Filename, (byte[])DefinedData.ToArray(typeof(byte))));
                                    }
                                    break;
                                    #endregion
                                case ".block":
                                    #region Block
                                    try {
                                        CurrentPage.ProgramCounter += TranslateArgument(RestOfLine);
                                    } catch {
                                        DisplayError(ErrorType.Error, "'" + RestOfLine + "' is not a valid number.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    break;
                                    #endregion
                                case ".chk":
                                    #region Checksum
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
                                        for (int i = StartAddress; i < CurrentPage.ProgramCounter; ++i) {
                                            Checksum += (byte)CurrentPage.OutputBinary[i];
                                        }
                                        if (WriteListFile) ListFile.Add(new ListFileEntry(Command + " " + RestOfLine, CurrentLineNumber, CurrentPage.ProgramCounter, Filename, new byte[] { (byte)Checksum }));
                                        WriteToBinary(Checksum);
                                    } else {
                                        ++CurrentPage.ProgramCounter;
                                    }
                                    break;
                                    #endregion
                                case ".echo":
                                    #region Messages
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
                                    //if (PassNumber == Pass.Labels) {
                                    if (JustHitALabel == false) {
                                        DisplayError(ErrorType.Error, Command + " directive is invalid unless you have just declared a label.", Filename, CurrentLineNumber);
                                    } else {
                                        try {
                                            int NewValue = TranslateArgument(RestOfLine);
                                            AddNewLabel(JustHitLabelName, TranslateArgument(RestOfLine), true, Filename, CurrentLineNumber, PassNumber);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not assign value '" + RestOfLine + "' to label '" + JustHitLabelName + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        }
                                    }
                                    //}
                                    break;
                                    #endregion
                                case ".export":
                                    #region Label exporting
                                    if (PassNumber == Pass.Assembling) {
                                        string[] ExportVars = SafeSplit(RestOfLine, ',');
                                        foreach (string S in ExportVars) {
                                            string LabelName = FixLabelName(S);
                                            if (LabelName.StartsWith(CurrentLocalLabel)) LabelName = CurrentModule + "." + LabelName;
                                            if (Labels[LabelName] != null) {
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
                                                        CurrentPage.ProgramCounter - FilledData.Count,
                                                        Filename,
                                                        (byte[])FilledData.ToArray(typeof(byte))));
                                            } else {
                                                CurrentPage.ProgramCounter += FillSize * (Command == ".fill" ? 1 : 2);
                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not evaluate '" + FillArgs[Progress] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".end":
                                    #region End assembling
                                    return true;
                                    #endregion
                                case ".define":
                                case "#define":
                                    #region TASM macros
                                    // TASM macro
                                    try {
                                        AddMacroThroughDefinition(RestOfLine, Filename, CurrentLineNumber, PassNumber == Pass.Assembling);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    break;
                                    #endregion
                                case ".defcont":
                                case "#defcont":
                                    #region Continue macros
                                    if (LastReplacement == null) {
                                        DisplayError(ErrorType.Error, "No macro to continue from!");
                                    } else {
                                        LastMacro.Replacements.Remove(LastReplacement);
                                        LastReplacement.ReplacementString += UnalteredRestOfLine;
                                        LastMacro.Replacements.Add(LastReplacement);
                                        LastMacro.Replacements.Sort();
                                        AvailableMacros[LastMacro.Name] = LastMacro;
                                    }
                                    break;
                                    #endregion
                                case ".list":
                                case ".nolist":
                                    #region Listing
                                    WriteListFile = (Command == ".list");
                                    break;
                                    #endregion
                                case ".addinstr":
                                    #region Add instruction
                                    if (AddInstructionLine(RestOfLine)) {
                                        RehashInstructionTable();
                                    } else {
                                        DisplayError(ErrorType.Error, "Could not add instruction.", Filename, CurrentLineNumber);
                                    }
                                    break;
                                    #endregion
                                case ".variablename":
                                    #region Set variable name
                                    if (PassNumber == Pass.Labels) {
                                        VariableName = RestOfLine.Trim().Trim('"');
                                    }
                                    break;
                                    #endregion
                                case ".binarymode":
                                    #region Binary mode
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
                                            case "segams":
                                                BinaryType = Binary.SegaMS; break;
                                            case "segagg":
                                                BinaryType = Binary.SegaGG; break;
                                            default:
                                                DisplayError(ErrorType.Error, "Invalid binary mode '" + RestOfLine + "'", Filename, CurrentLineNumber);
                                                break;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".tivariabletype":
                                    #region TI variable type
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
                                                        CurrentPage.ProgramCounter - RandomData.Count,
                                                        Filename,
                                                        (byte[])RandomData.ToArray(typeof(byte))));

                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not generate random data: '" + ex.Message + "'.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }

                                        } else {
                                            CurrentPage.ProgramCounter += RndDataSize * RealRndArgs[0];
                                        }

                                    }

                                    break;
                                    #endregion
                                case ".var":
                                    #region Variable
                                    if (PassNumber != Pass.Labels) break;
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
                                        if (AddNewLabel(VariableName, VariableTableOff + VariableTable, false, Filename, CurrentLineNumber, PassNumber)) {
                                            bool DisplayedWarn = false;
                                            if (VariableTableSize != 0 && VariableTableOff >= VariableTableSize) {
                                                DisplayError(ErrorType.Warning, "Variable '" + VariableName + "' starts outside allocated variable table space.", Filename, CurrentLineNumber);
                                                DisplayedWarn = true;
                                            }
                                            VariableTableOff += VariableSize;
                                            if (VariableTableSize != 0 && VariableTableOff > VariableTableSize && !DisplayedWarn) DisplayError(ErrorType.Warning, "Variable '" + VariableName + "' doesn't completely fit into the free space of the current variable table.", Filename, CurrentLineNumber);
                                        } else {
                                            DisplayError(ErrorType.Error, "Could not add variable '" + VariableName + "' (previously defined label).", Filename, CurrentLineNumber);
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".varloc":
                                    #region Variable table location
                                    if (PassNumber != Pass.Labels) break;
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
                                    if (PassNumber != Pass.Assembling) break;
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
                                        CurrentPage.ProgramCounter += AnglesToUse.Count * TrigElementSize;
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
                                                CurrentPage.ProgramCounter - TrigData.Count,
                                                Filename,
                                                (byte[])TrigData.ToArray(typeof(byte))));
                                    }
                                    break;
                                    #endregion
                                case ".rlemode":
                                    #region RLE mode
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
                                case ".for":
                                    #region for-loop start
                                    string[] ForArgs = SafeSplit(RestOfLine, ',');
                                    int ForStart = 0;
                                    int ForEnd = 0;
                                    int ForStep = 1;
                                    if (ForArgs.Length < 3 || ForArgs.Length > 4) {
                                        DisplayError(ErrorType.Error, "For loops require 3 or 4 arguments: Variable, start, end, and (optionally) step.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    string ForLabel;
                                    try {
                                        ForLabel = FixLabelName(ForArgs[0]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }

                                    if (Labels[ForLabel] != null) {
                                        DisplayError(ErrorType.Error, "Label '" + ForLabel + "' already defined!", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    try {
                                        ForStart = TranslateArgument(ForArgs[1]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not calculate for-loop start: " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    try {
                                        ForEnd = TranslateArgument(ForArgs[2]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not calculate for-loop end: " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    try {
                                        if (ForArgs.Length == 4) {
                                            ForStep = TranslateArgument(ForArgs[3]);
                                        }
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not calculate for-loop step: " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    if (ForStep == 0 || ForStep > 0 && ForStart > ForEnd || ForStep < 0 && ForStart < ForEnd) {
                                        DisplayError(ErrorType.Error, "Infinite loop.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }

                                    // We've got this far, so we can assume it's a safe to loop
                                    AddNewLabel(ForLabel, ForStart, false, Filename, CurrentLineNumber, PassNumber);
                                    ForLoop NewForLoop = new ForLoop();
                                    //NewForLoop.Value = ForStart;
                                    NewForLoop.Start = ForStart;
                                    NewForLoop.End = ForEnd;
                                    NewForLoop.Step = ForStep;
                                    NewForLoop.Filename = Filename;
                                    NewForLoop.LineNumber = CurrentLineNumber;
                                    ForLoops[ForLabel] = NewForLoop;
                                    LastForLoop.Push(ForLabel);
                                    break;
                                    #endregion
                                case ".loop":
                                    #region for-loop looping
                                    string LoopLabel;
                                    try {
                                        LoopLabel = (string)LastForLoop.Peek();
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    object LoopBack = ForLoops[LoopLabel];
                                    if (LoopBack == null) {
                                        // Eh?
                                        DisplayError(ErrorType.Error, "For-loop label '" + LoopLabel + "' not found.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    } else {
                                        // We have found the loop.
                                        ForLoop FL = (ForLoop)LoopBack;
                                        if (FL.Filename.ToLower() != Filename.ToLower()) {
                                            DisplayError(ErrorType.Error, "You cannot loop from a different file to the one in which the for-loop was defined.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        int ForLoopValue = ((LabelDetails)Labels[LoopLabel]).Value;
                                        if ((FL.End < FL.Start && (ForLoopValue <= FL.End || ForLoopValue + FL.Step < FL.End))
                                         || (FL.End > FL.Start && (ForLoopValue >= FL.End || ForLoopValue + FL.Step > FL.End))) {
                                            // End
                                            // Clear up the variables
                                            Labels.Remove(LoopLabel);
                                            ForLoops.Remove(LoopLabel);
                                            LastForLoop.Pop();
                                        } else {
                                            // Carry on
                                            CurrentLineNumber = FL.LineNumber;
                                            ForLoopValue += FL.Step;
                                            Labels[LoopLabel] = new LabelDetails(LoopLabel, ForLoopValue, Filename, CurrentLineNumber);
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".fopen":
                                    #region File open
                                    string[] FileOpenArgs = SafeSplit(RestOfLine, ',');
                                    if (FileOpenArgs.Length != 2) {
                                        DisplayError(ErrorType.Error, ".fopen expects two arguments.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }
                                    string HandleName = FileOpenArgs[0].Trim();
                                    HandleName = IsCaseSensitive ? HandleName : HandleName.ToLower();
                                    if (FileHandles[HandleName] != null) {
                                        DisplayError(ErrorType.Error, "File handle '" + HandleName + "' already opened.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }
                                    FileStream F;
                                    try {
                                        string ToOpen = ResolvePath(Filename, FileOpenArgs[1].Trim().Replace("\"", ""));
                                        F = new FileStream(ToOpen, FileMode.Open);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }
                                    FileHandles[HandleName] = new BinaryReader(F);
                                    break;
                                    #endregion                                    
                                case ".fread":
                                case ".freadw":
                                case ".fpeek":
                                case ".fpeekw":
                                case ".fsize":
                                case ".fpos":
                                case ".fseek":
                                    #region File read
                                    string[] FileReadArgs = SafeSplit(RestOfLine, ',');
                                    if (FileReadArgs.Length != 2) {
                                        DisplayError(ErrorType.Error, Command + " expects two arguments.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }

                                    string ReadLabelName = "";
                                    int SeekPos = 0;
                                    try {
                                        if (Command != ".fseek") {
                                            ReadLabelName = FixLabelName(FileReadArgs[1]);
                                        } else {
                                            SeekPos = TranslateArgument(FileReadArgs[1]);
                                        }
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }

                                    string ReadHandleName = IsCaseSensitive ? FileReadArgs[0] : FileReadArgs[0].ToLower();
                                    object FileReadReader = FileHandles[ReadHandleName];
                                    if (FileReadReader == null) {
                                        DisplayError(ErrorType.Error, "File handle '" + ReadHandleName + "' not found.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    } else {
                                        BinaryReader BR = (BinaryReader)FileReadReader;
                                        try {
                                            switch (Command) {
                                                case ".fread":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.ReadByte(), Filename, CurrentLineNumber);
                                                    break;
                                                case ".freadw":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.ReadInt16(), Filename, CurrentLineNumber);
                                                    break;
                                                case ".fpeek":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.ReadByte(), Filename, CurrentLineNumber);
                                                    BR.BaseStream.Seek(-1, SeekOrigin.Current);
                                                    break;
                                                case ".fpeekw":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.ReadInt16(), Filename, CurrentLineNumber);
                                                    BR.BaseStream.Seek(-2, SeekOrigin.Current);
                                                    break;
                                                case ".fsize":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.BaseStream.Length, Filename, CurrentLineNumber);
                                                    break;
                                                case ".fpos":
                                                    Labels[ReadLabelName] = new LabelDetails(ReadLabelName, (int)BR.BaseStream.Position, Filename, CurrentLineNumber);
                                                    break;
                                                case ".fseek":
                                                    BR.BaseStream.Seek(SeekPos, SeekOrigin.Begin);
                                                    break;

                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                    }
                                    break;
                                    #endregion                                    
                                case ".fclose":
                                    #region File close
                                    string CloseHandleName = IsCaseSensitive ? RestOfLine.Trim() : RestOfLine.Trim().ToLower();
                                    object FileCloseReader = FileHandles[CloseHandleName];
                                    if (FileCloseReader == null) {
                                        DisplayError(ErrorType.Error, "File handle '" + CloseHandleName + "' not found.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    } else {
                                        BinaryReader BR = (BinaryReader)FileCloseReader;
                                        try {
                                            BR.Close();
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".defpage":
                                    #region Create a page
                                    string[] PageArgs = SafeSplit(RestOfLine, ',');
                                    // Page number, address[, size [, origin]]
                                    if (PageArgs.Length < 2 || PageArgs.Length > 4) {
                                        DisplayError(ErrorType.Error, "Page definitions require 2 to 4 arguments.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    int PageNumber;
                                    try {
                                        PageNumber = TranslateArgument(PageArgs[0]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Invalid page number - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    bool IsValidPage = true;
                                    if (Pages[PageNumber] != null) {
                                        if (!(PageNumber == 0 && CanStillDefinePage0)) {
                                            IsValidPage = false;
                                            DisplayError(ErrorType.Error, "You cannot redefine page " + PageNumber + ".", Filename, CurrentLineNumber);
                                        }
                                    }
                                    if (!IsValidPage) {
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    int PageAddress;
                                    try {
                                        PageAddress = TranslateArgument(PageArgs[1]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Invalid page address - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }

                                    int PageSize = 0x10000;
                                    try {
                                        if (PageArgs.Length > 2) PageSize = TranslateArgument(PageArgs[2]);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Invalid page size - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    if (PageSize > 0x10000) {
                                        DisplayError(ErrorType.Error, "A page size of " + PageSize.ToString() + " bytes is too large.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    int PageOrg = 0x0000;
                                    try {
                                        if (PageArgs.Length > 3) PageOrg = TranslateArgument(PageArgs[3]);
                                        if (PageOrg >= PageSize + PageAddress) throw new Exception("Origin is beyond the page boundary.");
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Invalid page origin - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }


                                    BinaryPage NewPage = new BinaryPage(PageNumber, PageAddress, PageSize, PageOrg);
                                    CanStillDefinePage0 = false;
                                    Pages[PageNumber] = NewPage;
                                    break;
                                    #endregion                                    
                                case ".page":
                                    #region Switch page
                                    int PageToSwitchTo;
                                    try {
                                        PageToSwitchTo = TranslateArgument(RestOfLine);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Invalid page - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    if (Pages[PageToSwitchTo] == null) {
                                        DisplayError(ErrorType.Error, "Page " + PageToSwitchTo.ToString() + " not defined.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    } else {
                                        CurrentPage = (BinaryPage)Pages[PageToSwitchTo];
                                    }
                                    break;
                                    #endregion
                                case ".binaryrange":
                                    #region Binary output range
                                    string[] RangeMarkers = SafeSplit(RestOfLine, ',');
                                    if (RangeMarkers.Length != 2) {
                                        DisplayError(ErrorType.Error, "Binary range requires both a start and an end value.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                        break;
                                    }
                                    try {
                                        StartOutputAddress = TranslateArgument(RangeMarkers[0]);
                                        EndOutputAddress = TranslateArgument(RangeMarkers[1]);
                                        OnlyOutputMinimalData = false;
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Error setting binary range  - " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    break;
                                    #endregion
                                case ".binaryfill":
                                    #region Binary fill character
                                    if (PassNumber == Pass.Assembling) {
                                        try {
                                            BinaryFillChar = (byte)TranslateArgument(RestOfLine);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not set binary fill value correctly - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".sdsctag":
                                    #region SDSC tag (Sega)
                                    if (PassNumber == Pass.Assembling) {
                                        HasSdscTag = false;
                                        string[] SdscTagData = SafeSplit(RestOfLine, ',');
                                        if (SdscTagData.Length != 4) {
                                            DisplayError(ErrorType.Error, "SDSC tags must have version, title, notes and author.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }
                                        string[] SdscVersion = SafeSplit(SdscTagData[0], '.');
                                        try {
                                            SdscMajorVersionNumber = (byte)TranslateArgument(SdscVersion[0]);
                                            if (SdscVersion.Length > 1) {
                                                SdscMinorVersionNumber = (byte)TranslateArgument(SdscVersion[1]);
                                            } else {
                                                SdscMinorVersionNumber = 0;
                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "SDSC version error: " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }
                                        try {
                                            for (int i = 1; i < 4; ++i) {

                                                SdscString ToAffect = null;
                                                switch (i) {
                                                    case 1: ToAffect = SdscTitle; break;
                                                    case 2: ToAffect = SdscDescription; break;
                                                    case 3: ToAffect = SdscAuthor; break;
                                                }
                                                string SdscText = SdscTagData[i].Trim();
                                                if (SdscText.StartsWith("\"") && SdscText.EndsWith("\"") && SdscText.Length > 1) {
                                                    ToAffect.PointerCreated = false;
                                                    ToAffect.Value = UnescapeString(SdscText.Substring(1, SdscText.Length - 2));
                                                } else {
                                                    ToAffect.PointerCreated = true;
                                                    int TagPointer = TranslateArgument(SdscText);
                                                    if (TagPointer < 0 || TagPointer > 0xFFFF) {
                                                        throw new Exception("Description string must lie within the $0000-$FFFF range.");
                                                    }
                                                    ToAffect.Pointer = (ushort)TagPointer;
                                                }
                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "SDSC tag error: " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }
                                        HasSdscTag = true;
                                    }
                                    break;
                                    #endregion
                                case ".segaregion":
                                    #region Sega Region
                                    if (PassNumber == Pass.Assembling) {
                                        switch (RestOfLine.ToLower().Trim()) {
                                            case "japan":
                                                SegaRegion = Region.Japan;
                                                break;
                                            case "export":
                                                SegaRegion = Region.Export;
                                                break;
                                            case "international":
                                                SegaRegion = Region.International;
                                                break;
                                            default:
                                                DisplayError(ErrorType.Warning, "Could not understand region '" + RestOfLine.Trim() + "'.", Filename, CurrentLineNumber);
                                                break;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".segapart":
                                    #region Sega Part Number
                                    if (PassNumber == Pass.Assembling) {
                                        try {
                                            SegaPart = BCD((ushort)TranslateArgument(RestOfLine));
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not set Sega ROM part number: " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".segaversion":
                                    #region Sega Version
                                    if (PassNumber == Pass.Assembling) {
                                        try {
                                            string[] SegaVer = SafeSplit(RestOfLine, '.');
                                            int VerMajor = TranslateArgument(SegaVer[0]);
                                            int VerMinor = SegaVer.Length == 1 ? 0 : TranslateArgument(SegaVer[1]);
                                            SegaVersion = (byte)((VerMinor & 0xF) | ((VerMajor & 0xF) << 4));
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not set Sega ROM version: " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }
                                    }
                                    break;
                                    #endregion
                                case ".incbmp":
                                    #region Monochrome bitmap inclusion
                                    string[] IncBmpArgs = SafeSplit(RestOfLine, ',');
                                    try {
                                        if (IncBmpArgs.Length < 1 || IncBmpArgs.Length > 3) throw new Exception("Invalid number of arguments.");
                                        using (Bitmap B = new Bitmap(ResolvePath(Filename,IncBmpArgs[0].Replace("\"", "")))) {
                                            bool CanRle = false;
                                            if (B.Width != 0) {
                                                int ByteWidth = 1 + ((B.Width - 1) >> 3);
                                                /*if (PassNumber == Pass.Labels) {
                                                    CurrentPage.ProgramCounter += (B.Height * ByteWidth);
                                                } else {*/
                                                int BrightnessLimiter = 127;

                                                for (int i = 1; i < IncBmpArgs.Length; i++) {
                                                    if (IncBmpArgs[i].ToLower().Trim() == "rle") {
                                                        CanRle = true;
                                                    } else {
                                                        BrightnessLimiter = TranslateArgument(IncBmpArgs[i]);
                                                    }
                                                }
                                                byte[] ToAdd = new byte[B.Height * ByteWidth];
                                                int AddIndex = 0;
                                                for (int y = 0; y < B.Height; ++y) {
                                                    for (int x = 0; x < ByteWidth; ++x) {
                                                        byte Row = 0x00;
                                                        for (int i = 0; i < 8; ++i) {
                                                            Row <<= 1;
                                                            if (i + x * 8 < B.Width) {
                                                                int Pixel = B.GetPixel(i + x * 8, y).ToArgb();
                                                                int ComparePixel = Pixel & 0xFF;
                                                                Pixel >>= 8; ComparePixel += Pixel & 0xFF;
                                                                Pixel >>= 8; ComparePixel += Pixel & 0xFF;
                                                                ComparePixel /= 3;
                                                                if (ComparePixel < BrightnessLimiter) {
                                                                    Row |= 0x01;
                                                                }
                                                            }
                                                        }
                                                        ToAdd[AddIndex++] = Row;
                                                    }
                                                }
                                                if (CanRle) ToAdd = RLE(ToAdd);
                                                if (PassNumber == Pass.Labels) {
                                                    CurrentPage.ProgramCounter += ToAdd.Length;
                                                } else {
                                                    for (int i = 0; i < ToAdd.Length; i++) {
                                                        WriteToBinary(ToAdd[i]);
                                                    }
                                                }
                                            }
                                            //}
                                        }
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Bitmap inclusion error: " + ex.Message, Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }
                                    break;
                                    #endregion
                                case ".using":
                                    #region 'Using' namespaces
                                    string UsingName = IsCaseSensitive ? RestOfLine : RestOfLine.ToLower();
                                    if (Namespaces.Contains(UsingName)) {
                                        DisplayError(ErrorType.Warning, "Module " + RestOfLine + " already in use.", Filename, CurrentLineNumber);
                                    } else {
                                        Namespaces.Add(UsingName);
                                    }
                                    break;
                                    #endregion
                                case ".endmodule":
                                    #region End module
                                    CurrentModule = "noname";
                                    Namespaces = new ArrayList();
                                    break;
                                    #endregion
                                case ".varfree":
                                    #region Variable table free memory pointer
                                    if (PassNumber != Pass.Labels) break;
                                    if (RestOfLine == "") {
                                        DisplayError(ErrorType.Error, ".varfree expects a variable name!", Filename, CurrentLineNumber);
                                        if (StrictMode) return false; break;
                                    }
                                    try {
                                        AddNewLabel(FixLabelName(RestOfLine), VariableTableOff + VariableTable, false, Filename, CurrentLineNumber, PassNumber);
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not assign value: " + ex.Message, Filename, CurrentLineNumber);
                                    }
                                    break;
                                    #endregion
                                case ".relocate":
                                    #region Relocatable code block
                                        try {
                                            RelocationOffset = TranslateArgument(RestOfLine) - CurrentPage.ProgramCounter;
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not set relocation offset - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                    break;
                                    #endregion
                                case ".endrelocate":
                                    #region End relocatable code block
                                        RelocationOffset = 0;
                                    break;
                                    #endregion
                                case ".undef":
                                case "#undef":
                                    #region Undefine macro
                                    if (IsCaseSensitive) RestOfLine = RestOfLine.ToLower();
                                    RestOfLine = RestOfLine.Trim();
                                    object CheckMacro =  AvailableMacros[RestOfLine];
                                    if (CheckMacro == null) {
                                        DisplayError(ErrorType.Error, "Macro " + RestOfLine + " could not be undefined.", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    } else {
                                        AvailableMacros.Remove(RestOfLine);
                                    }
                                    break;
                                    #endregion
                                default:
                                    DisplayError(ErrorType.Error, "Unsupported directive '" + Command + "'.", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                    break;
                            }
                            break;
                    }
                    if (SourceLine != "") goto CarryOnAssembling;
                    #endregion
                } else if ((bool)ConditionalStack.Peek()) {
                    if (FindFirstChar == 0) {
                        #region Label Detection
                        // Label
                        int EndOfLabel = SourceLine.IndexOfAny("\t .#:".ToCharArray());
                        string CheckLabelName;
                        if (EndOfLabel == -1) {
                            CheckLabelName = SourceLine.Trim().Replace(":", "");
                        } else {
                            CheckLabelName = SourceLine.Remove(EndOfLabel);
                            SourceLine = SourceLine.Substring(EndOfLabel + 1);
                        }
                        if (CheckLabelName.Length != 0) {
                            bool IsReusable = (CheckLabelName == "@") || (CheckLabelName.Replace("+", "") == "") || (CheckLabelName.Replace("-", "") == "");
                            if (PassNumber == Pass.Labels || IsReusable) {
                                if (!IsReusable) {
                                    JustHitALabel = true;
                                    JustHitLabelName = CheckLabelName;
                                }
                                if (!AddNewLabel(CheckLabelName, CurrentPage.ProgramCounter + RelocationOffset, false, Filename, CurrentLineNumber, PassNumber)) {
                                    DisplayError(ErrorType.Error, "Could not create new label '" + JustHitLabelName + "' (previously defined?)", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                }
                            } else {
                                JustHitALabel = true;
                                JustHitLabelName = CheckLabelName;
                            }
                        }
                        if (EndOfLabel != -1 && SourceLine.Trim() != "") {
                            SourceLine = " " + SourceLine;
                            goto CarryOnAssembling;
                        }
                        #endregion
                    } else {
                        JustHitALabel = false;  // Clear that
                        // Assembly

                        if (PassNumber == Pass.Labels) {
                            // Work out what it is, and save it.
                            string Instr = "";
                            while (FindFirstChar < SourceLine.Length && SourceLine[FindFirstChar] != ' ' && SourceLine[FindFirstChar] != '\t') {
                                Instr += SourceLine[FindFirstChar];
                                ++FindFirstChar;
                            }
                            Instr = Instr.ToLower();
                            if (Instr == "\\") {
                                SourceLine = SourceLine.Substring(FindFirstChar);
                                goto CarryOnAssembling;
                            }
                            if (Instructions[Instr] == null) {
                                DisplayError(ErrorType.Error, "Instruction '" + Instr + "' not understood.", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            } else {
                                string Args = "";

                                ArrayList H = (ArrayList)Instructions[Instr];

                                Instruction I = null;

                                string[] MultipleStatements = SafeSplit(SourceLine, '\\');
                                if (MultipleStatements.Length == 0) {
                                    DisplayError(ErrorType.Error, "Internal assembler error.", Filename, CurrentLineNumber);
                                    return false;
                                } 

                                // Strip out whitespace
                                Args = SafeStripWhitespace(MultipleStatements[0].Substring(FindFirstChar));

                                ArrayList SourceArgs = new ArrayList();

                                foreach (Instruction FindI in H) {
                                    string Arg = FindI.Arguments;
                                    if (MatchWildcards(Arg, Args, ref SourceArgs)) {
                                        I = FindI;
                                        break;
                                    }
                                }
                                if (I == null) {
                                    DisplayError(ErrorType.Error, "Argument '" + Args + "' (for '" + Instr + "') not understood.", Filename, CurrentLineNumber);

                                    MatchedAssembly.Enqueue(new Instruction());
                                    MatchedAssembly.Enqueue(Args);
                                    MatchedAssembly.Enqueue(FindFirstChar);
                                    if (StrictMode) return false;
                                } else {

                                    FindFirstChar = MultipleStatements[0].Length;

                                    MatchedAssembly.Enqueue(I);
                                    MatchedAssembly.Enqueue(Args);
                                    MatchedAssembly.Enqueue(FindFirstChar);

                                    CurrentPage.ProgramCounter += I.Size;
                                    SourceLine = "";
                                    for (int i = 1; i < MultipleStatements.Length; ++i) {
                                        SourceLine += @"\" + MultipleStatements[i];
                                    }
                                    goto CarryOnAssembling;
                                }
                            }
                        } else {
                            #region Assemble
                            Instruction I = (Instruction)MatchedAssembly.Dequeue();
                            string Args = (string)MatchedAssembly.Dequeue();
                            FindFirstChar = (int)MatchedAssembly.Dequeue();

                            byte[] InstructionBytes = new byte[I.Size];
                            for (int i = 0; i < I.Opcodes.Length; ++i) {
                                InstructionBytes[i] = I.Opcodes[i];
                            }
                            ArrayList SourceArgs = ExtractArguments(I.Arguments, Args);

                            for (int i = 0; i < SourceArgs.Count; ++i) {
                                string TestArg = (string)SourceArgs[i];
                                if (TestArg == "" && !(I.Rule == Instruction.InstructionRule.ZIdX && i == 0)) {
                                    DisplayError(ErrorType.Warning, "Missing argument? (Expected " + I.Name + " " + I.Arguments + ")", Filename, CurrentLineNumber);
                                }
                            }


                            ArrayList AdjustedArgs = new ArrayList();

                            int RealArgument = 0;
                            try {
                                RealArgument = (((SourceArgs.Count == 0) ? 0 : TranslateArgument((string)SourceArgs[0])));
                            } catch (Exception ex) {
                                DisplayError(ErrorType.Error, "Could not parse expression '" + SourceArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            }

                            if (I.Rule != Instruction.InstructionRule.ZBit && I.Rule != Instruction.InstructionRule.ZIdX) {
                                RealArgument <<= I.Shift;
                                if (I.Rule == Instruction.InstructionRule.Swap) {
                                    I.Rule = Instruction.InstructionRule.NoTouch;
                                    RealArgument = ((RealArgument & 0xFF) << 8) | (RealArgument >> 8);
                                }
                                RealArgument |= I.Or;
                            }


                            switch (I.Rule) {

                                case Instruction.InstructionRule.NoTouch:
                                    for (int i = I.Opcodes.Length; i < I.Size; ++i) {
                                        InstructionBytes[i] = (byte)(RealArgument & 0xFF);
                                        RealArgument >>= 8;
                                    }
                                    break;
                                case Instruction.InstructionRule.R1:
                                    RealArgument -= (CurrentPage.ProgramCounter + I.Size + RelocationOffset);
                                    if (RealArgument > 127 || RealArgument < -128) {
                                        DisplayError(ErrorType.Error, "Range of relative jump exceeded. (" + RealArgument + " bytes)", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }
                                    InstructionBytes[InstructionBytes.Length - 1] = (byte)RealArgument;
                                    break;
                                case Instruction.InstructionRule.R2:
                                    RealArgument -= (CurrentPage.ProgramCounter + I.Size + RelocationOffset);
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
                                            for (int j = I.Opcodes.Length; j < I.Size; ++j) {
                                                InstructionBytes[j] = (byte)(RealArgument & 0xFF);
                                                RealArgument >>= 8;
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
                                ListFile.Add(new ListFileEntry(FindFirstChar == SourceLine.Length ? SourceLine : SourceLine.Remove(FindFirstChar), CurrentLineNumber, CurrentPage.ProgramCounter, Filename, InstructionBytes));
                            }
                            foreach (byte B in InstructionBytes) {
                                WriteToBinary(B);
                            }

                            //ProgramCounter += I.Size;
                            SourceLine = SourceLine.Substring(FindFirstChar);
                            goto CarryOnAssembling;
                            #endregion

                        }
                    }
                }
            }

            return true;
        }
    }
}
