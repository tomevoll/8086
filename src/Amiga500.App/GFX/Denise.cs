using System;
using Amiga500.Memory;

namespace Amiga500.GFX;

public class Denise
{
    public ushort BPLCON0 { get; set; } // Bitplane Control 0
    public ushort BPLCON1 { get; set; } // Bitplane Scroll
    public ushort BPLCON2 { get; set; } // Bitplane Priority
    public ushort BPL1MOD { get; set; } // Odd bitplane modulo
    public ushort BPL2MOD { get; set; } // Even bitplane modulo

    public ushort DIWSTRT { get; set; } = 0x2C81; // Window Start
    public ushort DIWSTOP { get; set; } = 0xF4C1; // Window Stop
    public ushort DDFSTRT { get; set; } = 0x0038; // Data Fetch Start
    public ushort DDFSTOP { get; set; } = 0x00D0; // Data Fetch Stop

    public uint[] BPLPT { get; } = new uint[6];      // BPL1PTH/L .. BPL6PTH/L
    public ushort[] Color { get; } = new ushort[32]; // COLOR00 .. COLOR31

    // Framebuffer: 640x312 RGBA32 format
    public uint[] FrameBuffer { get; } = new uint[640 * 312];

    public AmigaMemoryBus Memory { get; set; }

    public Denise(AmigaMemoryBus memory)
    {
        Memory = memory;
        // Default Kickstart color palette initializer
        Color[0] = 0x000; // Black/Blue background
        Color[1] = 0xFFF; // White
        Color[2] = 0x000; // Black
        Color[3] = 0xAAA; // Grey
    }

    public static uint AmigaColorToRgba32(ushort col12)
    {
        byte r = (byte)(((col12 >> 8) & 0x0F) * 17);
        byte g = (byte)(((col12 >> 4) & 0x0F) * 17);
        byte b = (byte)((col12 & 0x0F) * 17);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public void RenderScanline(int line)
    {
        if (line < 0 || line >= 312) return;

        int numPlanes = (BPLCON0 >> 12) & 0x07;
        if (numPlanes > 6) numPlanes = 6;
        bool hires = (BPLCON0 & 0x8000) != 0;

        uint bgColor = AmigaColorToRgba32(Color[0]);
        int lineOffset = line * 640;

        if (numPlanes == 0)
        {
            // Border / background color fill
            for (int x = 0; x < 640; x++)
            {
                FrameBuffer[lineOffset + x] = bgColor;
            }
            return;
        }

        // Fetch bitplane data for 320/640 pixels
        int numWords = hires ? 40 : 20;
        ushort[][] planeData = new ushort[numPlanes][];
        for (int p = 0; p < numPlanes; p++)
        {
            planeData[p] = new ushort[numWords];
            uint ptr = BPLPT[p];
            for (int w = 0; w < numWords; w++)
            {
                planeData[p][w] = Memory.ReadWord(ptr);
                ptr += 2;
            }
            // Advance bitplane pointers with modulo
            short mod = (p % 2 == 0) ? (short)BPL1MOD : (short)BPL2MOD;
            BPLPT[p] = (uint)(ptr + mod);
        }

        // Render pixels
        int targetWidth = hires ? 640 : 320;
        for (int wordIdx = 0; wordIdx < numWords; wordIdx++)
        {
            for (int bit = 15; bit >= 0; bit--)
            {
                byte colorIndex = 0;
                for (int p = 0; p < numPlanes; p++)
                {
                    if ((planeData[p][wordIdx] & (1 << bit)) != 0)
                    {
                        colorIndex |= (byte)(1 << p);
                    }
                }

                uint pixelRgba = AmigaColorToRgba32(Color[colorIndex & 31]);
                int xPos = (wordIdx * 16) + (15 - bit);

                if (hires)
                {
                    if (xPos < 640) FrameBuffer[lineOffset + xPos] = pixelRgba;
                }
                else
                {
                    int x2 = xPos * 2;
                    if (x2 + 1 < 640)
                    {
                        FrameBuffer[lineOffset + x2] = pixelRgba;
                        FrameBuffer[lineOffset + x2 + 1] = pixelRgba;
                    }
                }
            }
        }
    }
}
