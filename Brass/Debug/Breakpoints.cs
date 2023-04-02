using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {
        public class Breakpoint {
            public readonly string Filename;
            public readonly int LineNumber;
            public readonly uint Address;
            public readonly uint Page;
            public readonly string Description;
            public Breakpoint(string Filename, int LineNumber, uint Address, uint Page, string Description) {
                this.Filename = Filename;
                this.LineNumber = LineNumber;
                this.Address = Address;
                this.Page = Page;
                this.Description = Description;
            }
        }
        public static List<Breakpoint> Breakpoints;
    }
}
