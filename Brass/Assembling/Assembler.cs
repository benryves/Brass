
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Drawing;

namespace Brass {
    public partial class Program {

        //public static Dictionary<string,LabelDetails> Labels;         // Stores label names -> address mapping.

        public static Dictionary<string, InstructionGroup> Instructions;   // Stores instructions -> Instruction class mapping (eg 'LD').

        public static Dictionary<string, string[]> SourceFiles;    // Loaded source files.
        public static Dictionary<string, bool[]> HasLabelInIt; // Marks whether a line has a label in it
        public static Hashtable BinaryFiles;    // Loaded binary files.

        public static bool IsCaseSensitive = false; // Case sensitive or not?

        public static Stack<Module> Modules;

        public static string CurrentLocalLabel; // Current local label definition.

        public static bool AllLabelsLocal;

        public enum Pass { Labels, Assembling } // Which pass are we on?

        public static Dictionary<int, bool> ConditionalHasBeenTrue; // Remembering if a conditional on this level has ever been true.
        public static Stack<bool> ConditionalStack;   // Stack of conditionals keeping track of true/false/FileNotFound

        public static Macro LastMacro;
        public static MacroReplacement LastReplacement;

        private static Dictionary<int, ReusableLabelTracker>[] ReusableLabels = { new Dictionary<int, ReusableLabelTracker>(), new Dictionary<int, ReusableLabelTracker>() }; // + or - : This is pure magic ;)

        public static ArrayList BookmarkLabels; // @
        public static int BookmarkIndex;

        public static Dictionary<LabelDetails, ForLoop> ForLoops;       // Hold for-loop label names->for-loop data

        public static Dictionary<char, byte> ASCIITable;        // ASCII mapping table


        public static List<Instruction> AllInstructions;


        public static Dictionary<uint, List<ListFileEntry>> AllListFiles;
        public static bool WriteListFile = false;
        public static List<ListFileEntry> ListFile;

        public static Dictionary<string, BinaryReader> FileHandles;

        private static Queue<MatchedAssemblyNugget> MatchedAssembly;

        public static uint StartOutputAddress = 0;
        public static uint EndOutputAddress = 0;

        public static byte BinaryFillChar = 0xFF;

        public static int RelocationOffset = 0x0000;

        private static bool AmExportingLabels = false;
        private static bool AmAssembling = true;
        private static bool AmDefiningLongMacro = false;

        private static int UniqueMacroIndex;
        private static int RepeatLabelCount = 0;

        private static bool NestableModules = false;

        private static Random RandomSource = new Random();

        private static bool SquishedData;

        private static bool DeclaringStruct = false;

        private static string BranchTableRule;

        public static string CurrentFilename = "";
        public static int CurrentLineNumber = 0;
        private static Stack<int> OldLineNumbers;
        private static Stack<string> OldFilenames;

        private static Queue<byte[]> QueuedBinaryFileData;

        private class MatchedAssemblyNugget {
            public Instruction MatchedInstruction;
            public List<string> Arguments;
            public int FindFirstChar;
			public string Filename;
			public int LineNumber;

			public MatchedAssemblyNugget(int FindFirstChar, string filename, int lineNumber) {
                this.FindFirstChar = FindFirstChar;
				this.Filename = filename;
				this.LineNumber = lineNumber;
            }
            public MatchedAssemblyNugget(Instruction MatchedInstruction, List<string> Arguments, int FindFirstChar, string filename, int lineNumber) {
                this.MatchedInstruction = MatchedInstruction;
                this.Arguments = Arguments;
                this.FindFirstChar = FindFirstChar;
				this.Filename = filename;
				this.LineNumber = lineNumber;
            }
        }



        private static string ResolvePath(string CurrentFile, string RemoteFile) {
            string Closest = Path.Combine(Path.GetDirectoryName(CurrentFile.Replace("\"", "")), RemoteFile.Replace("\"", ""));
            if (File.Exists(Closest)) return Closest;
            return RemoteFile;
        }



        public class ListFileEntry {
            public string Source;
            public int Line;
            public uint Address;
            public string File;
            public byte[] Data;
            public ListFileEntry(string Source, int Line, uint Address, string File, byte[] Data) {
                this.Source = Source;
                this.Line = Line;
                this.Address = Address;
                this.File = File;
                this.Data = Data;
            }
        }




        /// <summary>
        /// Reset the state of the assembler (needs doing at the start of each pass).
        /// </summary>
        public static void ResetStateOnPass() {
            AllLabelsLocal = false;

            WriteListFile = true;
            ConditionalStack = new Stack<bool>(128);
            ConditionalStack.Push(true);
            ConditionalHasBeenTrue = new Dictionary<int, bool>(128);
            ConditionalHasBeenTrue[0] = true;
            ForLoops = new Dictionary<LabelDetails, ForLoop>(64);
            RLE_Flag = 0x91;
            RLE_ValueFirst = true;
            LastReplacement = null;
            LastMacro = null;
            AvailableMacros = new Dictionary<string, Macro>(512);
            CurrentLocalLabel = "_";

            CurrentPage = new BinaryPage();
            Pages = new Dictionary<uint, BinaryPage>();
            Pages.Add(0, CurrentPage);
            CanStillDefinePage0 = true;

            LastForLoop = new Stack<LabelDetails>(64);
            CloseFileHandles();

            BookmarkIndex = 0;

            for (int i = 0; i < 2; i++) {
                foreach (KeyValuePair<int, ReusableLabelTracker> KVP in ReusableLabels[i]) {
                    KVP.Value.Index = 0;
                }
            }

            AmAssembling = true;

            RelocationOffset = 0x0000;



            AmDefiningLongMacro = false;

            CurrentModule = NoName;
            Modules = new Stack<Module>(128);

            NestableModules = false;
            SquishedData = true;
            DeclaringStruct = false;

            OldLineNumbers = new Stack<int>();
            OldFilenames = new Stack<string>();

            BranchTableRule = "_{*}";

            ASCIITable = new Dictionary<char, byte>();

        }

        public static void CloseFileHandles() {
            if (FileHandles != null) {
                foreach (KeyValuePair<string, BinaryReader> V in FileHandles) {
                    V.Value.Close();
                }
            }
            FileHandles = new Dictionary<string, BinaryReader>();
        }

        /// <summary>
        /// Start assembling a source file
        /// </summary>
        /// <param name="Filename">Filename to start from</param>
        /// <returns>True on success, false on errors</returns>
        public static bool AssembleFile(string Filename) {

            UniqueMacroIndex = -1;

            SourceFiles = new Dictionary<string, string[]>(64);
            BinaryFiles = new Hashtable(64);
            BookmarkLabels = new ArrayList(32000);
            MatchedAssembly = new Queue<MatchedAssemblyNugget>(64000);
            ResetStateOnPass();

            VariableAreas = new List<VariableArea>(16);
            Structs = new Dictionary<string, Struct>(32);
            QueuedBinaryFileData = new Queue<byte[]>();

            DateTime StartTime = DateTime.Now;

            if (AssembleFile(Filename, Pass.Labels)) {
                TimeSpan PassTime = DateTime.Now - StartTime;
                Console.WriteLine("Pass 1 complete. ({0}ms).", (int)(PassTime.Seconds * 1000) + (int)PassTime.Milliseconds);

                foreach (Error E in ErrorLog) {
                    if (E.E == ErrorType.Error) {
                        DisplayError(ErrorType.Message, "Pass 1 failed, pass 2 therefore skipped.\n");
                        return false;
                    }
                }

                Breakpoints = new List<Breakpoint>(16);
                OutputAddresses = new List<OutputAddress>(1024);

                AllocateVariables();

                foreach (Error E in ErrorLog) {
                    if (E.E == ErrorType.Error) {
                        DisplayError(ErrorType.Error, "Could not fit all variables into variable areas.\n");
                        return false;
                    }
                }

				// Do we need to correct variable allocation?
				StartTime = DateTime.Now;

				/*foreach (var item in GetAllLabels()) {
					
				}*/
				

				ResetStateOnPass();
				if (AssembleFile(Filename, Pass.Labels)) {
					PassTime = DateTime.Now - StartTime;
					Console.WriteLine("Pass 1 repeated to correct variables ({0}ms).", (int)PassTime.TotalMilliseconds);
				} else {
					return false;
				}

                StartTime = DateTime.Now;

                AllListFiles = new Dictionary<uint, List<ListFileEntry>>(8);
                ListFile = new List<ListFileEntry>(32000);
                AllListFiles.Add(0, ListFile);



                ResetStateOnPass();
                bool Ret = AssembleFile(Filename, Pass.Assembling);
                PassTime = DateTime.Now - StartTime;
                Console.WriteLine("Pass 2 complete. ({0}ms).", (int)PassTime.TotalMilliseconds);
                return Ret;
            } else {
                return false;
            }
        }

        public static int AssembledFileCount = 0;

