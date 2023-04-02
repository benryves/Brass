using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {


        public static bool IsValidLabelName(string label, out string error) {

            if (label.IndexOfAny(OperatorCharsAsArray) != -1) {
                error = "Operator characters not permitted.";
                return false;
            }

            if (label.IndexOfAny(Parens) != -1) {
                int PA = label.IndexOf('(');
                int PB = label.IndexOf(')', Math.Max(0, PA));
                error = "Parentheses not permitted";
                if (PA != -1 && PB != -1) {
                    error += " - maybe macro '" + label.Remove(PA) + "' not defined";
                }                
                return false;
            }

            error = "Valid label name";
            return true;
        }

        public class LabelDetails : IComparable {


            public struct LabelReference {
                public readonly string Filename;
                public readonly int LineNumber;
                public LabelReference(string Filename, int LineNumber) {
                    this.Filename = Filename;
                    this.LineNumber = LineNumber;
                }
            }

            public int Value { get { return (int)RealValue; } }

            public double RealValue = 0.0d;

            public string File = "";
            public int Line = 0;
            public string Name = "";
            public uint Page = 0;
            public bool ExportMe = false;

            public bool IsUnmolested = true;

            //private Dictionary<string, LabelDetails> Owner = null;
            public readonly Module OwnerModule;

            public IType Type;

            public bool IsVariable;
			public bool IsVariableAllocated;
            public bool TempVariable = false;

            public int ArrayCount = 1;

            public int Size {
                get {
                    return Type.Size * ArrayCount;
                }
            }

            public LabelDetails(
                IType MyType,
                string Name,
                double Value,
                string File,
                int Line,
                uint Page,
                //Dictionary<string, LabelDetails> Owner,
                Module OwnerModule,
                bool IsVariable) {

                this.Type = MyType;
                this.Name = Name;
                this.RealValue = Value;
                this.File = File;
                this.Line = Line;
                this.Page = Page;
                this.ExportMe = Program.AmExportingLabels;
                //this.Owner = Owner;
                this.OwnerModule = OwnerModule;
                this.IsVariable = IsVariable;

                this.References = new List<LabelReference>();

            }

            public void Destroy() {
                OwnerModule.Labels.Remove(this.Name);
            }
            public override string ToString() {
                return this.Name + " = " + this.Value + ":" + this.Page;
            }


            public List<LabelReference> References;


            #region Full path expansion
            private string fullPath = "";
            public string FullPath {
                get {
                    if (fullPath == "") {
                        string Path = this.Name;
                        Module O = OwnerModule;
                        while (!(O == null || O == GlobalLabels || O == RootModule)) {
                            Path = O.Name + "." + Path;
                            O = O.Parent;
                        }
                        fullPath = Path;
                    }
                    return fullPath;
                }
            }
            #endregion

            #region Absolute address expansion
            public uint AbsoluteAddress {
                get {
                    return (uint)(Pages[this.Page].StartAddress + (int)this.Value);
                }
            }
            #endregion

            #region IComparable Members

            public int CompareTo(object obj) {
                return this.RealValue.CompareTo(((LabelDetails)obj).RealValue);
            }

            #endregion
        }

        public class Module {
            public Dictionary<string, Module> Modules = new Dictionary<string, Module>();
            public Dictionary<string, LabelDetails> Labels = new Dictionary<string, LabelDetails>(1024);
            public readonly string Name;
            public readonly Module Parent;
            public List<string> Using;

            public bool HalfCreated = false;

            public Module(string Name, Module Parent) {
                this.Name = Name;
                this.Parent = Parent;
                this.Using = new List<string>();
            }

            public override string ToString() {
                string FullName = this.Name;
                Module P = this.Parent;
                while (P != null) {
                    FullName = P.Name + "." + FullName;
                    P = P.Parent;
                }
                return FullName;
            }
        }


        public static Module CurrentModule = null;
        public static Module NoName = new Module("noname", null);
        public static Module RootModule = new Module("root", NoName);
        public static Module GlobalLabels = new Module("global", null);

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
        public static char[] InvalidChars = { '+', '-', '*', '/', ':', '(', ')', '<', '>', '&', '%', '^', '|' };

        public static bool AddNewLabel(string Name, double Value, bool ForceNewLabel, string SourceFile, int Line, Pass PassNumber, uint Page, bool Unmolested) {
            if (Name != "") {
                if (Name == "@") {
                    if (PassNumber == Pass.Labels) {
                        BookmarkLabels.Add(Value);
                    }
                    ++BookmarkIndex;
                } else if (Name.IndexOfAny(Reusables) != -1 && Name.Replace("+", "") == "" || Name.Replace("-", "") == "") {
                    int Mode = Name[0] == '+' ? 1 : 0;
                    ReusableLabelTracker FindReusableLabel;
                    if (PassNumber == Pass.Labels) {
                        if (!ReusableLabels[Mode].TryGetValue(Name.Length, out FindReusableLabel)) {
                            FindReusableLabel = new ReusableLabelTracker();
                            ReusableLabels[Mode].Add(Name.Length, FindReusableLabel);
                        }
                        FindReusableLabel.AllLabels.Add(new LabelDetails(null, Name, Value, SourceFile, Line, Page, null, false));
                    } else {
                        FindReusableLabel = ReusableLabels[Mode][Name.Length];
                    }
                    ++FindReusableLabel.Index;
                } else {
                    LabelDetails ToAdd = null;
                    if (TryGetLabel(Name, out ToAdd, true) && !ForceNewLabel) {
                        DisplayError(ErrorType.Error, "Label '" + Name + "' already defined.", SourceFile, Line);
                        return false;
                    } else {
                        if (Name.IndexOfAny(InvalidChars) != -1) {
                            DisplayError(ErrorType.Error, "Invalid characters in label name '" + Name + "'.", SourceFile, Line);
                            return false;
                        }
                        ToAdd.RealValue = Value;
                        //ToAdd.Name = FixLabelName(Name);
                        ToAdd.Line = Line;
                        ToAdd.File = SourceFile;
                        ToAdd.Page = Page;
                        ToAdd.IsUnmolested = Unmolested;
                        return true;
                    }
                }
                return true;
            } else {
                DisplayError(ErrorType.Error, "Invalid label name.", SourceFile, Line);
                return false;
            }
        }

        private static void TryToFollowModulePath(string PathItem, ref Module ActiveModule) {
            if (ActiveModule == null) {
                return;
            } else {
                if (ActiveModule.Equals(NoName)) {
                    if (PathItem != "noname") {
                        ActiveModule = null;
                    }
                } else {
                    if (PathItem.ToLower() == "parent") {
                        ActiveModule = ActiveModule.Parent;
                    } else if (!ActiveModule.Modules.TryGetValue(PathItem, out ActiveModule)) {
                        ActiveModule = null;
                    }
                }
            }


        }

        public static bool ForceLabelsGlobal = false;

        public static bool TryGetLabel(string Name, out LabelDetails Label, bool CreateIfNotExists) {

            Name = FixLabelName(Name);

            if (CreateIfNotExists) {
                return TryGetSingleLabel(Name, out Label, CreateIfNotExists);
            } else {
                bool CurrentProgress;
                CurrentProgress = TryGetSingleLabel(Name, out Label, CreateIfNotExists);
                if (CurrentProgress) return true;
                foreach (string s in CurrentModule.Using) {
                    CurrentProgress = TryGetSingleLabel(s + "." + Name, out Label, CreateIfNotExists);
                    if (CurrentProgress) return true;
                }
                return false;
            }
        }

        private static bool TryGetSingleLabel(string Name, out LabelDetails Label, bool CreateIfNotExists) {
            
            string FullName = Name;
            string[] LabelPath = Name.Split('.');
            Name = LabelPath[LabelPath.Length - 1];

            Module Absolute = RootModule;
            Module Relative = CurrentModule == NoName ? null : CurrentModule;
            Module Unnamed = NoName;

            bool ExplicitChecking = false;

            //if (LabelPath[0].ToLower() == "parent" || LabelPath[0].ToLower() == "this" || LabelPath[0].ToLower() == "root" || LabelPath[0].ToLower() == "global") {
            if (LabelPath[0].ToLower() == "this" || LabelPath[0].ToLower() == "root" || LabelPath[0].ToLower() == "global") {

                ExplicitChecking = true;
                string ParentModulePath = "";

                for (int i = 1; i < LabelPath.Length - 1; ++i) {
                    ParentModulePath += LabelPath[i] + ".";
                }
                ParentModulePath += Name;

                Module TraceBack = null;
                switch (LabelPath[0].ToLower()) {
                    /*case "parent":
                        Relative = null;
                        Unnamed = null;
                        TraceBack = CurrentModule.Parent;
                        break;*/
                    case "this":
                        Unnamed = null;
                        TraceBack = CurrentModule;
                        break;
                    case "root":
                        Relative = null;
                        TraceBack = null;
                        break;
                    case "global":
                        Relative = GlobalLabels;
                        Unnamed = null;
                        Absolute = null;
                        break;
                }
                while (!(TraceBack == null || TraceBack.Parent == null || TraceBack.Parent.Parent == null)) {
                    ParentModulePath = TraceBack.Name + "." + ParentModulePath;
                    TraceBack = TraceBack.Parent;
                }
                FullName = ParentModulePath;
                LabelPath = ParentModulePath.Split('.');
            }

            // Label is somewhere inside a module
            List<Module> ModulesToCheck = new List<Module>();

            if ((!ForceLabelsGlobal && (AllLabelsLocal || Name.StartsWith(CurrentLocalLabel))) || (ForceLabelsGlobal && !CreateIfNotExists)) {
                if (!ExplicitChecking && LabelPath.Length == 1) {
                    // It's just a label on it's own...
                    ModulesToCheck.Add(CurrentModule);
                } else {
                    // We have a path to follow.
                    // It will be an absolute path or/and a relative path.
                    // Relative paths take priority.

                    for (int i = 0; i < LabelPath.Length - 1; ++i) {
                        string SubPath = IsCaseSensitive ? LabelPath[i] : LabelPath[i].ToLower();
                        TryToFollowModulePath(SubPath, ref Absolute);
                        TryToFollowModulePath(SubPath, ref Relative);
                        TryToFollowModulePath(SubPath, ref Unnamed);
                    }
                    if (Relative != null) ModulesToCheck.Add(Relative);
                    if (Absolute != null) ModulesToCheck.Add(Absolute);
                    if (Unnamed != null) ModulesToCheck.Add(Unnamed);
                }
                //if (!ExplicitChecking) ModulesToCheck.AddRange(CurrentModule.Using);
            } else if (ForceLabelsGlobal) {
                ModulesToCheck.Add(GlobalLabels);
            }
            if (!ExplicitChecking && LabelPath.Length == 1) ModulesToCheck.Add(RootModule);

            bool IsGlobal = (ForceLabelsGlobal || (!AllLabelsLocal && !Name.StartsWith(CurrentLocalLabel)));

            if (!CreateIfNotExists || IsGlobal) ModulesToCheck.Add(GlobalLabels);

            foreach (Module m in ModulesToCheck) {
                if (m.Labels.TryGetValue(IsCaseSensitive ? Name : Name.ToLower(), out Label)) {
                    return true;
                }
            }

            if (!CreateIfNotExists) {
                Label = null;
            } else {
                Module ToAddTo = IsGlobal ? GlobalLabels : ModulesToCheck[0];
                Label = new LabelDetails(null, Name, 0, CurrentFilename, 0, CurrentPage.Page, ToAddTo, false);
                ToAddTo.Labels.Add(IsCaseSensitive ? Name : Name.ToLower(), Label);
                /*if (IsGlobal) {
                    GlobalLabels.Labels.Add(IsCaseSensitive ? Name : Name.ToLower(), Label);
                } else {
                    ModulesToCheck[0].Labels.Add(IsCaseSensitive ? Name : Name.ToLower(), Label);
                }*/
            }
            return false;
        }

        /// <summary>
        /// Adjust a label name
        /// </summary>
        /// <param name="Name">Label name</param>
        /// <returns>Fixed label name</returns>
        public static string FixLabelName(string Name) { return FixLabelName(Name, false); }
        public static string FixLabelName(string Name, bool IgnoreErrors) {
            //Name = IsCaseSensitive ? Name.Trim() : Name.Trim().ToLower();

            int NextBrace = Name.IndexOf('{');
            while (NextBrace != -1) {
                int CloseBrace = Name.IndexOf('}', NextBrace);
                if (CloseBrace == -1) return Name;
                string ThingToReplace = Name.Substring(NextBrace + 1, CloseBrace - NextBrace - 1);
                string Replacement;
                try {
                    Replacement = Evaluate(Name.Substring(NextBrace + 1, CloseBrace - NextBrace - 1)).ToString();
                } catch {
                    Replacement = ThingToReplace;
                }

                Name = Name.Remove(NextBrace) + Replacement + Name.Substring(CloseBrace + 1);
                NextBrace = Name.IndexOf('{', NextBrace);
            }

            NextBrace = Name.IndexOf('[');
            while (NextBrace != -1) {
                int CloseBrace = Name.IndexOf(']', NextBrace);
                if (CloseBrace == -1) return Name;
                string ThingToReplace = Name.Substring(NextBrace + 1, CloseBrace - NextBrace - 1);
                string Replacement;
                try {
                    Replacement = Evaluate(Name.Substring(NextBrace + 1, CloseBrace - NextBrace - 1)).ToString();
                } catch {
                    Replacement = ThingToReplace;
                }

                Name = Name.Remove(NextBrace + 1) + Replacement + Name.Substring(CloseBrace);
                NextBrace = Name.IndexOf('[', NextBrace + 1);
            }

            return Name.Trim();
        }

        private class ReusableLabelTracker {
            public int Index = 0;
            public List<LabelDetails> AllLabels = new List<LabelDetails>(1024);
        }

    }
}
