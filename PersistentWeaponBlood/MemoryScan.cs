using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PersistentWeaponBlood
{
    public static class MemoryScan
    {
        /// <inheritdoc cref="FindPatternBmhInternal(string, string, IntPtr, ulong)"/>
		public static unsafe byte* FindPatternBmhInternal(string pattern, string mask)
        {
            ProcessModule module = Process.GetCurrentProcess().MainModule;
            return FindPatternBmhInternal(pattern, mask, module.BaseAddress, (ulong)module.ModuleMemorySize);
        }

        /// <inheritdoc cref="FindPatternBmhInternal(string, string, IntPtr, ulong)"/>
        public static unsafe byte* FindPatternBmhInternal(string pattern, string mask, IntPtr startAddress)
        {
            ProcessModule module = Process.GetCurrentProcess().MainModule;

            if ((ulong)startAddress.ToInt64() < (ulong)module.BaseAddress.ToInt64())
                return null;

            ulong size = (ulong)module.ModuleMemorySize - ((ulong)startAddress - (ulong)module.BaseAddress);

            return FindPatternBmhInternal(pattern, mask, startAddress, size);
        }

        /// <summary>
        /// Searches the address space of the current process for a memory pattern using the Boyer–Moore–Horspool algorithm.
        /// Will perform faster than the naive algorithm when the pattern is long enough to expect the bad character skip is consistently high.
        /// </summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="mask">The pattern mask.</param>
        /// <param name="startAddress">The address to start searching at.</param>
        /// <param name="size">The size where the pattern search will be performed from <paramref name="startAddress"/>.</param>
        /// <returns>The address of a region matching the pattern or <see langword="null" /> if none was found.</returns>
        public static unsafe byte* FindPatternBmhInternal(string pattern, string mask, IntPtr startAddress, ulong size)
        {
            // Use short array intentionally to spare heap
            // Warning: throws an exception if length of pattern and mask strings does not match
            short[] patternArray = new short[pattern.Length];
            for (int i = 0; i < patternArray.Length; i++)
            {
                patternArray[i] = (mask[i] != '?') ? (short)pattern[i] : (short)-1;
            }

            int lastPatternIndex = patternArray.Length - 1;
            short[] skipTable = CreateShiftTableForBmh(patternArray);

            byte* endAddressToScan = (byte*)startAddress + size - patternArray.Length;

            // Pin arrays to avoid boundary check, search will be long enough to amortize the pin cost in time wise
            fixed (short* skipTablePtr = skipTable)
            fixed (short* patternArrayPtr = patternArray)
            {
                for (byte* curHeadAddress = (byte*)startAddress; curHeadAddress <= endAddressToScan; curHeadAddress += Math.Max((int)skipTablePtr[(curHeadAddress)[lastPatternIndex] & 0xFF], 1))
                {
                    for (var i = lastPatternIndex; patternArrayPtr[i] < 0 || ((byte*)curHeadAddress)[i] == patternArrayPtr[i]; --i)
                    {
                        if (i == 0)
                        {
                            return curHeadAddress;
                        }
                    }
                }
            }

            return null;
        }

        private static short[] CreateShiftTableForBmh(short[] pattern)
        {
            short[] skipTable = new short[256];
            int lastIndex = pattern.Length - 1;

            int diff = lastIndex - Math.Max(Array.LastIndexOf<short>(pattern, -1), 0);
            if (diff == 0)
            {
                diff = 1;
            }

            for (var i = 0; i < skipTable.Length; i++)
            {
                skipTable[i] = (short)diff;
            }

            for (var i = lastIndex - diff; i < lastIndex; i++)
            {
                var patternVal = pattern[i];
                if (patternVal >= 0)
                {
                    skipTable[patternVal] = (short)(lastIndex - i);
                }
            }

            return skipTable;
        }

        public static IntPtr FindPatternBmh(string pattern, IntPtr startAddress = default)
        {
            StringBuilder patternStringBuilder = new StringBuilder();
            StringBuilder maskStringBuilder = new StringBuilder();

            foreach (string rawHex in pattern.Split(' '))
            {
                if (string.IsNullOrEmpty(rawHex))
                {
                    continue;
                }

                if (rawHex == "??" || rawHex == "?")
                {
                    patternStringBuilder.Append("\x00");
                    maskStringBuilder.Append("?");
                    continue;
                }

                char character = (char)short.Parse(rawHex, NumberStyles.AllowHexSpecifier);
                patternStringBuilder.Append(character);
                maskStringBuilder.Append("x");
            }

            return FindPatternBmh(patternStringBuilder.ToString(), maskStringBuilder.ToString(), startAddress);
        }

        public static IntPtr FindPatternBmh(string pattern, string mask, IntPtr startAddress = default)
        {
            unsafe
            {
                byte* address = (startAddress == IntPtr.Zero ? FindPatternBmhInternal(pattern, mask) : FindPatternBmhInternal(pattern, mask, startAddress));
                return address == null ? IntPtr.Zero : new IntPtr(address);
            }
        }
    }
}
