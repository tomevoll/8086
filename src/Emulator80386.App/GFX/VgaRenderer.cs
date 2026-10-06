using System;

namespace Emulator80386.App.GFX
{
    public class VgaRenderer
    {
        public const int Width = 640;
        public const int Height = 400;
        public const int Columns = 80;
        public const int Rows = 25;

        public uint[] PixelBuffer { get; } = new uint[Width * Height];

        public void RenderTextMode(byte[] vram)
        {
            if (vram == null || vram.Length < 0x18000 + (Columns * Rows * 2)) return;

            int vramOffset = 0x18000; // 0xB8000 - 0xA0000 = 0x18000 offset in VRAM array

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    int index = vramOffset + (row * Columns + col) * 2;
                    byte character = vram[index];
                    byte attribute = vram[index + 1];

                    byte fgIdx = (byte)(attribute & 0x0F);
                    byte bgIdx = (byte)((attribute >> 4) & 0x0F);

                    uint fgColor = VgaFont.Vga16Colors[fgIdx];
                    uint bgColor = VgaFont.Vga16Colors[bgIdx];

                    int fontOffset = character * 16;

                    for (int py = 0; py < 16; py++)
                    {
                        byte fontRow = VgaFont.FontData[fontOffset + py];
                        int pixelY = row * 16 + py;

                        for (int px = 0; px < 8; px++)
                        {
                            int pixelX = col * 8 + px;
                            bool bit = (fontRow & (1 << (7 - px))) != 0;

                            PixelBuffer[pixelY * Width + pixelX] = bit ? fgColor : bgColor;
                        }
                    }
                }
            }
        }
    }
}
