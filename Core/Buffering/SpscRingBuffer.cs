namespace BtAudioMixer.Core.Buffering
{
    public sealed class SpscRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly object _gate = new();
        private int _writePosition;
        private int _readPosition;
        private int _availableBytes;

        public SpscRingBuffer(int capacityInBytes)
        {
            if (capacityInBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityInBytes), capacityInBytes, "Ring buffer capacity must be positive.");
            }

            _buffer = new byte[capacityInBytes];
        }

        public int Write(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (_gate)
            {
                int freeBytes = _buffer.Length - _availableBytes;

                if (count > freeBytes)
                {
                    int bytesToDrop = count - freeBytes;
                    AdvanceReadPositionForOverflow(bytesToDrop);
                }

                WriteInternal(data, offset, count);
                return count;
            }
        }

        public int Read(byte[] destination, int offset, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            lock (_gate)
            {
                int bytesToRead = Math.Min(count, _availableBytes);
                int totalBytesRead = 0;

                while (totalBytesRead < bytesToRead)
                {
                    int contiguousBytes = Math.Min(bytesToRead - totalBytesRead, _buffer.Length - _readPosition);
                    Array.Copy(_buffer, _readPosition, destination, offset + totalBytesRead, contiguousBytes);

                    _readPosition = (_readPosition + contiguousBytes) % _buffer.Length;
                    totalBytesRead += contiguousBytes;
                }

                _availableBytes -= totalBytesRead;
                return totalBytesRead;
            }
        }

        private void WriteInternal(byte[] data, int offset, int count)
        {
            int totalBytesWritten = 0;

            while (totalBytesWritten < count)
            {
                int contiguousBytes = Math.Min(count - totalBytesWritten, _buffer.Length - _writePosition);
                Array.Copy(data, offset + totalBytesWritten, _buffer, _writePosition, contiguousBytes);

                _writePosition = (_writePosition + contiguousBytes) % _buffer.Length;
                totalBytesWritten += contiguousBytes;
            }

            _availableBytes += totalBytesWritten;
        }

        private void AdvanceReadPositionForOverflow(int bytesToDrop)
        {
            bytesToDrop = Math.Min(bytesToDrop, _availableBytes);
            _readPosition = (_readPosition + bytesToDrop) % _buffer.Length;
            _availableBytes -= bytesToDrop;
        }
    }
}