        /// <summary>
        /// Assemble a source file
        /// </summary>
        /// <param name="Filename">Filename of the source file to assemble</param>
        /// <returns>True on success, False on any failure</returns>
        public static bool AssembleFile(string Filename, Pass PassNumber) {

            bool InsideComment = false;
            int LineCommentOpened = 0;

            CurrentFilename = Path.GetFullPath(Filename);

            if (PassNumber == Pass.Assembling) ++AssembledFileCount;

            string[] RealSourceLines;
            if (!SourceFiles.TryGetValue(CurrentFilename, out RealSourceLines)) {
                TextReader SourceFile;
                try {
                    SourceFile = new StreamReader(CurrentFilename);
                } catch (Exception) {
                    DisplayError(ErrorType.Error, "Could not open file " + CurrentFilename);
                    return false;
                }
                RealSourceLines = SourceFile.ReadToEnd().Replace("\r", "").Split('\n');
                SourceFiles.Add(CurrentFilename, RealSourceLines);
                SourceFile.Close();
            }

            if (RealSourceLines.Length == 0) return true;

            string RealSourceLine = RealSourceLines[0];
            CurrentLineNumber = 0;

            bool JustHitALabel = false;
            string JustHitLabelName = "";
            while (CurrentLineNumber++ < RealSourceLines.Length) {
                RealSourceLine = RealSourceLines[CurrentLineNumber - 1];


                #region Handle multiline /* */ comments

                if (InsideComment) {
                    int CheckEnd = RealSourceLine.IndexOf("*/");
                    if (CheckEnd != -1) {
                        RealSourceLine = RealSourceLine.Substring(CheckEnd + 2);
                        InsideComment = false;
                    } else {
                        RealSourceLine = "";
                    }
                } else if (RealSourceLine.IndexOf("/*") != -1) {
                TryStrippingCommentsAgain:
                    int CheckStart = GetSafeIndexOf(RealSourceLine, '*');
                    int CheckInsideComment = GetSafeIndexOf(RealSourceLine, ';');
                    if (CheckInsideComment != -1 && CheckInsideComment < CheckStart) continue;
                    while (CheckStart != -1 && CheckStart > 0) {
                        if (RealSourceLine[CheckStart - 1] == '/') {
                            LineCommentOpened = CurrentLineNumber;
                            int EndComment = RealSourceLine.IndexOf("*/");
                            if (EndComment == -1) {
                                RealSourceLine = RealSourceLine.Remove(CheckStart - 1);
                                InsideComment = true;

                            } else {
                                RealSourceLine = RealSourceLine.Remove(CheckStart - 1) + RealSourceLine.Substring(EndComment + 2);
                                InsideComment = false;
                            }
                            goto TryStrippingCommentsAgain;
                        }
                        CheckStart = GetSafeIndexOf(RealSourceLine, '*', CheckStart + 1);
                    }
                }

                #endregion

                #region Macro preprocessor
                if (PassNumber == Pass.Labels) {

                    string SourceLine = "";
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
                    SourceFiles[CurrentFilename][CurrentLineNumber - 1] = SourceLine;
                    RealSourceLine = SourceLine;

                    if (InsideComment) continue;
                } else {
                    RealSourceLine = SourceFiles[CurrentFilename][CurrentLineNumber - 1];
                }
                #endregion




                // Now we get to do all sorts of assembling fun.
                bool NotYetAsmFailed = true;
                string[] SplitLines = SafeSplit(RealSourceLine, '\\');
                for (int LinePart = 0; LinePart < SplitLines.Length; ++LinePart) {
                    NotYetAsmFailed = true;
                    string SourceLine = SplitLines[LinePart];
                CarryOnAssembling:
                    //Console.WriteLine("-->{0}<--",SourceLine);
                    // Fix up the conditionals:
                    if ((bool)ConditionalStack.Peek()) {
                        ConditionalHasBeenTrue[ConditionalStack.Count - 1] = true;
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
                        //string UnalteredRestOfLine = SourceLine.Substring(FindFirstChar);
                        string FullRestOfLine = SourceLine.Substring(FindFirstChar).Trim();
                        int SplitLine = GetSafeIndexOf(FullRestOfLine, '\\');
                        string RestOfLine = FullRestOfLine;

                        Command = Command.ToLower();

                        if (!AmAssembling) {
                            AmAssembling = (Command == ".asm");
                            continue;
                        }

                        if (AmDefiningLongMacro) {
                            AmDefiningLongMacro = !(Command == ".enddeflong");
                            if (AmDefiningLongMacro) {
                                if (LastReplacement == null) {
                                    DisplayError(ErrorType.Error, "Could not add to macro (no macro to add to!)", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                } else {
                                    LastMacro.Replacements.Remove(LastReplacement);
                                    LastReplacement.ReplacementString += @"\ " + RealSourceLine;
                                    LastMacro.Replacements.Add(LastReplacement);
                                    LastMacro.Replacements.Sort();
                                    AvailableMacros[LastMacro.Name] = LastMacro;
                                    continue;
                                }
                            }
                        }


                        switch (Command) {
                            case ".ifdef":
                            case "#ifdef":
                            case ".ifndef":
                            case "#ifndef":
                                #region Conditional defines
                                if ((bool)ConditionalStack.Peek() == true) {
                                    //if (DebugMode) Console.WriteLine("Checking " + RestOfLine);
                                    bool CheckDefine = AvailableMacros.ContainsKey(IsCaseSensitive ? RestOfLine : RestOfLine.ToLower());
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
                                    bool CheckConditions;
                                    if (ConditionalHasBeenTrue.TryGetValue(ConditionalStack.Count, out CheckConditions) && CheckConditions) {
                                        ConditionalStack.Push(false);
                                    } else {
                                        bool CheckDefine = AvailableMacros.ContainsKey(IsCaseSensitive ? RestOfLine : RestOfLine.ToLower());
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
                                        Result = IntEvaluate(RestOfLine);
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
                                        bool CheckConditions;
                                        if (ConditionalHasBeenTrue.TryGetValue(ConditionalStack.Count, out CheckConditions) && CheckConditions) {
                                            ConditionalStack.Push(false);
                                        } else {
                                            int Result = IntEvaluate(RestOfLine);
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
                                    ConditionalHasBeenTrue.Remove(ConditionalStack.Count);
                                }
                                break;
                                #endregion
                            case ".else":
                            case "#else":
                                #region Conditionals (basic else)
                                bool CurrentLevel = (bool)ConditionalStack.Pop();
                                bool CheckCondition;
                                if ((bool)ConditionalStack.Peek() == true) {
                                    if (ConditionalHasBeenTrue.TryGetValue(ConditionalStack.Count, out CheckCondition) && CheckCondition) {
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
                                if (DeclaringStruct && !(Command == ".var" || Command == ".endstruct")) {
                                    DisplayError(ErrorType.Error, "The only non-conditional directive allowed inside structs is .var", Filename, CurrentLineNumber);
                                    if (StrictMode) return false;
                                    break;
                                }
                                switch (Command) {
                                    case ".org":
                                        #region Origin
                                        try {
                                            CurrentPage.ProgramCounter = (uint)(UintEvaluate(RestOfLine) - RelocationOffset);
                                            //RelocationOffset
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                        break;
                                        #endregion
                                    case "#include":
                                    case ".include":
                                        #region Include
                                        string NewFileName = RestOfLine.Replace("\"", "");
                                        OldFilenames.Push(CurrentFilename);
                                        OldLineNumbers.Push(CurrentLineNumber);
                                        if (!AssembleFile(ResolvePath(Filename, NewFileName), PassNumber)) {
                                            DisplayError(ErrorType.Error, "Error in file '" + NewFileName + "'", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                        if (OldFilenames.Count != 0 && OldLineNumbers.Count != 0) {
                                            CurrentFilename = OldFilenames.Pop();
                                            CurrentLineNumber = OldLineNumbers.Pop();
                                        }
                                        //if (DebugMode) Console.WriteLine("Done " + RestOfLine);
                                        break;
                                        #endregion
                                    case "#incbin":
                                    case ".incbin":
                                        #region Include (Binaries)

                                        switch (PassNumber) {
                                            case Pass.Labels: {
                                                    try {
                                                        string[] BinaryArguments = SafeSplit(RestOfLine, ',');

                                                        bool UseRLE = false;
                                                        string SizeLabel = "";

                                                        byte[] TranslationTable = new byte[256];
                                                        

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

                                                        if (Rule != "") {
                                                            for (int j = 0; j < 256; ++j) {
                                                                try {
                                                                    TranslationTable[j] = (byte)Evaluate(Rule.Replace("{*}", "(" + j.ToString() + ")"));
                                                                } catch (Exception ex) {
                                                                    DisplayError(ErrorType.Error, "Could not apply rule to binary: " + ex.Message, Filename, CurrentLineNumber);
                                                                    if (StrictMode) return false;
                                                                    break;
                                                                }
                                                            }
                                                        } else {
                                                            for (int i = 0; i < 256; ++i) {
                                                                TranslationTable[i] = (byte)i;
                                                            }
                                                        }

                                                        byte[] BinaryFile;

                                                        using (BinaryReader BR = new BinaryReader(new FileStream(FullFilename, FileMode.Open))) {

                                                            if (SizeLabel != "") {
                                                                if (!AddNewLabel(SizeLabel, (int)BR.BaseStream.Length, false, Filename, CurrentLineNumber, PassNumber, 0, false)) {
                                                                    DisplayError(ErrorType.Warning, "Could not create file size label '" + SizeLabel + "'.", Filename, CurrentLineNumber);
                                                                }
                                                            }

                                                            int BinStartIx = 0;
                                                            if (BinStart != "") {
                                                                try {
                                                                    BinStartIx = IntEvaluate(BinStart);
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
                                                                    BinEndIx = IntEvaluate(BinEnd);
                                                                    if (BinEndIx < BinStartIx) throw new Exception("End location $" + BinEndIx.ToString("X4") + " is before start location $" + BinStartIx.ToString("X4") + "!");
                                                                    if (BinEndIx < 0 || BinEndIx >= BR.BaseStream.Length) throw new Exception("Address $" + BinStartIx.ToString("X4") + " is out of the bounds of the binary file.");
                                                                } catch (Exception ex) {
                                                                    DisplayError(ErrorType.Error, "Could not use end location '" + BinEnd + "' - " + ex.Message, Filename, CurrentLineNumber);
                                                                    if (StrictMode) return false;
                                                                    break;
                                                                }
                                                            }

                                                            BinaryFile = new byte[BinEndIx - BinStartIx + 1];

                                                            BR.BaseStream.Seek(BinStartIx, SeekOrigin.Begin);

                                                            for (int i = 0; i <= BinEndIx - BinStartIx; i++) {
                                                                BinaryFile[i] = TranslationTable[BR.ReadByte()];
                                                            }
                                                            if (UseRLE) {
                                                                BinaryFile = RLE(BinaryFile);
                                                            }
                                                        }
                                                        QueuedBinaryFileData.Enqueue(BinaryFile);
                                                        CurrentPage.ProgramCounter += (uint)BinaryFile.Length;
                                                    } catch (Exception ex) {
                                                        DisplayError(ErrorType.Error, "Could not include binary data: " + ex.Message, Filename, CurrentLineNumber);
                                                    }
                                                }

                                                break;
                                            case Pass.Assembling: {
                                                    if (QueuedBinaryFileData.Count == 0) {
                                                        DisplayError(ErrorType.Error, "Fatal error with binary data (synchronisation between passes error).", Filename, CurrentLineNumber);
                                                        return false;
                                                    } else {
                                                        WriteToBinary(QueuedBinaryFileData.Dequeue());
                                                    }
                                                }
                                                break;
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
                                        string LcNewModuleName = IsCaseSensitive ? NewModuleName : NewModuleName.ToLower();

                                        if (NewModuleName.Length == 0) {
                                            DisplayError(ErrorType.Error, "Module name not specified.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }

                                        if (NewModuleName.Contains(".")) {
                                            DisplayError(ErrorType.Error, "Module name contains invalid character (\".\").", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }

                                        bool ProtectedName = false;
                                        switch (NewModuleName.ToLower()) {
                                            case "global":
                                            case "root":
                                            case "parent":
                                            case "this":
                                            case "noname":
                                                ProtectedName = true;
                                                break;
                                        }

                                        if (ProtectedName) {
                                            DisplayError(ErrorType.Error, "'" + NewModuleName + "' is a protected module name.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }

                                        if (!NestableModules) {
                                            if (!RootModule.Modules.TryGetValue(LcNewModuleName, out CurrentModule)) {
                                                CurrentModule = new Module(NewModuleName, RootModule);
                                                RootModule.Modules.Add(LcNewModuleName, CurrentModule);
                                            }
                                        } else {
                                            // Save current module to the stack:
                                            Modules.Push(CurrentModule);
                                            if (PassNumber == Pass.Labels) {
                                                Module ChildOf = CurrentModule.Equals(NoName) ? RootModule : CurrentModule;
                                                Module New = null;
                                                if (ChildOf.Modules.TryGetValue(LcNewModuleName, out New)) {
                                                    if (New.HalfCreated) {
                                                        New.HalfCreated = false;
                                                        CurrentModule = New;
                                                    } else {
                                                        DisplayError(ErrorType.Error, "Module '" + NewModuleName + "' already defined inside module '" + ChildOf.Name + "'.", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false; break;
                                                    }
                                                } else {
                                                    New = new Module(NewModuleName, ChildOf);
                                                    ChildOf.Modules.Add(LcNewModuleName, New);
                                                    CurrentModule = New;
                                                }
                                            } else {
                                                if (!CurrentModule.Equals(NoName)) {
                                                    CurrentModule = CurrentModule.Modules[LcNewModuleName];
                                                } else {
                                                    CurrentModule = RootModule.Modules[LcNewModuleName];
                                                }
                                            }

                                        }
                                        break;
                                        #endregion
                                    case ".endmodule":
                                        #region End module
                                        if (!NestableModules) {
                                            CurrentModule = NoName;
                                        } else {
                                            if (Modules.Count == 0) {
                                                DisplayError(ErrorType.Error, "No module to end.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            } else {
                                                CurrentModule = Modules.Pop();
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".db":
                                    case ".dw":
                                    case ".text":
                                    case ".byte":
                                    case ".word":
                                    case ".asc": {
                                        #region Define data 
                                            int DataSize = (Command.ToLower() == ".dw" || Command.ToLower() == ".word") ? 2 : 1;
                                            string[] Args = SafeSplit(RestOfLine, ',');

                                            foreach (string Arg in Args) {
                                                string RArg = Arg.Trim();
                                                if (RArg == "") continue;
                                                if (RArg.Length > 1 && (RArg[0] == RArg[RArg.Length - 1] && (RArg[0] == '\'' || RArg[0] == '"'))) {
                                                    RArg = UnescapeString(RArg.Substring(1, RArg.Length - 2));
                                                    switch (PassNumber) {
                                                        case Pass.Labels:
                                                            CurrentPage.ProgramCounter += (uint)(RArg.Length * DataSize);
                                                            break;
                                                        case Pass.Assembling:
                                                            try {
                                                                AsciiChar A = new AsciiChar();
                                                                foreach (char c in RArg) {
                                                                    int ValueToWrite = (int)A.Cast((double)c);
                                                                    for (int i = 0; i < DataSize; ++i) {
                                                                        WriteToBinary((byte)ValueToWrite);
                                                                        ValueToWrite >>= 8;
                                                                    }
                                                                }
                                                            } catch (Exception ex) {
                                                                DisplayError(ErrorType.Error, "Could not parse string expression '" + RArg + "' in data list: " + ex.Message, Filename, CurrentLineNumber);
                                                            }
                                                            break;
                                                    }

                                                } else {
                                                    switch (PassNumber) {
                                                        case Pass.Labels:
                                                            CurrentPage.ProgramCounter += (uint)DataSize;
                                                            break;
                                                        case Pass.Assembling:
                                                            try {
                                                                int ValueToWrite = IntEvaluate(RArg);
                                                                for (int i = 0; i < DataSize; ++i) {
                                                                    byte ToWrite = (byte)ValueToWrite;
                                                                    WriteToBinary(ToWrite);
                                                                    ValueToWrite >>= 8;
                                                                }
                                                            } catch (Exception ex) {
                                                                DisplayError(ErrorType.Error, "Could not parse expression '" + RArg + "' in data list: " + ex.Message, Filename, CurrentLineNumber);
                                                            }
                                                   
                                                            break;
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".block":
                                        #region Block
                                        try {
                                            CurrentPage.ProgramCounter += UintEvaluate(RestOfLine);
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
                                                StartAddress = IntEvaluate(RestOfLine);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            byte Checksum = 0;
                                            if (StartAddress < 0 || StartAddress >= CurrentPage.OutputBinary.Length) {
                                                Checksum = 0;
                                                DisplayError(ErrorType.Warning, "TASM checksum set to 0 (wanders off page boundary).", Filename, CurrentLineNumber);
                                            } else {
                                                for (int i = StartAddress; i < CurrentPage.ProgramCounter; ++i) {
                                                    if (CurrentPage.OutputBinary[i].WriteCount == 0) {
                                                        Checksum += (byte)CurrentPage.OutputBinary[i].Data;
                                                    } else {
                                                        Checksum += CurrentPage.OutputBinary[i].EmptyFill;
                                                    }
                                                }
                                            }
                                            WriteToBinary(Checksum);
                                        } else {
                                            ++CurrentPage.ProgramCounter;
                                        }
                                        break;
                                        #endregion
                                    case ".echo":
                                    case ".warn":
                                    case ".fail":
                                    case ".echoln":
                                        #region Messages
                                        if (PassNumber == Pass.Assembling) {
                                            string[] Messages = SafeSplit(RestOfLine, ',');
                                            for (int i = 0; i < Messages.Length; ++i) {
                                                Messages[i] = Messages[i].Trim();

                                            }
                                            ErrorType MessageType;
                                            switch (Command) {
                                                case ".echo":
                                                case ".echoln":
                                                    MessageType = ErrorType.Message;
                                                    break;
                                                case ".warn":
                                                    MessageType = ErrorType.Warning;
                                                    break;
                                                default:
                                                    MessageType = ErrorType.Error;
                                                    break;
                                            }
                                            string ErrorMessage = "";
                                            for (int i = 0; i < Messages.Length; ++i) {
                                                string EchoMessage = Messages[i];
                                                try {
                                                    if (EchoMessage.Length > 1 && EchoMessage[0] == '"' && EchoMessage[EchoMessage.Length - 1] == '"') {
                                                        if (i == 0 && EchoMessage.Contains("{") && EchoMessage.Contains("}")) {
                                                            string FormatString = UnescapeString(EchoMessage.Substring(1, EchoMessage.Length - 2));
                                                            List<object> O = new List<object>();
                                                            ++i;
                                                            for (; i < Messages.Length; ++i) {
                                                                if (Messages[i].Length > 1 && Messages[i][0] == '"' && Messages[i][Messages[i].Length - 1] == '"') {
                                                                    O.Add(UnescapeString(Messages[i].Substring(1, Messages[i].Length - 2)));
                                                                } else {
                                                                    try {
                                                                        double d = Evaluate(Messages[i]);
                                                                        if (d == (int)d) {
                                                                            O.Add((int)d);
                                                                        } else {
                                                                            O.Add(d);
                                                                        }
                                                                    } catch {
                                                                        O.Add(Messages[i]);
                                                                    }
                                                                }
                                                            }
                                                            ErrorMessage += string.Format(FormatString, O.ToArray());
                                                        } else {
                                                            ErrorMessage += UnescapeString(EchoMessage.Substring(1, EchoMessage.Length - 2));
                                                        }
                                                    } else {
                                                        ErrorMessage += Evaluate(EchoMessage).ToString();
                                                    }
                                                } catch (Exception ex) {
                                                    DisplayError(MessageType == ErrorType.Error ? ErrorType.Error : ErrorType.Warning, ".echo directive argument malformed: '" + EchoMessage + "' (" + ex.Message + ")", Filename, CurrentLineNumber);
                                                }
                                            }

                                            if (Command == ".echoln") ErrorMessage += "\n";

                                            if (MessageType == ErrorType.Message) {
                                                DisplayError(MessageType, ErrorMessage);
                                            } else {
                                                DisplayError(MessageType, ErrorMessage, Filename, CurrentLineNumber);
                                            }
                                            if (MessageType == ErrorType.Error && StrictMode) return false;
                                            break;
                                        }
                                        break;
                                        #endregion
                                    case ".equ":
                                    case "=":
                                        #region Label assignment
                                        if (JustHitALabel == false) {
                                            DisplayError(ErrorType.Error, Command + " directive is invalid unless you have just declared a label.", Filename, CurrentLineNumber);
                                        } else {
                                            try {
                                                if (RestOfLine.Trim() == "") throw new Exception("Nothing to assign");
                                                string[] LabelArgs = SafeSplit(RestOfLine, ',');
                                                if (LabelArgs.Length < 1 || LabelArgs.Length > 2) {
                                                    DisplayError(ErrorType.Error, "Label assignment requires one or two arguments.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false; break;
                                                }
                                                double NewValue = Evaluate(LabelArgs[0]);
                                                uint PageN = LabelArgs.Length == 2 ? UintEvaluate(LabelArgs[1]) : CurrentPage.Page;
                                                AddNewLabel(JustHitLabelName, NewValue, true, Filename, CurrentLineNumber, PassNumber, PageN, false);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not assign value '" + RestOfLine + "' to label '" + JustHitLabelName + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                            } finally {
                                                JustHitALabel = false;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".export":
                                        #region Label exporting
                                        if (RestOfLine.Trim() == "") {
                                            Program.AmExportingLabels = true;
                                        } else {
                                            if (PassNumber == Pass.Assembling) {
                                                string[] ExportVars = SafeSplit(RestOfLine, ',');
                                                foreach (string S in ExportVars) {
                                                    string LabelName = FixLabelName(S);
                                                    //if (LabelName.StartsWith(CurrentLocalLabel)) LabelName = CurrentModule + "." + LabelName;
                                                    LabelDetails TryToExport = null;
                                                    if (TryGetLabel(LabelName, out TryToExport, false)) {
                                                        TryToExport.ExportMe = true;
                                                    } else {
                                                        DisplayError(ErrorType.Warning, "Could not find label '" + LabelName + "' to export.");
                                                    }
                                                }
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".endexport":
                                        #region Stop exporting labels
                                        AmExportingLabels = false;
                                        break;
                                        #endregion
                                    case ".fill":
                                    case ".fillw":
                                    case ".ds":
                                        #region Fill data
                                        string[] FillArgs = SafeSplit(RestOfLine, ',');
                                        if (FillArgs.Length < 1 || FillArgs.Length > 2) {
                                            DisplayError(ErrorType.Error, Command + " syntax invalid.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        } else {
                                            int FillElementSize = (Command == ".fillw") ? 2 : 1;

                                            int FillValue = Command == ".ds" ? 0x00 : 0xFF;
                                            if (FillElementSize == 2) FillValue |= (FillValue << 8);
                                            int FillSize = 0;
                                            int Progress = 0;

                                            try {
                                                FillSize = IntEvaluate(FillArgs[0]);
                                                if (FillArgs.Length == 2) {
                                                    Progress = 1;
                                                    FillValue = IntEvaluate(FillArgs[1]);
                                                }

                                                if (PassNumber == Pass.Assembling) {
                                                    for (int i = 0; i < FillSize; ++i) {
                                                        WriteToBinary((byte)(FillValue & 0xFF));
                                                        if (FillElementSize == 2) {
                                                            WriteToBinary((byte)(FillValue >> 8));
                                                        }
                                                    }
                                                } else {
                                                    CurrentPage.ProgramCounter += (uint)(FillSize * FillElementSize);
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
                                            for (LinePart++; LinePart < SplitLines.Length; ++LinePart) {
                                                RestOfLine += @"\" + SplitLines[LinePart];
                                            }
                                            AddMacroThroughDefinition(RestOfLine, Filename, CurrentLineNumber, PassNumber == Pass.Assembling);
                                            continue;
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
                                            for (LinePart++; LinePart < SplitLines.Length; ++LinePart) {
                                                RestOfLine += @"\" + SplitLines[LinePart];
                                            }
                                            LastMacro.Replacements.Remove(LastReplacement);
                                            LastReplacement.ReplacementString += RestOfLine;
                                            LastMacro.Replacements.Add(LastReplacement);
                                            LastMacro.Replacements.Sort();
                                            AvailableMacros[LastMacro.Name] = LastMacro;
                                            continue;
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
                                                /*case "intelword":
                                                    BinaryType = Binary.IntelWord; break;
                                                case "mos":
                                                    BinaryType = Binary.MOS; break;
                                                case "motorola":
                                                    BinaryType = Binary.Motorola; break;*/
                                                case "segams":
                                                    BinaryType = Binary.SegaMS; break;
                                                case "segagg":
                                                    BinaryType = Binary.SegaGG; break;
                                                case "ti8xapp":
                                                    BinaryType = Binary.TI8XApp; break;
                                                case "ti73app":
                                                    BinaryType = Binary.TI73App; break;
                                                default:
                                                    DisplayError(ErrorType.Error, "Invalid binary mode '" + RestOfLine + "'", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false; break;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".tivariabletype":
                                        #region TI variable type
                                        if (PassNumber == Pass.Assembling) {
                                            try {
                                                TIVariableType = IntEvaluate(RestOfLine);
                                                TIVariableTypeSet = true;
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                            }
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
                                                    RealRndArgs[i] = IntEvaluate(RndArgs[i]);
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
                                                List<byte> RandomData = new List<byte>();

                                                try {
                                                    if (RealRndArgs[1] > RealRndArgs[2]) throw new Exception("Minimum must be less than maximum");
                                                    for (int i = 0; i < RealRndArgs[0]; ++i) {
                                                        int RN = RandomSource.Next(RealRndArgs[1], RealRndArgs[2]);
                                                        WriteToBinary((byte)(RN & 0xFF));
                                                        RandomData.Add(((byte)(RN & 0xFF)));
                                                        if (RndDataSize == 2) {
                                                            WriteToBinary((byte)(RN >> 8));
                                                            RandomData.Add((byte)(RN >> 8));
                                                        }
                                                    }

                                                } catch (Exception ex) {
                                                    DisplayError(ErrorType.Error, "Could not generate random data: '" + ex.Message + "'.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                }

                                            } else {
                                                CurrentPage.ProgramCounter += (uint)(RndDataSize * RealRndArgs[0]);
                                            }

                                        }

                                        break;
                                        #endregion
                                    case ".var":
                                    case ".tempvar":
                                    case ".tvar":
                                        #region Variables
                                        if (PassNumber != Pass.Labels) break;
                                        if (!VariableDirective(Command, RestOfLine, Filename, CurrentLineNumber) && StrictMode) return false;
                                        break;
                                        #endregion
                                    case ".varloc":
                                        #region Variable table location
                                        if (PassNumber != Pass.Labels) break;
                                        string[] VarLocArgs = SafeSplit(RestOfLine, ',');
                                        if (VarLocArgs.Length != 2) {
                                            DisplayError(ErrorType.Error, "Variable table location definition must specify the location and size.", Filename, CurrentLineNumber);
                                        } else {
                                            int VariableTable = 0;
                                            try {
                                                VariableTable = IntEvaluate(VarLocArgs[0]);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Invalid variable table location '" + VarLocArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                                break;
                                            }
                                            int VariableTableSize = 0;
                                            try {
                                                VariableTableSize = IntEvaluate(VarLocArgs[1]);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Warning, "Invalid variable table size '" + VarLocArgs[1] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                                break;
                                            }
                                            VariableAreas.Add(new VariableArea(VariableTable, VariableTableSize));
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
                                                MinChar = IntEvaluate(ASCIIArgs[0]);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Warning, "Could not parse argument for " + ((ASCIIArgs.Length == 3) ? "minimum" : "character") + ": " + ex.Message);
                                                break;
                                            }
                                            int MaxChar = MinChar;
                                            if (ASCIIArgs.Length == 3) {
                                                try {
                                                    MaxChar = IntEvaluate(ASCIIArgs[1]);
                                                } catch (Exception ex) {
                                                    DisplayError(ErrorType.Warning, "Could not parse argument for maximum: " + ex.Message);
                                                    break;
                                                }
                                            }
                                            string Rule = ASCIIArgs[ASCIIArgs.Length - 1];
                                            for (char i = (char)MinChar; i <= (char)MaxChar; ++i) {
                                                try {
                                                    if (ASCIITable.ContainsKey(i)) ASCIITable.Remove(i);
                                                    ASCIITable.Add(i, (byte)IntEvaluate(Rule.Replace("{*}", "(" + ((int)i).ToString() + ")")));
                                                } catch (Exception ex) {
                                                    DisplayError(ErrorType.Warning, "Invalid ASCII mapping: could not parse remapping of character #" + (int)i + " (" + ex.Message + " '" + (char)i + "').", Filename, CurrentLineNumber);
                                                    break;
                                                }
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".dbsin":
                                    case ".dbcos":
                                    case ".dbtan":
                                    case ".dwsin":
                                    case ".dwcos":
                                    case ".dwtan":
                                        #region Trig tables
                                        string[] TrigArgs = SafeSplit(RestOfLine, ',');
                                        if (TrigArgs.Length != 6) {
                                            DisplayError(ErrorType.Error, "Trigonometric table directives require 6 arguments.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        double TrigScale = 0;
                                        double TrigMag = 0;
                                        double TrigStart = 0;
                                        double TrigEnd = 0;
                                        double TrigStep = 0;
                                        double TrigOffset = 0;
                                        try {
                                            TrigScale = Evaluate(TrigArgs[0]);
                                        } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table scale: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                                        try {
                                            TrigMag = Evaluate(TrigArgs[1]);
                                        } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table amplitude: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                                        try {
                                            TrigStart = Evaluate(TrigArgs[2]);
                                        } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table start: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                                        try {
                                            TrigEnd = Evaluate(TrigArgs[3]);
                                        } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table end: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                                        try {
                                            TrigStep = Evaluate(TrigArgs[4]);
                                        } catch (Exception ex) { DisplayError(ErrorType.Error, "Could not calculate table step: " + ex.Message, Filename, CurrentLineNumber); if (StrictMode) return false; break; }
                                        try {
                                            TrigOffset = Evaluate(TrigArgs[5]);
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

                                        List<double> AnglesToUse = new List<double>();
                                        if (TrigStep > 0) {
                                            for (double i = TrigStart; i <= TrigEnd; i += TrigStep) {
                                                AnglesToUse.Add(i);
                                            }
                                        } else {
                                            for (double i = TrigStart; i >= TrigEnd; i += TrigStep) {
                                                AnglesToUse.Add(i);
                                            }
                                        }
                                        int TrigElementSize = (Command.IndexOf('w') == -1) ? 1 : 2;
                                        List<byte> TrigData = new List<byte>();
                                        if (PassNumber == Pass.Labels) {
                                            CurrentPage.ProgramCounter += (uint)(AnglesToUse.Count * TrigElementSize);
                                        } else {
                                            int TrigMode = (Command.IndexOf('i') != -1) ? 0 : Command.IndexOf('c') != -1 ? 1 : 2;
                                            foreach (int I in AnglesToUse) {
                                                double RealAngle = ((double)I / (double)TrigScale) * Math.PI * 2;
                                                int PlainTrigValue = (int)Math.Round(TrigOffset + (((TrigMode == 0) ? Math.Sin(RealAngle) : (TrigMode == 1) ? Math.Cos(RealAngle) : Math.Tan(RealAngle)) * (double)TrigMag), 0);
                                                WriteToBinary((byte)(PlainTrigValue & 0xFF));
                                                TrigData.Add((byte)(PlainTrigValue & 0xFF));
                                                if (TrigElementSize == 2) {
                                                    WriteToBinary((byte)(PlainTrigValue >> 8));
                                                    TrigData.Add((byte)(PlainTrigValue >> 8));
                                                }
                                            }


                                        }
                                        break;
                                        #endregion
                                    case ".rlemode":
                                        #region RLE mode
                                        string[] RLE_Args = SafeSplit(RestOfLine, ',');
                                        try {
                                            RLE_Flag = (byte)(Evaluate(RLE_Args[0]));
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Warning, "Could not set new RLE run character - " + ex.Message, Filename, CurrentLineNumber);
                                            break;
                                        }
                                        if (RLE_Args.Length > 1) {
                                            try {
                                                RLE_ValueFirst = (Evaluate(RLE_Args[1]) != 0);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Warning, "Could not set new RLE ordering mode - " + ex.Message, Filename, CurrentLineNumber);
                                                break;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".for":
                                    case ".repeat":
                                    case ".rept":
                                        #region for-loop start

                                        double ForStart = 0;
                                        double ForEnd = 0;
                                        double ForStep = 1;
                                        string ForLabel = "";

                                        bool SuccessInForLoop = false;
                                        LabelDetails ForLoopLabel = null;
                                        if (Command == ".for") {
                                            string[] ForArgs = SafeSplit(RestOfLine, ',');
                                            if (ForArgs.Length < 3 || ForArgs.Length > 4) {
                                                DisplayError(ErrorType.Error, "For loops require 3 or 4 arguments: Variable, start, end, and (optionally) step.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }

                                            try {
                                                ForLabel = FixLabelName(ForArgs[0]);
                                                if (ForLabel == "") throw new Exception("For loops must have a label.");
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }

                                            if (TryGetLabel(ForLabel, out ForLoopLabel, true) && PassNumber == Pass.Labels) {
                                                DisplayError(ErrorType.Error, "Label '" + ForLabel + "' already defined!", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                            try {
                                                ForStart = Evaluate(ForArgs[1]);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not calculate for-loop start: " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                            try {
                                                ForEnd = Evaluate(ForArgs[2]);
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not calculate for-loop end: " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                            try {
                                                if (ForArgs.Length == 4) {
                                                    ForStep = Evaluate(ForArgs[3]);
                                                }
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not calculate for-loop step: " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                            if (ForStep == 0 || ForStep > 0 && ForStart > ForEnd || ForStep < 0 && ForStart < ForEnd) {
                                                DisplayError(ErrorType.Error, "Infinite loop.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }

                                            SuccessInForLoop = true;
                                        } else {
                                            try {
                                                ForEnd = Evaluate(RestOfLine) - 1;
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could not repeat (" + ex.Message + ").", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }

                                            if (ForEnd < 1) {
                                                DisplayError(ErrorType.Error, ".repeat requires an argument greater than zero.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }

                                            SuccessInForLoop = true;
                                            ForLabel = "repeat_label_temp_";
                                            while (TryGetLabel(ForLabel + RepeatLabelCount.ToString("X4"), out ForLoopLabel, true)) {
                                                ++RepeatLabelCount;
                                            }
                                        }
                                        if (!SuccessInForLoop) break;

                                        // We've got this far, so we can assume it's a safe to loop
                                        ForLoopLabel.RealValue = ForStart;
                                        ForLoopLabel.Page = CurrentPage.Page;
                                        ForLoopLabel.File = Filename;
                                        ForLoopLabel.Line = CurrentLineNumber;

                                        ForLoop NewForLoop = new ForLoop();
                                        //NewForLoop.Value = ForStart;
                                        NewForLoop.Start = ForStart;
                                        NewForLoop.End = ForEnd;
                                        NewForLoop.CalculateLength(ForStep);
                                        NewForLoop.Filename = Filename;
                                        NewForLoop.LineNumber = CurrentLineNumber;
                                        NewForLoop.RealSourceLine = RealSourceLine;
                                        NewForLoop.LinePart = LinePart + 1;
                                        ForLoops[ForLoopLabel] = NewForLoop;
                                        LastForLoop.Push(ForLoopLabel);
                                        break;
                                        #endregion
                                    case ".loop":
                                        #region for-loop looping
                                        LabelDetails LoopLabel;
                                        try {
                                            LoopLabel = LastForLoop.Peek();
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }

                                        ForLoop FL;
                                        if (!ForLoops.TryGetValue(LoopLabel, out FL)) {
                                            // Eh?
                                            DisplayError(ErrorType.Error, "For-loop label '" + LoopLabel + "' not found.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        } else {
                                            // We have found the loop.
                                            if (FL.Filename.ToLower() != Filename.ToLower()) {
                                                DisplayError(ErrorType.Error, "You cannot loop from a different file to the one in which the for-loop was defined.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            }
                                            if (FL.Step(ref LoopLabel)) {
                                                // End
                                                // Clear up the variables
                                                LoopLabel.Destroy();
                                                ForLoops.Remove(LoopLabel);
                                                LastForLoop.Pop();
                                            } else {
                                                // Carry on
                                                CurrentLineNumber = FL.LineNumber;
                                                RealSourceLine = FL.RealSourceLine;
                                                LinePart = FL.LinePart;
                                                SplitLines = SafeSplit(RealSourceLine, '\\');
                                                if (LinePart >= SplitLines.Length) {
                                                    continue;
                                                }
                                                SourceLine = SplitLines[LinePart];
                                                goto CarryOnAssembling;
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

                                        if (FileHandles.ContainsKey(HandleName)) {
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
                                        FileHandles.Add(HandleName, new BinaryReader(F));
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
                                                SeekPos = IntEvaluate(FileReadArgs[1]);
                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        }

                                        string ReadHandleName = IsCaseSensitive ? FileReadArgs[0] : FileReadArgs[0].ToLower();
                                        BinaryReader ReadBR;
                                        if (!FileHandles.TryGetValue(ReadHandleName, out ReadBR)) {
                                            DisplayError(ErrorType.Error, "File handle '" + ReadHandleName + "' not found.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        } else {
                                            try {
                                                LabelDetails FileLabelToEdit = null;
                                                if (Command != ".fseek") {
                                                    if (!TryGetLabel(ReadLabelName, out FileLabelToEdit, true)) {
                                                        FileLabelToEdit.File = Filename;
                                                        FileLabelToEdit.Line = CurrentLineNumber;
                                                    }
                                                }
                                                switch (Command) {
                                                    case ".fread":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.ReadByte();
                                                        break;
                                                    case ".freadw":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.ReadInt16();
                                                        break;
                                                    case ".fpeek":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.ReadByte();
                                                        ReadBR.BaseStream.Seek(-1, SeekOrigin.Current);
                                                        break;
                                                    case ".fpeekw":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.ReadInt16();
                                                        ReadBR.BaseStream.Seek(-2, SeekOrigin.Current);
                                                        break;
                                                    case ".fsize":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.BaseStream.Length;
                                                        break;
                                                    case ".fpos":
                                                        FileLabelToEdit.RealValue = (int)ReadBR.BaseStream.Position;
                                                        break;
                                                    case ".fseek":
                                                        ReadBR.BaseStream.Seek(SeekPos, SeekOrigin.Begin);
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
                                        BinaryReader CloseBR;
                                        if (!FileHandles.TryGetValue(CloseHandleName, out CloseBR)) {
                                            DisplayError(ErrorType.Error, "File handle '" + CloseHandleName + "' not found.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false; break;
                                        } else {
                                            try {
                                                CloseBR.Close();
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
                                        // Page number, size [, origin]
                                        if (PageArgs.Length < 2 || PageArgs.Length > 3) {
                                            DisplayError(ErrorType.Error, "Page definitions require 2 to 3 arguments.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        uint PageNumber;
                                        try {
                                            PageNumber = UintEvaluate(PageArgs[0]);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Invalid page number - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }

                                        bool IsValidPage = true;
                                        if (Pages.ContainsKey(PageNumber)) {
                                            if (!(PageNumber == 0 && CanStillDefinePage0)) {
                                                IsValidPage = false;
                                                DisplayError(ErrorType.Error, "You cannot redefine page " + PageNumber + ".", Filename, CurrentLineNumber);
                                            }
                                        }
                                        if (!IsValidPage) {
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        uint PageSize = 0x10000;
                                        try {
                                            PageSize = UintEvaluate(PageArgs[1]);
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
                                        uint PageOrg = 0x0000;
                                        try {
                                            if (PageArgs.Length > 2) PageOrg = UintEvaluate(PageArgs[2]);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Invalid page origin - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        BinaryPage NewPage = new BinaryPage(PageNumber, PageSize, PageOrg);
                                        CanStillDefinePage0 = false;
                                        bool WasRedefiningPage0 = (Pages.ContainsKey(PageNumber));
                                        if (WasRedefiningPage0) Pages.Remove(PageNumber);
                                        Pages.Add(PageNumber, NewPage);
                                        if (WasRedefiningPage0) CurrentPage = NewPage;
                                        if (PageNumber == 0) Page0Defined = true;
                                        break;
                                        #endregion
                                    case ".page":
                                        #region Switch page
                                        uint PageToSwitchTo;
                                        try {
                                            PageToSwitchTo = UintEvaluate(RestOfLine);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Invalid page - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        BinaryPage OldPage = CurrentPage;
                                        if (!Pages.TryGetValue(PageToSwitchTo, out CurrentPage)) {
                                            DisplayError(ErrorType.Error, "Page " + PageToSwitchTo.ToString() + " not defined.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            CurrentPage = OldPage;
                                            break;
                                        }
                                        break;
                                        #endregion
                                    case ".binaryrange":
                                        #region Binary output range
                                        /*string[] RangeMarkers = SafeSplit(RestOfLine, ',');
                                        if (RangeMarkers.Length != 2) {
                                            DisplayError(ErrorType.Error, "Binary range requires both a start and an end value.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                            break;
                                        }
                                        try {
                                            StartOutputAddress = UintEvaluate(RangeMarkers[0]);
                                            EndOutputAddress = UintEvaluate(RangeMarkers[1]);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Error setting binary range  - " + ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }*/
                                        DisplayError(ErrorType.Warning, ".binaryrange directive deprecated", Filename, CurrentLineNumber);
                                        break;
                                        #endregion
                                    case ".binaryfill":
                                    case ".emptyfill":
                                        #region Binary fill character
                                        if (PassNumber == Pass.Assembling) {
                                            try {
                                                BinaryFillChar = (byte)IntEvaluate(RestOfLine);
                                                for (uint i = CurrentPage.ProgramCounter; i < CurrentPage.Size; ++i) {
                                                    CurrentPage.OutputBinary[i].EmptyFill = BinaryFillChar;
                                                }
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
                                                SdscMajorVersionNumber = (byte)Evaluate(SdscVersion[0]);
                                                if (SdscVersion.Length > 1) {
                                                    SdscMinorVersionNumber = (byte)Evaluate(SdscVersion[1]);
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
                                                        int TagPointer = IntEvaluate(SdscText);
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
                                    case ".segaversion":
                                        #region Sega product number
                                        string[] VersionInfo = RestOfLine.Split('.');
                                        try {
                                            if (VersionInfo.Length == 1) {
                                                switch (Command) {
                                                    case ".segapart":
                                                        SegaPartNumber = IntEvaluate(VersionInfo[0]);
                                                        break;
                                                    case ".segaversion":
                                                        SegaVersion = IntEvaluate(VersionInfo[0]);
                                                        break;
                                                }
                                            } else if (VersionInfo.Length == 2) {
                                                SegaPartNumber = IntEvaluate(VersionInfo[0]);
                                                SegaVersion = IntEvaluate(VersionInfo[1]);
                                            } else {
                                                throw new Exception("Invalid syntax.");
                                            }
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Warning, "Problem with Sega part/version directive: " + ex.Message, Filename, CurrentLineNumber);
                                        }
                                        if (SegaPartNumber > 159999 || SegaPartNumber < 0) {
                                            DisplayError(ErrorType.Warning, "Sega part numbers must be between 0 and 159999.", Filename, CurrentLineNumber);
                                            SegaPartNumber = SegaPartNumber < 0 ? 0 : 159999;
                                        }
                                        if (SegaVersion > 9 || SegaVersion < 0) {
                                            DisplayError(ErrorType.Warning, "Sega version numbers must be between 0 and 9.", Filename, CurrentLineNumber);
                                            SegaVersion = SegaVersion < 0 ? 0 : 9;
                                        }
                                        break;
                                        #endregion
                                    case ".incbmp":
                                        #region Monochrome bitmap inclusion
                                        string[] IncBmpArgs = SafeSplit(RestOfLine, ',');

                                        try {
                                            if (IncBmpArgs.Length < 1 || IncBmpArgs.Length > 3) throw new Exception("Invalid number of arguments.");
                                            string BmpFilename = ResolvePath(Filename, IncBmpArgs[0].Replace("\"", ""));
                                            if (!File.Exists(BmpFilename)) throw new Exception("Image file " + BmpFilename + " not found.");
                                            using (Bitmap B = new Bitmap(BmpFilename)) {
                                                bool CanRle = false;

                                                int BmpWidth = B.Width;
                                                int BmpHeight = B.Height;

                                                if (BmpWidth != 0) {

                                                    /*if (PassNumber == Pass.Labels) {
                                                        CurrentPage.ProgramCounter += (B.Height * ByteWidth);
                                                    } else {*/
                                                    int BrightnessLimiter = 127;

                                                    for (int i = 1; i < IncBmpArgs.Length; i++) {
                                                        string Arg = IncBmpArgs[i].ToLower().Trim();
                                                        if (Arg == "rle") {
                                                            CanRle = true;
                                                        } else if (Arg.StartsWith("width") || Arg.StartsWith("height")) {
                                                            string[] BmpSizeArgs = SafeSplit(IncBmpArgs[i].Trim(), '=');
                                                            if (BmpSizeArgs.Length != 2) {
                                                                DisplayError(ErrorType.Warning, "Bitmap size argument malformed (" + IncBmpArgs[i].Trim() + ").", Filename, CurrentLineNumber);
                                                            } else {
                                                                if (Arg.StartsWith("width")) {
                                                                    BmpWidth = IntEvaluate(BmpSizeArgs[1]);
                                                                } else {
                                                                    BmpHeight = IntEvaluate(BmpSizeArgs[1]);
                                                                }
                                                            }
                                                        } else {
                                                            BrightnessLimiter = IntEvaluate(IncBmpArgs[i]);
                                                        }
                                                    }

                                                    int ByteWidth = 1 + ((BmpWidth - 1) >> 3);
                                                    int RBmpHeight = B.Height;
                                                    int RBmpWidth = B.Width;
                                                    byte[] ToAdd = new byte[BmpHeight * ByteWidth];
                                                    int AddIndex = 0;
                                                    for (int y = 0; y < BmpHeight; ++y) {
                                                        for (int x = 0; x < ByteWidth; ++x) {
                                                            byte Row = 0x00;
                                                            for (int i = 0; i < 8; ++i) {
                                                                Row <<= 1;
                                                                if (i + x * 8 < B.Width) {
                                                                    int Pixel = (x >= 0 && x < RBmpWidth && y >= 0 && y < RBmpHeight) ? B.GetPixel(i + x * 8, y).ToArgb() : 0;
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
                                                        CurrentPage.ProgramCounter += (uint)(ToAdd.Length);
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
                                        /*string UsingName = IsCaseSensitive ? RestOfLine : RestOfLine.ToLower();
                                        if (UsingName == "noname") {
                                            CurrentModule.Using.Add(NoName);
                                        } else {
                                            Module FromRoot = RootModule;
                                            Module FromRel = CurrentModule;
                                            string[] ModulePath = UsingName.Split('.');
                                            foreach (string S in ModulePath) {
                                                TryToFollowModulePath(S, ref FromRoot);
                                                TryToFollowModulePath(S, ref FromRel);
                                            }
                                            if (FromRoot != null) {
                                                CurrentModule.Using.Add(FromRoot);
                                            } else if (FromRel != null) {
                                                CurrentModule.Using.Add(FromRel);
                                            } else {
                                                Module NewUsing = RootModule;
                                                foreach (string S in ModulePath) {
                                                    Module SwitchTo = null;
                                                    if (!NewUsing.Modules.TryGetValue(S, out SwitchTo)) {
                                                        SwitchTo = new Module(S, NewUsing);
                                                        SwitchTo.HalfCreated = true;
                                                        NewUsing.Modules.Add(S, SwitchTo);
                                                    }
                                                    NewUsing = SwitchTo;
                                                }
                                                CurrentModule.Using.Add(NewUsing);
                                            }
                                        }*/
                                        string[] UsingList = SafeSplit(RestOfLine, ',');
                                        foreach (string s in UsingList) {
                                            CurrentModule.Using.Add(s.Trim());
                                        }
                                        break;
                                        #endregion
                                    case ".varfree":
                                        #region Variable table free memory pointer
                                        if (PassNumber != Pass.Labels) break;
                                        //if (RestOfLine == "") {
                                        DisplayError(ErrorType.Error, "This directive no deprecated: .var/.varloc is now dynamic.", Filename, CurrentLineNumber);
                                        /*    if (StrictMode) return false; break;
                                        }
                                        try {
                                            AddNewLabel(FixLabelName(RestOfLine), VariableTableOff + VariableTable, false, Filename, CurrentLineNumber, PassNumber, CurrentPage.Page);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not assign value: " + ex.Message, Filename, CurrentLineNumber);
                                        }*/
                                        break;
                                        #endregion
                                    case ".relocate":
                                        #region Relocatable code block
                                        try {
                                            if (RelocationOffset != 0) throw new Exception("you may not nest relocated blocks.");
                                            RelocationOffset = (int)(UintEvaluate(RestOfLine) - CurrentPage.ProgramCounter);
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
                                        if (AvailableMacros.ContainsKey(RestOfLine)) {
                                            AvailableMacros.Remove(RestOfLine);
                                        } else {
                                            DisplayError(ErrorType.Error, "Macro " + RestOfLine + " could not be undefined.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                        break;
                                        #endregion
                                    case ".inclabels":
                                        #region Include labels file
                                        if (PassNumber == Pass.Labels) {
                                            try {
                                                using (BinaryReader BR = new BinaryReader(new FileStream(ResolvePath(Filename, RestOfLine.Replace("\"", "")), FileMode.Open))) {
                                                    while (BR.BaseStream.Position < BR.BaseStream.Length) {
                                                        string LN = new string(BR.ReadChars(BR.ReadByte()));
                                                        uint LV = BR.ReadUInt16();
                                                        uint LP = BR.ReadUInt16();
                                                        if (!AddNewLabel(LN, LV, false, Filename, CurrentLineNumber, Pass.Labels, LP, false)) {
                                                            DisplayError(ErrorType.Error, "Could not add label " + LN, Filename, CurrentLineNumber);
                                                            if (StrictMode) return false;
                                                        }
                                                    }
                                                }
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Error, "Could  not parse labels file: " + ex.Message, Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".align":
                                        #region Align Origin
                                        try {
                                            int DesiredAlignment = IntEvaluate(RestOfLine);
                                            uint AbsolutePc = (uint)(CurrentPage.ProgramCounter + RelocationOffset);
                                            AbsolutePc = (uint)(((AbsolutePc + DesiredAlignment - 1) / DesiredAlignment) * DesiredAlignment);
                                            CurrentPage.ProgramCounter = (uint)(AbsolutePc - RelocationOffset);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not align to '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                        break;
                                        #endregion
                                    case ".endasm":
                                        #region End assembling (multiline comments)
                                        AmAssembling = false;
                                        break;
                                        #endregion
                                    case ".deflong":
                                        #region For lengthy TASM macros
                                        try {
                                            for (LinePart++; LinePart < SplitLines.Length; ++LinePart) {
                                                RestOfLine += @"\" + SplitLines[LinePart];
                                            }
                                            AddMacroThroughDefinition(RestOfLine, Filename, CurrentLineNumber, PassNumber == Pass.Assembling);
                                            AmDefiningLongMacro = true;
                                            continue;
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, ex.Message, Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }
                                        break;
                                        #endregion
                                    case ".enddeflong":
                                        #region Ending lengthy multi-line TASM macros
                                        AmDefiningLongMacro = false;
                                        break;
                                        #endregion
                                    case ".exportmode":
                                        #region Export mode
                                        if (PassNumber == Pass.Labels) {
                                            switch (RestOfLine.ToLower()) {
                                                case "assembly":
                                                    ExportFileFormat = ExportFormat.Assembly; break;
                                                case "fullassembly":
                                                    ExportFileFormat = ExportFormat.FullAssembly; break;
                                                case "labelfile":
                                                    ExportFileFormat = ExportFormat.LabelFile; break;
                                                case "no$gmb":
                                                    ExportFileFormat = ExportFormat.NoGmb; break;
                                                case "emukonpatch":
                                                    ExportFileFormat = ExportFormat.EmukonPatch; break;
                                                default:
                                                    DisplayError(ErrorType.Error, "Invalid export file format '" + RestOfLine + "'", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false; break;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".local":
                                    case ".endlocal":
                                        #region Local labels
                                        AllLabelsLocal = Command == ".local";
                                        break;
                                        #endregion
                                    case ".nestmodules":
                                    case ".endnestmodules":
                                        #region Nestable modules
                                        NestableModules = Command == ".nestmodules";
                                        break;
                                        #endregion
                                    case ".squish":
                                    case ".unsquish":
                                        #region Binary squishing
                                        if (PassNumber == Pass.Assembling) {
                                            SquishedData = Command == ".squish";
                                            for (uint i = CurrentPage.ProgramCounter; i < CurrentPage.Size; ++i) {
                                                CurrentPage.OutputBinary[i].Squished = SquishedData;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".global":
                                    case ".endglobal":
                                        #region Force labels global
                                        ForceLabelsGlobal = Command == ".global";
                                        break;
                                        #endregion
                                    case ".asm":
                                        #region Swallow surplus directive
                                        break;
                                        #endregion
                                    case ".struct":
                                        #region Start defining a structure
                                        if (PassNumber == Pass.Labels) {
                                            if (DeclaringStruct) {
                                                DisplayError(ErrorType.Error, "You may not nest struct declarations.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                                break;
                                            } else {
                                                string StructName = RestOfLine.Trim();
                                                if (!IsCaseSensitive) StructName = StructName.ToLower();
                                                if (StructName == "") {
                                                    DisplayError(ErrorType.Error, "Structs must have a name.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                    break;
                                                }
                                                if (Structs.ContainsKey(StructName)) {
                                                    DisplayError(ErrorType.Error, "Struct " + StructName + " already defined.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false;
                                                    break;
                                                } else {
                                                    DeclaringStruct = true;
                                                    CurrentStruct = new Struct(StructName, Filename, CurrentLineNumber);
                                                    Structs.Add(StructName, CurrentStruct);
                                                }
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".endstruct":
                                        #region Finish defining a structure
                                        if (PassNumber == Pass.Labels) {
                                            if (!DeclaringStruct) {
                                                DisplayError(ErrorType.Warning, "No struct definition to end.", Filename, CurrentLineNumber);
                                                break;
                                            } else {
                                                DeclaringStruct = false;
                                                CurrentStruct = null;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".breakpoint":
                                        #region Breakpoint
                                        if (PassNumber == Pass.Assembling) {
                                            string EndOfLine = RestOfLine.Trim();
                                            if (RestOfLine.Length != 0) {
                                                if (RestOfLine[0] == '"' && RestOfLine[RestOfLine.Length - 1] == '"') {
                                                    EndOfLine = UnescapeString(RestOfLine.Trim('"'));
                                                }
                                            }
                                            Breakpoints.Add(new Breakpoint(Filename, CurrentLineNumber, (uint)(CurrentPage.ProgramCounter + RelocationOffset), CurrentPage.Page, EndOfLine));
                                        }
                                        break;
                                        #endregion
                                    case ".enum": 
                                        #region Enumeration
                                {
                                            if (PassNumber == Pass.Labels) {
                                                string[] EnumParts = SafeSplit(RestOfLine, ',');
                                                if (EnumParts.Length < 2) {
                                                    DisplayError(ErrorType.Error, "Enumerations need at least one item.", Filename, CurrentLineNumber);
                                                    if (StrictMode) return false; break;
                                                }
                                                string EnumName = EnumParts[0];
                                                Module ToAddTo = CurrentModule;
                                                if (ForceLabelsGlobal || (!AllLabelsLocal && !EnumName.StartsWith(CurrentLocalLabel))) {
                                                    ToAddTo = GlobalLabels;
                                                }
                                                if (ToAddTo.Modules.ContainsKey(IsCaseSensitive ? EnumName : EnumName.ToLower())) {
                                                    DisplayError(ErrorType.Error, "Module already contains definition for " + EnumName, Filename, CurrentLineNumber);
                                                    if (StrictMode) return false; break;
                                                }
                                                Module Enum = new Module(EnumName, ToAddTo);
                                                ToAddTo.Modules.Add(IsCaseSensitive ? EnumName : EnumName.ToLower(), Enum);
                                                List<LabelDetails> Unallocated = new List<LabelDetails>();
                                                List<int> UsedNumbers = new List<int>();
                                                List<string> LabelNames = new List<string>();
                                                for (int i = 1; i < EnumParts.Length; ++i) {
                                                    string[] EnumItem = SafeSplit(EnumParts[i], '=');
                                                    if (EnumItem.Length < 1 || EnumItem.Length > 2) {
                                                        DisplayError(ErrorType.Error, "'" + EnumParts[i].Trim() + "' is not valid syntax for part of an enumeration.", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false; break;
                                                    }
                                                    string LabelName = FixLabelName(EnumItem[0]);
                                                    string LookupName = IsCaseSensitive ? LabelName : LabelName.ToLower();
                                                    if (LabelNames.Contains(LookupName)) {
                                                        DisplayError(ErrorType.Error, "Enumeration " + EnumName + " already contains a definition for " + LabelName + ".", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false; break;
                                                    }
                                                    LabelNames.Add(LookupName);
                                                    int LabelValue = 0;
                                                    if (EnumItem.Length == 2) {
                                                        try {
                                                            LabelValue = IntEvaluate(EnumItem[1]);
                                                            UsedNumbers.Add(LabelValue);
                                                        } catch (Exception ex) {
                                                            DisplayError(ErrorType.Error, "Could not parse " + EnumItem[1].Trim() + " - " + ex.Message, Filename, CurrentLineNumber);
                                                            if (StrictMode) return false; break;
                                                        }
                                                    }
                                                    LabelDetails EnumLabel = new LabelDetails(null, LabelName, LabelValue, Filename, CurrentLineNumber, CurrentPage.Page, ToAddTo, false);
                                                    EnumLabel.IsUnmolested = false;
                                                    if (EnumItem.Length != 2) Unallocated.Add(EnumLabel);
                                                    Enum.Labels.Add(LookupName, EnumLabel);
                                                }
                                                int UnusedNumber = 0;
                                                foreach (LabelDetails L in Unallocated) {
                                                    while (UsedNumbers.Contains(UnusedNumber)) UnusedNumber++;
                                                    L.RealValue = UnusedNumber;
                                                    UsedNumbers.Add(UnusedNumber);
                                                }

                                            }
                                            break;
                                        }
                                            #endregion
                                    case ".dvar":
                                        #region Define variables
 {
                                            string[] Args = RestOfLine.Split(',');
                                            if (Args.Length < 2) {
                                                DisplayError(ErrorType.Error, ".dvar expects at least two arguments: a type and a value.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }
                                            IType T;
                                            if (!TryGetTypeInformation(Args[0], out T)) {
                                                DisplayError(ErrorType.Error, "Could not work out what type " + Args[0] + " was.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }
                                            switch (PassNumber) {
                                                case Pass.Labels:
                                                    CurrentPage.ProgramCounter += (uint)(T.Size * (Args.Length - 1));
                                                    break;
                                                case Pass.Assembling:

                                                    for (int i = 1; i < Args.Length; ++i) {
                                                        try {
                                                            WriteToBinary(T.ByteRepresentation(Evaluate(Args[i])));
                                                        } catch (Exception ex) {
                                                            DisplayError(ErrorType.Error, "Could not evalulate " + Args[i] + ": " + ex.Message);
                                                            if (StrictMode) return false; break;
                                                        }
                                                    }
                                                    break;
                                            }
                                        }
                                        break;
                                        #endregion
                                    case ".pvar":
                                        #region Existing variables
                                        if (PassNumber == Pass.Assembling) {
                                            string[] PVar = SafeSplit(RestOfLine, ',');
                                            IType PVarType;
                                            if (!TryGetTypeInformation(PVar[0], out PVarType)) {
                                                DisplayError(ErrorType.Error, "Could not work out what type '" + PVar[0].Trim() + "' is.", Filename, CurrentLineNumber);
                                                if (StrictMode) return false; break;
                                            }
                                            for (int i = 1; i < PVar.Length; ++i) {
                                                LabelDetails ToChange;
                                                if (TryGetLabel(PVar[i], out ToChange, false)) {
                                                    ToChange.Type = PVarType;
                                                }
                                            }

                                        }
                                        break;
                                        #endregion
                                    case ".tiarchived":
                                        #region Set TI files to be archived
                                        TIVariableArchived = true;
                                        break;
                                        #endregion
                                    case ".branch":
                                        #region Branch table generation
                                        string[] LabelsToExport = SafeSplit(RestOfLine, ',');
                                        //while ((CurrentPage.ProgramCounter - CurrentPage.StartAddress) % 3 != 0) ++CurrentPage.ProgramCounter;
                                        switch (PassNumber) {
                                            case Pass.Labels:
                                                foreach (string LN in LabelsToExport) {
                                                    LabelDetails OutNewLabel;
                                                    string s = LN.Trim();
                                                    string[] LabelComponents = s.Split('.');
                                                    s = "";
                                                    for (int i = 0; i < LabelComponents.Length - 1; ++i) {
                                                        s += LabelComponents[i] + ".";
                                                    }
                                                    s += BranchTableRule.Replace("{*}", LabelComponents[LabelComponents.Length - 1]);
                                                    if (TryGetLabel(s, out OutNewLabel, true)) {
                                                        DisplayError(ErrorType.Error, "Couldn't create branch table value: label '" + s + "' already exists.", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false;
                                                    } else {
                                                        OutNewLabel.RealValue = CurrentPage.ProgramCounter - CurrentPage.StartAddress;
                                                    }
                                                    CurrentPage.ProgramCounter += 3;
                                                }
                                                break;
                                            case Pass.Assembling:
                                                foreach (string LN in LabelsToExport) {
                                                    string s = LN.Trim();
                                                    LabelDetails L;
                                                    if (!TryGetLabel(s, out L, false)) {
                                                        DisplayError(ErrorType.Error, "Could not populate branch table entry '" + s + "' (label not found).", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false;
                                                    } else {
                                                        WriteToBinary((byte)(L.Value & 0xFF));
                                                        WriteToBinary((byte)(L.Value >> 0x8));
                                                        WriteToBinary((byte)(L.Page));
                                                    }
                                                }
                                                break;
                                        }
                                        break;
                                        #endregion
                                    case ".branchrule":
                                        #region Branch table rule
                                        BranchTableRule = RestOfLine.Trim('"');
                                        break;
                                        #endregion
                                    case ".signkey":
                                        #region Application key
                                        try {
                                            ushort Key;
                                            Key = Convert.ToUInt16(RestOfLine.Trim(), 16);
                                            AppKey = Key;
                                        } catch {
                                            DisplayError(ErrorType.Warning, "Invalid developer key '" + RestOfLine.Trim() + "' (will use freeware key instead).", Filename, CurrentLineNumber);
                                        }
                                        break;
                                        #endregion
                                    case ".appfield":
                                        #region Application fields
                                        if (PassNumber == Pass.Labels) break;
                                        string[] FieldArgs = SafeSplit(RestOfLine, ',');
                                        if (FieldArgs.Length != 2) {
                                            DisplayError(ErrorType.Warning, "Application fields must have two arguments - a field and a value.", Filename, CurrentLineNumber);
                                        } else {
                                            try {
                                                switch (FieldArgs[0].Trim().ToLower()) {
                                                    case "revision":
                                                        AppRevision = (byte)IntEvaluate(FieldArgs[1]);
                                                        break;
                                                    case "build":
                                                        AppBuild = (byte)IntEvaluate(FieldArgs[1]);
                                                        break;
                                                    case "expires":
                                                        DateTime ExpiresDate;
                                                        if (!DateTimeTryParse(FieldArgs[1].Trim(), out ExpiresDate)) {
                                                            DisplayError(ErrorType.Warning, "Could not parse date '" + FieldArgs[1].Trim() + "'.", Filename, CurrentLineNumber);
                                                        } else {
                                                            TimeSpan Since1997 = ExpiresDate - new DateTime(1997, 1, 1);
                                                            AppExpires = (int)Since1997.TotalSeconds;
                                                        }
                                                        break;
                                                    case "uses":
                                                        AppUses = (byte)IntEvaluate(FieldArgs[1]);
                                                        break;
                                                    case "nosplash":
                                                        AppNoSplash = IntEvaluate(FieldArgs[1]) != 0;
                                                        break;
                                                    case "hardware":
                                                        AppHardware = (byte)IntEvaluate(FieldArgs[1]);
                                                        break;
                                                    case "basecode":
                                                        double BasecodeVersion = Evaluate(FieldArgs[1]);
                                                        byte AppBasecodeMajor = (byte)BasecodeVersion;
                                                        byte AppBasecodeMinor = (byte)((BasecodeVersion - AppBasecodeMajor) * 100);
                                                        AppBasecode = (ushort)((AppBasecodeMajor << 8) + AppBasecodeMinor);
                                                        break;
                                                    default:
                                                        DisplayError(ErrorType.Warning, "Application field '" + FieldArgs[0].Trim() + "' not recognised.", Filename, CurrentLineNumber);
                                                        break;
                                                }
                                            } catch (Exception ex) {
                                                DisplayError(ErrorType.Warning, "Could not assign '" + FieldArgs[1].Trim() + "' to '" + FieldArgs[0].Trim() + "' - " + ex.Message, Filename, CurrentLineNumber);
                                            }
                                        }
                                        #endregion
                                        break;
									case ".appheaderpadding":
										#region Application Header Padding
										if (PassNumber == Pass.Labels) break;
										try {
											AppHeaderPadding = (uint)UintEvaluate(RestOfLine);
                                        } catch (Exception ex) {
                                            DisplayError(ErrorType.Error, "Could not evaluate '" + RestOfLine + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
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
                        #endregion
                    } else if ((bool)ConditionalStack.Peek() && AmAssembling) {

                        if (DeclaringStruct) {
                            DisplayError(ErrorType.Error, "You may not insert labels or assembly code inside a struct.", Filename, CurrentLineNumber);
                            if (StrictMode) return false;
                            break;
                        }

                        if (AmDefiningLongMacro) {
                            if (LastReplacement == null) {
                                DisplayError(ErrorType.Error, "Could not add to macro (no macro to add to!)", Filename, CurrentLineNumber);
                                if (StrictMode) return false;
                            } else {
                                LastMacro.Replacements.Remove(LastReplacement);
                                LastReplacement.ReplacementString += @"\ " + RealSourceLine;
                                LastMacro.Replacements.Add(LastReplacement);
                                LastMacro.Replacements.Sort();
                                AvailableMacros[LastMacro.Name] = LastMacro;
                                continue;
                            }
                        } else {
                            if (FindFirstChar == 0) {
                                #region Label Detection
                                // Label
                                int EndOfLabel = SourceLine.IndexOfAny("\t .#:=".ToCharArray()); // Remove '=' to fix TASM compatibility hack
                                string CheckLabelName;
                                if (EndOfLabel == -1) {
                                    CheckLabelName = SourceLine.Trim().Replace(":", "");
                                } else {
                                    CheckLabelName = SourceLine.Remove(EndOfLabel);
                                    if (SourceLine[EndOfLabel] == '=') --EndOfLabel; // Comment out to fix TASM compatibility hack
                                    SourceLine = SourceLine.Substring(EndOfLabel + 1);
                                }
                                if (CheckLabelName.Length != 0) {
                                    bool IsReusable = (CheckLabelName == "@") || (CheckLabelName.Replace("+", "") == "") || (CheckLabelName.Replace("-", "") == "");

                                    if (PassNumber == Pass.Labels || IsReusable) {
                                        if (!IsReusable) {
                                            JustHitALabel = true;
                                            JustHitLabelName = CheckLabelName;
                                        }
                                        string err;
                                        if (!IsReusable && !IsValidLabelName(CheckLabelName, out err)) {
                                            DisplayError(ErrorType.Error, "Invalid label name '" + CheckLabelName + "' (" + err + ").", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        } else if (!AddNewLabel(CheckLabelName, CurrentPage.ProgramCounter + RelocationOffset, (EndOfLabel != -1 && SourceLine.Trim().StartsWith("=")), Filename, CurrentLineNumber, PassNumber, CurrentPage.Page, true)) {
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

                                    InstructionGroup IGroup;

                                    if (!Instructions.TryGetValue(Instr, out IGroup)) {
                                        if (NotYetAsmFailed) {
                                            NotYetAsmFailed = false;
                                            // Assume it's a label then.
                                            SourceLine = SourceLine.TrimStart();

                                            string AmendedLineWithLabel = "";
                                            for (int i = 0; i < SplitLines.Length; ++i) {
                                                if (i != 0) AmendedLineWithLabel += @"\";
                                                if (i == LinePart) {
                                                    SplitLines[i] = SplitLines[i].TrimStart();
                                                }
                                                AmendedLineWithLabel += SplitLines[i];
                                            }
                                            RealSourceLines[CurrentLineNumber - 1] = AmendedLineWithLabel;
                                            RealSourceLine = AmendedLineWithLabel;
                                            goto CarryOnAssembling;
                                        } else {
                                            DisplayError(ErrorType.Error, "Instruction '" + Instr + "' not understood.", Filename, CurrentLineNumber);
                                            if (StrictMode) return false;
                                        }

                                    } else {
                                        string Args = "";


                                        string[] MultipleStatements = SafeSplit(SourceLine, '\\');
                                        if (MultipleStatements.Length == 0) {
                                            DisplayError(ErrorType.Error, "Internal assembler error.", Filename, CurrentLineNumber);
                                            return false;
                                        }

                                        // Strip out whitespace
                                        Args = SafeStripWhitespace(MultipleStatements[0].Substring(FindFirstChar));

                                        List<string> SourceArgs = new List<string>();

                                        Instruction I = null;

                                        List<string> MatchedArgs = new List<string>(2);

                                        if (Args == "") {
                                            I = IGroup.NoArgs;
                                        } else {
                                            if (!IGroup.SingleArgs.TryGetValue(Args.ToLower(), out I)) {
                                                foreach (Instruction FindInstruction in IGroup.MultipleArgs) {
                                                    if (MatchWildcards(FindInstruction.Arguments, Args, ref MatchedArgs)) {
                                                        I = FindInstruction;
                                                        break;
                                                    }
                                                }
                                            }
                                        }

                                        if (I == null) {
                                            DisplayError(ErrorType.Error, "Argument '" + Args + "' (for '" + Instr + "') not understood.", Filename, CurrentLineNumber);
                                            MatchedAssembly.Enqueue(new MatchedAssemblyNugget(FindFirstChar, CurrentFilename, CurrentLineNumber));
                                            if (StrictMode) return false;
                                        } else {
                                            FindFirstChar = MultipleStatements[0].Length;
                                            MatchedAssembly.Enqueue(new MatchedAssemblyNugget(I, MatchedArgs, FindFirstChar, CurrentFilename, CurrentLineNumber));
											//Console.WriteLine("ENQ\t" + CurrentLineNumber + "\t" + Path.GetFileName(CurrentFilename));
                                            CurrentPage.ProgramCounter += (uint)(I.Size);
                                            continue;
                                        }
                                    }
                                } else {
                                    #region Assemble

									//Console.WriteLine("DEQ\t" + CurrentLineNumber + "\t" + Path.GetFileName(CurrentFilename));
                                    if (MatchedAssembly.Count == 0) {
                                        DisplayError(ErrorType.Error, "Fatal assembly error (unidentified previous instruction).", Filename, CurrentLineNumber);
                                        return false;
                                    }
                                    MatchedAssemblyNugget MAN = MatchedAssembly.Dequeue();

									if (MAN.LineNumber != CurrentLineNumber || MAN.Filename != CurrentFilename) {
										DisplayError(ErrorType.Error, "Fatal parser error. Check syntax.", CurrentFilename, CurrentLineNumber);
										MatchedAssembly.Clear();
										return false;
									}

                                    Instruction I = MAN.MatchedInstruction;
                                    List<string> SourceArgs = MAN.Arguments;

                                    foreach (string S in SourceArgs) {
                                        if (S.Length >= 2 && (S[0] == '(' && S[S.Length - 1] == ')')) {
                                            int ParensLevel = 0;
                                            bool InvalidIndex = true;
                                            for (int i = 0; InvalidIndex && i < S.Length - 1; ++i) {
                                                switch (S[i]) {
                                                    case '(': ++ParensLevel; break;
                                                    case ')': --ParensLevel; if (ParensLevel == 0) InvalidIndex = false; break;
                                                }
                                            }
                                            if (InvalidIndex) DisplayError(ErrorType.Warning, "Instruction " + I.Name + " " + I.Arguments + " does not expect an index - check parentheses.", Filename, CurrentLineNumber);
                                        }
                                    }

                                    FindFirstChar = MAN.FindFirstChar;

                                    byte[] InstructionBytes = new byte[I.Size];
                                    for (int i = 0; i < I.Opcodes.Length; ++i) {
                                        InstructionBytes[i] = I.Opcodes[i];
                                    }


                                    for (int i = 0; i < SourceArgs.Count; ++i) {
                                        string TestArg = (string)SourceArgs[i];
                                        if (TestArg == "" && !(I.Rule == Instruction.InstructionRule.ZIdX && i == 0)) {
                                            DisplayError(ErrorType.Warning, "Missing argument? (Expected " + I.Name + " " + I.Arguments + ")", Filename, CurrentLineNumber);
                                        }
                                    }


                                    //ArrayList AdjustedArgs = new ArrayList();

                                    int RealArgument = 0;
                                    try {
                                        RealArgument = (((SourceArgs.Count == 0) ? 0 : IntEvaluate((string)SourceArgs[0])));
                                    } catch (Exception ex) {
                                        DisplayError(ErrorType.Error, "Could not parse expression '" + SourceArgs[0] + "' (" + ex.Message + ").", Filename, CurrentLineNumber);
                                        if (StrictMode) return false;
                                    }

                                    if (I.Rule == Instruction.InstructionRule.ZIdX && SourceArgs.Count == 1) {
                                        RealArgument &= 0xFF;
                                    }


                                    if (I.Rule != Instruction.InstructionRule.ZBit && !(I.Rule == Instruction.InstructionRule.ZIdX && SourceArgs.Count != 1)) {
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
                                            RealArgument -= (int)(CurrentPage.ProgramCounter + I.Size + RelocationOffset);
                                            if (RealArgument > 127 || RealArgument < -128) {
                                                DisplayError(ErrorType.Error, "Range of relative jump exceeded. (" + RealArgument + " bytes)", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            InstructionBytes[InstructionBytes.Length - 1] = (byte)RealArgument;
                                            break;
                                        case Instruction.InstructionRule.R2:
                                            RealArgument -= (int)(CurrentPage.ProgramCounter + I.Size + RelocationOffset);
                                            if (RealArgument > 32767 || RealArgument < -32768) {
                                                DisplayError(ErrorType.Error, "Range of relative jump exceeded. (" + RealArgument + " bytes)", Filename, CurrentLineNumber);
                                                if (StrictMode) return false;
                                            }
                                            InstructionBytes[InstructionBytes.Length - 2] = (byte)(RealArgument & 0xFF);
                                            InstructionBytes[InstructionBytes.Length - 1] = (byte)(RealArgument >> 8);
                                            break;
                                        case Instruction.InstructionRule.ZIdX:
                                            if (SourceArgs.Count == 2) {
                                                for (int i = I.Opcodes.Length; i < I.Size; i++) {
                                                    int Arg = 0;
                                                    string ToUse = (string)SourceArgs[i - I.Opcodes.Length];
                                                    try {
                                                        Arg = IntEvaluate(ToUse);
                                                        InstructionBytes[i] = (byte)Arg;
                                                    } catch {
                                                        DisplayError(ErrorType.Error, "Could not understand argument '" + ToUse + "'.", Filename, CurrentLineNumber);
                                                        if (StrictMode) return false;
                                                    }
                                                }
                                            } else {
                                                for (int j = I.Opcodes.Length; j < I.Size; ++j) {
                                                    InstructionBytes[j] = (byte)(RealArgument & 0xFF);
                                                    RealArgument >>= 8;
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
                                                    SecondArgument = IntEvaluate((string)SourceArgs[1]);
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

                                    WriteToBinary(InstructionBytes);
                                    //OutputAddresses.Add(new OutputAddress((uint)(CurrentPage.ProgramCounter + RelocationOffset), CurrentPage.Page, CurrentFilename, CurrentLineNumber));

                                    //ProgramCounter += I.Size;
                                    /*SourceLine = SourceLine.Substring(FindFirstChar);
                                    goto CarryOnAssembling;*/
                                    continue;
                                    #endregion

                                }
                            }
                        }
                    }
                }
            }

            if (InsideComment) DisplayError(ErrorType.Warning, "Unclosed multiline comment /* */ in file.", Filename, LineCommentOpened);

            return true;
        }
    }
}
