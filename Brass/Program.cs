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

        

        static int Main(string[] args) {


            string Title = "Brass Z80 Assembler " +
                Assembly.GetExecutingAssembly().GetName().Version.ToString() +
                " - Ben Ryves 2005";
            Console.WriteLine(Title);
            Console.WriteLine("".PadRight(Title.Length, '-'));

            using (System.Diagnostics.Process P = new System.Diagnostics.Process()) {
                EnvironmentVariables = P.StartInfo.EnvironmentVariables;
            }

            ErrorLog = new ArrayList();

            if (args.Length == 0) {
                DisplayError(ErrorType.Error, "No command-line arguments specified!");
                return 1;
            }

            string SourceFile = "";
            string BinaryFile = "";
            string ExportFile = "";
            string ErrLogFile = "";
            string OutLstFile = "";
            string TTableFile = "";

            bool WaitingForListFile = false;
            bool WaitingForTableFile = false;

            bool WriteCompleteXmlLog = true;

            foreach (string Argument in args) {
                if (Argument.StartsWith("-") && Argument.Length == 2) {
                    switch (Argument.ToLower()[1]) {
                        case 's':
                            IsCaseSensitive = true;
                            break;
                        case 'd':
                            DebugMode = true;
                            break;
                        case 'x':
                            if (EnvironmentVariables["error_log"] == null) {
                                DisplayError(ErrorType.Warning, "Environment variable ERROR_LOG not set.");
                            } else {
                                ErrLogFile = EnvironmentVariables["error_log"];
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
                                BinaryFile = Argument.Replace("\"", "");
                                ExportFile = Path.GetFileNameWithoutExtension(BinaryFile) + "_labels.inc";
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

            // Parse the instruction list
            AllInstructions = new ArrayList();

            if (TTableFile == "") {

                string[] TableLines = Properties.Resources.Z80.Split('\n');
                foreach (string S in TableLines) {
                    AddInstructionLine(S.Trim());
                }
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

            
            bool Success = (TotalErrors == 0);
            if (Success) {
                Console.WriteLine("Writing output file...");
                WriteBinary(BinaryFile);
            }

            // Write the export table:
            if (ExportTable.Count != 0) {
                try {
                    if (File.Exists(ExportFile)) File.Delete(ExportFile);
                    using (TextWriter T = new StreamWriter(ExportFile)) {
                        foreach (string Name in ExportTable) {
                            object Label = Labels[IsCaseSensitive ? Name : Name.ToLower()];
                            if (Label != null) {
                                T.WriteLine(Name + "\t.equ\t$" + ((int)Label).ToString("X4"));
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
            int TrackOverwrites = BinaryStartLocation;

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
            }

            Console.WriteLine("Errors: " + TotalErrors + ", Warnings: " + TotalWarnings + ".");

            // Do we write an error log?

            if (ErrLogFile != "") {
                CurrentMessageLine += "\n"; // Flush message
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

            if (OutLstFile != "") {
                Console.WriteLine("Writing list file...");
                try {
                    using (TextWriter T = new StreamWriter(OutLstFile, false)) {
                        foreach (string Lbl in Labels.Keys) {
                            T.WriteLine("{0}:{1}", ((int)(Labels[Lbl])).ToString("X4"), Lbl);
                        }
                        foreach (ListFileEntry L in ListFile) {
                            string AssembledData = "";
                            foreach (byte B in L.Data) {
                                AssembledData += B.ToString("X2");
                            }
                            T.WriteLine("{0}:{1}\t{2}:{3}\t{4}\t{5}", (L.Address - BinaryStartLocation).ToString("X4"), L.Address.ToString("X4"), Path.GetFileName(L.File), L.Line, AssembledData, L.Source);
                        }
                    }
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Could not write list file (" + ex.Message + ").");
                }
            }

            Console.WriteLine(Success ? "Done!" : "Build failed.");

            return 0;
        }
    }
}
