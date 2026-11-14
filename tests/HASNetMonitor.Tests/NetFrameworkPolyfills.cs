#if NETFRAMEWORK
// .NET Framework has no System.Index or System.Range, which the C# compiler needs for
// the range and index operators (`text[1..]`, `text[^1]`) that the test sources use.
// This is the standard polyfill from the C# language documentation, so the same test
// sources compile unchanged on both targets.
using System;
using System.Globalization;

namespace System
{
    internal readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Indexes cannot be negative.");
            }

            _value = fromEnd ? ~value : value;
        }

        private Index(int value) => _value = value;

        public static Index Start => new(0);

        public static Index End => new(~0);

        public static implicit operator Index(int value) => FromStart(value);

        public bool IsFromEnd => _value < 0;

        public int Value => _value < 0 ? ~_value : _value;

        public static Index FromStart(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Indexes cannot be negative.");
            }

            return new Index(value);
        }

        public static Index FromEnd(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Indexes cannot be negative.");
            }

            return new Index(~value);
        }

        public int GetOffset(int length)
        {
            int offset = _value;
            if (IsFromEnd)
            {
                offset += length + 1;
            }

            return offset;
        }

        public bool Equals(Index other) => _value == other._value;

        public override bool Equals(object? value) => value is Index index && Equals(index);

        public override int GetHashCode() => _value;

        public override string ToString() =>
            IsFromEnd ? "^" + Value.ToString(CultureInfo.InvariantCulture)
                      : Value.ToString(CultureInfo.InvariantCulture);
    }

    internal readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }

        public Index End { get; }

        public static Range StartAt(Index start) => new(start, Index.End);

        public static Range EndAt(Index end) => new(Index.Start, end);

        public static Range All => new(Index.Start, Index.End);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);

            if ((uint)end > (uint)length || (uint)start > (uint)end)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return (start, end - start);
        }

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

        public override bool Equals(object? value) => value is Range range && Equals(range);

        public override int GetHashCode() => (Start.GetHashCode() * 397) ^ End.GetHashCode();

        public override string ToString() => Start + ".." + End;
    }
}
#endif
