using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VBFTool.VirtuosBigFile
{
    internal class VirtuosBigFileReader
    {
        private static readonly MD5 Md5 = MD5.Create();
        private ushort[] _blockList;
        private uint[] _blockListStarts;
        private string[] _fileNameMd5S;
        private ulong[] _fileNameOffsets;
        private Dictionary<string, ulong> _md5ToIndex;
        private ulong[] _originalSizes;
        private ulong[] _startOffsets;

        public VirtuosBigFileReader(string path)
        {
            FilePath = path;
        }

        public string FilePath { get; }
        public ulong FileCount { get; private set; }
        public string[] FileList { get; private set; }

        private static ushort ReadUInt16(Stream fs)
        {
            var buffer = new byte[2];
            fs.Read(buffer, 0, 2);
            return BitConverter.ToUInt16(buffer, 0);
        }

        private static uint ReadUInt32(Stream fs)
        {
            var buffer = new byte[4];
            fs.Read(buffer, 0, 4);
            return BitConverter.ToUInt32(buffer, 0);
        }

        private static ulong ReadUInt64(Stream fs)
        {
            var buffer = new byte[8];
            fs.Read(buffer, 0, 8);
            return BitConverter.ToUInt64(buffer, 0);
        }

        private static string ReadMd5Hash(Stream fs)
        {
            var buffer = new byte[16];
            fs.Read(buffer, 0, 16);
            return ByteArrayToHex(buffer);
        }

        private static string ByteArrayToHex(byte[] buffer)
        {
            var stringBuilder = new StringBuilder();
            foreach (var num in buffer)
                stringBuilder.Append(num.ToString("X02"));
            return stringBuilder.ToString();
        }

        public void Load()
        {
            using (var fs = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (ReadUInt32(fs) != 0x4B595253) // Check Header
                    throw new VirtuosBigFileException("Invalid Header");

                var headerLength = ReadUInt32(fs);
                FileCount = ReadUInt64(fs);

                _fileNameMd5S = new string[FileCount];
                _fileNameOffsets = new ulong[FileCount];
                _blockListStarts = new uint[FileCount];
                _originalSizes = new ulong[FileCount];
                _startOffsets = new ulong[FileCount];
                _md5ToIndex = new Dictionary<string, ulong>();

                for (ulong index = 0; index < FileCount; ++index)
                {
                    _fileNameMd5S[index] = ReadMd5Hash(fs);
                    _md5ToIndex.Add(_fileNameMd5S[index], index);
                }

                for (ulong index = 0; index < FileCount; ++index)
                {
                    _blockListStarts[index] = ReadUInt32(fs);
                    var num3 = ReadUInt32(fs);
                    _originalSizes[index] = ReadUInt64(fs);
                    _startOffsets[index] = ReadUInt64(fs);
                    _fileNameOffsets[index] = ReadUInt64(fs);
                }

                var stringTableSize = ReadUInt32(fs);
                var stringTable = new byte[stringTableSize - 4];
                fs.Read(stringTable, 0, (int) stringTableSize - 4);

                // Convert string table bytes to string, split into individual file names
                FileList = Encoding.UTF8.GetString(stringTable).Trim('\0').Split('\0');
                if ((ulong) FileList.LongLength != FileCount)
                    throw new VirtuosBigFileException("File list count does not match total files!");

                uint blockCount = 0;
                foreach (var originalSize in _originalSizes)
                {
                    blockCount += (uint) (originalSize / 0x10000);
                    if (originalSize % 0x10000 != 0)
                        ++blockCount;
                }

                _blockList = new ushort[blockCount];
                for (var index = 0; index < blockCount; ++index)
                    _blockList[index] = ReadUInt16(fs);

                fs.Seek(0, SeekOrigin.Begin);

                var header = new byte[headerLength];
                fs.Read(header, 0, (int) headerLength);

                var headerHash = new byte[16];
                fs.Seek(-16, SeekOrigin.End);
                fs.Read(headerHash, 0, 16);
                fs.Close();

                if (!Md5.ComputeHash(header).SequenceEqual(headerHash))
                    throw new VirtuosBigFileException("File Invalid");
            }
        }

        public void GetFileContents(string path, Stream outputStream)
        {
            var md5 = Md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLower()));

            ulong fileIndex;
            if (!_md5ToIndex.TryGetValue(ByteArrayToHex(md5), out fileIndex))
                return;

            var blockCount = (int) (_originalSizes[fileIndex] / 0x10000);
            var blockRemainder = (int) (_originalSizes[fileIndex] % 0x10000);
            if (blockRemainder != 0)
                ++blockCount;
            else
                blockRemainder = 0x10000;

            using (var fileStream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fileStream.Seek((long) _startOffsets[fileIndex], SeekOrigin.Begin);
                for (var blockIndex = 0; blockIndex < blockCount; ++blockIndex)
                {
                    int blockLength = _blockList[_blockListStarts[fileIndex] + blockIndex];
                    if (blockLength == 0)
                        blockLength = 0x10000;

                    var compressedBuffer = new byte[blockLength];
                    fileStream.Read(compressedBuffer, 0, blockLength);
                    var decBlockSize = blockIndex != blockCount - 1 ? 0x10000 : blockRemainder;

                    byte[] decompressedBuffer;
                    if (blockLength != 0x10000)
                    {
                        if (blockIndex == blockCount - 1 && blockLength == blockRemainder)
                            goto MoveUncompressedData;

                        try
                        {
                            decompressedBuffer = new byte[decBlockSize];
                            using (var deflateStream = new DeflateStream(
                                new MemoryStream(compressedBuffer, 2, blockLength - 2), CompressionMode.Decompress))
                            {
                                deflateStream.Read(decompressedBuffer, 0, decBlockSize);
                            }

                            goto WriteBuffer;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Exception extracting file: {path}");
                            throw new VirtuosBigFileException(ex.Message);
                        }
                    }

                    MoveUncompressedData:
                    decompressedBuffer = compressedBuffer;

                    WriteBuffer:
                    outputStream.Write(decompressedBuffer, 0, decBlockSize);
                }
            }
        }
    }
}