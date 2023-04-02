using System;
using System.Collections;
using System.Text;
using System.IO;

namespace Brass {
    public partial class Program {
        public enum ErrorType { Warning, Error, Message }

        public static int TotalErrors = 0;
        public static int TotalWarnings = 0;

        public static ArrayList ErrorLog;

        public static string GetErrorType(ErrorType e) {
            switch (e) {
                case ErrorType.Warning: return "Warning: ";
                case ErrorType.Error: return "Error: ";
                case ErrorType.Message: return "";
            }
            return "";
        }

        public class Error {
            public string Message = "";
            public int Line = 0;
            public string File = "";
            public ErrorType E = ErrorType.Message;


            public Error(ErrorType e, string message) {
                E = e;
                Message = message;
            }

            public Error(ErrorType e, string message, string file, int line) {
                E = e;
                Message = message;
                File = file;
                Line = line;
            }
        }


        public static string _CurrentMessageLine = "";
        public static string CurrentMessageLine {
            set {
                _CurrentMessageLine = value;
                if (_CurrentMessageLine.IndexOf('\n') != -1) {
                    string MessageToAdd = _CurrentMessageLine.Remove(_CurrentMessageLine.IndexOf('\n'));
                    ErrorLog.Add(new Error(ErrorType.Message, MessageToAdd));
                    _CurrentMessageLine = CurrentMessageLine.Substring(CurrentMessageLine.IndexOf('\n') + 1);

                }
            }
            get { return _CurrentMessageLine; }
        }

        public static void DisplayError(ErrorType e, string Message, string SourceFile, int Line) {
            if (e == ErrorType.Error) ++TotalErrors;
            if (e == ErrorType.Warning) ++TotalWarnings;
            Console.WriteLine(GetErrorType(e) + Message + " [" + Path.GetFileName(SourceFile) + ":" + Line + "]");
            if (e != ErrorType.Message) {
                ErrorLog.Add(new Error(e, Message, SourceFile, Line));
            } else {
                CurrentMessageLine += Message;
            }
        }

        public static void DisplayError(ErrorType e, string Message) {
            if (e == ErrorType.Error) ++TotalErrors;
            if (e == ErrorType.Warning) ++TotalWarnings;
            if (e != ErrorType.Message) {
                Console.WriteLine(GetErrorType(e) + Message);
                ErrorLog.Add(new Error(e, Message));
            } else {
                Console.Write(GetErrorType(e) + Message);
                CurrentMessageLine += Message;
            }
        }

    }
}
