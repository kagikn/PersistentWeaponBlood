#if SHVDN
using GTA;
using GTA.Native;
#endif
#if RPH
using Rage;
using Rage.Native;
#endif

using System;
using System.Text;

namespace PersistentWeaponBlood
{
    internal static class TheFeed
    {
        /// <summary>
        /// Posts a ticker to the feed.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="isImportant">Specifies whether the ticker important. Will flash if true.</param>
        /// <param name="cacheMessage">Specifies whether the feed will cache the ticker and users can view the ticker in the pause menu.</param>
        internal static void PostTickerToTheFeed(string text, bool isImportant, bool cacheMessage = true)
        {
            var textByteLength = Encoding.UTF8.GetByteCount(text);
            if (textByteLength > 99)
            {
                PostTickerWithLongMessageToTheFeed(text, isImportant, cacheMessage);
            }
            else
            {
                PostTickerWithShortMessageToTheFeed(text, isImportant, cacheMessage);
            }
        }

        private static void PostTickerWithShortMessageToTheFeed(string text, bool isImportant, bool cacheMessage = true)
        {
#if SHVDN
            Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
            Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, isImportant, cacheMessage);
#endif
#if RPH
            NativeFunction.Natives.BEGIN_TEXT_COMMAND_THEFEED_POST("STRING");
            NativeFunction.Natives.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME(text);
            NativeFunction.Natives.END_TEXT_COMMAND_THEFEED_POST_TICKER(isImportant, cacheMessage);
#endif
        }

        private static void PostTickerWithLongMessageToTheFeed(string text, bool isImportant, bool cacheMessage = true)
        {
#if SHVDN
            Function.Call(Hash.BEGIN_TEXT_COMMAND_THEFEED_POST, "CELL_EMAIL_BCON");
            PushLongString(text);
            Function.Call(Hash.END_TEXT_COMMAND_THEFEED_POST_TICKER, isImportant, cacheMessage);
#endif
#if RPH
            NativeFunction.Natives.BEGIN_TEXT_COMMAND_THEFEED_POST("CELL_EMAIL_BCON");
            PushLongString(text);
            NativeFunction.Natives.END_TEXT_COMMAND_THEFEED_POST_TICKER(isImportant, cacheMessage);
#endif
        }

        private static void PushLongString(string str, int maxLengthUtf8 = 99)
        {
            PushLongString(str, PushStringInternal, maxLengthUtf8);
        }

        private static void PushStringInternal(string str)
        {
#if SHVDN
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, str);
#endif
#if RPH
            NativeFunction.Natives.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME(str);
#endif
        }

        private static void PushLongString(string str, Action<string> action, int maxLengthUtf8 = 99)
        {
            int startPos = 0;
            int currentPos = 0;
            int currentUtf8StrLength = 0;

            while (currentPos < str.Length)
            {
                int codePointSize = 0;

                // Calculate the UTF-8 code point size of the current character
                var chr = str[currentPos];
                if (chr < 0x80)
                {
                    codePointSize = 1;
                }
                else if (chr < 0x800)
                {
                    codePointSize = 2;
                }
                else if (chr < 0x10000)
                {
                    codePointSize = 3;
                }
                else
                {
                    #region Surrogate check
                    const int LowSurrogateStart = 0xD800;
                    const int HighSurrogateStart = 0xD800;

                    var temp1 = (int)chr - HighSurrogateStart;
                    if (temp1 >= 0 && temp1 <= 0x7ff)
                    {
                        // Found a high surrogate
                        if (currentPos < str.Length - 1)
                        {
                            var temp2 = str[currentPos + 1] - LowSurrogateStart;
                            if (temp2 >= 0 && temp2 <= 0x3ff)
                            {
                                // Found a low surrogate
                                codePointSize = 4;
                            }
                        }
                    }
                    #endregion
                }

                if (currentUtf8StrLength + codePointSize > maxLengthUtf8)
                {
                    action(str.Substring(startPos, currentPos - startPos));

                    startPos = currentPos;
                    currentUtf8StrLength = 0;
                }
                else
                {
                    currentPos++;
                    currentUtf8StrLength += codePointSize;
                }

                // Additional increment is needed for surrogate
                if (codePointSize == 4)
                {
                    currentPos++;
                }
            }

            if (startPos == 0)
                action(str);
            else
                action(str.Substring(startPos, str.Length - startPos));
        }
    }
}