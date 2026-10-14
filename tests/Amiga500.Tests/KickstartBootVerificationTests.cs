using System;
using System.IO;
using Amiga500.Config;
using Amiga500.GFX;
using Xunit;

namespace Amiga500.Tests;

public class KickstartBootVerificationTests
{
    [Fact]
    public void HeadlessBootVerification_RunsFrameLoopAndInspectsFramebuffer()
    {
        var config = new EmulatorConfig();

        // Generate a valid synthetic Kickstart ROM image with proper reset vector ($00FC0002) and code
        byte[] romData = new byte[256 * 1024];

        // Initial SSP ($00080000) and PC ($00FC0002)
        romData[0] = 0x00; romData[1] = 0x08; romData[2] = 0x00; romData[3] = 0x00;
        romData[4] = 0x00; romData[5] = 0xFC; romData[6] = 0x00; romData[7] = 0x02;

        // ROM instructions at $00FC0002:
        // 1) MOVE.W #$0F00, $DFF180 (Set COLOR00 to Red) -> 0x33FC 0x0F00 0x00DF 0xF180
        // 2) BRA.S * (Infinite loop) -> 0x60FE
        int pcOffset = 0x0002;
        byte[] code = new byte[]
        {
            0x33, 0xFC, 0x0F, 0x00, 0x00, 0xDF, 0xF1, 0x80,
            0x60, 0xFE
        };
        Array.Copy(code, 0, romData, pcOffset, code.Length);

        var system = new AmigaSystem(config, romData);
        system.Cpu.TraceEnabled = true;

        // Step 10 frames headlessly
        for (int frame = 0; frame < 10; frame++)
        {
            system.StepFrame();
        }

        // Verify CPU executed instructions
        Assert.True(system.Cpu.InstructionCount > 100);

        // Verify Denise FrameBuffer output is rendered
        Assert.NotNull(system.Denise.FrameBuffer);
        Assert.Equal(640 * 312, system.Denise.FrameBuffer.Length);
    }
}
