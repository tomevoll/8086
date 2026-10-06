using System;

namespace Emulator80386.App.GFX
{
    public static class VgaFont
    {
        public static readonly byte[] FontData = GenerateCP437FontData();

        private static byte[] GenerateCP437FontData()
        {
            byte[] font = new byte[256 * 16];

            for (int c = 0; c < 256; c++)
            {
                int baseOffset = c * 16;
                char ch = (char)c;

                if (c == 0 || c == 32)
                {
                    continue;
                }
                else if (c == 0xDB) // Full block
                {
                    for (int r = 0; r < 16; r++) font[baseOffset + r] = 0xFF;
                }
                else if (c == 0xDC) // Lower half block
                {
                    for (int r = 8; r < 16; r++) font[baseOffset + r] = 0xFF;
                }
                else if (c == 0xDD) // Right half block
                {
                    for (int r = 0; r < 16; r++) font[baseOffset + r] = 0x0F;
                }
                else if (c == 0xDE) // Left half block
                {
                    for (int r = 0; r < 16; r++) font[baseOffset + r] = 0xF0;
                }
                else if (c == 0xDF) // Upper half block
                {
                    for (int r = 0; r < 8; r++) font[baseOffset + r] = 0xFF;
                }
                else
                {
                    byte[] pattern = GetCharPattern(ch);
                    Array.Copy(pattern, 0, font, baseOffset, 16);
                }
            }

            return font;
        }

