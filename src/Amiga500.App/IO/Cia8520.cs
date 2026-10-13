using System;

namespace Amiga500.IO;

public class Cia8520 : ICiaDevice
{
    public string Name { get; }

    public byte OutputLatchA { get; set; } = 0xFF;
    public byte OutputLatchB { get; set; } = 0xFF;

    public byte DDRA { get; set; } = 0x00; // Direction A
    public byte DDRB { get; set; } = 0x00; // Direction B

    public byte PRA => (byte)((OutputLatchA & DDRA) | (~DDRA & 0xFF));
    public byte PRB => (byte)((OutputLatchB & DDRB) | (~DDRB & 0xFF));

    // Timer A
    public ushort LatchA { get; set; } = 0xFFFF;
    public ushort TimerA { get; set; } = 0xFFFF;
    public byte CRA { get; set; } = 0x00; // Control Register A
    public bool TimerAStarted => (CRA & 1) != 0;

    // Timer B
    public ushort LatchB { get; set; } = 0xFFFF;
    public ushort TimerB { get; set; } = 0xFFFF;
    public byte CRB { get; set; } = 0x00; // Control Register B
    public bool TimerBStarted => (CRB & 1) != 0;

    // Interrupt Control
    public byte ICRMask { get; set; } = 0x00; // Interrupt Enable Mask
    public byte ICRData { get; set; } = 0x00; // Interrupt Pending Data
    public bool InterruptSignaled { get; set; }

    public Action<bool>? OnOverlayChanged { get; set; }

    public Cia8520(string name)
    {
        Name = name;
        if (Name == "CIA-A")
        {
            OutputLatchA = 0x01; // Bit 0 = OVL = 1 (Kickstart ROM overlay enabled)
        }
    }

    public void StepEClock(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // Timer A
            if (TimerAStarted)
            {
                if (TimerA == 0)
                {
                    TimerA = LatchA;
                    SignalInterrupt(0x01); // Bit 0 = Timer A
                    if ((CRA & 0x08) != 0) CRA &= 0xFE; // One-shot mode
                }
                else
                {
                    TimerA--;
                }
            }

            // Timer B
            if (TimerBStarted)
            {
                if (TimerB == 0)
                {
                    TimerB = LatchB;
                    SignalInterrupt(0x02); // Bit 1 = Timer B
                    if ((CRB & 0x08) != 0) CRB &= 0xFE; // One-shot mode
                }
                else
                {
                    TimerB--;
                }
            }
        }
    }

    public void SignalInterrupt(byte bitMask)
    {
        ICRData |= bitMask;
        if ((ICRMask & bitMask) != 0)
        {
            ICRData |= 0x80; // Set IRQ pending bit 7
            InterruptSignaled = true;
        }
    }

    public byte ReadRegister(uint rawAddress)
    {
        uint reg = (rawAddress >> 8) & 0x0F;
        switch (reg)
        {
            case 0x0: return PRA;
            case 0x1: return PRB;
            case 0x2: return DDRA;
            case 0x3: return DDRB;
            case 0x4: return (byte)(TimerA & 0xFF);
            case 0x5: return (byte)(TimerA >> 8);
            case 0x6: return (byte)(TimerB & 0xFF);
            case 0x7: return (byte)(TimerB >> 8);
            case 0xD: // Reading ICR clears pending flags!
                byte ret = ICRData;
                ICRData = 0;
                InterruptSignaled = false;
                return ret;
            case 0xE: return CRA;
            case 0xF: return CRB;
            default: return 0xFF;
        }
    }

    public void WriteRegister(uint rawAddress, byte value)
    {
        uint reg = (rawAddress >> 8) & 0x0F;
        switch (reg)
        {
            case 0x0:
                bool oldOvlA = (OutputLatchA & 1) != 0;
                OutputLatchA = value;
                if (Name == "CIA-A")
                {
                    bool newOvlA = (OutputLatchA & 1) != 0;
                    if (oldOvlA != newOvlA)
                    {
                        OnOverlayChanged?.Invoke(newOvlA);
                    }
                }
                break;
            case 0x1: OutputLatchB = value; break;
            case 0x2:
                DDRA = value;
                if (Name == "CIA-A")
                {
                    bool ovl = (OutputLatchA & 1) != 0;
                    OnOverlayChanged?.Invoke(ovl);
                }
                break;
            case 0x3: DDRB = value; break;
            case 0x4: LatchA = (ushort)((LatchA & 0xFF00) | value); break;
            case 0x5:
                LatchA = (ushort)((LatchA & 0x00FF) | (value << 8));
                if (!TimerAStarted) TimerA = LatchA;
                break;
            case 0x6: LatchB = (ushort)((LatchB & 0xFF00) | value); break;
            case 0x7:
                LatchB = (ushort)((LatchB & 0x00FF) | (value << 8));
                if (!TimerBStarted) TimerB = LatchB;
                break;
            case 0xD: // Writing ICR sets/clears interrupt mask
                if ((value & 0x80) != 0)
                {
                    ICRMask |= (byte)(value & 0x7F);
                }
                else
                {
                    ICRMask &= (byte)~(value & 0x7F);
                }
                break;
            case 0xE:
                CRA = value;
                if ((value & 0x10) != 0) TimerA = LatchA; // Force Load
                break;
            case 0xF:
                CRB = value;
                if ((value & 0x10) != 0) TimerB = LatchB; // Force Load
                break;
        }
    }
}
