partial struct SYSTEMTIME
	: IEquatable<SYSTEMTIME>
{
	public bool Equals(SYSTEMTIME other) => this.wYear == other.wYear && this.wMonth == other.wMonth && this.wDayOfWeek == other.wDayOfWeek && this.wDay == other.wDay && this.wHour == other.wHour && this.wMinute == other.wMinute && this.wSecond == other.wSecond && this.wMilliseconds == other.wMilliseconds;
	public override bool Equals(object obj) => obj is SYSTEMTIME other && this.Equals(other);
	public override int GetHashCode() => (this.wYear, this.wMonth, this.wDayOfWeek, this.wDay, this.wHour, this.wMinute, this.wSecond, this.wMilliseconds).GetHashCode();
	public static bool operator ==(SYSTEMTIME d1, SYSTEMTIME d2) => d1.wYear == d2.wYear && d1.wMonth == d2.wMonth && d1.wDayOfWeek == d2.wDayOfWeek && d1.wDay == d2.wDay && d1.wHour == d2.wHour && d1.wMinute == d2.wMinute && d1.wSecond == d2.wSecond && d1.wMilliseconds == d2.wMilliseconds;
	public static bool operator !=(SYSTEMTIME d1, SYSTEMTIME d2) => d1.wYear != d2.wYear && d1.wMonth != d2.wMonth && d1.wDayOfWeek != d2.wDayOfWeek && d1.wDay != d2.wDay && d1.wHour != d2.wHour && d1.wMinute != d2.wMinute && d1.wSecond != d2.wSecond && d1.wMilliseconds != d2.wMilliseconds;

	public static explicit operator global::System.DateTime(SYSTEMTIME sysTime)
	{
		if (sysTime == default(SYSTEMTIME) || sysTime.wYear <= 0 || sysTime.wMonth <= 0 || sysTime.wDay <= 0)
		{
			return default;
		}

		// DateTime gets DayOfWeek automatically
		return new global::System.DateTime(sysTime.wYear,
			sysTime.wMonth, sysTime.wDay, sysTime.wHour,
			sysTime.wMinute, sysTime.wSecond, sysTime.wMilliseconds);
	}

	public static explicit operator SYSTEMTIME(global::System.DateTime time)
	{
		if (time == default)
		{
			return default;
		}

		return new SYSTEMTIME
		{
			wYear = checked((ushort)time.Year),
			wMonth = checked((ushort)time.Month),
			wDayOfWeek = checked((ushort)time.DayOfWeek),
			wDay = checked((ushort)time.Day),
			wHour = checked((ushort)time.Hour),
			wMinute = checked((ushort)time.Minute),
			wSecond = checked((ushort)time.Second),
			wMilliseconds = checked((ushort)time.Millisecond)
		};
	}
}
