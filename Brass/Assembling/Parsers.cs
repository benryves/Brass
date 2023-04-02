/* BRASS Z80 ASSEMBLER
 * -------------------
 * PARSERS.CS - GENERAL TEXT PARSING ROUTINES
 */

using System;
using System.Collections;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

namespace Brass {
    public partial class Program {

        public static CultureInfo InvariantCulture = new CultureInfo("");

        /// <summary>
        /// Escape a string into an HTML (XML) compliant form.
        /// </summary>
        /// <param name="Data">String to escape.</param>
        /// <returns>Escaped string.</returns>
        public static string EscapeHTML(string Data) {
            string R = Data.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            StringBuilder Escaped = new StringBuilder(R.Length * 3);
            foreach (char c in R) {
                if (c < ' ' || c > '}') {
                    Escaped.Append("&#");
                    Escaped.Append((int)c);
                    Escaped.Append(";");
                } else {
                    Escaped.Append(c);
                }
            }

            return Escaped.ToString();
        }

        /// <summary>
        /// Check to see if a label name is valid or not.
        /// </summary>
        /// <param name="LabelName">Label name to check</param>
        /// <returns>True if the label is a valid name, false if it is invalid.</returns>
        public static bool CheckLabelName(string LabelName) {
            string ValidCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_.";
            foreach (char C in LabelName.ToLower()) {
                if (ValidCharacters.IndexOf(C) == -1) return false;
            }
            return true;
        }

        /// <summary>
        /// Converts a number from a string into an integer based on special characters.
        /// </summary>
        /// <param name="Number">The string to convert.</param>
        /// <returns>The number, as an int (or throws an exception).</returns>
        public static bool ConvertNumber(string Number, out double Result) { return ConvertNumber(Number, false, out Result); }
        public static bool ConvertNumber(string Number, bool CanGoWrong, out double Result) {
            double MakeNegative = 1.0d;

            if (Number.StartsWith("¬")) {
                MakeNegative = -1.0d;
                Number = Number.Substring(1);
            }
            Number = Number.Trim();

            if (Number == "" && !CanGoWrong) {
                Result = 0;
                return true;
            }

            if (Number == "$") {
                Result = CurrentPage.ProgramCounter + RelocationOffset;
                return true;
            }

            if (Number == "#") {
                Result = CurrentPage.Page;
                return true;
            }

            bool EscapeAscii = Number[0] == '"';
            if (Number.Length > 1 && Number[0] == Number[Number.Length - 1] && (Number[0] == '\'' || Number[0] == '"')) {
                Number = UnescapeString(Number.Substring(1, Number.Length - 2));
                Result = 0d;
                AsciiChar A = new AsciiChar();                
                for (int i = Number.Length - 1; i >= 0; --i) {
                    Result *= 256d;
                    Result += EscapeAscii ? A.Cast((double)Number[i]) : (double)Number[i];
                }
                return true;
            }

            try {
                if (Number.StartsWith("$") || Number.ToLower().EndsWith("h")) {
                    Result = Convert.ToInt32(Number.ToLower().Replace("$", "").Replace("h", ""), 16) * MakeNegative;
                } else if (Number.StartsWith("%") || Number.ToLower().EndsWith("b")) {
                    Result = Convert.ToInt32(Number.ToLower().Replace("%", "").Replace("b", ""), 2) * MakeNegative;
                } else if (Number.StartsWith("@") || Number.ToLower().EndsWith("o")) {
                    Result = Convert.ToInt32(Number.ToLower().Replace("@", "").Replace("o", ""), 8) * MakeNegative;
                } else {
                    Result = double.Parse(Number.ToLower().Replace("d", ""), InvariantCulture) * MakeNegative;
                }
                return true;
            } catch {
                Result = 0;
                return false;
            }
        }

