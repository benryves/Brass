using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;

namespace Brass {
    public partial class Program {
        public enum ExportFormat {
            Assembly,
            FullAssembly,
            LabelFile,
            NoGmb,
            EmukonPatch
        }
        public static ExportFormat ExportFileFormat = ExportFormat.Assembly;

        public static void WriteExportFile(string SourceFile, string ExportFile) {
            // Write the export table:
            List<LabelDetails> ToExport = new List<LabelDetails>();

            AddLabelsFromModule(GlobalLabels, ref ToExport);
            AddLabelsFromModule(RootModule, ref ToExport);
            AddLabelsFromModule(NoName, ref ToExport);

            if (ToExport.Count != 0) {
                try {
                    switch (ExportFileFormat) {
                        case ExportFormat.Assembly:
                        case ExportFormat.FullAssembly: {
                                using (TextWriter T = new StreamWriter(ExportFile, false)) {
                                    T.WriteLine("; Output by Brass {0} at {1}", Assembly.GetExecutingAssembly().GetName().Version.ToString(), DateTime.Now);
                                    T.WriteLine("; Source file: {0}", SourceFile);
                                    T.WriteLine();
                                    foreach (LabelDetails L in ToExport) {
                                        T.Write("{0}\t.equ\t${1:X4}", L.FullPath, L.Value & 0xFFFF);
                                        if (ExportFileFormat == ExportFormat.FullAssembly) {
                                            T.Write(",{0}", L.Page);
                                        }
                                        T.WriteLine("\t; [{0}:{1}]", L.File, L.Line);
                                    }
                                }
                            }
                            break;
                        case ExportFormat.LabelFile: {
                                using (BinaryWriter BW = new BinaryWriter(new FileStream(ExportFile, FileMode.Create))) {
                                    foreach (LabelDetails L in ToExport) {
                                        BW.Write((byte)L.FullPath.Length);
                                        BW.Write(L.FullPath.ToCharArray());
                                        BW.Write((ushort)L.Value);
                                        BW.Write((ushort)L.Page);
                                    }
                                }
                            }
                            break;
                        case ExportFormat.NoGmb: {
                                using (TextWriter T = new StreamWriter(ExportFile, false)) {
                                    T.WriteLine("; Output by Brass {0} at {1}", Assembly.GetExecutingAssembly().GetName().Version.ToString(), DateTime.Now);
                                    T.WriteLine("; Source file: {0}", SourceFile);
                                    foreach (LabelDetails L in ToExport) {
                                        T.WriteLine("{0:X4}:{1:X4} {2}", L.Page & 0xFFFF, L.Value & 0xFFFF, L.FullPath);
                                    }
                                }
                            }
                            break;
                        case ExportFormat.EmukonPatch: {
                                using (TextWriter T = new StreamWriter(ExportFile, false)) {
                                    T.WriteLine("; Output by Brass {0} at {1}", Assembly.GetExecutingAssembly().GetName().Version.ToString(), DateTime.Now);
                                    T.WriteLine("; Source file: {0}", SourceFile);
                                    foreach (LabelDetails L in ToExport) {
                                        T.WriteLine("Label ${0:X4}, \"{1}\"", L.Value & 0xFFFF, EscapeString(L.FullPath));
                                        if (L.IsVariable) {
                                            string Size;
                                            switch (L.Size) {
                                                case 1: Size = "B"; break;
                                                case 4: Size = "D"; break;
                                                default: Size = "W"; break;
                                            }
                                            T.WriteLine("Var{0} ${1:X4}, \"{2}\"", Size, L.Value & 0xFFFF, EscapeString(L.FullPath));
                                        }
                                    }
                                    foreach (Breakpoint B in Breakpoints) {
                                        T.WriteLine("BP ${0:X4}", B.Address);                                        
                                    }
                                }
                            }
                            break;

                    }

                } catch (Exception ex) {
                    DisplayError(ErrorType.Warning, "Could not write export file: " + ex.Message);
                }
            }
        }

    }
}
