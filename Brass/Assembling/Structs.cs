using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    partial class Program {

        private static Dictionary<string, Struct> Structs;
        private static Struct CurrentStruct = null;

        

        public class Struct {

            public readonly string Name = "";
            public List<LabelDetails> ExpandedLabels;

            public int Size {
                get {
                    int TotalSize = 0;
                    foreach (LabelDetails I in Items) {
                        TotalSize += (I.Type.Size * I.ArrayCount);
                    }
                    return TotalSize;
                }
            }

            public List<LabelDetails> Items;

            public readonly string Filename;
            public readonly int Linenumber;

            public Struct(string Name, string Filename, int Linenumber) {
                this.Name = Name;
                this.Items = new List<LabelDetails>(16);
                this.ExpandedLabels = new List<LabelDetails>();
                this.Filename = Filename;
                this.Linenumber = Linenumber;
            }

            public bool ExpandLabels(Module ModuleToExpandInto, LabelDetails RootLabel, string LabelName, bool CalculateVariables, int StartAddress) {

                for (int i = 0; i < RootLabel.ArrayCount; ++i) {


                    string SubModuleName = IsCaseSensitive ? LabelName : LabelName.ToLower();

                    if (RootLabel.ArrayCount != 1) SubModuleName += "[" + i + "]";

                    if (!CalculateVariables) {
                        if (ModuleToExpandInto.Modules.ContainsKey(SubModuleName)) {
                            DisplayError(ErrorType.Error, "Module " + ModuleToExpandInto.Name + " already contains a submodule " + this.Name);
                            return false;
                        }
                    }

                    Module SubModule;
                    if (CalculateVariables) {
                        if (!ModuleToExpandInto.Modules.TryGetValue(SubModuleName, out SubModule)) {
                            DisplayError(ErrorType.Error, "Fatal error: could not find module '" + LabelName + "' in '" + ModuleToExpandInto.Name + "' to set variable offset.", Filename, Linenumber);
                            return false;
                        }
                    } else {
                        SubModule = new Module(SubModuleName, ModuleToExpandInto);
                    }

                    if (!CalculateVariables) ModuleToExpandInto.Modules.Add(SubModuleName, SubModule);


                    foreach (LabelDetails I in this.Items) {
                        bool IsArray = I.ArrayCount != 1;
                        for (int j = IsArray ? -1 : 0; j < I.ArrayCount; ++j) {

                            string RLName = I.Name;
                            if (IsArray && j != -1) {
                                RLName += "[" + j + "]";
                            }

                            string LName = IsCaseSensitive ? RLName : RLName.ToLower();

                            if (CalculateVariables) {
                                SubModule.Labels[LName].RealValue = StartAddress;
                                SubModule.Labels[LName].IsVariable = true;
                                if (j != -1) StartAddress += I.Type.Size;
                            } else {
                                LabelDetails SubStructLabel = new LabelDetails(I.Type, RLName, 0.0d, Filename, Linenumber, CurrentPage.Page, SubModule, false);
                                SubModule.Labels.Add(LName, SubStructLabel);
                            }
                            if (I.Type.Type == DataType.Structure) {
                                StructureType S = (StructureType)I.Type;
                                if (!S.Structure.ExpandLabels(SubModule, I, RLName, CalculateVariables, StartAddress - I.Type.Size)) {
                                    return false;
                                }
                            }
                        }
                    }
                }                
                return true;
            }

        }

        private static bool IsSelfReferential(string StructName, Struct StructToCheck) {
            if (StructToCheck.Name == StructName) return true;
            foreach (LabelDetails I in StructToCheck.Items) {
                if (I.Type.Type == DataType.Structure && IsSelfReferential(StructName, ((StructureType)I.Type).Structure)) return true;
            }
            return false;
        }
    }
}
