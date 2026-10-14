using System;
using Amiga500.Chipset;
using Amiga500.Memory;

namespace Amiga500.GFX;

public class Sprite
{
    public uint Pointer { get; set; }
    public ushort POS { get; set; }
    public ushort CTL { get; set; }
    public ushort DATA { get; set; }
    public ushort DATB { get; set; }

    public int VStart => ((POS >> 8) & 0xFF) | ((CTL & 0x04) != 0 ? 0x100 : 0);
    public int VStop => ((CTL >> 8) & 0xFF) | ((CTL & 0x02) != 0 ? 0x100 : 0);
    public int HStart => ((POS & 0xFF) << 1) | (CTL & 0x01);
}

public class Denise
{
    public ushort BPLCON0 { get; set; } // Bitplane Control 0
    public ushort BPLCON1 { get; set; } // Bitplane Scroll
    public ushort BPLCON2 { get; set; } // Bitplane Priority
    public ushort BPL1MOD { get; set; } // Odd bitplane modulo
    public ushort BPL2MOD { get; set; } // Even bitplane modulo

    public ushort DIWSTRT { get; set; } = 0x2C81; // Window Start (Default line $2C = 44)
    public ushort DIWSTOP { get; set; } = 0xF4C1; // Window Stop (Default line $F4 = 244)
    public ushort DDFSTRT { get; set; } = 0x0038; // Data Fetch Start
    public ushort DDFSTOP { get; set; } = 0x00D0; // Data Fetch Stop

    public Agnus? Agnus { get; set; }

    public uint[] BPLPT { get; } = new uint[6];         // Base registers set by CPU/Copper
    public uint[] BPLPT_Working { get; } = new uint[6]; // Working pointers during frame
    public ushort[] Color { get; } = new ushort[32];    // COLOR00 .. COLOR31

    public Sprite[] Sprites { get; } = new Sprite[8];

    // Framebuffer: 640x312 RGBA32 format
    public uint[] FrameBuffer { get; } = new uint[640 * 312];

    public AmigaMemoryBus Memory { get; set; }

    public Denise(AmigaMemoryBus memory)
    {
        Memory = memory;
        for (int i = 0; i < 8; i++) Sprites[i] = new Sprite();

        // Default Kickstart color palette initializer
        Color[0] = 0xAAA; // Light Grey / White background
        Color[1] = 0x000; // Black
        Color[2] = 0xFFF; // White
        Color[3] = 0x00F; // Blue

        // Default Sprite palette (COLOR16 - COLOR31)
        Color[16] = 0x000; Color[17] = 0xF00; Color[18] = 0x0F0; Color[19] = 0x00F;
    }

