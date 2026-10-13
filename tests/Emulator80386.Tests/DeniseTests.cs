using Amiga500.Config;
using Amiga500.GFX;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class DeniseTests
{
    [Fact]
    public void AmigaColorToRgba32_Converts12BitColorCorrectly()
    {
        // $0F00 = Pure Red
        uint red = Denise.AmigaColorToRgba32(0x0F00);
        Assert.Equal(0xFFFF0000u, red);

        // $00F0 = Pure Green
        uint green = Denise.AmigaColorToRgba32(0x00F0);
        Assert.Equal(0xFF00FF00u, green);

        // $000F = Pure Blue
        uint blue = Denise.AmigaColorToRgba32(0x000F);
        Assert.Equal(0xFF0000FFu, blue);

        // $0FFF = Pure White
        uint white = Denise.AmigaColorToRgba32(0x0FFF);
        Assert.Equal(0xFFFFFFFFu, white);
    }

    [Fact]
    public void RenderScanline_ZeroBitplanes_FillsBackground()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        var denise = new Denise(bus);

        denise.Color[0] = 0x000F; // Blue background
        denise.BPLCON0 = 0x0000; // 0 bitplanes

        denise.RenderScanline(50);

        uint expectedBlue = Denise.AmigaColorToRgba32(0x000F);
        Assert.Equal(expectedBlue, denise.FrameBuffer[50 * 640]);
        Assert.Equal(expectedBlue, denise.FrameBuffer[50 * 640 + 319]);
    }

    [Fact]
    public void RenderScanline_OneBitplane_RendersPixels()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        bus.OverlayEnabled = false;
        var denise = new Denise(bus);

        denise.Color[0] = 0x0000; // Color 0 = Black
        denise.Color[1] = 0x0FFF; // Color 1 = White
        denise.BPLCON0 = 0x1000;  // 1 bitplane, low-res

        // Write bitplane pattern at $2000: 0x8000 = MSB set (first pixel white)
        bus.WriteWord(0x2000, 0x8000);
        denise.BPLPT[0] = 0x2000;

        denise.RenderScanline(100);

        uint white = Denise.AmigaColorToRgba32(0x0FFF);
        uint black = Denise.AmigaColorToRgba32(0x0000);

        // In low-res mode, pixel 0 spans 2 pixels (0 and 1 in 640-wide framebuffer)
        Assert.Equal(white, denise.FrameBuffer[100 * 640 + 0]);
        Assert.Equal(white, denise.FrameBuffer[100 * 640 + 1]);
        Assert.Equal(black, denise.FrameBuffer[100 * 640 + 2]);
    }
}
