using System.Security.Cryptography;

namespace ROROROblox.Core.Notify;

/// <summary>
/// Generates the ntfy topic. An ntfy topic is the ENTIRE security model — it grants both
/// subscribe and publish, so a guessable topic means strangers reading alert traffic and
/// spoofing notifications under RoRoRo's name. Users never type one by hand; they get 128 bits
/// from the OS CSPRNG, base32-encoded so it survives being read aloud over a shoulder-surfed
/// phone screen (no ambiguous casing, no symbols the ntfy app rejects).
/// </summary>
public static class NtfyTopicGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>"rororo-" plus 26 base32 chars (130 random bits, 128 used).</summary>
    public static string NewTopic()
    {
        var bytes = RandomNumberGenerator.GetBytes(17); // 136 bits; 26 chars consume 130
        var chars = new char[26];
        var bitBuffer = 0;
        var bitCount = 0;
        var byteIndex = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            while (bitCount < 5)
            {
                bitBuffer = (bitBuffer << 8) | bytes[byteIndex++];
                bitCount += 8;
            }

            bitCount -= 5;
            chars[i] = Alphabet[(bitBuffer >> bitCount) & 0b11111];
        }

        return $"rororo-{new string(chars)}";
    }
}
