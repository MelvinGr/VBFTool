using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VBFTool.VirtuosBigFile
{
    internal class VirtuosBigFileReader : IDisposable
    {
        private static readonly MD5 Md5 = MD5.Create();
        private ushort[] _blockList;
        private uint[] _blockListStarts;
        private string[] _fileNameMd5S;
        private ulong[] _fileNameOffsets;
        private FileStream _fileStream;
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

        public void Dispose() => Close();
        public void Close() => _fileStream?.Dispose();

        private ushort ReadUInt16()
        {
            var buffer = new byte[2];
            _fileStream.Read(buffer, 0, 2);
            return BitConverter.ToUInt16(buffer, 0);
        }

        private uint ReadUInt32()
        {
            var buffer = new byte[4];
            _fileStream.Read(buffer, 0, 4);
            return BitConverter.ToUInt32(buffer, 0);
        }

        private ulong ReadUInt64()
        {
            var buffer = new byte[8];
            _fileStream.Read(buffer, 0, 8);
            return BitConverter.ToUInt64(buffer, 0);
        }

        private string ReadMd5Hash()
        {
            var buffer = new byte[16];
            _fileStream.Read(buffer, 0, 16);
            return ByteArrayToHex(buffer);
        }

        private static string ByteArrayToHex(byte[] buffer)
        {
            var stringBuilder = new StringBuilder();
            foreach (var num in buffer)
                stringBuilder.Append(num.ToString("X02"));
            return stringBuilder.ToString();
        }

        public void Open()
        {
            _fileStream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (ReadUInt32() != 0x4B595253) // Check Header
                throw new VirtuosBigFileException("Invalid Header");

            var headerLength = ReadUInt32();
            FileCount = ReadUInt64();

            _fileNameMd5S = new string[FileCount];
            _fileNameOffsets = new ulong[FileCount];
            _blockListStarts = new uint[FileCount];
            _originalSizes = new ulong[FileCount];
            _startOffsets = new ulong[FileCount];
            _md5ToIndex = new Dictionary<string, ulong>();

            for (ulong index = 0; index < FileCount; ++index)
            {
                _fileNameMd5S[index] = ReadMd5Hash();
                _md5ToIndex.Add(_fileNameMd5S[index], index);
            }

            for (ulong index = 0; index < FileCount; ++index)
            {
                _blockListStarts[index] = ReadUInt32();
                var num3 = ReadUInt32();
                _originalSizes[index] = ReadUInt64();
                _startOffsets[index] = ReadUInt64();
                _fileNameOffsets[index] = ReadUInt64();
            }

            var stringTableSize = ReadUInt32();
            var stringTable = new byte[stringTableSize - 4];
            _fileStream.Read(stringTable, 0, (int)stringTableSize - 4);

            // Convert string table bytes to string, split into individual file names
            FileList = Encoding.UTF8.GetString(stringTable).Trim('\0').Split('\0');
            if ((ulong)FileList.LongLength != FileCount)
                throw new VirtuosBigFileException("File list count does not match total files!");

            uint blockCount = 0;
            foreach (var originalSize in _originalSizes)
            {
                blockCount += (uint)(originalSize / 0x10000);
                if (originalSize % 0x10000 != 0)
                    ++blockCount;
            }

            _blockList = new ushort[blockCount];
            for (var index = 0; index < blockCount; ++index)
                _blockList[index] = ReadUInt16();

            _fileStream.Seek(0, SeekOrigin.Begin);

            var header = new byte[headerLength];
            _fileStream.Read(header, 0, (int)headerLength);

            var headerHash = new byte[16];
            _fileStream.Seek(-16, SeekOrigin.End);
            _fileStream.Read(headerHash, 0, 16);

            if (!Md5.ComputeHash(header).SequenceEqual(headerHash))
                throw new VirtuosBigFileException("File Invalid");
        }

        public bool FileExists(string path)
        {
            var md5 = Md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLower()));
            return _md5ToIndex.ContainsKey(ByteArrayToHex(md5));
        }

        /*public Stream GetStreamAtFile(string path)
        {
            var md5 = Md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLower()));

            ulong fileIndex;
            if (!_md5ToIndex.TryGetValue(ByteArrayToHex(md5), out fileIndex))
                return null;

            var fileStream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fileStream.Seek((long)_startOffsets[fileIndex], SeekOrigin.Begin);
            return fileStream;
        }*/

        public void GetFileContents(string path, Stream outputStream, int maxBlocks = -1)
        {
            var md5 = Md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLower()));

            ulong fileIndex;
            if (!_md5ToIndex.TryGetValue(ByteArrayToHex(md5), out fileIndex))
                return;

            var blockCount = (int)(_originalSizes[fileIndex] / 0x10000);
            var blockRemainder = (int)(_originalSizes[fileIndex] % 0x10000);
            if (blockRemainder != 0)
                ++blockCount;
            else
                blockRemainder = 0x10000;

            if (maxBlocks != -1)
                blockCount = maxBlocks;

            _fileStream.Seek((long)_startOffsets[fileIndex], SeekOrigin.Begin);
            for (var blockIndex = 0; blockIndex < blockCount; ++blockIndex)
            {
                int blockLength = _blockList[_blockListStarts[fileIndex] + blockIndex];
                if (blockLength == 0)
                    blockLength = 0x10000;

                var compressedBuffer = new byte[blockLength];
                _fileStream.Read(compressedBuffer, 0, blockLength);
                var decBlockSize = blockIndex != blockCount - 1 ? 0x10000 : blockRemainder;

                if (blockLength == 0x10000)
                {
                    outputStream.Write(compressedBuffer, 0, decBlockSize);
                    continue;
                }

                if (blockIndex == blockCount - 1 && blockLength == blockRemainder) // last block
                    outputStream.Write(compressedBuffer, 0, decBlockSize);
                else
                {
                    var decompressedBuffer = new byte[decBlockSize];
                    using (var deflateStream = new DeflateStream(
                        new MemoryStream(compressedBuffer, 2, blockLength - 2), CompressionMode.Decompress))
                    {
                        deflateStream.Read(decompressedBuffer, 0, decBlockSize);
                        outputStream.Write(decompressedBuffer, 0, decBlockSize);
                    }
                }
            }
        }
    }
}