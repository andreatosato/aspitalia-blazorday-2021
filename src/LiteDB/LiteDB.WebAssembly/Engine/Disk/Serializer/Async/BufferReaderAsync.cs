﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Read multiple array segment as a single linear segment - Forward Only
    /// </summary>
    internal class BufferReaderAsync : IAsyncDisposable
    {
        private readonly IAsyncEnumerator<BufferSlice> _source;
        private readonly bool _utcDate;

        private BufferSlice _current;
        private int _currentPosition = 0; // position in _current
        private int _position = 0; // global position

        private bool _isEOF = false;

        /// <summary>
        /// Current global cursor position
        /// </summary>
        public int Position => _position;

        /// <summary>
        /// Indicate position are at end of last source array segment
        /// </summary>
        public bool IsEOF => _isEOF;

        /// <summary>
        /// Async ctor
        /// </summary>
        public static async Task<BufferReaderAsync> CreateAsync(IAsyncEnumerable<BufferSlice> source, bool utcDate = false)
        {
            var instance = new BufferReaderAsync(source, utcDate);
            await instance.InitializeAsync();
            return instance;
        }

        private BufferReaderAsync(IAsyncEnumerable<BufferSlice> source, bool utcDate = false)
        {
            _source = source.GetAsyncEnumerator();
            _utcDate = utcDate;
        }

        private async Task InitializeAsync()
        {
            var read = await _source.MoveNextAsync();
            if (!read) _isEOF = true;
            _current = _source.Current;
        }

        #region Basic Read

        /// <summary>
        /// Move forward in current segment. If array segment finishes, open next segment
        /// Returns true if moved to another segment - returns false if continues in the same segment
        /// </summary>
        private async Task<bool> MoveForward(int count)
        {
            // do not move forward if source finish
            if (_isEOF) return false;

            ENSURE(_currentPosition + count <= _current.Count, "forward is only for current segment");

            _currentPosition += count;
            _position += count;

            // request new source array if _current all consumed
            if (_currentPosition == _current.Count)
            {
                if (_source == null || await _source.MoveNextAsync() == false)
                {
                    _isEOF = true;
                }
                else
                {
                    _current = _source.Current;
                    _currentPosition = 0;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Read bytes from source and copy into buffer. Return how many bytes was read
        /// </summary>
        public async Task<int> Read(byte[] buffer, int offset, int count)
        {
            var bufferPosition = 0;

            while (bufferPosition < count)
            {
                var bytesLeft = _current.Count - _currentPosition;
                var bytesToCopy = Math.Min(count - bufferPosition, bytesLeft);

                // fill buffer
                if (buffer != null)
                {
                    Buffer.BlockCopy(_current.Array, 
                        _current.Offset + _currentPosition, 
                        buffer, 
                        offset + bufferPosition, 
                        bytesToCopy);
                }

                bufferPosition += bytesToCopy;

                // move position in current segment (and go to next segment if finish)
                await this.MoveForward(bytesToCopy);

                if (_isEOF) break;
            }

            ENSURE(count == bufferPosition, "current value must fit inside defined buffer");

            return bufferPosition;
        }

        /// <summary>
        /// Skip bytes (same as Read but with no array copy)
        /// </summary>
        public async Task<int> Skip(int count) => await this.Read(null, 0, count);

        /// <summary>
        /// Consume all data source until finish
        /// </summary>
        public async Task Consume()
        {
            if (_source != null)
            {
                while (await _source.MoveNextAsync())
                {
                }
            }
        }

        #endregion

        #region Read String

        /// <summary>
        /// Read string with fixed size
        /// </summary>
        public async Task<string> ReadString(int count)
        {
            string value;

            // if fits in current segment, use inner array - otherwise copy from multiples segments
            if (_currentPosition + count <= _current.Count)
            {
                value = Encoding.UTF8.GetString(_current.Array, _current.Offset + _currentPosition, count);

                await this.MoveForward(count);
            }
            else
            {
                // rent a buffer to be re-usable
                var buffer = BufferPool.Rent(count);

                await this.Read(buffer, 0, count);

                value = Encoding.UTF8.GetString(buffer, 0, count);

                BufferPool.Return(buffer);
            }

            return value;
        }

        /// <summary>
        /// Reading string until find \0 at end
        /// </summary>
        public async Task<string> ReadCString()
        {
            var singleSegment = await this.TryReadCStringCurrentSegment();

            // first try read CString in current segment
            if (singleSegment.done)
            {
                return singleSegment.value;
            }
            else
            {
                using (var mem = new MemoryStream())
                {
                    // copy all first segment 
                    var initialCount = _current.Count - _currentPosition;

                    mem.Write(_current.Array, _current.Offset + _currentPosition, initialCount);

                    await this.MoveForward(initialCount);

                    // and go to next segment
                    while (_current[_currentPosition] != 0x00 && _isEOF == false)
                    {
                        mem.WriteByte(_current[_currentPosition]);

                        await this.MoveForward(1);
                    }

                    await this.MoveForward(1); // +1 to '\0'

                    return Encoding.UTF8.GetString(mem.ToArray());
                }
            }
        }

        /// <summary>	
        /// Try read CString in current segment avoind read byte-to-byte over segments	
        /// </summary>	
        private async Task<(bool done, string value)> TryReadCStringCurrentSegment()
        {
            var pos = _currentPosition;
            var count = 0;
            while (pos < _current.Count)
            {
                if (_current[pos] == 0x00)
                {
                    var value = Encoding.UTF8.GetString(_current.Array, _current.Offset + _currentPosition, count);
                    await this.MoveForward(count + 1); // +1 means '\0'	
                    return (true, value);
                }
                else
                {
                    count++;
                    pos++;
                }
            }
            return (false, null);
        }

        #endregion

        #region Read Numbers

        private async Task<T> ReadNumber<T>(Func<byte[], int, T> convert, int size)
        {
            T value;

            // if fits in current segment, use inner array - otherwise copy from multiples segments
            if (_currentPosition + size <= _current.Count)
            {
                value = convert(_current.Array, _current.Offset + _currentPosition);

                await this.MoveForward(size);
            }
            else
            {
                var buffer = BufferPool.Rent(size);

                await this.Read(buffer, 0, size);

                value = convert(buffer, 0);

                BufferPool.Return(buffer);
            }

            return value;
        }

        public async Task<Int32> ReadInt32() => await this.ReadNumber(BitConverter.ToInt32, 4);
        public async Task<Int64> ReadInt64() => await this.ReadNumber(BitConverter.ToInt64, 8);
        public async Task<UInt32> ReadUInt32() => await this.ReadNumber(BitConverter.ToUInt32, 4);
        public async Task<Double> ReadDouble() => await this.ReadNumber(BitConverter.ToDouble, 8);

        public async Task<Decimal> ReadDecimal()
        {
            var a = await this.ReadInt32();
            var b = await this.ReadInt32();
            var c = await this.ReadInt32();
            var d = await this.ReadInt32();
            return new Decimal(new int[] { a, b, c, d });
        }

        #endregion

        #region Complex Types

        /// <summary>
        /// Read DateTime as UTC ticks (not BSON format)
        /// </summary>
        public async Task<DateTime> ReadDateTime()
        {
            var date = new DateTime(await this.ReadInt64(), DateTimeKind.Utc);

            return _utcDate ? date.ToLocalTime() : date;
        }

        /// <summary>
        /// Read Guid as 16 bytes array
        /// </summary>
        public async Task<Guid> ReadGuid()
        {
            Guid value;

            if (_currentPosition + 16 <= _current.Count)
            {
                value = _current.ReadGuid(_currentPosition);

                await this.MoveForward(16);
            }
            else
            {
                // can't use _tempoBuffer because Guid validate 16 bytes array length
                value = new Guid(await this.ReadBytes(16));
            }

            return value;
        }

        /// <summary>
        /// Write ObjectId as 12 bytes array
        /// </summary>
        public async Task<ObjectId> ReadObjectId()
        {
            ObjectId value;

            if (_currentPosition + 12 <= _current.Count)
            {
                value = new ObjectId(_current.Array, _current.Offset + _currentPosition);

                await this.MoveForward(12);
            }
            else
            {
                var buffer = BufferPool.Rent(12);

                await this.Read(buffer, 0, 12);

                value = new ObjectId(buffer, 0);

                BufferPool.Return(buffer);
            }

            return value;
        }

        /// <summary>
        /// Write a boolean as 1 byte (0 or 1)
        /// </summary>
        public async Task<bool> ReadBoolean()
        {
            var value = _current[_currentPosition] != 0;
            await this.MoveForward(1);
            return value;
        }

        /// <summary>
        /// Write single byte
        /// </summary>
        public async Task<byte> ReadByte()
        {
            var value = _current[_currentPosition];
            await this.MoveForward(1);
            return value;
        }

        /// <summary>
        /// Write PageAddress as PageID, Index
        /// </summary>
        internal async Task<PageAddress> ReadPageAddress()
        {
            return new PageAddress(await this.ReadUInt32(), await this.ReadByte());
        }

        /// <summary>
        /// Read byte array - not great because need create new array instance
        /// </summary>
        public async Task<byte[]> ReadBytes(int count)
        {
            var buffer = new byte[count];
            await this.Read(buffer, 0, count);
            return buffer;
        }

        /// <summary>
        /// Read single IndexKey (BsonValue) from buffer. Use +1 length only for string/binary
        /// </summary>
        public async Task<BsonValue> ReadIndexKey()
        {
            var type = (BsonType)await this.ReadByte();

            switch (type)
            {
                case BsonType.Null: return BsonValue.Null;

                case BsonType.Int32: return await this.ReadInt32();
                case BsonType.Int64: return await this.ReadInt64();
                case BsonType.Double: return await this.ReadDouble();
                case BsonType.Decimal: return await this.ReadDecimal();
                
                // Use +1 byte only for length
                case BsonType.String: return await this.ReadString(await this.ReadByte());

                case BsonType.Document: return await this.ReadDocument(null);
                case BsonType.Array: return await this.ReadArray();

                // Use +1 byte only for length
                case BsonType.Binary: return await this.ReadBytes(await this.ReadByte());
                case BsonType.ObjectId: return await this.ReadObjectId();
                case BsonType.Guid: return await this.ReadGuid();

                case BsonType.Boolean: return await this.ReadBoolean();
                case BsonType.DateTime: return await this.ReadDateTime();

                case BsonType.MinValue: return BsonValue.MinValue;
                case BsonType.MaxValue: return BsonValue.MaxValue;

                default: throw new NotImplementedException();
            }
        }

        #endregion

        #region BsonDocument as SPECS

        /// <summary>
        /// Read a BsonDocument from reader
        /// </summary>
        public async Task<BsonDocument> ReadDocument(HashSet<string> fields = null)
        {
            var length = await this.ReadInt32();
            var end = _position + length - 5;
            var remaining = fields == null || fields.Count == 0 ? null : new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);

            var doc = new BsonDocument();

            while (_position < end && (remaining == null || remaining?.Count > 0))
            {
                var elem = await this.ReadElement(remaining);

                // null value means are not selected field
                if (elem.value != null)
                {
                    doc[elem.name] = elem.value;

                    // remove from remaining fields
                    remaining?.Remove(elem.name);
                }
            }

            await this.MoveForward(1); // skip \0

            return doc;
        }

        /// <summary>
        /// Read an BsonArray from reader
        /// </summary>
        public async Task<BsonArray> ReadArray()
        {
            var length = await this.ReadInt32();
            var end = _position + length - 5;
            var arr = new BsonArray();

            while (_position < end)
            {
                var elem = await this.ReadElement(null);

                arr.Add(elem.value);
            }

            await this.MoveForward(1); // skip \0

            return arr;
        }

        /// <summary>
        /// Reads an element (key-value) from an reader
        /// </summary>
        private async Task<(BsonValue value, string name)> ReadElement(HashSet<string> remaining)
        {
            var type = await this.ReadByte();
            var name = await this.ReadCString();

            // check if need skip this element
            if (remaining != null && !remaining.Contains(name))
            {
                // define skip length according type
                var length =
                    (type == 0x0A || type == 0xFF || type == 0x7F) ? 0 : // Null, MinValue, MaxValue
                    (type == 0x08) ? 1 : // Boolean
                    (type == 0x10) ? 4 : // Int
                    (type == 0x01 || type == 0x12 || type == 0x09) ? 8 : // Double, Int64, DateTime
                    (type == 0x07) ? 12 : // ObjectId
                    (type == 0x13) ? 16 : // Decimal
                    (type == 0x02) ? await this.ReadInt32() : // String
                    (type == 0x05) ? await this.ReadInt32() + 1 : // Binary (+1 for subtype)
                    (type == 0x03 || type == 0x04) ? await this.ReadInt32() - 4 : 0; // Document, Array (-4 to Length + zero)

                if (length > 0)
                {
                    await this.Skip(length);
                }

                return (null, name);
            }

            if (type == 0x01) // Double
            {
                return (await this.ReadDouble(), name);
            }
            else if (type == 0x02) // String
            {
                var length = await this.ReadInt32();
                var value = await this.ReadString(length - 1);
                await this.MoveForward(1); // read '\0'
                return (value, name);
            }
            else if (type == 0x03) // Document
            {
                return (await this.ReadDocument(), name);
            }
            else if (type == 0x04) // Array
            {
                return (await this.ReadArray(), name);
            }
            else if (type == 0x05) // Binary
            {
                var length = await this.ReadInt32();
                var subType = await this.ReadByte();
                var bytes = await this.ReadBytes(length);

                switch (subType)
                {
                    case 0x00: return (bytes, name);
                    case 0x04: return (new Guid(bytes), name);
                }
            }
            else if (type == 0x07) // ObjectId
            {
                return (await this.ReadObjectId(), name);
            }
            else if (type == 0x08) // Boolean
            {
                return (await this.ReadBoolean(), name);
            }
            else if (type == 0x09) // DateTime
            {
                var ts = await this.ReadInt64();

                // catch specific values for MaxValue / MinValue #19
                if (ts == 253402300800000) return (DateTime.MaxValue, name);
                if (ts == -62135596800000) return (DateTime.MinValue, name);

                var date = BsonValue.UnixEpoch.AddMilliseconds(ts);

                return (_utcDate ? date : date.ToLocalTime(), name);
            }
            else if (type == 0x0A) // Null
            {
                return (BsonValue.Null, name);
            }
            else if (type == 0x10) // Int32
            {
                return (await this.ReadInt32(), name);
            }
            else if (type == 0x12) // Int64
            {
                return (await this.ReadInt64(), name);
            }
            else if (type == 0x13) // Decimal
            {
                return (await this.ReadDecimal(), name);
            }
            else if (type == 0xFF) // MinKey
            {
                return (BsonValue.MinValue, name);
            }
            else if (type == 0x7F) // MaxKey
            {
                return (BsonValue.MaxValue, name);
            }

            throw new NotSupportedException("BSON type not supported");
        }

        #endregion

        public ValueTask DisposeAsync()
        {
            return _source?.DisposeAsync() ?? default;
        }
    }
}