using System;
using System.Collections.Generic;
using System.Text;

namespace Brass {
    public partial class Program {

        private enum AppField {
            ProgramLength = 0x0F,
            DeveloperKey = 0x12,
            Revision = 0x21,
            Build = 0x31,
            Name = 0x48,
            PageCount = 0x81,
            NoSplash = 0x90,
            ImageLength = 0x7F,
            OveruseCount = 0x61,
            Hardware = 0xA1,
            Basecode = 0xC2,
        };


        public static ushort? AppKey = null;

        public static byte AppRevision = 1;
        public static byte AppBuild = 1;
        public static int AppExpires = 0;
        public static byte AppUses = 0;
        public static bool AppNoSplash = true;
        public static byte AppHardware = 0;
        public static ushort AppBasecode = 0;
		public static uint AppHeaderPadding = 128;

        public static byte[] CreateAppHeader() {
            List<byte> AppHeader = new List<byte>(128);

            if (!AppKey.HasValue) {
                switch (BinaryType) {
                    case Binary.TI73App:
                        AppKey = 0x0102;
                        break;
                    case Binary.TI8XApp:
                        AppKey = 0x0104;
                        break;
                }
                
            }

            AddAppField(AppHeader, AppField.ProgramLength, (int)0);
            AddAppField(AppHeader, AppField.DeveloperKey, (ushort)AppKey.Value);
            AddAppField(AppHeader, AppField.Revision, (byte)AppRevision);

            AddAppField(AppHeader, AppField.Build, (byte)AppBuild);

            string AppName = VariableName.PadRight(8,' ');
            if (AppName.Length > 8) {
                AppName = AppName.Remove(8);
                DisplayError(ErrorType.Warning, "Application name truncated to '" + AppName + "'.");
            }
            AddAppField(AppHeader, AppField.Name, (string)AppName);

            AddAppField(AppHeader, AppField.PageCount, (byte)Pages.Count);

            if (AppNoSplash) AddAppField(AppHeader, AppField.NoSplash);

            if (AppBasecode != 0) AddAppField(AppHeader, AppField.Basecode, (ushort)AppBasecode);

            // Date stamp
            AppHeader.AddRange(new byte[] { 0x003, 0x026, 0x009, 0x004, 0x004, 0x06f, 0x01b, 0x080 });

            // Dummy encrypted TI date stamp signature
            AppHeader.AddRange(new byte[] { 0x002, 0x00d, 0x040, 0x0a1, 0x06b, 0x099, 0x0f6, 0x059, 0x0bc, 0x067, 0x0f5, 0x085, 0x09c, 0x009, 0x06c, 0x00f, 0x0b4, 0x003, 0x09b, 0x0c9, 0x003, 0x032, 0x02c, 0x0e0, 0x003, 0x020, 0x0e3, 0x02c, 0x0f4, 0x02d, 0x073, 0x0b4, 0x027, 0x0c4, 0x0a0, 0x072, 0x054, 0x0b9, 0x0ea, 0x07c, 0x03b, 0x0aa, 0x016, 0x0f6, 0x077, 0x083, 0x07a, 0x0ee, 0x01a, 0x0d4, 0x042, 0x04c, 0x06b, 0x08b, 0x013, 0x01f, 0x0bb, 0x093, 0x08b, 0x0fc, 0x019, 0x01c, 0x03c, 0x0ec, 0x04d, 0x0e5, 0x075 });

            AddAppField(AppHeader, AppField.ImageLength, (int)0);

            if (AppHardware != 0) AddAppField(AppHeader, AppField.Hardware, (byte)AppHardware);
            if (AppUses != 0) AddAppField(AppHeader, AppField.OveruseCount, (byte)AppUses);

            // Pad
            while (AppHeader.Count < AppHeaderPadding) AppHeader.Add(0x00);
            if (AppHeader.Count > 128) {
                DisplayError(ErrorType.Warning, "Application header size > 128 bytes.");
            }



            return AppHeader.ToArray();            
        }

        private static void AddAppField(List<byte> header, AppField field) {
            header.Add(0x80);
            header.Add((byte)field);
        }

        private static void AddAppField(List<byte> header, AppField field, byte value) {
            AddAppField(header, field);
            header.Add(value);
        }

        private static void AddAppField(List<byte> header, AppField field, ushort value) {
            AddAppField(header, field);
            for (int i = 0; i < 2; ++i) {
                header.Add((byte)(value >> 8));
                value <<= 8;
            }
        }

        private static void AddAppField(List<byte> header, AppField field, int value) {
            AddAppField(header, field);
            for (int i = 0; i < 4; ++i) {
                header.Add((byte)(value >> 24));
                value <<= 8;
            }            
        }

        private static void AddAppField(List<byte> header, AppField field, string value) {
            AddAppField(header, field);
            foreach (char c in value) {
                header.Add((byte)c);
            }
        }
        
        
    }
}