        private static byte[] GetCharPattern(char c)
        {
            byte[] rows = new byte[16];

            switch (c)
            {
                case 'A': case 'a':
                    rows[2] = 0x18; rows[3] = 0x3C; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x7E; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x66;
                    break;
                case 'B': case 'b':
                    rows[2] = 0x7C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x7C;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x7C;
                    break;
                case 'C': case 'c':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x60; rows[5] = 0x60;
                    rows[6] = 0x60; rows[7] = 0x60; rows[8] = 0x66; rows[9] = 0x3C;
                    break;
                case 'D': case 'd':
                    rows[2] = 0x78; rows[3] = 0x6C; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x6C; rows[9] = 0x78;
                    break;
                case 'E': case 'e':
                    rows[2] = 0x7E; rows[3] = 0x60; rows[4] = 0x60; rows[5] = 0x78;
                    rows[6] = 0x60; rows[7] = 0x60; rows[8] = 0x60; rows[9] = 0x7E;
                    break;
                case 'F': case 'f':
                    rows[2] = 0x7E; rows[3] = 0x60; rows[4] = 0x60; rows[5] = 0x78;
                    rows[6] = 0x60; rows[7] = 0x60; rows[8] = 0x60; rows[9] = 0x60;
                    break;
                case 'G': case 'g':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x60; rows[5] = 0x6E;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x3C;
                    break;
                case 'H': case 'h':
                    rows[2] = 0x66; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x7E;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x66;
                    break;
                case 'I': case 'i':
                    rows[2] = 0x3C; rows[3] = 0x18; rows[4] = 0x18; rows[5] = 0x18;
                    rows[6] = 0x18; rows[7] = 0x18; rows[8] = 0x18; rows[9] = 0x3C;
                    break;
                case 'J': case 'j':
                    rows[2] = 0x1E; rows[3] = 0x0C; rows[4] = 0x0C; rows[5] = 0x0C;
                    rows[6] = 0x0C; rows[7] = 0x6C; rows[8] = 0x6C; rows[9] = 0x38;
                    break;
                case 'K': case 'k':
                    rows[2] = 0x66; rows[3] = 0x6C; rows[4] = 0x78; rows[5] = 0x70;
                    rows[6] = 0x78; rows[7] = 0x6C; rows[8] = 0x66; rows[9] = 0x66;
                    break;
                case 'L': case 'l':
                    rows[2] = 0x60; rows[3] = 0x60; rows[4] = 0x60; rows[5] = 0x60;
                    rows[6] = 0x60; rows[7] = 0x60; rows[8] = 0x60; rows[9] = 0x7E;
                    break;
                case 'M': case 'm':
                    rows[2] = 0x63; rows[3] = 0x77; rows[4] = 0x7F; rows[5] = 0x6B;
                    rows[6] = 0x63; rows[7] = 0x63; rows[8] = 0x63; rows[9] = 0x63;
                    break;
                case 'N': case 'n':
                    rows[2] = 0x66; rows[3] = 0x76; rows[4] = 0x7E; rows[5] = 0x6E;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x66;
                    break;
                case 'O': case 'o': case '0':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x3C;
                    break;
                case 'P': case 'p':
                    rows[2] = 0x7C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x7C;
                    rows[6] = 0x60; rows[7] = 0x60; rows[8] = 0x60; rows[9] = 0x60;
                    break;
                case 'Q': case 'q':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x66; rows[7] = 0x6E; rows[8] = 0x3C; rows[9] = 0x0E;
                    break;
                case 'R': case 'r':
                    rows[2] = 0x7C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x7C;
                    rows[6] = 0x70; rows[7] = 0x68; rows[8] = 0x66; rows[9] = 0x66;
                    break;
                case 'S': case 's':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x60; rows[5] = 0x3C;
                    rows[6] = 0x06; rows[7] = 0x06; rows[8] = 0x66; rows[9] = 0x3C;
                    break;
                case 'T': case 't':
                    rows[2] = 0x7E; rows[3] = 0x18; rows[4] = 0x18; rows[5] = 0x18;
                    rows[6] = 0x18; rows[7] = 0x18; rows[8] = 0x18; rows[9] = 0x18;
                    break;
                case 'U': case 'u':
                    rows[2] = 0x66; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x3C;
                    break;

                case 'V': case 'v':
                    rows[2] = 0x66; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x66;
                    rows[6] = 0x66; rows[7] = 0x3C; rows[8] = 0x18; rows[9] = 0x18;
                    break;

                case 'W': case 'w':
                    rows[2] = 0x63; rows[3] = 0x63; rows[4] = 0x63; rows[5] = 0x6B;
                    rows[6] = 0x7F; rows[7] = 0x77; rows[8] = 0x63; rows[9] = 0x63;
                    break;

                case 'X': case 'x':
                    rows[2] = 0x66; rows[3] = 0x66; rows[4] = 0x3C; rows[5] = 0x18;
                    rows[6] = 0x18; rows[7] = 0x3C; rows[8] = 0x66; rows[9] = 0x66;
                    break;

                case 'Y': case 'y':
                    rows[2] = 0x66; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x3C;
                    rows[6] = 0x18; rows[7] = 0x18; rows[8] = 0x18; rows[9] = 0x18;
                    break;

                case 'Z': case 'z':
                    rows[2] = 0x7E; rows[3] = 0x06; rows[4] = 0x0C; rows[5] = 0x18;
                    rows[6] = 0x30; rows[7] = 0x60; rows[8] = 0x60; rows[9] = 0x7E;
                    break;

                case '1':
                    rows[2] = 0x18; rows[3] = 0x38; rows[4] = 0x18; rows[5] = 0x18;
                    rows[6] = 0x18; rows[7] = 0x18; rows[8] = 0x18; rows[9] = 0x7E;
                    break;
                case '2':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x06; rows[5] = 0x0C;
                    rows[6] = 0x18; rows[7] = 0x30; rows[8] = 0x60; rows[9] = 0x7E;
                    break;
                case '3':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x06; rows[5] = 0x1C;
                    rows[6] = 0x06; rows[7] = 0x06; rows[8] = 0x66; rows[9] = 0x3C;
                    break;

                case '4':
                    rows[2] = 0x0C; rows[3] = 0x1C; rows[4] = 0x3C; rows[5] = 0x6C;
                    rows[6] = 0x7E; rows[7] = 0x0C; rows[8] = 0x0C; rows[9] = 0x1E;
                    break;

                case '5':
                    rows[2] = 0x7E; rows[3] = 0x60; rows[4] = 0x7C; rows[5] = 0x06;
                    rows[6] = 0x06; rows[7] = 0x06; rows[8] = 0x66; rows[9] = 0x3C;
                    break;

                case '6':
                    rows[2] = 0x38; rows[3] = 0x60; rows[4] = 0x60; rows[5] = 0x7C;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x3C;
                    break;

                case '7':
                    rows[2] = 0x7E; rows[3] = 0x66; rows[4] = 0x0C; rows[5] = 0x18;
                    rows[6] = 0x18; rows[7] = 0x18; rows[8] = 0x18; rows[9] = 0x18;
                    break;

                case '8':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x3C;
                    rows[6] = 0x66; rows[7] = 0x66; rows[8] = 0x66; rows[9] = 0x3C;
                    break;

                case '9':
                    rows[2] = 0x3C; rows[3] = 0x66; rows[4] = 0x66; rows[5] = 0x3E;
                    rows[6] = 0x06; rows[7] = 0x06; rows[8] = 0x0C; rows[9] = 0x38;
                    break;

                case ':':
                    rows[4] = 0x18; rows[5] = 0x18; rows[8] = 0x18; rows[9] = 0x18;
                    break;

                case '-':
                    rows[6] = 0x7E;
                    break;

                case '=':
                    rows[5] = 0x7E; rows[7] = 0x7E;
                    break;

                case '.':
                    rows[8] = 0x18; rows[9] = 0x18;
                    break;

                default:
                    rows[3] = 0x3C; rows[4] = 0x42; rows[5] = 0x42; rows[6] = 0x3C;
                    break;
            }

            return rows;
        }

        // Standard 16-color VGA Palette
        public static readonly uint[] Vga16Colors = new uint[16]
        {
            MakeColor(0, 0, 0),        // 0: Black
            MakeColor(0, 0, 170),      // 1: Blue
            MakeColor(0, 170, 0),      // 2: Green
            MakeColor(0, 170, 170),    // 3: Cyan
            MakeColor(170, 0, 0),      // 4: Red
            MakeColor(170, 0, 170),    // 5: Magenta
            MakeColor(170, 85, 0),     // 6: Brown
            MakeColor(170, 170, 170),  // 7: Light Gray
            MakeColor(85, 85, 85),     // 8: Dark Gray
            MakeColor(85, 85, 255),    // 9: Bright Blue
            MakeColor(85, 255, 85),    // 10: Bright Green
            MakeColor(85, 255, 255),   // 11: Bright Cyan
            MakeColor(255, 85, 85),    // 12: Bright Red
            MakeColor(255, 85, 255),   // 13: Bright Magenta
            MakeColor(255, 255, 85),   // 14: Yellow
            MakeColor(255, 255, 255)   // 15: White
        };

        private static uint MakeColor(byte r, byte g, byte b)
        {
            return (uint)(r | (g << 8) | (b << 16) | (255 << 24));
        }
    }
}
