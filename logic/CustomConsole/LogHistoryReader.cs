using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fork.Logic.CustomConsole;

/// <summary>
/// Reads a server log (latest.log) backwards, in small chunks, so the console only
/// ever holds the newest lines in memory and older history is pulled from disk when
/// the user scrolls back to it. Nothing here reads a whole file.
/// </summary>
public static class LogHistoryReader
{
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Yields (line, byte offset of the line's first byte) from <paramref name="endOffset"/>
    /// backwards, newest first. <paramref name="endOffset"/> must be a line boundary or the
    /// end of the file. A final empty segment (file ends in a newline) is not yielded.
    /// </summary>
    public static IEnumerable<(string Line, long Start)> ReadBackward(string path, long endOffset)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long chunkEnd = Math.Min(Math.Max(endOffset, 0), fs.Length);
        byte[] carry = Array.Empty<byte>(); // bytes after the first newline seen so far (one partial line)
        long carryStart = chunkEnd; // file offset of carry[0]
        bool first = true;

        while (chunkEnd > 0)
        {
            int toRead = (int)Math.Min(ChunkSize, chunkEnd);
            long chunkStart = chunkEnd - toRead;
            byte[] data = new byte[toRead + carry.Length];
            fs.Seek(chunkStart, SeekOrigin.Begin);
            int got = 0;
            while (got < toRead)
            {
                int n = fs.Read(data, got, toRead - got);
                if (n <= 0) break;
                got += n;
            }

            if (got < toRead) yield break; // file shrank under us
            Buffer.BlockCopy(carry, 0, data, toRead, carry.Length);

            int segEnd = data.Length; // exclusive end of the segment being cut, within data
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (data[i] != (byte)'\n') continue;
                int segStart = i + 1;
                if (!(first && segStart == segEnd))
                    yield return (Decode(data, segStart, segEnd), chunkStart + segStart);
                first = false;
                segEnd = i; // the next (older) line ends before this newline
            }

            first = false;
            carry = new byte[segEnd];
            Buffer.BlockCopy(data, 0, carry, 0, segEnd);
            carryStart = chunkStart;
            chunkEnd = chunkStart;
        }

        // Whatever remains at the very beginning of the file is the oldest line.
        if (carry.Length > 0)
            yield return (Decode(carry, 0, carry.Length), carryStart);
    }

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of the file, oldest first. Also returns the
    /// offset the read ended at (the file length when it was opened) and the offset of the
    /// first returned line.
    /// </summary>
    public static List<string> ReadLastLines(string path, int maxLines, out long endOffset, out long startOffset)
    {
        endOffset = new FileInfo(path).Length;
        return ReadLinesBefore(path, endOffset, maxLines, out startOffset);
    }

    /// <summary>
    /// Up to <paramref name="maxLines"/> lines that end at <paramref name="beforeOffset"/>,
    /// oldest first. <paramref name="startOffset"/> is where the first returned line begins
    /// (0 when the beginning of the file was reached).
    /// </summary>
    public static List<string> ReadLinesBefore(string path, long beforeOffset, int maxLines,
        out long startOffset)
    {
        List<string> lines = new(Math.Min(maxLines, 4096));
        startOffset = beforeOffset;
        foreach ((string line, long start) in ReadBackward(path, beforeOffset))
        {
            lines.Add(line);
            startOffset = start;
            if (lines.Count >= maxLines) break;
        }

        lines.Reverse();
        return lines;
    }

    /// <summary>
    /// Finds where the console's oldest in-memory lines sit in the file, scanning back from
    /// the end of the file at most <paramref name="maxScanBytes"/>. Tries the oldest few
    /// lines as a consecutive run first, then single lines, because the console can hold
    /// Fork's own banners that never appear in the log. Returns the start offset of the
    /// matched line.
    /// </summary>
    public static bool TryLocate(string path, IReadOnlyList<string> oldestFirst, long maxScanBytes,
        out long startOffset)
    {
        startOffset = 0;
        if (oldestFirst.Count == 0) return false;

        long fileLength = new FileInfo(path).Length;
        long floor = Math.Max(0, fileLength - maxScanBytes);
        int candidates = Math.Min(oldestFirst.Count, 20);
        string[] keys = new string[candidates];
        for (int k = 0; k < candidates; k++) keys[k] = oldestFirst[k].TrimEnd('\r', '\n');

        // Scan newest -> oldest. A hit on keys[0] (the true oldest in-memory line) is final.
        // A hit on a later key is only a fallback (Fork banners at the front of the console
        // never appear in the log); the scan keeps going in case keys[0] turns up further back.
        List<string> seen = new(); // seen[j] is older than seen[j-1]
        int bestK = int.MaxValue;
        long bestStart = 0;
        foreach ((string line, long start) in ReadBackward(path, fileLength))
        {
            seen.Add(line);
            int j = seen.Count - 1;
            for (int k = 0; k < candidates && k < bestK; k++)
            {
                if (keys[k] != line) continue;
                // Confirm with the line after it, which was seen just before (seen[j-1]).
                if (k + 1 < candidates && j > 0 && keys[k + 1] != seen[j - 1]) continue;
                if (k == 0)
                {
                    startOffset = start;
                    return true;
                }

                bestK = k;
                bestStart = start;
                break;
            }

            if (start < floor) break;
        }

        if (bestK == int.MaxValue) return false;
        startOffset = bestStart;
        return true;
    }

    private static string Decode(byte[] data, int start, int end)
    {
        if (end > start && data[end - 1] == (byte)'\r') end--;
        return Encoding.UTF8.GetString(data, start, end - start);
    }
}
