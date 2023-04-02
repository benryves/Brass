using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections.Specialized;
using System.Collections.Generic;

namespace Brass {
    public partial class Program {


        public static StringDictionary EnvironmentVariables;

        public static int OutputFilenameCount = 0;

        public static bool StrictMode = false;

        public static bool DelayAtEnd = false;

        public static bool GivenFilename = false;

        public static bool GivenExportFilename = false;

        //public static bool ListFileRequired = false;

        public static string VersionString;

        public static string SourceFile = "";

        static int Main(string[] args) {
            VersionString = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            string Title = "Brass Z80 Assembler " +
                VersionString +
                " - Ben Ryves 2005-2006";
            Console.WriteLine(Title);
            Console.WriteLine("".PadRight(Title.Length, '-'));

            using (System.Diagnostics.Process P = new System.Diagnostics.Process()) {
                EnvironmentVariables = P.StartInfo.EnvironmentVariables;
            }
           
            ErrorLog = new List<Error>();

            
            string BinaryFile = "";
            string ExportFile = "";
            string ErrLogFile = "";
            string OutLstFile = "";
            string TTableFile = "";
            string DbgLogFile = "";

            bool WaitingForListFile = false;
            bool WaitingForTableFile = false;

            bool WriteCompleteXmlLog = true;

            foreach (string Argument in args) {
                if (Argument.StartsWith("-") && Argument.Length == 2) {
                    switch (Argument.ToLower()[1]) {
                        case 's':
                            IsCaseSensitive = true;
                            break;
                        case 'x':
                            if (EnvironmentVariables["error_log"] == null) {
                                DisplayError(ErrorType.Warning, "Environment variable ERROR_LOG not set.");
                            } else {
                                ErrLogFile = EnvironmentVariables["error_log"];
                            }
                            break;
                        case 'd':
                            if (EnvironmentVariables["debug_log"] == null) {
                                DisplayError(ErrorType.Warning, "Environment variable DEBUG_LOG not set.");
                            } else {
                                DbgLogFile = EnvironmentVariables["debug_log"];
                            }
                            break;
                        case 'o':
                            WriteCompleteXmlLog = false;
                            break;
                        case 'e':
                            StrictMode = true;
                            break;
                        case 'l':
                            WaitingForListFile = true;
                            break;
                        case 't':
                            WaitingForTableFile = true;
                            break;
                        case 'p':
                            DelayAtEnd = true;
                            break;
                        default:
                            DisplayError(ErrorType.Warning, Argument + " is not a valid command-line switch.");
                            break;
                    }
                } else {
                    if (WaitingForListFile) {
                        OutLstFile = Argument.Replace("\"", "");
                        WaitingForListFile = false;
                    } else if (WaitingForTableFile) {
                        TTableFile = Argument.Replace("\"", "");
                        WaitingForTableFile = false;
                    } else {
                        switch (OutputFilenameCount) {
                            case 0:
                                SourceFile = Argument.Replace("\"", "");
                                BinaryFile = Path.GetFileNameWithoutExtension(SourceFile) + ".bin";
                                break;
                            case 1:
                                GivenFilename = true;
                                BinaryFile = Argument.Replace("\"", "");
                                break;
                            case 2:
                                GivenExportFilename = true;
                                ExportFile = Argument.Replace("\"", "");
                                break;
                            default:
                                DisplayError(ErrorType.Warning, "You have specified too many filenames in the command-line - check your typing!");
                                break;
                        }
                        ++OutputFilenameCount;
                    }
                    
                }
            }

            if (BinaryFile == "" || SourceFile == "") {
                DisplayError(ErrorType.Error, "Command-line syntax incorrect.");
                return 1;
            }

            VariableName = Path.GetFileNameWithoutExtension(SourceFile).ToUpper();

            // Parse the instruction list
            AllInstructions = new List<Instruction>(64000);

            if (TTableFile == "") {
                GenerateDefaultTable();
            } else {
                try {
                    using (TextReader T = new StreamReader(TTableFile)) {
                        string L = T.ReadLine();
                        int TFileLine = 1;
                        while (L != null) {
                            if (!AddInstructionLine(L.Trim())) {
                                Console.WriteLine("Problem reading line {0} in {1}.", TFileLine, Path.GetFileName(TTableFile));
                            }
                            ++TFileLine;
                            L = T.ReadLine();
                        }
                    }
                } catch (Exception ex) {
                    Console.WriteLine("Error reading table file: {0}", ex.Message);
                    throw;
                }
            }

            RehashInstructionTable();

            // Assemble!

            Console.WriteLine("Assembling...");
            AssembleFile(SourceFile);
            CloseFileHandles();
            
            bool Success = (TotalErrors == 0);
            if (Success) {
                Console.WriteLine("Writing output file...");

                if (!GivenFilename) {
                    switch (BinaryType) {
                        case Binary.Raw: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".bin"; break;
                        case Binary.TI8X: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".8xp"; break;
                        case Binary.TI83: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".83p"; break;
                        case Binary.TI82: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".82p"; break;
                        case Binary.TI86: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".86p"; break;
                        case Binary.TI85: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".85p"; break;
                        case Binary.TI73: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".73p"; break;
                        case Binary.Intel:
                        case Binary.IntelWord:
                        case Binary.MOS:
                        case Binary.Motorola: 
                        case Binary.TI73App:
                        case Binary.TI8XApp:
                            BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".hex"; break;
                        case Binary.SegaMS: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".sms"; break;
                        case Binary.SegaGG: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".gg"; break;
                    }
                }
                WriteBinary(Path.GetFullPath(BinaryFile));
            }

            if (!GivenExportFilename) {
                switch (ExportFileFormat) {
                    case ExportFormat.Assembly: case ExportFormat.FullAssembly: ExportFile = BinaryFile + ".inc"; break;
                    case ExportFormat.LabelFile: ExportFile = BinaryFile + ".lbl"; break;
                    case ExportFormat.NoGmb: ExportFile = BinaryFile + ".sym"; break;
                    case ExportFormat.EmukonPatch: ExportFile = BinaryFile + ".pat"; break;
                }
            }

            WriteExportFile(SourceFile, ExportFile);

            Console.WriteLine("Errors: " + TotalErrors + ", Warnings: " + TotalWarnings + ".");

            // Do we write an error log?

            if (ErrLogFile != "") {
                CurrentMessageLine += "\n"; // Flush message
                Console.WriteLine("Writing error log...");
                try {
                    if (WriteCompleteXmlLog && File.Exists(ErrLogFile)) File.Delete(ErrLogFile);
                    using (TextWriter T = new StreamWriter(ErrLogFile, !WriteCompleteXmlLog)) {
                        if (WriteCompleteXmlLog) T.WriteLine("<latenite version=\"2\">");

                        foreach (Error E in ErrorLog) {
                            if (E.Message != "") {
                                string Tag = "";
                                switch (E.E) {
                                    case ErrorType.Error:
                                        Tag = "error"; break;
                                    case ErrorType.Warning:
                                        Tag = "warning"; break;
                                    case ErrorType.Message:
                                        Tag = "message"; break;
                                }
                                T.Write("<{0}", Tag);

                                if (E.Line != 0) T.Write(" line=\"{0}\"", E.Line);
                                if (E.File != "") T.Write(" file=\"{0}\"", E.File);
                                T.Write(">");
                                T.Write(EscapeHTML(E.Message));
                                T.Write("</{0}>\n", Tag);
                            }
                        }

                        if (WriteCompleteXmlLog) T.WriteLine("</latenite>");
                    }
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Could not write error log (" + ex.Message + ").");
                }
            }

            if (DbgLogFile != "") {
                WriteDebugLog(DbgLogFile, WriteCompleteXmlLog, BinaryFile);
            }

            if (TotalErrors == 0 && OutLstFile != "") {
                Console.WriteLine("Writing list file...");
                try {
                    /*using (TextWriter T = new StreamWriter(OutLstFile, false)) {
                        foreach (KeyValuePair<uint, List<ListFileEntry>> L in AllListFiles) {
                            foreach (ListFileEntry E in L.Value) {
                                string ExpandedData = "";
                                bool First = true;
                                foreach (byte B in E.Data) {
                                    if (First) {
                                        First = false;
                                    } else {
                                        ExpandedData += " ";
                                    }
                                    ExpandedData += B.ToString("X2");
                                }
                                T.WriteLine("{0:X2}\t{1:X4}\t{2}\t[{3}:{4}]\t{5}", L.Key, E.Address, ExpandedData, E.File, E.Line, E.Source, E.Source);
                            }
                            T.WriteLine();
                        }
                    }*/
                    GenerateListFile(OutLstFile);
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Could not write list file (" + ex.Message + ").");
                }
            }
            Console.WriteLine(Success ? "Done!" : "Build failed.");
            if (DelayAtEnd) Console.ReadKey();
            return 0;
        }
        private static void AddLabelsFromModule(Module LabelModule, ref List<LabelDetails> LabelList) {
            foreach (KeyValuePair<string, LabelDetails> L in LabelModule.Labels) {
                if (L.Value.ExportMe) LabelList.Add(L.Value);
            }
            foreach (KeyValuePair<string, Module> M in LabelModule.Modules) {
                AddLabelsFromModule(M.Value, ref LabelList);
            }
        }

        public static bool DateTimeTryParse(string s, out DateTime result) {
            try {
                result = DateTime.Parse(s);
                return true;
            } catch {
                result = DateTime.Now;
                return false;
            }
        }

    }

    
}