    public static uint AmigaColorToRgba32(ushort col12)
    {
        byte r = (byte)(((col12 >> 8) & 0x0F) * 17);
        byte g = (byte)(((col12 >> 4) & 0x0F) * 17);
        byte b = (byte)((col12 & 0x0F) * 17);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public void OnBplPointerWritten(int plane, uint address)
    {
        BPLPT[plane] = address;
        BPLPT_Working[plane] = address;
    }

    public void OnVblank()
    {
        // Reload working bitplane pointers from base registers on vertical blank
        for (int p = 0; p < 6; p++)
        {
            BPLPT_Working[p] = BPLPT[p];
        }
    }

    public void RenderScanline(int line)
    {
        if (line < 0 || line >= 312) return;

        uint bgColor = AmigaColorToRgba32(Color[0]);
        int lineOffset = line * 640;

        int vstart = (DIWSTRT >> 8) & 0xFF;
        int vstop = (DIWSTOP >> 8) & 0xFF;
        if (vstop < vstart) vstop += 256;

        int numPlanes = (BPLCON0 >> 12) & 0x07;
        if (numPlanes > 6) numPlanes = 6;
        bool hires = (BPLCON0 & 0x8000) != 0;
        bool dualPlayfield = (BPLCON0 & 0x0400) != 0;

        // Check Bitplane DMA enable (DMACON bit 8 = BPLEN, bit 9 = DMAEN)
        bool bplDmaEnabled = Agnus == null || ((Agnus.DMACON & 0x0300) == 0x0300);

        // Border line, 0 bitplanes, or Bitplane DMA disabled
        if (line < vstart || line > vstop || numPlanes == 0 || !bplDmaEnabled)
        {
            for (int x = 0; x < 640; x++)
            {
                FrameBuffer[lineOffset + x] = bgColor;
            }
            RenderSpritesOnScanline(line, lineOffset);
            return;
        }

        // Fetch bitplane data for 320/640 pixels
        int numWords = hires ? 40 : 20;
        ushort[][] planeData = new ushort[numPlanes][];
        for (int p = 0; p < numPlanes; p++)
        {
            planeData[p] = new ushort[numWords];
            uint ptr = BPLPT_Working[p] != 0 ? BPLPT_Working[p] : BPLPT[p];
            if (ptr != 0)
            {
                for (int w = 0; w < numWords; w++)
                {
                    planeData[p][w] = Memory.ReadWord(ptr);
                    ptr += 2;
                }
                short mod = (p % 2 == 0) ? (short)BPL1MOD : (short)BPL2MOD;
                BPLPT_Working[p] = (uint)(ptr + mod);
            }
        }

        // Render playfield pixels
        for (int wordIdx = 0; wordIdx < numWords; wordIdx++)
        {
            for (int bit = 15; bit >= 0; bit--)
            {
                byte colorIndex = 0;

                if (dualPlayfield)
                {
                    // Playfield 1 (odd bitplanes 1, 3, 5)
                    byte pf1 = 0;
                    if (numPlanes >= 1 && (planeData[0][wordIdx] & (1 << bit)) != 0) pf1 |= 1;
                    if (numPlanes >= 3 && (planeData[2][wordIdx] & (1 << bit)) != 0) pf1 |= 2;
                    if (numPlanes >= 5 && (planeData[4][wordIdx] & (1 << bit)) != 0) pf1 |= 4;

                    // Playfield 2 (even bitplanes 2, 4, 6)
                    byte pf2 = 0;
                    if (numPlanes >= 2 && (planeData[1][wordIdx] & (1 << bit)) != 0) pf2 |= 1;
                    if (numPlanes >= 4 && (planeData[3][wordIdx] & (1 << bit)) != 0) pf2 |= 2;
                    if (numPlanes >= 6 && (planeData[5][wordIdx] & (1 << bit)) != 0) pf2 |= 4;

                    colorIndex = pf1 > 0 ? pf1 : (byte)(pf2 > 0 ? (8 + pf2) : 0);
                }
                else
                {
                    for (int p = 0; p < numPlanes; p++)
                    {
                        if ((planeData[p][wordIdx] & (1 << bit)) != 0)
                        {
                            colorIndex |= (byte)(1 << p);
                        }
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

        // Render Sprites 0-7
        RenderSpritesOnScanline(line, lineOffset);
    }

    private void RenderSpritesOnScanline(int line, int lineOffset)
    {
        for (int s = 0; s < 8; s++)
        {
            var sp = Sprites[s];
            if (sp.Pointer == 0) continue;

            if (line >= sp.VStart && line < sp.VStop)
            {
                // Fetch control words on the very first line of sprite execution
                if (line == sp.VStart)
                {
                    sp.POS = Memory.ReadWord(sp.Pointer);
                    sp.CTL = Memory.ReadWord(sp.Pointer + 2);
                    sp.Pointer += 4;
                }

                ushort spDataA = Memory.ReadWord(sp.Pointer);
                ushort spDataB = Memory.ReadWord(sp.Pointer + 2);
                sp.Pointer += 4;

                int hStart = sp.HStart;
                int paletteBase = 16 + ((s / 2) * 4); // Pairs share palette

                for (int bit = 15; bit >= 0; bit--)
                {
                    byte spColor = 0;
                    if ((spDataA & (1 << bit)) != 0) spColor |= 1;
                    if ((spDataB & (1 << bit)) != 0) spColor |= 2;

                    if (spColor > 0)
                    {
                        int xPos = (hStart + (15 - bit) - 128) * 2;
                        uint spRgba = AmigaColorToRgba32(Color[(paletteBase + spColor) & 31]);

                        if (xPos >= 0 && xPos < 640) FrameBuffer[lineOffset + xPos] = spRgba;
                        if (xPos + 1 >= 0 && xPos + 1 < 640) FrameBuffer[lineOffset + xPos + 1] = spRgba;
                    }
                }
            }
        }
    }
}
