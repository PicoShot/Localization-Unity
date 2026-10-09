using System;
using System.IO;

namespace PicoShot.Localization.Utils
{
    public class SectionStream : Stream
    {
        private Stream baseStream;
        private long start;
        private long length;
        private long position;

        public Stream BaseStream => baseStream;

        public SectionStream(Stream baseStream, long start, long length)
        {
            this.baseStream = baseStream;
            this.start = start;
            this.length = length;
            position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = (int)Math.Min(count, length - position);
            if (toRead <= 0)
                return 0;

            baseStream.Position = start + position;
            int read = baseStream.Read(buffer, offset, toRead);

            position += read;
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int toWrite = (int)Math.Min(count, length - position);
            if (toWrite <= 0)
                return;

            baseStream.Position = start + position;
            baseStream.Write(buffer, offset, toWrite);

            position += toWrite;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException()
            };

            position = newPos;
            return position;
        }

        public override long Position
        {
            get => position;
            set => position = value;
        }

        public override long Length => length;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => true;

        public override void Flush() => baseStream.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}