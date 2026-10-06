namespace Emulator80386.App.GFX
{
    public static class VgaFont
    {
        // 8x16 CP437 VGA Font bitmap data (256 characters * 16 bytes per char)
        public static readonly byte[] FontData = GenerateCP437FontData();

        private static byte[] GenerateCP437FontData()
        {
            byte[] font = new byte[256 * 16];

            // Basic ASCII printable character bitmap patterns (simplified representative glyph set)
            for (int c = 0; c < 256; c++)
            {
                int baseOffset = c * 16;
                if (c >= 32 && c <= 126)
                {
                    // Basic glyph shapes
                    for (int row = 2; row < 14; row++)
                    {
                        if (c == 'A' || c == 'a') font[baseOffset + row] = 0x3C; // 00111100
                        else if (c == 'B' || c == 'b') font[baseOffset + row] = 0x76;
                        else if (c == 'C' || c == 'c') font[baseOffset + row] = 0x3E;
                        else if (c == 'O' || c == 'o' || c == '0') font[baseOffset + row] = 0x3C;
                        else if (c == 'H' || c == 'h') font[baseOffset + row] = (row == 7 || row == 8) ? (byte)0x7E : (byte)0x66;
                        else if (c == 'I' || c == 'i' || c == '1') font[baseOffset + row] = 0x18;
                        else if (c == '=') font[baseOffset + row] = (row == 6 || row == 9) ? (byte)0x7E : (byte)0x00;
                        else if (c == '-') font[baseOffset + row] = (row == 7) ? (byte)0x7E : (byte)0x00;
                        else font[baseOffset + row] = 0x5A; // General pattern
                    }
                }
                else if (c == 0xDB) // Solid block
                {
                    for (int row = 0; row < 16; row++) font[baseOffset + row] = 0xFF;
                }
            }

            return font;
        }

        // Standard 16-color VGA Palette (RGBA bytes)
        public static readonly uint[] Vga16Colors = new uint[16]
        {
            0xFF000000, // 0: Black
            0xFFAA0000, // 1: Blue
            0xFF00AA00, // 2: Green
            0xFFAAAA00, // 3: Cyan
            0xFF0000AA, // 4: Red
            0xFFAA00AA, // 5: Magenta
            0xFF0055AA, // 6: Brown
            0xFFAAAAAA, // 7: Light Gray
            0xFF555555, // 8: Dark Gray
            0xFFFF5555, // 9: Bright Blue
            0xFF55FF55, // 10: Bright Green
            0xFFFFFF55, // 11: Bright Cyan
            0xFF5555FF, // 12: Bright Red
            0xFFFF55FF, // 13: Bright Magenta
            0xFF55FFFF, // 14: Yellow
            0xFFFFFFFF  // 15: White
        };
    }
}
