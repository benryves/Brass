using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {

        public static List<OutputAddress> OutputAddresses;

        public struct OutputAddress : IComparable {
            public readonly uint Address;
            public readonly uint Page;
            public readonly string Filename;
            public readonly int LineNumber;
            public readonly byte Value;
            
            public OutputAddress(uint Address, uint Page, string Filename, int LineNumber, byte Value) {
                this.Address = Address;
                this.Page = Page;
                this.Filename = Filename;
                this.LineNumber = LineNumber;
                this.Value = Value;
            }

            public int CompareTo(object obj) {
                int PageCompare = this.Page.CompareTo(((OutputAddress)obj).Page);
                if (PageCompare != 0) {
                    return PageCompare;
                } else {
                    return this.Address.CompareTo(((OutputAddress)obj).Address);
                }
            }
        }
    }
}
