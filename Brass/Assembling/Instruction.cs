using System;
using System.Collections;
using System.Text;
using System.Collections.Generic;

namespace Brass {
    public partial class Program {


        public static bool AddInstructionLine(string PlainDataLine) {

            string DataLine = PlainDataLine.Replace('\t', ' ');


            while (DataLine.IndexOf("  ") != -1) DataLine = DataLine.Replace("  ", " ");

            string[] DataLineComponents = DataLine.Split(' ');

            if (DataLineComponents.Length >= 6 && DataLine.ToLower()[0] >= 'a' && DataLine.ToLower()[0] <= 'z') {
                string Instr = DataLineComponents[0];
                string Args = DataLineComponents[1];
                string Opcodes = DataLineComponents[2];
                string Bytes = DataLineComponents[3];
                string Rule = DataLineComponents[4];
                string Class = DataLineComponents[5];
                string Shift = DataLineComponents.Length > 6 ? DataLineComponents[6] : "0";
                string Or = DataLineComponents.Length > 7 ? DataLineComponents[7] : "0";

                Instruction I = new Instruction();
                I.Name = Instr.ToLower();
                I.Arguments = Args.ToLower();

                try {
                    if (Opcodes == "\"\"") {
                        I.Opcodes = new byte[0];
                    } else {
                        int Length = Opcodes.Length >> 1;
                        I.Opcodes = new byte[Length];
                        for (int i = 0; i < Length; ++i) {
                            I.Opcodes[Length - i - 1] = Convert.ToByte(Opcodes.Substring(i * 2, 2), 16);
                        }
                    }

                    I.Size = Convert.ToInt32(Bytes, 16);

                    #region Rule selection
                    switch (Rule.ToLower()) {
                        case "notouch":
                        case "nop":
                            I.Rule = Instruction.InstructionRule.NoTouch;
                            break;
                        /*case "jmppage":
                            I.Rule = Instruction.InstructionRule.JmpPage;
                            break;
                        case "zpage":
                            I.Rule = Instruction.InstructionRule.ZPage;
                            break;*/
                        case "r1":
                            I.Rule = Instruction.InstructionRule.R1;
                            break;
                        /*case "r2":
                            I.Rule = Instruction.InstructionRule.R2;
                            break;
                        case "crel":
                            I.Rule = Instruction.InstructionRule.CRel;
                            break;*/
                        case "swap":
                            I.Rule = Instruction.InstructionRule.Swap;
                            break;
                        /*case "combine":
                            I.Rule = Instruction.InstructionRule.Combine;
                            break;
                        case "cswap":
                            I.Rule = Instruction.InstructionRule.CSwap;
                            break;*/
                        case "zbit":
                            I.Rule = Instruction.InstructionRule.ZBit;
                            break;
                        case "zidx":
                        case "zix":
                            I.Rule = Instruction.InstructionRule.ZIdX;
                            break;
                        case "rst":
                            I.Rule = Instruction.InstructionRule.RST;
                            break;
                        /*case "mbit":
                            I.Rule = Instruction.InstructionRule.MBit;
                            break;
                        case "mzero":
                            I.Rule = Instruction.InstructionRule.MZero;
                            break;
                        case "3arg":
                            I.Rule = Instruction.InstructionRule.ThreeArg;
                            break;
                        case "3rel":
                            I.Rule = Instruction.InstructionRule.ThreeRel;
                            break;
                        case "t1":
                            I.Rule = Instruction.InstructionRule.T1;
                            break;
                        case "tdma":
                            I.Rule = Instruction.InstructionRule.TDma;
                            break;
                        case "tar":
                            I.Rule = Instruction.InstructionRule.TAr;
                            break;
                        case "i1":
                            I.Rule = Instruction.InstructionRule.I1;
                            break;
                        case "i2":
                            I.Rule = Instruction.InstructionRule.I2;
                            break;
                        case "i3":
                            I.Rule = Instruction.InstructionRule.I3;
                            break;
                        case "i4":
                            I.Rule = Instruction.InstructionRule.I4;
                            break;
                        case "i5":
                            I.Rule = Instruction.InstructionRule.I5;
                            break;
                        case "i6":
                            I.Rule = Instruction.InstructionRule.I6;
                            break;
                        case "i7":
                            I.Rule = Instruction.InstructionRule.I7;
                            break;
                        case "i8":
                            I.Rule = Instruction.InstructionRule.I8;
                            break;*/
                        default:
                            DisplayError(ErrorType.Warning, "Rule " + Rule + " not understood.");
                            return false;
                    }
                    #endregion

                    I.Class = Convert.ToInt32(Class, 16);
                    I.Shift = Convert.ToInt32(Shift, 16);
                    I.Or = Convert.ToInt32(Or, 16);
                    AllInstructions.Add(I);

                } catch (Exception) {
                    DisplayError(ErrorType.Warning, "Could not understand instruction " + Instr + " " + Args);
                    return false;
                }
            }
            
            return true;

        }

        public static void RehashInstructionTable() {
            // Now we need to index the instructions:
            Instructions = new  Dictionary<string,InstructionGroup>(64);
            foreach (Instruction I in AllInstructions) {
                InstructionGroup IGroup;
                if (!Instructions.TryGetValue(I.Name, out IGroup)) {
                    IGroup = new InstructionGroup();
                    Instructions.Add(I.Name, IGroup);
                }
                if (I.Arguments == "\"\"") {
                    IGroup.NoArgs = I;
                } else if (I.Arguments.IndexOf('*') == -1) {
                    IGroup.SingleArgs[I.Arguments] = I;
                } else {
                    IGroup.MultipleArgs.Add(I);
                }                
            }
        }

        public class InstructionGroup {
            public Instruction NoArgs = null; // Argumentless instruction (eg CPL)
            public Dictionary<string, Instruction> SingleArgs = new  Dictionary<string,Instruction>(); // Single, predetermined arguments (eg LD A,B)
            public List<Instruction> MultipleArgs = new List<Instruction>(); // The slowest ones to parse.
        }

        public class Instruction {

            public enum InstructionRule {
                NoTouch, JmpPage, ZPage, R1, R2, CRel, Swap, Combine, CSwap, ZBit, ZIdX, MBit, MZero, ThreeArg, ThreeRel, T1, TDma, TAr, I1, I2, I3, I4, I5, I6, I7, I8, RST
            };

            public string Name = "";
            public string Arguments = "";
            public byte[] Opcodes;
            public int Size = 0;
            public InstructionRule Rule = InstructionRule.NoTouch;
            public int Class = 0;
            public int Shift = 0;
            public int Or = 0;

            public Instruction() { }
            public Instruction(string name, string arguments, int size, byte[] opcodes, InstructionRule rule, int iclass, int shift, int or) {
                Name = name;
                Arguments = arguments;
                Opcodes = opcodes;
                Rule = rule;
                Class = iclass;
                Shift = shift;
                Or = or;
                Size = size;
            }

        }
    }
}
