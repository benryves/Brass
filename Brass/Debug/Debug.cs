using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Brass {
    public partial class Program {
        private static void WriteDebugLog(string DbgLogFile, bool WriteCompleteXmlLog, string BinaryFile) {
            CurrentMessageLine += "\n"; // Flush messages
            Console.WriteLine("Writing debug log...");
            try {
                
                if (WriteCompleteXmlLog && File.Exists(DbgLogFile)) File.Delete(DbgLogFile);
                using (TextWriter T = new StreamWriter(DbgLogFile, !WriteCompleteXmlLog)) {
                    if (WriteCompleteXmlLog) T.WriteLine("<brass version=\"{0}\">", VersionString);

                    T.Write("<debug binary=\"" + EscapeHTML(Path.GetFullPath(BinaryFile)) + "\" ");
                    if (EnvironmentVariables["debug_debugger"] != null) T.Write("debugger=\"" + EscapeHTML(Path.GetFullPath(EnvironmentVariables["debug_debugger"].Replace("\"", ""))) + "\" ");
                    if (EnvironmentVariables["debug_debugger_args"] != null) T.Write("debugger_args=\"" + EscapeHTML(EnvironmentVariables["debug_debugger_args"]) + "\" ");
                    T.WriteLine("/>");

                    WriteModuleLabels(T, GlobalLabels);
                    WriteModuleLabels(T, NoName);
                    WriteModuleLabels(T, RootModule);                    

                    foreach (Breakpoint B in Breakpoints) {
                        string Description = "";
                        if (B.Description != "") {
                            Description = "description=\"" + EscapeHTML(B.Description) + "\" ";
                        }
                        T.WriteLine("<breakpoint file=\"{0}\" line=\"{1}\" page=\"{2}\" address=\"{3}\" {4}/>", 
                            EscapeHTML(Path.GetFullPath(B.Filename)),
                            EscapeHTML(B.LineNumber.ToString()),
                            EscapeHTML(B.Page.ToString()),
                            EscapeHTML(B.Address.ToString()),
                            Description);
                    }

                    OutputAddresses.Sort();
                    foreach (OutputAddress O in OutputAddresses) {
                        T.WriteLine("<address value=\"{0}\" page=\"{1}\" file=\"{2}\" line=\"{3}\" />", O.Address.ToString(), O.Page.ToString(), EscapeHTML(O.Filename), O.LineNumber.ToString());
                    }

                    if (WriteCompleteXmlLog) T.WriteLine("</brass>");
                }
            //*
            } catch (Exception ex) {
                DisplayError(ErrorType.Error, "Could not write error log (" + ex.Message + ").");
            }
            //*/

        }

        //private static List<string> DisplayedPaths = new List<string>();

        private static void WriteModuleLabels(TextWriter T, Module Start) {

            T.WriteLine("<module name=\"{0}\">", EscapeHTML(Start.Name));
            foreach (KeyValuePair<string, LabelDetails> L in Start.Labels) {

                /*if (!DisplayedPaths.Contains(L.Value.File)) {
                    DisplayedPaths.Add(L.Value.File);
                    Console.WriteLine("FILE = " + L.Value.File + "~" + L.Value.FullPath);
                }*/

                T.Write("<label name=\"{0}\" value=\"{1}\" page=\"{2}\" type=\"{3}\" fullname=\"{4}\" file=\"{5}\" line=\"{6}\" exported=\"{7}\"",
                    EscapeHTML(L.Value.Name),
                    L.Value.RealValue.ToString(InvariantCulture),
                    L.Value.Page.ToString(),
                    L.Value.Type == null ? "none" : L.Value.Type.OutputName,
                    EscapeHTML(L.Value.FullPath),
                    EscapeHTML(Path.GetFullPath(L.Value.File)),
                    L.Value.Line,
                    L.Value.ExportMe ? "true" : "false"
                );
                if (L.Value.References.Count == 0) {
                    T.WriteLine(" />");
                } else {
                    T.WriteLine(">");
                    foreach (LabelDetails.LabelReference R in L.Value.References) {
                        T.WriteLine("<ref file=\"{0}\" line=\"{1}\" />", EscapeHTML(R.Filename), R.LineNumber.ToString());
                    }
                    T.WriteLine("</label>");
                }
            }
            foreach (KeyValuePair<string, Module> M in Start.Modules) {
                WriteModuleLabels(T, M.Value);
            }
            T.WriteLine("</module>");
        }

        

    }
}
