using AuroraLib.Compression.Exceptions;
using AuroraLib.Core;
using AuroraLib.Core.Interfaces;
using AuroraLib.Core.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AuroraLib.Compression.Algorithms
{
    /// <summary>
    /// Nintendo LZ77 extension Header from LZ10 algorithm
    /// </summary>
    public sealed class LZ77 : LZ10, IHasIdentifier
    {
        /// <inheritdoc/>
        public IIdentifier Identifier => _identifier;

        private static readonly Identifier32 _identifier = new Identifier32("LZ77".AsSpan());

        /// <summary>
        /// Specifies the type of compression used.
        /// </summary>
        public CompressionType Type = CompressionType.LZ10;

        /// <summary>
        /// Defines the size of the chunks when <see cref="CompressionType.ChunkLZ10"/> is used. (4 KB by default).
        /// </summary>
        public UInt24 ChunkSize = 0x1000;

        /// <inheritdoc/>
        public override bool IsMatch(Stream stream, ReadOnlySpan<char> extension = default)
            => IsMatchStatic(stream, extension);

        /// <inheritdoc cref="IsMatch(Stream, ReadOnlySpan{char})"/>
        public static bool IsMatchStatic(Stream stream, ReadOnlySpan<char> extension = default)
            => stream.Position + 0x8 < stream.Length && stream.Peek(s => s.Match(_identifier) && Enum.IsDefined(typeof(CompressionType), s.Read<CompressionType>()));

        /// <inheritdoc/>
        public override void Compress(ReadOnlySpan<byte> source, Stream destination, CompressionLevel level = CompressionLevel.Optimal)
        {
            destination.Write(_identifier);

            if (Type == CompressionType.ChunkLZ10 && ChunkSize < source.Length)
            {
                if (source.Length > 0xffffff)
                {
                    new ArgumentOutOfRangeException($"{nameof(LZ77)} compression type {nameof(CompressionType.ChunkLZ10)} does not support files larger than 0xffffff.");
                }

                destination.Write((byte)Type | (source.Length << 8));
                int segments = (source.Length + ChunkSize - 1) / ChunkSize;
                ushort[] segmentEndOffsets = new ushort[segments];
                destination.Write<ushort>(segmentEndOffsets); // Placeholder

                long headerEndOffset = destination.Position;
                for (int i = 0; i < segmentEndOffsets.Length; i++)
                {
                    int segmentStart = i * ChunkSize;
                    int segmentSize = Math.Min(ChunkSize, source.Length - segmentStart);
                    base.Compress(source.Slice(segmentStart, segmentSize), destination, level);

                    long segmentEndOffset = destination.Position - headerEndOffset;
                    if (segmentEndOffset > 0xffff)
                    {
                        throw new ArgumentOutOfRangeException($"{nameof(LZ77)} chunks too large to process.");
                    }
                    segmentEndOffsets[i] = (ushort)segmentEndOffset;
                }

                destination.At(headerEndOffset - (segments * 2), s => s.Write<ushort>(segmentEndOffsets));
            }
            else
            {
                base.Compress(source, destination, level);
            }
        }

        /// <inheritdoc/>
        public override void Decompress(Stream source, Stream destination)
        {
            source.MatchThrow(_identifier);
            CompressionType type = source.Read<CompressionType>();

            int decompressedSize = source.ReadUInt24();
            if (decompressedSize == 0) decompressedSize = (int)source.ReadUInt32();

            long destinationEndPosition = destination.Position + decompressedSize;
            switch (type)
            {
                case CompressionType.LZ10:
                    DecompressHeaderless(source, destination, decompressedSize);
                    break;
                case CompressionType.ChunkLZ10:
                    List<ushort> segmentEndOffsets = new List<ushort>();
                    do
                    {
                        segmentEndOffsets.Add(source.ReadUInt16());
                    } while (segmentEndOffsets.Last() + source.Position != source.Length);
                    long headerEndOffset = source.Position;

                    //Unpack the individual chunks
                    for (int i = 0; i < segmentEndOffsets.Count; i++)
                    {
                        base.Decompress(source, destination);
                        source.Seek(segmentEndOffsets[i] + headerEndOffset, SeekOrigin.Begin);
                    }
                    break;
                default:
                    throw new NotSupportedException($"{nameof(LZ77)} compression type {type} not supported.");
            }

            if (destination.Position > destinationEndPosition)
            {
                throw new DecompressedSizeException(decompressedSize, destination.Position - (destinationEndPosition - decompressedSize));
            }
        }

        public enum CompressionType : byte
        {
            LZ10 = 0x10,
            ChunkLZ10 = 0xf7
        }

    }
}
