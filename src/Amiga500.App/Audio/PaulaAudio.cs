using System;
using Amiga500.Memory;

namespace Amiga500.Audio;

public class AudioChannel
{
    public uint Pointer { get; set; }
    public uint StartPointer { get; set; }
    public ushort Length { get; set; }
    public ushort CurrentLength { get; set; }
    public ushort Period { get; set; }
    public ushort Volume { get; set; } // 0 - 64
    public ushort Data { get; set; }
    public bool Active { get; set; }

    public double Counter { get; set; }
    public sbyte CurrentSampleLeft { get; set; }
    public sbyte CurrentSampleRight { get; set; }

    public void TriggerReload()
    {
        Pointer = StartPointer;
        CurrentLength = Length;
    }
}

public class PaulaAudio
{
    public AudioChannel[] Channels { get; } = new AudioChannel[4];
    public ushort DMACON { get; set; }
    public ushort INTREQ { get; set; }

    public AmigaMemoryBus Memory { get; set; }

    private const double AmigaClockFreq = 3579545.0; // Amiga Color Clock / 2

    public PaulaAudio(AmigaMemoryBus memory)
    {
        Memory = memory;
        for (int i = 0; i < 4; i++)
        {
            Channels[i] = new AudioChannel();
        }
    }

    public void StepClock(int colorClocks)
    {
        for (int i = 0; i < 4; i++)
        {
            bool dmaEnabled = (DMACON & 0x0200) != 0 && (DMACON & (1 << i)) != 0;
            var ch = Channels[i];

            if (!dmaEnabled || ch.Period < 124) // Minimum period hardware restriction
            {
                ch.Active = false;
                continue;
            }

            ch.Active = true;
            ch.Counter += colorClocks;

            if (ch.Counter >= ch.Period)
            {
                ch.Counter -= ch.Period;

                // Fetch word from Chip RAM
                if (ch.CurrentLength > 0)
                {
                    ushort sampleWord = Memory.ReadWord(ch.Pointer);
                    ch.Pointer += 2;
                    ch.CurrentLength--;

                    // Left sample (high byte), Right sample (low byte)
                    sbyte sample1 = (sbyte)(sampleWord >> 8);
                    sbyte sample2 = (sbyte)(sampleWord & 0xFF);

                    ch.CurrentSampleLeft = sample1;
                    ch.CurrentSampleRight = sample2;
                }

                if (ch.CurrentLength == 0)
                {
                    // Loop or reload
                    ch.TriggerReload();
                    // Signal Audio Channel Interrupt (INTREQ bits 7, 8, 9, 10 for channels 0-3)
                    INTREQ |= (ushort)(1 << (7 + i));
                }
            }
        }
    }

    public void GenerateStereoSamples(short[] buffer, int sampleRate = 44100)
    {
        int numSamples = buffer.Length / 2;
        double clocksPerSample = AmigaClockFreq / sampleRate;

        for (int s = 0; s < numSamples; s++)
        {
            StepClock((int)clocksPerSample);

            int leftMix = 0;
            int rightMix = 0;

            // Channels 0 & 3 = Left, Channels 1 & 2 = Right
            leftMix += Channels[0].CurrentSampleLeft * (Channels[0].Volume & 0x3F);
            leftMix += Channels[3].CurrentSampleLeft * (Channels[3].Volume & 0x3F);

            rightMix += Channels[1].CurrentSampleLeft * (Channels[1].Volume & 0x3F);
            rightMix += Channels[2].CurrentSampleLeft * (Channels[2].Volume & 0x3F);

            // Scale to 16-bit PCM output range
            buffer[s * 2] = (short)Math.Clamp(leftMix * 4, short.MinValue, short.MaxValue);
            buffer[s * 2 + 1] = (short)Math.Clamp(rightMix * 4, short.MinValue, short.MaxValue);
        }
    }
}