        /// <summary>
        /// Check to see if a string matches a wildcard pattern.
        /// </summary>
        /// <param name="WildcardPattern">Pattern to compare against (eg ld hl,*)</param>
        /// <param name="Test">String to test (eg ld hl,_hello)</param>
        /// <returns>True if matched, False if not matched.</returns>
        public static bool MatchWildcards(string WildcardPattern, string Test, ref List<string> Match) {

            string Original = Test.Trim();
            string Working = Original.ToLower() + "\r";
            Original += "\r";

            /*if (WildcardPattern == "\"\"" && Working == "") {
                Match.Clear();
                return true;
            }*/

            // Adding \r to the end is IMPORTANT.
            // Otherwise, a,(*) vs a,((3+1)*2) would NOT match.
            WildcardPattern += "\r";

            //
            string TokenToMatch = "";

            Match.Clear();

            for (int i = 0; i < WildcardPattern.Length; ++i) {
                if (WildcardPattern[i] == '*') {
                    if (!Working.StartsWith(TokenToMatch)) return false;
                    if (i == WildcardPattern.Length - 2) {
                        // Got an argument!
                        Match.Add(Original.Substring(TokenToMatch.Length, Original.Length - TokenToMatch.Length - 1));
                        return true;
                    }
                    string EndOfAsteriskToMatch = WildcardPattern.Substring(i + 1);
                    int AsteriskMatched = EndOfAsteriskToMatch.IndexOf('*');
                    if (AsteriskMatched != -1) {
                        EndOfAsteriskToMatch = EndOfAsteriskToMatch.Remove(AsteriskMatched);
                    }
                    int JumpToNextBit = Working.IndexOf(EndOfAsteriskToMatch);
                    if (JumpToNextBit == -1) return false;
                    string MatchedAsterisk = Original.Remove(JumpToNextBit).Substring(TokenToMatch.Length);
                    Match.Add(MatchedAsterisk);
                    int DetectBuggeredParens = MatchedAsterisk.IndexOf(")");
                    if (DetectBuggeredParens != -1) {
                        int FindMatchingParens = MatchedAsterisk.IndexOf("(");
                        if (FindMatchingParens == -1 || FindMatchingParens > DetectBuggeredParens) return false;
                    }
                    Working = Working.Substring(JumpToNextBit);
                    Original = Original.Substring(JumpToNextBit);
                    TokenToMatch = "";
                } else {
                    TokenToMatch += WildcardPattern[i];
                }
                if (i == WildcardPattern.Length - 1) {
                    return (TokenToMatch.ToLower() == Working);
                }
            }
            return false;
        }

        /// <summary>
        /// Extract the arguments from a string based on a wildcard pattern.
        /// </summary>
        /// <param name="WildcardPattern">Pattern to use to perform the extraction on.</param>
        /// <param name="Source">Line of code to extract the arguments from.</param>
        /// <returns>An arraylist where each element is a match on the wildcard.</returns>
        public static List<string> ExtractArguments(string WildcardPattern, string Source) {

            List<string> Returner = new List<string>();

            if (WildcardPattern.IndexOf('*') == -1) return Returner;

            Source = SafeStripWhitespace(Source) + "\r";

            if (WildcardPattern == "\"\"" && Source == "") return Returner;

            WildcardPattern += "\r";

            for (int i = 0; i < WildcardPattern.Length; ++i) {
                if (WildcardPattern[i] == '*') {
                    // What's the next bit?
                    if (i == WildcardPattern.Length - 1) {
                        // Rest of the string:
                        Returner.Add(Source);
                        return Returner;
                    }
                    string NextBit = WildcardPattern.Substring(i + 1);
                    int NextBitIndex = NextBit.IndexOf('*');
                    if (NextBitIndex != -1) {
                        NextBit = NextBit.Remove(NextBitIndex);
                    }
                    int EndOfString = Source.ToLower().IndexOf(NextBit);
                    Returner.Add(Source.Remove(EndOfString));
                    Source = Source.Substring(EndOfString);
                } else {
                    // Remove the first character
                    Source = Source.Substring(1);
                }
                if (i == WildcardPattern.Length - 1) {
                    return Returner;
                }
            }
            return Returner;
        }

