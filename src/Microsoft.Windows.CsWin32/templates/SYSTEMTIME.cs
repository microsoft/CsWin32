partial struct SYSTEMTIME
	: IEquatable<SYSTEMTIME>
{
	public bool Equals(SYSTEMTIME other) => this.wYear == other.wYear && this.wMonth == other.wMonth && this.wDayOfWeek == other.wDayOfWeek && this.wDay == other.wDay && this.wHour == other.wHour && this.wMinute == other.wMinute && this.wSecond == other.wSecond && this.wMilliseconds == other.wMilliseconds;
	public override bool Equals(object obj) => obj is SYSTEMTIME other && this.Equals(other);
	public override int GetHashCode() => (this.wYear, this.wMonth, this.wDayOfWeek, this.wDay, this.wHour, this.wMinute, this.wSecond, this.wMilliseconds).GetHashCode();
	public static bool operator ==(SYSTEMTIME d1, SYSTEMTIME d2) => d1.Equals(d2);
	public static bool operator !=(SYSTEMTIME d1, SYSTEMTIME d2) => !(d1 == d2);

	public static explicit operator global::System.DateTime(SYSTEMTIME sysTime)
	{
		if (sysTime == default)
		{
			return default;
		}

		return new global::System.DateTime(sysTime.wYear, sysTime.wMonth, sysTime.wDay, sysTime.wHour, sysTime.wMinute, sysTime.wSecond, sysTime.wMilliseconds);
	}

	public static explicit operator SYSTEMTIME(global::System.DateTime time)
	{
		if (time == default)
		{
			return default;
		}

		checked
		{
			return new SYSTEMTIME
			{
				wYear = (ushort)time.Year,
				wMonth = (ushort)time.Month,
				wDayOfWeek = (ushort)time.DayOfWeek,
				wDay = (ushort)time.Day,
				wHour = (ushort)time.Hour,
				wMinute = (ushort)time.Minute,
				wSecond = (ushort)time.Second,
				wMilliseconds = (ushort)time.Millisecond,
			};
		}
	}
}
