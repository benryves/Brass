using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    partial class Program {

        public static List<VariableArea> VariableAreas;

        public class VariableArea : IComparable {
            public int Address;
            public int Size;

            private Stack<int> LockedAddress = new Stack<int>();
            private Stack<int> LockedSize = new Stack<int>();

            public void Lock() {
                LockedAddress.Push(Address);
                LockedSize.Push(Size);
            }

            public void Unlock() {
                Address = LockedAddress.Pop();
                Size = LockedSize.Pop();
            }

            public VariableArea(int Address, int Size) {
                this.Address = Address;
                this.Size = Size;
            }
            
            public bool AddVariable(LabelDetails Variable) {
                if (Variable.Size <= this.Size) {
                    Variable.RealValue = this.Address;

                    if (Variable.ArrayCount != 1) {
                        for (int i = 0; i < Variable.ArrayCount; ++i) {
                            string Name = Variable.Name + "[" + i + "]";
                            LabelDetails L = new LabelDetails(
                                Variable.Type,
                                Name,
                                this.Address + Variable.Type.Size * i,
                                Variable.File,
                                Variable.Line,
                                Variable.Page,
                                Variable.OwnerModule,
                                true);
                            L.ExportMe = false;
                            Variable.OwnerModule.Labels.Add(IsCaseSensitive ? Name : Name.ToLower(), L);
                        }
                    }

                    if (Variable.Type.Type == DataType.Structure) {
                        ((StructureType)Variable.Type).Structure.ExpandLabels(Variable.OwnerModule, Variable, Variable.Name, true, Variable.Value);
                    }

                    this.Size -= Variable.Size;
                    this.Address += Variable.Size;
                    return true;
                } else {
                    return false;
                }
            }

            public int CompareTo(object ToCompare) {
                if (ToCompare.GetType() != typeof(VariableArea)) throw new Exception("You can only compare variable areas to other variable areas");

                if (ToCompare.Equals(this)) {
                    return 0;
                } else {
                    return ((VariableArea)ToCompare).Size.CompareTo(this.Size);
                }
            }
            
        }

        public class VariableToAllocate : IComparable {
            public readonly LabelDetails Label;
            public VariableToAllocate(LabelDetails Label) {
                this.Label = Label;
            }

            public int CompareTo(object obj) {
                return ((VariableToAllocate)obj).Label.Size.CompareTo(this.Label.Size);
            }
        }

        public static void AllocateVariables() {           
            
            List<VariableToAllocate> StaticVars = new List<VariableToAllocate>();

            AllocateStaticVariablesFromModule(GlobalLabels, ref StaticVars);
            AllocateStaticVariablesFromModule(RootModule, ref StaticVars);
            AllocateStaticVariablesFromModule(NoName, ref StaticVars);

            if (VariableAreas.Count == 0 && StaticVars.Count > 0) {
                DisplayError(ErrorType.Error, "No variable areas defined (use .varloc)");
                return;
            }

            StaticVars.Sort();

            foreach (VariableToAllocate V in StaticVars) {
                VariableAreas.Sort();
                VariableArea ToUse = VariableAreas[0];
                if (!ToUse.AddVariable(V.Label)) {
                    DisplayError(ErrorType.Error, "Not enough space for variable " + V.Label.Name + ".", V.Label.File, V.Label.Line);
                    return;
                }
            }

            AllocateTemporaryVariablesFromModule(GlobalLabels);
            AllocateTemporaryVariablesFromModule(RootModule);
            AllocateTemporaryVariablesFromModule(NoName);
        }

        static void AllocateStaticVariablesFromModule(Module M, ref List<VariableToAllocate> V) {
            foreach (LabelDetails L in M.Labels.Values) {
                if (L.IsVariable && !L.TempVariable) {
                    V.Add(new VariableToAllocate(L));
                }
            }
            foreach (Module SM in M.Modules.Values) {
                AllocateStaticVariablesFromModule(SM, ref V);
            }
        }


        static void AllocateTemporaryVariablesFromModule(Module M) {
            foreach (VariableArea V in VariableAreas) V.Lock();

            VariableAreas.Sort();
            List<VariableToAllocate> SortedVariables = new List<VariableToAllocate>();
            foreach (LabelDetails L in M.Labels.Values) {
                if (L.IsVariable && L.TempVariable) {
                    SortedVariables.Add(new VariableToAllocate(L));
                }
            }
            SortedVariables.Sort();
            foreach (VariableToAllocate V in SortedVariables) {
                if (!VariableAreas[0].AddVariable(V.Label)) {
                    DisplayError(ErrorType.Error, "Not enough space for temporary variable " + V.Label.Name + ".", V.Label.File, V.Label.Line);
                    return;
                }
            }

            foreach (Module S in M.Modules.Values) {
                AllocateTemporaryVariablesFromModule(S);                
            }

            foreach (VariableArea V in VariableAreas) V.Unlock();
        }

        public static bool VariableDirective(string Command, string RestOfLine, string Filename, int CurrentLineNumber) {

            string[] VarArgs = SafeSplit(RestOfLine, ',');

            if (VarArgs.Length != 2) {
                DisplayError(ErrorType.Error, "Variables must have a name and size definition.", Filename, CurrentLineNumber);
                return false;
            }

            string VariableName = VarArgs[1].Trim();

            if (VariableName == "") {
                DisplayError(ErrorType.Error, "No variable name specified.", Filename, CurrentLineNumber);
                return false;
            }

            string VariableTypeString = VarArgs[0];
            int VariableItemCount = 1;

            int DetectArray = VariableTypeString.IndexOf('[');

            if (DetectArray != -1) {

                if (VariableTypeString[VariableTypeString.Length - 1] != ']') {
                    DisplayError(ErrorType.Error, "Incorrect variable array syntax.", Filename, CurrentLineNumber);
                    return false;
                }

                string VarCount = VariableTypeString.Substring(DetectArray + 1, VariableTypeString.Length - DetectArray - 2);
                try {
                    VariableItemCount = IntEvaluate(VarCount);
                } catch (Exception ex) {
                    DisplayError(ErrorType.Error, "Invalid (or no) array size: " + ex.Message, Filename, CurrentLineNumber);
                    return false;
                }

                VariableTypeString = VariableTypeString.Remove(DetectArray);
            }

            IType Lt;
            if (!TryGetTypeInformation(VariableTypeString, out Lt)) {
                DisplayError(ErrorType.Error, "Could not work out what type '" + VariableTypeString + "' is.", Filename, CurrentLineNumber);
                return false;
            }


            // So, we have a variable name and a size :)

            if (!DeclaringStruct) {
                bool WasLocal = AllLabelsLocal;

                bool IsInUnnamedModule = CurrentModule.Equals(RootModule) || CurrentModule.Equals(NoName) || CurrentModule.Equals(GlobalLabels);
                bool IsTempVar = false;
                if (Command == ".tvar" || Command == ".tempvar") {
                    if (IsInUnnamedModule) {
                        DisplayError(ErrorType.Error, "Temporary variables must be declared inside a named module.", Filename, CurrentLineNumber);
                        return false;
                    }
                    AllLabelsLocal = true;
                    IsTempVar = true;
                }

                LabelDetails NewLabel = null;

                bool SuccessfullyAdded = TryGetLabel(VariableName, out NewLabel, true);

                NewLabel.File = Filename;
                NewLabel.Line = CurrentLineNumber;
                NewLabel.IsVariable = true;
                NewLabel.Type = Lt;
                NewLabel.TempVariable = IsTempVar;
                NewLabel.ArrayCount = VariableItemCount;

                AllLabelsLocal = WasLocal;


                if (Lt.Type == DataType.Structure) {
                    ((StructureType)Lt).Structure.ExpandLabels(CurrentModule, NewLabel, VariableName, false, 0);
                }

            } else {
                // Adding something to a struct!
                if (Lt.Type == DataType.Structure && IsSelfReferential(Lt.OutputName, CurrentStruct)) {
                    DisplayError(ErrorType.Error, "Struct refers to itself, creating infinitely nested structure (use a pointer instead).", Filename, CurrentLineNumber);
                    return false;
                }
                LabelDetails L = new LabelDetails(Lt, VariableName, 0.0d, Filename, CurrentLineNumber, CurrentPage.Page, null, false);
                L.ArrayCount = VariableItemCount;
                CurrentStruct.Items.Add(L);
            }
            return true;
        }
    }
}