        /// <summary>
        /// Evaluates an expression into an integer.
        /// </summary>
        /// <param name="Argument">Expression to convert.</param>
        /// <returns>An integer result of the evaluation.</returns>
        public static string[] Operators = { "||", "&&", "|", "^", "&", "!=", "==", ">=", "<=", ">>", "<<", ">", "<", "+", "-", "%", "/", "*", "~", "!" };//  Removed "?"
        public static string OperatorChars = "<>|&^=!+-*/%~";
        public static char[] OperatorCharsAsArray = OperatorChars.ToCharArray();
        public static char[] Parens = { '(', ')' };

        public static int IntEvaluate(string Argument) {
            double RetVal = Evaluate(Argument, false);
            if (StrictMode && (double)(int)RetVal != RetVal) throw new Exception("Expected integer value, not double!");
            return (int)RetVal;
        }

        public static uint UintEvaluate(string Argument) {
            double RetVal = Evaluate(Argument, false);
            if (StrictMode && (double)(uint)RetVal != RetVal) throw new Exception("Expected integer value, not double!");
            return (uint)RetVal;
        }

        public static double Evaluate(string Argument) { return Evaluate(Argument, false); }
        public static double Evaluate(string Argument, bool CanGoWrong) {
            //Console.WriteLine("Evaluating: " + Argument);

            // Deal with character constants:

            Argument = SafeStripWhitespace(Argument);

            // Deal with reusable labels
            int ReplaceReusableLabels = Argument.IndexOf('{');
            while (ReplaceReusableLabels != -1) {
                int EndOfReusableLabel = Argument.IndexOf('}');
                if (EndOfReusableLabel == -1) throw new Exception("Badly formed label in '" + Argument + "'.");

                string Before = Argument.Remove(ReplaceReusableLabels);
                string After = Argument.Substring(EndOfReusableLabel + 1);

                // Now we need to work out what the reusable label actually is.
                string RLabel = Argument.Substring(ReplaceReusableLabels, EndOfReusableLabel - ReplaceReusableLabels).Substring(1).Trim();
                if (RLabel == "") throw new Exception("Label not specified.");

                if (RLabel.IndexOf('@') != -1) {
                    int Offset = 1;
                    if (RLabel != "@") {
                        try {
                            Offset = (int)Evaluate(RLabel.Remove(RLabel.IndexOf('@'), 1));
                        } catch (Exception ex) {
                            throw new Exception("@-style reusable label offset malformed - " + ex.Message);
                        }
                    }
                    if (Offset == 0) throw new Exception("@-style reusable labels cannot have an offset of zero.");

                    if (Offset > 0) Offset--;
                    int Search = BookmarkIndex + Offset;

                    if (Search >= 0 && Search <= BookmarkLabels.Count) {
                        Argument = Before + BookmarkLabels[Search] + After;
                    } else {
                        Argument = Before + Evaluate(RLabel) + After;
                    }
                } else {

                    if (RLabel.Length != 0 && (RLabel.Replace("+", "") == "" || RLabel.Replace("-", "") == "")) {
                        int Mode = RLabel[0] == '+' ? 1 : 0;
                        ReusableLabelTracker FindLabel;
                        if (!ReusableLabels[Mode].TryGetValue(RLabel.Length, out FindLabel)) throw new Exception("Reusable label " + RLabel + " not found.");
                        int Search = FindLabel.Index;
                        if (Mode == 0) --Search;
                        if (Search >= 0 && Search <= FindLabel.AllLabels.Count) {
                            Argument = Before + FindLabel.AllLabels[Search].Value + After;
                        } else {
                            Argument = Before + Evaluate(RLabel) + After;
                        }
                    } else {
                        break;
                    }
                }
                ReplaceReusableLabels = Argument.IndexOf('{');
            }


            // Deal with double negatives.
            Argument = Argument.Replace("--", "+");


            // Deal with string constants
            StringBuilder NewArgument = new StringBuilder(Argument.Length);
            StringBuilder StringToStrip;
            for (int i = 0; i < Argument.Length; ++i) {
                if (Argument[i] == '"' || Argument[i] == '\'') {
                    StringToStrip = new StringBuilder();

                    char Start = Argument[i++];
                    StringToStrip.Append(Start);
                    bool Escaping = false;
                    for (; i < Argument.Length && (Argument[i] != Start || Escaping); ++i) {
                        Escaping = (Argument[i] == '\\');
                        StringToStrip.Append(Argument[i]);
                    }
                    if (i == Argument.Length) throw new Exception("Unterminated string constant " + StringToStrip.ToString().Trim() + "_");
                    StringToStrip.Append(Start);
                    double StringVal = 0;
                    if (!ConvertNumber(StringToStrip.ToString(), out StringVal)) {
                        throw new Exception("Invalid string constant '" + StringToStrip.ToString().Trim() + "'.");
                    }
                    NewArgument.Append("(" + StringVal.ToString(InvariantCulture) + ")");
                } else {
                    NewArgument.Append(Argument[i]);
                }
            }
            Argument = NewArgument.ToString();

            // Deal with parentheses:
            if (Argument.IndexOfAny(Parens) != -1) {
                // We now need to split apart ( )
                int ParenIndex = Argument.IndexOf('(');
                if (ParenIndex == -1) throw new Exception("Mismatched parentheses.");
                string FunctionName = "";
                if (ParenIndex > 0 && OperatorChars.IndexOf(Argument[ParenIndex - 1]) == -1 && Argument[ParenIndex - 1] != ':') { // '?' to stop ternaries from breaking
                    int StartParenIndex = ParenIndex - 1;

                    while (StartParenIndex >= 0 && OperatorChars.IndexOf(Argument[StartParenIndex]) == -1) {
                        FunctionName = Argument[StartParenIndex] + FunctionName;
                        --StartParenIndex;
                    }
                }
                string BeforeString = Argument.Remove(ParenIndex - FunctionName.Length);
                ++ParenIndex;
                int StartIndex = ParenIndex;
                int Count = 1;
                while (ParenIndex < Argument.Length) {
                    if (Argument[ParenIndex] == '(') ++Count;
                    if (Argument[ParenIndex] == ')') --Count;
                    ++ParenIndex;
                    if (Count == 0) break;
                }
                if (Count != 0) throw new Exception("Mismatched parentheses.");
                string AfterString = Argument.Substring(ParenIndex);

                string MiddleString = Argument.Substring(StartIndex, ParenIndex - StartIndex - 1);
                return Evaluate(BeforeString + ExecuteFunction(FunctionName, MiddleString) + AfterString);
            }

            foreach (string Operator in Operators) {
                Argument = Argument.Replace(Operator + "-", Operator + "¬");
            }

            // Now, one important thing to do would be to strip out any % binary numbers.

            int HopToNextPercent = Argument.IndexOf('%');
            while (HopToNextPercent != -1) {
                bool IsBinaryNumber = false;
                if (HopToNextPercent == 0) {
                    IsBinaryNumber = true;
                } else {
                    foreach (string Op in Operators) {
                        if (HopToNextPercent >= Op.Length) {
                            string PossibleOp = Argument.Substring(HopToNextPercent - Op.Length, Op.Length);
                            if (Op == PossibleOp) {
                                IsBinaryNumber = true;
                                break; // It's preceded by an operator, so it MUST be a binary number.
                            }
                        }
                    }
                }

                if (IsBinaryNumber) {
                    int StartOfOldNumber = HopToNextPercent;
                    string OldBinaryNumber = "";
                    ++HopToNextPercent;
                    while (HopToNextPercent < Argument.Length && (Argument[HopToNextPercent] == '0' || Argument[HopToNextPercent] == '1')) {
                        OldBinaryNumber += Argument[HopToNextPercent];
                        ++HopToNextPercent;
                    }
                    Argument = Argument.Remove(StartOfOldNumber) + OldBinaryNumber + "b" + Argument.Substring(HopToNextPercent);
                }

                if (HopToNextPercent < Argument.Length - 1) {
                    HopToNextPercent = Argument.IndexOf('%', HopToNextPercent + 1);
                } else {
                    HopToNextPercent = -1;
                }
            }





            // Label?

            if (Argument.Length != 0) {
                string JustLabel = Argument[0] == ':' ? Argument.Substring(1) : Argument;

                LabelDetails GetLabel = null;
                if (TryGetLabel(JustLabel, out GetLabel, false)) {
                    LabelDetails.LabelReference Ref = new LabelDetails.LabelReference(CurrentFilename, CurrentLineNumber);
                    if (!GetLabel.References.Contains(Ref)) GetLabel.References.Add(Ref);
                    return Argument[0] == ':' ? GetLabel.Page : GetLabel.RealValue;// +(GetLabel.IsVariable ? Ix * GetLabel.MyType.Size : 0.0d);
                }


            }

            // Ternaries!

            int IndexOfOperator = Argument.IndexOf("?");
            if (IndexOfOperator != -1) {

                string TernBeforeOperator = Argument.Remove(IndexOfOperator);
                string TernAfterOperator = Argument.Substring(IndexOfOperator + 1);
                int Colon = GetSafeIndexOf(TernAfterOperator, ':');
                if (Colon == -1) throw new Exception("Ternaries must be followed by two expressions around a colon ('condition ? true : false')");

                string[] Results = new string[] { TernAfterOperator.Remove(Colon), TernAfterOperator.Substring(Colon + 1) };
                int Check = IntEvaluate(TernBeforeOperator);
                double Result = Evaluate(Results[(Check != 0) ? 0 : 1]);
                return Result;
            }

            foreach (String Operator in Operators) {
                IndexOfOperator = Argument.LastIndexOf(Operator);

                //int IndexOfOperator = Argument.IndexOf(Operator);

                if (IndexOfOperator != -1) {
                    // Found an operator.
                    string BeforeOperator = Argument.Remove(IndexOfOperator);
                    string AfterOperator = Argument.Substring(IndexOfOperator + Operator.Length);
                    if (AfterOperator == "") throw new Exception("'" + Operator + "' operator expects two arguments.");
                    switch (Operator) {
                        case "+":
                            return Evaluate(BeforeOperator) + Evaluate(AfterOperator);
                        case "-":
                            return Evaluate(BeforeOperator) - Evaluate(AfterOperator);
                        case "*":
                            return Evaluate(BeforeOperator) * Evaluate(AfterOperator);
                        case "/":
                            return Evaluate(BeforeOperator) / Evaluate(AfterOperator);
                        case "|":
                            return IntEvaluate(BeforeOperator) | IntEvaluate(AfterOperator);
                        case "&":
                            return IntEvaluate(BeforeOperator) & IntEvaluate(AfterOperator);
                        case "^":
                            return IntEvaluate(BeforeOperator) ^ IntEvaluate(AfterOperator);
                        case ">>":
                            return IntEvaluate(BeforeOperator) >> IntEvaluate(AfterOperator);
                        case "<<":
                            return IntEvaluate(BeforeOperator) << IntEvaluate(AfterOperator);
                        case "%":
                            return Evaluate(BeforeOperator) % Evaluate(AfterOperator);
                        case "==":
                            return (Evaluate(BeforeOperator) == Evaluate(AfterOperator)) ? 1 : 0;
                        case "!=":
                            return (Evaluate(BeforeOperator) != Evaluate(AfterOperator)) ? 1 : 0;
                        case ">=":
                            return (Evaluate(BeforeOperator) >= Evaluate(AfterOperator)) ? 1 : 0;
                        case "<=":
                            return (Evaluate(BeforeOperator) <= Evaluate(AfterOperator)) ? 1 : 0;
                        case ">":
                            return (Evaluate(BeforeOperator) > Evaluate(AfterOperator)) ? 1 : 0;
                        case "<":
                            return (Evaluate(BeforeOperator) < Evaluate(AfterOperator)) ? 1 : 0;
                        case "&&":
                            return ((Evaluate(BeforeOperator) != 0) && (Evaluate(AfterOperator) != 0)) ? 1 : 0;
                        case "||":
                            return ((Evaluate(BeforeOperator) != 0) || (Evaluate(AfterOperator) != 0)) ? 1 : 0;
                        case "!":
                            return (Evaluate(BeforeOperator + (Evaluate(AfterOperator) != 0 ? 0 : 1))) != 0 ? 1 : 0;
                        case "~":
                            return Evaluate(BeforeOperator + ((int)~IntEvaluate(AfterOperator)).ToString());
                        

                        default:
                            throw new Exception("Operator '" + Operator + "' not valid.");
                    }
                }
            }



            if (CanGoWrong) {
                if (Argument == "") {
                    throw new Exception("Invalid expression");
                }
                double Result = 0;
                if (ConvertNumber(Argument, true, out Result)) {
                    return Result;
                } else {
                    throw new Exception("Invalid expression");
                }
            } else {
                double Result = 0;
                if (ConvertNumber(Argument, out Result)) {
                    return Result;
                } else {
                    throw new Exception("Invalid number");
                }
            }

        }

