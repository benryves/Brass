using System;
using System.Collections.Generic;
using System.IO;

namespace Brass {
    public partial class Program {


        public static void GenerateListFile(string Filename) {
            using (TextWriter T = new StreamWriter(Filename, false)) {

                T.WriteLine("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\">\n<html>\n<head>\n<title>List file for {0} ({1})</title>\n</head>\n<body>", EscapeHTML(SourceFile), EscapeHTML(DateTime.Now.ToString()));

                T.WriteLine("<style>");
                T.WriteLine("h1, h2, h3, h4, p { font-family: trebuchet ms, verdana, tahoma, sans serif; text-align: center; }");
                T.WriteLine("#asmout, #ascii { border-collapse: collapse; border: 1px solid black; margin-left: auto; margin-right: auto; }");
                T.WriteLine("#asmout td, #asmout th, #ascii td, #ascii th { vertical-align: top; padding: 3px; margin: 0px; border-left: 1px solid #333333; border-right: 1px solid #333333; font-family: consolas, lucida console, courier new, monospace; font-size: 10pt; }");
                T.WriteLine("*.r0 { background-color: #DDDDDD; }");
                T.WriteLine("*.r1 { background-color: #CCCCCC; }");
                T.WriteLine("#asmout td.f { background-color: #9999FF; text-align: center; border: 1px solid #333333; }");
                T.WriteLine("#asmout td.p { background-color: #99FF99; text-align: center; border: 1px solid #333333; }");
                T.WriteLine("#asmout td.j { background-color: #FF9999; text-align: center; border: 1px solid #333333; }");
                T.WriteLine("#asmout td.c { text-align: center; }");
                T.WriteLine("#asmout th, #ascii th { font-weight: bold; text-align: center; background-color: black; color: white;}");
                T.WriteLine("#ascii td { text-align: center; width: 24px; } #unused { color: #999999; }");
                T.WriteLine("</style>");
                OutputAddresses.Sort();
                string LastFilename = null;
                
                string[] FileLines = null;


                uint? LastPage = null;
                int? LastLine = null;

                T.WriteLine("<h1>General Information</h1>");
                T.WriteLine("<table id=\"asmout\">");
                T.WriteLine("<th>Field</th><th>Value</th>");
                T.WriteLine("<tr class=\"r0\"><td>Brass Version</td><td>{0}</td></tr>", VersionString);
                T.WriteLine("<tr class=\"r1\"><td>Source File</td><td>{0}</td></tr>", EscapeHTML(Path.GetFullPath(SourceFile)));
                T.WriteLine("<tr class=\"r0\"><td>Total Source Files</td><td>{0}</td></tr>", EscapeHTML(AssembledFileCount.ToString()));
                T.WriteLine("<tr class=\"r1\"><td>Page Count</td><td>{0}</td></tr>", Pages.Count);
                T.WriteLine("</table>");


                if (ASCIITable.Count != 0) {
                    //List<AsciiMapSorter> s = new List<AsciiMapSorter>(ASCIITable.Count);


                    char[] Map = new char[256];
                    foreach (KeyValuePair<char, byte> kvp in ASCIITable) {
                        Map[kvp.Value] = kvp.Key;
                    }

                    T.WriteLine("<h1>Custom ASCII Mapping</h1>");
                    T.WriteLine("<table id=\"ascii\">");
                    T.WriteLine("<tr><th colspan=\"16\">ASCII Characters ($00 to $FF)</th></tr>");

                    for (int y = 0; y < 32; ++y) {
                        T.WriteLine("<tr>");
                        for (int x = 0; x < 8; ++x) {
                            byte a = (byte)(x * 32 + y);
                            if (ASCIITable.ContainsValue(a)) {
                                T.Write("<td class=\"r{0}\">${1:X2}</td><td>&#{2};</td>", y & 1, a, (int)Map[a]);
                            } else {
                                T.Write("<td class=\"r{0}\" id=\"unused\">${1:X2}</td><td></td>", y & 1, a);
                            }
                            
                        }
                        T.WriteLine("</tr>");
                    }
                   
                    T.WriteLine("</table>");
                }

                T.WriteLine("<h1>Output</h1>");
                

                List<OutputAddress> Record = new List<OutputAddress>();
                List<OutputAddress[]> AllRecords = new List<OutputAddress[]>();

                if (OutputAddresses.Count != 0) {
                    for (int i = 0; i <= OutputAddresses.Count; ++i) {

                        OutputAddress O = OutputAddresses[Math.Min(i, OutputAddresses.Count - 1)];

                        if (Record != null && Record.Count > 0 && (i == OutputAddresses.Count || !LastPage.HasValue || !LastLine.HasValue || LastPage.Value != O.Page || LastLine.Value != O.LineNumber || O.Filename != LastFilename)) {
                            AllRecords.Add(Record.ToArray());
                            Record = new List<OutputAddress>();
                            LastPage = O.Page;
                            LastLine = O.LineNumber;
                            LastFilename = O.Filename;
                        }

                        if (i != OutputAddresses.Count) Record.Add(O);

                    }



                    LastLine = null;
                    int RowCount = 0;
                    LastFilename = null;
                    LastPage = null;

                    T.WriteLine("<table id=\"asmout\">");
                    T.WriteLine("<tr><th>Address</th><th>Output</th><th>Source</th></tr>");

                    uint RunningAddressCounter = 0;
                    bool First = true;
                    foreach (OutputAddress[] O in AllRecords) {

                        if (First) {
                            First = false;
                            RunningAddressCounter = O[0].Address;
                        }

                        string HexOut = "";
                        if (!LastLine.HasValue || LastLine != O[0].LineNumber) {
                            FileLines = SourceFiles[O[0].Filename.ToLower()];
                        }
                        int HexCharCount = 0;

                        foreach (OutputAddress o in O) {
                            HexOut += o.Value.ToString("X2") + " ";
                            if (((++HexCharCount) & 7) == 0) HexOut += "\n";
                        }
                        HexOut = HexOut.Trim().Replace(" ", "&nbsp;").Replace("\n", "<br />");

                        if (LastFilename != O[0].Filename) {
                            LastFilename = O[0].Filename;
                            T.WriteLine("<tr><td class=\"f\" colspan=\"4\">{0}</td></tr>", EscapeHTML(LastFilename));

                        }

                        if (LastPage != O[0].Page) {
                            LastPage = O[0].Page;
                            T.WriteLine("<tr><td class=\"p\" colspan=\"4\">{0}</td></tr>", EscapeHTML(string.Format("Page {0}", LastPage)));
                        }

                        if (RunningAddressCounter != O[0].Address) {
                            T.WriteLine("<tr><td class=\"j\" colspan=\"4\">Skipped {0} bytes</td></tr>", O[0].Address - RunningAddressCounter);
                        }

                        T.WriteLine("<tr class=\"r{3}\"><td class=\"c\">{0:X4}</td><td>{1}</td><td>{2}</td></tr>",
                            O[0].Address,
                            HexOut,
                            EscapeHTML(FileLines[O[0].LineNumber - 1]).Trim(),
                            (RowCount++) & 1);

                        RunningAddressCounter = O[0].Address + (uint)O.Length;
                    }

                    T.WriteLine("</table>");

                } else {
                    T.WriteLine("<p>None</p>");
                }


                T.WriteLine("<h1>Variables</h1>");
                List<LabelDetails> AllLabels = GetAllLabels();                
                
                AllLabels.Sort();
                T.WriteLine("<table id=\"asmout\">");
                foreach (LabelDetails l in AllLabels) {
                    if (l.IsVariable) {
                        T.WriteLine("<tr><td>{0}</td><td>{1:X4}</td></tr>", EscapeHTML(l.FullPath), l.Value);
                    }
                }
                T.WriteLine("</table>");
                


                T.WriteLine("</body></html>");



            }
        }
        public static List<LabelDetails> GetAllLabels() {
            List<LabelDetails> list = new List<LabelDetails>();
            AddLabelsFromModule(list, GlobalLabels);
            AddLabelsFromModule(list, NoName);
            AddLabelsFromModule(list, RootModule);
            return list;
        }
        private static void AddLabelsFromModule(List<LabelDetails> list, Module module) {
            list.AddRange(module.Labels.Values);
            foreach (Module m in module.Modules.Values) AddLabelsFromModule(list, m);
        }
    }
    
}