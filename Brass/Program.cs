using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections.Specialized;

namespace Brass {
    public partial class Program {


        public static StringDictionary EnvironmentVariables;

        public static int OutputFilenameCount = 0;

        public static bool StrictMode = false;

        public static bool DelayAtEnd = false;

        public static bool GivenFilename = false;

        static int Main(string[] args) {
            string Title = "Brass Z80 Assembler " +
                Assembly.GetExecutingAssembly().GetName().Version.ToString() +
                " - Ben Ryves 2005-2006";
            Console.WriteLine(Title);
            Console.WriteLine("".PadRight(Title.Length, '-'));

            using (System.Diagnostics.Process P = new System.Diagnostics.Process()) {
                EnvironmentVariables = P.StartInfo.EnvironmentVariables;
            }


            /*if (args.Length == 0) {
                Console.WriteLine("No command-line arguments specified... installing the manual instead!");
                if (!Directory.Exists("Manual")) {
                    try {
                        Directory.CreateDirectory("Manual");
                    } catch { return 1; }
                }
                try {
                    using (TextWriter T = new StreamWriter("Manual/index.htm")) { T.Write(Brass.Properties.Resources.index); }
                    using (TextWriter T = new StreamWriter("Manual/style.css")) { T.Write(Brass.Properties.Resources.style); }
                    Brass.Properties.Resources.pig.Save("Manual/pig.png");
                    System.Diagnostics.Process P = new System.Diagnostics.Process();
                    P.StartInfo.FileName = "Manual/index.htm";
                    P.Start();
                } catch { }
                return 0;
            }*/
            
            ErrorLog = new ArrayList();

            string SourceFile = "";
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
                                ExportFile = Path.GetFileNameWithoutExtension(SourceFile) + "_labels.inc";
                                break;
                            case 1:
                                GivenFilename = true;
                                BinaryFile = Argument.Replace("\"", "");
                                try {
                                    ExportFile = Path.GetFileNameWithoutExtension(BinaryFile) + "_labels.inc";
                                } catch { }
                                break;
                            case 2:
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
            AllInstructions = new ArrayList();

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
                        case Binary.Intel: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".hex"; break;
                        case Binary.IntelWord: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".hex"; break;
                        case Binary.MOS: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".hex"; break;
                        case Binary.Motorola: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".hex"; break;
                        case Binary.SegaMS: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".sms"; break;
                        case Binary.SegaGG: BinaryFile = Path.GetFileNameWithoutExtension(BinaryFile) + ".gg"; break;
                    }
                }


                WriteBinary(BinaryFile);
            }

            // Write the export table:
            if (ExportTable.Count != 0) {
                try {
                    if (File.Exists(ExportFile)) File.Delete(ExportFile);
                    using (TextWriter T = new StreamWriter(ExportFile)) {
                        foreach (string Name in ExportTable) {
                            object Label = Labels[Name];
                            if (Label != null) {
                                T.WriteLine(Name + "\t.equ\t$" + ((LabelDetails)Label).Value.ToString("X4"));
                            } else {
                                DisplayError(ErrorType.Error, "Could not locate label " + Name + " for export table.");
                            }
                        }
                    }
                } catch (Exception ex) {
                    DisplayError(ErrorType.Warning, "Could not write export file: " + ex.Message);
                    throw;
                }
            }

            // Finally, quick overwrite check:
            /*int TrackOverwrites = BinaryStartLocation;

            if (HasBeenOutput != null) {

                while (TrackOverwrites <= BinaryEndLocation) {
                    if (HasBeenOutput[TrackOverwrites] > 1) {
                        int StartOfErrorRange = TrackOverwrites;
                        while (HasBeenOutput[TrackOverwrites] > 1 && TrackOverwrites <= BinaryEndLocation) {
                            ++TrackOverwrites;
                        }
                        DisplayError(ErrorType.Warning, "Binary data has been written more than once between $" + StartOfErrorRange.ToString("X4") + "->$" + (TrackOverwrites - 1).ToString("X4"));
                    }
                    ++TrackOverwrites;
                }
            }*/

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
                CurrentMessageLine += "\n"; // Flush message
                Console.WriteLine("Writing debug log...");
                try {
                    if (WriteCompleteXmlLog && File.Exists(DbgLogFile)) File.Delete(DbgLogFile);
                    using (TextWriter T = new StreamWriter(DbgLogFile, !WriteCompleteXmlLog)) {
                        if (WriteCompleteXmlLog) T.WriteLine("<latenite version=\"2\">");

                        T.Write("\t<debug binary=\"" + EscapeHTML(Path.GetFullPath(BinaryFile)) + "\" ");
                        if (EnvironmentVariables["debug_debugger"] != null) T.Write("debugger=\"" + EscapeHTML(Path.GetFullPath(EnvironmentVariables["debug_debugger"].Replace("\"", ""))) + "\" ");
                        if (EnvironmentVariables["debug_debugger_args"] != null) T.Write("debugger_args=\"" + EscapeHTML(EnvironmentVariables["debug_debugger_args"]) + "\" ");
                        T.WriteLine("/>");

                        Hashtable DebugFiles = new Hashtable();

                        foreach (object LabelName in Labels.Keys) {
                            LabelDetails LabelToAdd = (LabelDetails)Labels[LabelName];
                            if (DebugFiles[LabelToAdd.File] == null) {
                                DebugFiles[LabelToAdd.File] = new ArrayList();
                            }
                            ((ArrayList)DebugFiles[LabelToAdd.File]).Add(LabelToAdd);
                        }

                        foreach (object LabelFile in DebugFiles.Keys) {
                            T.WriteLine("\t\t<source file=\"" + EscapeHTML(Path.GetFullPath(LabelFile.ToString())) + "\">");
                            foreach (LabelDetails Label in ((ArrayList)DebugFiles[LabelFile])) {
                                T.WriteLine("\t\t\t<label name=\"" + EscapeHTML(Label.Name) + "\" value=\"" + EscapeHTML(Label.Value.ToString()) + "\" line=\"" + EscapeHTML(Label.Line.ToString()) + "\" />");
                            }
                            T.WriteLine("\t\t</source>");
                        }

                        if (WriteCompleteXmlLog) T.WriteLine("</latenite>");
                    }
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Could not write error log (" + ex.Message + ").");
                }
            }

            if (OutLstFile != "") {
                Console.WriteLine("Writing list file...");
                try {
                    using (TextWriter T = new StreamWriter(OutLstFile, false)) {
                        foreach (string Lbl in Labels.Keys) {
                            T.WriteLine("{0}:{1}", ((LabelDetails)(Labels[Lbl])).Value.ToString("X4"), Lbl);
                        }
                        foreach (ListFileEntry L in ListFile) {
                            string AssembledData = "";
                            foreach (byte B in L.Data) {
                                AssembledData += B.ToString("X2");
                            }
                            //TODO: Broken output (relative)
                            T.WriteLine("{0}:{1}\t{2}:{3}\t{4}\t{5}", (L.Address - 0).ToString("X4"), L.Address.ToString("X4"), Path.GetFileName(L.File), L.Line, AssembledData, L.Source);
                        }
                    }
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Could not write list file (" + ex.Message + ").");
                }
            }

            Console.WriteLine(Success ? "Done!" : "Build failed.");

            if (DelayAtEnd) Console.ReadKey();

            return 0;
        }
    }
}