        /// <summary>
        /// Unescape a string using TASM's string escaped string format.
        /// </summary>
        /// <param name="StringToUnescape">String to unescape.</param>
        /// <returns>The original, unescaped string.</returns>
        public static string UnescapeString(string StringToUnescape) {
            /*List<int> RealBackSlashes = new List<int>();
            int NextEscapedBackslash = StringToUnescape.IndexOf(@"\\");
            while (NextEscapedBackslash != -1) {
                RealBackSlashes.Add(NextEscapedBackslash);
                return (UnescapeString(StringToUnescape.Remove(NextEscapedBackslash)) + "\\" + UnescapeString(StringToUnescape.Substring(NextEscapedBackslash + 2)));
            }
            StringToUnescape = StringToUnescape.Replace("\\\"", "\"");
            StringToUnescape = StringToUnescape.Replace(@"\n", "\n");
            StringToUnescape = StringToUnescape.Replace(@"\r", "\r");
            StringToUnescape = StringToUnescape.Replace(@"\b", "\b");
            StringToUnescape = StringToUnescape.Replace(@"\t", "\t");
            StringToUnescape = StringToUnescape.Replace(@"\f", "\f");
            return StringToUnescape;*/
            StringBuilder Unescaped = new StringBuilder(StringToUnescape.Length);
            bool WasEscaped = false;
            foreach (char c in StringToUnescape) {
                if (WasEscaped) {
                    WasEscaped = false;
                    switch (c) {
                        case '0': Unescaped.Append('\0'); break;
                        case '\\': Unescaped.Append('\\'); break;
                        case 'n': Unescaped.Append('\n'); break;
                        case 'r': Unescaped.Append('\r'); break;
                        case 'b': Unescaped.Append('\b'); break;
                        case 't': Unescaped.Append('\t'); break;
                        case 'f': Unescaped.Append('\f'); break;
                        case '"': Unescaped.Append('"'); break;
                        case '\'': Unescaped.Append('\''); break;
                        default: throw new Exception("Unrecognised escape sequence \\" + c + ".");
                    }
                } else {
                    if (c == '\\') {
                        WasEscaped = true;
                    } else {
                        Unescaped.Append(c);
                    }
                }
                
            }
            return Unescaped.ToString();
        }

