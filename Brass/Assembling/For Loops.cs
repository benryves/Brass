using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {

        public class ForLoop {
            public double Start = 0;
            public double End = 0;
            public int Length;
            public int Progress = 0;
            public string Filename = "";
            public int LineNumber = 0;
            public int LinePart = 0;
            public string RealSourceLine;

            public void CalculateLength(double Step) {
                this.Length = (int)((End - Start) / Step);
            }
            public bool Step(ref LabelDetails ToStep) {
                ++Progress;
                ToStep.RealValue = ((double)Progress * (End - Start)) / (double)Length + Start;
                return (Progress == Length + 1);
            }

        }

        public static Stack<LabelDetails> LastForLoop;


    }
}
