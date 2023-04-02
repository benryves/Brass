using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {
        public static double ExecuteFunction(string FunctionName, string InsideFunction) {
            IType WorkingType;
            switch (FunctionName.ToLower()) {
                case "":
                    return Evaluate(InsideFunction);
                case "sizeof":
                    if (!TryGetTypeInformation(InsideFunction, out WorkingType, true)) {
                        LabelDetails L;
                        if (!TryGetLabel(InsideFunction, out L, false)) {
                            throw new Exception("Type or label '" + FunctionName + "' not found.");
                        }
                        return (double)L.Size;
                    } else {
                        return (double)WorkingType.Size;
                    }
				case "defined":
					var macroName = InsideFunction.Trim();
					return AvailableMacros.ContainsKey(IsCaseSensitive ? macroName : macroName.ToLowerInvariant()) ? 1.0d : 0.0d;
                default:
                    if (TryGetTypeInformation(FunctionName, out WorkingType)) {
                        try {
                            return WorkingType.Cast(Evaluate(InsideFunction));
                        } catch {
                            throw new Exception("Type '" + FunctionName + "' does not support casting.");
                        }
                    } else {
                        throw new Exception("Function/type '" + FunctionName + "()' not not understood.");
                    }
            }
        }
    }
}
