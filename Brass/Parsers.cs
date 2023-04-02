/* BRASS Z80 ASSEMBLER
 * -------------------
 * PARSERS.CS - GENERAL TEXT PARSING ROUTINES
 */

using System;
using System.Collections;
using System.Text;

namespace Brass {
    public partial class Program {


        /// <summary>
        /// Escape a string into an HTML (XML) compliant form.
        /// </summary>
        /// <param name="Data">String to escape.</param>
        /// <returns>Escaped string.</returns>
        public static string EscapeHTML(string Data) {
            return Data.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("&", "&amp;");
        }

        /// <summary>
        /// Check to see if a label name is valid or not.
        /// </summary>
        /// <param name="LabelName">Label name to check</param>
        /// <returns>True if the label is a valid name, false if it is invalid.</returns>
        public static bool CheckLabelName(string LabelName) {
            string ValidCharacters = "abcdefghijklmnopqrstuvwxyz0123456789_#.?";
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
        public static int ConvertNumber(string Number) {
            int MakeNegative = 1;
            if (Number.StartsWith("¬")) {
                MakeNegative = -1;
                Number = Number.Substring(1);
            }
            Number = Number.Trim();
            if (Number == "") return 0;
            if (Number == "$") return ProgramCounter;
            if (Number.StartsWith("'") && Number.EndsWith("'") && Number.Length >= 3) {
                Number = Number.Substring(1, Number.Length - 2).Replace(@"\'", "'");
                if (Number.Length != 1) throw new Exception("Not a valid character.");
                return (int)Number[0];
            }
            if (Number.StartsWith("$") || Number.ToLower().EndsWith("h")) {
                return Convert.ToInt32(Number.ToLower().Replace("$", "").Replace("h", ""), 16) * MakeNegative;
            } else if (Number.StartsWith("%") || Number.ToLower().EndsWith("b")) {
                return Convert.ToInt32(Number.ToLower().Replace("%", "").Replace("b", ""), 2) * MakeNegative;
            } else if (Number.StartsWith("@") || Number.ToLower().EndsWith("o")) {
                return Convert.ToInt32(Number.ToLower().Replace("@", "").Replace("o", ""), 8) * MakeNegative;
            } else {
                return Convert.ToInt32(Number.ToLower().Replace("d", ""), 10) * MakeNegative;
            }
        }

        /// <summary>
        /// Check to see if a string matches a wildcard pattern.
        /// </summary>
        /// <param name="WildcardPattern">Pattern to compare against (eg ld hl,*)</param>
        /// <param name="Test">String to test (eg ld hl,_hello)</param>
        /// <returns>True if matched, False if not matched.</returns>
        public static bool MatchWildcards(string WildcardPattern, string Test) {
            string Working = Test.Trim().ToLower().Replace("\t", "").Replace(" ", "");
            if (WildcardPattern == "\"\"" && Working == "") return true;
            // Adding \r to the end is IMPORTANT.
            // Otherwise, a,(*) vs a,((3+1)*2) would NOT match.
            WildcardPattern += "\r";
            Working += "\r";
            //
            string TokenToMatch = "";
            for (int i = 0; i < WildcardPattern.Length; ++i) {
                if (WildcardPattern[i] == '*') {
                    if (Working.StartsWith(TokenToMatch) == false) return false;
                    if (i == WildcardPattern.Length - 2) return true;
                    //Working = Working.Substring(i);
                    string EndOfAsteriskToMatch = WildcardPattern.Substring(i + 1);
                    if (EndOfAsteriskToMatch.IndexOf('*') != -1) {
                        EndOfAsteriskToMatch = EndOfAsteriskToMatch.Remove(EndOfAsteriskToMatch.IndexOf('*'));
                    }
                    int JumpToNextBit = Working.IndexOf(EndOfAsteriskToMatch);
                    if (JumpToNextBit == -1) return false;
                    Working = Working.Substring(JumpToNextBit);
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
        public static ArrayList ExtractArguments(string WildcardPattern, string Source) {

            ArrayList Returner = new ArrayList();

            if (WildcardPattern.IndexOf('*') == -1) return Returner;

            Source = SafeStripWhitespace(Source) + "\r";
            WildcardPattern += "\r";
            if (WildcardPattern == "\"\"" && Source == "") return Returner;
            for (int i = 0; i < WildcardPattern.Length; ++i) {
                if (WildcardPattern[i] == '*') {
                    // What's the next bit?
                    if (i == WildcardPattern.Length - 1) {
                        // Rest of the string:
                        Returner.Add(Source);
                        return Returner;
                    }
                    string NextBit = WildcardPattern.Substring(i + 1);
                    if (NextBit.IndexOf('*') != -1) {
                        NextBit = NextBit.Remove(NextBit.IndexOf('*'));
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
        public static int TranslateArgument(string Argument) {
            if (DebugMode) Console.WriteLine("EXP:>" + Argument);
            // Deal with character constants:
            Argument = SafeStripWhitespace(Argument);
            if (Argument.StartsWith("'") && Argument.EndsWith("'") && Argument.Length <= 4) {
                return ConvertNumber(Argument);
            }

            // Deal with reusable labels
            int ReplaceReusableLabels = Argument.IndexOf('{');
            while (ReplaceReusableLabels != -1) {
                int EndOfReusableLabel = Argument.IndexOf('}');
                if (EndOfReusableLabel == -1) throw new Exception("Badly formed reusable label in '" + Argument + "'.");

                string Before = Argument.Remove(ReplaceReusableLabels);
                string After = Argument.Substring(EndOfReusableLabel + 1);

                // Now we need to work out what the reusable label actually is.
                string RLabel = Argument.Substring(ReplaceReusableLabels, EndOfReusableLabel - ReplaceReusableLabels).Substring(1);
                if (RLabel == "") throw new Exception("Reusable label not specified.");

                char RLabelChar = RLabel[0];
                foreach (char C in RLabel) {
                    if (C != RLabelChar) throw new Exception("You cannot mix and match symbols in reusable labels.");
                }
                if (RLabelChar != '+' && RLabelChar != '-') throw new Exception("Invalid reusable label symbol.");

                int SearchLabel = ProgramCounter;
                int LabelSearchDir = (RLabelChar == '+') ? 1 : -1;
                int MaxRange = 0x10000;
                int LabelAddress = 0;
                while (MaxRange > 0) {
                    if (ReusableLabels[SearchLabel] != null) {
                        object DetectLabel = ((Hashtable)ReusableLabels[SearchLabel])[RLabel];
                        if (DetectLabel != null) {
                            LabelAddress = (int)DetectLabel;
                            break;
                        }
                    }
                    SearchLabel += LabelSearchDir;
                    --MaxRange;
                }
                if (MaxRange <= 0) throw new Exception("Matching '" + RLabel + "' label not found.");

                Argument = Before + LabelAddress.ToString() + After;

                ReplaceReusableLabels = Argument.IndexOf('{');
            }

            // Deal with double negatives.
            Argument = Argument.Replace("--", "+");

            if (Argument.IndexOfAny(new char[] { '(', ')' }) != -1) {
                // We now need to split apart ( )
                int ParenIndex = Argument.IndexOf('(');
                if (ParenIndex == -1) throw new Exception("Mismatched parentheses.");

                string BeforeString = Argument.Remove(ParenIndex);
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
                return TranslateArgument(BeforeString + TranslateArgument(MiddleString) + AfterString);
            }


            // NOTE: In REVERSE ORDER OF PRECEDENCE:
            //string[] Operators = { "?", ">>", "<<", "<", ">", ">=", "<=", "==", "!=", "!", "||", "&&", "+", "-", "*", "/", "|", "&", "^", "%", "~" };
            string[] Operators = { "?", "||", "&&", "|", "^", "&", "!=", "==", ">=", "<=", ">", "<", ">>", "<<", "+", "-", "%", "/", "*", "~", "!" };

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



            foreach (String Operator in Operators) {
                int IndexOfOperator = Argument.LastIndexOf(Operator);
                if (IndexOfOperator != -1) {
                    // Found an operator.
                    string BeforeOperator = Argument.Remove(IndexOfOperator);
                    string AfterOperator = Argument.Substring(IndexOfOperator + Operator.Length);
                    switch (Operator) {
                        case "+":
                            return TranslateArgument(BeforeOperator) + TranslateArgument(AfterOperator);
                        case "-":
                            return TranslateArgument(BeforeOperator) - TranslateArgument(AfterOperator);
                        case "*":
                            return TranslateArgument(BeforeOperator) * TranslateArgument(AfterOperator);
                        case "/":
                            return TranslateArgument(BeforeOperator) / TranslateArgument(AfterOperator);
                        case "|":
                            return TranslateArgument(BeforeOperator) | TranslateArgument(AfterOperator);
                        case "&":
                            return TranslateArgument(BeforeOperator) & TranslateArgument(AfterOperator);
                        case "^":
                            return TranslateArgument(BeforeOperator) ^ TranslateArgument(AfterOperator);
                        case ">>":
                            return TranslateArgument(BeforeOperator) >> TranslateArgument(AfterOperator);
                        case "<<":
                            return TranslateArgument(BeforeOperator) << TranslateArgument(AfterOperator);
                        case "%":
                            return TranslateArgument(BeforeOperator) % TranslateArgument(AfterOperator);
                        case "==":
                            return (TranslateArgument(BeforeOperator) == TranslateArgument(AfterOperator)) ? 1 : 0;
                        case "!=":
                            return (TranslateArgument(BeforeOperator) != TranslateArgument(AfterOperator)) ? 1 : 0;
                        case ">=":
                            return (TranslateArgument(BeforeOperator) >= TranslateArgument(AfterOperator)) ? 1 : 0;
                        case "<=":
                            return (TranslateArgument(BeforeOperator) <= TranslateArgument(AfterOperator)) ? 1 : 0;
                        case ">":
                            return (TranslateArgument(BeforeOperator) > TranslateArgument(AfterOperator)) ? 1 : 0;
                        case "<":
                            return (TranslateArgument(BeforeOperator) < TranslateArgument(AfterOperator)) ? 1 : 0;
                        case "&&":
                            return ((TranslateArgument(BeforeOperator) != 0) && (TranslateArgument(AfterOperator) != 0)) ? 1 : 0;
                        case "||":
                            return ((TranslateArgument(BeforeOperator) != 0) || (TranslateArgument(AfterOperator) != 0)) ? 1 : 0;
                        case "!":
                            return (TranslateArgument(BeforeOperator + (TranslateArgument(AfterOperator) != 0 ? 0 : 1))) != 0 ? 1 : 0;
                        case "~":
                            return TranslateArgument(BeforeOperator + ((int)~TranslateArgument(AfterOperator)).ToString());
                        case "?":
                            string[] Results = SafeSplit(AfterOperator, ':');
                            if (Results.Length != 2) throw new Exception("The ternary operator expects two result arguments; true and false.");
                            int Check = TranslateArgument(BeforeOperator);
                            int Result = TranslateArgument(Results[(Check != 0) ? 0 : 1]);
                            return Result;

                        default:
                            throw new Exception("Operator '" + Operator + "' not valid.");
                    }
                }
            }

            if (Argument.StartsWith(CurrentLocalLabel)) Argument = CurrentModule + "." + Argument;
            Argument = Argument.Trim();

            if (Labels[IsCaseSensitive ? Argument : Argument.ToLower()] != null) {
                return (int)Labels[IsCaseSensitive ? Argument : Argument.ToLower()];
            }
            return ConvertNumber(Argument);
        }

        /// <summary>
        /// Unescape a string using TASM's string escaped string format.
        /// </summary>
        /// <param name="StringToUnescape">String to unescape.</param>
        /// <returns>The original, unescaped string.</returns>
        public static string UnescapeString(string StringToUnescape) {
            ArrayList RealBackSlashes = new ArrayList();
            int NextEscapedBackslash = StringToUnescape.IndexOf(@"\\");
            while (NextEscapedBackslash != -1) {
                RealBackSlashes.Add(NextEscapedBackslash);
                //StringToUnescape = StringToUnescape.Remove(NextEscapedBackslash) + "??" + StringToUnescape.Substring(NextEscapedBackslash + 2);
                //NextEscapedBackslash = StringToUnescape.IndexOf(@"\\", NextEscapedBackslash + 1);
                return (UnescapeString(StringToUnescape.Remove(NextEscapedBackslash)) + "\\" + UnescapeString(StringToUnescape.Substring(NextEscapedBackslash + 2)));
            }
            StringToUnescape = StringToUnescape.Replace("\\\"", "\"");
            StringToUnescape = StringToUnescape.Replace(@"\n", "\n");
            StringToUnescape = StringToUnescape.Replace(@"\r", "\r");
            StringToUnescape = StringToUnescape.Replace(@"\b", "\b");
            StringToUnescape = StringToUnescape.Replace(@"\t", "\t");
            StringToUnescape = StringToUnescape.Replace(@"\f", "\f");

            /*string UnescapedString = "";
            int LastChunkStart = 0;
            foreach (int I in RealBackSlashes) {
                int Length = I - LastChunkStart;
                if (Length>0) {
                    UnescapedString += StringToUnescape.Substring(LastChunkStart, Length) + @"\";
                }
                LastChunkStart = I + 2;
            }
            UnescapedString += StringToUnescape.Substring(LastChunkStart);
            return UnescapedString;*/
            return StringToUnescape;

        }

        public static string EscapeString(string StringToEscape) {
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
            ArrayList A = new ArrayList();
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
            return (string[])A.ToArray(typeof(string));
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
                //Console.WriteLine(StringToStrip[i] + "\t:" + InString);
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
        public static int GetSafeIndexOf(string StringToSearch, char CharToSearch) {
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

        }

        /// <summary>
        /// Split a string by a character, safely taking into consideration strings/characters.
        /// </summary>
        /// <param name="StringToSplit">The string to split</param>
        /// <param name="CharToSplitBy">The character we are splitting by.</param>
        /// <returns>A string[] of the split items.</returns>
        public static string[] SafeSplit(string StringToSplit, char CharToSplitBy) {
            ArrayList Return = new ArrayList();
            int Split = GetSafeIndexOf(StringToSplit,CharToSplitBy);
            while (Split != -1) {
                Return.Add(StringToSplit.Remove(Split));
                StringToSplit = StringToSplit.Substring(Split + 1);
                Split = GetSafeIndexOf(StringToSplit,CharToSplitBy);
            }
            Return.Add(StringToSplit);
            return (string[])Return.ToArray(typeof(string));
        }

        /// <summary>
        /// Take a token and swap it for the enmacroed version.
        /// </summary>
        /// <param name="Token">The token to replace.</param>
        /// <returns>The macro corresponding to the token.</returns>
        public static string ApplyMacros(string Token) {

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
        }

    }
}