        public static string EscapeString(string StringToEscape) {
            if (StringToEscape == null || StringToEscape == "") return "";
            StringToEscape = StringToEscape.Replace(@"\", @"\\");
            StringToEscape = StringToEscape.Replace("\r", @"\r");
            StringToEscape = StringToEscape.Replace("\b", @"\b");
            StringToEscape = StringToEscape.Replace("\t", @"\t");
            StringToEscape = StringToEscape.Replace("\f", @"\f");
            return StringToEscape;

        }


        /// <summary>
        /// Split a string up into the individual tokens.
        /// </summary>
        /// <param name="LineToSplit">Line to split up.</param>
        /// <returns>String array of individual tokens.</returns>
        public static string[] SplitIntoTokens(string LineToSplit) {
            List<string> A = new List<string>();
            string CurrentToken = "";
            bool AmInString = false;
            char StringMarker = ' ';
            for (int i = 0; i < LineToSplit.Length; ++i) {
                if (LineToSplit[i] == '\'' || LineToSplit[i] == '"') {
                    if (!AmInString) {
                        AmInString = true;
                    } else if (AmInString && LineToSplit[i] == StringMarker && i > 0 && LineToSplit[i - 1] != '\\') {
                        AmInString = false;
                    }

                }
                if ("()+*/-,|& <>¬~^\\?\t".IndexOf(LineToSplit[i]) != -1 && !AmInString) {
                    A.Add(CurrentToken.Trim());
                    A.Add(LineToSplit[i].ToString());
                    CurrentToken = "";
                } else {
                    CurrentToken += LineToSplit[i].ToString();
                }

            }
            if (CurrentToken != "") A.Add(CurrentToken);
            return A.ToArray();
        }

        /// <summary>
        /// Strip all whitespace and comments from a string without butchering strings/character constants.
        /// </summary>
        /// <param name="StringToStrip">The string to strip whitespace from.</param>
        /// <returns>A string with the whitespace stripped from it.</returns>
        public static string SafeStripWhitespace(string StringToStrip) { return SafeStripWhitespace(StringToStrip, false); }
        public static string SafeStripWhitespace(string StringToStrip, bool DoNotStripWhitespace) {
            string Return = "";
            bool InString = false;
            char StringChar = ' ';
            bool LastCharWasWhitespace = false;
            StringToStrip = StringToStrip.Trim();
            for (int i = 0; i < StringToStrip.Length; ++i) {
                if (StringToStrip[i] == ';' && !InString) break;

                if (StringToStrip[i] == '\'' || StringToStrip[i] == '"') {

                    if (InString == false) {
                        if (StringToStrip[i] == '\'') {
                            string CheckNotChar = StringToStrip + "   ";
                            if (CheckNotChar[i + 2] == '\'' || (CheckNotChar[i + 2] == '\\' && CheckNotChar[i + 3] == '\'')) {
                                InString = true;
                                StringChar = StringToStrip[i];
                            }
                        } else {
                            InString = true;
                            StringChar = StringToStrip[i];
                        }
                    } else {
                        if (StringToStrip[i] == StringChar && i > 0 && StringToStrip[i - 1] != '\\') {
                            InString = false;
                        }
                    }
                }
                if (InString || (DoNotStripWhitespace && !LastCharWasWhitespace) || (StringToStrip[i] != ' ' && StringToStrip[i] != '\t' && StringToStrip[i] != '\r')) Return += StringToStrip[i];
                LastCharWasWhitespace = (StringToStrip[i] == ' ' || StringToStrip[i] == '\t' || StringToStrip[i] == '\r');
            }
            return Return;
        }



        /// <summary>
        /// Return the index of a character in a line, ignoring instances found in strings.
        /// </summary>
        /// <param name="StringToSearch">The string to look in.</param>
        /// <param name="CharToSearch">The character to look for.</param>
        /// <returns>The index of the character if found - if not, -1.</returns>
        /*public static int GetSafeIndexOf(string StringToSearch, char CharToSearch) {
            bool InString = false;
            char StringChar = ' '; 
            for (int i = 0; i < StringToSearch.Length; ++i) {
                if (StringToSearch[i] == '\'' || StringToSearch[i] == '"') {
                    if (InString == false) {
                        InString = true;
                        StringChar = StringToSearch[i];
                    } else {
                        if (StringToSearch[i] == StringChar && i > 0 && StringToSearch[i - 1] != '\\') {
                            InString = false;
                        }
                    }
                }
                if (StringToSearch[i] == CharToSearch && !InString) return i;
            }
            return -1;

        }*/


        public static int GetSafeIndexOf(string StringToSearch, char CharToSearch) {
            return GetSafeIndexOf(StringToSearch, CharToSearch, 0);
        }
        public static int GetSafeIndexOf(string StringToSearch, char CharToSearch, int Start) {

            for (int i = Start; i < StringToSearch.Length; ++i) {

                if (StringToSearch[i] == '\'' || StringToSearch[i] == '"') {
                    char StringChar = StringToSearch[i++];
                    for (; i < StringToSearch.Length; ++i) {
                        if (StringToSearch[i] == '\\') {
                            ++i;
                        } else if (StringToSearch[i] == StringChar) {
                            break;
                        }
                    }
                } else if (StringToSearch[i] == CharToSearch) {
                    return i;
                }
            }
            return -1;

        }

        /// <summary>
        /// Split a string by a character, safely taking into consideration strings/characters.
        /// </summary>
        /// <param name="StringToSplit">The string to split</param>
        /// <param name="CharToSplitBy">The character we are splitting by.</param>
        /// <returns>A string[] of the split items.</returns>
        public static string[] SafeSplit(string StringToSplit, char CharToSplitBy) {
            List<string> Return = new List<string>();
            int Split = GetSafeIndexOf(StringToSplit, CharToSplitBy);
            while (Split != -1) {
                Return.Add(StringToSplit.Remove(Split));
                StringToSplit = StringToSplit.Substring(Split + 1);
                Split = GetSafeIndexOf(StringToSplit, CharToSplitBy);
            }
            Return.Add(StringToSplit);
            return Return.ToArray();
        }

        /// <summary>
        /// Take a token and swap it for the enmacroed version.
        /// </summary>
        /// <param name="Token">The token to replace.</param>
        /// <returns>The macro corresponding to the token.</returns>
        /*public static string ApplyMacros(string Token) {

            Token = SafeStripWhitespace(Token); //.Trim();
            if (Token == "") return "";

            string Args = "";
            bool WeHaveArgs = false;
            if (Token.IndexOf('(') != -1) {
                WeHaveArgs = true;
                Args = Token.Substring(Token.IndexOf('(') + 1);
                if (Args.EndsWith(")")) Args = Args.Remove(Args.Length - 1);
                Token = Token.Remove(Token.IndexOf('('));
            }

            if (Macros[IsCaseSensitive ? Token : Token.ToLower()] == null) {
                return Token;
            }
            Macro M = (Macro)Macros[IsCaseSensitive ? Token : Token.ToLower()];

            // So we have the macro - are we passing args?
            if (M.Args.Length != 0 && !WeHaveArgs) {
                return Token;
            }

            if (M.Args.Length == 0) {
                return M.Replacement;
            } else {
                ArrayList PassedArgs = new ArrayList();
                string CurrentToken = "";
                bool AmInString = false;
                char StringMarker = ' ';
                for (int i = 0; i < Args.Length; ++i) {
                    if (Args[i] == '\'' || Args[i] == '"') {
                        if (!AmInString) {
                            AmInString = true;
                        } else if (AmInString && Args[i] == StringMarker && i > 0 && Args[i - 1] != '\\') {
                            AmInString = false;
                        }
                        
                    }
                    if (Args[i] == ',' && !AmInString) {
                        PassedArgs.Add(CurrentToken);
                        CurrentToken = "";
                    } else {
                        CurrentToken += Args[i];
                    }
                }
                if (CurrentToken != "") PassedArgs.Add(CurrentToken);

                CurrentToken = "";

                string[] TokenedReplacement = SplitIntoTokens(M.Replacement);

                Token = "";

                foreach (string S in TokenedReplacement) {
                    bool FoundArgument = false;
                    for (int i = 0; i < Math.Min(M.Args.Length, PassedArgs.Count); ++i) {
                        if (S.ToLower() == M.Args[i]) {
                            Token += PassedArgs[i];
                            FoundArgument = true;
                            break;
                        }
                        
                    }
                    if (!FoundArgument) Token += S;
                }

                return Token;

            }
        }*/

    }
}
