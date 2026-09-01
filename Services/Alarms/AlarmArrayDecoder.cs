using Opc.Ua;
using Small_square_cavity_coating_machine.Models.Alarms;
using System.Globalization;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public sealed record AlarmArraySample(IReadOnlyList<AlarmSignal> Signals, string Error);

public sealed class AlarmArrayDecoder
{
    private readonly (string Address, int Index)[] _points;
    public AlarmArrayDecoder(IReadOnlyList<AlarmDefinition> definitions)
    {
        if (definitions.Count == 0) throw new InvalidOperationException("EventDef没有报警定义");
        _points = definitions.Select(d => (d.Address, ParseIndex(d.Address))).ToArray();
    }

    public static int ParseIndex(string address)
    {
        const string prefix = "EQ_Alarm[";
        if (!address.StartsWith(prefix, StringComparison.Ordinal) || !address.EndsWith(']')
            || !int.TryParse(address.AsSpan(prefix.Length, address.Length - prefix.Length - 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            throw new InvalidOperationException($"报警地址不是有效的EQ_Alarm数组下标：{address}");
        return index;
    }

    public static bool? DecodeElement(object? value) => value switch
    {
        bool b => b,
        byte b when b <= 1 => b == 1,
        sbyte b when b is 0 or 1 => b == 1,
        short b when b is 0 or 1 => b == 1,
        ushort b when b <= 1 => b == 1,
        int b when b is 0 or 1 => b == 1,
        uint b when b <= 1 => b == 1,
        long b when b is 0 or 1 => b == 1,
        ulong b when b <= 1 => b == 1,
        _ => null
    };

    public AlarmArraySample Decode(DataValue value, DateTimeOffset observedAt)
    {
        var array = value.Value as Array;
        var valid = StatusCode.IsGood(value.StatusCode) && !value.StatusCode.Overflow
            && array is { Rank: 1 } && array.GetLowerBound(0) == 0
            && array.GetType().GetElementType() is { } type
            && (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort) || type == typeof(int)
                || type == typeof(uint) || type == typeof(long) || type == typeof(ulong));
        var timestamp = value.SourceTimestamp == DateTime.MinValue ? (DateTimeOffset?)null
            : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc));
        var signals = _points.Select(point =>
        {
            var active = valid && point.Index < array!.Length ? DecodeElement(array.GetValue(point.Index)) : null;
            return new AlarmSignal(point.Address, active, active.HasValue ? AlarmQuality.Good : AlarmQuality.Bad,
                observedAt, timestamp);
        }).ToArray();
        var invalid = signals.Count(s => s.Quality != AlarmQuality.Good);
        return new AlarmArraySample(signals, invalid == 0 ? string.Empty
            : !valid ? $"EQ_Alarm质量或数组类型无效（{value.StatusCode}），报警状态未确认"
            : $"EQ_Alarm长度{array!.Length}，需覆盖下标{_points.Max(p => p.Index)}；{invalid}个点越界或值不是0/1");
    }
}
