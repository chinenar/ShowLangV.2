using System;
using System.Runtime.InteropServices;

namespace UiaTextPattern2Probe;

[ComImport]
[Guid("e22ad333-b25f-460c-83d0-0581107395c9")]
internal sealed class CUIAutomation8
{
}

[ComImport]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationNative
{
    [PreserveSig] int Slot1();
    [PreserveSig] int Slot2();
    [PreserveSig] int Slot3();
    [PreserveSig] int Slot4();
    [PreserveSig] int Slot5();

    [PreserveSig]
    int GetFocusedElement(
        [MarshalAs(UnmanagedType.Interface)]
        out IUIAutomationElementNative element);
}

[ComImport]
[Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementNative
{
    [PreserveSig] int Slot1();
    [PreserveSig] int Slot2();
    [PreserveSig] int Slot3();
    [PreserveSig] int Slot4();
    [PreserveSig] int Slot5();
    [PreserveSig] int Slot6();
    [PreserveSig] int Slot7();
    [PreserveSig] int Slot8();
    [PreserveSig] int Slot9();
    [PreserveSig] int Slot10();
    [PreserveSig] int Slot11();

    [PreserveSig]
    int GetCurrentPatternAs(
        int patternId,
        ref Guid interfaceId,
        out IntPtr patternObject);
}

[ComImport]
[Guid("506a921a-fcc9-409f-b23b-37eb74106872")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextPattern2Native
{
    [PreserveSig] int Slot1();
    [PreserveSig] int Slot2();
    [PreserveSig] int Slot3();
    [PreserveSig] int Slot4();
    [PreserveSig] int Slot5();
    [PreserveSig] int Slot6();
    [PreserveSig] int Slot7();

    [PreserveSig]
    int GetCaretRange(
        out int isActive,
        [MarshalAs(UnmanagedType.Interface)]
        out IUIAutomationTextRangeNative range);
}

[ComImport]
[Guid("a543cc6a-f4ae-494b-8239-c814481187a8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRangeNative
{
    [PreserveSig] int Slot1();
    [PreserveSig] int Slot2();
    [PreserveSig] int Slot3();
    [PreserveSig] int Slot4();
    [PreserveSig] int Slot5();
    [PreserveSig] int Slot6();
    [PreserveSig] int Slot7();

    [PreserveSig]
    int GetBoundingRectangles(
        [MarshalAs(UnmanagedType.SafeArray,
            SafeArraySubType = VarEnum.VT_R8)]
        out double[] rectangles);
}

public static class Probe
{
    private const int TextPattern2Id = 10024;
    private static readonly Guid TextPattern2InterfaceId =
        new("506a921a-fcc9-409f-b23b-37eb74106872");

    public static string ReadCaretRectangles()
    {
        object? automationObject = null;
        object? elementObject = null;
        object? patternObject = null;
        object? rangeObject = null;
        IntPtr patternPointer = IntPtr.Zero;
        try
        {
            automationObject = new CUIAutomation8();
            IUIAutomationNative automation =
                (IUIAutomationNative)automationObject;

            int result = automation.GetFocusedElement(
                out IUIAutomationElementNative element);
            if (result < 0)
            {
                return $"GetFocusedElement HRESULT=0x{result:X8}";
            }

            elementObject = element;
            Guid interfaceId = TextPattern2InterfaceId;
            result = element.GetCurrentPatternAs(
                TextPattern2Id,
                ref interfaceId,
                out patternPointer);
            if (result < 0 || patternPointer == IntPtr.Zero)
            {
                return $"GetCurrentPatternAs HRESULT=0x{result:X8}";
            }

            patternObject = Marshal.GetObjectForIUnknown(patternPointer);
            IUIAutomationTextPattern2Native pattern =
                (IUIAutomationTextPattern2Native)patternObject;

            result = pattern.GetCaretRange(
                out int isActive,
                out IUIAutomationTextRangeNative range);
            if (result < 0)
            {
                return $"GetCaretRange HRESULT=0x{result:X8}";
            }

            rangeObject = range;
            result = range.GetBoundingRectangles(
                out double[] rectangles);
            if (result < 0)
            {
                return $"GetBoundingRectangles HRESULT=0x{result:X8}";
            }

            return $"active={isActive}; count={rectangles.Length}; "
                + string.Join(",", rectangles);
        }
        catch (Exception exception)
        {
            return exception.ToString();
        }
        finally
        {
            if (patternPointer != IntPtr.Zero)
            {
                Marshal.Release(patternPointer);
            }

            Release(rangeObject);
            Release(patternObject);
            Release(elementObject);
            Release(automationObject);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
