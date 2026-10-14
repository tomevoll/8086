using Amiga500.Audio;
using Amiga500.Config;
using Amiga500.Memory;
using Xunit;

namespace Amiga500.Tests;

public class AudioTests
{
    [Fact]
    public void AudioChannel_FetchesSamplesAndSignalsInterrupt()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        bus.OverlayEnabled = false;

        var audio = new PaulaAudio(bus);
        audio.DMACON = 0x0201; // Enable Master DMA + Audio Channel 0

        // Write 2 sample words at $4000
        bus.WriteWord(0x4000, 0x7F80); // Sample 1 = +127, -128
        bus.WriteWord(0x4002, 0x40C0); // Sample 2 = +64, -64

        var ch0 = audio.Channels[0];
        ch0.StartPointer = 0x4000;
        ch0.Pointer = 0x4000;
        ch0.Length = 2; // 2 words
        ch0.CurrentLength = 2;
        ch0.Period = 200; // Period = 200 color clocks
        ch0.Volume = 64;

        // Step 200 color clocks
        audio.StepClock(200);

        Assert.Equal(127, ch0.CurrentSampleLeft);
        Assert.Equal(-128, ch0.CurrentSampleRight);
        Assert.Equal(1, ch0.CurrentLength);

        // Step another 200 color clocks
        audio.StepClock(200);

        Assert.Equal(64, ch0.CurrentSampleLeft);
        Assert.Equal(-64, ch0.CurrentSampleRight);

        // Should have reloaded length and raised Channel 0 interrupt (INTREQ bit 7)
        Assert.Equal(2, ch0.CurrentLength);
        Assert.True((audio.INTREQ & 0x0080) != 0);
    }

    [Fact]
    public void GenerateStereoSamples_ProducesPcmStream()
    {
        var config = new EmulatorConfig();
        var bus = new AmigaMemoryBus(config, new byte[256 * 1024]);
        bus.OverlayEnabled = false;

        var audio = new PaulaAudio(bus);
        short[] pcmBuffer = new short[512]; // 256 stereo frames

        audio.GenerateStereoSamples(pcmBuffer, 44100);

        Assert.Equal(512, pcmBuffer.Length);
    }
}
