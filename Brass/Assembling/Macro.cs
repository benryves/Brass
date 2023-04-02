using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    /*public class Macro {
        public string Name = "";
        public string[] Args = { };
        public string Replacement = "";
    }*/

    partial class Program {

        public static char[] Seperators = { ' ', '\t', '+', '-', '/', '*', '\\', '&', '|', '!', '<', '>', '~', '¬', '?', '(', ')', ',', '\'', '"', ';', '{', '}', ':', '[', ']' };



        public static Dictionary<string, Macro> AvailableMacros;

        public class MacroReplacement : IComparable {
            // The signature specifies the format of the replacement.
            // For example, a macro defined as something(value,{hl}) will have a signature of { "*", "hl" }
            // The asterisk is a wildcard.
            public class SignatureItem {
                public readonly string MatchValue;
                public enum MatchType {
                    None,
                    Text,
                    Numeric,
                }
                public readonly MatchType Type;
                public SignatureItem(MatchType matchType, string matchValue) {
                    this.Type = matchType;
                    this.MatchValue = matchValue;
                }

            }
            public SignatureItem[] Signature = { };
            public List<string> Arguments = new List<string>();
            public string ReplacementString = "";

            public void GenerateSignature() {
                this.Signature = new SignatureItem[this.Arguments.Count];
                for (int i = 0; i < this.Arguments.Count; i++) {
                    this.Arguments[i] = this.Arguments[i].Trim();
                    if (this.Arguments[i].StartsWith("{") && this.Arguments[i].EndsWith("}")) {
                        string s = this.Arguments[i].Substring(1, this.Arguments[i].Length - 2).Trim();
                        if (s.StartsWith("(") && s.EndsWith(")")) {
                            this.Signature[i] = new SignatureItem(SignatureItem.MatchType.Numeric, s.Substring(1, s.Length - 2));
                        } else {
                            this.Signature[i] = new SignatureItem(SignatureItem.MatchType.Text, s.ToLower());
                        }
                    } else {
                        this.Signature[i] = new SignatureItem(SignatureItem.MatchType.None, "");
                    }
                }
            }
            public int CompareTo(object other) {
                MacroReplacement that = (MacroReplacement)other;
                if (that.Signature.Length == this.Signature.Length) {
                    int ThisWildcard = 0;
                    int ThatWildcard = 0;
                    for (int i = 0; i < this.Signature.Length; i++) {
                        if (this.Signature[i].Type == SignatureItem.MatchType.None) ++ThisWildcard;
                        if (that.Signature[i].Type == SignatureItem.MatchType.None) ++ThatWildcard;
                    }
                    return ThisWildcard.CompareTo(ThatWildcard);
                } else {
                    return this.Signature.Length.CompareTo(that.Signature.Length);
                }
            }


        }

        public class Macro {
            public List<MacroReplacement> Replacements = new List<MacroReplacement>();
            public string Name = "";

            public Macro(string Name) {
                this.Name = Name;
            }

            public override string ToString() {
                return Name;
            }
        }

        public static void AddMacroThroughDefinition(string MacroDefinition, string Filename, int LineNumber, bool SecondPass) {
            // Extract the macro name
            string MacroName = "";
            int i = 0;

            for (; i < MacroDefinition.Length; ++i) {
                if (MacroDefinition[i].ToString().IndexOfAny(Seperators) == -1) {
                    MacroName += MacroDefinition[i];
                } else {
                    break;
                }
            }

            // Extract the macro name
            MacroName = MacroName.Trim();
            if (!IsCaseSensitive) MacroName = MacroName.ToLower();
            if (MacroName == "") {
                throw new Exception("No macro name detected.");
            }

            // Get arguments
            string AfterName = MacroDefinition.Substring(i);
            string[] Args = GetArguments(ref AfterName);

            MacroReplacement Replacement = new MacroReplacement();

            Replacement.ReplacementString = AfterName;

            // Index arguments
            if (Args != null) {
                foreach (string S in Args) {
                    Replacement.Arguments.Add(S.Trim().ToLower());
                }
            }

            // Create macro signature
            Replacement.GenerateSignature();

            // Find the macro to add to, if it doesn't exist then create a fresh one.
            Macro OldMacro;
            if (!AvailableMacros.TryGetValue(MacroName, out OldMacro)) {
                // Add a new macro and add the replacement
                Macro AddNew = new Macro(MacroName);
                AddNew.Replacements.Add(Replacement);
                AvailableMacros.Add(MacroName, AddNew);
                LastMacro = AddNew;
            } else {
                // Add to an existing macro:
                List<MacroReplacement> ToRemove = new List<MacroReplacement>();
                foreach (MacroReplacement CheckReplacement in OldMacro.Replacements) {
                    if (CheckReplacement.Signature.Length == Replacement.Signature.Length) {
                        bool IdenticalSignature = true;
                        for (int j = 0; j < Replacement.Signature.Length; ++j) {
                            if (Replacement.Signature[j] != CheckReplacement.Signature[j]) {
                                IdenticalSignature = false;
                                break;
                            }
                        }
                        if (IdenticalSignature) ToRemove.Add(CheckReplacement);
                    }
                }
                // If we've got this far, then it should be possible to add a new replacement to the macro.
                foreach (MacroReplacement Remove in ToRemove) {
                    OldMacro.Replacements.Remove(Remove);
                }
                OldMacro.Replacements.Add(Replacement);
                OldMacro.Replacements.Sort();
                LastMacro = OldMacro;
            }
            LastReplacement = Replacement;

        }

        /// <summary>
        /// Step through every token in a string and do some magic on it.
        /// </summary>
        public static string TokenedReplacement(string StringToProcess, bool IsMacro, object ExtraData, string[] Replacements) {
            if (!IsMacro && Replacements.Length == 0) return StringToProcess;

            if (ExtraData != null) StringToProcess = StringToProcess.Replace("{#}", UniqueMacroIndex.ToString());

            string CheckToken = StringToProcess.Trim().ToLower();
            if (CheckToken.Length > 0) {
                CheckToken = CheckToken.Substring(1);
                string[] InvalidReplacers = { "define", "defcont", "ifdef", "ifndef", "elseifdef", "elseifndef" };
                foreach (string CheckValid in InvalidReplacers) {
                    if (CheckToken.StartsWith(CheckValid)) {
                        return StringToProcess;
                    }
                }
            }

            // Build up a string that is the final result
            StringBuilder ReturningValue = new StringBuilder(StringToProcess.Length * 2);

            MacroReplacement Replacement = IsMacro ? null : (MacroReplacement)ExtraData;

            // Flag to mention whether we are inside a "string" or not.
            bool InString = false;
            // Character used to denote a string (eg ' or ")
            char StringCharacter = ' ';
            // What was the token seperator on the last loop around? (To detect escaped strings)
            char LastSeperator = ' ';

            // Hunt for the next seperator
            int NextSeperator = StringToProcess.IndexOfAny(Seperators);
            while (NextSeperator != -1) {

                // Get the token and seperator from the string
                string Token = StringToProcess.Remove(NextSeperator);
                char Seperator = StringToProcess[NextSeperator];

                // Chop off the start of the string, as we now have them in our token/seperator pair.
                StringToProcess = StringToProcess.Substring(NextSeperator + 1);

                // Expand the macro:

                if (IsMacro) {
                    string FullRestOfLine = Seperator + StringToProcess;
                    string CheckChanged = FullRestOfLine;
                    ReturningValue.Append(InString ? Token : ApplyMacro(Token, ref FullRestOfLine));
                    if (FullRestOfLine != CheckChanged) {
                        StringToProcess = FullRestOfLine;
                    } else {
                        if (Seperator == ';' && !InString) {
                            return ReturningValue.ToString();
                        } else {
                            ReturningValue.Append(Seperator);
                        }
                    }
                } else {
                    for (int i = 0; i < Replacement.Arguments.Count; i++) {
                        if (Token.ToLower() == (string)(Replacement.Arguments[i])) Token = Replacements[i];
                    }
                    ReturningValue.Append(Token);
                    ReturningValue.Append(Seperator);
                }


                // Check to see if we are now in or are now leaving a string:
                if (Seperator == '"' || Seperator == '\'') {
                    if (InString == false) {
                        InString = true;
                        StringCharacter = Seperator;
                    } else if (LastSeperator != '\\' && Seperator == StringCharacter) {
                        InString = false;
                    }
                }

                // Save the current seperator for the next loop, then move along to the next token
                LastSeperator = Seperator;
                NextSeperator = StringToProcess.IndexOfAny(Seperators);
            }

            // Append the last chunk of the string, then return.
            string Argless = "";    // Dud string
            if (IsMacro) {
                ReturningValue.Append(InString ? StringToProcess : ApplyMacro(StringToProcess, ref Argless));
            } else {
                for (int i = 0; i < Replacement.Arguments.Count; i++) {
                    if (StringToProcess.ToLower() == (string)(Replacement.Arguments[i])) StringToProcess = Replacements[i];
                }
                ReturningValue.Append(StringToProcess);
            }
            return ReturningValue.ToString();
        }

        /// <summary>
        /// Expand all macros in a line of assembly code.
        /// </summary>
        /// <param name="StringToProcess">The line of source code that contains the macros we need to expand.</param>
        /// <returns>A line of assembly code, no longer en-macroed.</returns>

        public static string PreprocessMacros(string StringToProcess) {
            string[] RealStringToProcess = SafeSplit(StringToProcess, ';');
            return TokenedReplacement(RealStringToProcess[0], true, null, null);
        }

        /// <summary>
        /// Checks to see if the token is a valid macro (with or without arguments), expands it, then returns the full version.
        /// </summary>
        /// <param name="Token">The token to check</param>
        /// <param name="RestOfLine">The rest of the source line (to check for arguments).</param>
        /// <returns>The line of code, macros fully expanded.</returns>
        public static string ApplyMacro(string Token, ref string RestOfLine) {
            // Try and find a macro
            Macro FoundMacro;

            if (!AvailableMacros.TryGetValue(IsCaseSensitive ? Token : Token.ToLower(), out FoundMacro)) {
                return Token; // Don't do anything, I can't find a valid macro to fit this token.
            } else {
                ++UniqueMacroIndex;
                // Cast the macro to a proper Macro class.
                string[] Arguments = { };
                // Are we passing any arguments?

                int ArgumentListEnd = 0;
                if (RestOfLine.Trim().StartsWith("(")) {

                    int ArgumentListStart = GetSafeIndexOf(RestOfLine, '(');

                    int ParensDepth = 1;
                    int ReadDepth = ArgumentListStart + 1;

                    while (ParensDepth > 0) {
                        int GetC = GetSafeIndexOf(RestOfLine, ')', ReadDepth);
                        int GetO = GetSafeIndexOf(RestOfLine, '(', ReadDepth);
                        if (GetC != -1 && (GetC < GetO || GetO == -1)) {
                            --ParensDepth;
                            ReadDepth = GetC + 1;
                        } else if (GetO != -1) {
                            ++ParensDepth;
                            ReadDepth = GetO + 1;
                        } else {
                            break;
                        }
                    }

                    ArgumentListEnd = ReadDepth - 1;

                    if (ArgumentListEnd != -1) {
                        Arguments = SafeSplit(RestOfLine.Substring(ArgumentListStart + 1, ArgumentListEnd - ArgumentListStart - 1), ',');
                        /*for (int i = 0; i < Arguments.Length; ++i) {
                            string Return;
                            try {
                                Return = Evaluate(TokenedReplacement(Arguments[i].Trim(), true, null, null), true).ToString();
                            } catch {
                                Return = Arguments[i];
                            }
                            Arguments[i] = Return;
                        }*/
                    }
                }
                MacroReplacement Replacement = null;
                foreach (MacroReplacement HuntReplacement in FoundMacro.Replacements) {
                    if (HuntReplacement.Signature.Length == Arguments.Length) {
                        bool IsTheRightReplacement = true;
                        for (int i = 0; i < HuntReplacement.Signature.Length; i++) {
                            //if (HuntReplacement.Signature[i] != "*" && HuntReplacement.Signature[i] != Arguments[i].Trim().ToLower()) {

                            if (HuntReplacement.Signature[i].Type !=  MacroReplacement.SignatureItem.MatchType.None) {
                                string TryCompare = Arguments[i].Trim();
                                if (HuntReplacement.Signature[i].Type == MacroReplacement.SignatureItem.MatchType.Numeric) {
                                    try {
                                        TryCompare = Evaluate(TokenedReplacement(Arguments[i].Trim(), true, null, null), true).ToString(Program.InvariantCulture);
                                    } catch {
                                        TryCompare = TryCompare.ToLower();
                                    }
                                } else {
                                    TryCompare = TryCompare.ToLower();
                                }
                                if (HuntReplacement.Signature[i].MatchValue != TryCompare) {
                                    IsTheRightReplacement = false;
                                }
                            }
                            if (!IsTheRightReplacement) break;
                        }
                        if (IsTheRightReplacement) {
                            Replacement = HuntReplacement;
                            break;
                        }
                    }
                }

                if (Replacement == null) {
                    // No macro signatures matched
                    return Token;
                } else {
                    if (Arguments.Length > 0) {
                        RestOfLine = RestOfLine.Substring(ArgumentListEnd + 1);
                    }
                    if (Replacement.ReplacementString == "" && Arguments.Length == 0) {
                        return FoundMacro.Name;
                    } else {
                        return TokenedReplacement(TokenedReplacement(Replacement.ReplacementString, false, Replacement, Arguments), true, null, null);
                    }
                    //return TokenedReplacement(Replacement.ReplacementString, false, Replacement, Arguments);
                }
            }
        }

        public static string[] GetArguments(ref string ArgumentString) {
            int ArgumentListEnd = 0;
            if (ArgumentString.StartsWith("(")) {

                int ArgumentListStart = GetSafeIndexOf(ArgumentString, '(');

                int ParensDepth = 1;
                int ReadDepth = ArgumentListStart + 1;

                while (ParensDepth > 0) {
                    int GetC = GetSafeIndexOf(ArgumentString, ')', ReadDepth);
                    int GetO = GetSafeIndexOf(ArgumentString, '(', ReadDepth);
                    if (GetC != -1 && (GetC < GetO || GetO == -1)) {
                        --ParensDepth;
                        ReadDepth = GetC + 1;
                    } else if (GetO != -1) {
                        ++ParensDepth;
                        ReadDepth = GetO + 1;
                    } else {
                        break;
                    }
                }

                ArgumentListEnd = ReadDepth - 1;

                if (ArgumentListEnd != -1) {


                    string[] Return = SafeSplit(ArgumentString.Substring(ArgumentListStart + 1, ArgumentListEnd - ArgumentListStart - 1), ',');
                    ArgumentString = ArgumentString.Substring(ArgumentListEnd + 1);
                    return Return;
                }
            }
            return null;
        }
    }

}
